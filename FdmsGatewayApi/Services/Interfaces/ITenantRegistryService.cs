namespace FdmsGatewayApi.Services.Interfaces;

public class TenantContext
{
    public int TenantId { get; set; }
    public int PartnerId { get; set; }
    public string TenantApiKey { get; set; } = string.Empty;
    public string BusinessName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public List<string> AuthorizedDeviceIds { get; set; } = new();

    public bool IsAuthorizedForDevice(string deviceId)
    {
        return AuthorizedDeviceIds.Contains(deviceId, StringComparer.OrdinalIgnoreCase);
    }
}

public interface ITenantRegistryService
{
    Task<TenantContext?> GetTenantContextAsync(string tenantApiKey, CancellationToken cancellationToken = default);
    Task<byte[]> GetEncryptedDeviceCertificateAsync(int tenantId, string deviceId, CancellationToken cancellationToken = default);
    Task<string> GetCertificatePasswordAsync(int tenantId, string deviceId, CancellationToken cancellationToken = default);
    void InvalidateDeviceCertificateCache(int tenantId, string deviceId);
    Task RecordLedgerEntryAsync(int tenantId, string deviceId, string invoiceUuid, string invoiceNo, string currencyCode, decimal grossAmount, string signature, string status, CancellationToken cancellationToken = default);
}
