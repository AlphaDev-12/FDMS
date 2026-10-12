using FdmsGatewayApi.Data;
using FdmsGatewayApi.Models.Entities;
using FdmsGatewayApi.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace FdmsGatewayApi.Services.Implementation;

public class PartnerManagementService : IPartnerManagementService
{
    private readonly FdmsDbContext _db;
    private readonly IEncryptedKeyStore _keyStore;
    private readonly ITenantRegistryService _tenantRegistry;
    private readonly ILogger<PartnerManagementService> _logger;

    public PartnerManagementService(
        FdmsDbContext db,
        IEncryptedKeyStore keyStore,
        ITenantRegistryService tenantRegistry,
        ILogger<PartnerManagementService> logger)
    {
        _db = db;
        _keyStore = keyStore;
        _tenantRegistry = tenantRegistry;
        _logger = logger;
    }

    public async Task EnsurePartnerProfileAsync(string partnerKey, string partnerName, decimal initialTokens, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(partnerKey)) return;

        var partnerProfile = await _db.SharedPartnerProfiles
            .FirstOrDefaultAsync(p => p.PartnerKey == partnerKey, cancellationToken);

        if (partnerProfile == null)
        {
            _logger.LogInformation("[PARTNER AUTO-SEED] Creating initial partner profile '{PartnerKey}' with {Tokens} wholesale tokens.", partnerKey, initialTokens);
            partnerProfile = new SharedPartnerProfileEntity
            {
                PartnerKey = partnerKey,
                WholesaleTokenBalance = initialTokens,
                CreatedAt = DateTime.UtcNow
            };
            _db.SharedPartnerProfiles.Add(partnerProfile);
            await _db.SaveChangesAsync(cancellationToken);
        }

        bool hasTenant = await _db.TenantBusinessAccounts.AnyAsync(cancellationToken);
        if (!hasTenant)
        {
            var defaultTenant = new TenantBusinessAccountEntity
            {
                PartnerId = partnerProfile.PartnerId,
                TenantApiKey = "css_tenant_prod_76b96dd9fb5e",
                BusinessName = "Default Enterprise Partner Tenant",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            _db.TenantBusinessAccounts.Add(defaultTenant);
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("[TENANT AUTO-SEED] Created default tenant business account with key 'css_tenant_prod_76b96dd9fb5e'.");
        }
    }

    public async Task<OnboardTenantResponseDto> OnboardTenantAsync(OnboardTenantRequestDto request, CancellationToken cancellationToken = default)
    {
        var partner = await _db.SharedPartnerProfiles
            .FirstOrDefaultAsync(p => p.PartnerKey == request.PartnerKey, cancellationToken);

        if (partner == null)
        {
            throw new KeyNotFoundException($"Wholesale partner with key '{request.PartnerKey}' was not found.");
        }

        string apiKey = !string.IsNullOrWhiteSpace(request.DesiredTenantApiKey)
            ? request.DesiredTenantApiKey
            : $"tenant_key_{Guid.NewGuid():N}";

        // Check if API key already exists
        var existingAccount = await _db.TenantBusinessAccounts
            .FirstOrDefaultAsync(t => t.TenantApiKey == apiKey, cancellationToken);

        if (existingAccount != null)
        {
            throw new InvalidOperationException($"TenantApiKey '{apiKey}' is already in use by another tenant.");
        }

        var tenant = new TenantBusinessAccountEntity
        {
            PartnerId = partner.PartnerId,
            TenantApiKey = apiKey,
            BusinessName = request.BusinessName,
            IsActive = partner.WholesaleTokenBalance > 0,
            CreatedAt = DateTime.UtcNow
        };

        _db.TenantBusinessAccounts.Add(tenant);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("[PARTNER ONBOARDING] Onboarded new SME tenant '{BusinessName}' under PartnerId {PartnerId}. Assigned TenantApiKey: {ApiKey}", tenant.BusinessName, partner.PartnerId, tenant.TenantApiKey);

        return new OnboardTenantResponseDto
        {
            TenantId = tenant.TenantId,
            PartnerId = tenant.PartnerId,
            TenantApiKey = tenant.TenantApiKey,
            BusinessName = tenant.BusinessName,
            IsActive = tenant.IsActive,
            CreatedAt = tenant.CreatedAt
        };
    }

