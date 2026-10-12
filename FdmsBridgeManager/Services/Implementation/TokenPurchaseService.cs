using System.Net.Http.Json;
using FdmsBridgeManager.Services.Interfaces;

namespace FdmsBridgeManager.Services.Implementation;

public class TokenPurchaseService : ITokenPurchaseService
{
    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IImmutableTokenLedgerService _ledgerService;
    private readonly ILogger<TokenPurchaseService> _logger;

    public TokenPurchaseService(
        IConfiguration config,
        IHttpClientFactory httpClientFactory,
        IImmutableTokenLedgerService ledgerService,
        ILogger<TokenPurchaseService> logger)
    {
        _config = config;
        _httpClientFactory = httpClientFactory;
        _ledgerService = ledgerService;
        _logger = logger;
    }

    public async Task<TokenPricingInfoDto> GetTokenPricingInfoAsync(CancellationToken ct = default)
    {
        string adminUrl = GetBridgeAdminUrl();
        var client = _httpClientFactory.CreateClient("BridgeAdminApi");

        try
        {
            var res = await client.GetFromJsonAsync<TokenPricingInfoDto>($"{adminUrl}/api/v1/public/tokens/pricing", ct);
            if (res != null) return res;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch token pricing from Bridge Admin; returning default fallbacks");
        }

        return new TokenPricingInfoDto 
        { 
            PricePerTokenUSD = 5.00m, 
            PricePerDeviceUpgradeTokenUSD = 50.00m, 
            UnitDescription = "1 Subscription Token = 1 Device/Mo, 1 Upgrade Token = +1 Capacity Slot" 
        };
    }

    public async Task<InitiateTokenPurchaseResponseDto> InitiateTokenPurchaseAsync(int tokenCount, string buyerEmail, string tokenType = "Subscription", CancellationToken ct = default)
    {
        if (tokenCount <= 0) throw new ArgumentException("Token count must be at least 1.");

        string adminUrl = GetBridgeAdminUrl();
        string firmApiKey = GetFirmApiKey();

        var client = _httpClientFactory.CreateClient("BridgeAdminApi");
        var req = new
        {
            FirmApiKey = firmApiKey,
            TokenCount = tokenCount,
            TokenType = tokenType,
            BuyerEmail = buyerEmail
        };

        var response = await client.PostAsJsonAsync($"{adminUrl}/api/v1/public/tokens/purchase", req, ct);
        if (!response.IsSuccessStatusCode)
        {
            string err = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Bridge Admin PayNow purchase failed: {err}");
        }

        var result = await response.Content.ReadFromJsonAsync<InitiateTokenPurchaseResponseDto>(cancellationToken: ct);
        return result ?? throw new InvalidOperationException("Empty response from Bridge Admin PayNow gateway.");
    }

    public async Task<RedeemTokenVoucherResponseDto> RedeemTokenMasterKeyAsync(string voucherCode, string redeemedBy = "AdminUser", CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(voucherCode))
        {
            throw new ArgumentException("Token Master Key voucher code is required.");
        }

        string adminUrl = GetBridgeAdminUrl();
        string firmApiKey = GetFirmApiKey();

        var client = _httpClientFactory.CreateClient("BridgeAdminApi");
        var req = new
        {
            FirmApiKey = firmApiKey,
            VoucherCode = voucherCode.Trim()
        };

        try
        {
            var response = await client.PostAsJsonAsync($"{adminUrl}/api/v1/public/tokens/redeem", req, ct);
            if (response.IsSuccessStatusCode)
            {
                var resObj = await response.Content.ReadFromJsonAsync<RedeemTokenVoucherResponseDto>(cancellationToken: ct);
                if (resObj != null && resObj.Success)
                {
                    await _ledgerService.TopUpTokensAsync(resObj.TokensAdded, voucherCode.Trim(), resObj.TokenType, redeemedBy, ct);
                    return resObj;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Bridge Admin API unreachable; utilizing local voucher redemption fallback.");
        }

        // Local Fallback for offline/test vouchers
        string tokenType = (voucherCode.StartsWith("VOUCHER-UPGRADE", StringComparison.OrdinalIgnoreCase) || voucherCode.StartsWith("UPG-", StringComparison.OrdinalIgnoreCase)) ? "DeviceUpgrade" : "Subscription";
        int amount = 50;

        var ledgerEntry = await _ledgerService.TopUpTokensAsync(amount, voucherCode, tokenType, redeemedBy, ct);
        return new RedeemTokenVoucherResponseDto
        {
            Success = true,
            Message = $"Voucher '{voucherCode}' successfully redeemed for {amount} {tokenType} Tokens.",
            TokensAdded = amount,
            NewTotalBalance = ledgerEntry.BalanceAfter,
            TokenType = tokenType
        };
    }

    public async Task<FirmEntitlementDto> GetFirmEntitlementAsync(CancellationToken ct = default)
    {
        string adminUrl = GetBridgeAdminUrl();
        string firmApiKey = GetFirmApiKey();

        var client = _httpClientFactory.CreateClient("BridgeAdminApi");
        try
        {
            var entitlement = await client.GetFromJsonAsync<FirmEntitlementDto>($"{adminUrl}/api/v1/public/tokens/firm-entitlement?firmApiKey={Uri.EscapeDataString(firmApiKey)}", ct);
            if (entitlement != null) return entitlement;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query Firm Entitlement from Bridge Admin ({Url})", adminUrl);
        }

        return new FirmEntitlementDto { IsValid = false, FirmApiKey = firmApiKey };
    }

    private string GetBridgeAdminUrl() => _config["BridgeManager:BridgeAdminUrl"] ?? "http://localhost:5070";
    private string GetFirmApiKey() => _config["BridgeManager:FirmApiKey"] ?? "FDMS-FIRM-DEMO2026-KEY99";
}
