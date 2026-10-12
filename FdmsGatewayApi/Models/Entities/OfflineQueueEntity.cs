using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FdmsGatewayApi.Models.Entities;

[Table("OfflineQueue")]
public class OfflineQueueEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public int DeviceId { get; set; }

    [Required]
    public string InvoiceNo { get; set; } = string.Empty;

    public int ReceiptGlobalNo { get; set; } // Enforces strict sequential processing order per FDMS §9 Rule 13

    [Required]
    public string ReceiptType { get; set; } = "FiscalInvoice";

    [Required]
    public string SubmitPayloadJson { get; set; } = string.Empty;

    [Required]
    public string Status { get; set; } = "Queued"; // Queued, Processing, Completed, Failed

    public int RetryCount { get; set; } = 0;

    public DateTime QueuedAt { get; set; } = DateTime.UtcNow;

    public DateTime? LastAttemptAt { get; set; }

    public string? ErrorMessage { get; set; }
}
