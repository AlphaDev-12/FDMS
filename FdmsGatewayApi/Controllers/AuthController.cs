using FdmsGatewayApi.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace FdmsGatewayApi.Controllers;

/// <summary>
/// Admin Dashboard Authentication and PIN Security Endpoints.
/// </summary>
[ApiController]
[Route("api/v1/auth")]
[Produces("application/json")]
[Tags("Authentication & PIN Security")]
public class AuthController : ControllerBase
{
    private readonly IAdminAuthService _authService;
    private readonly IAuditLogger _auditLogger;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        IAdminAuthService authService,
        IAuditLogger auditLogger,
        ILogger<AuthController> logger)
    {
        _authService = authService;
        _auditLogger = auditLogger;
        _logger = logger;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginPinRequest request, CancellationToken cancellationToken)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Pin))
        {
            return BadRequest(new { success = false, message = "PIN is required." });
        }

        var result = await _authService.VerifyPinAsync(request.Pin, cancellationToken);
        if (!result.Success)
        {
            await _auditLogger.LogAsync("AdminPinLoginFailed", "System", 401, errorMessage: result.ErrorMessage, cancellationToken: cancellationToken);
            return Unauthorized(new
            {
                success = false,
                message = result.ErrorMessage,
                remainingAttempts = result.RemainingAttempts,
                lockoutTillUtc = result.LockoutTillUtc
            });
        }

        await _auditLogger.LogAsync("AdminPinLoginSuccess", "System", 200, errorMessage: result.IsDefaultPin ? "User authenticated with DEFAULT PIN ('0000'). Mandatory change required." : "Admin PIN authentication successful.", cancellationToken: cancellationToken);

        return Ok(new
        {
            success = true,
            isDefaultPin = result.IsDefaultPin,
            token = result.Token,
            message = result.IsDefaultPin
                ? "Initial login successful. You must change your default '0000' PIN to proceed."
                : "Authentication successful."
        });
    }

    [HttpPost("change-pin")]
    public async Task<IActionResult> ChangePin([FromBody] ChangePinRequest request, CancellationToken cancellationToken)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.CurrentPin) || string.IsNullOrWhiteSpace(request.NewPin))
        {
            return BadRequest(new { success = false, message = "Both current PIN and new PIN are required." });
        }

        var result = await _authService.ChangePinAsync(request.CurrentPin, request.NewPin, cancellationToken);
        if (!result.Success)
        {
            await _auditLogger.LogAsync("AdminPinChangeFailed", "System", 400, errorMessage: result.ErrorMessage, cancellationToken: cancellationToken);
            return BadRequest(new { success = false, message = result.ErrorMessage });
        }

        await _auditLogger.LogAsync("AdminPinChangeSuccess", "System", 200, errorMessage: "Admin PIN successfully updated and default state cleared.", cancellationToken: cancellationToken);

        return Ok(new
        {
            success = true,
            isDefaultPin = false,
            token = result.Token,
            message = "Admin PIN successfully updated. Secure access granted."
        });
    }

    [HttpPost("reset-pin")]
    public async Task<IActionResult> ResetPin([FromBody] ResetPinRequest request, CancellationToken cancellationToken)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.ResetCode))
        {
            return BadRequest(new { success = false, message = "16-digit master reset code is required." });
        }

        var result = await _authService.ResetPinWithCodeAsync(request.ResetCode, cancellationToken);
        if (!result.Success)
        {
            await _auditLogger.LogAsync("AdminPinResetFailed", "System", 400, errorMessage: result.ErrorMessage, cancellationToken: cancellationToken);
            return BadRequest(new { success = false, message = result.ErrorMessage });
        }

        await _auditLogger.LogAsync("AdminPinResetSuccess", "System", 200, errorMessage: "Admin PIN successfully reset back to default '0000' using Master Reset Code.", cancellationToken: cancellationToken);

        return Ok(new
        {
            success = true,
            isDefaultPin = true,
            message = "Admin PIN reset to default '0000'. You may now log in using '0000' and configure your new PIN."
        });
    }

    [HttpGet("status")]
    public async Task<IActionResult> GetStatus(CancellationToken cancellationToken)
    {
        string? token = ExtractSessionToken();
        bool isValid = _authService.ValidateSession(token ?? string.Empty);

        bool isDefaultPin = false;
        if (isValid)
        {
            // Verify if still in default state
            var verifyResult = await _authService.VerifyPinAsync("0000", cancellationToken);
            // If verify result is success when testing "0000", then default PIN is still active!
            isDefaultPin = verifyResult.Success;
        }

        return Ok(new
        {
            isAuthenticated = isValid,
            isDefaultPin = isDefaultPin
        });
    }

    [HttpPost("logout")]
    public IActionResult Logout()
    {
        string? token = ExtractSessionToken();
        if (!string.IsNullOrWhiteSpace(token))
        {
            _authService.RevokeSession(token);
        }

        return Ok(new { success = true, message = "Successfully logged out." });
    }

    private string? ExtractSessionToken()
    {
        if (Request.Headers.TryGetValue("X-Dashboard-Session", out var headerVal))
        {
            return headerVal.ToString();
        }

        if (Request.Headers.TryGetValue("Authorization", out var authVal))
        {
            string str = authVal.ToString();
            if (str.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                return str.Substring(7).Trim();
            }
        }

        return null;
    }
}

public class LoginPinRequest
{
    public string Pin { get; set; } = string.Empty;
}

public class ChangePinRequest
{
    public string CurrentPin { get; set; } = string.Empty;
    public string NewPin { get; set; } = string.Empty;
}

public class ResetPinRequest
{
    public string ResetCode { get; set; } = string.Empty;
}
