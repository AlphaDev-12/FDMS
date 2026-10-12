using FdmsGatewayApi.Data;
using FdmsGatewayApi.Models.Dtos;
using FdmsGatewayApi.Models.Entities;
using FdmsGatewayApi.Services.Implementation;
using FdmsGatewayApi.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FdmsGatewayApi.Tests;

public class MultiDeviceRoutingTests
{
    private FdmsDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<FdmsDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new FdmsDbContext(options);
    }

    [Fact]
    public async Task ProcessDocumentAsync_TargetedDeviceId_SelectsCorrectDeviceConfig()
    {
        // Arrange
        using var db = CreateInMemoryDbContext();
        db.DeviceConfigs.Add(new DeviceConfigEntity
        {
            DeviceId = 101,
            FiscalDayStatus = "FiscalDayClosed", // Device 101 is closed
            TaxPayerName = "Till 1 Vendor"
        });
        db.DeviceConfigs.Add(new DeviceConfigEntity
        {
            DeviceId = 102,
            FiscalDayStatus = "FiscalDayOpened", // Device 102 is open
            TaxPayerName = "Till 2 Vendor"
        });
        await db.SaveChangesAsync();

        var valMock = new Mock<IFdmsValidationService>();
        var cryptoMock = new Mock<IFdmsCryptoService>();
        var apiMock = new Mock<IFdmsApiClient>();
        var splitMock = new Mock<ICurrencySplitService>();
        var taxMock = new Mock<ITaxCodeMappingService>();
        var webhookMock = new Mock<IWebhookDispatchService>();
        var healthMock = new Mock<IZimraHealthCheck>();
        var keyStoreMock = new Mock<IEncryptedKeyStore>();
        var escPosMock = new Mock<IEscPosPrintService>();
        var auditMock = new Mock<IAuditLogger>();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            { "FdmsGateway:PassPhrase", "FDMS_ZIMRA_SECRET_KEY_2026" },
            { "FdmsGateway:PrintOptions:PaperWidthMm", "80" },
            { "FdmsGateway:AsyncFastRetailMode", "true" }
        }).Build();

        keyStoreMock.Setup(k => k.DecryptKey(It.IsAny<string>(), It.IsAny<string>())).Returns("DEC_KEY");
        cryptoMock.Setup(c => c.ComputeReceiptHash(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<decimal>(), It.IsAny<List<ErpReceiptTaxDto>>(), It.IsAny<string>()))
            .Returns("HASH123");
        cryptoMock.Setup(c => c.SignReceipt(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(new SignatureDataDto { Hash = "HASH123", Signature = "SIG123" });

        splitMock.Setup(s => s.SplitIfNeeded(It.IsAny<ErpInvoiceRequestDto>()))
            .Returns<ErpInvoiceRequestDto>(req => new List<ErpInvoiceRequestDto> { req });

        var orchestrator = new FiscalDocumentOrchestrator(
            db, valMock.Object, cryptoMock.Object, apiMock.Object,
            splitMock.Object, taxMock.Object, webhookMock.Object,
            healthMock.Object, keyStoreMock.Object, escPosMock.Object,
            auditMock.Object, config, NullLogger<FiscalDocumentOrchestrator>.Instance);

        var request = new ErpInvoiceRequestDto
        {
            InvoiceNo = "INV-TILL2-01",
            DeviceId = 102, // Explicitly target Till 2
            Currency = "USD",
            ReceiptTotal = 75.00m
        };

        // Act
        var result = await orchestrator.ProcessDocumentAsync(request, "FiscalInvoice");

        // Assert - Should proceed (Status != 423) because Device 102 is FiscalDayOpened, whereas Device 101 is Closed
        Assert.NotEqual(423, result.StatusCode);
    }

    [Fact]
    public async Task ProcessDocumentAsync_UnregisteredDeviceId_Returns404NotFound()
    {
        // Arrange
        using var db = CreateInMemoryDbContext();
        db.DeviceConfigs.Add(new DeviceConfigEntity
        {
            DeviceId = 101,
            FiscalDayStatus = "FiscalDayOpened"
        });
        await db.SaveChangesAsync();

        var valMock = new Mock<IFdmsValidationService>();
        var cryptoMock = new Mock<IFdmsCryptoService>();
        var apiMock = new Mock<IFdmsApiClient>();
        var splitMock = new Mock<ICurrencySplitService>();
        var taxMock = new Mock<ITaxCodeMappingService>();
        var webhookMock = new Mock<IWebhookDispatchService>();
        var healthMock = new Mock<IZimraHealthCheck>();
        var keyStoreMock = new Mock<IEncryptedKeyStore>();
        var escPosMock = new Mock<IEscPosPrintService>();
        var auditMock = new Mock<IAuditLogger>();
        var configMock = new Mock<IConfiguration>();

        var orchestrator = new FiscalDocumentOrchestrator(
            db, valMock.Object, cryptoMock.Object, apiMock.Object,
            splitMock.Object, taxMock.Object, webhookMock.Object,
            healthMock.Object, keyStoreMock.Object, escPosMock.Object,
            auditMock.Object, configMock.Object, NullLogger<FiscalDocumentOrchestrator>.Instance);

        var request = new ErpInvoiceRequestDto
        {
            InvoiceNo = "INV-UNKNOWN-DEV",
            DeviceId = 999, // Unregistered device
            Currency = "USD",
            ReceiptTotal = 25.00m
        };

        // Act
        var result = await orchestrator.ProcessDocumentAsync(request, "FiscalInvoice");

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task ProcessDocumentAsync_OmittedDeviceId_FallsBackToFirstDevice()
    {
        // Arrange
        using var db = CreateInMemoryDbContext();
        db.DeviceConfigs.Add(new DeviceConfigEntity
        {
            DeviceId = 101,
            FiscalDayStatus = "FiscalDayClosed"
        });
        await db.SaveChangesAsync();

        var valMock = new Mock<IFdmsValidationService>();
        var cryptoMock = new Mock<IFdmsCryptoService>();
        var apiMock = new Mock<IFdmsApiClient>();
        var splitMock = new Mock<ICurrencySplitService>();
        var taxMock = new Mock<ITaxCodeMappingService>();
        var webhookMock = new Mock<IWebhookDispatchService>();
        var healthMock = new Mock<IZimraHealthCheck>();
        var keyStoreMock = new Mock<IEncryptedKeyStore>();
        var escPosMock = new Mock<IEscPosPrintService>();
        var auditMock = new Mock<IAuditLogger>();
        var configMock = new Mock<IConfiguration>();

        var orchestrator = new FiscalDocumentOrchestrator(
            db, valMock.Object, cryptoMock.Object, apiMock.Object,
            splitMock.Object, taxMock.Object, webhookMock.Object,
            healthMock.Object, keyStoreMock.Object, escPosMock.Object,
            auditMock.Object, configMock.Object, NullLogger<FiscalDocumentOrchestrator>.Instance);

        var request = new ErpInvoiceRequestDto
        {
            InvoiceNo = "INV-FALLBACK-01",
            DeviceId = null, // Omitted DeviceId
            Currency = "USD",
            ReceiptTotal = 30.00m
        };

        // Act
        var result = await orchestrator.ProcessDocumentAsync(request, "FiscalInvoice");

        // Assert - Should fallback to Device 101 (which is closed -> 423)
        Assert.False(result.IsSuccess);
        Assert.Equal(423, result.StatusCode);
    }
}
