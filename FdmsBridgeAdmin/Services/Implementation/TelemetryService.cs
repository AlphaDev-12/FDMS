using FdmsBridgeAdmin.Data;
using FdmsBridgeAdmin.Models.Dtos;
using FdmsBridgeAdmin.Models.Entities;
using FdmsBridgeAdmin.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace FdmsBridgeAdmin.Services.Implementation;

public class TelemetryService : ITelemetryService
{
    private readonly BridgeAdminDbContext _db;
    private readonly ILogger<TelemetryService> _logger;

    public TelemetryService(BridgeAdminDbContext db, ILogger<TelemetryService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task RecordHeartbeatAsync(TelemetryHeartbeatRequest req, string clientIp, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.FirmApiKey))
        {
            throw new ArgumentException("Firm API Key required for telemetry reporting.");
        }

        var firm = await _db.ConsultancyFirms.FirstOrDefaultAsync(f => f.FirmApiKey == req.FirmApiKey.Trim(), ct);
        if (firm == null)
        {
            _logger.LogWarning("Received heartbeat from unknown Firm API Key: {ApiKey}", req.FirmApiKey);
            return;
        }

        // Update firm state
        firm.TotalTokensPurchased = req.TotalTokensPurchased;
        if (req.TotalSubscriptionTokensPurchased > 0 || req.TotalDeviceUpgradeTokensPurchased > 0)
        {
            firm.TotalSubscriptionTokensPurchased = req.TotalSubscriptionTokensPurchased;
            firm.TotalDeviceUpgradeTokensPurchased = req.TotalDeviceUpgradeTokensPurchased;
        }
        firm.TotalTokensConsumed = req.TotalTokensConsumed;
        firm.CurrentTokenBalance = req.CurrentTokenBalance;
        firm.ImmutabilityStatus = req.ImmutabilityStatus;
        firm.LastHeartbeatAt = DateTime.UtcNow;

        var logEntry = new TelemetryLogEntity
        {
            Id = Guid.NewGuid(),
            ConsultancyFirmId = firm.Id,
            FirmName = firm.FirmName,
            TotalTokensPurchased = req.TotalTokensPurchased,
            TotalTokensConsumed = req.TotalTokensConsumed,
            CurrentTokenBalance = req.CurrentTokenBalance,
            ActiveClientSubscriptions = req.ActiveClientSubscriptions,
            ImmutabilityStatus = req.ImmutabilityStatus,
            LogType = string.IsNullOrWhiteSpace(req.LogType) ? "24H_Heartbeat" : req.LogType,
            TamperErrorMessage = req.TamperErrorMessage,
            IpAddress = clientIp,
            ReportedAt = DateTime.UtcNow
        };

        _db.TelemetryLogs.Add(logEntry);
        await _db.SaveChangesAsync(ct);

        if (req.ImmutabilityStatus == "TAMPER_DETECTED")
        {
            _logger.LogError("CRITICAL ALERT: Immutability tamper error reported by Firm '{FirmName}' ({Id})! Error: {Error}", firm.FirmName, firm.Id, req.TamperErrorMessage);
        }
        else
        {
            _logger.LogInformation("Recorded 24H telemetry heartbeat from Firm '{FirmName}': Balance={Balance}, Consumed={Consumed}", firm.FirmName, req.CurrentTokenBalance, req.TotalTokensConsumed);
        }
    }

    public async Task<List<TelemetryLogEntityDto>> GetTelemetryLogsAsync(Guid? firmId = null, CancellationToken ct = default)
    {
        var query = _db.TelemetryLogs.AsQueryable();
        if (firmId.HasValue)
        {
            query = query.Where(t => t.ConsultancyFirmId == firmId.Value);
        }

        var list = await query.OrderByDescending(t => t.ReportedAt).Take(200).ToListAsync(ct);
        return list.Select(t => new TelemetryLogEntityDto
        {
            Id = t.Id,
            ConsultancyFirmId = t.ConsultancyFirmId,
            FirmName = t.FirmName,
            TotalTokensPurchased = t.TotalTokensPurchased,
            TotalTokensConsumed = t.TotalTokensConsumed,
            CurrentTokenBalance = t.CurrentTokenBalance,
            ActiveClientSubscriptions = t.ActiveClientSubscriptions,
            ImmutabilityStatus = t.ImmutabilityStatus,
            LogType = t.LogType,
            TamperErrorMessage = t.TamperErrorMessage,
            IpAddress = t.IpAddress,
            ReportedAt = t.ReportedAt
        }).ToList();
    }
}
