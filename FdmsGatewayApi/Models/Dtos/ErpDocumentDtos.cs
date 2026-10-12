using System.ComponentModel.DataAnnotations;

namespace FdmsGatewayApi.Models.Dtos;

public class ErpInvoiceRequestDto
{
    [Required]
    public string InvoiceNo { get; set; } = string.Empty;

    [Required]
    [StringLength(3, MinimumLength = 3)]
    public string Currency { get; set; } = "USD";

    [Required]
    public DateTime ReceiptDate { get; set; } = DateTime.Now;

    public bool ReceiptLinesTaxInclusive { get; set; } = true;

    public string ReceiptPrintForm { get; set; } = "Receipt48";

    public string? Username { get; set; }
    public string? UserNameSurname { get; set; }

    public BuyerDto? BuyerData { get; set; }

    [Required]
    public List<ErpReceiptLineDto> Lines { get; set; } = new();

    [Required]
    public List<ErpReceiptTaxDto> Taxes { get; set; } = new();

    [Required]
    public List<ErpPaymentDto> Payments { get; set; } = new();

    public int? DeviceId { get; set; }

    [Required]
    public decimal ReceiptTotal { get; set; }
}

public class ErpCreditNoteRequestDto : ErpInvoiceRequestDto
{
    [Required]
    public string ReceiptNotes { get; set; } = string.Empty;

    [Required]
    public CreditDebitNoteRefDto CreditDebitNoteRef { get; set; } = new();
}

public class ErpDebitNoteRequestDto : ErpInvoiceRequestDto
{
    [Required]
    public string ReceiptNotes { get; set; } = string.Empty;

    [Required]
    public CreditDebitNoteRefDto CreditDebitNoteRef { get; set; } = new();
}

public class CreditDebitNoteRefDto
{
    public long? OriginalReceiptID { get; set; }
    public int? OriginalDeviceID { get; set; }
    public int? OriginalReceiptGlobalNo { get; set; }
    public int? OriginalFiscalDayNo { get; set; }
}

public class BuyerDto
{
    [Required]
    public string BuyerRegisterName { get; set; } = string.Empty;

    [Required]
    [StringLength(10)]
    public string BuyerTIN { get; set; } = string.Empty;

    public string? BuyerTradeName { get; set; }
    public string? VATNumber { get; set; }
}

public class ErpReceiptLineDto
{
    public string ReceiptLineType { get; set; } = "Sale"; // Sale, Discount
    public int ReceiptLineNo { get; set; } = 1;
    public string? ReceiptLineHSCode { get; set; }

    [Required]
    public string ReceiptLineName { get; set; } = string.Empty;

    public decimal? ReceiptLinePrice { get; set; }

    [Required]
    public decimal ReceiptLineQuantity { get; set; } = 1;

    [Required]
    public decimal ReceiptLineTotal { get; set; }

    public string? TaxCode { get; set; }
    public decimal? TaxPercent { get; set; }
    public int TaxID { get; set; }
}

public class ErpReceiptTaxDto
{
    public string? TaxCode { get; set; }
    public decimal? TaxPercent { get; set; }
    public int TaxID { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal SalesAmountWithTax { get; set; }
}

public class ErpPaymentDto
{
    [Required]
    public string MoneyTypeCode { get; set; } = "Cash"; // Cash, Card, MobileWallet, Coupon, Credit, BankTransfer, Other

    [Required]
    public decimal PaymentAmount { get; set; }

    public string? PaymentCurrency { get; set; } // Optional ISO 4217 currency for split tender (e.g. USD, ZiG)
}

public class ReceiptPrintLayoutDto
{
    public string HeaderTitle { get; set; } = "OFFICIAL FISCAL RECEIPT";
    public string TaxPayerName { get; set; } = string.Empty;
    public string TaxPayerTIN { get; set; } = string.Empty;
    public string? VatNumber { get; set; }
    public string DeviceSerialNo { get; set; } = string.Empty;
    public string DeviceBranchName { get; set; } = string.Empty;
    public int DeviceId { get; set; }
    public string InvoiceNo { get; set; } = string.Empty;
    public string ReceiptType { get; set; } = "FiscalInvoice";
    public int FiscalDayNo { get; set; }
    public int ReceiptGlobalNo { get; set; }
    public DateTime ReceiptDate { get; set; }
    public string Currency { get; set; } = "USD";
    public decimal ReceiptTotal { get; set; }
    public List<ErpReceiptLineDto> Lines { get; set; } = new();
    public List<ErpReceiptTaxDto> Taxes { get; set; } = new();
    public List<ErpPaymentDto> Payments { get; set; } = new();
    public string DeviceSignatureHash { get; set; } = string.Empty;
    public string DeviceSignature { get; set; } = string.Empty;
    public string QrCodeUrl { get; set; } = string.Empty;
    public string VerificationTextFallback { get; set; } = string.Empty;
    public string FooterNotice { get; set; } = "Thank you for your business!";
}

public class FiscalizedDocumentResponseDto
{
    public string InvoiceNo { get; set; } = string.Empty;
    public int DeviceId { get; set; }
    public string ReceiptType { get; set; } = "FiscalInvoice";
    public long ReceiptID { get; set; }
    public int ReceiptGlobalNo { get; set; }
    public int ReceiptCounter { get; set; }
    public int FiscalDayNo { get; set; }
    public DateTime ServerDate { get; set; }
    public SignatureDataDto DeviceSignature { get; set; } = new();
    public SignatureDataDto ServerSignature { get; set; } = new();
    public string QrCodeUrl { get; set; } = string.Empty;
    public string? QrCodeBase64Image { get; set; }
    public string FiscalStatus { get; set; } = "Fiscalized";
    public bool IsOfflineFiscalized { get; set; } = false;
    public string OperationID { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
    public string? PreviousDeviceHash { get; set; }

    // Direct Physical Thermal Printing Blobs
    public ReceiptPrintLayoutDto? PrintLayout { get; set; }
    public string? EscPosStreamBase64 { get; set; }

    // For multi-currency split invoices
    public List<FiscalizedDocumentResponseDto>? SplitReceipts { get; set; }
}

public class SignatureDataDto
{
    public string Hash { get; set; } = string.Empty;
    public string Signature { get; set; } = string.Empty;
}

public class DeviceCounterSequenceResult
{
    public int ReceiptCounter { get; set; }
    public int ReceiptGlobalNo { get; set; }
    public int FiscalDayNo { get; set; }
}

public class ErpBulkInvoiceRequestDto
{
    public string? BatchId { get; set; }
    public string? CallbackUrl { get; set; }
    [Required]
    public List<ErpInvoiceRequestDto> Invoices { get; set; } = new();
}

public class ErpBulkInvoiceResponseDto
{
    public string BatchId { get; set; } = Guid.NewGuid().ToString();
    public int TotalReceived { get; set; }
    public int ProcessedCount { get; set; }
    public int SuccessCount { get; set; }
    public int QueuedCount { get; set; }
    public int FailedCount { get; set; }
    public List<ErpBulkItemResultDto> Results { get; set; } = new();
}

public class ErpBulkItemResultDto
{
    public string InvoiceNo { get; set; } = string.Empty;
    public int? DeviceId { get; set; }
    public string FiscalStatus { get; set; } = "Pending";
    public int StatusCode { get; set; } = 200;
    public FiscalizedDocumentResponseDto? Document { get; set; }
    public string? ErrorMessage { get; set; }
}


