using System;
using System.Security.Cryptography;
using System.Text;

namespace FiscalReceiptParser.Services
{
    public static class XSignature
    {
        /// <summary>
        /// Matches Java's Helper.computeXSignature(activationCode, secretKey) exactly:
        /// HMAC-SHA512, key = secretKey (UTF-8), message = activationCode (UTF-8),
        /// output Base64-encoded.
        /// </summary>
        public static string? Compute(string activationCode, string secretKey)
        {
            try
            {
                using var hmac = new HMACSHA512(Encoding.UTF8.GetBytes(secretKey));
                byte[] hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(activationCode));
                return Convert.ToBase64String(hashBytes);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to compute x-signature: {ex.Message}");
                return null;
            }
        }
    }
}