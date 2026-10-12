using FdmsGatewayApi.Data;
using FdmsGatewayApi.Models.Entities;
using FdmsGatewayApi.Services.Implementation;
using FdmsGatewayApi.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Moq;
using Xunit;

namespace FdmsGatewayApi.Tests;

public class ProductionHardeningTests
{
    private static IServiceProvider CreateServiceProviderWithDb(FdmsDbContext db)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => db);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void LocalEncryptedKeyStore_EncryptAndDecrypt_RoundTripSuccessWith600kIterations()
    {
        // Arrange
        var keyStore = new LocalEncryptedKeyStore();
        string plainPem = "-----BEGIN PRIVATE KEY-----\nMIIEvgIBADANBgkqhkiG9w0BAQEFAASCBKgwggSkAgEAAoIBAQC...\n-----END PRIVATE KEY-----";
        string passPhrase = "SecretProductionPassphrase2026!";

        // Act
        string encrypted = keyStore.EncryptKey(plainPem, passPhrase);
        string decrypted = keyStore.DecryptKey(encrypted, passPhrase);

        // Assert
        Assert.NotEmpty(encrypted);
        Assert.NotEqual(plainPem, encrypted);
        Assert.Equal(plainPem, decrypted);
    }

    [Fact]
    public void LocalEncryptedKeyStore_DecryptLegacyKey_FallbackSuccess()
    {
        // Arrange
        var keyStore = new LocalEncryptedKeyStore();
        string plainPem = "-----BEGIN EC PRIVATE KEY-----\nMHcCAQEEI... legacy key ...\n-----END EC PRIVATE KEY-----";
        string passPhrase = "FDMS_ZIMRA_SECRET_KEY_2026";

        // Encrypt manually with legacy 10,000 iterations using Rfc2898DeriveBytes
        byte[] salt = System.Text.Encoding.UTF8.GetBytes("FDMS_ZIMRA_KEY_STORE_SALT_v1");
        using var aes = System.Security.Cryptography.Aes.Create();
        using var deriveBytes = new System.Security.Cryptography.Rfc2898DeriveBytes(passPhrase, salt, 10000, System.Security.Cryptography.HashAlgorithmName.SHA256);
        aes.Key = deriveBytes.GetBytes(32);
        aes.GenerateIV();
        using var encryptor = aes.CreateEncryptor();
        byte[] plainBytes = System.Text.Encoding.UTF8.GetBytes(plainPem);
        byte[] cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);
        byte[] result = new byte[aes.IV.Length + cipherBytes.Length];
        Array.Copy(aes.IV, 0, result, 0, aes.IV.Length);
        Array.Copy(cipherBytes, 0, result, aes.IV.Length, cipherBytes.Length);
        string legacyEncryptedBase64 = Convert.ToBase64String(result);

        // Act - should attempt 600k first, fail, then fallback to 10k and succeed
        string decrypted = keyStore.DecryptKey(legacyEncryptedBase64, passPhrase);

        // Assert
        Assert.Equal(plainPem, decrypted);
    }

    [Fact]
    public async Task HealthChecks_OfflineQueueHealthCheck_ReturnsHealthyWhenNoDeadLetters()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<FdmsDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        using var db = new FdmsDbContext(options);
        db.OfflineQueue.Add(new OfflineQueueEntity
        {
            DeviceId = 1,
            InvoiceNo = "INV-001",
            ReceiptGlobalNo = 1,
            ReceiptType = "FiscalInvoice",
            SubmitPayloadJson = "{}",
            Status = "Queued"
        });
        await db.SaveChangesAsync();

        var serviceProvider = CreateServiceProviderWithDb(db);
        var check = new OfflineQueueHealthCheck(serviceProvider);

        // Act
        var result = await check.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task HealthChecks_OfflineQueueHealthCheck_ReturnsDegradedWhenDeadLetterExists()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<FdmsDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        using var db = new FdmsDbContext(options);
        db.OfflineQueue.Add(new OfflineQueueEntity
        {
            DeviceId = 1,
            InvoiceNo = "INV-DEAD-001",
            ReceiptGlobalNo = 5,
            ReceiptType = "FiscalInvoice",
            SubmitPayloadJson = "{}",
            Status = "DeadLetter",
            RetryCount = 10,
            ErrorMessage = "ZIMRA endpoint repeatedly timed out"
        });
        await db.SaveChangesAsync();

        var serviceProvider = CreateServiceProviderWithDb(db);
        var check = new OfflineQueueHealthCheck(serviceProvider);

        // Act
        var result = await check.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Contains("dead-lettered item(s)", result.Description);
    }

    [Fact]
    public async Task ZimraGatewayHealthCheck_ReturnsDegradedWhenGatewayUnreachable()
    {
        // Arrange
        var zimraHealthMock = new Mock<IZimraHealthCheck>();
        zimraHealthMock.Setup(z => z.IsHealthy).Returns(false);
        zimraHealthMock.Setup(z => z.LastCheckedUtc).Returns(DateTime.UtcNow);
        zimraHealthMock.Setup(z => z.LastLatencyMs).Returns(5000);
        zimraHealthMock.Setup(z => z.LastError).Returns("HTTP 502 Bad Gateway");

        var check = new ZimraGatewayHealthCheck(zimraHealthMock.Object);

        // Act
        var result = await check.CheckHealthAsync(new HealthCheckContext());

        // Assert
        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Contains("ZIMRA Gateway unreachable", result.Description);
    }
}
