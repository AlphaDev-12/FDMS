namespace FdmsBridgeAdmin.Models.Dtos;

public class CreateConsultancyFirmRequest
{
    public string FirmName { get; set; } = string.Empty;
    public string ContactEmail { get; set; } = string.Empty;
    public string ContactPhone { get; set; } = string.Empty;
    public string TaxPayerTIN { get; set; } = string.Empty;
    public string UnlimitedTier { get; set; } = "None"; // None, Annual, Completely
    public DateTime? UnlimitedExpiresAt { get; set; }
}

public class UpdateFirmUnlimitedRequest
{
    public string UnlimitedTier { get; set; } = "None"; // None, Annual, Completely
    public DateTime? UnlimitedExpiresAt { get; set; }
}

public class ConsultancyFirmDto
{
    public Guid Id { get; set; }
    public string FirmName { get; set; } = string.Empty;
    public string ContactEmail { get; set; } = string.Empty;
    public string ContactPhone { get; set; } = string.Empty;
    public string TaxPayerTIN { get; set; } = string.Empty;
    public string FirmApiKey { get; set; } = string.Empty;
    public int TotalTokensPurchased { get; set; }
    public int TotalSubscriptionTokensPurchased { get; set; }
    public int TotalDeviceUpgradeTokensPurchased { get; set; }
    public int TotalTokensConsumed { get; set; }
    public int CurrentTokenBalance { get; set; }
    public bool IsActive { get; set; }
    public string UnlimitedTier { get; set; } = "None";
    public DateTime? UnlimitedExpiresAt { get; set; }
    public bool IsUnlimitedActive { get; set; }
    public string ImmutabilityStatus { get; set; } = "OK";
    public DateTime? LastHeartbeatAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class FirmEntitlementDto
{
    public bool IsValid { get; set; }
    public string FirmApiKey { get; set; } = string.Empty;
    public string FirmName { get; set; } = string.Empty;
    public string UnlimitedTier { get; set; } = "None";
    public DateTime? UnlimitedExpiresAt { get; set; }
    public bool IsUnlimitedActive { get; set; }
    public int CurrentTokenBalance { get; set; }
    public bool IsActive { get; set; }
}

public class GenerateTokenVoucherRequest
{
    public Guid ConsultancyFirmId { get; set; }
    public int TokenCount { get; set; }
    public string TokenType { get; set; } = "Subscription"; // Subscription ($5) or DeviceUpgrade ($50)
    public bool PurchasedViaPayNow { get; set; } = false;
    public string TransactionRef { get; set; } = string.Empty;
}

public class TokenVoucherDto
{
    public Guid Id { get; set; }
    public Guid ConsultancyFirmId { get; set; }
    public string FirmName { get; set; } = string.Empty;
    public string VoucherCode { get; set; } = string.Empty;
    public int TokenCount { get; set; }
    public string TokenType { get; set; } = "Subscription";
    public decimal UnitPriceUSD { get; set; }
    public decimal TotalPriceUSD { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool PurchasedViaPayNow { get; set; }
    public string TransactionRef { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; }
    public DateTime? RedeemedAt { get; set; }
    public string GeneratedBy { get; set; } = string.Empty;
}

public class UpdatePricePerTokenRequest
{
    public decimal? PricePerTokenUSD { get; set; }
    public decimal? PricePerDeviceUpgradeTokenUSD { get; set; }
}

public class TelemetryHeartbeatRequest
{
    public string FirmApiKey { get; set; } = string.Empty;
    public string FirmName { get; set; } = string.Empty;
    public int TotalTokensPurchased { get; set; }
    public int TotalSubscriptionTokensPurchased { get; set; }
    public int TotalDeviceUpgradeTokensPurchased { get; set; }
    public int TotalTokensConsumed { get; set; }
    public int CurrentTokenBalance { get; set; }
    public int ActiveClientSubscriptions { get; set; }
    public string ImmutabilityStatus { get; set; } = "OK"; // OK, TAMPER_DETECTED
    public string LogType { get; set; } = "24H_Heartbeat"; // 24H_Heartbeat, TamperAlert, StartupCheck
    public string? TamperErrorMessage { get; set; }
}

public class PayNowTokenCheckoutRequest
{
    public string FirmApiKey { get; set; } = string.Empty;
    public int TokenCount { get; set; }
    public string TokenType { get; set; } = "Subscription"; // Subscription ($5) or DeviceUpgrade ($50)
    public string BuyerEmail { get; set; } = string.Empty;
}

public class PayNowTokenCheckoutResponse
{
    public string TransactionId { get; set; } = string.Empty;
    public string PayNowRedirectUrl { get; set; } = string.Empty;
    public string PayNowReference { get; set; } = string.Empty;
    public int TokenCount { get; set; }
    public string TokenType { get; set; } = "Subscription";
    public decimal UnitPriceUSD { get; set; }
    public decimal TotalPriceUSD { get; set; }
}

public class ValidateAndRedeemVoucherRequest
{
    public string FirmApiKey { get; set; } = string.Empty;
    public string VoucherCode { get; set; } = string.Empty;
}

public class ValidateAndRedeemVoucherResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string TokenType { get; set; } = "Subscription";
    public int TokensAdded { get; set; }
    public int NewTotalBalance { get; set; }
}
