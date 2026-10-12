using FdmsGatewayApi.Data;
using FdmsGatewayApi.Models.Entities;
using FdmsGatewayApi.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FdmsGatewayApi.Controllers;

/// <summary>
/// Gateway Operational Dashboard and Metrics Telemetry Endpoints.
/// </summary>
[ApiController]
[Route("api/v1/dashboard")]
[Produces("application/json")]
[Tags("Admin & Dashboard Metrics")]
public class DashboardController : ControllerBase
{
    private readonly FdmsDbContext _db;
    private readonly IZimraHealthCheck _healthCheck;
    private readonly IFdmsCryptoService _cryptoService;
    private readonly IAuditLogger _auditLogger;
    private readonly IBrandingService _brandingService;
    private readonly ILogger<DashboardController> _logger;

    public DashboardController(
        FdmsDbContext db,
        IZimraHealthCheck healthCheck,
        IFdmsCryptoService cryptoService,
        IAuditLogger auditLogger,
        IBrandingService brandingService,
        ILogger<DashboardController> logger)
    {
        _db = db;
        _healthCheck = healthCheck;
        _cryptoService = cryptoService;
        _auditLogger = auditLogger;
        _brandingService = brandingService;
        _logger = logger;
    }

    [HttpGet("summary")]
    public async Task<IActionResult> GetDashboardSummary(CancellationToken cancellationToken = default)
    {
        var devices = await _db.DeviceConfigs.ToListAsync(cancellationToken);
        var device = devices.FirstOrDefault();

        var queuedCount = await _db.OfflineQueue.CountAsync(q => q.Status == "Queued" && q.RetryCount == 0 && q.ErrorMessage == null, cancellationToken);
        var processingCount = await _db.OfflineQueue.CountAsync(q => q.Status == "Processing", cancellationToken);
        var failedQueueCount = await _db.OfflineQueue.CountAsync(q => q.Status == "Failed" || q.Status == "DeadLetter" || (q.Status == "Queued" && (q.RetryCount > 0 || q.ErrorMessage != null)), cancellationToken);
        var completedQueueCount = await _db.OfflineQueue.CountAsync(q => q.Status == "Completed", cancellationToken);

        var oldestQueued = await _db.OfflineQueue
            .Where(q => q.Status != "Completed")
            .OrderBy(q => q.QueuedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var totalDocumentsCount = await _db.FiscalDocuments.CountAsync(cancellationToken);
        var totalFiscalizedCount = await _db.FiscalDocuments.CountAsync(d => d.FiscalStatus == "Fiscalized", cancellationToken);
        var totalPendingCount = await _db.FiscalDocuments.CountAsync(d => d.FiscalStatus == "Pending", cancellationToken);
        var totalFailedCount = await _db.FiscalDocuments.CountAsync(d => d.FiscalStatus == "Failed", cancellationToken);

        // Today's documents
        var today = DateTime.UtcNow.Date;
        var todayDocs = await _db.FiscalDocuments
            .Where(d => d.CreatedAt >= today)
            .ToListAsync(cancellationToken);

        var todayUsdSales = todayDocs.Where(d => (d.Currency == "USD" || d.Currency == "842") && d.FiscalStatus == "Fiscalized").Sum(d => d.TotalAmount);
        var todayZigSales = todayDocs.Where(d => (d.Currency == "ZIG" || d.Currency == "ZWG" || d.Currency == "932") && d.FiscalStatus == "Fiscalized").Sum(d => d.TotalAmount);

        var recentDocuments = await _db.FiscalDocuments
            .OrderByDescending(d => d.CreatedAt)
            .Take(10)
            .Select(d => new
            {
                d.Id,
                d.InvoiceNo,
                d.DeviceId,
                d.ReceiptType,
                d.Currency,
                d.TotalAmount,
                d.FiscalDayNo,
                d.ReceiptCounter,
                d.ReceiptGlobalNo,
                d.FiscalStatus,
                d.CreatedAt,
                d.ErrorMessage,
                HasQrCode = !string.IsNullOrEmpty(d.QrCodeUrl)
            })
            .ToListAsync(cancellationToken);

        var recentAuditLogs = await _db.AuditLogs
            .OrderByDescending(a => a.Timestamp)
            .Take(10)
            .ToListAsync(cancellationToken);

        var recentQueueItems = await _db.OfflineQueue
            .OrderByDescending(q => q.QueuedAt)
            .Take(20)
            .Select(q => new
            {
                q.Id,
                q.DeviceId,
                q.InvoiceNo,
                q.ReceiptType,
                q.ReceiptGlobalNo,
                q.Status,
                q.RetryCount,
                q.QueuedAt,
                q.ErrorMessage
            })
            .ToListAsync(cancellationToken);

        return Ok(new
        {
            device = device == null ? null : new
            {
                device.DeviceId,
                device.DeviceSerialNo,
                device.TaxPayerName,
                device.TaxPayerTIN,
                device.FiscalDayStatus,
                device.CurrentFiscalDayNo,
                device.FiscalDayOpenedAt,
                device.CurrentReceiptCounter,
                device.CurrentReceiptGlobalNo,
                device.DeviceOperatingMode,
                device.QrUrl,
                HasCertificate = !string.IsNullOrEmpty(device.CertificatePem),
                HasPrivateKey = !string.IsNullOrEmpty(device.EncryptedPrivateKeyPem)
            },
            devices = devices.Select(d => new
            {
                d.DeviceId,
                d.DeviceSerialNo,
                d.TaxPayerName,
                d.TaxPayerTIN,
                d.FiscalDayStatus,
                d.CurrentFiscalDayNo,
                d.CurrentReceiptCounter,
                d.CurrentReceiptGlobalNo,
                d.DeviceOperatingMode,
                HasCertificate = !string.IsNullOrEmpty(d.CertificatePem)
            }),
            health = new
            {
                isHealthy = _healthCheck.IsHealthy,
                isSimulatedOffline = _healthCheck.IsSimulatedOffline,
                latencyMs = _healthCheck.LastLatencyMs,
                lastCheckedUtc = _healthCheck.LastCheckedUtc,
                lastError = _healthCheck.LastError
            },
            queue = new
            {
                queued = queuedCount,
                processing = processingCount,
                failed = failedQueueCount,
                completed = completedQueueCount,
                totalPending = queuedCount + processingCount + failedQueueCount,
                oldestQueuedAgeSeconds = oldestQueued != null ? (DateTime.UtcNow - oldestQueued.QueuedAt).TotalSeconds : 0,
                oldestQueuedInvoice = oldestQueued?.InvoiceNo
            },
            stats = new
            {
                totalDocuments = totalDocumentsCount,
                totalFiscalized = totalFiscalizedCount,
                totalPending = totalPendingCount,
                totalFailed = totalFailedCount,
                todayCount = todayDocs.Count,
                todayUsdSales,
                todayZigSales
            },
            recentDocuments,
            recentAuditLogs,
            recentQueueItems,
            branding = _brandingService.GetBrandingInfo()
        });
    }

    [HttpGet("mode")]
    public IActionResult GetOperatingMode()
    {
        return Ok(new
        {
            isHealthy = _healthCheck.IsHealthy,
            isSimulatedOffline = _healthCheck.IsSimulatedOffline,
            targetEnvironment = _healthCheck.TargetEnvironment,
            operatingMode = _healthCheck.TargetEnvironment,
            activeBaseUrl = _healthCheck.GetActiveBaseUrl(),
            latencyMs = _healthCheck.LastLatencyMs,
            lastCheckedUtc = _healthCheck.LastCheckedUtc,
            lastError = _healthCheck.LastError
        });
    }

    [HttpPost("mode")]
    public async Task<IActionResult> SetOperatingMode([FromBody] SetOperatingModeRequest request, CancellationToken cancellationToken = default)
    {
        string env = request.TargetEnvironment;
        if (string.IsNullOrWhiteSpace(env))
        {
            env = request.IsOffline == true ? "Simulated" : "Sandbox";
        }

        _healthCheck.SetTargetEnvironment(env);

        var allDevices = await _db.DeviceConfigs.ToListAsync(cancellationToken);
        foreach (var dev in allDevices)
        {
            dev.DeviceOperatingMode = env;
        }
        if (allDevices.Count > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        _logger.LogInformation("Target environment manually toggled to {Environment} via Dashboard.", env);

        await _auditLogger.LogAsync("OperatingModeToggle", "Internal", 200, deviceId: allDevices.FirstOrDefault()?.DeviceId, errorMessage: $"Gateway target environment switched to {env}.", cancellationToken: cancellationToken);

        return Ok(new
        {
            message = $"Gateway target environment switched to {env}.",
            targetEnvironment = _healthCheck.TargetEnvironment,
            isSimulatedOffline = _healthCheck.IsSimulatedOffline,
            isHealthy = _healthCheck.IsHealthy,
            operatingMode = _healthCheck.TargetEnvironment,
            activeBaseUrl = _healthCheck.GetActiveBaseUrl()
        });
    }

    [HttpGet("documents")]
    public async Task<IActionResult> GetDocuments(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 15,
        [FromQuery] string? search = null,
        [FromQuery] string? status = null,
        [FromQuery] string? currency = null,
        CancellationToken cancellationToken = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 15;

        var query = _db.FiscalDocuments.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            search = search.Trim();
            query = query.Where(d => d.InvoiceNo.Contains(search) || d.DeviceId.ToString() == search);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(d => d.FiscalStatus == status);
        }

        if (!string.IsNullOrWhiteSpace(currency))
        {
            query = query.Where(d => d.Currency == currency);
        }

        var totalItems = await query.CountAsync(cancellationToken);
        var totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);

        var items = await query
            .OrderByDescending(d => d.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var dtoList = items.Select(d => new
        {
            d.Id,
            d.InvoiceNo,
            d.DeviceId,
            d.ReceiptType,
            d.Currency,
            d.TotalAmount,
            d.FiscalDayNo,
            d.ReceiptCounter,
            d.ReceiptGlobalNo,
            d.ReceiptID,
            d.DeviceHash,
            d.DeviceSignature,
            d.FdmsSignature,
            d.QrCodeUrl,
            QrCodeBase64 = !string.IsNullOrEmpty(d.QrCodeUrl)
                ? _cryptoService.GenerateQrCodeBase64Image(d.QrCodeUrl)
                : string.Empty,
            d.FiscalStatus,
            d.OperationID,
            d.ErrorMessage,
            d.CreatedAt,
            d.ServerDate
        });

        return Ok(new
        {
            page,
            pageSize,
            totalItems,
            totalPages,
            items = dtoList
        });
    }

    [HttpGet("queue")]
    public async Task<IActionResult> GetQueueItems(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? search = null,
        [FromQuery] string? status = null,
        [FromQuery] int? deviceId = null,
        CancellationToken cancellationToken = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 10;

        var query = _db.OfflineQueue.AsQueryable();

        if (deviceId.HasValue && deviceId.Value > 0)
        {
            query = query.Where(q => q.DeviceId == deviceId.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            search = search.Trim();
            query = query.Where(q => q.InvoiceNo.Contains(search) || q.DeviceId.ToString() == search);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            status = status.Trim();
            if (status == "Failed")
            {
                query = query.Where(q => q.Status == "Failed" || q.Status == "DeadLetter");
            }
            else
            {
                query = query.Where(q => q.Status == status);
            }
        }

        var totalItems = await query.CountAsync(cancellationToken);
        var totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);

        var items = await query
            .OrderByDescending(q => q.QueuedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(q => new
            {
                q.Id,
                q.DeviceId,
                q.InvoiceNo,
                q.ReceiptType,
                q.ReceiptGlobalNo,
                q.Status,
                q.RetryCount,
                q.QueuedAt,
                q.ErrorMessage
            })
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

    [HttpPost("queue/retry")]
    public async Task<IActionResult> RetryFailedQueueItems(
        [FromQuery] int? deviceId = null,
        CancellationToken cancellationToken = default)
    {
        var query = _db.OfflineQueue
            .Where(q => q.Status == "Failed" || q.Status == "Queued" || (q.Status == "Processing" && q.QueuedAt < DateTime.UtcNow.AddMinutes(-5)));

        if (deviceId.HasValue && deviceId.Value > 0)
        {
            query = query.Where(q => q.DeviceId == deviceId.Value);
        }

        var failedItems = await query.ToListAsync(cancellationToken);

        if (failedItems.Count == 0)
        {
            return Ok(new { message = "No queued or failed items found to process.", resetCount = 0 });
        }

        foreach (var item in failedItems)
        {
            item.Status = "Queued";
            item.RetryCount = 0;
            item.ErrorMessage = null;
        }

        await _db.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync("QueueForceRetry", "Internal", 200, deviceId: deviceId, errorMessage: $"Manual queue force-retry triggered. Reset {failedItems.Count} offline queue items to 'Queued' state for background processing.", cancellationToken: cancellationToken);

        _logger.LogInformation("Reset {Count} offline queue items back to 'Queued' state for background processing.", failedItems.Count);

        return Ok(new
        {
            message = $"Successfully queued {failedItems.Count} offline items for immediate ZIMRA dispatch.",
            resetCount = failedItems.Count
        });
    }
}

public class SetOperatingModeRequest
{
    public bool? IsOffline { get; set; }
    public string? TargetEnvironment { get; set; }
}
