using System;
using System.Collections.Generic;
using System.Text;

namespace FiscalReceiptParser.Models
{
    public class SubmitSaleResult
    {
        public bool Success { get; set; }
        public string InvoiceNumber { get; set; } = "";
        public string SourceInvoiceNumber { get; set; } = ""; // the parsed receipt's own number, kept for reference
        public string ValidationUrl { get; set; } = "";
        public string Remark { get; set; } = "";
        public bool IsOffline { get; set; }
        public bool IsOutOfStock { get; set; }   // NEW
        public List<string> Warnings { get; } = new();
    }
}
