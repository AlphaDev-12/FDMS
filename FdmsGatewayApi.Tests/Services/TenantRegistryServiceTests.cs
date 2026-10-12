using FdmsGatewayApi.Data;
using FdmsGatewayApi.Models.Entities;
using FdmsGatewayApi.Services.Implementation;
using FdmsGatewayApi.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FdmsGatewayApi.Tests.Services;

public class TenantRegistryServiceTests
{
    private FdmsDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<FdmsDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new FdmsDbContext(options);
    }

    [Fact]
    public async Task GetTenantContextAsync_ValidActiveTenant_ReturnsPopulatedTenantContext()
    {
        // Arrange
        using var db = CreateInMemoryDbContext();
        var partner = new SharedPartnerProfileEntity
        {
            PartnerId = 1,
            PartnerKey = "partner_css",
            WholesaleTokenBalance = 100
        };
        db.SharedPartnerProfiles.Add(partner);

        var tenant = new TenantBusinessAccountEntity
        {
            TenantId = 10,
            PartnerId = 1,
            TenantApiKey = "css_tenant_key_01",
            BusinessName = "Harare Supermarket",
            IsActive = true
        };
        db.TenantBusinessAccounts.Add(tenant);

        db.TenantZimraDevices.Add(new TenantZimraDeviceEntity
        {
            TenantId = 10,
            ZimraDeviceId = "DEV-9001",
            EncryptedPfxCertificate = new byte[] { 1, 2, 3 },
            CertificateDecryptionSalt = "salt",
            IsActive = true
        });
        await db.SaveChangesAsync();

        var keyStoreMock = new Mock<IEncryptedKeyStore>();
        var service = new TenantRegistryService(db, keyStoreMock.Object, NullLogger<TenantRegistryService>.Instance);

        // Act
        var context = await service.GetTenantContextAsync("css_tenant_key_01");

        // Assert
        Assert.NotNull(context);
        Assert.Equal(10, context.TenantId);
        Assert.Equal(1, context.PartnerId);
        Assert.Equal("css_tenant_key_01", context.TenantApiKey);
        Assert.True(context.IsActive);
        Assert.True(context.IsAuthorizedForDevice("DEV-9001"));
        Assert.False(context.IsAuthorizedForDevice("DEV-9999"));
    }

    [Fact]
    public async Task GetTenantContextAsync_DepletedPartnerTokenBalance_MarksTenantInactive()
    {
        // Arrange
        using var db = CreateInMemoryDbContext();
        var partner = new SharedPartnerProfileEntity
        {
            PartnerId = 2,
            PartnerKey = "depleted_partner",
            WholesaleTokenBalance = 0 // Depleted balance
        };
        db.SharedPartnerProfiles.Add(partner);

        var tenant = new TenantBusinessAccountEntity
        {
            TenantId = 20,
            PartnerId = 2,
            TenantApiKey = "tenant_key_depleted",
            BusinessName = "Bulawayo Traders",
            IsActive = true
        };
        db.TenantBusinessAccounts.Add(tenant);
        await db.SaveChangesAsync();

        var keyStoreMock = new Mock<IEncryptedKeyStore>();
        var service = new TenantRegistryService(db, keyStoreMock.Object, NullLogger<TenantRegistryService>.Instance);

        // Act
        var context = await service.GetTenantContextAsync("tenant_key_depleted");

        // Assert
        Assert.NotNull(context);
        Assert.False(context.IsActive); // Should be inactive due to zero partner token balance
    }

    [Fact]
    public async Task GetEncryptedDeviceCertificateAsync_ExistingDevice_ReturnsCertBytes()
    {
        // Arrange
        using var db = CreateInMemoryDbContext();
        byte[] expectedCert = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        db.TenantZimraDevices.Add(new TenantZimraDeviceEntity
        {
            TenantId = 5,
            ZimraDeviceId = "DEV-8888",
            EncryptedPfxCertificate = expectedCert,
            CertificateDecryptionSalt = "salt",
            IsActive = true
        });
        await db.SaveChangesAsync();

        var keyStoreMock = new Mock<IEncryptedKeyStore>();
        var service = new TenantRegistryService(db, keyStoreMock.Object, NullLogger<TenantRegistryService>.Instance);

        // Act
        var certBytes = await service.GetEncryptedDeviceCertificateAsync(5, "DEV-8888");

        // Assert
        Assert.Equal(expectedCert, certBytes);
    }

    [Fact]
    public async Task GetCertificatePasswordAsync_EncryptedPassword_DecryptsViaKeyStore()
    {
        // Arrange
        using var db = CreateInMemoryDbContext();
        db.TenantZimraDevices.Add(new TenantZimraDeviceEntity
        {
            TenantId = 5,
            ZimraDeviceId = "DEV-8888",
            EncryptedPfxCertificate = new byte[] { 1 },
            CertificateDecryptionSalt = "my_salt",
            CertificatePassword = "ENCRYPTED_PASS_BLOB",
            IsActive = true
        });
        await db.SaveChangesAsync();

        var keyStoreMock = new Mock<IEncryptedKeyStore>();
        keyStoreMock.Setup(k => k.DecryptKey("ENCRYPTED_PASS_BLOB", "my_salt"))
            .Returns("DECRYPTED_SECRET_123");

        var service = new TenantRegistryService(db, keyStoreMock.Object, NullLogger<TenantRegistryService>.Instance);

        // Act
        string pass = await service.GetCertificatePasswordAsync(5, "DEV-8888");

        // Assert
        Assert.Equal("DECRYPTED_SECRET_123", pass);
    }
}
