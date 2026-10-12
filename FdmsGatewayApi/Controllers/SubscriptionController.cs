using FdmsGatewayApi.Models.Dtos;
using FdmsGatewayApi.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace FdmsGatewayApi.Controllers;

/// <summary>
/// Gateway License and Subscription Management Endpoints.
/// </summary>
[ApiController]
[Route("api/v1/subscription")]
[Produces("application/json")]
[Tags("License & Subscriptions")]
public class SubscriptionController : ControllerBase
{
    private readonly ISubscriptionService _subscriptionService;
    private readonly ILogger<SubscriptionController> _logger;

    public SubscriptionController(
        ISubscriptionService subscriptionService,
        ILogger<SubscriptionController> logger)
    {
        _subscriptionService = subscriptionService;
        _logger = logger;
    }

    /// <summary>
    /// Retrieves current subscription details, validity dates, grace period status, and remaining days.
    /// </summary>
    [HttpGet("status")]
    public async Task<ActionResult<SubscriptionStatusDto>> GetStatus(CancellationToken ct)
    {
        var status = await _subscriptionService.GetCurrentSubscriptionStatusAsync(ct);
        return Ok(status);
    }

    /// <summary>
    /// Activates a new cryptographically signed subscription key upon renewal.
    /// </summary>
    [HttpPost("activate")]
    public async Task<ActionResult<SubscriptionStatusDto>> ActivateSubscription([FromBody] ActivateSubscriptionRequest request, CancellationToken ct)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.ApiKey))
        {
            return BadRequest(new { error = "SUB_01", message = "Subscription API Key is required." });
        }

        try
        {
            var updatedStatus = await _subscriptionService.ActivateSubscriptionAsync(request.ApiKey, User.Identity?.Name ?? "DashboardUser", ct);
            return Ok(updatedStatus);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Failed subscription activation attempt.");
            return BadRequest(new { error = "SUB_02", message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = "SUB_03", message = ex.Message });
        }
    }

    /// <summary>
    /// Applies a cryptographically signed Device Capacity Upgrade Key (FDMS-UPG-...) to expand the drive's device limit up to the tier hard cap.
    /// </summary>
    [HttpPost("upgrade-capacity")]
    public async Task<ActionResult<SubscriptionStatusDto>> ApplyDeviceUpgrade([FromBody] ApplyDeviceUpgradeRequest request, CancellationToken ct)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.UpgradeKey))
        {
            return BadRequest(new { error = "UPG_01", message = "Device Upgrade Key is required." });
        }

        try
        {
            var updatedStatus = await _subscriptionService.ApplyDeviceUpgradeKeyAsync(request.UpgradeKey, User.Identity?.Name ?? "DashboardAdmin", ct);
            return Ok(updatedStatus);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Failed device capacity upgrade attempt.");
            return BadRequest(new { error = "UPG_02", message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = "UPG_03", message = ex.Message });
        }
    }

    /// <summary>
    /// Developer / Admin utility to generate a signed subscription API key valid for 1 month, 1 year, or custom duration.
    /// </summary>
    [HttpPost("generate-dev-key")]
    public ActionResult<GenerateSubscriptionKeyResponse> GenerateDevKey([FromBody] GenerateSubscriptionKeyRequest request)
    {
        if (request == null)
        {
            request = new GenerateSubscriptionKeyRequest();
        }

        var keyResponse = _subscriptionService.GenerateSubscriptionKey(request);
        return Ok(keyResponse);
    }
}
