using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using ApiModels = FiscalReceiptParser.Models; // <-- Ensure this matches your NSwag namespace (.Models or .MraApiClient)

namespace FiscalReceiptParser.Services
{
    /// <summary>
    /// Parses a "Sellies Enterprises"-style till receipt (Malawi retail slip format)
    /// into the target invoice JSON structure.
    /// </summary>
    public static class ReceiptParser
    {
        public static ApiModels.InvoiceRoot Parse(List<string> lines)
        {
            var root = new ApiModels.InvoiceRoot();

            root.InvoiceHeader = ParseHeader(lines);
            root.InvoiceLineItems = ParseLineItems(lines, out decimal taxRatePercent);
            root.InvoiceSummary = ParseSummary(lines, taxRatePercent);

            return root;
        }

        private static ApiModels.InvoiceHeader ParseHeader(List<string> lines)
        {
            var header = new ApiModels.InvoiceHeader
            {
                SellerTIN = string.Empty,
                BuyerTIN = string.Empty,
                BuyerName = string.Empty,
                BuyerAuthorizationCode = string.Empty,
                SiteId = string.Empty,
                GlobalConfigVersion = 0,
                TaxpayerConfigVersion = 0,
                TerminalConfigVersion = 0,
                IsExport = false,
                IsReliefSupply = false,
                Vat5CertificateDetails = null,
                PaymentMethod = "Cash"
            };

            string? tillNo = MatchGroup(lines, @"Till No\s*(\d+)");
            string? slipNo = MatchGroup(lines, @"Slip No#\s*(\d+)");
            header.InvoiceNumber = $"T{tillNo ?? "0"}-S{slipNo ?? "0"}";

            var dateLine = lines.FirstOrDefault(l => l.StartsWith("Date and Time", StringComparison.OrdinalIgnoreCase));
            if (dateLine != null)
            {
                var match = Regex.Match(dateLine, @"(\d{2}:\d{2})\s+(\d{2}-\d{2}-\d{2})");
                if (match.Success)
                {
                    var combined = $"{match.Groups[2].Value} {match.Groups[1].Value}";
                    if (DateTime.TryParseExact(combined, "dd-MM-yy HH:mm",
                            CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                    {
                        header.InvoiceDateTime = dt;
                    }
                }
            }

            return header;
        }

        private static List<ApiModels.InvoiceLineItem> ParseLineItems(List<string> lines, out decimal taxRatePercent)
        {
            taxRatePercent = 18.00m;

            var taxLine = lines.FirstOrDefault(l => l.StartsWith("Tax1", StringComparison.OrdinalIgnoreCase));
            if (taxLine != null)
            {
                var rateMatch = Regex.Match(taxLine, @"Tax1\s*@\s*(\d+\.\d{2})%");
                if (rateMatch.Success)
                {
                    taxRatePercent = decimal.Parse(rateMatch.Groups[1].Value, CultureInfo.InvariantCulture);
                }
            }

            var items = new List<ApiModels.InvoiceLineItem>();
            int startIdx = lines.FindIndex(l => l.Contains("ITEM DESCRIPTION", StringComparison.OrdinalIgnoreCase));
            int taxIdx = lines.FindIndex(l => l.StartsWith("Tax1", StringComparison.OrdinalIgnoreCase));

            if (startIdx < 0 || taxIdx < 0) return items;

            var itemLineRegex = new Regex(@"^(?<desc>.+?)\s+(?<qty>\d+)\s+(?<price>[\d,]+\.\d{2})\.?$");

            int id = 1;
            for (int i = startIdx + 1; i < taxIdx; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("----")) continue;

                var match = itemLineRegex.Match(line);
                if (!match.Success) continue;

                var description = match.Groups["desc"].Value.Trim();
                var qty = decimal.Parse(match.Groups["qty"].Value, CultureInfo.InvariantCulture);

                string clearPrice = match.Groups["price"].Value.Replace(",", "");
                var grossTotal = decimal.Parse(clearPrice, CultureInfo.InvariantCulture);

                var taxableAmount = Math.Round(grossTotal / (1 + taxRatePercent / 100m), 2);
                var vatAmount = Math.Round(grossTotal - taxableAmount, 2);
                var unitPrice = qty == 0 ? grossTotal : Math.Round(grossTotal / qty, 2);

                // FIXED: Keeping values native high-precision decimals to match your models
                items.Add(new ApiModels.InvoiceLineItem
                {
                    Id = id++,
                    ProductCode = string.Empty,
                    Description = description,
                    UnitPrice = unitPrice,
                    Quantity = qty,
                    Discount = 0,
                    Total = grossTotal,
                    TotalVAT = vatAmount,
                    TaxRateId = $"VAT{taxRatePercent:0.##}",
                    IsProduct = true
                });
            }

            return items;
        }

        private static ApiModels.InvoiceSummary ParseSummary(List<string> lines, decimal taxRatePercent)
        {
            var summary = new ApiModels.InvoiceSummary
            {
                OfflineSignature = string.Empty,
                TaxBreakDown = new List<ApiModels.TaxBreakDown>()
            };

            var taxLine = lines.FirstOrDefault(l => l.StartsWith("Tax1", StringComparison.OrdinalIgnoreCase));
            if (taxLine != null)
            {
                var match = Regex.Match(taxLine, @"Tax1\s*@\s*(\d+\.\d{2})%\s*on\s*([\d,]+\.\d{2})\s+([\d,]+\.\d{2})\.?");
                if (match.Success)
                {
                    var taxableAmount = decimal.Parse(match.Groups[2].Value.Replace(",", ""), CultureInfo.InvariantCulture);
                    var taxAmount = decimal.Parse(match.Groups[3].Value.Replace(",", ""), CultureInfo.InvariantCulture);

                    // FIXED: Passed straight as decimals
                    summary.TaxBreakDown.Add(new ApiModels.TaxBreakDown
                    {
                        RateId = $"VAT{taxRatePercent:0.##}",
                        TaxableAmount = taxableAmount,
                        TaxAmount = taxAmount
                    });

                    summary.TotalVAT = taxAmount;
                }
            }

            // FIXED: Values extracted safely as decimals
            summary.InvoiceTotal = ParseMoneyClean(MatchGroup(lines, @"TOTAL DUE:\s*([\d,]+\.\d{2})"));
            summary.AmountTendered = ParseMoneyClean(MatchGroup(lines, @"CASH TENDERED:\s*([\d,]+\.\d{2})"));

            return summary;
        }

        private static string? MatchGroup(List<string> lines, string pattern)
        {
            foreach (var line in lines)
            {
                var match = Regex.Match(line, pattern, RegexOptions.IgnoreCase);
                if (match.Success) return match.Groups[1].Value;
            }
            return null;
        }

        private static decimal ParseMoneyClean(string? rawValue)
        {
            if (string.IsNullOrEmpty(rawValue)) return 0;
            return decimal.Parse(rawValue.Replace(",", ""), CultureInfo.InvariantCulture);
        }
    }
}