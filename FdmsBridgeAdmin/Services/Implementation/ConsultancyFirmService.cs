using System.Security.Cryptography;
using FdmsBridgeAdmin.Data;
using FdmsBridgeAdmin.Models.Dtos;
using FdmsBridgeAdmin.Models.Entities;
using FdmsBridgeAdmin.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace FdmsBridgeAdmin.Services.Implementation;

public class ConsultancyFirmService : IConsultancyFirmService
{
    private readonly BridgeAdminDbContext _db;
    private readonly ILogger<ConsultancyFirmService> _logger;

    public ConsultancyFirmService(BridgeAdminDbContext db, ILogger<ConsultancyFirmService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<ConsultancyFirmDto> CreateFirmAsync(CreateConsultancyFirmRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.FirmName) || string.IsNullOrWhiteSpace(req.ContactEmail))
        {
            throw new ArgumentException("Firm Name and Contact Email are required.");
        }

        string apiKey = $"FDMS-FIRM-{Guid.NewGuid():N}".ToUpper();

        string tier = string.IsNullOrWhiteSpace(req.UnlimitedTier) ? "None" : req.UnlimitedTier.Trim();
        DateTime? expiresAt = req.UnlimitedExpiresAt;
        if (tier.Equals("Annual", StringComparison.OrdinalIgnoreCase) && !expiresAt.HasValue)
        {
            expiresAt = DateTime.UtcNow.AddYears(1);
        }

        var entity = new ConsultancyFirmEntity
        {
            Id = Guid.NewGuid(),
            FirmName = req.FirmName.Trim(),
            ContactEmail = req.ContactEmail.Trim(),
            ContactPhone = req.ContactPhone?.Trim() ?? string.Empty,
            TaxPayerTIN = req.TaxPayerTIN?.Trim() ?? string.Empty,
            FirmApiKey = apiKey,
            TotalTokensPurchased = 0,
            TotalTokensConsumed = 0,
            CurrentTokenBalance = 0,
            IsActive = true,
            UnlimitedTier = tier,
            UnlimitedExpiresAt = expiresAt,
            ImmutabilityStatus = "OK",
            CreatedAt = DateTime.UtcNow
        };

        _db.ConsultancyFirms.Add(entity);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Registered new consultancy firm: {FirmName} ({Email}), ApiKey: {ApiKey}, UnlimitedTier: {Tier}", entity.FirmName, entity.ContactEmail, apiKey, tier);

