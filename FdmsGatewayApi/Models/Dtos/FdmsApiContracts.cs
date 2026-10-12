using System.Text.Json.Serialization;

namespace FdmsGatewayApi.Models.Dtos;

public class VerifyTaxpayerInfoRequest
{
    public int DeviceID { get; set; }
    public string ActivationKey { get; set; } = string.Empty;
    public string DeviceSerialNo { get; set; } = string.Empty;
}

public class VerifyTaxpayerInfoResponse
{
    public string OperationID { get; set; } = string.Empty;
    public string TaxPayerName { get; set; } = string.Empty;
    public string TaxPayerTIN { get; set; } = string.Empty;
    public string? VatNumber { get; set; }
    public string DeviceBranchName { get; set; } = string.Empty;
}

public class RegisterDeviceRequest
{
    public int DeviceID { get; set; }
    public string ActivationKey { get; set; } = string.Empty;
    public string CertificateRequest { get; set; } = string.Empty; // PEM CSR
}

public class RegisterDeviceResponse
{
    public string OperationID { get; set; } = string.Empty;
    public string Certificate { get; set; } = string.Empty; // PEM X.509 Device Cert
}

public class GetConfigRequest
{
    public int DeviceID { get; set; }
}

public class GetConfigResponse
{
    public string OperationID { get; set; } = string.Empty;
    public string TaxPayerName { get; set; } = string.Empty;
    public string TaxPayerTIN { get; set; } = string.Empty;
    public string? VatNumber { get; set; }
    public string DeviceSerialNo { get; set; } = string.Empty;
    public string DeviceBranchName { get; set; } = string.Empty;
    public string DeviceOperatingMode { get; set; } = "Online";
    public int TaxPayerDayMaxHrs { get; set; } = 24;
    public int TaxpayerDayEndNotificationHrs { get; set; } = 2;
    public List<FdmsTaxRateDto> ApplicableTaxes { get; set; } = new();
    public DateTime CertificateValidTill { get; set; }
    public string QrUrl { get; set; } = string.Empty;
}

public class FdmsTaxRateDto
{
    public int TaxID { get; set; }
    public decimal? TaxPercent { get; set; }
    public string TaxName { get; set; } = string.Empty;
    public DateTime TaxValidFrom { get; set; }
    public DateTime? TaxValidTill { get; set; }
}

public class GetStatusRequest
{
    public int DeviceID { get; set; }
}

public class GetStatusResponse
{
    public string OperationID { get; set; } = string.Empty;
    public string FiscalDayStatus { get; set; } = string.Empty; // FiscalDayClosed, FiscalDayOpened, etc.
    public string? FiscalDayReconciliationMode { get; set; }
    public FdmsSignatureDataExDto? FiscalDayServerSignature { get; set; }
    public DateTime? FiscalDayClosed { get; set; }
    public string? FiscalDayClosingErrorCode { get; set; }
    public int? LastReceiptGlobalNo { get; set; }
    public int? LastFiscalDayNo { get; set; }
}

public class FdmsSignatureDataExDto
{
    public string Hash { get; set; } = string.Empty;
    public string Signature { get; set; } = string.Empty;
}

public class OpenDayRequest
{
    public int DeviceID { get; set; }
    public DateTime FiscalDayOpened { get; set; }
    public int? FiscalDayNo { get; set; }
}

public class OpenDayResponse
{
    public string OperationID { get; set; } = string.Empty;
    public int FiscalDayNo { get; set; }
}

public class SubmitReceiptRequest
{
    public int DeviceID { get; set; }
    public FdmsReceiptPayload Receipt { get; set; } = new();
}

