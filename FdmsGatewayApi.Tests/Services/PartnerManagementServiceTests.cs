using FdmsGatewayApi.Data;
using FdmsGatewayApi.Models.Entities;
using FdmsGatewayApi.Services.Implementation;
using FdmsGatewayApi.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FdmsGatewayApi.Tests.Services;

public class PartnerManagementServiceTests
{
    private FdmsDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<FdmsDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new FdmsDbContext(options);
    }

    [Fact]
    public async Task EnsurePartnerProfileAsync_NewPartner_CreatesProfileWithTokens()
    {
        // Arrange
        using var db = CreateInMemoryDbContext();
        var keyStoreMock = new Mock<IEncryptedKeyStore>();
        var tenantRegistryMock = new Mock<ITenantRegistryService>();

        var service = new PartnerManagementService(db, keyStoreMock.Object, tenantRegistryMock.Object, NullLogger<PartnerManagementService>.Instance);

        // Act
        await service.EnsurePartnerProfileAsync("partner_ttcs", "TTCS Consultancy", 500m);

        // Assert
        var partner = await db.SharedPartnerProfiles.FirstOrDefaultAsync(p => p.PartnerKey == "partner_ttcs");
        Assert.NotNull(partner);
        Assert.Equal(500m, partner.WholesaleTokenBalance);
    }

    [Fact]
    public async Task OnboardTenantAsync_ValidPartnerKey_CreatesTenantAccount()
    {
        // Arrange
        using var db = CreateInMemoryDbContext();
        db.SharedPartnerProfiles.Add(new SharedPartnerProfileEntity
        {
            PartnerId = 1,
            PartnerKey = "partner_serpa",
            WholesaleTokenBalance = 50m
        });
        await db.SaveChangesAsync();

        var keyStoreMock = new Mock<IEncryptedKeyStore>();
        var tenantRegistryMock = new Mock<ITenantRegistryService>();
        var service = new PartnerManagementService(db, keyStoreMock.Object, tenantRegistryMock.Object, NullLogger<PartnerManagementService>.Instance);

        var request = new OnboardTenantRequestDto
        {
            PartnerKey = "partner_serpa",
            BusinessName = "Victoria Falls Hardware",
            DesiredTenantApiKey = "custom_serpa_key_001"
        };

        // Act
        var result = await service.OnboardTenantAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("custom_serpa_key_001", result.TenantApiKey);
        Assert.Equal("Victoria Falls Hardware", result.BusinessName);
        Assert.True(result.IsActive);
    }

    [Fact]
    public async Task RegisterDeviceAsync_ValidPayload_EncryptsPasswordAndInvalidatesCache()
    {
        // Arrange
        using var db = CreateInMemoryDbContext();
        db.SharedPartnerProfiles.Add(new SharedPartnerProfileEntity { PartnerId = 1, PartnerKey = "pkey", WholesaleTokenBalance = 10m });
        db.TenantBusinessAccounts.Add(new TenantBusinessAccountEntity
        {
            TenantId = 100,
            PartnerId = 1,
            TenantApiKey = "tkey_100",
            BusinessName = "Mutare Motors",
            IsActive = true
        });
        await db.SaveChangesAsync();

        var keyStoreMock = new Mock<IEncryptedKeyStore>();
        keyStoreMock.Setup(k => k.EncryptKey("plain_pfx_pass", "salt_123"))
            .Returns("ENCRYPTED_PFX_PASS");

        var tenantRegistryMock = new Mock<ITenantRegistryService>();

        var service = new PartnerManagementService(db, keyStoreMock.Object, tenantRegistryMock.Object, NullLogger<PartnerManagementService>.Instance);

        var request = new RegisterDeviceRequestDto
        {
            TenantApiKey = "tkey_100",
            ZimraDeviceId = "DEV-777",
            PfxCertificateBytes = new byte[] { 0x01, 0x02 },
            CertificateDecryptionSalt = "salt_123",
            CertificatePassword = "plain_pfx_pass"
        };

        // Act
        bool success = await service.RegisterDeviceAsync(request);

        // Assert
        Assert.True(success);
        var storedDevice = await db.TenantZimraDevices.FirstOrDefaultAsync(d => d.TenantId == 100 && d.ZimraDeviceId == "DEV-777");
        Assert.NotNull(storedDevice);
        Assert.Equal("ENCRYPTED_PFX_PASS", storedDevice.CertificatePassword);

        // Verify cache invalidation was triggered
        tenantRegistryMock.Verify(t => t.InvalidateDeviceCertificateCache(100, "DEV-777"), Times.Once);
    }

    [Fact]
    public async Task GetPartnerDashboardAsync_CalculatesDailyBurnRateAndRunwayDays()
    {
        // Arrange
        using var db = CreateInMemoryDbContext();
        db.SharedPartnerProfiles.Add(new SharedPartnerProfileEntity { PartnerId = 1, PartnerKey = "partner_runway", WholesaleTokenBalance = 10m });
        db.TenantBusinessAccounts.Add(new TenantBusinessAccountEntity
        {
            TenantId = 10,
            PartnerId = 1,
            TenantApiKey = "tkey_10",
            BusinessName = "Runway Store",
            IsActive = true
        });

        // 30 active devices => 1.0 token/day burn rate => 10 tokens balance = 10 projected days remaining
        for (int i = 1; i <= 30; i++)
        {
            db.TenantZimraDevices.Add(new TenantZimraDeviceEntity { TenantId = 10, ZimraDeviceId = $"DEV-{i}", IsActive = true });
        }
        await db.SaveChangesAsync();

        var keyStoreMock = new Mock<IEncryptedKeyStore>();
        var tenantRegistryMock = new Mock<ITenantRegistryService>();
        var service = new PartnerManagementService(db, keyStoreMock.Object, tenantRegistryMock.Object, NullLogger<PartnerManagementService>.Instance);

        // Act
        var dashboard = await service.GetPartnerDashboardAsync("partner_runway");

        // Assert
        Assert.NotNull(dashboard);
        Assert.Equal(10m, dashboard.WholesaleTokenBalance);
        Assert.Equal(30, dashboard.ActiveRegisteredDevices);
        Assert.Equal(1.0, dashboard.DailyBurnRate);
        Assert.Equal(10, dashboard.ProjectedDaysRemaining);
        Assert.False(dashboard.IsLowBalanceWarning); // 10 days > 7 days warning threshold
    }
}
