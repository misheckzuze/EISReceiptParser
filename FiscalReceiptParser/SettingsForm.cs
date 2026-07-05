using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using FiscalReceiptParser.Data;

namespace FiscalReceiptParser
{
    public partial class SettingsForm : Form
    {
        private static readonly Color BrandDark = Color.FromArgb(21, 42, 45);
        private static readonly Color BrandAccent = Color.FromArgb(0, 150, 110);
        private static readonly Color BrandAccentHover = Color.FromArgb(0, 128, 94);
        private static readonly Color BodyBg = Color.White;
        private static readonly Color TextMuted = Color.FromArgb(110, 118, 122);
        private static readonly Color TextDark = Color.FromArgb(50, 55, 57);
        private static readonly Color BorderColor = Color.FromArgb(214, 219, 220);
        private static readonly Color ErrorColor = Color.FromArgb(196, 60, 60);

        private TextBox _txtWatchFolder = null!;
        private TextBox _txtFailedFolder = null!;
        private TextBox _txtOutputFolder = null!;
        private Label _lblError = null!;
        private Button _btnSave = null!;
        private Button _btnCancel = null!;

        public SettingsForm()
        {
            BuildUi();
            LoadCurrentSettings();
        }

        private void BuildUi()
        {
            Text = "Settings";
            AutoScaleMode = AutoScaleMode.None;
            AutoScaleDimensions = new SizeF(96F, 96F);
            ClientSize = new Size(720, 640);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = BodyBg;
            Font = new Font("Segoe UI", 9.5f);

            int marginX = 48;
            int contentW = ClientSize.Width - marginX * 2;

            // ---------- HEADER ----------
            var headerBg = new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(ClientSize.Width, 120),
                BackColor = BrandDark
            };

            var lblTitle = new Label
            {
                Text = "Folder Settings",
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 18f, FontStyle.Bold),
                Location = new Point(marginX, 30),
                AutoSize = true
            };

            var lblSubtitle = new Label
            {
                Text = "Configure where invoices are watched, and where they land",
                ForeColor = Color.FromArgb(180, 195, 192),
                Font = new Font("Segoe UI", 10f),
                Location = new Point(marginX, 68),
                AutoSize = true
            };

            headerBg.Controls.Add(lblTitle);
            headerBg.Controls.Add(lblSubtitle);

            int y = headerBg.Bottom + 40;

            (TextBox box, int bottom) watchRow = AddFolderRow(
                "Watch Folder", "PDFs are picked up automatically from here.",
                marginX, y, contentW);
            _txtWatchFolder = watchRow.box;
            y = watchRow.bottom + 32;

            (TextBox box, int bottom) failedRow = AddFolderRow(
                "Failed Folder", "PDFs that can't be read or fiscalised are moved here.",
                marginX, y, contentW);
            _txtFailedFolder = failedRow.box;
            y = failedRow.bottom + 32;

            (TextBox box, int bottom) outputRow = AddFolderRow(
                "Output Folder", "Successfully fiscalised PDFs are moved here.",
                marginX, y, contentW);
            _txtOutputFolder = outputRow.box;
            y = outputRow.bottom + 28;

            _lblError = new Label
            {
                Text = "",
                ForeColor = ErrorColor,
                Font = new Font("Segoe UI", 9.5f),
                Location = new Point(marginX, y),
                Size = new Size(contentW, 28),
                AutoSize = false
            };

            // Buttons at fixed bottom position
            _btnCancel = new Button
            {
                Text = "Cancel",
                Size = new Size(130, 46),
                Location = new Point(ClientSize.Width - marginX - 130, ClientSize.Height - 90),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.White,
                ForeColor = TextDark,
                Font = new Font("Segoe UI", 10.5f),
                Cursor = Cursors.Hand,
                DialogResult = DialogResult.Cancel
            };
            _btnCancel.FlatAppearance.BorderColor = BorderColor;

            _btnSave = new Button
            {
                Text = "Save",
                Size = new Size(150, 46),
                Location = new Point(_btnCancel.Left - 20 - 150, ClientSize.Height - 90),
                FlatStyle = FlatStyle.Flat,
                BackColor = BrandAccent,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            _btnSave.FlatAppearance.BorderSize = 0;
            _btnSave.MouseEnter += (s, e) => _btnSave.BackColor = BrandAccentHover;
            _btnSave.MouseLeave += (s, e) => _btnSave.BackColor = BrandAccent;
            _btnSave.Click += BtnSave_Click;

            Controls.Add(headerBg);
            Controls.Add(_lblError);
            Controls.Add(_btnSave);
            Controls.Add(_btnCancel);

            AcceptButton = _btnSave;
            CancelButton = _btnCancel;
        }

        private (TextBox box, int bottom) AddFolderRow(string title, string description, int x, int y, int width)
        {
            var lblTitle = new Label
            {
                Text = title,
                ForeColor = TextDark,
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                Location = new Point(x, y),
                AutoSize = true
            };

            var lblDesc = new Label
            {
                Text = description,
                ForeColor = TextMuted,
                Font = new Font("Segoe UI", 9f),
                Location = new Point(x, lblTitle.Bottom + 4),
                AutoSize = true
            };

            var box = new TextBox
            {
                Location = new Point(x, lblDesc.Bottom + 10),
                Size = new Size(width - 110, 34),
                Font = new Font("Segoe UI", 10f)
            };

            var btnBrowse = new Button
            {
                Text = "Browse…",
                Location = new Point(box.Right + 12, box.Top - 2),
                Size = new Size(98, 34),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.White,
                ForeColor = TextDark,
                Font = new Font("Segoe UI", 9f),
                Cursor = Cursors.Hand
            };
            btnBrowse.FlatAppearance.BorderColor = BorderColor;
            btnBrowse.Click += (s, e) =>
            {
                using var dialog = new FolderBrowserDialog { Description = $"Select the {title}" };
                if (!string.IsNullOrWhiteSpace(box.Text) && Directory.Exists(box.Text))
                    dialog.SelectedPath = box.Text;

                if (dialog.ShowDialog(this) == DialogResult.OK)
                    box.Text = dialog.SelectedPath;
            };

            Controls.Add(lblTitle);
            Controls.Add(lblDesc);
            Controls.Add(box);
            Controls.Add(btnBrowse);

            return (box, box.Bottom);
        }

        private void LoadCurrentSettings()
        {
            var settings = Database.GetFolderSettings();
            _txtWatchFolder.Text = settings.WatchFolder;
            _txtFailedFolder.Text = settings.FailedFolder;
            _txtOutputFolder.Text = settings.OutputFolder;
        }

        private void BtnSave_Click(object? sender, EventArgs e)
        {
            var watch = _txtWatchFolder.Text.Trim();
            var failed = _txtFailedFolder.Text.Trim();
            var output = _txtOutputFolder.Text.Trim();

            if (string.IsNullOrEmpty(watch) || string.IsNullOrEmpty(failed) || string.IsNullOrEmpty(output))
            {
                _lblError.Text = "All three folders are required.";
                return;
            }

            try
            {
                Directory.CreateDirectory(watch);
                Directory.CreateDirectory(failed);
                Directory.CreateDirectory(output);
            }
            catch (Exception ex)
            {
                _lblError.Text = $"Couldn't create one of the folders: {ex.Message}";
                return;
            }

            Database.SaveFolderSettings(watch, failed, output);
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}