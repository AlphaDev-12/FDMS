using FdmsGatewayApi.Data;
using FdmsGatewayApi.Models.Dtos;
using FdmsGatewayApi.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FdmsGatewayApi.Controllers;

/// <summary>
/// Tenant-Isolated Operations and Fiscal Ledger Dashboard Endpoints.
/// </summary>
[ApiController]
[Route("api/v1/tenant/dashboard")]
[Produces("application/json")]
[Tags("Multi-Tenant Operations Dashboard")]
public class TenantDashboardController : ControllerBase
{
    private readonly FdmsDbContext _db;
    private readonly ITenantRegistryService _tenantRegistry;
    private readonly IFdmsCryptoService _cryptoService;
    private readonly ILogger<TenantDashboardController> _logger;

    public TenantDashboardController(
        FdmsDbContext db,
        ITenantRegistryService tenantRegistry,
        IFdmsCryptoService cryptoService,
        ILogger<TenantDashboardController> logger)
    {
        _db = db;
        _tenantRegistry = tenantRegistry;
        _cryptoService = cryptoService;
        _logger = logger;
    }

    [HttpGet("summary")]
    public async Task<IActionResult> GetTenantSummary([FromQuery] string? tenantApiKey, CancellationToken cancellationToken)
    {
        var tenantContext = await ResolveTenantContextAsync(tenantApiKey, cancellationToken);
        if (tenantContext == null)
        {
            return Unauthorized(new { message = "Invalid or missing tenant authentication context." });
        }

        int tenantId = tenantContext.Value.TenantId;

        var account = await _db.TenantBusinessAccounts
            .FirstOrDefaultAsync(t => t.TenantId == tenantId, cancellationToken);

        if (account == null || !account.IsActive)
        {
            return StatusCode(403, new { message = "Tenant business account is inactive or token balance depleted." });
        }

        var devices = await _db.TenantZimraDevices
            .Where(d => d.TenantId == tenantId)
            .Select(d => new TenantDeviceSummaryDto
            {
                ZimraDeviceId = d.ZimraDeviceId,
                LastSequenceNumber = d.LastSequenceNumber,
                IsActive = d.IsActive,
                CreatedAt = d.CreatedAt
            })
            .ToListAsync(cancellationToken);

        var ledgerQuery = _db.MultiTenantFiscalLedger
            .Where(l => l.TenantId == tenantId);

        var totalDocuments = await ledgerQuery.CountAsync(cancellationToken);
        var totalFiscalized = await ledgerQuery.CountAsync(l => l.ZimraSyncStatus == "SYNCED" || l.ZimraSyncStatus == "Fiscalized", cancellationToken);
        var totalPending = await ledgerQuery.CountAsync(l => l.ZimraSyncStatus == "PENDING" || l.ZimraSyncStatus == "Pending", cancellationToken);
        var totalFailed = await ledgerQuery.CountAsync(l => l.ZimraSyncStatus == "FAILED" || l.ZimraSyncStatus == "Failed", cancellationToken);

        var today = DateTime.UtcNow.Date;
        var todayLedger = await ledgerQuery
            .Where(l => l.CreatedAt >= today)
            .ToListAsync(cancellationToken);

        var todayUsdSales = todayLedger
            .Where(l => l.CurrencyCode == "USD" || l.CurrencyCode == "842")
            .Sum(l => l.GrossInclusiveAmount);

        var todayZigSales = todayLedger
            .Where(l => l.CurrencyCode == "ZIG" || l.CurrencyCode == "ZWG" || l.CurrencyCode == "932")
            .Sum(l => l.GrossInclusiveAmount);

        return Ok(new TenantDashboardSummaryDto
        {
            TenantId = tenantId,
            BusinessName = account.BusinessName,
            TotalDocuments = totalDocuments,
            TotalFiscalized = totalFiscalized,
            TotalPending = totalPending,
            TotalFailed = totalFailed,
            TodayUsdSales = todayUsdSales,
            TodayZigSales = todayZigSales,
            RegisteredDevicesCount = devices.Count,
            Devices = devices
        });
    }

