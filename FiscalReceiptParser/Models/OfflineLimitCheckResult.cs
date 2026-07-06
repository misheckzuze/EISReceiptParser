using System;
using System.Collections.Generic;
using System.Text;

namespace FiscalReceiptParser.Models
{
    public class OfflineLimitCheckResult
    {
        public bool IsAllowed { get; set; }
        public double HoursElapsed { get; set; }
        public int LimitHours { get; set; }
    }
}
