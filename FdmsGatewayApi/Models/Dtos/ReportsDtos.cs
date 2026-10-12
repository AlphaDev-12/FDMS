namespace FdmsGatewayApi.Models.Dtos;

public class ReportsConfigDto
{
    public string StoragePath { get; set; } = string.Empty;
    public bool IsPathValid { get; set; } = true;
    public string? Message { get; set; }
}

public class UpdateReportsConfigDto
{
    public string StoragePath { get; set; } = string.Empty;
}

public class XReportResponseDto
{
    public int DeviceId { get; set; }
    public string DeviceSerialNo { get; set; } = string.Empty;
    public string TaxPayerName { get; set; } = string.Empty;
    public string TaxPayerTIN { get; set; } = string.Empty;
    public int FiscalDayNo { get; set; }
    public DateTime FiscalDayOpenedAt { get; set; }
    public DateTime ReportGeneratedAtUtc { get; set; }
    public int StartingReceiptGlobalNo { get; set; }
    public int EndingReceiptGlobalNo { get; set; }
    public int TotalFiscalizedDocuments { get; set; }
    public int TotalPendingDocuments { get; set; }
    public List<ZReportCurrencySummaryDto> CurrencySummaries { get; set; } = new();
    public string BlockHashDigest { get; set; } = string.Empty;
    public SignatureDataDto ReportSignature { get; set; } = new();
    public string? PdfFileName { get; set; }
    public string? PdfDownloadUrl { get; set; }
}

public class ZReportListItemDto
{
    public Guid Id { get; set; }
    public int DeviceId { get; set; }
    public int FiscalDayNo { get; set; }
    public DateTime ReportDate { get; set; }
    public int StartingReceiptGlobalNo { get; set; }
    public int EndingReceiptGlobalNo { get; set; }
    public int TotalFiscalizedDocuments { get; set; }
    public decimal TotalUsdSales { get; set; }
    public decimal TotalZigSales { get; set; }
    public string BlockHashDigest { get; set; } = string.Empty;
    public string PdfFileName { get; set; } = string.Empty;
    public DateTime GeneratedAtUtc { get; set; }
    public string PdfViewUrl { get; set; } = string.Empty;
}

public class PagedZReportsResultDto
{
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalItems { get; set; }
    public int TotalPages { get; set; }
    public List<ZReportListItemDto> Items { get; set; } = new();
}

