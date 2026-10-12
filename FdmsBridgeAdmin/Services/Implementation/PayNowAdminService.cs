using FdmsBridgeAdmin.Data;
using FdmsBridgeAdmin.Models.Dtos;
using FdmsBridgeAdmin.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace FdmsBridgeAdmin.Services.Implementation;

public class PayNowAdminService : IPayNowAdminService
{
    private readonly BridgeAdminDbContext _db;
    private readonly ITokenAdminService _tokenService;
    private readonly IConsultancyFirmService _firmService;
    private readonly ILogger<PayNowAdminService> _logger;

    public PayNowAdminService(
        BridgeAdminDbContext db,
        ITokenAdminService tokenService,
        IConsultancyFirmService firmService,
        ILogger<PayNowAdminService> logger)
    {
        _db = db;
        _tokenService = tokenService;
        _firmService = firmService;
        _logger = logger;
    }

    public async Task<PayNowTokenCheckoutResponse> InitiateTokenPurchaseAsync(PayNowTokenCheckoutRequest req, CancellationToken ct = default)
    {
        if (req.TokenCount <= 0) throw new ArgumentException("Token count must be at least 1.");

        var firm = await _firmService.GetFirmByApiKeyAsync(req.FirmApiKey, ct);
        if (firm == null || !firm.IsActive)
        {
            throw new ArgumentException("Invalid or inactive consultancy firm API key.");
        }

        string tokenType = string.Equals(req.TokenType, "DeviceUpgrade", StringComparison.OrdinalIgnoreCase) ? "DeviceUpgrade" : "Subscription";
        decimal unitPrice = tokenType == "DeviceUpgrade" 
            ? await _tokenService.GetPricePerDeviceUpgradeTokenAsync(ct) 
            : await _tokenService.GetPricePerTokenAsync(ct);
        decimal totalPrice = unitPrice * req.TokenCount;

        string payNowRef = $"PAYNOW-TOKEN-{Guid.NewGuid():N}".Substring(0, 18).ToUpper();

        // Redirect URL to PayNow simulator/gateway
        string redirectUrl = $"/index.html?action=paynow_simulator&ref={payNowRef}&tokens={req.TokenCount}&type={tokenType}&amount={totalPrice}&firmId={firm.Id}";

        _logger.LogInformation("Initiated PayNow Token Purchase ({TokenType}) for firm {FirmName}: {Tokens} tokens (${TotalPrice} USD), Ref: {Ref}", tokenType, firm.FirmName, req.TokenCount, totalPrice, payNowRef);

        return new PayNowTokenCheckoutResponse
        {
            TransactionId = Guid.NewGuid().ToString("N"),
            PayNowRedirectUrl = redirectUrl,
            PayNowReference = payNowRef,
            TokenCount = req.TokenCount,
            TokenType = tokenType,
            UnitPriceUSD = unitPrice,
            TotalPriceUSD = totalPrice
        };
    }

    public async Task<TokenVoucherDto> ProcessPayNowCallbackAsync(string payNowRef, string status, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(payNowRef)) throw new ArgumentException("PayNow Reference required.");

        // For simulation or webhook, parse firmId & tokens from payNowRef or query
        // In full production, this verifies PayNow signature & lookup transaction
        var existingVoucher = await _db.TokenVouchers.FirstOrDefaultAsync(v => v.TransactionRef == payNowRef, ct);
        if (existingVoucher != null)
        {
            var firm = await _db.ConsultancyFirms.FirstOrDefaultAsync(f => f.Id == existingVoucher.ConsultancyFirmId, ct);
            return new TokenVoucherDto
            {
                Id = existingVoucher.Id,
                ConsultancyFirmId = existingVoucher.ConsultancyFirmId,
                FirmName = firm?.FirmName ?? "Consultancy Firm",
                VoucherCode = existingVoucher.VoucherCode,
                TokenCount = existingVoucher.TokenCount,
                UnitPriceUSD = existingVoucher.UnitPriceUSD,
                TotalPriceUSD = existingVoucher.TotalPriceUSD,
                Status = existingVoucher.Status,
                PurchasedViaPayNow = existingVoucher.PurchasedViaPayNow,
                TransactionRef = existingVoucher.TransactionRef,
                GeneratedAt = existingVoucher.GeneratedAt,
                RedeemedAt = existingVoucher.RedeemedAt,
                GeneratedBy = existingVoucher.GeneratedBy
            };
        }

        throw new KeyNotFoundException("PayNow token transaction not found.");
    }
}
