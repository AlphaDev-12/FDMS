using System.Security.Cryptography;
using System.Text;
using FdmsBridgeManager.Data;
using FdmsBridgeManager.Models.Entities;
using FdmsBridgeManager.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace FdmsBridgeManager.Services.Implementation;

public class ImmutableTokenLedgerService : IImmutableTokenLedgerService
{
    private readonly BridgeManagerDbContext _db;
    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ImmutableTokenLedgerService> _logger;
    private readonly string _hmacSecret;

    public ImmutableTokenLedgerService(
        BridgeManagerDbContext db,
        IConfiguration config,
        IHttpClientFactory httpClientFactory,
        ILogger<ImmutableTokenLedgerService> logger)
    {
        _db = db;
        _config = config;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _hmacSecret = _config["BridgeManager:LedgerSecret"] ?? "FDMS-Bridge-Manager-Immutable-Ledger-Secret-2026";
    }

    public async Task<(bool IsValid, string? ErrorMessage)> VerifyLedgerIntegrityAsync(CancellationToken ct = default)
    {
        var entries = await _db.TokenLedgers.OrderBy(l => l.CreatedAt).ToListAsync(ct);
        if (entries.Count == 0)
        {
            return (true, null);
        }

        string expectedPrevHash = "GENESIS_HASH_00000000000000000000000000000000";

        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];

            if (!string.Equals(entry.PreviousHash, expectedPrevHash, StringComparison.OrdinalIgnoreCase))
            {
                string msg = $"Immutability Hash Chain Broken at record ID {entry.Id} (Row #{i + 1}). Expected PreviousHash: '{expectedPrevHash}', Actual: '{entry.PreviousHash}'.";
                await TriggerTamperLockAsync("PREVIOUS_HASH_MISMATCH", msg, ct);
                return (false, msg);
            }

