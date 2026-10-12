using FdmsGatewayApi.Config;
using FdmsGatewayApi.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace FdmsGatewayApi.Controllers;

public class RegisterDevicePayloadDto
{
    public string TenantApiKey { get; set; } = string.Empty;
    public string ZimraDeviceId { get; set; } = string.Empty;
    public string PfxBase64Certificate { get; set; } = string.Empty;
    public string CertificateDecryptionSalt { get; set; } = "default-salt";
    public string? CertificatePassword { get; set; }
}

[ApiController]
[Route("api/v1/partner")]
[Produces("application/json")]
[Tags("Partner Cloud Management")]
public class PartnerAdminController : ControllerBase
{
    private readonly IPartnerManagementService _partnerService;
    private readonly MultiTenancyOptions _options;
    private readonly ILogger<PartnerAdminController> _logger;

    public PartnerAdminController(
        IPartnerManagementService partnerService,
        IOptions<MultiTenancyOptions> options,
        ILogger<PartnerAdminController> logger)
    {
        _partnerService = partnerService;
        _options = options.Value;
        _logger = logger;
    }

    private bool IsPartnerAuthorized(string requestedPartnerKey)
    {
        if (Request.Headers.TryGetValue("X-Partner-Key", out var headerKey) && !string.IsNullOrWhiteSpace(headerKey))
        {
            return string.Equals(headerKey.ToString(), requestedPartnerKey, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(_options.PartnerKey, requestedPartnerKey, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Onboard a new SME Customer under the Partner's Wholesale Account.
    /// </summary>
    [HttpPost("tenants")]
    [ProducesResponseType(typeof(OnboardTenantResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> OnboardTenant([FromBody] OnboardTenantRequestDto request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.PartnerKey))
        {
            request.PartnerKey = _options.PartnerKey;
        }

        if (!IsPartnerAuthorized(request.PartnerKey))
        {
            _logger.LogWarning("[PARTNER AUTH FAILURE] Unauthorized tenant onboarding attempt for PartnerKey '{PartnerKey}'", request.PartnerKey);
            return StatusCode(StatusCodes.Status403Forbidden, new { error = $"Access Denied: You are not authorized to onboard tenants for wholesale partner '{request.PartnerKey}'." });
        }

        if (string.IsNullOrWhiteSpace(request.BusinessName))
        {
            return BadRequest(new { error = "BusinessName is required for onboarding." });
        }

        try
        {
            var response = await _partnerService.OnboardTenantAsync(request, cancellationToken);
            return Ok(response);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Register a ZIMRA Fiscal Device and upload PFX certificate for an onboarded SME Tenant.
    /// </summary>
    [HttpPost("devices")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RegisterDevice([FromBody] RegisterDevicePayloadDto payload, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(payload.TenantApiKey) || string.IsNullOrWhiteSpace(payload.ZimraDeviceId))
        {
            return BadRequest(new { error = "TenantApiKey and ZimraDeviceId are required parameters." });
        }

        byte[] certBytes = Array.Empty<byte>();
        if (!string.IsNullOrWhiteSpace(payload.PfxBase64Certificate))
        {
            try
            {
                certBytes = Convert.FromBase64String(payload.PfxBase64Certificate);
            }
            catch (FormatException)
            {
                return BadRequest(new { error = "PfxBase64Certificate is not a valid base64-encoded string." });
            }
        }

        var request = new RegisterDeviceRequestDto
        {
            TenantApiKey = payload.TenantApiKey,
            ZimraDeviceId = payload.ZimraDeviceId,
            PfxCertificateBytes = certBytes,
            CertificateDecryptionSalt = payload.CertificateDecryptionSalt,
            CertificatePassword = payload.CertificatePassword
        };

        try
        {
            await _partnerService.RegisterDeviceAsync(request, cancellationToken);
            return Ok(new { message = $"Device {payload.ZimraDeviceId} successfully provisioned under TenantApiKey '{payload.TenantApiKey}'." });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Get Partner Wholesale Token Balance and SME Customer Fleet Telemetry.
    /// </summary>
    [HttpGet("dashboard")]
    [ProducesResponseType(typeof(PartnerDashboardSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDashboard([FromQuery] string? partnerKey, CancellationToken cancellationToken = default)
    {
        string targetKey = !string.IsNullOrWhiteSpace(partnerKey) ? partnerKey : _options.PartnerKey;

        if (!IsPartnerAuthorized(targetKey))
        {
            _logger.LogWarning("[PARTNER AUTH FAILURE] Unauthorized dashboard access attempt for PartnerKey '{PartnerKey}'", targetKey);
            return StatusCode(StatusCodes.Status403Forbidden, new { error = $"Access Denied: You are not authorized to view dashboard telemetry for wholesale partner '{targetKey}'." });
        }

        var summary = await _partnerService.GetPartnerDashboardAsync(targetKey, cancellationToken);

        if (summary == null)
        {
            return NotFound(new { error = $"Wholesale Partner account '{targetKey}' was not found." });
        }

        return Ok(summary);
    }
}
