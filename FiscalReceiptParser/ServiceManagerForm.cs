using System;
using System.IO;
using System.ServiceProcess;
using System.Drawing;
using System.Windows.Forms;
using FiscalReceiptParser.Services;

namespace FiscalReceiptParser
{
    /// <summary>
    /// Lets an admin install/uninstall/start/stop/restart the
    /// EISFiscalizationService without touching services.msc or a command line.
    /// All actual work is delegated to EisServiceController.
    /// </summary>
    public partial class ServiceManagerForm : Form
    {
        // ===== Brand palette (matches Form1) =====
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

        // Remembers the last-picked service exe path between app restarts.
        private static readonly string PathConfigFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "POSSetup", "service-exe-path.txt");

        private TextBox _txtExePath = null!;
        private Button _btnBrowse = null!;
        private Button _btnInstall = null!;
        private Button _btnUninstall = null!;
        private Button _btnStart = null!;
        private Button _btnStop = null!;
        private Button _btnRestart = null!;
        private Button _btnRefresh = null!;
        private Label _lblStatusDot = null!;
        private Label _lblStatusText = null!;
        private System.Windows.Forms.Timer _statusTimer = null!;

        public ServiceManagerForm()
        {
            BuildUi();
            LoadSavedExePath();
            RefreshStatus();

            _statusTimer = new System.Windows.Forms.Timer { Interval = 3000 };
            _statusTimer.Tick += (s, e) => RefreshStatus();
            _statusTimer.Start();
        }