public class FdmsReceiptPayload
{
    public string ReceiptType { get; set; } = "FiscalInvoice"; // FiscalInvoice, CreditNote, DebitNote
    public string ReceiptCurrency { get; set; } = "USD";
    public int ReceiptCounter { get; set; }
    public int ReceiptGlobalNo { get; set; }
    public string InvoiceNo { get; set; } = string.Empty;
    public FdmsBuyerDto? BuyerData { get; set; }
    public string? ReceiptNotes { get; set; }
    public DateTime ReceiptDate { get; set; }
    public FdmsCreditDebitNoteRefDto? CreditDebitNote { get; set; }
    public bool ReceiptLinesTaxInclusive { get; set; } = true;
    public List<FdmsReceiptLinePayload> ReceiptLines { get; set; } = new();
    public List<FdmsReceiptTaxPayload> ReceiptTaxes { get; set; } = new();
    public List<FdmsPaymentPayload> ReceiptPayments { get; set; } = new();
    public decimal ReceiptTotal { get; set; }
    public string ReceiptPrintForm { get; set; } = "Receipt48";
    public SignatureDataDto ReceiptDeviceSignature { get; set; } = new();
    public string? Username { get; set; }
    public string? UserNameSurname { get; set; }
}

public class FdmsBuyerDto
{
    public string BuyerRegisterName { get; set; } = string.Empty;
    public string BuyerTIN { get; set; } = string.Empty;
    public string? BuyerTradeName { get; set; }
    public string? VATNumber { get; set; }
}

public class FdmsCreditDebitNoteRefDto
{
    public long? ReceiptID { get; set; }
    public int? DeviceID { get; set; }
    public int? ReceiptGlobalNo { get; set; }
    public int? FiscalDayNo { get; set; }
}

public class FdmsReceiptLinePayload
{
    public string ReceiptLineType { get; set; } = "Sale";
    public int ReceiptLineNo { get; set; }
    public string? ReceiptLineHSCode { get; set; }
    public string ReceiptLineName { get; set; } = string.Empty;
    public decimal? ReceiptLinePrice { get; set; }
    public decimal ReceiptLineQuantity { get; set; }
    public decimal ReceiptLineTotal { get; set; }
    public string? TaxCode { get; set; }
    public decimal? TaxPercent { get; set; }
    public int TaxID { get; set; }
}

