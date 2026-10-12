using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FdmsGatewayApi.Models.Entities;

[Table("AuditLogs")]
public class AuditLogEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public int? DeviceId { get; set; }

    public string? InvoiceNo { get; set; }

    [Required]
    public string Direction { get; set; } = "Inbound"; // Inbound, Outbound

    [Required]
    public string Endpoint { get; set; } = string.Empty;

    public string RawRequestJson { get; set; } = string.Empty;

    public string? RawResponseJson { get; set; }

    public int HttpStatusCode { get; set; }

    [Required]
    public string ErrorCategory { get; set; } = "None"; // None, UserInput, Infrastructure

    public string? ErrorCode { get; set; } // DEV01, RCPT01, etc.

    public string? ErrorMessage { get; set; }

    public long DurationMs { get; set; }
}