        // ============================================================
        // UI CONSTRUCTION
        // ============================================================
        private void BuildUi()
        {
            Text = "Manage Fiscalisation Service";
            ClientSize = new Size(560, 420);
            MinimumSize = new Size(560, 420);
            MaximizeBox = false;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = BodyBg;
            Font = new Font("Segoe UI", 9.5f);

            int marginX = 24;

            // ---------- HEADER ----------
            var header = new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(ClientSize.Width, 76),
                BackColor = BrandDark
            };
            var lblTitle = new Label
            {
                Text = "Fiscalisation Service",
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 14f, FontStyle.Bold),
                Location = new Point(marginX, 14),
                AutoSize = true
            };
            var lblSubtitle = new Label
            {
                Text = EisServiceConstants.SERVICE_NAME,
                ForeColor = Color.FromArgb(180, 195, 192),
                Font = new Font("Segoe UI", 9f),
                Location = new Point(marginX, 44),
                AutoSize = true
            };
            header.Controls.Add(lblTitle);
            header.Controls.Add(lblSubtitle);

            // ---------- STATUS CARD ----------
            var statusCard = new Panel
            {
                Location = new Point(marginX, header.Bottom + 20),
                Size = new Size(ClientSize.Width - marginX * 2, 60),
                BackColor = CardBg,
                BorderStyle = BorderStyle.FixedSingle
            };
            _lblStatusDot = new Label
            {
                Text = "●",
                Font = new Font("Segoe UI", 14f),
                ForeColor = ErrorColor,
                Location = new Point(16, 18),
                AutoSize = true
            };
            _lblStatusText = new Label
            {
                Text = "Checking status…",
                ForeColor = TextDark,
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                Location = new Point(40, 18),
                AutoSize = true
            };
            _btnRefresh = new Button
            {
                Text = "Refresh",
                Size = new Size(90, 30),
                Location = new Point(statusCard.Width - 106, 15),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(235, 237, 237),
                ForeColor = TextDark,
                Cursor = Cursors.Hand
            };
            _btnRefresh.FlatAppearance.BorderColor = BorderColor;
            _btnRefresh.Click += (s, e) => RefreshStatus();

            statusCard.Controls.Add(_lblStatusDot);
            statusCard.Controls.Add(_lblStatusText);
            statusCard.Controls.Add(_btnRefresh);

            // ---------- EXE PATH ----------
            var lblExePath = new Label
            {
                Text = "Service executable (.exe)",
                ForeColor = TextMuted,
                Font = new Font("Segoe UI", 9f),
                Location = new Point(marginX, statusCard.Bottom + 18),
                AutoSize = true
            };

            _txtExePath = new TextBox
            {
                Location = new Point(marginX, lblExePath.Bottom + 4),
                Size = new Size(ClientSize.Width - marginX * 2 - 96, 28),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Font = new Font("Segoe UI", 9.5f)
            };

            _btnBrowse = new Button
            {
                Text = "Browse…",
                Size = new Size(86, 28),
                Location = new Point(_txtExePath.Right + 10, _txtExePath.Top),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(235, 237, 237),
                ForeColor = TextDark,
                Cursor = Cursors.Hand
            };
            _btnBrowse.FlatAppearance.BorderColor = BorderColor;
            _btnBrowse.Click += BtnBrowse_Click;

            // ---------- ACTION BUTTONS ----------
            int btnTop = _txtExePath.Bottom + 24;
            int btnWidth = (ClientSize.Width - marginX * 2 - 10 * 2) / 3;

            _btnInstall = MakeActionButton("Install", BrandAccent, marginX, btnTop, btnWidth);
            _btnInstall.Click += BtnInstall_Click;

            _btnUninstall = MakeActionButton("Uninstall", ErrorColor, _btnInstall.Right + 10, btnTop, btnWidth);
            _btnUninstall.Click += BtnUninstall_Click;

            _btnRestart = MakeActionButton("Restart", WarningColor, _btnUninstall.Right + 10, btnTop, btnWidth);
            _btnRestart.Click += BtnRestart_Click;

            int btnTop2 = btnTop + 46;
            int wideWidth = (ClientSize.Width - marginX * 2 - 10) / 2;

            _btnStart = MakeActionButton("Start", BrandAccent, marginX, btnTop2, wideWidth);
            _btnStart.Click += BtnStart_Click;

            _btnStop = MakeActionButton("Stop", Color.FromArgb(90, 96, 99), _btnStart.Right + 10, btnTop2, wideWidth);
            _btnStop.Click += BtnStop_Click;

            var lblHint = new Label
            {
                Text = "Install/Uninstall/Start/Stop/Restart require administrator rights — " +
                       "Windows will prompt for elevation (UAC) for each action.",
                ForeColor = TextMuted,
                Font = new Font("Segoe UI", 8.5f),
                Location = new Point(marginX, btnTop2 + 50),
                Size = new Size(ClientSize.Width - marginX * 2, 40),
                AutoSize = false
            };

            Controls.Add(header);
            Controls.Add(statusCard);
            Controls.Add(lblExePath);
            Controls.Add(_txtExePath);
            Controls.Add(_btnBrowse);
            Controls.Add(_btnInstall);
            Controls.Add(_btnUninstall);
            Controls.Add(_btnRestart);
            Controls.Add(_btnStart);
            Controls.Add(_btnStop);
            Controls.Add(lblHint);
        }

        private Button MakeActionButton(string text, Color color, int x, int y, int width)
        {
            var btn = new Button
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(width, 38),
                FlatStyle = FlatStyle.Flat,
                BackColor = color,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderSize = 0;
            return btn;
        }

        // ============================================================
        // EXE PATH PERSISTENCE
        // ============================================================
        private void LoadSavedExePath()
        {
            try
            {
                if (File.Exists(PathConfigFile))
                    _txtExePath.Text = File.ReadAllText(PathConfigFile).Trim();
            }
            catch { /* best effort */ }
        }

        private void SaveExePath(string path)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(PathConfigFile)!);
                File.WriteAllText(PathConfigFile, path);
            }
            catch { /* best effort */ }
        }

        private void BtnBrowse_Click(object? sender, EventArgs e)
        {
            using var dlg = new OpenFileDialog
            {
                Title = "Select the compiled service executable",
                Filter = "Executable (*.exe)|*.exe",
                CheckFileExists = true
            };

            if (dlg.ShowDialog(this) == DialogResult.OK)
                _txtExePath.Text = dlg.FileName;
        }

        // ============================================================
        // ACTIONS
        // ============================================================
        private void BtnInstall_Click(object? sender, EventArgs e)
        {
            var path = _txtExePath.Text.Trim();
            if (string.IsNullOrWhiteSpace(path))
            {
                MessageBox.Show(this, "Choose the service .exe first.", "No executable selected",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            RunAction(() =>
            {
                EisServiceController.InstallService(path);
                SaveExePath(path);
            }, "Service installed.");
        }

        private void BtnUninstall_Click(object? sender, EventArgs e)
        {
            var confirm = MessageBox.Show(this,
                "This will stop and remove the service. Continue?",
                "Confirm uninstall", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (confirm != DialogResult.Yes) return;

            RunAction(() => EisServiceController.UninstallService(), "Service uninstalled.");
        }

        private void BtnStart_Click(object? sender, EventArgs e) =>
            RunAction(() => EisServiceController.StartService(), "Service started.");

        private void BtnStop_Click(object? sender, EventArgs e) =>
            RunAction(() => EisServiceController.StopService(), "Service stopped.");

        private void BtnRestart_Click(object? sender, EventArgs e) =>
            RunAction(() => EisServiceController.RestartService(), "Service restarted.");

        private void RunAction(Action action, string successMessage)
        {
            SetButtonsEnabled(false);
            try
            {
                action();
                RefreshStatus();
                MessageBox.Show(this, successMessage, "Done",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    $"That didn't work: {ex.Message}\n\n" +
                    "If this mentions access being denied, re-run this app as Administrator.",
                    "Service action failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                SetButtonsEnabled(true);
                RefreshStatus();
            }
        }

        private void SetButtonsEnabled(bool enabled)
        {
            _btnInstall.Enabled = enabled;
            _btnUninstall.Enabled = enabled;
            _btnStart.Enabled = enabled;
            _btnStop.Enabled = enabled;
            _btnRestart.Enabled = enabled;
            _btnBrowse.Enabled = enabled;
        }

        // ============================================================
        // STATUS
        // ============================================================
        private void RefreshStatus()
        {
            bool installed = EisServiceController.IsInstalled();
            var status = installed ? EisServiceController.GetStatus() : (ServiceControllerStatus?)null;

            string text;
            Color color;

            if (!installed)
            {
                text = "Not installed";
                color = TextMuted;
            }
            else
            {
                switch (status)
                {
                    case ServiceControllerStatus.Running:
                        text = "Running";
                        color = SuccessColor;
                        break;
                    case ServiceControllerStatus.Stopped:
                        text = "Stopped";
                        color = ErrorColor;
                        break;
                    case ServiceControllerStatus.StartPending:
                        text = "Starting…";
                        color = WarningColor;
                        break;
                    case ServiceControllerStatus.StopPending:
                        text = "Stopping…";
                        color = WarningColor;
                        break;
                    default:
                        text = status?.ToString() ?? "Unknown";
                        color = WarningColor;
                        break;
                }
            }

            _lblStatusDot.ForeColor = color;
            _lblStatusText.Text = text;
            _lblStatusText.ForeColor = TextDark;

            _btnInstall.Enabled = !installed;
            _btnUninstall.Enabled = installed;
            _btnStart.Enabled = installed && status != ServiceControllerStatus.Running;
            _btnStop.Enabled = installed && status == ServiceControllerStatus.Running;
            _btnRestart.Enabled = installed;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _statusTimer?.Stop();
            _statusTimer?.Dispose();
            base.OnFormClosed(e);
        }
    }
}