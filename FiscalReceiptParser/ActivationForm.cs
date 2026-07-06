using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Net.Http;
using System.Windows.Forms;
using FiscalReceiptParser.Services;

namespace FiscalReceiptParser
{
    public partial class ActivationForm : Form
    {
        private static readonly Color BrandDark = Color.FromArgb(21, 42, 45);
        private static readonly Color BrandAccent = Color.FromArgb(0, 150, 110);
        private static readonly Color BrandAccentHover = Color.FromArgb(0, 128, 94);
        private static readonly Color BrandAccentPressed = Color.FromArgb(0, 108, 79);
        private static readonly Color BodyBg = Color.White;
        private static readonly Color TextMuted = Color.FromArgb(110, 118, 122);
        private static readonly Color BorderColor = Color.FromArgb(214, 219, 220);
        private static readonly Color ErrorColor = Color.FromArgb(196, 60, 60);
        private static readonly Color SuccessColor = Color.FromArgb(30, 140, 90);
        private static readonly Color WarningColor = Color.FromArgb(196, 130, 30);

        private const string CodePlaceholder = "e.g. MRA-TERM-XXXXXX";

        private TextBox txtCode = null!;
        private Button btnActivate = null!;
        private Label lblStatus = null!;
        private Label lblStatusIcon = null!;
        private ProgressBar spinner = null!;
        private Panel codeBorder = null!;

        public ActivationForm()
        {
            BuildUi();
        }

        private void BuildUi()
        {
            Text = "Activate Terminal";
            AutoScaleMode = AutoScaleMode.None;
            AutoScaleDimensions = new SizeF(96F, 96F);
            ClientSize = new Size(720, 580);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = BodyBg;
            Font = new Font("Segoe UI", 9.5f);

            int marginX = 56;
            int contentW = ClientSize.Width - (marginX * 2);

            // ========== HEADER ==========
            var headerBg = new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(ClientSize.Width, 170),
                BackColor = BrandDark
            };

            var lblTitle = new Label
            {
                Text = "Terminal Activation",
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 20f, FontStyle.Bold),
                Location = new Point(marginX, 40),
                AutoSize = true
            };

            var lblSubtitle = new Label
            {
                Text = "Malawi Revenue Authority · Fiscal Device Setup",
                ForeColor = Color.FromArgb(180, 195, 192),
                Font = new Font("Segoe UI", 11f),
                Location = new Point(marginX, 88),
                AutoSize = true
            };

            headerBg.Controls.Add(lblTitle);
            headerBg.Controls.Add(lblSubtitle);

            // ========== BODY ==========
            int y = headerBg.Bottom + 48;

            // Prompt
            var lblPrompt = new Label
            {
                Text = "Enter your MRA terminal activation code",
                ForeColor = Color.FromArgb(50, 55, 57),
                Font = new Font("Segoe UI", 12f),
                Location = new Point(marginX, y),
                Size = new Size(contentW, 36),
                AutoSize = false
            };
            y = lblPrompt.Bottom + 20;

            // Textbox border
            codeBorder = new Panel
            {
                Location = new Point(marginX, y),
                Size = new Size(contentW, 64),
                BackColor = BorderColor,
                Padding = new Padding(1)
            };

            var textHost = new Panel
            {
                Location = new Point(1, 1),
                Size = new Size(codeBorder.Width - 2, codeBorder.Height - 2),
                BackColor = Color.White
            };

            txtCode = new TextBox
            {
                Location = new Point(18, (textHost.Height - 26) / 2),
                Size = new Size(textHost.Width - 36, 26),
                BorderStyle = BorderStyle.None,
                Font = new Font("Segoe UI", 14f),
                ForeColor = TextMuted,
                Text = CodePlaceholder
            };

            textHost.Controls.Add(txtCode);
            codeBorder.Controls.Add(textHost);
            y = codeBorder.Bottom + 28;

