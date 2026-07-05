using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FiscalReceiptParser.Data;

namespace FiscalReceiptParser.Services
{
    /// <summary>
    /// Watches the Palladium PDF export folder and fiscalises every invoice PDF that
    /// lands in it — the same pipeline that used to live inside Form1's
    /// FileSystemWatcher, now running inside the Windows Service so it keeps working
    /// even when nobody is logged into the RDS session and the dashboard isn't open.
    /// </summary>
    public sealed class PdfFolderWatcher : IDisposable
    {
        private const int SettingsRefreshMs = 60_000; // re-check folder settings every 60s

        private readonly ConcurrentDictionary<string, byte> _inFlight =
            new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);

        private readonly object _watcherLock = new object();

        private FileSystemWatcher _watcher;
        private System.Threading.Timer _settingsRefreshTimer;

        private string _watchFolder;
        private string _failedFolder;
        private string _outputFolder;

        public void Start()
        {
            ApplyFolderSettings(logIfUnchanged: true);

            // Folder paths can be changed from the WinForms Settings screen while the
            // service keeps running (e.g. someone repoints the watch folder for a new
            // RDS user). Poll for changes instead of requiring a service restart.
            _settingsRefreshTimer = new System.Threading.Timer(
                _ => ApplyFolderSettings(logIfUnchanged: false),
                null, SettingsRefreshMs, SettingsRefreshMs);
        }

        public void Stop()
        {
            _settingsRefreshTimer?.Dispose();
            _settingsRefreshTimer = null;

            lock (_watcherLock)
            {
                _watcher?.Dispose();
                _watcher = null;
            }
        }

        public void Dispose() => Stop();

