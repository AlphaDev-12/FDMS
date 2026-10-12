using FdmsGatewayApi.Data;
using FdmsGatewayApi.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FdmsGatewayApi.Controllers;

/// <summary>
/// System Diagnostics, Health Checks and Operational Queue Telemetry Endpoints.
/// </summary>
[ApiController]
[Route("api/v1/diagnostics")]
[Produces("application/json")]
[Tags("System Diagnostics & Health")]
public class DiagnosticsController : ControllerBase
{
    private readonly FdmsDbContext _db;
    private readonly IZimraHealthCheck _healthCheck;
    private readonly ILogger<DiagnosticsController> _logger;

    public DiagnosticsController(
        FdmsDbContext db,
        IZimraHealthCheck healthCheck,
        ILogger<DiagnosticsController> logger)
    {
        _db = db;
        _healthCheck = healthCheck;
        _logger = logger;
    }

    /// <summary>
    /// Check ZIMRA Server Connectivity and Gateway Latency.
    /// </summary>
    [HttpGet("health")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetHealth()
    {
        return Ok(new
        {
            status = _healthCheck.IsHealthy ? "Healthy" : "Degraded",
            zimraReachable = _healthCheck.IsHealthy,
            lastCheckedUtc = _healthCheck.LastCheckedUtc,
            latencyMs = _healthCheck.LastLatencyMs,
            lastError = _healthCheck.LastError,
            timestampUtc = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Get metrics for the resilient offline document ingestion queue.
    /// </summary>
    [HttpGet("queue-status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetQueueStatus(CancellationToken cancellationToken = default)
    {
        var queuedCount = await _db.OfflineQueue.CountAsync(q => q.Status == "Queued", cancellationToken);
        var processingCount = await _db.OfflineQueue.CountAsync(q => q.Status == "Processing", cancellationToken);
        var failedCount = await _db.OfflineQueue.CountAsync(q => q.Status == "Failed", cancellationToken);
        var completedCount = await _db.OfflineQueue.CountAsync(q => q.Status == "Completed", cancellationToken);

        var oldestQueued = await _db.OfflineQueue
            .Where(q => q.Status == "Queued")
            .OrderBy(q => q.QueuedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return Ok(new
        {
            queued = queuedCount,
            processing = processingCount,
            failed = failedCount,
            completed = completedCount,
            oldestQueuedAgeSeconds = oldestQueued != null ? (DateTime.UtcNow - oldestQueued.QueuedAt).TotalSeconds : 0,
            oldestQueuedInvoice = oldestQueued?.InvoiceNo
        });
    }

    /// <summary>
    /// Query paginated API audit logs and transaction traces.
    /// </summary>
    [HttpGet("audit-log")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAuditLog(
        [FromQuery] string? invoiceNo = null,
        [FromQuery] string? eventType = null,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 15,
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 15;

        var query = _db.AuditLogs.AsQueryable();

        if (!string.IsNullOrWhiteSpace(invoiceNo))
        {
            var search = invoiceNo.Trim();
            query = query.Where(a => (a.InvoiceNo != null && a.InvoiceNo.Contains(search))
                                  || (a.Endpoint != null && a.Endpoint.Contains(search))
                                  || (a.ErrorMessage != null && a.ErrorMessage.Contains(search))
                                  || (a.ErrorCode != null && a.ErrorCode.Contains(search))
                                  || (a.Direction != null && a.Direction.Contains(search)));
        }

        if (!string.IsNullOrWhiteSpace(eventType))
        {
            var typeSearch = eventType.Trim();
            query = query.Where(a => (a.Direction != null && a.Direction.Contains(typeSearch))
                                  || (a.Endpoint != null && a.Endpoint.Contains(typeSearch))
                                  || (a.ErrorCategory != null && a.ErrorCategory.Contains(typeSearch))
                                  || (a.ErrorCode != null && a.ErrorCode.Contains(typeSearch)));
        }

        if (fromDate.HasValue)
        {
            var startUtc = DateTime.SpecifyKind(fromDate.Value.Date, DateTimeKind.Utc);
            query = query.Where(a => a.Timestamp >= startUtc);
        }

        if (toDate.HasValue)
        {
            var endUtc = DateTime.SpecifyKind(toDate.Value.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc);
            query = query.Where(a => a.Timestamp <= endUtc);
        }

        var totalItems = await query.CountAsync(cancellationToken);
        var totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);

        var items = await query
            .OrderByDescending(a => a.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return Ok(new
        {
            page,
            pageSize,
            totalItems,
            totalPages,
            items
        });
    }

    /// <summary>
    /// Get real-time daily fiscal summary metrics for a specific device.
    /// </summary>
    [HttpGet("day-summary/{deviceId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDaySummary(int deviceId, CancellationToken cancellationToken = default)
    {
        var device = await _db.DeviceConfigs.FirstOrDefaultAsync(d => d.DeviceId == deviceId, cancellationToken);
        if (device == null)
        {
            return NotFound(new { error = $"Device {deviceId} not found." });
        }

        var todayDocuments = await _db.FiscalDocuments
            .Where(d => d.DeviceId == deviceId && d.FiscalDayNo == device.CurrentFiscalDayNo)
            .ToListAsync(cancellationToken);

        return Ok(new
        {
            deviceId = device.DeviceId,
            taxPayerName = device.TaxPayerName,
            taxPayerTIN = device.TaxPayerTIN,
            fiscalDayNo = device.CurrentFiscalDayNo,
            fiscalDayStatus = device.FiscalDayStatus,
            fiscalDayOpenedAt = device.FiscalDayOpenedAt,
            currentReceiptCounter = device.CurrentReceiptCounter,
            currentReceiptGlobalNo = device.CurrentReceiptGlobalNo,
            todayFiscalizedCount = todayDocuments.Count(d => d.FiscalStatus == "Fiscalized"),
            todayPendingCount = todayDocuments.Count(d => d.FiscalStatus == "Pending"),
            todayFailedCount = todayDocuments.Count(d => d.FiscalStatus == "Failed"),
            todayTotalSalesUSD = todayDocuments.Where(d => d.Currency == "USD" && d.FiscalStatus == "Fiscalized").Sum(d => d.TotalAmount),
            todayTotalSalesZIG = todayDocuments.Where(d => d.Currency == "ZIG" && d.FiscalStatus == "Fiscalized").Sum(d => d.TotalAmount)
        });
    }
}
