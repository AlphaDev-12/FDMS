using System.ComponentModel.DataAnnotations;

namespace FdmsTestIntegration.Models;

/// <summary>
/// Represents a primary business transaction document in the Partner ERP system.
/// </summary>
public class ErpDocument
{
    public int Id { get; set; }

    /// <summary>
    /// Document Type: FiscalInvoice, CreditNote, DebitNote
    /// </summary>
    [Required]
    public string DocumentType { get; set; } = "FiscalInvoice";

    /// <summary>
    /// Partner ERP Document Reference Number (e.g. INV-2026-001, CN-2026-001, DN-2026-001)
    /// </summary>
    [Required]
    public string InvoiceNo { get; set; } = string.Empty;

    /// <summary>
    /// For Credit Notes and Debit Notes: Links back to the original sales InvoiceNo in the ERP.
    /// </summary>
    public string? OriginalInvoiceNo { get; set; }

    /// <summary>
    /// Reason notes for returns or debit adjustments.
    /// </summary>
    public string? ReceiptNotes { get; set; }

    /// <summary>
    /// ISO 4217 Currency (USD, ZiG / ZWG)
    /// </summary>
    [Required]
    [StringLength(3)]
    public string Currency { get; set; } = "USD";

    public DateTime ReceiptDate { get; set; } = DateTime.Now;

    public string BuyerName { get; set; } = string.Empty;
    public string BuyerTIN { get; set; } = string.Empty;

    public decimal ReceiptTotal { get; set; }
    public decimal TaxAmount { get; set; }

    public string PaymentMethod { get; set; } = "Cash"; // Cash, BankTransfer, Card

    // --- ZIMRA FDMS Gateway Fiscal State Audit Fields ---
    public string FiscalStatus { get; set; } = "Pending"; // Pending, Fiscalized, Queued, Failed
    public long? ReceiptID { get; set; }
    public int? ReceiptGlobalNo { get; set; }
    public int? FiscalDayNo { get; set; }
    public int? DeviceId { get; set; }
    public string? QrCodeUrl { get; set; }
    public string? DeviceSignature { get; set; }
    public DateTime? FiscalizedAt { get; set; }
    public string? ErrorMessage { get; set; }

    public List<ErpLineItem> Lines { get; set; } = new();
}

/// <summary>
/// Individual line items belonging to an ERP transaction document.
/// </summary>
public class ErpLineItem
{
    public int Id { get; set; }
    public int ErpDocumentId { get; set; }

    public int LineNo { get; set; } = 1;
    public string? ItemCode { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Total { get; set; }

    public string TaxCode { get; set; } = "A";
    public decimal TaxPercent { get; set; } = 15.0m;
    public int TaxID { get; set; } = 1;

    public ErpDocument? ErpDocument { get; set; }
}
