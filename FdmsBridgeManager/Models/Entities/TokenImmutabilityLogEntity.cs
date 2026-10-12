using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FdmsBridgeManager.Models.Entities;

[Table("TokenImmutabilityLogs")]
public class TokenImmutabilityLogEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public string ErrorType { get; set; } = "HASH_CHAIN_MISMATCH"; // HASH_CHAIN_MISMATCH, TAMPER_DETECTED, CORRUPTED_RECORD

    public string Details { get; set; } = string.Empty;

    public bool IsReportedToAdmin { get; set; } = false;

    public DateTime ReportedToAdminAt { get; set; }

    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;
}
