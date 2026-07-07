using System;
using System.Collections.Generic;
using FiscalReceiptParser.Data;
using Microsoft.Data.Sqlite;
using FiscalReceiptParser.Models;

namespace FiscalReceiptParser.Services
{
    public static class InvoiceRepository
    {
        public static string? GetSellerTin()
        {
            using var conn = Database.ConnOpen();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT TIN FROM TaxpayerConfiguration LIMIT 1";
            return cmd.ExecuteScalar() as string;
        }

        public static string? GetSiteId()
        {
            using var conn = Database.ConnOpen();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT SiteId FROM TerminalSites LIMIT 1";
            return cmd.ExecuteScalar() as string;
        }

        public static int GetGlobalConfigVersion()
        {
            using var conn = Database.ConnOpen();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT VersionNo FROM GlobalConfiguration LIMIT 1";
            var result = cmd.ExecuteScalar();
            return result != null && result != DBNull.Value ? Convert.ToInt32(result) : 0;
        }

        public static int GetTaxpayerConfigVersion()
        {
            using var conn = Database.ConnOpen();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT VersionNo FROM TaxpayerConfiguration LIMIT 1";
            var result = cmd.ExecuteScalar();
            return result != null && result != DBNull.Value ? Convert.ToInt32(result) : 0;
        }

        public static int GetTerminalConfigVersion()
        {
            using var conn = Database.ConnOpen();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT VersionNo FROM TerminalConfiguration WHERE Id = 1";
            var result = cmd.ExecuteScalar();
            return result != null && result != DBNull.Value ? Convert.ToInt32(result) : 0;
        }

        /// <summary>
        /// Rate (percent) for a given tax rate id, e.g. "A" -> 16.5, "B" -> 0.0.
        /// Falls back to 0 if the rate id isn't found (treated as zero-rated/exempt
        /// rather than crashing the sale).
        /// </summary>
        public static double GetTaxRatePercent(string taxRateId)
        {
            using var conn = Database.ConnOpen();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Rate FROM TaxRates WHERE Id = $id LIMIT 1";
            cmd.Parameters.AddWithValue("$id", taxRateId);
            var result = cmd.ExecuteScalar();
            return result != null && result != DBNull.Value ? Convert.ToDouble(result) : 0.0;
        }

        /// <summary>
        /// Active levies (id, chargeMode, rate) — matches Java's Helper.getActiveLevies().
        /// </summary>
        public static List<(string Id, string ChargeMode, double Rate)> GetActiveLevies()
        {
            var levies = new List<(string, string, double)>();

            using var conn = Database.ConnOpen();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, ChargeMode, Rate FROM Levies WHERE IsActive = 1";
            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                levies.Add((reader.GetString(0), reader.GetString(1), reader.GetDouble(2)));
            }

            return levies;
        }

        /// <summary>
        /// Looks up a product by description (matching ProductName first, then Description,
        /// case-insensitively) and returns its ProductCode + authoritative TaxRateId — the
        /// real registered rate id from MRA's product sync, not whatever the receipt parser
        /// guessed from the source document's printed tax line.
        /// </summary>

