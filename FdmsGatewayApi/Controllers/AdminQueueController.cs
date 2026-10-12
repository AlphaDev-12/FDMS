using FdmsGatewayApi.Data;
using FdmsGatewayApi.Models.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FdmsGatewayApi.Controllers;

/// <summary>
/// Administrative Dead-Letter Queue Management Endpoints.
/// Allows operators to inspect, retry, and manage failed invoice transmissions in the offline queue.
/// </summary>
[ApiController]
[Route("api/v1/admin/queue")]
[Produces("application/json")]
[Tags("Admin Queue Management")]
public class AdminQueueController : ControllerBase
{
    private readonly FdmsDbContext _db;
    private readonly ILogger<AdminQueueController> _logger;

    public AdminQueueController(FdmsDbContext db, ILogger<AdminQueueController> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// List queued and dead-lettered invoices in the offline queue.
    /// </summary>
    [HttpGet("items")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetQueueItems(
        [FromQuery] string? status = null,
        [FromQuery] int? deviceId = null,
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var query = _db.OfflineQueue.AsQueryable();

        if (deviceId.HasValue && deviceId.Value > 0)
        {
            query = query.Where(q => q.DeviceId == deviceId.Value);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(q => q.Status == status);
        }

        var items = await query
            .OrderByDescending(q => q.QueuedAt)
            .Take(Math.Min(limit, 200))
            .ToListAsync(cancellationToken);

        return Ok(new
        {
            totalCount = items.Count,
            statusFilter = status ?? "All",
            deviceIdFilter = deviceId,
            items
        });
    }

    /// <summary>
    /// Retry dead-lettered items by resetting their status to 'Queued' and resetting retry counts.
    /// </summary>
    [HttpPost("retry")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RetryDeadLetterItems([FromQuery] int? deviceId = null, [FromQuery] string? invoiceNo = null, CancellationToken cancellationToken = default)
    {
        var query = _db.OfflineQueue.Where(q => q.Status == "DeadLetter");

        if (deviceId.HasValue)
        {
            query = query.Where(q => q.DeviceId == deviceId.Value);
        }

        if (!string.IsNullOrWhiteSpace(invoiceNo))
        {
            query = query.Where(q => q.InvoiceNo == invoiceNo);
        }

        var deadLetters = await query.ToListAsync(cancellationToken);
        if (deadLetters.Count == 0)
        {
            return NotFound(new { message = "No matching DeadLetter queue items found for re-queueing." });
        }

        foreach (var item in deadLetters)
        {
            item.Status = "Queued";
            item.RetryCount = 0;
            item.ErrorMessage = null;
            item.LastAttemptAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Admin re-queued {Count} dead-letter items for background submission.", deadLetters.Count);

        return Ok(new
        {
            message = $"Successfully re-queued {deadLetters.Count} item(s) for ZIMRA transmission.",
            requeuedCount = deadLetters.Count
        });
    }

    /// <summary>
    /// Purge dead-lettered items from the database.
    /// </summary>
    [HttpDelete("dead-letters")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> PurgeDeadLetterItems(
        [FromQuery] int? deviceId = null,
        CancellationToken cancellationToken = default)
    {
        var query = _db.OfflineQueue.Where(q => q.Status == "DeadLetter");

        if (deviceId.HasValue && deviceId.Value > 0)
        {
            query = query.Where(q => q.DeviceId == deviceId.Value);
        }

        var deadLetters = await query.ToListAsync(cancellationToken);
        if (deadLetters.Count == 0)
        {
            return Ok(new { message = "No DeadLetter items to purge.", purgedCount = 0 });
        }

        _db.OfflineQueue.RemoveRange(deadLetters);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogWarning("Admin purged {Count} dead-letter items from the offline queue.", deadLetters.Count);

        return Ok(new
        {
            message = $"Successfully purged {deadLetters.Count} dead-letter item(s).",
            purgedCount = deadLetters.Count
        });
    }
}
