namespace FdmsBridgeManager.Services.Interfaces;

public interface ITokenPurchaseService
{
    Task<TokenPricingInfoDto> GetTokenPricingInfoAsync(CancellationToken ct = default);
    Task<InitiateTokenPurchaseResponseDto> InitiateTokenPurchaseAsync(int tokenCount, string buyerEmail, string tokenType = "Subscription", CancellationToken ct = default);
    Task<RedeemTokenVoucherResponseDto> RedeemTokenMasterKeyAsync(string voucherCode, string redeemedBy = "AdminUser", CancellationToken ct = default);
    Task<FirmEntitlementDto> GetFirmEntitlementAsync(CancellationToken ct = default);
}

public class FirmEntitlementDto
{
    public bool IsValid { get; set; }
    public string FirmApiKey { get; set; } = string.Empty;
    public string FirmName { get; set; } = string.Empty;
    public string UnlimitedTier { get; set; } = "None"; // None, Annual, Completely
    public DateTime? UnlimitedExpiresAt { get; set; }
    public bool IsUnlimitedActive { get; set; }
    public int CurrentTokenBalance { get; set; }
    public bool IsActive { get; set; }
}

public class TokenPricingInfoDto
{
    public decimal PricePerTokenUSD { get; set; } = 5.00m;
    public decimal PricePerDeviceUpgradeTokenUSD { get; set; } = 50.00m;
    public string UnitDescription { get; set; } = "1 Subscription Token = 1 Device/Mo, 1 Upgrade Token = +1 Capacity Slot";
}

public class InitiateTokenPurchaseResponseDto
{
    public string TransactionId { get; set; } = string.Empty;
    public string PayNowRedirectUrl { get; set; } = string.Empty;
    public string PayNowReference { get; set; } = string.Empty;
    public int TokenCount { get; set; }
    public string TokenType { get; set; } = "Subscription";
    public decimal UnitPriceUSD { get; set; }
    public decimal TotalPriceUSD { get; set; }
}

public class RedeemTokenVoucherResponseDto
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string TokenType { get; set; } = "Subscription";
    public int TokensAdded { get; set; }
    public int NewTotalBalance { get; set; }
}
