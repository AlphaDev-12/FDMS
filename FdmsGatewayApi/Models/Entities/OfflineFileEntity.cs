using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FdmsGatewayApi.Models.Entities;

[Table("OfflineFiles")]
public class OfflineFileEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public int DeviceId { get; set; }

    [Required]
    public string OperationId { get; set; } = string.Empty;

    public int FiscalDayNo { get; set; }

    public int FileSequence { get; set; } = 1;

    public int ReceiptCount { get; set; } = 0;

    [Required]
    public string Base64FilePayload { get; set; } = string.Empty;

    [Required]
    public string Status { get; set; } = "Submitted"; // Submitted, Processing, FileProcessingIsSuccessful, FileProcessingWithErrors, FileProcessingFailed

    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;

    public DateTime? ProcessedAt { get; set; }

    public string? ErrorDetails { get; set; }
}
