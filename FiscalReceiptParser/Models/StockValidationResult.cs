using System;
using System.Collections.Generic;
using System.Text;

namespace FiscalReceiptParser.Models
{
    public class StockValidationResult
    {
        public bool IsSufficient { get; set; } = true;
        public List<string> InsufficientItems { get; } = new();
    }
}
