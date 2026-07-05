using Microsoft.Data.Sqlite;

namespace FiscalReceiptParser.Data
{
    /// <summary>
    /// Second half of the partial Database class. Lives in its own file on purpose —
    /// Database.cs only needs a one-line "partial" addition and a single call to
    /// EnsureSettingsTable(), so this can be developed independently without
    /// conflicting with anyone else editing Database.cs.
    /// </summary>
    public static partial class Database
    {
        /// <summary>
        /// Single-row table (Id = 1) holding the three folders the app needs:
        /// where to watch for incoming PDFs, where to move ones that fail to
        /// read/process, and where to move ones that are handled successfully.
        /// </summary>
        internal static void EnsureSettingsTable(SqliteConnection conn)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"CREATE TABLE IF NOT EXISTS AppSettings (
                Id INTEGER PRIMARY KEY CHECK (Id = 1),
                WatchFolder TEXT,
                FailedFolder TEXT,
                OutputFolder TEXT
            )";
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// Reads the folder settings. Returns empty strings for any value that
        /// hasn't been configured yet (i.e. no row exists, or a column is NULL).
        /// </summary>
        public static (string WatchFolder, string FailedFolder, string OutputFolder) GetFolderSettings()
        {
            using var conn = ConnOpen();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT WatchFolder, FailedFolder, OutputFolder FROM AppSettings WHERE Id = 1";
            using var reader = cmd.ExecuteReader();

            if (reader.Read())
            {
                return (
                    reader.IsDBNull(0) ? "" : reader.GetString(0),
                    reader.IsDBNull(1) ? "" : reader.GetString(1),
                    reader.IsDBNull(2) ? "" : reader.GetString(2)
                );
            }

            return ("", "", "");
        }

        /// <summary>
        /// Upserts the folder settings row. Safe to call whether or not a row
        /// already exists (Id = 1 is always the row, same pattern as
        /// TerminalConfiguration).
        /// </summary>
        public static void SaveFolderSettings(string watchFolder, string failedFolder, string outputFolder)
        {
            using var conn = ConnOpen();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO AppSettings (Id, WatchFolder, FailedFolder, OutputFolder)
                VALUES (1, $watch, $failed, $output)
                ON CONFLICT(Id) DO UPDATE SET
                    WatchFolder = excluded.WatchFolder,
                    FailedFolder = excluded.FailedFolder,
                    OutputFolder = excluded.OutputFolder";

            cmd.Parameters.AddWithValue("$watch", watchFolder ?? "");
            cmd.Parameters.AddWithValue("$failed", failedFolder ?? "");
            cmd.Parameters.AddWithValue("$output", outputFolder ?? "");
            cmd.ExecuteNonQuery();
        }
    }
}