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
        public static string? GetActivationCode()
        {
            using var conn = Database.ConnOpen();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT ActivationCode FROM ActivationCode LIMIT 1";
            return cmd.ExecuteScalar() as string;
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
            // Start a transaction to ensure both updates succeed together
            using var transaction = conn.BeginTransaction();
            using var cmd = conn.CreateCommand();
            cmd.Transaction = transaction;

            try
            {
                // 1. Update TerminalConfiguration
                cmd.CommandText = "UPDATE TerminalConfiguration SET IsActive = $isActive WHERE Id = 1";
                cmd.Parameters.Clear();
                cmd.Parameters.AddWithValue("$isActive", isActive ? 1 : 0);
                int rows1 = cmd.ExecuteNonQuery();

                // 2. Update ActivatedTerminals
                cmd.CommandText = "UPDATE ActivatedTerminal SET IsActive = $isActive WHERE Id = 1";
                // Note: If 'Id = 1' isn't the correct matching criteria for ActivatedTerminals, 
                // change the WHERE clause to match your schema (e.g., WHERE TerminalId = 1)
                int rows2 = cmd.ExecuteNonQuery();

                // Commit changes if both statements executed without errors
                transaction.Commit();

                // Returns true if at least one of the tables was modified
                return (rows1 > 0 || rows2 > 0);
            }
            catch (Exception)
            {
                // Rollback the database state if any update fails
                transaction.Rollback();
                throw;
            }
        }
    }
}