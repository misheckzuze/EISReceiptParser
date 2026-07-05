using System.Text.Json.Serialization;

namespace FiscalReceiptParser.Models;

public class InvoiceRoot
{
    public InvoiceHeader InvoiceHeader { get; set; } = new();
    public List<InvoiceLineItem> InvoiceLineItems { get; set; } = new();
    public InvoiceSummary InvoiceSummary { get; set; } = new();
}

public class InvoiceHeader
{
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateTime InvoiceDateTime { get; set; }
    public string SellerTIN { get; set; } = string.Empty;
    public string BuyerTIN { get; set; } = string.Empty;
    public string BuyerName { get; set; } = string.Empty;
    public string BuyerAuthorizationCode { get; set; } = string.Empty;
    public string SiteId { get; set; } = string.Empty;
    public int GlobalConfigVersion { get; set; }
    public int TaxpayerConfigVersion { get; set; }
    public int TerminalConfigVersion { get; set; }
    public bool IsExport { get; set; }
    public bool IsReliefSupply { get; set; }
    public Vat5CertificateDetails? Vat5CertificateDetails { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
}

public class Vat5CertificateDetails
{
    public int Id { get; set; }
    public string ProjectNumber { get; set; } = string.Empty;
    public string CertificateNumber { get; set; } = string.Empty;
    public int Quantity { get; set; }
}

public class InvoiceLineItem
{
    public int Id { get; set; }
    public string ProductCode { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public decimal Quantity { get; set; }
    public decimal Discount { get; set; }
    public decimal Total { get; set; }
    public decimal TotalVAT { get; set; }
    public string TaxRateId { get; set; } = string.Empty;
    public bool IsProduct { get; set; }
}

public class InvoiceSummary
{
    public List<TaxBreakDown> TaxBreakDown { get; set; } = new();
    public List<LevyBreakDown> LevyBreakDown { get; set; } = new();
    public decimal TotalVAT { get; set; }
    public string OfflineSignature { get; set; } = string.Empty;
    public decimal InvoiceTotal { get; set; }
    public decimal AmountTendered { get; set; }
}

public class TaxBreakDown
{
    public string RateId { get; set; } = string.Empty;
    public decimal TaxableAmount { get; set; }
    public decimal TaxAmount { get; set; }
}

public class LevyBreakDown
{
    public string LevyTypeId { get; set; } = string.Empty;
    public decimal LevyRate { get; set; }
    public decimal LevyAmount { get; set; }
}