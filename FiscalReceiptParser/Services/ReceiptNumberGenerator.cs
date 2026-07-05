using System;
using System.Text;
using FiscalReceiptParser.Data;

namespace FiscalReceiptParser.Services
{
    /// <summary>
    /// Simple DTO mirroring Java's InvoiceDetails — just enough to compute
    /// the next sequential receipt number from the last saved invoice.
    /// </summary>
    public class InvoiceDetails
    {
        public string InvoiceNumber { get; set; } = "";
        public DateTime InvoiceDateTime { get; set; }
    }

    /// <summary>
    /// Ports Java's receipt-number generation exactly: taxpayerId, terminal position,
    /// Julian date, and a daily sequence counter, each Base64-encoded and joined
    /// with hyphens (e.g. "B-A-cQ-A").
    /// </summary>
    public static class ReceiptNumberGenerator
    {
        // NOTE: this is the exact alphabet from Java's base10ToBase64 (encode).
        private const string EncodeAlphabet =
            "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

        // NOTE: this is the exact alphabet from Java's base64ToBase10 (decode) —
        // it ends in "-_" instead of "+/". This mismatch exists in the original
        // Java source too. It only matters for values that land on index 62/63
        // (extremely rare for small taxpayerId/position/count values in practice),
        // but it means encode-then-decode is NOT guaranteed to round-trip for
        // every input. Ported faithfully as-is; flag to your team if this needs
        // fixing at the source.
        private const string DecodeAlphabet =
            "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";

        public static string GenerateNewReceiptNumber()
        {
            long taxpayerId = GetTaxpayerId();
            int terminalPosition = GetTerminalPosition();
            var transactionDate = DateTime.Now.Date;
            long transactionCount = 1;

            var lastDetails = GetLastInvoiceDetails();
            if (lastDetails != null)
            {
                var lastDate = lastDetails.InvoiceDateTime.Date;
                if (lastDate == transactionDate)
                {
                    transactionCount = ConvertSequentialToBase10(lastDetails.InvoiceNumber) + 1;
                }
                else
                {
                    transactionCount = 1; // new day → reset
                }
            }

            return GenerateReceiptNumber(taxpayerId, terminalPosition, transactionDate, transactionCount);
        }

        public static string GenerateReceiptNumber(long taxpayerId, int terminalPosition, DateTime transactionDate, long transactionCount)
        {
            long julianDate = ToJulianDate(transactionDate);
            string base64Taxpayer = Base10ToBase64(taxpayerId);
            string base64Position = Base10ToBase64(terminalPosition);
            string base64Julian = Base10ToBase64(julianDate);
            string base64Count = Base10ToBase64(transactionCount);

            return $"{base64Taxpayer}-{base64Position}-{base64Julian}-{base64Count}";
        }

        public static long ToJulianDate(DateTime date)
        {
            int year = date.Year;
            int month = date.Month;
            int day = date.Day;

            if (month <= 2)
            {
                year -= 1;
                month += 12;
            }

            int a = year / 100;
            int b = 2 - a + (a / 4);

            return (long)(Math.Floor(365.25 * (year + 4716))
                        + Math.Floor(30.6001 * (month + 1))
                        + day + b - 1524);
        }

        public static string Base10ToBase64(long number)
        {
            if (number == 0) return "A";

            var result = new StringBuilder();
            while (number > 0)
            {
                int remainder = (int)(number % 64);
                result.Insert(0, EncodeAlphabet[remainder]);
                number /= 64;
            }
            return result.ToString();
        }

        public static long Base64ToBase10(string base64)
        {
            long result = 0;
            foreach (char c in base64)
            {
                int index = DecodeAlphabet.IndexOf(c);
                if (index == -1)
                    throw new ArgumentException($"Invalid Base64 character: {c}");
                result = result * 64 + index;
            }
            return result;
        }

        public static long ConvertSequentialToBase10(string invoiceNumber)
        {
            if (string.IsNullOrEmpty(invoiceNumber) || !invoiceNumber.Contains('-'))
                return 0;

            var parts = invoiceNumber.Split('-');
            if (parts.Length != 4)
                return 0;

            try
            {
                return Base64ToBase10(parts[3]);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Failed to decode invoice serial: {ex.Message}");
                return 0;
            }
        }

        public static long GetTaxpayerId()
        {
            using var conn = Database.ConnOpen();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT TaxpayerId FROM TaxpayerConfiguration LIMIT 1";
            var result = cmd.ExecuteScalar();
            return result != null && result != DBNull.Value ? Convert.ToInt64(result) : 0;
        }

        public static int GetTerminalPosition()
        {
            try
            {
                using var conn = Database.ConnOpen();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT TerminalPosition FROM ActivatedTerminal LIMIT 1";
                var result = cmd.ExecuteScalar();
                return result != null && result != DBNull.Value ? Convert.ToInt32(result) : -1;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Failed to fetch Terminal Position: {ex.Message}");
                return -1;
            }
        }

        public static InvoiceDetails? GetLastInvoiceDetails()
        {
            try
            {
                using var conn = Database.ConnOpen();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT InvoiceNumber, InvoiceDateTime FROM Invoices ORDER BY InvoiceDateTime DESC LIMIT 1";
                using var reader = cmd.ExecuteReader();

                if (reader.Read())
                {
                    return new InvoiceDetails
                    {
                        InvoiceNumber = reader.GetString(0),
                        InvoiceDateTime = DateTime.Parse(reader.GetString(1))
                    };
                }

                System.Diagnostics.Debug.WriteLine("ℹ️ No records found in Invoices.");
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error fetching last invoice: {ex.Message}");
                return null;
            }
        }
    }
}