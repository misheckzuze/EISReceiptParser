using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FiscalReceiptParser.Services
{
    /// <summary>
    /// Ported from Java's EscPosReceiptPrinter — same raw ESC/POS byte commands,
    /// same layout logic, sent via RawPrinterHelper instead of javax.print.
    /// </summary>
    public static class EscPosReceiptPrinter
    {
        private static readonly byte[] EscInit = { 0x1B, 0x40 };
        private static readonly byte[] Lf = { 0x0A };
        private static readonly byte[] EscAlignCenter = { 0x1B, 0x61, 0x01 };
        private static readonly byte[] EscAlignLeft = { 0x1B, 0x61, 0x00 };
        private static readonly byte[] EscEmphasizeOn = { 0x1B, 0x45, 0x01 };
        private static readonly byte[] EscEmphasizeOff = { 0x1B, 0x45, 0x00 };
        private static readonly byte[] EscDoubleSizeOn = { 0x1B, 0x21, 0x10 };
        private static readonly byte[] EscDoubleSizeOff = { 0x1B, 0x21, 0x00 };
        private static readonly byte[] GsCutPaper = { 0x1D, 0x56, 0x41, 0x10 };
        private static readonly byte[] EscDrawerKick = { 0x1B, 0x70, 0x00, 0x32, 0xFA };

        private const int ReceiptWidth = 48;
        private static Encoding _cp437 = Encoding.GetEncoding(437);

        public static void PrintReceipt(
            InvoiceHeader invoiceHeader,
            string buyersName,
            string buyersTIN,
            List<LineItemDto> lineItems,
            string validationUrl,
            double amountTendered,
            double change,
            List<TaxBreakDown> invoiceTaxBreakDown,
            List<LevyBreakDown> invoiceLevies)
        {
            using var output = new MemoryStream();
            var contact = InvoiceRepository.GetTerminalContactInfo();

            string tin = InvoiceRepository.GetSellerTin() ?? "";
            string companyName = InvoiceRepository.GetTradingName();
            bool isVatRegistered = InvoiceRepository.IsVatRegistered();
            string vatStatus = isVatRegistered ? "*VAT REGISTERED*" : "*NOT VAT REGISTERED*";
            string currentDateTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            try
            {
                output.Write(EscInit); output.Write(Lf);
                output.Write(EscAlignCenter);

                output.Write(EscEmphasizeOn);
                WriteText(output, "*** START OF LEGAL RECEIPT ***");
                output.Write(EscEmphasizeOff);
                output.Write(Lf); output.Write(Lf);

                output.Write(EscEmphasizeOn);
                output.Write(EscDoubleSizeOn);
                WriteText(output, companyName);
                output.Write(EscDoubleSizeOff);
                output.Write(Lf);
                output.Write(EscEmphasizeOff);

                WriteText(output, contact.AddressLine); output.Write(Lf);
                WriteText(output, $"Tel: {contact.Phone}"); output.Write(Lf);
                WriteText(output, $"E-MAIL: {contact.Email}"); output.Write(Lf);
                WriteText(output, $"TIN: {tin}"); output.Write(Lf);
                WriteText(output, vatStatus); output.Write(Lf); output.Write(Lf);

                output.Write(EscEmphasizeOn);
                WriteText(output, "*** TAX INVOICE ***");
                output.Write(EscEmphasizeOff);
                output.Write(Lf);

                PrintDivider(output);

                PrintFormattedLine(output, "Receipt#", invoiceHeader.InvoiceNumber);
                PrintFormattedLine(output, "Date", currentDateTime);
                PrintFormattedLine(output, "Customer", buyersName);
                PrintFormattedLine(output, "Buyer TIN", buyersTIN);

                PrintDivider(output);

                output.Write(EscAlignLeft);
                foreach (var item in lineItems)
                {
                    WriteText(output, item.Description); output.Write(Lf);

                    string quantityPrice = $"{item.Quantity} X {FormatCurrency(item.UnitPrice)}";
                    string totalAmount = FormatCurrency(item.Quantity * item.UnitPrice);
                    string taxRateDisplay = isVatRegistered ? $"{totalAmount} {item.TaxRateId}" : totalAmount;

                    PrintFormattedLine(output, quantityPrice, taxRateDisplay);
                }

                PrintDivider(output);

                double totalVat;
                double subtotal;

                if (isVatRegistered)
                {
                    totalVat = 0;
                    subtotal = 0;
                    foreach (var tax in invoiceTaxBreakDown)
                    {
                        totalVat += tax.TaxAmount;
                        subtotal += tax.TaxableAmount;
                    }
                }
                else
                {
                    subtotal = 0;
                    foreach (var tax in invoiceTaxBreakDown) subtotal += tax.TaxableAmount;
                    totalVat = 0;
                }

                double totalLevies = 0;
                if (invoiceLevies != null)
                {
                    foreach (var levy in invoiceLevies) totalLevies += levy.LevyAmount;
                }

                PrintFormattedLine(output, "Subtotal", FormatCurrency(subtotal));

                if (isVatRegistered)
                {
                    foreach (var tax in invoiceTaxBreakDown)
                    {
                        double rate = InvoiceRepository.GetTaxRatePercent(tax.RateId ?? "");
                        string label = $"VAT {tax.RateId} - {rate}%";
                        PrintFormattedLine(output, label, FormatCurrency(tax.TaxAmount));
                    }
                }

                if (invoiceLevies != null)
                {
                    foreach (var levy in invoiceLevies)
                    {
                        string levyName = InvoiceRepository.GetLevyNameById(levy.LevyTypeId ?? "");
                        PrintFormattedLine(output, levyName, FormatCurrency(levy.LevyAmount));
                    }
                }

                double invoiceTotal = subtotal + totalVat + totalLevies;

                output.Write(EscEmphasizeOn);
                PrintFormattedLine(output, "TOTAL", FormatCurrency(invoiceTotal));
                output.Write(EscEmphasizeOff);

                PrintFormattedLine(output, "Transaction", invoiceHeader.PaymentMethod);
                PrintFormattedLine(output, "Amount Paid", FormatCurrency(amountTendered));

                if (change > 0)
                {
                    PrintFormattedLine(output, "Change", FormatCurrency(change));
                }

                output.Write(Lf);
                PrintDivider(output);

                output.Write(EscAlignCenter);
                output.Write(EscEmphasizeOn);
                WriteText(output, "*** VALIDATE YOUR RECEIPT ***");
                output.Write(EscEmphasizeOff);
                output.Write(Lf);

                PrintQrCode(output, validationUrl);

                output.Write(Lf);
                PrintDivider(output);

                output.Write(EscEmphasizeOn);
                WriteText(output, "*** THANK YOU FOR YOUR BUSINESS ***");
                output.Write(EscEmphasizeOff);
                output.Write(Lf);
                WriteText(output, "Keep this receipt for your records");
                output.Write(Lf);

                output.Write(EscEmphasizeOn);
                WriteText(output, "*** END OF LEGAL RECEIPT ***");
                output.Write(EscEmphasizeOff);
                output.Write(Lf); output.Write(Lf); output.Write(Lf);

                output.Write(EscDrawerKick);
                output.Write(GsCutPaper);

                RawPrinterHelper.SendBytesToPrinter(RawPrinterHelper.GetDefaultPrinterName(), output.ToArray());
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to print receipt: {ex.Message}", ex);
            }
        }

        private static void PrintFormattedLine(MemoryStream output, string label, string value)
        {
            int totalSpacing = ReceiptWidth - label.Length - value.Length;
            var line = new StringBuilder(label);
            for (int i = 0; i < totalSpacing; i++) line.Append(' ');
            line.Append(value);
            WriteText(output, line.ToString());
            output.Write(Lf);
        }

        private static void PrintDivider(MemoryStream output)
        {
            WriteText(output, new string('-', ReceiptWidth));
            output.Write(Lf);
        }

        private static string FormatCurrency(double value) => value.ToString("#,##0.00");

        private static void WriteText(MemoryStream output, string text)
        {
            byte[] bytes = _cp437.GetBytes(text ?? "");
            output.Write(bytes, 0, bytes.Length);
        }

        private static void PrintQrCode(MemoryStream output, string data)
        {
            try
            {
                string qrData = data.Length > 300 ? data.Substring(0, 300) : data;
                output.Write(EscAlignCenter);

                output.Write(new byte[] { 0x1D, 0x28, 0x6B, 0x03, 0x00, 0x31, 0x43, 0x00 });
                output.Write(new byte[] { 0x1D, 0x28, 0x6B, 0x04, 0x00, 0x31, 0x41, 0x32, 0x00 });

                byte qrSize = 4;
                output.Write(new byte[] { 0x1D, 0x28, 0x6B, 0x03, 0x00, 0x31, 0x43, qrSize });

                byte errorCorrectionLevel = 51; // H
                output.Write(new byte[] { 0x1D, 0x28, 0x6B, 0x03, 0x00, 0x31, 0x45, errorCorrectionLevel });

                byte[] qrBytes = _cp437.GetBytes(qrData);
                int dataLength = qrBytes.Length + 3;
                int pL = dataLength % 256;
                int pH = dataLength / 256;

                output.Write(new byte[] { 0x1D, 0x28, 0x6B, (byte)pL, (byte)pH, 0x31, 0x50, 0x30 });
                output.Write(qrBytes, 0, qrBytes.Length);

                output.Write(new byte[] { 0x1D, 0x28, 0x6B, 0x03, 0x00, 0x31, 0x51, 0x30 });
            }
            catch
            {
                output.Write(EscAlignCenter);
                output.Write(Lf);
                WriteText(output, "(QR Code Unavailable)");
                output.Write(Lf);
            }
        }
    }
}