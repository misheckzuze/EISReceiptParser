using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;
using FiscalReceiptParser.Data;
using FiscalReceiptParser.Services;

namespace FiscalReceiptParser
{
    public partial class Form1 : Form
    {
        // ===== Brand palette (matches ActivationForm) =====
        private static readonly Color BrandDark = Color.FromArgb(21, 42, 45);
        private static readonly Color BrandAccent = Color.FromArgb(0, 150, 110);
        private static readonly Color BrandAccentHover = Color.FromArgb(0, 128, 94);
        private static readonly Color BodyBg = Color.FromArgb(244, 246, 246);
        private static readonly Color CardBg = Color.White;
        private static readonly Color TextMuted = Color.FromArgb(110, 118, 122);
        private static readonly Color TextDark = Color.FromArgb(50, 55, 57);
        private static readonly Color BorderColor = Color.FromArgb(214, 219, 220);
        private static readonly Color ErrorColor = Color.FromArgb(196, 60, 60);
        private static readonly Color SuccessColor = Color.FromArgb(30, 140, 90);
        private static readonly Color WarningColor = Color.FromArgb(196, 130, 30);
        private static readonly Color InfoColor = Color.FromArgb(40, 110, 190);

        // ===== Folders, sourced from the AppSettings table (see Database.Settings.cs) =====
        private string? _watchFolder;
        private string? _failedFolder;
        private string? _outputFolder;

        private FileSystemWatcher? _watcher;
        private readonly ConcurrentDictionary<string, byte> _inFlight = new(StringComparer.OrdinalIgnoreCase);
        private bool _monitoring;

        // ===== UI =====
        private Panel _headerBg = null!;
        private Label _lblMonitoringDot = null!;
        private Label _lblMonitoringStatus = null!;
        private Label _lblFolderPath = null!;
        private Button _btnOpenSettings = null!;
        private Button _btnToggleMonitoring = null!;
        private Button _btnDownloadInventory = null!;

        private StatCard _cardFiscalised = null!;
        private StatCard _cardOnline = null!;
        private StatCard _cardOffline = null!;
        private StatCard _cardPending = null!;

        private ListView _activityLog = null!;
        private Label _lblActivityHeader = null!;

        public Form1()
        {
            InitializeComponent();
            BuildUi();
            LoadFolderSettingsFromDb();
            RefreshCounters();

            if (FoldersConfigured() && Directory.Exists(_watchFolder))
            {
                StartMonitoring();
            }
            else
            {
                SetMonitoringStatus(false, "No folders configured — open Settings");
            }
        }

        private bool FoldersConfigured() =>
            !string.IsNullOrWhiteSpace(_watchFolder) &&
            !string.IsNullOrWhiteSpace(_failedFolder) &&
            !string.IsNullOrWhiteSpace(_outputFolder);

        // ============================================================
        // UI CONSTRUCTION
        // ============================================================
        private void BuildUi()
        {
            Text = "Fiscal Receipt Dashboard";
            AutoScaleMode = AutoScaleMode.None;
            AutoScaleDimensions = new SizeF(96F, 96F);
            ClientSize = new Size(1040, 720);
            MinimumSize = new Size(900, 620);
            BackColor = BodyBg;
            Font = new Font("Segoe UI", 9.5f);
            StartPosition = FormStartPosition.CenterScreen;

            int marginX = 32;

            // ---------- HEADER ----------
            _headerBg = new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(ClientSize.Width, 120),
                BackColor = BrandDark,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            var lblTitle = new Label
            {
                Text = "Fiscal Receipt Dashboard",
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 17f, FontStyle.Bold),
                Location = new Point(marginX, 18),
                AutoSize = true
            };

            var lblSubtitle = new Label
            {
                Text = "Malawi Revenue Authority · Automatic EIS Fiscalisation",
                ForeColor = Color.FromArgb(180, 195, 192),
                Font = new Font("Segoe UI", 10f),
                Location = new Point(marginX, 50),
                AutoSize = true
            };

            _lblMonitoringDot = new Label
            {
                Text = "●",
                Font = new Font("Segoe UI", 12f),
                ForeColor = ErrorColor,
                Location = new Point(marginX, 82),
                AutoSize = true
            };

            _lblMonitoringStatus = new Label
            {
                Text = "Monitoring: OFF",
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                Location = new Point(marginX + 20, 82),
                AutoSize = true
            };

            _lblFolderPath = new Label
            {
                Text = "No folder selected",
                ForeColor = Color.FromArgb(180, 195, 192),
                Font = new Font("Segoe UI", 9f),
                Location = new Point(marginX + 190, 84),
                AutoSize = true,
                MaximumSize = new Size(420, 0)
            };

