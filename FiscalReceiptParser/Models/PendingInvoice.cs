using System;
using System.Collections.Generic;
using System.Text;

namespace FiscalReceiptParser.Models
{
    public class PendingInvoice
    {
        public string InvoiceNumber { get; set; } = "";
        public string PaymentId { get; set; } = "";
    }
}
