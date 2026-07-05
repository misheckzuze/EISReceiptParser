using System;
using System.Collections.Generic;
using System.Text;

namespace FiscalReceiptParser.Models
{
    /// <summary>
    /// Looks up a product by description (matching ProductName first, then Description,
    /// case-insensitively) and returns its ProductCode + authoritative TaxRateId — the
    /// real registered rate id from MRA's product sync, not whatever the receipt parser
    /// guessed from the source document's printed tax line.
    /// </summary>
    public class ProductLookupResult
    {
        public string ProductCode { get; set; } = "";
        public string TaxRateId { get; set; } = "";
        public bool Found { get; set; }
    }
}
