using FdmsGatewayApi.Data;
using FdmsGatewayApi.Models.Entities;
using FdmsGatewayApi.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace FdmsGatewayApi.Services.Implementation;

public class TenantRegistryService : ITenantRegistryService
{
    private readonly FdmsDbContext _db;
    private readonly IEncryptedKeyStore _keyStore;
    private readonly ILogger<TenantRegistryService> _logger;

    public TenantRegistryService(FdmsDbContext db, IEncryptedKeyStore keyStore, ILogger<TenantRegistryService> logger)
    {
        _db = db;
        _keyStore = keyStore;
        _logger = logger;
    }

    public async Task<TenantContext?> GetTenantContextAsync(string tenantApiKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tenantApiKey))
        {
            return null;
        }

        // Enforcement of Tenant Isolation boundary: Direct query filter on TenantApiKey
        var account = await _db.TenantBusinessAccounts
            .Include(a => a.PartnerProfile)
            .Include(a => a.ZimraDevices)
            .FirstOrDefaultAsync(a => a.TenantApiKey == tenantApiKey, cancellationToken);

        if (account == null)
        {
            _logger.LogWarning("[TENANT BOUNDARY ALERT] Lookup failed for TenantApiKey '{ApiKey}'", tenantApiKey);
            return null;
        }

        // Check if tenant account or wholesale partner account is suspended
        bool isPartnerActive = account.PartnerProfile != null && account.PartnerProfile.WholesaleTokenBalance > 0;
        bool isActive = account.IsActive && isPartnerActive;

        var context = new TenantContext
        {
            TenantId = account.TenantId,
            PartnerId = account.PartnerId,
            TenantApiKey = account.TenantApiKey,
            BusinessName = account.BusinessName,
            IsActive = isActive,
            AuthorizedDeviceIds = account.ZimraDevices
                .Where(d => d.IsActive)
                .Select(d => d.ZimraDeviceId)
                .ToList()
        };

        return context;
    }

    public async Task<byte[]> GetEncryptedDeviceCertificateAsync(int tenantId, string deviceId, CancellationToken cancellationToken = default)
    {
        var device = await _db.TenantZimraDevices
            .FirstOrDefaultAsync(d => d.TenantId == tenantId && d.ZimraDeviceId == deviceId && d.IsActive, cancellationToken);

        if (device == null || device.EncryptedPfxCertificate.Length == 0)
        {
            throw new KeyNotFoundException($"Certificate bytes not provisioned for Tenant {tenantId}, Device {deviceId}.");
        }

        return device.EncryptedPfxCertificate;
    }

    public async Task<string> GetCertificatePasswordAsync(int tenantId, string deviceId, CancellationToken cancellationToken = default)
    {
        var device = await _db.TenantZimraDevices
            .FirstOrDefaultAsync(d => d.TenantId == tenantId && d.ZimraDeviceId == deviceId && d.IsActive, cancellationToken);

        if (device == null || string.IsNullOrWhiteSpace(device.CertificatePassword))
        {
            return string.Empty;
        }

        try
        {
            string pass = !string.IsNullOrWhiteSpace(device.CertificateDecryptionSalt) ? device.CertificateDecryptionSalt : "default-salt";
            string decrypted = _keyStore.DecryptKey(device.CertificatePassword, pass);
            return !string.IsNullOrWhiteSpace(decrypted) ? decrypted : device.CertificatePassword;
        }
        catch
        {
            // Plaintext fallback for legacy records
            return device.CertificatePassword;
        }
    }

    public void InvalidateDeviceCertificateCache(int tenantId, string deviceId)
    {
        bool evicted = Controllers.ReceiptsController.InvalidateCache(tenantId, deviceId);
        if (evicted)
        {
            _logger.LogInformation("[CACHE EVICTION] Successfully evicted cached mTLS HttpClient for Tenant {TenantId}, Device {DeviceId}.", tenantId, deviceId);
        }
    }

    public async Task RecordLedgerEntryAsync(
        int tenantId,
        string deviceId,
        string invoiceUuid,
        string invoiceNo,
        string currencyCode,
        decimal grossAmount,
        string signature,
        string status,
        CancellationToken cancellationToken = default)
    {
        var entry = new MultiTenantFiscalLedgerEntity
        {
            TenantId = tenantId,
            ZimraDeviceId = deviceId,
            InvoiceUuid = invoiceUuid,
            InvoiceNumber = invoiceNo,
            CurrencyCode = currencyCode,
            GrossInclusiveAmount = grossAmount,
            CryptographicSignature = signature,
            ZimraSyncStatus = status,
            CreatedAt = DateTime.UtcNow
        };

        _db.MultiTenantFiscalLedger.Add(entry);
        await _db.SaveChangesAsync(cancellationToken);
    }
}
