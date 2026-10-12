using FdmsGatewayApi.Controllers;
using FdmsGatewayApi.Data;
using FdmsGatewayApi.Models.Dtos;
using FdmsGatewayApi.Models.Entities;
using FdmsGatewayApi.Services.Implementation;
using FdmsGatewayApi.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FdmsGatewayApi.Tests;

public class BulkInvoiceIngestTests
{
    private FdmsDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<FdmsDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new FdmsDbContext(options);
    }

    [Fact]
    public async Task ProcessBulkDocumentsAsync_ProcessesAllInvoicesInArray()
    {
        // Arrange
        using var db = CreateInMemoryDbContext();
        db.DeviceConfigs.Add(new DeviceConfigEntity
        {
            DeviceId = 1,
            FiscalDayStatus = "FiscalDayClosed", // Will test per-item status capture
            ActivationKey = "TESTKEY"
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

        var bulkReq = new ErpBulkInvoiceRequestDto
        {
            BatchId = "BATCH-UNIT-TEST-101",
            Invoices = new List<ErpInvoiceRequestDto>
            {
                new ErpInvoiceRequestDto { InvoiceNo = "INV-BULK-001", Currency = "USD", ReceiptTotal = 100m, DeviceId = 1 },
                new ErpInvoiceRequestDto { InvoiceNo = "INV-BULK-002", Currency = "USD", ReceiptTotal = 250m, DeviceId = 1 }
            }
        };

        // Act
        var result = await orchestrator.ProcessBulkDocumentsAsync(bulkReq);

        // Assert
        Assert.Equal("BATCH-UNIT-TEST-101", result.BatchId);
        Assert.Equal(2, result.TotalReceived);
        Assert.Equal(2, result.ProcessedCount);
        Assert.Equal(2, result.FailedCount); // 2 failed because fiscal day is closed
        Assert.Equal(2, result.Results.Count);
        Assert.Equal("INV-BULK-001", result.Results[0].InvoiceNo);
        Assert.Equal("INV-BULK-002", result.Results[1].InvoiceNo);
        Assert.Equal(423, result.Results[0].StatusCode);
    }

    [Fact]
    public async Task Controller_FiscalizeInvoiceBulk_EmptyList_ReturnsBadRequest()
    {
        // Arrange
        using var db = CreateInMemoryDbContext();
        var orchMock = new Mock<IFiscalDocumentOrchestrator>();
        var controller = new ErpIngestionController(orchMock.Object, db, NullLogger<ErpIngestionController>.Instance);

        var req = new ErpBulkInvoiceRequestDto { Invoices = new List<ErpInvoiceRequestDto>() };

        // Act
        var response = await controller.FiscalizeInvoiceBulk(req);

        // Assert
        var badReq = Assert.IsType<BadRequestObjectResult>(response);
        Assert.Equal(StatusCodes.Status400BadRequest, badReq.StatusCode);
    }

    [Fact]
    public async Task Controller_FiscalizeInvoiceBulk_ValidList_ReturnsOk()
    {
        // Arrange
        using var db = CreateInMemoryDbContext();
        var orchMock = new Mock<IFiscalDocumentOrchestrator>();
        orchMock.Setup(o => o.ProcessBulkDocumentsAsync(It.IsAny<ErpBulkInvoiceRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ErpBulkInvoiceResponseDto
            {
                BatchId = "BATCH-DIRECT-01",
                TotalReceived = 1,
                ProcessedCount = 1,
                SuccessCount = 1,
                Results = new List<ErpBulkItemResultDto>
                {
                    new ErpBulkItemResultDto { InvoiceNo = "INV-ARR-01", FiscalStatus = "Fiscalized", StatusCode = 200 }
                }
            });

        var controller = new ErpIngestionController(orchMock.Object, db, NullLogger<ErpIngestionController>.Instance);

        var req = new ErpBulkInvoiceRequestDto
        {
            BatchId = "BATCH-DIRECT-01",
            Invoices = new List<ErpInvoiceRequestDto>
            {
                new ErpInvoiceRequestDto { InvoiceNo = "INV-ARR-01", Currency = "USD", ReceiptTotal = 50m }
            }
        };

        // Act
        var response = await controller.FiscalizeInvoiceBulk(req);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(response);
        var bulkResp = Assert.IsType<ErpBulkInvoiceResponseDto>(okResult.Value);
        Assert.Equal("BATCH-DIRECT-01", bulkResp.BatchId);
        Assert.Equal(1, bulkResp.SuccessCount);
    }
}

