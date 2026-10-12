using FdmsGatewayApi.Models.Dtos;
using FdmsGatewayApi.Models.Entities;
using FdmsGatewayApi.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace FdmsGatewayApi.Controllers;

/// <summary>
/// ZIMRA Native Batch Offline File Submission &amp; Status Polling Endpoints (Sections 4.8, 4.9, 4.10).
/// </summary>
[ApiController]
[Route("api/v1/offline-files")]
[Produces("application/json")]
[Tags("ZIMRA Batch Offline Operations")]
public class OfflineFilesController : ControllerBase
{
    private readonly IFdmsOfflineFileService _offlineFileService;
    private readonly ILogger<OfflineFilesController> _logger;

    public OfflineFilesController(
        IFdmsOfflineFileService offlineFileService,
        ILogger<OfflineFilesController> logger)
    {
        _offlineFileService = offlineFileService;
        _logger = logger;
    }

    /// <summary>
    /// Package pending queued receipts into a base64 ZIMRA submitFile payload and transmit to ZIMRA FDMS Gateway.
    /// </summary>
    /// <param name="deviceId">Device ID.</param>
    /// <param name="fiscalDayNo">Optional fiscal day number override.</param>
    /// <param name="includeFooter">Whether to append Z-Report closing footer counters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpPost("submit/{deviceId}")]
    [ProducesResponseType(typeof(OfflineFileEntity), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SubmitOfflineFile(
        int deviceId,
        [FromQuery] int? fiscalDayNo = null,
        [FromQuery] bool includeFooter = false,
        CancellationToken cancellationToken = default)
    {
        if (deviceId <= 0)
        {
            return BadRequest(new { error = "Valid deviceId is required." });
        }

        try
        {
            var result = await _offlineFileService.PackageAndSubmitOfflineBatchAsync(
                deviceId, fiscalDayNo, includeFooter, cancellationToken);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to submit offline file for Device {DeviceId}", deviceId);
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Poll ZIMRA for processing status of submitted offline files for a device via getFileStatus.
    /// </summary>
    /// <param name="deviceId">Device ID.</param>
    /// <param name="operationId">Optional specific operation ID.</param>
    /// <param name="from">Optional start date (defaults to last 7 days).</param>
    /// <param name="till">Optional end date (defaults to current time).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpGet("status/{deviceId}")]
    [ProducesResponseType(typeof(List<FileStatusItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetFileStatus(
        int deviceId,
        [FromQuery] string? operationId = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? till = null,
        CancellationToken cancellationToken = default)
    {
        if (deviceId <= 0)
        {
            return BadRequest(new { error = "Valid deviceId is required." });
        }

        try
        {
            var statuses = await _offlineFileService.CheckFileStatusAsync(
                deviceId, operationId, from, till, cancellationToken);
            return Ok(statuses);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch file status for Device {DeviceId}", deviceId);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Get local database submission logs of offline batch files for a device.
    /// </summary>
    /// <param name="deviceId">Device ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpGet("history/{deviceId}")]
    [ProducesResponseType(typeof(List<OfflineFileEntity>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOfflineFileHistory(int deviceId, CancellationToken cancellationToken = default)
    {
        if (deviceId <= 0)
        {
            return BadRequest(new { error = "Valid deviceId is required." });
        }

        var history = await _offlineFileService.GetLocalOfflineFileHistoryAsync(deviceId, cancellationToken);
        return Ok(history);
    }
}