    public async Task<bool> RegisterDeviceAsync(RegisterDeviceRequestDto request, CancellationToken cancellationToken = default)
    {
        var tenant = await _db.TenantBusinessAccounts
            .FirstOrDefaultAsync(t => t.TenantApiKey == request.TenantApiKey, cancellationToken);

        if (tenant == null)
        {
            throw new KeyNotFoundException($"Tenant with ApiKey '{request.TenantApiKey}' was not found.");
        }

        string salt = !string.IsNullOrWhiteSpace(request.CertificateDecryptionSalt) ? request.CertificateDecryptionSalt : "default-salt";
        string? encryptedPassword = !string.IsNullOrWhiteSpace(request.CertificatePassword)
            ? _keyStore.EncryptKey(request.CertificatePassword, salt)
            : null;

        var existingDevice = await _db.TenantZimraDevices
            .FirstOrDefaultAsync(d => d.TenantId == tenant.TenantId && d.ZimraDeviceId == request.ZimraDeviceId, cancellationToken);

        if (existingDevice != null)
        {
            // Update existing certificate
            existingDevice.EncryptedPfxCertificate = request.PfxCertificateBytes;
            existingDevice.CertificateDecryptionSalt = salt;
            existingDevice.CertificatePassword = encryptedPassword;
            existingDevice.IsActive = true;
            _logger.LogInformation("[DEVICE REGISTRATION] Updated PFX certificate for Device {DeviceId} under Tenant {TenantId}.", request.ZimraDeviceId, tenant.TenantId);
        }
        else
        {
            var device = new TenantZimraDeviceEntity
            {
                TenantId = tenant.TenantId,
                ZimraDeviceId = request.ZimraDeviceId,
                EncryptedPfxCertificate = request.PfxCertificateBytes,
                CertificateDecryptionSalt = salt,
                CertificatePassword = encryptedPassword,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            _db.TenantZimraDevices.Add(device);
            _logger.LogInformation("[DEVICE REGISTRATION] Registered new Device {DeviceId} for SME Tenant '{BusinessName}' (TenantId: {TenantId}).", request.ZimraDeviceId, tenant.BusinessName, tenant.TenantId);
        }

        await _db.SaveChangesAsync(cancellationToken);

        // Evict any existing cached mTLS HttpClient socket connection for this device
        _tenantRegistry.InvalidateDeviceCertificateCache(tenant.TenantId, request.ZimraDeviceId);

        return true;
    }

    public async Task<PartnerDashboardSummaryDto?> GetPartnerDashboardAsync(string partnerKey, CancellationToken cancellationToken = default)
    {
        var partner = await _db.SharedPartnerProfiles
            .Include(p => p.TenantBusinessAccounts)
                .ThenInclude(a => a.ZimraDevices)
            .FirstOrDefaultAsync(p => p.PartnerKey == partnerKey, cancellationToken);

        if (partner == null) return null;

        var allTenants = partner.TenantBusinessAccounts.ToList();
        var allDevices = allTenants.SelectMany(t => t.ZimraDevices).ToList();

        int activeDevices = allDevices.Count(d => d.IsActive);
        double dailyBurnRate = activeDevices / 30.0;
        int projectedDays = activeDevices > 0 && partner.WholesaleTokenBalance > 0m
            ? (int)Math.Floor((double)partner.WholesaleTokenBalance / dailyBurnRate)
            : (partner.WholesaleTokenBalance > 0m ? 999 : 0);
        bool lowBalance = projectedDays <= 7;

        return new PartnerDashboardSummaryDto
        {
            PartnerId = partner.PartnerId,
            PartnerKey = partner.PartnerKey,
            WholesaleTokenBalance = partner.WholesaleTokenBalance,
            TotalTenantAccounts = allTenants.Count,
            ActiveTenantAccounts = allTenants.Count(t => t.IsActive),
            TotalRegisteredDevices = allDevices.Count,
            ActiveRegisteredDevices = activeDevices,
            DailyBurnRate = Math.Round(dailyBurnRate, 4),
            ProjectedDaysRemaining = projectedDays,
            IsLowBalanceWarning = lowBalance,
            Tenants = allTenants.Select(t => new TenantSummaryDto
            {
                TenantId = t.TenantId,
                TenantApiKey = t.TenantApiKey,
                BusinessName = t.BusinessName,
                IsActive = t.IsActive,
                DeviceCount = t.ZimraDevices.Count(d => d.IsActive),
                CreatedAt = t.CreatedAt
            }).ToList()
        };
    }
}