        public static ProductLookupResult GetProductInfoByDescription(string description)
        {
            using var conn = Database.ConnOpen();

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT ProductCode, TaxRateId, IsProduct FROM Products WHERE ProductName = $desc COLLATE NOCASE LIMIT 1";
                cmd.Parameters.AddWithValue("$desc", description);
                using var reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    return new ProductLookupResult
                    {
                        ProductCode = reader.IsDBNull(0) ? "" : reader.GetString(0),
                        TaxRateId = reader.IsDBNull(1) ? "" : reader.GetString(1),
                        IsProduct = !reader.IsDBNull(2) && reader.GetInt64(2) == 1,
                        Found = true
                    };
                }
            }

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT ProductCode, TaxRateId, IsProduct FROM Products WHERE Description = $desc COLLATE NOCASE LIMIT 1";
                cmd.Parameters.AddWithValue("$desc", description);
                using var reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    return new ProductLookupResult
                    {
                        ProductCode = reader.IsDBNull(0) ? "" : reader.GetString(0),
                        TaxRateId = reader.IsDBNull(1) ? "" : reader.GetString(1),
                        IsProduct = !reader.IsDBNull(2) && reader.GetInt64(2) == 1,
                        Found = true
                    };
                }
            }

            System.Diagnostics.Debug.WriteLine($"⚠️ No matching product found for description: {description}");
            return new ProductLookupResult { Found = false };
        }

        /// <summary>
        /// Saves the transaction locally BEFORE transmitting — matches Java's
        /// Helper.saveTransaction, called regardless of online/offline outcome.
        /// State: 0 = not yet transmitted, 1 = transmitted (set later via MarkAsTransmitted).
        /// </summary>
        public static bool SaveTransaction(
            InvoiceHeader header,
            string sourceInvoiceNumber,   // NEW
            IEnumerable<LineItemDto> lineItems,
            IEnumerable<TaxBreakDown> taxBreakdowns,
            IEnumerable<LevyBreakDown> levyBreakdowns,
            double invoiceTotal,
            double totalVat,
            string offlineSignature,
            string validationUrl,
            bool isTransmitted,
            string paymentId,
            double amountTendered)
        {
            try
            {
                using var conn = Database.ConnOpen();
                using var transaction = conn.BeginTransaction();

                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = transaction;
                    cmd.CommandText = @"
                        INSERT OR REPLACE INTO Invoices
                            (InvoiceNumber, SourceInvoiceNumber, InvoiceDateTime, InvoiceTotal, SellerTin, BuyerTin,
                             TotalVAT, OfflineTransactionSignature, SiteId, ValidationUrl,
                             IsReliefSupply, State, PaymentId, AmountPaid)
                        VALUES
                            ($invoiceNumber, $sourceInvoiceNumber, $invoiceDateTime, $invoiceTotal, $sellerTin, $buyerTin,
                             $totalVat, $offlineSignature, $siteId, $validationUrl,
                             $isReliefSupply, $state, $paymentId, $amountPaid)";

                    cmd.Parameters.AddWithValue("$invoiceNumber", header.InvoiceNumber);
                    cmd.Parameters.AddWithValue("$sourceInvoiceNumber",
                    string.IsNullOrEmpty(sourceInvoiceNumber) ? (object)DBNull.Value : sourceInvoiceNumber); cmd.Parameters.AddWithValue("$invoiceDateTime", header.InvoiceDateTime.ToString("O"));
                    cmd.Parameters.AddWithValue("$invoiceTotal", invoiceTotal);
                    cmd.Parameters.AddWithValue("$sellerTin", header.SellerTIN ?? "");
                    cmd.Parameters.AddWithValue("$buyerTin", header.BuyerTIN ?? "");
                    cmd.Parameters.AddWithValue("$totalVat", totalVat);
                    cmd.Parameters.AddWithValue("$offlineSignature", offlineSignature ?? "");
                    cmd.Parameters.AddWithValue("$siteId", header.SiteId ?? "");
                    cmd.Parameters.AddWithValue("$validationUrl", validationUrl ?? "");
                    cmd.Parameters.AddWithValue("$isReliefSupply", header.IsReliefSupply ? 1 : 0);
                    cmd.Parameters.AddWithValue("$state", isTransmitted ? 1 : 0);
                    cmd.Parameters.AddWithValue("$paymentId", paymentId ?? "");
                    cmd.Parameters.AddWithValue("$amountPaid", amountTendered);

                    cmd.ExecuteNonQuery();
                }

                foreach (var item in lineItems)
                {
                    using var cmd = conn.CreateCommand();
                    cmd.Transaction = transaction;
                    cmd.CommandText = @"
                        INSERT INTO LineItems
                            (InvoiceNumber, ProductCode, Description, Quantity, TaxRateID,
                             Discount, UnitPrice, TotalPrice, DiscountAmount, VATRate,
                             IsProduct, VATAmount)
                        VALUES
                            ($invoiceNumber, $productCode, $description, $quantity, $taxRateId,
                             $discount, $unitPrice, $totalPrice, $discountAmount, $vatRate,
                             $isProduct, $vatAmount)";

                    double rate = GetTaxRatePercent(item.TaxRateId ?? "");

                    cmd.Parameters.AddWithValue("$invoiceNumber", header.InvoiceNumber);
                    cmd.Parameters.AddWithValue("$productCode", item.ProductCode ?? "");
                    cmd.Parameters.AddWithValue("$description", item.Description ?? "");
                    cmd.Parameters.AddWithValue("$quantity", item.Quantity);
                    cmd.Parameters.AddWithValue("$taxRateId", item.TaxRateId ?? "");
                    cmd.Parameters.AddWithValue("$discount", item.Discount ?? 0);
                    cmd.Parameters.AddWithValue("$unitPrice", item.UnitPrice);
                    cmd.Parameters.AddWithValue("$totalPrice", item.Total);
                    cmd.Parameters.AddWithValue("$discountAmount", item.Discount ?? 0);
                    cmd.Parameters.AddWithValue("$vatRate", rate);
                    cmd.Parameters.AddWithValue("$isProduct", item.IsProduct ? 1 : 0);
                    cmd.Parameters.AddWithValue("$vatAmount", item.TotalVAT);

                    cmd.ExecuteNonQuery();
                }

                foreach (var tax in taxBreakdowns)
                {
                    using var cmd = conn.CreateCommand();
                    cmd.Transaction = transaction;
                    cmd.CommandText = @"
                        INSERT INTO InvoiceTaxBreakDown (InvoiceNumber, RateID, TaxableAmount, TaxAmount)
                        VALUES ($invoiceNumber, $rateId, $taxableAmount, $taxAmount)";

                    cmd.Parameters.AddWithValue("$invoiceNumber", header.InvoiceNumber);
                    cmd.Parameters.AddWithValue("$rateId", tax.RateId ?? "");
                    cmd.Parameters.AddWithValue("$taxableAmount", tax.TaxableAmount);
                    cmd.Parameters.AddWithValue("$taxAmount", tax.TaxAmount);

                    cmd.ExecuteNonQuery();
                }

                foreach (var levy in levyBreakdowns)
                {
                    using var cmd = conn.CreateCommand();
                    cmd.Transaction = transaction;
                    cmd.CommandText = @"
                        INSERT INTO InvoiceLevies (InvoiceNumber, LevyId, LevyAmount)
                        VALUES ($invoiceNumber, $levyId, $levyAmount)";

                    cmd.Parameters.AddWithValue("$invoiceNumber", header.InvoiceNumber);
                    cmd.Parameters.AddWithValue("$levyId", levy.LevyTypeId ?? "");
                    cmd.Parameters.AddWithValue("$levyAmount", levy.LevyAmount);

                    cmd.ExecuteNonQuery();
                }

                transaction.Commit();
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save transaction locally: {ex.Message}");
                return false;
            }
        }

        public static void MarkAsTransmitted(string invoiceNumber)
        {
            using var conn = Database.ConnOpen();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Invoices SET State = 1 WHERE InvoiceNumber = $invoiceNumber";
            cmd.Parameters.AddWithValue("$invoiceNumber", invoiceNumber);
            cmd.ExecuteNonQuery();
        }

        public static void UpdateValidationUrl(string invoiceNumber, string validationUrl)
        {
            using var conn = Database.ConnOpen();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Invoices SET ValidationUrl = $url WHERE InvoiceNumber = $invoiceNumber";
            cmd.Parameters.AddWithValue("$url", validationUrl ?? "");
            cmd.Parameters.AddWithValue("$invoiceNumber", invoiceNumber);
            cmd.ExecuteNonQuery();
        }

        public static void UpdateOfflineTransactionDetails(string invoiceNumber, string validationUrl, string offlineSignature)
        {
            using var conn = Database.ConnOpen();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE Invoices
                SET ValidationUrl = $url, OfflineTransactionSignature = $sig
                WHERE InvoiceNumber = $invoiceNumber";
            cmd.Parameters.AddWithValue("$url", validationUrl ?? "");
            cmd.Parameters.AddWithValue("$sig", offlineSignature ?? "");
            cmd.Parameters.AddWithValue("$invoiceNumber", invoiceNumber);
            cmd.ExecuteNonQuery();
        }

        public static TerminalContactInfo GetTerminalContactInfo()
        {
            using var conn = Database.ConnOpen();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Email, AddressLine, Phone FROM TerminalConfiguration WHERE Id = 1";
            using var reader = cmd.ExecuteReader();

            if (reader.Read())
            {
                return new TerminalContactInfo
                {
                    Email = reader.IsDBNull(0) ? "" : reader.GetString(0),
                    AddressLine = reader.IsDBNull(1) ? "" : reader.GetString(1),
                    Phone = reader.IsDBNull(2) ? "" : reader.GetString(2)
                };
            }
            return new TerminalContactInfo();
        }

        public static string GetTradingName()
        {
            using var conn = Database.ConnOpen();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT TradingName FROM TerminalConfiguration WHERE Id = 1";
            return cmd.ExecuteScalar() as string ?? "";
        }

        public static bool IsVatRegistered()
        {
            using var conn = Database.ConnOpen();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT IsVATRegistered FROM TaxpayerConfiguration LIMIT 1";
            var result = cmd.ExecuteScalar();
            return result != null && result != DBNull.Value && Convert.ToInt64(result) == 1;
        }

        public static string GetLevyNameById(string levyId)
        {
            using var conn = Database.ConnOpen();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Name FROM Levies WHERE Id = $id LIMIT 1";
            cmd.Parameters.AddWithValue("$id", levyId);
            return cmd.ExecuteScalar() as string ?? levyId;
        }

        public static List<PendingInvoice> GetPendingInvoiceNumbers()
        {
            var pending = new List<PendingInvoice>();

            using var conn = Database.ConnOpen();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT InvoiceNumber, PaymentId FROM Invoices WHERE State = 0";
            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                pending.Add(new PendingInvoice
                {
                    InvoiceNumber = reader.GetString(0),
                    PaymentId = reader.IsDBNull(1) ? "" : reader.GetString(1)
                });
            }

            return pending;
        }

        public class InvoiceRow
        {
            public DateTime InvoiceDateTime { get; set; }
            public bool IsReliefSupply { get; set; }
            public double TotalVAT { get; set; }
            public double InvoiceTotal { get; set; }
            public double AmountPaid { get; set; }
        }

        public static InvoiceRow? GetInvoiceRow(string invoiceNumber)
        {
            using var conn = Database.ConnOpen();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
        SELECT InvoiceDateTime, IsReliefSupply, TotalVAT, InvoiceTotal, AmountPaid
        FROM Invoices WHERE InvoiceNumber = $invoiceNumber";
            cmd.Parameters.AddWithValue("$invoiceNumber", invoiceNumber);
            using var reader = cmd.ExecuteReader();

            if (reader.Read())
            {
                return new InvoiceRow
                {
                    InvoiceDateTime = DateTime.Parse(reader.GetString(0)),
                    IsReliefSupply = !reader.IsDBNull(1) && reader.GetInt64(1) == 1,
                    TotalVAT = reader.IsDBNull(2) ? 0 : reader.GetDouble(2),
                    InvoiceTotal = reader.IsDBNull(3) ? 0 : reader.GetDouble(3),
                    AmountPaid = reader.IsDBNull(4) ? 0 : reader.GetDouble(4)
                };
            }
            return null;
        }

        public static List<LineItemDto> GetLineItemsForInvoice(string invoiceNumber)
        {
            var items = new List<LineItemDto>();

            using var conn = Database.ConnOpen();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
        SELECT Id, ProductCode, Description, UnitPrice, Quantity, Discount,
               TotalPrice, VATAmount, TaxRateID, IsProduct
        FROM LineItems WHERE InvoiceNumber = $invoiceNumber";
            cmd.Parameters.AddWithValue("$invoiceNumber", invoiceNumber);
            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                items.Add(new LineItemDto
                {
                    Id = (int)reader.GetInt64(0),
                    ProductCode = reader.IsDBNull(1) ? "" : reader.GetString(1),
                    Description = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    UnitPrice = reader.GetDouble(3),
                    Quantity = reader.GetDouble(4),
                    Discount = reader.IsDBNull(5) ? 0 : reader.GetDouble(5),
                    Total = reader.GetDouble(6),
                    TotalVAT = reader.GetDouble(7),
                    TaxRateId = reader.IsDBNull(8) ? "" : reader.GetString(8),
                    IsProduct = !reader.IsDBNull(9) && reader.GetInt64(9) == 1
                });
            }

            return items;
        }

        /// <summary>
        /// Matches Java's Helper.getLevyBreakdowns — read the stored levies as-is
        /// (not regenerated), rounded to 2dp exactly like the Java retry loop does.
        /// </summary>
        public static List<LevyBreakDown> GetLevyBreakdownsForInvoice(string invoiceNumber)
        {
            var levies = new List<LevyBreakDown>();

            using var conn = Database.ConnOpen();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT LevyId, LevyAmount FROM InvoiceLevies WHERE InvoiceNumber = $invoiceNumber";
            cmd.Parameters.AddWithValue("$invoiceNumber", invoiceNumber);
            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                levies.Add(new LevyBreakDown
                {
                    LevyTypeId = reader.GetString(0),
                    LevyAmount = Math.Round(reader.GetDouble(1), 2)
                });
            }

            return levies;
        }


        // ============================================================
        // Dashboard stats — read straight from Invoices so the numbers
        // survive an app restart instead of living in memory.
        // ============================================================

        /// <summary>Total invoices ever fiscalised (saved), online or offline.</summary>
        public static int GetFiscalisedCount()
        {
            using var conn = Database.ConnOpen();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM Invoices";
            var result = cmd.ExecuteScalar();
            return result != null && result != DBNull.Value ? Convert.ToInt32(result) : 0;
        }

        /// <summary>Invoices currently confirmed as transmitted to MRA (State = 1).</summary>
        public static int GetOnlineCount()
        {
            using var conn = Database.ConnOpen();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM Invoices WHERE State = 1";
            var result = cmd.ExecuteScalar();
            return result != null && result != DBNull.Value ? Convert.ToInt32(result) : 0;
        }

        /// <summary>Invoices that were signed offline at some point (have a signature on file).</summary>
        public static int GetOfflineCount()
        {
            using var conn = Database.ConnOpen();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM Invoices WHERE OfflineTransactionSignature IS NOT NULL AND OfflineTransactionSignature <> ''";
            var result = cmd.ExecuteScalar();
            return result != null && result != DBNull.Value ? Convert.ToInt32(result) : 0;
        }

        /// <summary>Invoices not yet confirmed transmitted (State = 0) — still queued to push to MRA.</summary>
        public static int GetPendingCount()
        {
            using var conn = Database.ConnOpen();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM Invoices WHERE State = 0";
            var result = cmd.ExecuteScalar();
            return result != null && result != DBNull.Value ? Convert.ToInt32(result) : 0;

        }

        public static int GetOfflineTransactionLimitHours()
        {
            using var conn = Database.ConnOpen();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT MaxTransactionAgeInHours FROM OfflineLimit LIMIT 1";
            var result = cmd.ExecuteScalar();
            return result != null && result != DBNull.Value ? Convert.ToInt32(result) : 0;
        }

        /// <summary>
        /// Matches Java's getLastSuccessfulSyncTimeFromInvoices — despite the method name,
        /// this actually returns the EARLIEST still-pending (State = 0) invoice's timestamp,
        /// used as the starting point for "how long has this terminal been offline".
        /// </summary>
        public static DateTime? GetEarliestPendingInvoiceDateTime()
        {
            using var conn = Database.ConnOpen();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT MIN(InvoiceDateTime) FROM Invoices WHERE State = 0";
            var result = cmd.ExecuteScalar();

            if (result == null || result == DBNull.Value) return null;

            string? str = result as string;
            if (string.IsNullOrEmpty(str)) return null;

            if (DateTime.TryParse(str, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
                return dt;

            System.Diagnostics.Debug.WriteLine($"❌ Failed to parse InvoiceDateTime: {str}");
            return null;
        }

        /// <summary>
        /// Looks up a previously saved invoice by the SOURCE receipt's own number
        /// (e.g. "T1-S3" from the till slip) — used to detect reprints/duplicate
        /// PDF drops before generating a new MRA invoice number and resubmitting.
        /// </summary>
        public class ExistingInvoiceLookup
        {
            public string InvoiceNumber { get; set; } = "";
            public string ValidationUrl { get; set; } = "";
            public int State { get; set; } // 0 = pending, 1 = transmitted
        }

        public static ExistingInvoiceLookup? FindBySourceInvoiceNumber(string sourceInvoiceNumber)
        {
            if (string.IsNullOrWhiteSpace(sourceInvoiceNumber)) return null;

            using var conn = Database.ConnOpen();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
        SELECT InvoiceNumber, ValidationUrl, State
        FROM Invoices
        WHERE SourceInvoiceNumber = $sourceInvoiceNumber
        LIMIT 1";
            cmd.Parameters.AddWithValue("$sourceInvoiceNumber", sourceInvoiceNumber);
            using var reader = cmd.ExecuteReader();

            if (reader.Read())
            {
                return new ExistingInvoiceLookup
                {
                    InvoiceNumber = reader.GetString(0),
                    ValidationUrl = reader.IsDBNull(1) ? "" : reader.GetString(1),
                    State = reader.IsDBNull(2) ? 0 : (int)reader.GetInt64(2)
                };
            }
            return null;
        }

        /// <summary>
        /// Validates stock for every product line item and, only if everything is
        /// sufficient, decrements Products.Quantity in the same transaction — so the
        /// check-then-decrement is atomic against a second sale racing in on the same
        /// product between the check and the write. If ANY item is short, nothing is
        /// decremented at all (all-or-nothing for the whole sale).
        ///
        /// Service line items (IsProduct == false) and items with no resolved
        /// ProductCode (unmatched against the local catalog) are skipped — there's
        /// nothing to validate/decrement stock against for those.
        /// </summary>
        public static StockValidationResult ValidateAndReserveStock(List<LineItemDto> lineItems)
        {
            var result = new StockValidationResult();

            using var conn = Database.ConnOpen();
            using var transaction = conn.BeginTransaction();

            try
            {
                // Group by ProductCode in case the same product appears as multiple line items.
                var neededByProduct = lineItems
                    .Where(i => i.IsProduct && !string.IsNullOrEmpty(i.ProductCode))
                    .GroupBy(i => i.ProductCode)
                    .ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity));

                var currentQuantities = new Dictionary<string, double>();
                var productNames = new Dictionary<string, string>();

                foreach (var productCode in neededByProduct.Keys)
                {
                    using var cmd = conn.CreateCommand();
                    cmd.Transaction = transaction;
                    cmd.CommandText = "SELECT Quantity, ProductName FROM Products WHERE ProductCode = $code";
                    cmd.Parameters.AddWithValue("$code", productCode);
                    using var reader = cmd.ExecuteReader();

                    if (reader.Read())
                    {
                        currentQuantities[productCode] = reader.GetDouble(0);
                        productNames[productCode] = reader.IsDBNull(1) ? productCode : reader.GetString(1);
                    }
                    // If not found: can't validate against a record that isn't there —
                    // skip rather than block, same reasoning as the description-match
                    // fallback elsewhere.
                }

                foreach (var (productCode, neededQty) in neededByProduct)
                {
                    if (!currentQuantities.TryGetValue(productCode, out var available)) continue;

                    if (available < neededQty)
                    {
                        string name = productNames.TryGetValue(productCode, out var n) ? n : productCode;
                        result.IsSufficient = false;
                        result.InsufficientItems.Add($"{name} (available: {available}, requested: {neededQty})");
                    }
                }

                if (!result.IsSufficient)
                {
                    transaction.Rollback();
                    return result;
                }

                foreach (var (productCode, neededQty) in neededByProduct)
                {
                    using var cmd = conn.CreateCommand();
                    cmd.Transaction = transaction;
                    cmd.CommandText = "UPDATE Products SET Quantity = Quantity - $qty WHERE ProductCode = $code";
                    cmd.Parameters.AddWithValue("$qty", neededQty);
                    cmd.Parameters.AddWithValue("$code", productCode);
                    cmd.ExecuteNonQuery();
                }

                transaction.Commit();
                return result;
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                System.Diagnostics.Debug.WriteLine($"❌ Stock validation/decrement failed: {ex.Message}");
                result.IsSufficient = false;
                result.InsufficientItems.Add("Internal error while checking stock — transaction cancelled.");
                return result;
            }
        }
    }
}