            // Button
            btnActivate = new Button
            {
                Text = "Activate",
                Location = new Point(marginX, y),
                Size = new Size(contentW, 58),
                FlatStyle = FlatStyle.Flat,
                BackColor = BrandAccent,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 13f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnActivate.FlatAppearance.BorderSize = 0;
            btnActivate.Region = RoundedRegion(btnActivate.Width, btnActivate.Height, 6);
            btnActivate.MouseEnter += (s, e) => btnActivate.BackColor = BrandAccentHover;
            btnActivate.MouseLeave += (s, e) => btnActivate.BackColor = btnActivate.Enabled ? BrandAccent : BorderColor;
            btnActivate.MouseDown += (s, e) => btnActivate.BackColor = BrandAccentPressed;
            btnActivate.MouseUp += (s, e) => btnActivate.BackColor = BrandAccentHover;
            btnActivate.Click += BtnActivate_Click;
            y = btnActivate.Bottom + 24;

            // Spinner
            spinner = new ProgressBar
            {
                Style = ProgressBarStyle.Marquee,
                MarqueeAnimationSpeed = 30,
                Location = new Point(marginX, y),
                Size = new Size(contentW, 6),
                Visible = false
            };
            y = spinner.Bottom + 24;

            // Status icon
            lblStatusIcon = new Label
            {
                Text = "",
                Font = new Font("Segoe UI", 14f, FontStyle.Bold),
                Location = new Point(marginX, y),
                Size = new Size(32, 32),
                TextAlign = ContentAlignment.TopLeft
            };

            // Status text
            lblStatus = new Label
            {
                Text = "",
                ForeColor = TextMuted,
                Font = new Font("Segoe UI", 10.5f),
                Location = new Point(marginX + 32, y),
                Size = new Size(contentW - 32, 80),
                TextAlign = ContentAlignment.TopLeft
            };

            // ========== ADD ALL ==========
            Controls.Add(headerBg);
            Controls.Add(lblPrompt);
            Controls.Add(codeBorder);
            Controls.Add(btnActivate);
            Controls.Add(spinner);
            Controls.Add(lblStatusIcon);
            Controls.Add(lblStatus);

            // ========== EVENTS ==========
            txtCode.GotFocus += (s, e) =>
            {
                if (txtCode.Text == CodePlaceholder)
                {
                    txtCode.Text = string.Empty;
                    txtCode.ForeColor = Color.Black;
                }
                codeBorder.BackColor = BrandAccent;
            };

            txtCode.LostFocus += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(txtCode.Text))
                {
                    txtCode.Text = CodePlaceholder;
                    txtCode.ForeColor = TextMuted;
                }
                codeBorder.BackColor = BorderColor;
            };

            txtCode.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    BtnActivate_Click(s, EventArgs.Empty);
                }
            };

            AcceptButton = btnActivate;
        }

        private static Region RoundedRegion(int width, int height, int radius)
        {
            var path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(0, 0, d, d, 180, 90);
            path.AddArc(width - d, 0, d, d, 270, 90);
            path.AddArc(width - d, height - d, d, d, 0, 90);
            path.AddArc(0, height - d, d, d, 90, 90);
            path.CloseFigure();
            return new Region(path);
        }

        private void SetStatus(string message, Color color, string icon = "")
        {
            lblStatusIcon.Text = icon;
            lblStatusIcon.ForeColor = color;
            lblStatus.Text = message;
            lblStatus.ForeColor = color == TextMuted ? TextMuted : color;
        }

        private void SetBusy(bool busy)
        {
            spinner.Visible = busy;
            btnActivate.Enabled = !busy;
            txtCode.Enabled = !busy;
            btnActivate.BackColor = busy ? BorderColor : BrandAccent;
            btnActivate.ForeColor = busy ? TextMuted : Color.White;
        }

        private void ActivationForm_Load(object sender, EventArgs e)
        {
        }

        private async void BtnActivate_Click(object? sender, EventArgs e)
        {
            var code = (txtCode.Text == CodePlaceholder ? string.Empty : txtCode.Text).Trim();
            if (string.IsNullOrEmpty(code))
            {
                SetStatus("Please enter an activation code.", ErrorColor, "!");
                txtCode.Focus();
                return;
            }

            SetBusy(true);
            SetStatus("Activating…", TextMuted);

            try
            {
                using var httpClient = new HttpClient();
                var service = new MraApiService(httpClient);
                var response = await service.ActivateTerminalServiceAsync(code);

                if (response != null && response.StatusCode == 1 && response.Data != null)
                {
                    SetStatus("Confirming activation…", TextMuted);
                    bool confirmed = await service.ConfirmTerminalActivationAsync();

                    if (confirmed)
                    {
                        SetStatus("Fetching products…", TextMuted);

                        var tin = ConfigHelper.GetTin();
                        var siteId = ConfigHelper.GetTerminalSiteId();
                        var token = ConfigHelper.GetToken();

                        bool productsFetched = false;
                        if (!string.IsNullOrEmpty(tin) && !string.IsNullOrEmpty(siteId) && !string.IsNullOrEmpty(token))
                        {
                            productsFetched = await service.GetTerminalSiteProductsAsync(tin, siteId, token);
                        }

                        if (productsFetched)
                        {
                            SetStatus("Activation complete. Products loaded.", SuccessColor, "✓");
                            DialogResult = DialogResult.OK;
                            Close();
                        }
                        else
                        {
                            SetStatus("Activated, but failed to fetch products.", WarningColor, "!");
                            DialogResult = DialogResult.OK;
                            Close();
                        }
                    }
                    else
                    {
                        SetStatus("Activated, but confirmation failed. Please try again.", ErrorColor, "✗");
                    }
                }
                else
                {
                    SetStatus(response?.Remark ?? "Activation failed. Check the code and try again.", ErrorColor, "✗");
                }
            }
            catch (Exception ex)
            {
                SetStatus($"Activation error: {ex.Message}", ErrorColor, "✗");
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void ActivationForm_Load_1(object sender, EventArgs e)
        {

        }
    }
}