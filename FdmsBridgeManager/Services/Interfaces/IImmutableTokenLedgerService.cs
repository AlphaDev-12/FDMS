using FdmsBridgeManager.Models.Entities;

namespace FdmsBridgeManager.Services.Interfaces;

public interface IImmutableTokenLedgerService
{
    Task<(bool IsValid, string? ErrorMessage)> VerifyLedgerIntegrityAsync(CancellationToken ct = default);
    Task<TokenLedgerStatusDto> GetTokenStatusAsync(CancellationToken ct = default);
    Task<TokenLedgerEntity> TopUpTokensAsync(int tokenAmount, string voucherCode, string tokenType = "Subscription", string createdBy = "Admin", CancellationToken ct = default);
    Task<TokenLedgerEntity> DeductTokensAsync(int maxDevices, int durationMonths, string clientSubscriptionRef, string createdBy = "SubscriptionService", CancellationToken ct = default);
    Task<TokenLedgerEntity> DeductUpgradeTokensAsync(int slotsCount, string clientUpgradeRef, string createdBy = "UpgradeService", CancellationToken ct = default);
    Task TriggerTamperLockAsync(string errorType, string details, CancellationToken ct = default);
}

public class TokenLedgerStatusDto
{
    public int TotalSubscriptionTokensPurchased { get; set; }
    public int TotalSubscriptionTokensConsumed { get; set; }
    public int CurrentSubscriptionTokenBalance { get; set; }

    public int TotalUpgradeTokensPurchased { get; set; }
    public int TotalUpgradeTokensConsumed { get; set; }
    public int CurrentUpgradeTokenBalance { get; set; }

    // Backward compatibility properties
    public int TotalTokensPurchased => TotalSubscriptionTokensPurchased + TotalUpgradeTokensPurchased;
    public int TotalTokensConsumed => TotalSubscriptionTokensConsumed + TotalUpgradeTokensConsumed;
    public int CurrentTokenBalance => CurrentSubscriptionTokenBalance;

    public bool IsTamperDetected { get; set; }
    public string ImmutabilityStatus { get; set; } = "OK"; // OK, TAMPER_DETECTED
    public string? LastTamperError { get; set; }
    public DateTime? LastVerifiedAt { get; set; }
}
