using FdmsGatewayApi.Models.Dtos;
using FdmsGatewayApi.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace FdmsGatewayApi.Controllers;

/// <summary>
/// Fiscal Reports and Hash Chain Verification Audit Endpoints.
/// </summary>
[ApiController]
[Route("api/v1")]
[Produces("application/json")]
[Tags("Fiscal Reports & Audit")]
public class AuditReportController : ControllerBase
{
    private readonly IZReportService _zReportService;
    private readonly IHashChainAuditorService _hashChainAuditor;
    private readonly ILogger<AuditReportController> _logger;

    public AuditReportController(
        IZReportService zReportService,
        IHashChainAuditorService hashChainAuditor,
        ILogger<AuditReportController> logger)
    {
        _zReportService = zReportService;
        _hashChainAuditor = hashChainAuditor;
        _logger = logger;
    }

    /// <summary>
    /// Generate or retrieve a Z-Report summary by Device ID and Date/Fiscal Day Number.
    /// </summary>
    /// <param name="deviceId">Registered ZIMRA Fiscal Device ID.</param>
    /// <param name="date">Target date (optional, defaults to today).</param>
    /// <param name="fiscalDayNo">Specific Fiscal Day Number (optional).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpGet("reports/z-report")]
    [ProducesResponseType(typeof(ZReportResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ZReportResponseDto>> GetZReport(
        [FromQuery] int deviceId,
        [FromQuery] DateTime? date,
        [FromQuery] int? fiscalDayNo,
        CancellationToken cancellationToken = default)
    {
        if (deviceId <= 0)
        {
            return BadRequest(new { error = "Valid deviceId is required." });
        }

        try
        {
            if (fiscalDayNo.HasValue)
            {
                var dayReport = await _zReportService.GenerateZReportByFiscalDayAsync(deviceId, fiscalDayNo.Value, cancellationToken);
                return Ok(dayReport);
            }

            DateTime targetDate = date ?? DateTime.UtcNow.Date;
            var report = await _zReportService.GenerateZReportAsync(deviceId, targetDate, cancellationToken);
            return Ok(report);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate Z-Report for device {DeviceId}", deviceId);
            return StatusCode(500, new { error = "ZREPORT01", message = ex.Message });
        }
    }

    /// <summary>
    /// Perform cryptographic hash chain verification across all documents for a given device.
    /// </summary>
    /// <param name="deviceId">Device ID to audit.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Hash chain integrity verification status, total documents checked, and break index if tampered.</returns>
    [HttpGet("admin/audit/verify-chain/{deviceId}")]
    [ProducesResponseType(typeof(HashChainVerificationResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(HashChainVerificationResultDto), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<HashChainVerificationResultDto>> VerifyHashChain(
        int deviceId,
        CancellationToken cancellationToken = default)
    {
        if (deviceId <= 0)
        {
            return BadRequest(new { error = "Valid deviceId is required." });
        }

        try
        {
            var result = await _hashChainAuditor.VerifyHashChainAsync(deviceId, cancellationToken);
            if (!result.IsChainValid)
            {
                return StatusCode(422, result); // 422 Unprocessable Entity for tamper detection
            }
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to verify hash chain for device {DeviceId}", deviceId);
            return StatusCode(500, new { error = "AUDIT01", message = ex.Message });
        }
    }
}
