using System;
using FiscalReceiptParser.Data;

namespace FiscalReceiptParser.Services
{
    /// <summary>
    /// Reads terminal/activation config already persisted by ActivationDataInserter,
    /// and updates the confirmation result. Mirrors the Java Helper.get*/update* methods.
    /// </summary>
    public static class ConfigHelper
    {
        private const string VendorAccessKey = "B4UH-PJUL-TY7C-I2ZA";
        public static string? GetActivationCode()
        {
            using var conn = Database.ConnOpen();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT ActivationCode FROM ActivationCode LIMIT 1";
            return cmd.ExecuteScalar() as string;
        }

        /// <summary>
        /// Gets the global MRA Vendor Access Key.
        /// </summary>
        public static string GetVendorAccessKey()
        {
            return VendorAccessKey;
        }

        public static string? GetSecretKey()
        {
            using var conn = Database.ConnOpen();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT SecretKey FROM ActivatedTerminal LIMIT 1";
            return cmd.ExecuteScalar() as string;
        }

        public static string? GetTerminalId()
        {
            using var conn = Database.ConnOpen();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT TerminalId FROM ActivatedTerminal LIMIT 1";
            return cmd.ExecuteScalar() as string;
        }

        public static string? GetToken()
        {
            using var conn = Database.ConnOpen();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT JwtToken FROM ActivatedTerminal LIMIT 1";
            return cmd.ExecuteScalar() as string;
        }

        public static string? GetTin()
        {
            using var conn = Database.ConnOpen();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT TIN FROM TaxpayerConfiguration LIMIT 1";
            return cmd.ExecuteScalar() as string;
        }

        public static string? GetTerminalSiteId()
        {
            using var conn = Database.ConnOpen();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT SiteId FROM TerminalSites LIMIT 1";
            return cmd.ExecuteScalar() as string;
        }

        /// <summary>
        /// Confirmed activation status lives on TerminalConfiguration (single-row table,
        /// Id = 1) — matching the Java version's updateIsActiveInTerminalConfiguration,
        /// NOT on ActivatedTerminal (which has no IsActive column in this schema).
        /// </summary>
        public static bool UpdateIsActiveInTerminalConfiguration(bool isActive)
        {
            using var conn = Database.ConnOpen();
            using var transaction = conn.BeginTransaction();

            try
            {
                int rows1;
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = transaction;
                    cmd.CommandText = "UPDATE TerminalConfiguration SET IsActive = $isActive WHERE Id = 1";
                    cmd.Parameters.AddWithValue("$isActive", isActive ? 1 : 0);
                    rows1 = cmd.ExecuteNonQuery();
                }

                int rows2;
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = transaction;
                    cmd.CommandText = "UPDATE ActivatedTerminal SET IsActive = $isActive WHERE TerminalId = $terminalId";
                    cmd.Parameters.AddWithValue("$isActive", isActive ? 1 : 0);
                    cmd.Parameters.AddWithValue("$terminalId", ConfigHelper.GetTerminalId() ?? "");
                    rows2 = cmd.ExecuteNonQuery();
                }

                transaction.Commit();
                return rows1 > 0 || rows2 > 0;
            }
            catch (Exception)
            {
                transaction.Rollback();
                throw;
            }
        }
    }
}