        // ============================================================
        // FOLDER SETTINGS / WATCHER LIFECYCLE
        // ============================================================
        private void ApplyFolderSettings(bool logIfUnchanged)
        {
            try
            {
                var settings = Database.GetFolderSettings();
                string watchFolder = string.IsNullOrWhiteSpace(settings.WatchFolder) ? null : settings.WatchFolder;
                string failedFolder = string.IsNullOrWhiteSpace(settings.FailedFolder) ? null : settings.FailedFolder;
                string outputFolder = string.IsNullOrWhiteSpace(settings.OutputFolder) ? null : settings.OutputFolder;

                bool changed =
                    !string.Equals(watchFolder, _watchFolder, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(failedFolder, _failedFolder, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(outputFolder, _outputFolder, StringComparison.OrdinalIgnoreCase);

                if (!changed)
                {
                    if (logIfUnchanged)
                        EisServiceLog.Info($"PDF watcher: folders unchanged. Watching '{watchFolder ?? "(none configured)"}'.");
                    return;
                }

                _watchFolder = watchFolder;
                _failedFolder = failedFolder;
                _outputFolder = outputFolder;

                RestartWatcher();
            }
            catch (Exception ex)
            {
                EisServiceLog.Error($"PDF watcher: failed to read folder settings: {ex.Message}");
            }
        }

        private void RestartWatcher()
        {
            lock (_watcherLock)
            {
                _watcher?.Dispose();
                _watcher = null;

                if (string.IsNullOrWhiteSpace(_watchFolder) ||
                    string.IsNullOrWhiteSpace(_failedFolder) ||
                    string.IsNullOrWhiteSpace(_outputFolder))
                {
                    EisServiceLog.Info("PDF watcher: folders not fully configured yet — open Settings once to set them. Waiting...");
                    return;
                }

                if (!Directory.Exists(_watchFolder))
                {
                    EisServiceLog.Error($"PDF watcher: watch folder '{_watchFolder}' does not exist. Waiting for it to appear...");
                    return;
                }

                Directory.CreateDirectory(_failedFolder);
                Directory.CreateDirectory(_outputFolder);

                _watcher = new FileSystemWatcher(_watchFolder, "*.pdf")
                {
                    IncludeSubdirectories = false,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime
                };
                _watcher.Created += (s, e) => _ = QueueFileAsync(e.FullPath);
                _watcher.Renamed += (s, e) => _ = QueueFileAsync(e.FullPath);
                _watcher.Error += (s, e) => EisServiceLog.Error($"PDF watcher error: {e.GetException()?.Message}");
                _watcher.EnableRaisingEvents = true;

                EisServiceLog.Info($"PDF watcher: now watching '{_watchFolder}' for invoices.");

                // Pick up anything that was dropped while the service was stopped/starting.
                foreach (var existing in Directory.GetFiles(_watchFolder, "*.pdf"))
                    _ = QueueFileAsync(existing);
            }
        }

        // ============================================================
        // PIPELINE — same logic that used to live in Form1
        // ============================================================
        private async Task QueueFileAsync(string path)
        {
            if (!_inFlight.TryAdd(path, 0)) return; // duplicate event for the same file — skip

            try
            {
                await Task.Delay(1200); // give Palladium's print driver time to finish writing the PDF
                if (!await WaitForFileReadyAsync(path))
                {
                    EisServiceLog.Error($"PDF watcher: '{Path.GetFileName(path)}' never became readable — skipped.");
                    return;
                }

                await ProcessFileAsync(path);
            }
            finally
            {
                _inFlight.TryRemove(path, out _);
            }
        }

        private static async Task<bool> WaitForFileReadyAsync(string path, int attempts = 10, int delayMs = 500)
        {
            for (int i = 0; i < attempts; i++)
            {
                try
                {
                    if (!File.Exists(path)) return false;
                    using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None);
                    return true;
                }
                catch (IOException)
                {
                    await Task.Delay(delayMs);
                }
            }
            return false;
        }

        private async Task ProcessFileAsync(string path)
        {
            var fileName = Path.GetFileName(path);
            string failedFolder = _failedFolder;
            string outputFolder = _outputFolder;

            try
            {
                var lines = PdfTextExtractor.ExtractLines(path);
                var invoice = ReceiptParser.Parse(lines);

                var token = ConfigHelper.GetToken();
                if (string.IsNullOrEmpty(token))
                {
                    MoveTo(path, failedFolder);
                    EisServiceLog.Error($"PDF watcher: '{fileName}' — terminal has no token, activate first.");
                    return;
                }

                var result = await TransactionService.SubmitParsedInvoiceAsync(invoice, token);

                if (result.Success)
                {
                    MoveTo(path, outputFolder);
                    EisServiceLog.Info($"PDF watcher: '{fileName}' fiscalised online — invoice {result.InvoiceNumber}.");
                }
                else if (result.IsOffline)
                {
                    MoveTo(path, outputFolder);
                    EisServiceLog.Info($"PDF watcher: '{fileName}' fiscalised offline — invoice {result.InvoiceNumber}, queued for retry.");
                }
                else
                {
                    MoveTo(path, failedFolder);
                    EisServiceLog.Error($"PDF watcher: '{fileName}' rejected by MRA.");
                }
            }
            catch (Exception ex)
            {
                MoveTo(path, failedFolder);
                EisServiceLog.Error($"PDF watcher: '{fileName}' failed to read/process: {ex.Message}");
            }
        }

        private static void MoveTo(string path, string destinationFolder)
        {
            try
            {
                Directory.CreateDirectory(destinationFolder);

                var currentDir = Path.GetDirectoryName(path);
                if (string.Equals(currentDir, destinationFolder.TrimEnd(Path.DirectorySeparatorChar),
                        StringComparison.OrdinalIgnoreCase))
                    return; // already there

                var target = Path.Combine(destinationFolder, Path.GetFileName(path));
                if (File.Exists(target))
                    target = Path.Combine(destinationFolder,
                        $"{Path.GetFileNameWithoutExtension(path)}_{DateTime.Now:HHmmss}{Path.GetExtension(path)}");

                File.Move(path, target);
            }
            catch (Exception ex)
            {
                EisServiceLog.Error($"PDF watcher: could not move '{Path.GetFileName(path)}' to '{destinationFolder}': {ex.Message}");
            }
        }
    }
}