using System;
using System.Collections.Generic;
using System.Text;

namespace FiscalReceiptParser.Models
{
    public class BlockingCheckResult
    {
        public bool IsAllowed { get; set; }
        public string? Reason { get; set; }
    }
}