public class FdmsReceiptTaxPayload
{
    public string? TaxCode { get; set; }
    public decimal? TaxPercent { get; set; }
    public int TaxID { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal SalesAmountWithTax { get; set; }
}

public class FdmsPaymentPayload
{
    public string MoneyTypeCode { get; set; } = "Cash";
    public decimal PaymentAmount { get; set; }
}

public class SubmitReceiptResponse
{
    public string OperationID { get; set; } = string.Empty;
    public long ReceiptID { get; set; }
    public DateTime ServerDate { get; set; }
    public SignatureDataDto ReceiptServerSignature { get; set; } = new();
}

public class CloseDayRequest
{
    public int DeviceID { get; set; }
    public int FiscalDayNo { get; set; }
    public List<FiscalDayCounterDto> FiscalDayCounters { get; set; } = new();
    public SignatureDataDto FiscalDayDeviceSignature { get; set; } = new();
    public int ReceiptCounter { get; set; }
}

public class FiscalDayCounterDto
{
    public string FiscalCounterType { get; set; } = "SaleByTax"; // SaleByTax, BalanceByMoneyType
    public string FiscalCounterCurrency { get; set; } = "USD";
    public int? FiscalCounterTaxID { get; set; }
    public decimal? FiscalCounterTaxPercent { get; set; }
    public string? FiscalCounterMoneyType { get; set; }
    public decimal FiscalCounterValue { get; set; }
}

public class CloseDayResponse
{
    public string OperationID { get; set; } = string.Empty;
}

public class GetServerCertificateResponse
{
    public List<string> Certificate { get; set; } = new(); // Certificate chain
    public DateTime CertificateValidTill { get; set; }
}

public class PingRequest
{
    public int DeviceID { get; set; }
}

public class PingResponse
{
    public string OperationID { get; set; } = string.Empty;
    public int ReportingFrequency { get; set; } = 30;
}

public class ZReportResponseDto
{
    public int DeviceId { get; set; }
    public string DeviceSerialNo { get; set; } = string.Empty;
    public string TaxPayerName { get; set; } = string.Empty;
    public string TaxPayerTIN { get; set; } = string.Empty;
    public int FiscalDayNo { get; set; }
    public DateTime ReportDate { get; set; }
    public int StartingReceiptGlobalNo { get; set; }
    public int EndingReceiptGlobalNo { get; set; }
    public int TotalFiscalizedDocuments { get; set; }
    public int TotalPendingDocuments { get; set; }
    public List<ZReportCurrencySummaryDto> CurrencySummaries { get; set; } = new();
    public string BlockHashDigest { get; set; } = string.Empty;
    public SignatureDataDto ReportSignature { get; set; } = new();
    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
}

public class ZReportCurrencySummaryDto
{
    public string Currency { get; set; } = "USD";
    public decimal GrossSalesAmount { get; set; }
    public decimal NetTaxableAmount { get; set; }
    public decimal TotalTaxAmount { get; set; }
    public List<ZReportTaxSummaryDto> TaxBreakdowns { get; set; } = new();
}

public class ZReportTaxSummaryDto
{
    public string TaxCode { get; set; } = string.Empty;
    public decimal TaxPercent { get; set; }
    public decimal TaxableSales { get; set; }
    public decimal TaxAmount { get; set; }
}

public class HashChainVerificationResultDto
{
    public int DeviceId { get; set; }
    public bool IsChainValid { get; set; }
    public int TotalDocumentsScanned { get; set; }
    public int? TamperedIndex { get; set; }
    public string? TamperedInvoiceNo { get; set; }
    public string StatusMessage { get; set; } = "Hash chain integrity verified.";
    public DateTime VerifiedAtUtc { get; set; } = DateTime.UtcNow;
}

public class DirectoryItemDto
{
    public string Name { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
}

#region ZIMRA Batch Offline File DTOs (submitFile & getFileStatus)

public class SubmitFileRequest
{
    public int DeviceID { get; set; }
    /// <summary>
    /// Base64 encoded JSON string representing the OfflineBatchFilePayloadDto (Header, Content, Footer).
    /// </summary>
    public string File { get; set; } = string.Empty;
}

public class SubmitFileResponse
{
    public string OperationID { get; set; } = string.Empty;
}

public class GetFileStatusRequest
{
    public int DeviceID { get; set; }
    public string? OperationID { get; set; }
    public DateTime FileUploadedFrom { get; set; }
    public DateTime FileUploadedTill { get; set; }
}

public class GetFileStatusResponse
{
    public string OperationID { get; set; } = string.Empty;
    public List<FileStatusItemDto> FileStatus { get; set; } = new();
}

public class FileStatusItemDto
{
    public string OperationID { get; set; } = string.Empty;
    public DateTime FileUploadDate { get; set; }
    public int DeviceId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public DateTime? FileProcessingDate { get; set; }
    public string FileProcessingStatus { get; set; } = "Pending";
    public List<string>? FileProcessingErrorCode { get; set; }
    public int FiscalDayNo { get; set; }
    public DateTime FiscalDayOpenedAt { get; set; }
    public int FileSequence { get; set; }
    public string IpAddress { get; set; } = string.Empty;
}

public class OfflineBatchFilePayloadDto
{
    public OfflineFileHeaderDto Header { get; set; } = new();
    public OfflineFileContentDto? Content { get; set; }
    public OfflineFileFooterDto? Footer { get; set; }
}

public class OfflineFileHeaderDto
{
    public int DeviceID { get; set; }
    public int FiscalDayNo { get; set; }
    public DateTime FiscalDayOpened { get; set; }
    public int FileSequence { get; set; }
}

public class OfflineFileContentDto
{
    public List<FdmsReceiptPayload> Receipts { get; set; } = new();
}

public class OfflineFileFooterDto
{
    public List<FiscalDayCounterDto> FiscalCounters { get; set; } = new();
    public SignatureDataDto FiscalDayDeviceSignature { get; set; } = new();
    public int ReceiptCounter { get; set; }
    public DateTime FiscalDayClosed { get; set; }
}

#endregion

#region ZIMRA Stock Inventory DTOs (getStockList Section 4.15)

public class GetStockListRequest
{
    public int DeviceID { get; set; }
    public int Offset { get; set; } = 0;
    public int Limit { get; set; } = 100;
    public string? HsCode { get; set; }
    public string? GoodName { get; set; }
    public string? Sort { get; set; }
    public string? Order { get; set; }
    public string? Operator { get; set; }
}

public class GetStockListResponse
{
    public string OperationID { get; set; } = string.Empty;
    public int Total { get; set; }
    public List<ZimraGoodDto> Rows { get; set; } = new();

