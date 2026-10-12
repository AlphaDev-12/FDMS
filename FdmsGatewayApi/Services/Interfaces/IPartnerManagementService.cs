namespace FdmsGatewayApi.Services.Interfaces;

public class OnboardTenantRequestDto
{
    public string PartnerKey { get; set; } = string.Empty;
    public string BusinessName { get; set; } = string.Empty;
    public string? DesiredTenantApiKey { get; set; }
}

public class OnboardTenantResponseDto
{
    public int TenantId { get; set; }
    public int PartnerId { get; set; }
    public string TenantApiKey { get; set; } = string.Empty;
    public string BusinessName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class RegisterDeviceRequestDto
{
    public string TenantApiKey { get; set; } = string.Empty;
    public string ZimraDeviceId { get; set; } = string.Empty;
    public byte[] PfxCertificateBytes { get; set; } = Array.Empty<byte>();
    public string CertificateDecryptionSalt { get; set; } = string.Empty;
    public string? CertificatePassword { get; set; }
}

public class PartnerDashboardSummaryDto
{
    public int PartnerId { get; set; }
    public string PartnerKey { get; set; } = string.Empty;
    public decimal WholesaleTokenBalance { get; set; }
    public int TotalTenantAccounts { get; set; }
    public int ActiveTenantAccounts { get; set; }
    public int TotalRegisteredDevices { get; set; }
    public int ActiveRegisteredDevices { get; set; }
    public double DailyBurnRate { get; set; }
    public int ProjectedDaysRemaining { get; set; }
    public bool IsLowBalanceWarning { get; set; }
    public List<TenantSummaryDto> Tenants { get; set; } = new();
}

public class TenantSummaryDto
{
    public int TenantId { get; set; }
    public string TenantApiKey { get; set; } = string.Empty;
    public string BusinessName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public int DeviceCount { get; set; }
    public DateTime CreatedAt { get; set; }
}

public interface IPartnerManagementService
{
    Task EnsurePartnerProfileAsync(string partnerKey, string partnerName, decimal initialTokens, CancellationToken cancellationToken = default);
    Task<OnboardTenantResponseDto> OnboardTenantAsync(OnboardTenantRequestDto request, CancellationToken cancellationToken = default);
    Task<bool> RegisterDeviceAsync(RegisterDeviceRequestDto request, CancellationToken cancellationToken = default);
    Task<PartnerDashboardSummaryDto?> GetPartnerDashboardAsync(string partnerKey, CancellationToken cancellationToken = default);
}
