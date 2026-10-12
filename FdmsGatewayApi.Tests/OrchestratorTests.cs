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

public class OrchestratorTests
{
    private FdmsDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<FdmsDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new FdmsDbContext(options);
    }

    [Fact]
    public async Task ProcessDocumentAsync_ClosedFiscalDay_ReturnsLockedStatus423()
    {
        // Arrange
        using var db = CreateInMemoryDbContext();
        db.DeviceConfigs.Add(new DeviceConfigEntity
        {
            DeviceId = 101,
            FiscalDayStatus = "FiscalDayClosed",
            ActivationKey = "KEY123"
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
            InvoiceNo = "INV-CLOSED-01",
            Currency = "USD",
            ReceiptTotal = 50.00m
        };

        // Act
        var result = await orchestrator.ProcessDocumentAsync(request, "FiscalInvoice");

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(423, result.StatusCode);
        Assert.NotNull(result.ErrorObject);
    }

    [Fact]
    public async Task ProcessDocumentAsync_IdempotencyMatch_ReturnsExistingDocument()
    {
        // Arrange
        using var db = CreateInMemoryDbContext();
        db.FiscalDocuments.Add(new FiscalDocumentEntity
        {
            InvoiceNo = "INV-EXISTING-01",
            DeviceId = 101,
            ReceiptType = "FiscalInvoice",
            FiscalStatus = "Fiscalized",
            ReceiptID = 999,
            ReceiptGlobalNo = 12,
            ReceiptCounter = 1,
            FiscalDayNo = 5,
            TotalAmount = 100.00m,
            Currency = "USD"
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
            InvoiceNo = "INV-EXISTING-01",
            Currency = "USD",
            ReceiptTotal = 100.00m
        };

        // Act
        var result = await orchestrator.ProcessDocumentAsync(request, "FiscalInvoice");

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(200, result.StatusCode);
        Assert.NotNull(result.Response);
        Assert.Equal("INV-EXISTING-01", result.Response.InvoiceNo);
        Assert.Equal(999, result.Response.ReceiptID);
    }

    [Fact]
    public async Task ProcessDocumentAsync_HonorsCancellationTokenCancellation()
    {
        // Arrange
        using var db = CreateInMemoryDbContext();
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

        var cts = new CancellationTokenSource();
        cts.Cancel(); // Pre-canceled token

        var request = new ErpInvoiceRequestDto { InvoiceNo = "INV-CANCEL-01" };

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await orchestrator.ProcessDocumentAsync(request, "FiscalInvoice", creditDebitRef: null, cts.Token);
        });
    }
}
