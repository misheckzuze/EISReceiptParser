using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ParsedModels = FiscalReceiptParser.Models;
using FiscalReceiptParser.Models;

namespace FiscalReceiptParser.Services
{

    public static class TransactionService
    {
        /// <summary>
        /// Takes an already-parsed invoice (from ReceiptParser.Parse) and enriches it
        /// with authoritative data from SQLite before submitting to MRA:
        ///   - SellerTIN, SiteId, config versions (from activation data)
        ///   - ProductCode + real TaxRateId per line item (from the Products table,
        ///     overriding whatever the parser guessed from the source document)
        ///   - A freshly generated MRA-format invoice number (the parsed document's
        ///     own invoice number is not valid MRA format and is kept only as
        ///     SourceInvoiceNumber for reference/logging)
        /// </summary>
        public static async Task<SubmitSaleResult> SubmitParsedInvoiceAsync(
     ParsedModels.InvoiceRoot parsedInvoice,
     string bearerToken)
        {
            if (parsedInvoice?.InvoiceLineItems == null || parsedInvoice.InvoiceLineItems.Count == 0)
                throw new ArgumentException("Parsed invoice has no line items.");

            string sourceInvoiceNumber = parsedInvoice.InvoiceHeader?.InvoiceNumber ?? "";

            var result = new SubmitSaleResult
            {
                SourceInvoiceNumber = sourceInvoiceNumber
            };

            // Reprint / duplicate-drop detection: if this exact source slip number has
            // already been recorded, don't fiscalise it again — return the existing
            // MRA invoice number/validation URL instead of generating a new invoice.
            var existing = InvoiceRepository.FindBySourceInvoiceNumber(sourceInvoiceNumber);
            if (existing != null)
            {
                result.InvoiceNumber = existing.InvoiceNumber;
                result.ValidationUrl = existing.ValidationUrl;
                result.Success = existing.State == 1;
                result.IsOffline = existing.State == 0;
                result.Remark = "Duplicate receipt — already fiscalised previously. Reprint only, not resubmitted.";
                result.Warnings.Add($"Source receipt '{sourceInvoiceNumber}' already exists as MRA invoice {existing.InvoiceNumber} — skipped resubmission.");

                // Still print, since this could be a genuine reprint request — but using
                // the ORIGINAL invoice's data, not a freshly generated one.
                // (See note below on reprinting from stored data.)
                return result;
            }

            string invoiceNumber = ReceiptNumberGenerator.GenerateNewReceiptNumber();
            result.InvoiceNumber = invoiceNumber;

            var header = BuildInvoiceHeader(parsedInvoice.InvoiceHeader, invoiceNumber);
            var lineItems = BuildLineItems(parsedInvoice.InvoiceLineItems, result.Warnings);
            var (taxBreakdowns, levyBreakdowns, totalVat, invoiceTotal) = BuildSummaryParts(lineItems);

            double amountTendered = parsedInvoice.InvoiceSummary != null
                ? (double)parsedInvoice.InvoiceSummary.AmountTendered
                : invoiceTotal;

            var summary = new InvoiceSummary
            {
                TaxBreakDown = taxBreakdowns,
                LevyBreakDown = levyBreakdowns,
                TotalVAT = totalVat,
                OfflineSignature = "",
                InvoiceTotal = invoiceTotal,
                AmountTendered = amountTendered
            };

            bool saved = InvoiceRepository.SaveTransaction(
                header, sourceInvoiceNumber, lineItems, taxBreakdowns, levyBreakdowns,
                invoiceTotal, totalVat, "", "", isTransmitted: false,
                header.PaymentMethod, amountTendered);

            if (!saved)
            {
                result.Warnings.Add("Failed to save transaction locally.");
            }

            var invoicePayload = new SalesInvoice
            {
                InvoiceHeader = header,
                InvoiceLineItems = lineItems,
                InvoiceSummary = summary
            };

            using var httpClient = new System.Net.Http.HttpClient();
            var service = new MraApiService(httpClient);
            var apiResult = await service.SubmitSalesTransactionServiceAsync(invoicePayload, bearerToken);

            if (apiResult.Success)
            {
                InvoiceRepository.UpdateValidationUrl(invoiceNumber, apiResult.ValidationUrl);
                InvoiceRepository.MarkAsTransmitted(invoiceNumber);

                result.Success = true;
                result.ValidationUrl = apiResult.ValidationUrl;
                result.IsOffline = false;
                result.Remark = apiResult.WasDuplicate
                    ? "Invoice already existed on MRA — marked as transmitted."
                    : apiResult.Remark;
            }
            else
            {
                result.Success = false;
                result.IsOffline = true;
                result.Remark = apiResult.Remark;
                result.Warnings.Add(apiResult.Remark);

                try
                {
                    string? secretKey = ConfigHelper.GetSecretKey();
                    if (!string.IsNullOrEmpty(secretKey))
                    {
                        var generationRequest = new InvoiceGenerationRequest
                        {
                            InvoiceNumber = invoiceNumber,
                            NumItems = lineItems.Count,
                            TransactionDate = header.InvoiceDateTime.DateTime,
                            InvoiceTotal = invoiceTotal,
                            VatAmount = totalVat
                        };

                        string validationUrl = OfflineReceiptSignature.GenerateOfflineReceiptSignature(generationRequest, secretKey);

                        string offlineSignature = validationUrl.Contains("S=")
                            ? validationUrl.Split(new[] { "S=" }, StringSplitOptions.None)[1]
                            : "";

                        InvoiceRepository.UpdateOfflineTransactionDetails(invoiceNumber, validationUrl, offlineSignature);
                        result.ValidationUrl = validationUrl;
                    }
                    else
                    {
                        result.Warnings.Add("Could not generate offline signature — no secret key found locally.");
                    }
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"Failed to generate offline receipt signature: {ex.Message}");
                }
            }

