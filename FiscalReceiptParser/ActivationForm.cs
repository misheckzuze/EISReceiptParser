using System;
using System.Net.Http;
using System.Windows.Forms;
using FiscalReceiptParser.Services;

namespace FiscalReceiptParser
{
    public partial class ActivationForm : Form
    {
        private TextBox txtCode = null!;
        private Button btnActivate = null!;
        private Label lblStatus = null!;

        public ActivationForm()
        {
            InitializeComponent();
            BuildUi();
        }

        private void BuildUi()
        {
            Text = "Activate Terminal";
            Width = 420;
            Height = 200;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;

            var lblPrompt = new Label
            {
                Text = "Enter your MRA terminal activation code:",
                Left = 12,
                Top = 15,
                Width = 380
            };

            txtCode = new TextBox { Left = 12, Top = 40, Width = 380 };

            btnActivate = new Button { Text = "Activate", Left = 12, Top = 75, Width = 100 };
            btnActivate.Click += BtnActivate_Click;

            lblStatus = new Label
            {
                Left = 12,
                Top = 110,
                Width = 380,
                Height = 40,
                ForeColor = Color.DimGray
            };

            Controls.Add(lblPrompt);
            Controls.Add(txtCode);
            Controls.Add(btnActivate);
            Controls.Add(lblStatus);
        }

        private void ActivationForm_Load(object sender, EventArgs e)
        {
        }

        private async void BtnActivate_Click(object? sender, EventArgs e)
        {
            var code = txtCode.Text.Trim();
            if (string.IsNullOrEmpty(code))
            {
                lblStatus.ForeColor = Color.Firebrick;
                lblStatus.Text = "Please enter an activation code.";
                return;
            }

            btnActivate.Enabled = false;
            lblStatus.ForeColor = Color.DimGray;
            lblStatus.Text = "Activating...";

            try
            {
                using var httpClient = new HttpClient();
                var service = new MraApiService(httpClient);
                var response = await service.ActivateTerminalServiceAsync(code);

                if (response != null && response.StatusCode == 1 && response.Data != null)
                {
                    lblStatus.Text = "Confirming activation...";
                    bool confirmed = await service.ConfirmTerminalActivationAsync();

                    if (confirmed)
                    {
                        lblStatus.Text = "Fetching products...";

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
                            lblStatus.ForeColor = Color.SeaGreen;
                            lblStatus.Text = "Activation complete. Products loaded.";
                            DialogResult = DialogResult.OK;
                            Close();
                        }
                        else
                        {
                            // Activation & confirmation already succeeded — don't block the
                            // terminal from being usable just because the product catalog
                            // sync failed; let them retry the sync later instead.
                            lblStatus.ForeColor = Color.DarkOrange;
                            lblStatus.Text = "Activated, but failed to fetch products.";
                            DialogResult = DialogResult.OK;
                            Close();
                        }
                    }
                    else
                    {
                        lblStatus.ForeColor = Color.Firebrick;
                        lblStatus.Text = "Activated, but confirmation failed. Please try again.";
                    }
                }
                else
                {
                    lblStatus.ForeColor = Color.Firebrick;
                    lblStatus.Text = response?.Remark ?? "Activation failed. Check the code and try again.";
                }
            }
            catch (Exception ex)
            {
                lblStatus.ForeColor = Color.Firebrick;
                lblStatus.Text = $"Activation error: {ex.Message}";
            }
            finally
            {
                btnActivate.Enabled = true;
            }
        }
    }
}