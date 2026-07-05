using FiscalReceiptParser.Data;
using Microsoft.Data.Sqlite;
using System.Threading.Tasks;

namespace FiscalReceiptParser.Services
{
    public static class TerminalActivationStatus
    {
        /// <summary>
        /// Source of truth for "has this install already been activated".
        /// A terminal counts as activated once a row exists in ActivatedTerminal
        /// with IsActive = 1 (written by ActivationDataInserter.InsertActivationDataAsync).
        /// </summary>
        public static async Task<bool> IsActivatedAsync()
        {
            using var conn = Database.ConnOpen();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM ActivatedTerminal WHERE IsActive = 1";

            var result = await cmd.ExecuteScalarAsync();
            var count = result is long l ? l : 0;
            return count > 0;
        }
    }
}