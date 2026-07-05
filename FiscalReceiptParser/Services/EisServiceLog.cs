using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FiscalReceiptParser.Services
{
    public static class EisServiceLog
    {
        private static readonly string LogPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "EisService.log");

        private static readonly object _lock = new object();

        private static void Write(string level, string msg)
        {
            string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {msg}";

            // Always write to file
            try
            {
                lock (_lock)
                {
                    File.AppendAllText(LogPath, line + Environment.NewLine);

                    // Keep log under 5MB — trim if needed
                    var info = new FileInfo(LogPath);
                    if (info.Length > 5 * 1024 * 1024)
                        TrimLog();
                }
            }
            catch { /* never let logging crash the service */ }

            System.Diagnostics.Debug.WriteLine(line);
        }

        private static void TrimLog()
        {
            try
            {
                var lines = File.ReadAllLines(LogPath);
                // Keep last 1000 lines
                int skip = Math.Max(0, lines.Length - 1000);
                File.WriteAllLines(LogPath,
                    new ArraySegment<string>(lines, skip, lines.Length - skip));
            }
            catch { }
        }

        public static void Info(string msg) => Write("INFO ", msg);
        public static void Warn(string msg) => Write("WARN ", msg);
        public static void Error(string msg) => Write("ERROR", msg);

        public static void Debug(string msg)
        {
#if DEBUG
            Write("DEBUG", msg);
#endif
        }
    }

}
