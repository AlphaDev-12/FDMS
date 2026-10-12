using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FdmsGatewayApi.Models.Entities;

[Table("ZReports")]
public class ZReportEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public int DeviceId { get; set; }

    public int FiscalDayNo { get; set; }

    public DateTime ReportDate { get; set; }

    public int StartingReceiptGlobalNo { get; set; }

    public int EndingReceiptGlobalNo { get; set; }

    public int TotalFiscalizedDocuments { get; set; }

    [Column(TypeName = "decimal(18, 2)")]
    public decimal TotalUsdSales { get; set; }

    [Column(TypeName = "decimal(18, 2)")]
    public decimal TotalZigSales { get; set; }

    public string BlockHashDigest { get; set; } = string.Empty;

    public string Signature { get; set; } = string.Empty;

    [Required]
    public string PdfFilePath { get; set; } = string.Empty;

    [Required]
    public string PdfFileName { get; set; } = string.Empty;

    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
}
