using System.Security.Cryptography.X509Certificates;
using FdmsGatewayApi.Data;
using Microsoft.EntityFrameworkCore;

namespace FdmsGatewayApi.Services.Implementation;

public class CertificateExpiryWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<CertificateExpiryWorker> _logger;
    private readonly TimeSpan _checkInterval = TimeSpan.FromHours(24);

    public CertificateExpiryWorker(IServiceProvider serviceProvider, ILogger<CertificateExpiryWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ZIMRA X.509 Certificate Expiry Telemetry Worker initialized.");

        // Initial delay to allow DB auto-migrations and seeding to settle
        await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await AuditCertificatesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while auditing ZIMRA device certificate expirations.");
            }

            await Task.Delay(_checkInterval, stoppingToken);
        }
    }

    public async Task AuditCertificatesAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FdmsDbContext>();

        var devices = await db.TenantZimraDevices
            .Include(d => d.TenantAccount)
            .Where(d => d.IsActive && d.EncryptedPfxCertificate != null)
            .ToListAsync(cancellationToken);

        int totalAudited = 0;
        int warningCount = 0;
        int expiredCount = 0;

        foreach (var device in devices)
        {
            totalAudited++;
            try
            {
                using var cert = X509CertificateLoader.LoadPkcs12(device.EncryptedPfxCertificate, device.CertificatePassword);

                DateTime expiresAt = cert.NotAfter;
                TimeSpan timeUntilExpiry = expiresAt - DateTime.UtcNow;

                if (expiresAt <= DateTime.UtcNow)
                {
                    expiredCount++;
                    _logger.LogError(
                        "[CERTIFICATE EXPIRED] ZIMRA Device '{ZimraDeviceId}' (Tenant: '{TenantApiKey}', Business: '{BusinessName}') EXPIRED on {ExpirationDate:yyyy-MM-dd HH:mm:ss UTC}! Outbound fiscal submissions will fail.",
                        device.ZimraDeviceId,
                        device.TenantAccount?.TenantApiKey ?? "N/A",
                        device.TenantAccount?.BusinessName ?? "Unknown",
                        expiresAt);
                }
                else if (timeUntilExpiry.TotalDays <= 30)
                {
                    warningCount++;
                    _logger.LogWarning(
                        "[CERTIFICATE EXPIRING SOON] ZIMRA Device '{ZimraDeviceId}' (Tenant: '{TenantApiKey}', Business: '{BusinessName}') expires in {DaysRemaining:F1} days (Expires: {ExpirationDate:yyyy-MM-dd HH:mm:ss UTC}). Renewal required.",
                        device.ZimraDeviceId,
                        device.TenantAccount?.TenantApiKey ?? "N/A",
                        device.TenantAccount?.BusinessName ?? "Unknown",
                        timeUntilExpiry.TotalDays,
                        expiresAt);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to parse X.509 Certificate payload for ZIMRA Device '{ZimraDeviceId}' (Tenant ID {TenantId}).",
                    device.ZimraDeviceId,
                    device.TenantId);
            }
        }

        _logger.LogInformation(
            "Completed X.509 Certificate Expiry Audit: Audited {Total} active device certs. Warnings: {Warnings}, Expired: {Expired}.",
            totalAudited,
            warningCount,
            expiredCount);
    }
}