            string computedHash = ComputeEntryHash(entry.PreviousHash, entry.Id, entry.TransactionType, entry.TokenAmount, entry.BalanceAfter, entry.ReferenceKey, entry.CreatedAt, _hmacSecret);
            if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes((entry.Hash ?? string.Empty).ToLowerInvariant()), Encoding.UTF8.GetBytes(computedHash.ToLowerInvariant())))
            {
                string msg = $"Immutability Record Hash Tampered at record ID {entry.Id} (Row #{i + 1}). Computed: '{computedHash}', Recorded Hash in DB: '{entry.Hash}'.";
                await TriggerTamperLockAsync("HASH_SIGNATURE_MISMATCH", msg, ct);
                return (false, msg);
            }

            expectedPrevHash = entry.Hash ?? string.Empty;
        }

        return (true, null);
    }

    public async Task<TokenLedgerStatusDto> GetTokenStatusAsync(CancellationToken ct = default)
    {
        var (isValid, errorMessage) = await VerifyLedgerIntegrityAsync(ct);

        var subTopups = await _db.TokenLedgers.Where(l => (l.TokenType == "Subscription" || string.IsNullOrEmpty(l.TokenType)) && l.TransactionType == "TOPUP").SumAsync(l => l.TokenAmount, ct);
        var subDeductions = await _db.TokenLedgers.Where(l => (l.TokenType == "Subscription" || string.IsNullOrEmpty(l.TokenType)) && l.TransactionType == "DEDUCTION").SumAsync(l => l.TokenAmount, ct);

        var upgTopups = await _db.TokenLedgers.Where(l => l.TokenType == "DeviceUpgrade" && l.TransactionType == "TOPUP").SumAsync(l => l.TokenAmount, ct);
        var upgDeductions = await _db.TokenLedgers.Where(l => l.TokenType == "DeviceUpgrade" && l.TransactionType == "DEDUCTION").SumAsync(l => l.TokenAmount, ct);

        int currentSubBalance = subTopups - subDeductions;
        int currentUpgBalance = upgTopups - upgDeductions;

        return new TokenLedgerStatusDto
        {
            TotalSubscriptionTokensPurchased = subTopups,
            TotalSubscriptionTokensConsumed = subDeductions,
            CurrentSubscriptionTokenBalance = Math.Max(0, currentSubBalance),

            TotalUpgradeTokensPurchased = upgTopups,
            TotalUpgradeTokensConsumed = upgDeductions,
            CurrentUpgradeTokenBalance = Math.Max(0, currentUpgBalance),

            IsTamperDetected = !isValid,
            ImmutabilityStatus = isValid ? "OK" : "TAMPER_DETECTED",
            LastTamperError = errorMessage,
            LastVerifiedAt = DateTime.UtcNow
        };
    }

    public async Task<TokenLedgerEntity> TopUpTokensAsync(int tokenAmount, string voucherCode, string tokenType = "Subscription", string createdBy = "Admin", CancellationToken ct = default)
    {
        if (tokenAmount <= 0) throw new ArgumentException("Top-up token amount must be greater than zero.");

        var (isValid, errorMessage) = await VerifyLedgerIntegrityAsync(ct);
        if (!isValid)
        {
            throw new InvalidOperationException($"EMERGENCY LOCK ACTIVE: Cannot process token top-up. Token Ledger Immutability Violation Detected: {errorMessage}");
        }

        tokenType = string.Equals(tokenType, "DeviceUpgrade", StringComparison.OrdinalIgnoreCase) ? "DeviceUpgrade" : "Subscription";

        var lastEntry = await _db.TokenLedgers.OrderByDescending(l => l.CreatedAt).FirstOrDefaultAsync(ct);
        string prevHash = lastEntry?.Hash ?? "GENESIS_HASH_00000000000000000000000000000000";
        int currentBalance = lastEntry?.BalanceAfter ?? 0;

        int newBalance = currentBalance + tokenAmount;
        Guid newId = Guid.NewGuid();
        DateTime now = DateTime.UtcNow;

        string hash = ComputeEntryHash(prevHash, newId, "TOPUP", tokenAmount, newBalance, voucherCode, now, _hmacSecret);

        var newLedger = new TokenLedgerEntity
        {
            Id = newId,
            TransactionType = "TOPUP",
            TokenType = tokenType,
            TokenAmount = tokenAmount,
            BalanceAfter = newBalance,
            ReferenceKey = voucherCode,
            PreviousHash = prevHash,
            Hash = hash,
            CreatedAt = now,
            CreatedBy = createdBy
        };

        _db.TokenLedgers.Add(newLedger);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Token Ledger TOP-UP ({TokenType}): +{Tokens} tokens via Voucher {Voucher}", tokenType, tokenAmount, voucherCode);

        return newLedger;
    }

    public async Task<TokenLedgerEntity> DeductTokensAsync(int maxDevices, int durationMonths, string clientSubscriptionRef, string createdBy = "SubscriptionService", CancellationToken ct = default)
    {
        if (maxDevices <= 0 || durationMonths <= 0)
        {
            throw new ArgumentException("Max devices and duration months must both be at least 1.");
        }

        int requiredTokens = maxDevices * durationMonths;

        var (isValid, errorMessage) = await VerifyLedgerIntegrityAsync(ct);
        if (!isValid)
        {
            throw new InvalidOperationException($"EMERGENCY LOCK ACTIVE: Token generation blocked due to Token Ledger Immutability Violation: {errorMessage}");
        }

        bool isUnlimited = await IsFirmUnlimitedActiveAsync(ct);
        var status = await GetTokenStatusAsync(ct);
        if (!isUnlimited && status.CurrentSubscriptionTokenBalance < requiredTokens)
        {
            throw new InvalidOperationException($"INSUFFICIENT_SUBSCRIPTION_TOKENS: Generating API Key for {maxDevices} device(s) over {durationMonths} month(s) requires {requiredTokens} subscription tokens. Current balance: {status.CurrentSubscriptionTokenBalance} tokens.");
        }

        var lastEntry = await _db.TokenLedgers.OrderByDescending(l => l.CreatedAt).FirstOrDefaultAsync(ct);
        string prevHash = lastEntry?.Hash ?? "GENESIS_HASH_00000000000000000000000000000000";
        int currentBalance = lastEntry?.BalanceAfter ?? 0;
        int newBalance = currentBalance - requiredTokens;

        Guid newId = Guid.NewGuid();
        DateTime now = DateTime.UtcNow;

        string hash = ComputeEntryHash(prevHash, newId, "DEDUCTION", requiredTokens, newBalance, clientSubscriptionRef, now, _hmacSecret);

        var newLedger = new TokenLedgerEntity
        {
            Id = newId,
            TransactionType = "DEDUCTION",
            TokenType = "Subscription",
            TokenAmount = requiredTokens,
            BalanceAfter = newBalance,
            ReferenceKey = clientSubscriptionRef,
            PreviousHash = prevHash,
            Hash = hash,
            CreatedAt = now,
            CreatedBy = createdBy
        };

        _db.TokenLedgers.Add(newLedger);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Token Ledger DEDUCTION (Subscription): -{Tokens} tokens for Client Sub {Ref} (UnlimitedActive: {Unlimited})", requiredTokens, clientSubscriptionRef, isUnlimited);

        return newLedger;
    }

    public async Task<TokenLedgerEntity> DeductUpgradeTokensAsync(int slotsCount, string clientUpgradeRef, string createdBy = "UpgradeService", CancellationToken ct = default)
    {
        if (slotsCount <= 0)
        {
            throw new ArgumentException("Slots count must be at least 1.");
        }

        var (isValid, errorMessage) = await VerifyLedgerIntegrityAsync(ct);
        if (!isValid)
        {
            throw new InvalidOperationException($"EMERGENCY LOCK ACTIVE: Device Upgrade Key generation blocked due to Token Ledger Immutability Violation: {errorMessage}");
        }

        bool isUnlimited = await IsFirmUnlimitedActiveAsync(ct);
        var status = await GetTokenStatusAsync(ct);
        if (!isUnlimited && status.CurrentUpgradeTokenBalance < slotsCount)
        {
            throw new InvalidOperationException($"INSUFFICIENT_UPGRADE_TOKENS: Generating a Device Upgrade Key for {slotsCount} slot(s) requires {slotsCount} Device Upgrade Tokens ($50/ea). Current balance: {status.CurrentUpgradeTokenBalance} Device Upgrade Tokens. Please purchase additional Device Upgrade Tokens from Bridge Admin.");
        }

        var lastEntry = await _db.TokenLedgers.OrderByDescending(l => l.CreatedAt).FirstOrDefaultAsync(ct);
        string prevHash = lastEntry?.Hash ?? "GENESIS_HASH_00000000000000000000000000000000";
        int currentBalance = lastEntry?.BalanceAfter ?? 0;
        int newBalance = currentBalance - slotsCount;

        Guid newId = Guid.NewGuid();
        DateTime now = DateTime.UtcNow;

        string hash = ComputeEntryHash(prevHash, newId, "DEDUCTION", slotsCount, newBalance, clientUpgradeRef, now, _hmacSecret);

        var newLedger = new TokenLedgerEntity
        {
            Id = newId,
            TransactionType = "DEDUCTION",
            TokenType = "DeviceUpgrade",
            TokenAmount = slotsCount,
            BalanceAfter = newBalance,
            ReferenceKey = clientUpgradeRef,
            PreviousHash = prevHash,
            Hash = hash,
            CreatedAt = now,
            CreatedBy = createdBy
        };

        _db.TokenLedgers.Add(newLedger);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Token Ledger DEDUCTION (DeviceUpgrade): -{Tokens} upgrade tokens for Client Ref {Ref}", slotsCount, clientUpgradeRef);

        return newLedger;
    }

    public async Task TriggerTamperLockAsync(string errorType, string details, CancellationToken ct = default)
    {
        _logger.LogError("CRITICAL IMMUTABILITY VIOLATION DETECTED: {ErrorType} - {Details}", errorType, details);

        bool alreadyLogged = await _db.TokenImmutabilityLogs.AnyAsync(l => l.Details == details, ct);
        if (!alreadyLogged)
        {
            var log = new TokenImmutabilityLogEntity
            {
                Id = Guid.NewGuid(),
                ErrorType = errorType,
                Details = details,
                IsReportedToAdmin = false,
                DetectedAt = DateTime.UtcNow
            };
            _db.TokenImmutabilityLogs.Add(log);
            await _db.SaveChangesAsync(ct);
        }
    }

    private async Task<bool> IsFirmUnlimitedActiveAsync(CancellationToken ct)
    {
        string adminUrl = _config["BridgeManager:BridgeAdminUrl"] ?? "http://localhost:5070";
        string firmApiKey = _config["BridgeManager:FirmApiKey"] ?? "FDMS-FIRM-DEMO2026-KEY99";

        try
        {
            var client = _httpClientFactory.CreateClient("BridgeAdminApi");
            var entitlement = await client.GetFromJsonAsync<FirmEntitlementDto>($"{adminUrl}/api/v1/public/tokens/firm-entitlement?firmApiKey={Uri.EscapeDataString(firmApiKey)}", ct);
            if (entitlement != null && entitlement.IsValid)
            {
                return entitlement.IsUnlimitedActive;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query Firm Entitlement from Bridge Admin ({Url}); falling back to standard balance check", adminUrl);
        }

        return false;
    }

    private static string ComputeEntryHash(string prevHash, Guid id, string type, int amount, int balanceAfter, string refKey, DateTime createdAt, string secret)
    {
        // Truncate to second precision to guarantee DB roundtrip determinism
        var normalizedDate = new DateTime(createdAt.Ticks - (createdAt.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc);
        string raw = $"{(prevHash ?? string.Empty).ToLowerInvariant()}|{id.ToString().ToLowerInvariant()}|{type}|{amount}|{balanceAfter}|{refKey}|{normalizedDate.Ticks}|{secret}";
        using var sha = SHA256.Create();
        byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
