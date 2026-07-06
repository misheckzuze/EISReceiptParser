using System;
using System.Security.Cryptography;
using System.Text;

namespace FiscalReceiptParser.Services
{
    public class InvoiceGenerationRequest
    {
        public string InvoiceNumber { get; set; } = "";
        public int NumItems { get; set; }
        public DateTime TransactionDate { get; set; }
        public double InvoiceTotal { get; set; }
        public double VatAmount { get; set; }
    }

    public static class OfflineReceiptSignature
    {
        // TODO: confirm the real value of ApiEndpoints.OFFLINE_VALIDATION_BASE_URL.
        // This is a placeholder based on the pattern of your other MRA endpoints.
        private const string OfflineValidationBaseUrl = "https://eis-portal.mra.mw/ReceiptValidation/Validate";

        /// <summary>
        /// Ported from Java's generateOfflineReceiptSignature. Builds the query-string
        /// payload, signs it, and returns the full validation URL with the signature
        /// appended as &amp;S=...
        ///
        /// NOTE: computeHMACWithSHA256's exact output encoding (hex vs Base64) wasn't
        /// shown in the Java source shared so far — this implementation assumes
        /// HMAC-SHA256 with Base64 output, matching the pattern used by
        /// computeXSignature (HMAC-SHA512/Base64) elsewhere in this codebase. If MRA
        /// rejects offline-validated receipts, this is the first thing to check —
        /// paste the real computeHMACWithSHA256 source and I'll correct it exactly.
        /// </summary>
        public static string GenerateOfflineReceiptSignature(InvoiceGenerationRequest request, string secretKey)
        {
            long julianDate = ReceiptNumberGenerator.ToJulianDate(request.TransactionDate.Date);
            string julianBase64 = ReceiptNumberGenerator.Base10ToBase64(julianDate);

            string param = string.Format(
                "TI={0}&N={1}&I={2:0.00}&V={3:0.00}&T={4}",
                request.InvoiceNumber,
                request.NumItems,
                request.InvoiceTotal,
                request.VatAmount,
                julianBase64);

            string offlineDataSignature = ComputeHmacSha256(param, secretKey);

            try
            {
                offlineDataSignature = Uri.EscapeDataString(offlineDataSignature);
            }
            catch
            {
                // matches Java's "ignored" catch — fall back to the unencoded signature
            }

            return $"{OfflineValidationBaseUrl}?{param}&S={offlineDataSignature}";
        }

        private static string ComputeHmacWithSha256(string data, string secretKey)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secretKey));
            byte[] hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
            return Convert.ToBase64String(hashBytes);
        }

        private static string ComputeHmacSha256(string plainText, string secretKey)
        {
            using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secretKey)))
            {
                byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(plainText));
                return Convert.ToBase64String(hash)
                              .Replace("+", "-")
                              .Replace("/", "_")
                              .TrimEnd('=');
            }
        }
    }
}