        return MapToDto(entity);
    }

    public async Task<List<ConsultancyFirmDto>> GetAllFirmsAsync(CancellationToken ct = default)
    {
        var firms = await _db.ConsultancyFirms
            .OrderByDescending(f => f.CreatedAt)
            .ToListAsync(ct);

        var vouchers = await _db.TokenVouchers.ToListAsync(ct);

        var dtos = new List<ConsultancyFirmDto>();
        foreach (var firm in firms)
        {
            var dto = MapToDto(firm);
            var firmVouchers = vouchers.Where(v => v.ConsultancyFirmId == firm.Id).ToList();

            int voucherSub = firmVouchers.Where(v => v.TokenType == "Subscription" || string.IsNullOrWhiteSpace(v.TokenType)).Sum(v => v.TokenCount);
            int voucherUpg = firmVouchers.Where(v => v.TokenType == "DeviceUpgrade").Sum(v => v.TokenCount);

            if (voucherSub > 0 || voucherUpg > 0)
            {
                dto.TotalSubscriptionTokensPurchased = voucherSub;
                dto.TotalDeviceUpgradeTokensPurchased = voucherUpg;
            }
            else if (firm.TotalSubscriptionTokensPurchased == 0 && firm.TotalDeviceUpgradeTokensPurchased == 0 && firm.TotalTokensPurchased > 0)
            {
                // Fallback for initial seeded balance
                dto.TotalSubscriptionTokensPurchased = firm.TotalTokensPurchased;
                dto.TotalDeviceUpgradeTokensPurchased = 0;
            }

            dtos.Add(dto);
        }

        return dtos;
    }

    public async Task<ConsultancyFirmDto?> GetFirmByIdAsync(Guid id, CancellationToken ct = default)
    {
        var firm = await _db.ConsultancyFirms.FirstOrDefaultAsync(f => f.Id == id, ct);
        return firm == null ? null : MapToDto(firm);
    }

    public async Task<ConsultancyFirmDto?> GetFirmByApiKeyAsync(string apiKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) return null;
        var firm = await _db.ConsultancyFirms.FirstOrDefaultAsync(f => f.FirmApiKey == apiKey.Trim(), ct);
        return firm == null ? null : MapToDto(firm);
    }

    public async Task<FirmEntitlementDto> GetFirmEntitlementByApiKeyAsync(string apiKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new FirmEntitlementDto { IsValid = false };
        }

        var firm = await _db.ConsultancyFirms.FirstOrDefaultAsync(f => f.FirmApiKey == apiKey.Trim(), ct);
        if (firm == null)
        {
            return new FirmEntitlementDto { IsValid = false };
        }

        var dto = MapToDto(firm);
        return new FirmEntitlementDto
        {
            IsValid = true,
            FirmApiKey = firm.FirmApiKey,
            FirmName = firm.FirmName,
            UnlimitedTier = firm.UnlimitedTier ?? "None",
            UnlimitedExpiresAt = firm.UnlimitedExpiresAt,
            IsUnlimitedActive = dto.IsUnlimitedActive,
            CurrentTokenBalance = firm.CurrentTokenBalance,
            IsActive = firm.IsActive
        };
    }

    public async Task<ConsultancyFirmDto> ToggleFirmStatusAsync(Guid id, CancellationToken ct = default)
    {
        var firm = await _db.ConsultancyFirms.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (firm == null) throw new KeyNotFoundException("Consultancy firm not found.");

        firm.IsActive = !firm.IsActive;
        await _db.SaveChangesAsync(ct);

        return MapToDto(firm);
    }

    public async Task<ConsultancyFirmDto> UpdateFirmUnlimitedTierAsync(Guid id, UpdateFirmUnlimitedRequest req, CancellationToken ct = default)
    {
        var firm = await _db.ConsultancyFirms.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (firm == null) throw new KeyNotFoundException("Consultancy firm not found.");

        string tier = string.IsNullOrWhiteSpace(req.UnlimitedTier) ? "None" : req.UnlimitedTier.Trim();
        DateTime? expiresAt = req.UnlimitedExpiresAt;

        if (tier.Equals("Annual", StringComparison.OrdinalIgnoreCase) && !expiresAt.HasValue)
        {
            expiresAt = DateTime.UtcNow.AddYears(1);
        }
        else if (tier.Equals("None", StringComparison.OrdinalIgnoreCase) || tier.Equals("Completely", StringComparison.OrdinalIgnoreCase))
        {
            expiresAt = null;
        }

        firm.UnlimitedTier = tier;
        firm.UnlimitedExpiresAt = expiresAt;

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Updated Unlimited Tier for Firm {FirmName} ({Id}): Tier={Tier}, ExpiresAt={ExpiresAt}", firm.FirmName, firm.Id, tier, expiresAt);

        return MapToDto(firm);
    }

    private static ConsultancyFirmDto MapToDto(ConsultancyFirmEntity entity)
    {
        string tier = entity.UnlimitedTier ?? "None";
        bool isUnlimitedActive = false;
        if (entity.IsActive)
        {
            if (string.Equals(tier, "Completely", StringComparison.OrdinalIgnoreCase))
            {
                isUnlimitedActive = true;
            }
            else if (string.Equals(tier, "Annual", StringComparison.OrdinalIgnoreCase))
            {
                isUnlimitedActive = entity.UnlimitedExpiresAt.HasValue && DateTime.UtcNow <= entity.UnlimitedExpiresAt.Value;
            }
        }

        return new ConsultancyFirmDto
        {
            Id = entity.Id,
            FirmName = entity.FirmName,
            ContactEmail = entity.ContactEmail,
            ContactPhone = entity.ContactPhone,
            TaxPayerTIN = entity.TaxPayerTIN,
            FirmApiKey = entity.FirmApiKey,
            TotalTokensPurchased = entity.TotalTokensPurchased,
            TotalSubscriptionTokensPurchased = entity.TotalSubscriptionTokensPurchased,
            TotalDeviceUpgradeTokensPurchased = entity.TotalDeviceUpgradeTokensPurchased,
            TotalTokensConsumed = entity.TotalTokensConsumed,
            CurrentTokenBalance = entity.CurrentTokenBalance,
            IsActive = entity.IsActive,
            UnlimitedTier = tier,
            UnlimitedExpiresAt = entity.UnlimitedExpiresAt,
            IsUnlimitedActive = isUnlimitedActive,
            ImmutabilityStatus = entity.ImmutabilityStatus,
            LastHeartbeatAt = entity.LastHeartbeatAt,
            CreatedAt = entity.CreatedAt
        };
    }
}