    [HttpGet("documents")]
    public async Task<IActionResult> GetTenantDocuments(
        [FromQuery] string? tenantApiKey,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 15,
        [FromQuery] string? search = null,
        [FromQuery] string? deviceId = null,
        CancellationToken cancellationToken = default)
    {
        var tenantContext = await ResolveTenantContextAsync(tenantApiKey, cancellationToken);
        if (tenantContext == null)
        {
            return Unauthorized(new { message = "Invalid or missing tenant authentication context." });
        }

        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 15;

        int tenantId = tenantContext.Value.TenantId;
        var query = _db.MultiTenantFiscalLedger.Where(l => l.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            search = search.Trim();
            query = query.Where(l => l.InvoiceNumber.Contains(search) || l.InvoiceUuid.Contains(search));
        }

        if (!string.IsNullOrWhiteSpace(deviceId))
        {
            query = query.Where(l => l.ZimraDeviceId == deviceId.Trim());
        }

        var totalItems = await query.CountAsync(cancellationToken);
        var totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);

        var items = await query
            .OrderByDescending(l => l.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(l => new
            {
                l.Id,
                l.TenantId,
                l.ZimraDeviceId,
                l.InvoiceUuid,
                l.InvoiceNumber,
                l.CurrencyCode,
                l.GrossInclusiveAmount,
                l.CryptographicSignature,
                l.ZimraSyncStatus,
                l.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return Ok(new
        {
            tenantId,
            page,
            pageSize,
            totalItems,
            totalPages,
            items
        });
    }

    [HttpGet("devices")]
    public async Task<IActionResult> GetTenantDevices([FromQuery] string? tenantApiKey, CancellationToken cancellationToken)
    {
        var tenantContext = await ResolveTenantContextAsync(tenantApiKey, cancellationToken);
        if (tenantContext == null)
        {
            return Unauthorized(new { message = "Invalid or missing tenant authentication context." });
        }

        int tenantId = tenantContext.Value.TenantId;
        var devices = await _db.TenantZimraDevices
            .Where(d => d.TenantId == tenantId)
            .Select(d => new
            {
                d.Id,
                d.TenantId,
                d.ZimraDeviceId,
                d.LastSequenceNumber,
                d.IsActive,
                d.CreatedAt,
                HasCertificate = d.EncryptedPfxCertificate != null && d.EncryptedPfxCertificate.Length > 0
            })
            .ToListAsync(cancellationToken);

        return Ok(devices);
    }

    private async Task<(int TenantId, string ApiKey)?> ResolveTenantContextAsync(string? queryApiKey, CancellationToken cancellationToken)
    {
        // 1. Try session token from header
        string? sessionToken = null;
        if (Request.Headers.TryGetValue("X-Tenant-Session", out var sessHeader))
        {
            sessionToken = sessHeader.ToString();
        }
        else if (Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            string str = authHeader.ToString();
            if (str.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                sessionToken = str.Substring(7).Trim();
            }
        }

        if (!string.IsNullOrWhiteSpace(sessionToken))
        {
            var parsed = ParseToken(sessionToken);
            if (parsed != null)
            {
                var user = await _db.TenantUsers
                    .Include(u => u.TenantAccount)
                    .FirstOrDefaultAsync(u => u.Id == parsed.Value.UserId && u.TenantId == parsed.Value.TenantId, cancellationToken);

                if (user != null && user.IsActive && user.TenantAccount != null && user.TenantAccount.IsActive)
                {
                    return (user.TenantId, user.TenantAccount.TenantApiKey);
                }
            }
        }

        // 2. Fallback: API key from header or query string
        string? apiKey = queryApiKey;
        if (string.IsNullOrWhiteSpace(apiKey) && Request.Headers.TryGetValue("X-Tenant-Key", out var tenantKeyHeader))
        {
            apiKey = tenantKeyHeader.ToString();
        }

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            var context = await _tenantRegistry.GetTenantContextAsync(apiKey, cancellationToken);
            if (context != null)
            {
                return (context.TenantId, context.TenantApiKey);
            }
        }

        return null;
    }

    private static (int UserId, int TenantId)? ParseToken(string token)
    {
        try
        {
            var parts = token.Split(':');
            if (parts.Length == 4 && parts[0] == "TENANT_SESSION")
            {
                int userId = int.Parse(parts[1]);
                int tenantId = int.Parse(parts[2]);
                long expiryTicks = long.Parse(parts[3]);
                if (new DateTime(expiryTicks, DateTimeKind.Utc) > DateTime.UtcNow)
                {
                    return (userId, tenantId);
                }
            }
        }
        catch { }
        return null;
    }
}
