using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FdmsBridgeAdmin.Data;
using FdmsBridgeAdmin.Models.Dtos;
using FdmsBridgeAdmin.Models.Entities;
using FdmsBridgeAdmin.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace FdmsBridgeAdmin.Services.Implementation;

public class TokenAdminService : ITokenAdminService
{
    private readonly BridgeAdminDbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<TokenAdminService> _logger;
    private readonly string _masterSecret;

    public TokenAdminService(BridgeAdminDbContext db, IConfiguration config, ILogger<TokenAdminService> logger)
    {
        _db = db;
        _config = config;
        _logger = logger;
        _masterSecret = _config["BridgeAdmin:MasterSecret"] ?? "FDMS-Bridge-Admin-Master-HMAC-Secret-Key-2026-SecurityKey";
    }

    public async Task<decimal> GetPricePerTokenAsync(CancellationToken ct = default)
    {
        var configItem = await _db.SystemConfigs.FirstOrDefaultAsync(c => c.ConfigKey == "PricePerTokenUSD", ct);
        if (configItem != null && decimal.TryParse(configItem.ConfigValue, out decimal price))
        {
            return price;
        }

        // Default to $5 USD as specified
        decimal defaultPrice = 5.00m;
        if (configItem == null)
        {
            _db.SystemConfigs.Add(new SystemConfigEntity
            {
                ConfigKey = "PricePerTokenUSD",
                ConfigValue = defaultPrice.ToString("F2"),
                Description = "Price per token in USD (1 token = 1 device per month)",
                UpdatedAt = DateTime.UtcNow
            });
            await _db.SaveChangesAsync(ct);
        }
        return defaultPrice;
    }