    [JsonIgnore]
    public List<StockItemDto> StockItems => Rows.Select(r => new StockItemDto
    {
        ItemCode = r.HsCode,
        ItemName = r.GoodName,
        Quantity = r.Quantity
    }).ToList();
}

public class ZimraGoodDto
{
    public string HsCode { get; set; } = string.Empty;
    public string GoodName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public long? TaxPayerId { get; set; }
    public string? TaxPayerName { get; set; }
    public long? BranchId { get; set; }
    public string? BranchName { get; set; }
}

public class StockItemDto
{
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string UnitOfMeasure { get; set; } = "Unit";
    public decimal Quantity { get; set; }
    public string TaxCategoryCode { get; set; } = "A";
    public decimal TaxPercent { get; set; } = 15.00m;
}

#endregion

#region ZIMRA User Management DTOs (Section 4.14)

public class OpenUserSessionRequest
{
    public int DeviceID { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class OpenUserSessionResponse
{
    public string OperationID { get; set; } = string.Empty;
    public int UserID { get; set; }
    public string UserRole { get; set; } = string.Empty;
    public string SessionToken { get; set; } = string.Empty;
}

public class RegisterZimraUserRequest
{
    public int DeviceID { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Surname { get; set; } = string.Empty;
    public string UserRole { get; set; } = "Operator";
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
}

public class RegisterZimraUserResponse
{
    public string OperationID { get; set; } = string.Empty;
    public int UserID { get; set; }
}

public class VerifyUserSecCodeRequest
{
    public int DeviceID { get; set; }
    public int UserID { get; set; }
    public string SecurityCode { get; set; } = string.Empty;
}

public class ResendUserSecCodeRequest
{
    public int DeviceID { get; set; }
    public int UserID { get; set; }
    public string SendSecurityCodeTo { get; set; } = "Email"; // Email or Phone
}

public class GetUsersListRequest
{
    public int DeviceID { get; set; }
}

public class GetUsersListResponse
{
    public string OperationID { get; set; } = string.Empty;
    public List<ZimraUserSummaryDto> Users { get; set; } = new();
}

public class ZimraUserSummaryDto
{
    public int UserID { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Surname { get; set; } = string.Empty;
    public string UserRole { get; set; } = string.Empty;
    public string UserStatus { get; set; } = "Active";
}

public class GetZimraUserRequest
{
    public int DeviceID { get; set; }
    public int UserID { get; set; }
}

public class GetZimraUserResponse
{
    public string OperationID { get; set; } = string.Empty;
    public int UserID { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Surname { get; set; } = string.Empty;
    public string UserRole { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string UserStatus { get; set; } = "Active";
}

public class UpdateZimraUserRequest
{
    public int DeviceID { get; set; }
    public int UserID { get; set; }
    public string UserRole { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
}

public class ChangeUserPasswordRequest
{
    public int DeviceID { get; set; }
    public int UserID { get; set; }
    public string CurrentPassword { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}

public class SaveUserPINRequest
{
    public int DeviceID { get; set; }
    public int UserID { get; set; }
    public string Pin { get; set; } = string.Empty;
}

public class VerifyUserPINRequest
{
    public int DeviceID { get; set; }
    public int UserID { get; set; }
    public string Pin { get; set; } = string.Empty;
}

public class VerifyUserPINResponse
{
    public string OperationID { get; set; } = string.Empty;
    public bool IsValid { get; set; } = true;
}

public class ResetUserPasswordRequest
{
    public int DeviceID { get; set; }
    public int UserID { get; set; }
}

public class InitResetUserPasswordRequest
{
    public int DeviceID { get; set; }
    public string Username { get; set; } = string.Empty;
}

public class InitResetUserPasswordResponse
{
    public string OperationID { get; set; } = string.Empty;
    public int UserID { get; set; }
}

public class VerifyResetUserPasswordSecCodeRequest
{
    public int DeviceID { get; set; }
    public int UserID { get; set; }
    public string SecurityCode { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}

public class ZimraGenericUserResponse
{
    public string OperationID { get; set; } = string.Empty;
}

#endregion