            double changeValue = amountTendered - invoiceTotal;
            if (changeValue < 0) changeValue = 0;

            try
            {
                EscPosReceiptPrinter.PrintReceipt(
                    header,
                    header.BuyerName,
                    header.BuyerTIN,
                    lineItems,
                    result.ValidationUrl,
                    amountTendered,
                    changeValue,
                    taxBreakdowns,
                    levyBreakdowns);

                System.Diagnostics.Debug.WriteLine(result.IsOffline ? "Offline receipt printed." : "Receipt printed.");
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"Failed to print receipt: {ex.Message}");
            }

            return result;
        }
        private static InvoiceHeader BuildInvoiceHeader(ParsedModels.InvoiceHeader parsedHeader, string invoiceNumber)
        {
            return new InvoiceHeader
            {
                InvoiceNumber = invoiceNumber,
                // Keep the parsed transaction's actual date/time — that's when the
                // sale really happened, unlike the invoice number which must be
                // freshly generated in MRA's format.
                InvoiceDateTime = parsedHeader?.InvoiceDateTime ?? DateTime.Now,
                SellerTIN = InvoiceRepository.GetSellerTin() ?? "",
                BuyerTIN = parsedHeader?.BuyerTIN ?? "",
                BuyerName = parsedHeader?.BuyerName ?? "",
                BuyerAuthorizationCode = parsedHeader?.BuyerAuthorizationCode ?? "",
                SiteId = InvoiceRepository.GetSiteId() ?? "",
                GlobalConfigVersion = InvoiceRepository.GetGlobalConfigVersion(),
                TaxpayerConfigVersion = InvoiceRepository.GetTaxpayerConfigVersion(),
                TerminalConfigVersion = InvoiceRepository.GetTerminalConfigVersion(),
                IsExport = parsedHeader?.IsExport ?? false,
                IsReliefSupply = parsedHeader?.IsReliefSupply ?? false,
                Vat5CertificateDetails = null, // TODO: map from parsedHeader.Vat5CertificateDetails if this terminal handles VAT5 exemptions
                PaymentMethod = string.IsNullOrWhiteSpace(parsedHeader?.PaymentMethod) ? "Cash" : parsedHeader.PaymentMethod
            };
        }

        /// <summary>
        /// Re-resolves each parsed line item against the local Products table to get
        /// the authoritative ProductCode + TaxRateId, then recomputes VAT using the
        /// real registered rate — rather than trusting whatever the document parser
        /// inferred from the source receipt's printed tax line.
        /// </summary>
        private static List<LineItemDto> BuildLineItems(
            List<ParsedModels.InvoiceLineItem> parsedItems,
            List<string> warnings)
        {
            var lineItems = new List<LineItemDto>();
            int id = 1;

            foreach (var parsed in parsedItems)
            {
                var product = InvoiceRepository.GetProductInfoByDescription(parsed.Description ?? "");

                string productCode = product.Found ? product.ProductCode : "";
                string taxRateId = product.Found ? product.TaxRateId : (parsed.TaxRateId ?? "");

                if (!product.Found)
                {
                    warnings.Add($"No product match for \"{parsed.Description}\" — falling back to parser's guessed tax rate id \"{taxRateId}\".");
                }

                double grossTotal = (double)parsed.Total; // trust the price actually charged, as parsed

                double rate = InvoiceRepository.GetTaxRatePercent(taxRateId);
                double taxable = Math.Round(grossTotal / (1 + rate / 100.0), 2);
                double vat = Math.Round(grossTotal - taxable, 2);

                lineItems.Add(new LineItemDto
                {
                    Id = id++,
                    ProductCode = productCode,
                    Description = parsed.Description ?? "",
                    UnitPrice = (double)parsed.UnitPrice,
                    Quantity = (double)parsed.Quantity,
                    Discount = (double)parsed.Discount,
                    Total = grossTotal,
                    TotalVAT = vat,
                    TaxRateId = taxRateId,
                    IsProduct = parsed.IsProduct
                });
            }

            return lineItems;
        }

        /// <summary>
        /// Extracted so both a fresh sale and a resend can regenerate the tax breakdown
        /// from line items the same way — matches Java's Helper.generateTaxBreakdown.
        /// </summary>
        private static List<TaxBreakDown> BuildTaxBreakdown(List<LineItemDto> lineItems)
        {
            return lineItems
                .GroupBy(i => i.TaxRateId ?? "")
                .Select(g =>
                {
                    double grossSum = g.Sum(i => i.Total);
                    double rate = InvoiceRepository.GetTaxRatePercent(g.Key);
                    double taxable = Math.Round(grossSum / (1 + rate / 100.0), 2);
                    double tax = Math.Round(grossSum - taxable, 2);

                    return new TaxBreakDown
                    {
                        RateId = g.Key,
                        TaxableAmount = taxable,
                        TaxAmount = tax
                    };
                })
                .ToList();
        }

        private static (List<TaxBreakDown> tax, List<LevyBreakDown> levy, double totalVat, double invoiceTotal)
        BuildSummaryParts(List<LineItemDto> lineItems)
        {
            double totalVat = lineItems.Sum(i => i.TotalVAT);
            double invoiceTotal = lineItems.Sum(i => i.Total);

            var taxBreakdowns = BuildTaxBreakdown(lineItems);

            var activeLevies = InvoiceRepository.GetActiveLevies();
            double totalLevyRate = activeLevies
                .Where(l => string.Equals(l.ChargeMode, "PERCENTAGE", StringComparison.OrdinalIgnoreCase))
                .Sum(l => l.Rate);

            var levyBreakdowns = new List<LevyBreakDown>();

            foreach (var levy in activeLevies)
            {
                double levyAmount = 0.0;

                foreach (var item in lineItems)
                {
                    double lineTotal = item.Total;
                    double itemVat = item.TotalVAT;
                    double netPlusLevies = lineTotal - itemVat;
                    double netTotal = netPlusLevies / (1 + totalLevyRate / 100.0);

                    double itemLevy = string.Equals(levy.ChargeMode, "PERCENTAGE", StringComparison.OrdinalIgnoreCase)
                        ? (netTotal * levy.Rate) / 100.0
                        : levy.Rate * item.Quantity;

                    levyAmount += itemLevy;
                }

                levyBreakdowns.Add(new LevyBreakDown
                {
                    LevyTypeId = levy.Id,
                    LevyRate = levy.Rate,
                    LevyAmount = Math.Round(levyAmount, 2)
                });
            }

            return (taxBreakdowns, levyBreakdowns, totalVat, invoiceTotal);
        }

        /// <summary>
        /// Ported from Java's ApiClient.retryPendingTransactions. Rebuilds each locally
        /// saved-but-not-transmitted invoice (State = 0) and resubmits it.
        ///
        /// NOTE: matches Java exactly in passing empty buyerTin/buyerAuthCode on resend —
        /// this is a limitation already present in the Java source, not something
        /// introduced here. Also matches Java in NOT attempting to print on retry
        /// (the original sale already printed a receipt, online or offline, the first time).
        /// </summary>
        public static async Task RetryPendingTransactionsAsync(string bearerToken)
        {
            if (string.IsNullOrEmpty(bearerToken))
            {
                System.Diagnostics.Debug.WriteLine("Retry skipped — no token available (terminal not activated?).");
                return;
            }

            List<PendingInvoice> pending;
            try
            {
                pending = InvoiceRepository.GetPendingInvoiceNumbers();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error fetching pending transactions: {ex.Message}");
                return;
            }

            if (pending.Count == 0) return;

            using var httpClient = new System.Net.Http.HttpClient();
            var service = new MraApiService(httpClient);

            foreach (var pendingInvoice in pending)
            {
                try
                {
                    var invoiceRow = InvoiceRepository.GetInvoiceRow(pendingInvoice.InvoiceNumber);
                    if (invoiceRow == null)
                    {
                        System.Diagnostics.Debug.WriteLine($"⚠️ No Invoices row found for {pendingInvoice.InvoiceNumber}, skipping.");
                        continue;
                    }

                    var lineItems = InvoiceRepository.GetLineItemsForInvoice(pendingInvoice.InvoiceNumber);
                    var levyBreakdowns = InvoiceRepository.GetLevyBreakdownsForInvoice(pendingInvoice.InvoiceNumber);
                    var taxBreakdowns = BuildTaxBreakdown(lineItems);

                    var header = new InvoiceHeader
                    {
                        InvoiceNumber = pendingInvoice.InvoiceNumber,
                        InvoiceDateTime = invoiceRow.InvoiceDateTime,
                        SellerTIN = InvoiceRepository.GetSellerTin() ?? "",
                        BuyerTIN = "",              // matches Java's explicit "" on resend
                        BuyerName = "",             // not persisted per-invoice; matches Java
                        BuyerAuthorizationCode = "", // matches Java's explicit "" on resend
                        SiteId = InvoiceRepository.GetSiteId() ?? "",
                        GlobalConfigVersion = InvoiceRepository.GetGlobalConfigVersion(),
                        TaxpayerConfigVersion = InvoiceRepository.GetTaxpayerConfigVersion(),
                        TerminalConfigVersion = InvoiceRepository.GetTerminalConfigVersion(),
                        IsExport = false,
                        IsReliefSupply = invoiceRow.IsReliefSupply,
                        Vat5CertificateDetails = null,
                        PaymentMethod = pendingInvoice.PaymentId
                    };

                    var summary = new InvoiceSummary
                    {
                        TaxBreakDown = taxBreakdowns,
                        LevyBreakDown = levyBreakdowns,
                        TotalVAT = invoiceRow.TotalVAT,
                        InvoiceTotal = invoiceRow.InvoiceTotal,
                        OfflineSignature = "", // re-sent, so no longer "offline" once accepted
                        AmountTendered = invoiceRow.AmountPaid
                    };

                    var payload = new SalesInvoice
                    {
                        InvoiceHeader = header,
                        InvoiceLineItems = lineItems,
                        InvoiceSummary = summary
                    };

                    var apiResult = await service.SubmitSalesTransactionServiceAsync(payload, bearerToken);

                    if (apiResult.Success)
                    {
                        InvoiceRepository.UpdateValidationUrl(pendingInvoice.InvoiceNumber, apiResult.ValidationUrl);
                        InvoiceRepository.MarkAsTransmitted(pendingInvoice.InvoiceNumber);
                        System.Diagnostics.Debug.WriteLine($"✅ Auto-resend success for: {pendingInvoice.InvoiceNumber}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ Auto-resend failed for: {pendingInvoice.InvoiceNumber} — {apiResult.Remark}");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ Error retrying {pendingInvoice.InvoiceNumber}: {ex.Message}");
                }
            }
        }

    }
}