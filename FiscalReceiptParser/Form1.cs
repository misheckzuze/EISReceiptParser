using System;
using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Windows.Forms;
using FiscalReceiptParser.Services;

namespace FiscalReceiptParser
{
    public partial class Form1 : Form
    {
        private TextBox txtFilePath = null!;
        private Button btnBrowse = null!;
        private Button btnParse = null!;
        private Button btnSubmit = null!;
        private Button btnSaveJson = null!;
        private RichTextBox txtOutput = null!;
        private Label lblStatus = null!;

        private string? _lastPdfPath;
        private string? _lastJson;
        private FiscalReceiptParser.Models.InvoiceRoot? _lastParsedInvoice;

        public Form1()
        {
            InitializeComponent();
            BuildUi();
        }

        private void BuildUi()
        {
            Text = "Fiscal Receipt Parser";
            Width = 900;
            Height = 650;
            MinimumSize = new Size(700, 500);

            txtFilePath = new TextBox
            {
                Left = 12,
                Top = 12,
                Width = 400,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                ReadOnly = true,
                PlaceholderText = "Select a receipt PDF..."
            };

            btnBrowse = new Button
            {
                Text = "Browse...",
                Left = 670,
                Top = 10,
                Width = 100,
                Height = 40,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            btnBrowse.Click += BtnBrowse_Click;

            btnParse = new Button
            {
                Text = "Parse to JSON",
                Left = 780,
                Top = 10,
                Width = 100,
                Height = 40,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Enabled = false
            };
            btnParse.Click += BtnParse_Click;

            txtOutput = new RichTextBox
            {
                Left = 12,
                Top = 46,
                Width = 868,
                Height = 500,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
                Font = new Font("Consolas", 10),
                ReadOnly = true,
                WordWrap = false,
                ScrollBars = RichTextBoxScrollBars.Both
            };

            btnSaveJson = new Button
            {
                Text = "Save JSON As...",
                Left = 12,
                Width = 140,
                Height = 40,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
                Enabled = false
            };
            btnSaveJson.Click += BtnSaveJson_Click;

            btnSubmit = new Button
            {
                Text = "Submit to MRA",
                Left = 160,
                Width = 140,
                Height = 40,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
                Enabled = false
            };
            btnSubmit.Click += BtnSubmit_Click;

            lblStatus = new Label
            {
                Left = 310,   // starts after btnSaveJson (12–152) and btnSubmit (160–300)
                Width = 550,
                AutoSize = false,
                Height = 40,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.DimGray
            };

            // Position the bottom row relative to the form's client area
            btnSaveJson.Top = ClientSize.Height - 40;
            btnSubmit.Top = ClientSize.Height - 40;
            lblStatus.Top = ClientSize.Height - 36;

            Controls.Add(txtFilePath);
            Controls.Add(btnBrowse);
            Controls.Add(btnParse);
            Controls.Add(txtOutput);
            Controls.Add(btnSaveJson);
            Controls.Add(btnSubmit);
            Controls.Add(lblStatus);
        }

        private void BtnBrowse_Click(object? sender, EventArgs e)
        {
            using var dialog = new OpenFileDialog
            {
                Title = "Select a receipt PDF",
                Filter = "PDF files (*.pdf)|*.pdf|All files (*.*)|*.*"
            };

            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                _lastPdfPath = dialog.FileName;
                txtFilePath.Text = _lastPdfPath;
                btnParse.Enabled = true;
                btnSaveJson.Enabled = false;
                btnSubmit.Enabled = false;
                _lastParsedInvoice = null;
                txtOutput.Clear();
                SetStatus(string.Empty);
            }
        }

        private void BtnParse_Click(object? sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_lastPdfPath) || !File.Exists(_lastPdfPath))
            {
                MessageBox.Show(this, "Please select a valid PDF file first.", "No file selected",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                Cursor = Cursors.WaitCursor;
                btnParse.Enabled = false;
                SetStatus("Parsing...");

                var lines = PdfTextExtractor.ExtractLines(_lastPdfPath);
                var invoice = ReceiptParser.Parse(lines);
                _lastParsedInvoice = invoice;
                btnSubmit.Enabled = true;

                var jsonOptions = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };
                _lastJson = JsonSerializer.Serialize(invoice, jsonOptions);

                txtOutput.Text = _lastJson;
                btnSaveJson.Enabled = true;
                SetStatus($"Parsed successfully — {invoice.InvoiceLineItems.Count} line item(s) found.");
            }
            catch (Exception ex)
            {
                _lastJson = null;
                _lastParsedInvoice = null;
                btnSaveJson.Enabled = false;
                btnSubmit.Enabled = false;
                txtOutput.Text = string.Empty;
                SetStatus("Failed to parse the PDF.");
                MessageBox.Show(this, $"Could not parse this receipt:\n\n{ex.Message}",
                    "Parsing error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Cursor = Cursors.Default;
                btnParse.Enabled = true;
            }
        }

        private async void BtnSubmit_Click(object? sender, EventArgs e)
        {
            if (_lastParsedInvoice == null)
            {
                MessageBox.Show(this, "Parse a receipt first.", "Nothing to submit",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string? bearerToken = ConfigHelper.GetToken();
            if (string.IsNullOrEmpty(bearerToken))
            {
                MessageBox.Show(this, "This terminal isn't activated yet — no token found.",
                    "Not activated", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            try
            {
                btnSubmit.Enabled = false;
                Cursor = Cursors.WaitCursor;
                SetStatus("Submitting to MRA...");

                var result = await TransactionService.SubmitParsedInvoiceAsync(_lastParsedInvoice, bearerToken);

                foreach (var warning in result.Warnings)
                {
                    System.Diagnostics.Debug.WriteLine(warning);
                }

                if (result.Success)
                {
                    SetStatus($"Submitted — invoice {result.InvoiceNumber}");
                    MessageBox.Show(this,
                        $"Invoice submitted successfully.\n\nInvoice Number: {result.InvoiceNumber}\nValidation URL: {result.ValidationUrl}",
                        "Submission successful", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else if (result.IsOffline)
                {
                    SetStatus($"Saved offline — invoice {result.InvoiceNumber}");
                    MessageBox.Show(this,
                        $"Could not reach MRA. Saved locally as invoice {result.InvoiceNumber} and will retry automatically when online.",
                        "Processing offline", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                SetStatus("Submission failed.");
                MessageBox.Show(this, $"Failed to submit invoice:\n\n{ex.Message}",
                    "Submission error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Cursor = Cursors.Default;
                btnSubmit.Enabled = true;
            }
        }

        private void BtnSaveJson_Click(object? sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_lastJson)) return;

            using var dialog = new SaveFileDialog
            {
                Title = "Save invoice JSON",
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                FileName = "invoice.json"
            };

            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                File.WriteAllText(dialog.FileName, _lastJson);
                SetStatus($"Saved to {dialog.FileName}");
            }
        }

        private void SetStatus(string message) => lblStatus.Text = message;

        private void Form1_Load_1(object sender, EventArgs e)
        {
        }
    }
}