using FdmsBridgeAdmin.Models.Dtos;

namespace FdmsBridgeAdmin.Services.Interfaces;

public interface ITelemetryService
{
    Task RecordHeartbeatAsync(TelemetryHeartbeatRequest req, string clientIp, CancellationToken ct = default);
    Task<List<TelemetryLogEntityDto>> GetTelemetryLogsAsync(Guid? firmId = null, CancellationToken ct = default);
}

public class TelemetryLogEntityDto
{
    public Guid Id { get; set; }
    public Guid ConsultancyFirmId { get; set; }
    public string FirmName { get; set; } = string.Empty;
    public int TotalTokensPurchased { get; set; }
    public int TotalTokensConsumed { get; set; }
    public int CurrentTokenBalance { get; set; }
    public int ActiveClientSubscriptions { get; set; }
    public string ImmutabilityStatus { get; set; } = "OK";
    public string LogType { get; set; } = string.Empty;
    public string? TamperErrorMessage { get; set; }
    public string IpAddress { get; set; } = string.Empty;
    public DateTime ReportedAt { get; set; }
}
