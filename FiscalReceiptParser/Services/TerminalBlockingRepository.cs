using System;
using FiscalReceiptParser.Data;

namespace FiscalReceiptParser.Services
{
    public static class TerminalBlockingRepository
    {
        public static void SaveBlockingReason(string terminalId, string reason, bool isUnblocked)
        {
            try
            {
                using var conn = Database.ConnOpen();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO TerminalBlockingReasons (TerminalId, BlockingReason, IsUnblocked, CreatedAt)
                    VALUES ($terminalId, $reason, $isUnblocked, datetime('now'))";
                cmd.Parameters.AddWithValue("$terminalId", terminalId);
                cmd.Parameters.AddWithValue("$reason", reason);
                cmd.Parameters.AddWithValue("$isUnblocked", isUnblocked ? 1 : 0);
                cmd.ExecuteNonQuery();
                System.Diagnostics.Debug.WriteLine("✅ Blocking reason saved.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Failed to save blocking reason: {ex.Message}");
            }
        }

        public static string? GetBlockingReason(string terminalId)
        {
            try
            {
                using var conn = Database.ConnOpen();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT BlockingReason FROM TerminalBlockingReasons
                    WHERE TerminalId = $terminalId ORDER BY CreatedAt DESC LIMIT 1";
                cmd.Parameters.AddWithValue("$terminalId", terminalId);
                return cmd.ExecuteScalar() as string;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Failed to fetch blocking reason: {ex.Message}");
                return null;
            }
        }

        public static void DeleteBlockingReason(string terminalId)
        {
            try
            {
                using var conn = Database.ConnOpen();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "DELETE FROM TerminalBlockingReasons WHERE TerminalId = $terminalId";
                cmd.Parameters.AddWithValue("$terminalId", terminalId);
                cmd.ExecuteNonQuery();
                System.Diagnostics.Debug.WriteLine("✅ Blocking reason deleted.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Failed to delete blocking reason: {ex.Message}");
            }
        }
    }
}