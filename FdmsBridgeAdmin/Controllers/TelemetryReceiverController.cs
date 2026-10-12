using FdmsBridgeAdmin.Models.Dtos;
using FdmsBridgeAdmin.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace FdmsBridgeAdmin.Controllers;

[ApiController]
[Route("api/v1/telemetry")]
public class TelemetryReceiverController : ControllerBase
{
    private readonly ITelemetryService _telemetryService;

    public TelemetryReceiverController(ITelemetryService telemetryService)
    {
        _telemetryService = telemetryService;
    }

    [HttpPost("heartbeat")]
    public async Task<ActionResult> ReceiveHeartbeat([FromBody] TelemetryHeartbeatRequest req, CancellationToken ct)
    {
        try
        {
            string clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
            await _telemetryService.RecordHeartbeatAsync(req, clientIp, ct);
            return Ok(new { status = "Received", timestamp = DateTime.UtcNow });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("logs")]
    public async Task<ActionResult<List<TelemetryLogEntityDto>>> GetTelemetryLogs([FromQuery] Guid? firmId, CancellationToken ct)
    {
        var logs = await _telemetryService.GetTelemetryLogsAsync(firmId, ct);
        return Ok(logs);
    }
}