            _btnOpenSettings = MakeHeaderButton("Settings", ClientSize.Width - marginX - 260);
            _btnOpenSettings.Top = 76;
            _btnOpenSettings.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _btnOpenSettings.Click += BtnOpenSettings_Click;

            _btnToggleMonitoring = MakeHeaderButton("Stop Monitoring", ClientSize.Width - marginX - 120);
            _btnToggleMonitoring.Top = 76;
            _btnToggleMonitoring.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _btnToggleMonitoring.Click += BtnToggleMonitoring_Click;

            _headerBg.Controls.Add(lblTitle);
            _headerBg.Controls.Add(lblSubtitle);
            _headerBg.Controls.Add(_lblMonitoringDot);
            _headerBg.Controls.Add(_lblMonitoringStatus);
            _headerBg.Controls.Add(_lblFolderPath);
            _headerBg.Controls.Add(_btnOpenSettings);
            _headerBg.Controls.Add(_btnToggleMonitoring);

            // ---------- STAT CARDS ----------
            var cardsPanel = new TableLayoutPanel
            {
                Location = new Point(marginX, _headerBg.Bottom + 24),
                Size = new Size(ClientSize.Width - marginX * 2, 130),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                ColumnCount = 4,
                RowCount = 1,
                BackColor = Color.Transparent
            };
            for (int i = 0; i < 4; i++)
                cardsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));

            _cardFiscalised = new StatCard("Fiscalised Sales", BrandAccent);
            _cardOnline = new StatCard("Online Sales", InfoColor);
            _cardOffline = new StatCard("Offline Sales", WarningColor);
            _cardPending = new StatCard("Pending Push", ErrorColor);

            cardsPanel.Controls.Add(Pad(_cardFiscalised), 0, 0);
            cardsPanel.Controls.Add(Pad(_cardOnline), 1, 0);
            cardsPanel.Controls.Add(Pad(_cardOffline), 2, 0);
            cardsPanel.Controls.Add(Pad(_cardPending), 3, 0);

            // ---------- ACTION ROW ----------
            _btnDownloadInventory = new Button
            {
                Text = "Download Inventory",
                Location = new Point(marginX, cardsPanel.Bottom + 20),
                Size = new Size(190, 42),
                FlatStyle = FlatStyle.Flat,
                BackColor = BrandAccent,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            _btnDownloadInventory.FlatAppearance.BorderSize = 0;
            _btnDownloadInventory.MouseEnter += (s, e) => _btnDownloadInventory.BackColor = BrandAccentHover;
            _btnDownloadInventory.MouseLeave += (s, e) => _btnDownloadInventory.BackColor = BrandAccent;
            _btnDownloadInventory.Click += BtnDownloadInventory_Click;

            // ---------- ACTIVITY LOG ----------
            _lblActivityHeader = new Label
            {
                Text = "Recent Activity",
                ForeColor = TextDark,
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                Location = new Point(marginX, _btnDownloadInventory.Bottom + 22),
                AutoSize = true
            };

            _activityLog = new ListView
            {
                Location = new Point(marginX, _lblActivityHeader.Bottom + 8),
                Size = new Size(ClientSize.Width - marginX * 2, ClientSize.Height - _lblActivityHeader.Bottom - 8 - 24),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
                View = View.Details,
                FullRowSelect = true,
                GridLines = false,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                BackColor = CardBg,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 9.5f)
            };
            _activityLog.Columns.Add("Time", 130);
            _activityLog.Columns.Add("File", 260);
            _activityLog.Columns.Add("Status", 140);
            _activityLog.Columns.Add("Details", 400);

            Controls.Add(_headerBg);
            Controls.Add(cardsPanel);
            Controls.Add(_btnDownloadInventory);
            Controls.Add(_lblActivityHeader);
            Controls.Add(_activityLog);
        }

        private static Panel Pad(Control inner)
        {
            var wrapper = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 0, 16, 0) };
            inner.Dock = DockStyle.Fill;
            wrapper.Controls.Add(inner);
            return wrapper;
        }

        private Button MakeHeaderButton(string text, int left)
        {
            var btn = new Button
            {
                Text = text,
                Left = left,
                Width = 140,
                Height = 32,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(35, 60, 63),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderColor = Color.FromArgb(60, 85, 88);
            btn.FlatAppearance.BorderSize = 1;
            return btn;
        }

        // ============================================================
        // KPI card control
        // ============================================================
        private sealed class StatCard : Panel
        {
            private readonly Label _lblValue;
            public StatCard(string title, Color accent)
            {
                BackColor = BorderColor;
                Padding = new Padding(1);

                var inner = new Panel { Dock = DockStyle.Fill, BackColor = CardBg };
                var accentBar = new Panel { Dock = DockStyle.Top, Height = 4, BackColor = accent };

                var lblTitle = new Label
                {
                    Text = title,
                    ForeColor = TextMuted,
                    Font = new Font("Segoe UI", 9.5f),
                    Location = new Point(16, 20),
                    AutoSize = true
                };

                _lblValue = new Label
                {
                    Text = "0",
                    ForeColor = TextDark,
                    Font = new Font("Segoe UI", 24f, FontStyle.Bold),
                    Location = new Point(14, 42),
                    AutoSize = true
                };

                inner.Controls.Add(accentBar);
                inner.Controls.Add(lblTitle);
                inner.Controls.Add(_lblValue);
                Controls.Add(inner);
            }

            public void SetValue(int value) => _lblValue.Text = value.ToString();
        }

        // ============================================================
        // SETTINGS (backed by the AppSettings table — see Database.Settings.cs)
        // ============================================================
        private void LoadFolderSettingsFromDb()
        {
            var settings = Database.GetFolderSettings();
            _watchFolder = string.IsNullOrWhiteSpace(settings.WatchFolder) ? null : settings.WatchFolder;
            _failedFolder = string.IsNullOrWhiteSpace(settings.FailedFolder) ? null : settings.FailedFolder;
            _outputFolder = string.IsNullOrWhiteSpace(settings.OutputFolder) ? null : settings.OutputFolder;
            _lblFolderPath.Text = _watchFolder ?? "No folder selected";
        }

        private void BtnOpenSettings_Click(object? sender, EventArgs e)
        {
            using var form = new SettingsForm();
            if (form.ShowDialog(this) == DialogResult.OK)
            {
                StopMonitoring();
                LoadFolderSettingsFromDb();
                RefreshCounters();
                StartMonitoring();
            }
        }

        // ============================================================
        // MONITORING — a FileSystemWatcher acting like a background service
        // ============================================================
        private void BtnToggleMonitoring_Click(object? sender, EventArgs e)
        {
            if (_monitoring)
                StopMonitoring();
            else
                StartMonitoring();
        }

        private void StartMonitoring()
        {
            if (!FoldersConfigured() || !Directory.Exists(_watchFolder))
            {
                MessageBox.Show(this, "Please configure the watch, failed, and output folders in Settings first.",
                    "Folders not configured", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                BtnOpenSettings_Click(null, EventArgs.Empty);
                return;
            }

            Directory.CreateDirectory(_failedFolder!);
            Directory.CreateDirectory(_outputFolder!);

            _watcher?.Dispose();
            _watcher = new FileSystemWatcher(_watchFolder!, "*.pdf")
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime
            };
            _watcher.Created += (s, e) => _ = QueueFileAsync(e.FullPath);
            _watcher.Renamed += (s, e) => _ = QueueFileAsync(e.FullPath);
            _watcher.EnableRaisingEvents = true;

            // Pick up anything already sitting in the folder when monitoring starts.
            foreach (var existing in Directory.GetFiles(_watchFolder!, "*.pdf"))
                _ = QueueFileAsync(existing);

            _monitoring = true;
            SetMonitoringStatus(true, _watchFolder!);
        }

        private void StopMonitoring()
        {
            _watcher?.Dispose();
            _watcher = null;
            _monitoring = false;
            SetMonitoringStatus(false, _watchFolder ?? "No folder selected");
        }

        private void SetMonitoringStatus(bool on, string folderText)
        {
            void Apply()
            {
                _lblMonitoringDot.ForeColor = on ? SuccessColor : ErrorColor;
                _lblMonitoringStatus.Text = on ? "Monitoring: ON" : "Monitoring: OFF";
                _lblFolderPath.Text = folderText;
                _btnToggleMonitoring.Text = on ? "Stop Monitoring" : "Start Monitoring";
            }

            if (InvokeRequired) Invoke((Action)Apply); else Apply();
        }

        private async Task QueueFileAsync(string path)
        {
            if (!_inFlight.TryAdd(path, 0)) return; // already being processed, skip duplicate event

            try
            {
                // Give the writer (Tally / print driver) time to finish producing the PDF.
                await Task.Delay(1200);
                if (!await WaitForFileReadyAsync(path))
                {
                    LogActivity(Path.GetFileName(path), "Skipped", "File never became readable", WarningColor);
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

        // ============================================================
        // CORE PIPELINE
        // If the PDF can't be read/parsed/submitted at all -> Failed folder.
        // If it's successfully fiscalised (online or offline) -> Output folder.
        // ============================================================
        private async Task ProcessFileAsync(string path)
        {
            var fileName = Path.GetFileName(path);

            try
            {
                var lines = PdfTextExtractor.ExtractLines(path);
                var invoice = ReceiptParser.Parse(lines);

                var token = ConfigHelper.GetToken();
                if (string.IsNullOrEmpty(token))
                {
                    MoveTo(path, _failedFolder!);
                    LogActivity(fileName, "Not activated", "Terminal has no token — activate first", ErrorColor);
                    return;
                }

                var result = await TransactionService.SubmitParsedInvoiceAsync(invoice, token);

                if (result.Success)
                {
                    MoveTo(path, _outputFolder!);
                    LogActivity(fileName, "Fiscalised (online)", $"Invoice {result.InvoiceNumber}", SuccessColor);
                }
                else if (result.IsOffline)
                {
                    MoveTo(path, _outputFolder!);
                    LogActivity(fileName, "Fiscalised (offline)", $"Invoice {result.InvoiceNumber} — queued for retry", WarningColor);
                }
                else
                {
                    MoveTo(path, _failedFolder!);
                    LogActivity(fileName, "Rejected", "MRA rejected the submission", ErrorColor);
                }
            }
            catch (Exception ex)
            {
                MoveTo(path, _failedFolder!);
                LogActivity(fileName, "Failed to read", ex.Message, ErrorColor);
            }
            finally
            {
                RefreshCounters();
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
            catch
            {
                // Best-effort — if the move fails the file stays put and will be re-picked-up.
            }
        }

        // ============================================================
        // DOWNLOAD INVENTORY
        // ============================================================
        private async void BtnDownloadInventory_Click(object? sender, EventArgs e)
        {
            var tin = ConfigHelper.GetTin();
            var siteId = ConfigHelper.GetTerminalSiteId();
            var token = ConfigHelper.GetToken();

            if (string.IsNullOrEmpty(tin) || string.IsNullOrEmpty(siteId) || string.IsNullOrEmpty(token))
            {
                MessageBox.Show(this, "This terminal isn't fully activated yet.", "Not activated",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _btnDownloadInventory.Enabled = false;
            var originalText = _btnDownloadInventory.Text;
            _btnDownloadInventory.Text = "Downloading…";

            try
            {
                using var httpClient = new HttpClient();
                var service = new MraApiService(httpClient);
                var success = await service.GetTerminalSiteProductsAsync(tin, siteId, token);

                LogActivity("Inventory", success ? "Downloaded" : "Failed",
                    success ? "Product catalogue refreshed from MRA" : "Could not download product catalogue",
                    success ? SuccessColor : ErrorColor);
            }
            catch (Exception ex)
            {
                LogActivity("Inventory", "Error", ex.Message, ErrorColor);
            }
            finally
            {
                _btnDownloadInventory.Enabled = true;
                _btnDownloadInventory.Text = originalText;
            }
        }

        // ============================================================
        // UI UPDATES (always marshalled onto the UI thread)
        // Stats are read live from the Invoices table (InvoiceRepository),
        // so they're accurate even after an app restart.
        // ============================================================
        private void RefreshCounters()
        {
            var fiscalised = InvoiceRepository.GetFiscalisedCount();
            var online = InvoiceRepository.GetOnlineCount();
            var offline = InvoiceRepository.GetOfflineCount();
            var pending = InvoiceRepository.GetPendingCount();

            void Apply()
            {
                _cardFiscalised.SetValue(fiscalised);
                _cardOnline.SetValue(online);
                _cardOffline.SetValue(offline);
                _cardPending.SetValue(pending);
            }

            if (InvokeRequired) Invoke((Action)Apply); else Apply();
        }

        private void LogActivity(string fileName, string status, string details, Color color)
        {
            void Apply()
            {
                var item = new ListViewItem(DateTime.Now.ToString("HH:mm:ss"));
                item.SubItems.Add(fileName);
                item.SubItems.Add(status);
                item.SubItems.Add(details);
                item.ForeColor = color;
                _activityLog.Items.Insert(0, item);

                while (_activityLog.Items.Count > 200)
                    _activityLog.Items.RemoveAt(_activityLog.Items.Count - 1);
            }

            if (InvokeRequired) Invoke((Action)Apply); else Apply();
        }

        private void Form1_Load_1(object sender, EventArgs e)
        {
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _watcher?.Dispose();
            base.OnFormClosed(e);
        }
    }
}