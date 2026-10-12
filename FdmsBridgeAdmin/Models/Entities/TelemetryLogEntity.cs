using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FdmsBridgeAdmin.Models.Entities;

[Table("TelemetryLogs")]
public class TelemetryLogEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ConsultancyFirmId { get; set; }

    public string FirmName { get; set; } = string.Empty;

    public int TotalTokensPurchased { get; set; }

    public int TotalTokensConsumed { get; set; }

    public int CurrentTokenBalance { get; set; }

    public int ActiveClientSubscriptions { get; set; }

    public string ImmutabilityStatus { get; set; } = "OK"; // OK, TAMPER_DETECTED

    public string LogType { get; set; } = "24H_Heartbeat"; // 24H_Heartbeat, TamperAlert, StartupCheck

    public string? TamperErrorMessage { get; set; }

    public string IpAddress { get; set; } = string.Empty;

    public DateTime ReportedAt { get; set; } = DateTime.UtcNow;
}
