namespace FdmsGatewayApi.Services.Interfaces;

public class AdminPinAuthResult
{
    public bool Success { get; set; }
    public bool IsDefaultPin { get; set; }
    public string? Token { get; set; }
    public string? ErrorMessage { get; set; }
    public int RemainingAttempts { get; set; } = 5;
    public DateTime? LockoutTillUtc { get; set; }
}

public interface IAdminAuthService
{
    Task EnsureSeedPinAsync(CancellationToken ct = default);
    Task<AdminPinAuthResult> VerifyPinAsync(string pin, CancellationToken ct = default);
    Task<AdminPinAuthResult> ChangePinAsync(string currentPin, string newPin, CancellationToken ct = default);
    Task<AdminPinAuthResult> ResetPinWithCodeAsync(string resetCode, CancellationToken ct = default);
    bool ValidateSession(string token);
    void RevokeSession(string token);
}
