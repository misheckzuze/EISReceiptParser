using FiscalReceiptParser.Data;
using Microsoft.Data.Sqlite;
using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace FiscalReceiptParser.Services
{
    internal static class JsonElementExtensions
    {
        /// <summary>
        /// Case-insensitive TryGetProperty. Needed because rawJson passed into
        /// InsertActivationDataAsync comes from re-serializing the NSwag C# response
        /// object (PascalCase properties), while the MRA API's actual wire JSON is
        /// camelCase — a plain TryGetProperty("Data", ...) silently misses on the
        /// wrong casing rather than throwing, which is what caused fields to be
        /// skipped without any error.
        /// </summary>
        public static bool TryGetPropertyCI(this JsonElement element, string name, out JsonElement value)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in element.EnumerateObject())
                {
                    if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        value = property.Value;
                        return true;
                    }
                }
            }
            value = default;
            return false;
        }
    }

    public static class ActivationDataInserter
    {
        // Assuming your Database helper exposes a factory method to create an open connection
        // e.g., public static SqliteConnection CreateConnection()

        public static async Task InsertActivationDataAsync(string responseBody)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(responseBody);
                JsonElement root = doc.RootElement;

                if (!root.TryGetPropertyCI("Data", out JsonElement data) || data.ValueKind == JsonValueKind.Null)
                    return;

                using var conn = Database.CreateConnection();
                await conn.OpenAsync();

                data.TryGetPropertyCI("Configuration", out var configuration);

                // 1. === TerminalSite ===
                if (configuration.ValueKind == JsonValueKind.Object &&
                    configuration.TryGetPropertyCI("TerminalConfiguration", out var termConfigForSite) &&
                    termConfigForSite.TryGetPropertyCI("TerminalSite", out var terminalSite))
                {
                    string siteId = terminalSite.TryGetPropertyCI("SiteId", out var sId) ? sId.GetString() ?? "" : "";
                    string siteName = terminalSite.TryGetPropertyCI("SiteName", out var sName) ? sName.GetString() ?? "" : "";

                    string insertSiteSQL = "INSERT OR REPLACE INTO TerminalSites (SiteId, Name, Location) VALUES ($siteId, $name, $location)";
                    using var stmt = new SqliteCommand(insertSiteSQL, conn);
                    stmt.Parameters.AddWithValue("$siteId", siteId);
                    stmt.Parameters.AddWithValue("$name", siteName);
                    stmt.Parameters.AddWithValue("$location", "");
                    await stmt.ExecuteNonQueryAsync();
                    Console.WriteLine($"Inserted TerminalSite: {siteId}");
                }

                // 2. === GlobalConfiguration ===
                if (configuration.ValueKind == JsonValueKind.Object &&
                    configuration.TryGetPropertyCI("GlobalConfiguration", out var globalConfig))
                {
                    int globalId = globalConfig.TryGetPropertyCI("Id", out var gId) ? gId.GetInt32() : 0;
                    int globalVersionNo = globalConfig.TryGetPropertyCI("VersionNo", out var gv) ? gv.GetInt32() : 0;

                    string insertGlobalSQL = "INSERT OR REPLACE INTO GlobalConfiguration (Id, VersionNo) VALUES ($id, $versionNo)";
                    using var stmt = new SqliteCommand(insertGlobalSQL, conn);
                    stmt.Parameters.AddWithValue("$id", globalId);
                    stmt.Parameters.AddWithValue("$versionNo", globalVersionNo);
                    await stmt.ExecuteNonQueryAsync();
                    Console.WriteLine("Inserted GlobalConfiguration");

                    // 3. === TaxRates ===
                    if (globalConfig.TryGetPropertyCI("Taxrates", out var taxRates) && taxRates.ValueKind == JsonValueKind.Array)
                    {
                        string insertTaxSQL = "INSERT OR REPLACE INTO TaxRates (Id, Name, ChargeMode, Ordinal, Rate) VALUES ($id, $name, $chargeMode, $ordinal, $rate)";

                        foreach (JsonElement rateObj in taxRates.EnumerateArray())
                        {
                            using var stmtTax = new SqliteCommand(insertTaxSQL, conn);
                            stmtTax.Parameters.AddWithValue("$id", rateObj.TryGetPropertyCI("Id", out var rid) ? rid.GetString() ?? "" : "");
                            stmtTax.Parameters.AddWithValue("$name", rateObj.TryGetPropertyCI("Name", out var rname) ? rname.GetString() ?? "" : "");
                            stmtTax.Parameters.AddWithValue("$chargeMode", rateObj.TryGetPropertyCI("ChargeMode", out var rmode) ? rmode.GetString() ?? "" : "");
                            stmtTax.Parameters.AddWithValue("$ordinal", rateObj.TryGetPropertyCI("Ordinal", out var rord) ? rord.GetInt32() : 0);
                            stmtTax.Parameters.AddWithValue("$rate", rateObj.TryGetPropertyCI("Rate", out var rrate) ? rrate.GetDouble() : 0);
                            await stmtTax.ExecuteNonQueryAsync();
                        }
                        Console.WriteLine("✅ Inserted TaxRates");
                    }
                }

                // 4. === Activated Levies ===
                JsonElement taxpayerConfig = default;
                bool hasTaxpayerConfig = configuration.ValueKind == JsonValueKind.Object &&
                    configuration.TryGetPropertyCI("TaxpayerConfiguration", out taxpayerConfig);

                if (hasTaxpayerConfig &&
                    taxpayerConfig.TryGetPropertyCI("ActivatedLevies", out var activatedLevies) &&
                    activatedLevies.ValueKind == JsonValueKind.Array)
                {
                    string insertLevySQL = "INSERT OR REPLACE INTO Levies (Id, Name, ChargeMode, Rate, IsActive) VALUES ($id, $name, $chargeMode, $rate, $isActive)";

                    foreach (JsonElement levyObj in activatedLevies.EnumerateArray())
                    {
                        using var stmt = new SqliteCommand(insertLevySQL, conn);
                        stmt.Parameters.AddWithValue("$id", levyObj.TryGetPropertyCI("Id", out var lid) ? lid.GetString() ?? "" : "");
                        stmt.Parameters.AddWithValue("$name", levyObj.TryGetPropertyCI("Name", out var lname) ? lname.GetString() ?? "" : "");
                        stmt.Parameters.AddWithValue("$chargeMode", levyObj.TryGetPropertyCI("ChargeMode", out var lmode) ? lmode.GetString() ?? "" : "");
                        stmt.Parameters.AddWithValue("$rate", levyObj.TryGetPropertyCI("Rate", out var lrate) ? lrate.GetDouble() : 0);
                        stmt.Parameters.AddWithValue("$isActive", levyObj.TryGetPropertyCI("IsActive", out var lact) && lact.GetBoolean() ? 1 : 0);
                        await stmt.ExecuteNonQueryAsync();
                    }
                    Console.WriteLine("Inserted/Updated Levies");
                }

                // 5. === ActivatedTerminal ===
                if (data.TryGetPropertyCI("ActivatedTerminal", out var activatedTerminal))
                {
                    string terminalId = activatedTerminal.TryGetPropertyCI("TerminalId", out var tId) ? tId.GetString() ?? "" : "";
                    int terminalPosition = activatedTerminal.TryGetPropertyCI("TerminalPosition", out var tPos) ? tPos.GetInt32() : 0;
                    int taxpayerId = activatedTerminal.TryGetPropertyCI("TaxpayerId", out var tpId) ? tpId.GetInt32() : 0;
                    string activationDate = activatedTerminal.TryGetPropertyCI("ActivationDate", out var aDate) ? aDate.GetString() ?? "" : "";

                    string jwtToken = "", secretKey = "";
                    if (activatedTerminal.TryGetPropertyCI("TerminalCredentials", out var credentials))
                    {
                        jwtToken = credentials.TryGetPropertyCI("JwtToken", out var jwt) ? jwt.GetString() ?? "" : "";
                        secretKey = credentials.TryGetPropertyCI("SecretKey", out var sec) ? sec.GetString() ?? "" : "";
                    }

                    string insertTerminalSQL = "INSERT OR REPLACE INTO ActivatedTerminal (TerminalId, TerminalPosition, TaxpayerId, ActivationDate, JwtToken, SecretKey) VALUES ($tId, $tPos, $tpId, $actDate, $jwt, $secret)";
                    using var stmt = new SqliteCommand(insertTerminalSQL, conn);
                    stmt.Parameters.AddWithValue("$tId", terminalId);
                    stmt.Parameters.AddWithValue("$tPos", terminalPosition);
                    stmt.Parameters.AddWithValue("$tpId", taxpayerId);
                    stmt.Parameters.AddWithValue("$actDate", activationDate);
                    stmt.Parameters.AddWithValue("$jwt", jwtToken);
                    stmt.Parameters.AddWithValue("$secret", secretKey);
                    await stmt.ExecuteNonQueryAsync();
                    Console.WriteLine("Inserted ActivatedTerminal");

                    // 6. === TerminalConfiguration ===
                    if (configuration.ValueKind == JsonValueKind.Object &&
                        configuration.TryGetPropertyCI("TerminalConfiguration", out var termConfig))
                    {
                        string terminalLabel = termConfig.TryGetPropertyCI("TerminalLabel", out var tLabel) ? tLabel.GetString() ?? "" : "";
                        bool isActiveTerminal = termConfig.TryGetPropertyCI("IsActiveTerminal", out var act) && act.GetBoolean();
                        string email = termConfig.TryGetPropertyCI("EmailAddress", out var em) ? em.GetString() ?? "" : "";
                        string phone = termConfig.TryGetPropertyCI("PhoneNumber", out var ph) ? ph.GetString() ?? "" : "";
                        string tradingName = termConfig.TryGetPropertyCI("TradingName", out var tName) ? tName.GetString() ?? "" : "";

                        string addressLine = "";
                        if (termConfig.TryGetPropertyCI("AddressLines", out var addrLines) && addrLines.ValueKind == JsonValueKind.Array && addrLines.GetArrayLength() > 0)
                        {
                            addressLine = addrLines[0].GetString() ?? "";
                        }
                        int versionNo = termConfig.TryGetPropertyCI("VersionNo", out var vNo) ? vNo.GetInt32() : 0;

                        string insertConfigSQL = "INSERT OR REPLACE INTO TerminalConfiguration (TerminalId, Label, IsActive, Email, Phone, VersionNo, TradingName, AddressLine) VALUES ($tId, $label, $isActive, $email, $phone, $version, $trading, $address)";
                        using var stmtConfig = new SqliteCommand(insertConfigSQL, conn);
                        stmtConfig.Parameters.AddWithValue("$tId", terminalId);
                        stmtConfig.Parameters.AddWithValue("$label", terminalLabel);
                        stmtConfig.Parameters.AddWithValue("$isActive", isActiveTerminal ? 1 : 0);
                        stmtConfig.Parameters.AddWithValue("$email", email);
                        stmtConfig.Parameters.AddWithValue("$phone", phone);
                        stmtConfig.Parameters.AddWithValue("$version", versionNo);
                        stmtConfig.Parameters.AddWithValue("$trading", tradingName);
                        stmtConfig.Parameters.AddWithValue("$address", addressLine);
                        await stmtConfig.ExecuteNonQueryAsync();
                        Console.WriteLine("Inserted TerminalConfiguration");

                        // 7. === OfflineLimit ===
                        if (termConfig.TryGetPropertyCI("OfflineLimit", out var offlineLimit))
                        {
                            int maxAge = offlineLimit.TryGetPropertyCI("MaxTransactionAgeInHours", out var ma) ? ma.GetInt32() : 0;
                            double maxAmount = offlineLimit.TryGetPropertyCI("MaxCummulativeAmount", out var mam) ? mam.GetDouble() : 0;

                            string insertLimitSQL = "INSERT OR REPLACE INTO OfflineLimit (TerminalId, MaxTransactionAgeInHours, MaxCummulativeAmount) VALUES ($tId, $maxAge, $maxAmt)";
                            using var stmtLimit = new SqliteCommand(insertLimitSQL, conn);
                            stmtLimit.Parameters.AddWithValue("$tId", terminalId);
                            stmtLimit.Parameters.AddWithValue("$maxAge", maxAge);
                            stmtLimit.Parameters.AddWithValue("$maxAmt", maxAmount);
                            await stmtLimit.ExecuteNonQueryAsync();
                            Console.WriteLine("Inserted OfflineLimit");
                        }
                    }

                    // 8. === TaxpayerConfiguration & TaxOffice ===
                    if (hasTaxpayerConfig)
                    {
                        string tin = taxpayerConfig.TryGetPropertyCI("Tin", out var valTin) ? valTin.GetString() ?? "" : "";
                        bool isVATRegistered = taxpayerConfig.TryGetPropertyCI("IsVATRegistered", out var valVat) && valVat.GetBoolean();
                        string taxOfficeCode = taxpayerConfig.TryGetPropertyCI("TaxOfficeCode", out var valCode) ? valCode.GetString() ?? "" : "";
                        int taxpayerVersionNo = taxpayerConfig.TryGetPropertyCI("VersionNo", out var valVer) ? valVer.GetInt32() : 0;

                        string officeName = "";
                        if (taxpayerConfig.TryGetPropertyCI("TaxOffice", out var taxOffice))
                        {
                            officeName = taxOffice.TryGetPropertyCI("Name", out var oName) ? oName.GetString() ?? "" : "";
                        }

                        string insertTaxpayerSQL = "INSERT OR REPLACE INTO TaxpayerConfiguration (TaxpayerId, TIN, IsVATRegistered, VersionNo, TaxOfficeCode) VALUES ($tpId, $tin, $vat, $version, $officeCode)";
                        using var stmtTaxpayer = new SqliteCommand(insertTaxpayerSQL, conn);
                        stmtTaxpayer.Parameters.AddWithValue("$tpId", taxpayerId);
                        stmtTaxpayer.Parameters.AddWithValue("$tin", tin);
                        stmtTaxpayer.Parameters.AddWithValue("$vat", isVATRegistered ? 1 : 0);
                        stmtTaxpayer.Parameters.AddWithValue("$version", taxpayerVersionNo);
                        stmtTaxpayer.Parameters.AddWithValue("$officeCode", taxOfficeCode);
                        await stmtTaxpayer.ExecuteNonQueryAsync();
                        Console.WriteLine("Inserted TaxpayerConfiguration");

                        string insertOfficeSQL = "INSERT OR REPLACE INTO TaxOffices (Code, Name) VALUES ($code, $name)";
                        using var stmtOffice = new SqliteCommand(insertOfficeSQL, conn);
                        stmtOffice.Parameters.AddWithValue("$code", taxOfficeCode);
                        stmtOffice.Parameters.AddWithValue("$name", officeName);
                        await stmtOffice.ExecuteNonQueryAsync();
                        Console.WriteLine("Inserted TaxOffice");
                    }
                }
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"Failed to insert activation data: {e.Message}");
            }
        }

        public static async Task InsertActivationCodeAsync(string activationCode)
        {
            try
            {
                using var conn = Database.CreateConnection();
                await conn.OpenAsync();

                string insertSQL = "INSERT OR REPLACE INTO ActivationCode (ActivationCode) VALUES ($code)";
                using var stmt = new SqliteCommand(insertSQL, conn);
                stmt.Parameters.AddWithValue("$code", activationCode);
                await stmt.ExecuteNonQueryAsync();
                Console.WriteLine($"Inserted ActivationCode: {activationCode}");
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"Failed to insert into ActivationCode table: {e.Message}");
            }
        }

        public static async Task GetLatestConfigurationAsync(string responseBody)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(responseBody);
                JsonElement root = doc.RootElement;

                if (!root.TryGetProperty("data", out JsonElement data) || data.ValueKind == JsonValueKind.Null)
                {
                    Console.WriteLine("No data object found in response");
                    return;
                }

                using var conn = Database.CreateConnection();
                await conn.OpenAsync();

                // === TerminalSite ===
                if (data.TryGetProperty("terminalConfiguration", out var termConfig) &&
                    termConfig.TryGetProperty("terminalSite", out var terminalSite))
                {
                    string siteId = terminalSite.GetProperty("siteId").GetString() ?? "";
                    string siteName = terminalSite.GetProperty("siteName").GetString() ?? "";

                    string insertSiteSQL = "INSERT OR REPLACE INTO TerminalSites (SiteId, Name, Location) VALUES ($id, $name, $loc)";
                    using var stmt = new SqliteCommand(insertSiteSQL, conn);
                    stmt.Parameters.AddWithValue("$id", siteId);
                    stmt.Parameters.AddWithValue("$name", siteName);
                    stmt.Parameters.AddWithValue("$loc", "");
                    await stmt.ExecuteNonQueryAsync();
                    Console.WriteLine($"Inserted TerminalSite: {siteId}");
                }

                // === GlobalConfiguration & TaxRates ===
                string tin = "";
                if (data.TryGetProperty("taxpayerConfiguration", out var taxpayerConfig))
                {
                    tin = taxpayerConfig.TryGetProperty("tin", out var t) ? t.GetString() ?? "" : "";
                }

                if (data.TryGetProperty("globalConfiguration", out var globalConfig))
                {
                    int globalId = globalConfig.GetProperty("id").GetInt32();
                    int globalVersionNo = globalConfig.GetProperty("versionNo").GetInt32();

                    string insertGlobalSQL = "INSERT OR REPLACE INTO GlobalConfiguration (Id, VersionNo) VALUES ($id, $vNo)";
                    using var stmt = new SqliteCommand(insertGlobalSQL, conn);
                    stmt.Parameters.AddWithValue("$id", globalId);
                    stmt.Parameters.AddWithValue("$vNo", globalVersionNo);
                    await stmt.ExecuteNonQueryAsync();
                    Console.WriteLine("Inserted GlobalConfiguration");

                    if (globalConfig.TryGetProperty("taxrates", out var taxRates) && taxRates.ValueKind == JsonValueKind.Array)
                    {
                        string insertTaxSQL = "INSERT OR REPLACE INTO TaxRates (Id, Name, ChargeMode, Ordinal, Rate) VALUES ($id, $name, $mode, $ord, $rate)";
                        foreach (JsonElement rateObj in taxRates.EnumerateArray())
                        {
                            using var stmtTax = new SqliteCommand(insertTaxSQL, conn);
                            stmtTax.Parameters.AddWithValue("$id", rateObj.GetProperty("id").GetString() ?? "");
                            stmtTax.Parameters.AddWithValue("$name", rateObj.GetProperty("name").GetString() ?? "");
                            stmtTax.Parameters.AddWithValue("$mode", rateObj.GetProperty("chargeMode").GetString() ?? "");
                            stmtTax.Parameters.AddWithValue("$ord", rateObj.GetProperty("ordinal").GetInt32());
                            stmtTax.Parameters.AddWithValue("$rate", rateObj.GetProperty("rate").GetDouble());
                            await stmtTax.ExecuteNonQueryAsync();
                        }
                        Console.WriteLine("Inserted TaxRates");
                    }
                }

                // === TerminalConfiguration ===
                if (data.TryGetProperty("terminalConfiguration", out termConfig))
                {
                    string terminalLabel = termConfig.TryGetProperty("terminalLabel", out var lbl) ? lbl.GetString() ?? "" : "";
                    string email = termConfig.TryGetProperty("emailAddress", out var em) ? em.GetString() ?? "" : "";
                    string phone = termConfig.TryGetProperty("phoneNumber", out var ph) ? ph.GetString() ?? "" : "";
                    string tradingName = termConfig.TryGetProperty("tradingName", out var tn) ? tn.GetString() ?? "" : "";

                    string addressLine = "";
                    if (termConfig.TryGetProperty("addressLines", out var lines) && lines.ValueKind == JsonValueKind.Array && lines.GetArrayLength() > 0)
                    {
                        addressLine = lines[0].GetString() ?? "";
                    }
                    int versionNo = termConfig.TryGetProperty("versionNo", out var v) ? v.GetInt32() : 0;

                    string updateConfigSQL = @"UPDATE TerminalConfiguration SET 
                                                Label = $lbl, Email = $email, Phone = $phone, 
                                                VersionNo = $vNo, TradingName = $trading, AddressLine = $addr 
                                                WHERE TerminalId IS NOT NULL";
                    using var stmt = new SqliteCommand(updateConfigSQL, conn);
                    stmt.Parameters.AddWithValue("$lbl", terminalLabel);
                    stmt.Parameters.AddWithValue("$email", email);
                    stmt.Parameters.AddWithValue("$phone", phone);
                    stmt.Parameters.AddWithValue("$vNo", versionNo);
                    stmt.Parameters.AddWithValue("$trading", tradingName);
                    stmt.Parameters.AddWithValue("$addr", addressLine);
                    await stmt.ExecuteNonQueryAsync();
                    Console.WriteLine("Updated TerminalConfiguration (TerminalId & IsActive preserved)");

                    // === OfflineLimit ===
                    if (termConfig.TryGetProperty("offlineLimit", out var offlineLimit))
                    {
                        int maxAge = offlineLimit.GetProperty("maxTransactionAgeInHours").GetInt32();
                        double maxAmount = offlineLimit.GetProperty("maxCummulativeAmount").GetDouble();

                        string insertLimitSQL = "INSERT OR REPLACE INTO OfflineLimit (TerminalId, MaxTransactionAgeInHours, MaxCummulativeAmount) VALUES ($tId, $maxAge, $maxAmt)";
                        using var stmtLimit = new SqliteCommand(insertLimitSQL, conn);
                        stmtLimit.Parameters.AddWithValue("$tId", "");
                        stmtLimit.Parameters.AddWithValue("$maxAge", maxAge);
                        stmtLimit.Parameters.AddWithValue("$maxAmt", maxAmount);
                        await stmtLimit.ExecuteNonQueryAsync();
                        Console.WriteLine("Inserted OfflineLimit");
                    }
                }

                // === TaxpayerConfiguration & TaxOffice ===
                if (data.TryGetProperty("taxpayerConfiguration", out taxpayerConfig))
                {
                    bool isVATRegistered = taxpayerConfig.TryGetProperty("isVATRegistered", out var vr) && vr.GetBoolean();
                    int taxpayerVersionNo = taxpayerConfig.TryGetProperty("versionNo", out var vn) ? vn.GetInt32() : 0;

                    var taxOffice = taxpayerConfig.GetProperty("taxOffice");
                    string taxOfficeCode = taxOffice.TryGetProperty("code", out var c) && c.ValueKind != JsonValueKind.Null ? c.GetString() ?? "" : "";
                    string officeName = taxOffice.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";

                    string updateTaxpayerSQL = @"UPDATE TaxpayerConfiguration SET 
                                                 IsVATRegistered = $vat, VersionNo = $vNo, TaxOfficeCode = $officeCode 
                                                 WHERE TIN = $tin";
                    using var stmt = new SqliteCommand(updateTaxpayerSQL, conn);
                    stmt.Parameters.AddWithValue("$vat", isVATRegistered ? 1 : 0);
                    stmt.Parameters.AddWithValue("$vNo", taxpayerVersionNo);
                    stmt.Parameters.AddWithValue("$officeCode", string.IsNullOrEmpty(taxOfficeCode) ? DBNull.Value : (object)taxOfficeCode);
                    stmt.Parameters.AddWithValue("$tin", tin);

                    int rows = await stmt.ExecuteNonQueryAsync();
                    if (rows == 0) Console.Error.WriteLine($"⚠️ No TaxpayerConfiguration row found for TIN: {tin}");
                    else Console.WriteLine($"✅ Updated TaxpayerConfiguration for TIN: {tin}");

                    if (!string.IsNullOrEmpty(taxOfficeCode))
                    {
                        string insertOfficeSQL = "INSERT OR REPLACE INTO TaxOffices (Code, Name) VALUES ($code, $name)";
                        using var stmtOffice = new SqliteCommand(insertOfficeSQL, conn);
                        stmtOffice.Parameters.AddWithValue("$code", taxOfficeCode);
                        stmtOffice.Parameters.AddWithValue("$name", officeName);
                        await stmtOffice.ExecuteNonQueryAsync();
                        Console.WriteLine($"✅ Inserted TaxOffice: {taxOfficeCode} - {officeName}");
                    }

                    // === Activated Tax Rates ===
                    if (taxpayerConfig.TryGetProperty("activatedTaxRateIds", out var activatedTaxRateIds) && activatedTaxRateIds.ValueKind == JsonValueKind.Array)
                    {
                        string deleteOldSQL = "DELETE FROM ActivatedTaxRates WHERE TaxpayerTin = $tin";
                        using var stmtDel = new SqliteCommand(deleteOldSQL, conn);
                        stmtDel.Parameters.AddWithValue("$tin", tin);
                        await stmtDel.ExecuteNonQueryAsync();
                        Console.WriteLine($"✅ Cleared old ActivatedTaxRates for TIN: {tin}");

                        string insertActivatedTaxSQL = "INSERT INTO ActivatedTaxRates (TaxType, TaxpayerTin) VALUES ($taxType, $tin)";
                        foreach (JsonElement val in activatedTaxRateIds.EnumerateArray())
                        {
                            if (val.ValueKind == JsonValueKind.String)
                            {
                                using var stmtIns = new SqliteCommand(insertActivatedTaxSQL, conn);
                                stmtIns.Parameters.AddWithValue("$taxType", val.GetString() ?? "");
                                stmtIns.Parameters.AddWithValue("$tin", tin);
                                await stmtIns.ExecuteNonQueryAsync();
                            }
                        }
                        Console.WriteLine($"✅ Inserted ActivatedTaxRates: {activatedTaxRateIds}");
                    }

                    // === Activated Levies ===
                    if (taxpayerConfig.TryGetProperty("activatedLevies", out var activatedLevies) && activatedLevies.ValueKind == JsonValueKind.Array)
                    {
                        string insertLevySQL = "INSERT OR REPLACE INTO Levies (Id, Name, ChargeMode, Rate, IsActive) VALUES ($id, $name, $mode, $rate, $act)";
                        foreach (JsonElement levyValue in activatedLevies.EnumerateArray())
                        {
                            using var stmtLevy = new SqliteCommand(insertLevySQL, conn);
                            stmtLevy.Parameters.AddWithValue("$id", levyValue.GetProperty("id").GetString() ?? "");
                            stmtLevy.Parameters.AddWithValue("$name", levyValue.GetProperty("name").GetString() ?? "");
                            stmtLevy.Parameters.AddWithValue("$mode", levyValue.GetProperty("chargeMode").GetString() ?? "");
                            stmtLevy.Parameters.AddWithValue("$rate", levyValue.GetProperty("rate").GetDouble());
                            stmtLevy.Parameters.AddWithValue("$act", levyValue.TryGetProperty("isActive", out var ia) && ia.GetBoolean() ? 1 : 0);
                            await stmtLevy.ExecuteNonQueryAsync();
                        }
                        Console.WriteLine("Synced Activated Levies");
                    }
                }
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"Failed to insert activation data: {e.Message}");
            }
        }
    }
}