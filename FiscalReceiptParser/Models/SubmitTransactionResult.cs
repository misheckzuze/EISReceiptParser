using System;
using System.Collections.Generic;
using System.Text;

namespace FiscalReceiptParser.Models
{
    /// <summary>
    /// Result of a submission attempt — mirrors what Java's callback receives
    /// (success, validationUrl), plus the extra signal needed for the config-refresh check.
    /// </summary>
    public class SubmitTransactionResult
    {
        public bool Success { get; set; }
        public string ValidationUrl { get; set; } = "";
        public bool ShouldDownloadLatestConfig { get; set; }
        /// <summary>True when MRA rejected it specifically because the invoice number
        /// already exists — treated as success (already transmitted), matching Java.</summary>
        public bool WasDuplicate { get; set; }
        public string Remark { get; set; } = "";
    }
}