    public async Task<decimal> UpdatePricePerTokenAsync(decimal newPrice, CancellationToken ct = default)
    {
        if (newPrice <= 0) throw new ArgumentException("Price per token must be greater than zero.");

        var configItem = await _db.SystemConfigs.FirstOrDefaultAsync(c => c.ConfigKey == "PricePerTokenUSD", ct);
        if (configItem == null)
        {
            configItem = new SystemConfigEntity
            {
                ConfigKey = "PricePerTokenUSD",
                ConfigValue = newPrice.ToString("F2"),
                Description = "Price per token in USD (1 token = 1 device per month)",
                UpdatedAt = DateTime.UtcNow
            };
            _db.SystemConfigs.Add(configItem);
        }
        else
        {
            configItem.ConfigValue = newPrice.ToString("F2");
            configItem.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Updated token price to ${Price} USD", newPrice);

        return newPrice;
    }

    public async Task<decimal> GetPricePerDeviceUpgradeTokenAsync(CancellationToken ct = default)
    {
        var configItem = await _db.SystemConfigs.FirstOrDefaultAsync(c => c.ConfigKey == "PricePerDeviceUpgradeTokenUSD", ct);
        if (configItem != null && decimal.TryParse(configItem.ConfigValue, out decimal price))
        {
            return price;
        }

        decimal defaultPrice = 50.00m;
        if (configItem == null)
        {
            _db.SystemConfigs.Add(new SystemConfigEntity
            {
                ConfigKey = "PricePerDeviceUpgradeTokenUSD",
                ConfigValue = defaultPrice.ToString("F2"),
                Description = "Price per device upgrade token in USD ($50 = 1 extra device slot)",
                UpdatedAt = DateTime.UtcNow
            });
            await _db.SaveChangesAsync(ct);
        }
        return defaultPrice;
    }

    public async Task<decimal> UpdatePricePerDeviceUpgradeTokenAsync(decimal newPrice, CancellationToken ct = default)
    {
        if (newPrice <= 0) throw new ArgumentException("Price per device upgrade token must be greater than zero.");

        var configItem = await _db.SystemConfigs.FirstOrDefaultAsync(c => c.ConfigKey == "PricePerDeviceUpgradeTokenUSD", ct);
        if (configItem == null)
        {
            configItem = new SystemConfigEntity
            {
                ConfigKey = "PricePerDeviceUpgradeTokenUSD",
                ConfigValue = newPrice.ToString("F2"),
                Description = "Price per device upgrade token in USD ($50 = 1 extra device slot)",
                UpdatedAt = DateTime.UtcNow
            };
            _db.SystemConfigs.Add(configItem);
        }
        else
        {
            configItem.ConfigValue = newPrice.ToString("F2");
            configItem.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Updated device upgrade token price to ${Price} USD", newPrice);

        return newPrice;
    }

    public async Task<TokenVoucherDto> GenerateTokenVoucherAsync(GenerateTokenVoucherRequest req, string generatedBy = "SystemAdmin", CancellationToken ct = default)
    {
        if (req.TokenCount <= 0) throw new ArgumentException("Token count must be at least 1.");

        var firm = await _db.ConsultancyFirms.FirstOrDefaultAsync(f => f.Id == req.ConsultancyFirmId, ct);
        if (firm == null) throw new KeyNotFoundException("Consultancy firm not found.");

        string tokenType = string.Equals(req.TokenType, "DeviceUpgrade", StringComparison.OrdinalIgnoreCase) ? "DeviceUpgrade" : "Subscription";
        decimal unitPrice = tokenType == "DeviceUpgrade" ? await GetPricePerDeviceUpgradeTokenAsync(ct) : await GetPricePerTokenAsync(ct);
        decimal totalPrice = unitPrice * req.TokenCount;

        // Generate HMAC-SHA256 Token Master Key
        var payloadObj = new
        {
            FirmId = firm.Id,
            FirmApiKey = firm.FirmApiKey,
            TokenType = tokenType,
            Tokens = req.TokenCount,
            IssuedAt = DateTime.UtcNow.ToString("o"),
            Nonce = Guid.NewGuid().ToString("N")
        };

        string payloadJson = JsonSerializer.Serialize(payloadObj);
        string payloadB64 = Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson));
        string signature = ComputeHmacSignature(payloadB64, _masterSecret);

        string prefix = tokenType == "DeviceUpgrade" ? "FDMS-DEVUPG-TOKEN-" : "FDMS-TOKEN-";
        string voucherCode = $"{prefix}{payloadB64}.{signature}";

        var voucherEntity = new TokenVoucherEntity
        {
            Id = Guid.NewGuid(),
            ConsultancyFirmId = firm.Id,
            VoucherCode = voucherCode,
            TokenCount = req.TokenCount,
            TokenType = tokenType,
            UnitPriceUSD = unitPrice,
            TotalPriceUSD = totalPrice,
            Status = "Active",
            PurchasedViaPayNow = req.PurchasedViaPayNow,
            TransactionRef = string.IsNullOrWhiteSpace(req.TransactionRef) ? $"ADMIN-GEN-{Guid.NewGuid():N}".Substring(0, 16).ToUpper() : req.TransactionRef,
            GeneratedAt = DateTime.UtcNow,
            GeneratedBy = generatedBy
        };

        if (tokenType == "DeviceUpgrade")
        {
            firm.TotalDeviceUpgradeTokensPurchased += req.TokenCount;
        }
        else
        {
            firm.TotalSubscriptionTokensPurchased += req.TokenCount;
        }
        firm.TotalTokensPurchased += req.TokenCount;
        firm.CurrentTokenBalance += req.TokenCount;

        _db.TokenVouchers.Add(voucherEntity);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Generated Token Master Key ({TokenType}) for Firm {FirmName}: {Tokens} tokens (${TotalPrice} USD)", tokenType, firm.FirmName, req.TokenCount, totalPrice);

        return MapToDto(voucherEntity, firm.FirmName);
    }

    public async Task<ValidateAndRedeemVoucherResponse> RedeemVoucherAsync(ValidateAndRedeemVoucherRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.FirmApiKey) || string.IsNullOrWhiteSpace(req.VoucherCode))
        {
            return new ValidateAndRedeemVoucherResponse { Success = false, Message = "Firm API Key and Voucher Code are required." };
        }

        var firm = await _db.ConsultancyFirms.FirstOrDefaultAsync(f => f.FirmApiKey == req.FirmApiKey.Trim(), ct);
        if (firm == null || !firm.IsActive)
        {
            return new ValidateAndRedeemVoucherResponse { Success = false, Message = "Invalid or inactive consultancy firm API Key." };
        }

        var voucher = await _db.TokenVouchers.FirstOrDefaultAsync(v => v.VoucherCode == req.VoucherCode.Trim(), ct);
        if (voucher == null)
        {
            return new ValidateAndRedeemVoucherResponse { Success = false, Message = "Token Master Key voucher not found." };
        }

        if (voucher.Status == "Redeemed")
        {
            return new ValidateAndRedeemVoucherResponse { Success = false, Message = $"This Token Master Key voucher has already been redeemed on {voucher.RedeemedAt:yyyy-MM-dd HH:mm:ss} UTC." };
        }

        if (voucher.ConsultancyFirmId != firm.Id)
        {
            return new ValidateAndRedeemVoucherResponse { Success = false, Message = "This Token Master Key voucher belongs to another consultancy firm." };
        }

        // Validate cryptographic signature
        if (!ValidateVoucherSignature(req.VoucherCode.Trim(), _masterSecret))
        {
            return new ValidateAndRedeemVoucherResponse { Success = false, Message = "Cryptographic signature validation failed. Voucher code is invalid or tampered with." };
        }

        voucher.Status = "Redeemed";
        voucher.RedeemedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Voucher redeemed successfully by {FirmName}: +{Tokens} tokens.", firm.FirmName, voucher.TokenCount);

        return new ValidateAndRedeemVoucherResponse
        {
            Success = true,
            Message = $"Token Master Key voucher redeemed successfully! Added {voucher.TokenCount} {voucher.TokenType ?? "Subscription"} tokens to your firm account.",
            TokenType = voucher.TokenType ?? "Subscription",
            TokensAdded = voucher.TokenCount,
            NewTotalBalance = firm.CurrentTokenBalance
        };
    }

    public async Task<List<TokenVoucherDto>> GetVouchersAsync(Guid? firmId = null, CancellationToken ct = default)
    {
        var query = _db.TokenVouchers.AsQueryable();
        if (firmId.HasValue)
        {
            query = query.Where(v => v.ConsultancyFirmId == firmId.Value);
        }

        var list = await query.OrderByDescending(v => v.GeneratedAt).ToListAsync(ct);
        var firmIds = list.Select(v => v.ConsultancyFirmId).Distinct().ToList();
        var firmMap = await _db.ConsultancyFirms
            .Where(f => firmIds.Contains(f.Id))
            .ToDictionaryAsync(f => f.Id, f => f.FirmName, ct);

        return list.Select(v => MapToDto(v, firmMap.TryGetValue(v.ConsultancyFirmId, out var name) ? name : "Unknown Firm")).ToList();
    }

    private static bool ValidateVoucherSignature(string voucherCode, string secret)
    {
        try
        {
            string clean = voucherCode.Trim();
            if (clean.StartsWith("FDMS-DEVUPG-TOKEN-")) clean = clean.Substring("FDMS-DEVUPG-TOKEN-".Length);
            else if (clean.StartsWith("FDMS-TOKEN-")) clean = clean.Substring("FDMS-TOKEN-".Length);

            var parts = clean.Split('.');
            if (parts.Length != 2) return false;

            string payloadB64 = parts[0];
            string signature = parts[1];

            string expectedSig = ComputeHmacSignature(payloadB64, secret);
            return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(signature), Encoding.UTF8.GetBytes(expectedSig));
        }
        catch
        {
            return false;
        }
    }

    private static TokenVoucherDto MapToDto(TokenVoucherEntity v, string firmName)
    {
        return new TokenVoucherDto
        {
            Id = v.Id,
            ConsultancyFirmId = v.ConsultancyFirmId,
            FirmName = firmName,
            VoucherCode = v.VoucherCode,
            TokenCount = v.TokenCount,
            TokenType = v.TokenType ?? "Subscription",
            UnitPriceUSD = v.UnitPriceUSD,
            TotalPriceUSD = v.TotalPriceUSD,
            Status = v.Status,
            PurchasedViaPayNow = v.PurchasedViaPayNow,
            TransactionRef = v.TransactionRef,
            GeneratedAt = v.GeneratedAt,
            RedeemedAt = v.RedeemedAt,
            GeneratedBy = v.GeneratedBy
        };
    }

    private static string ComputeHmacSignature(string data, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        return Base64UrlEncode(hash);
    }

    private static string Base64UrlEncode(byte[] input)
    {
        return Convert.ToBase64String(input).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
