using System.Text;
using System.Text.Json;
using FdmsGatewayApi.Data;
using FdmsGatewayApi.Models.Dtos;
using FdmsGatewayApi.Models.Entities;
using FdmsGatewayApi.Services.Implementation;
using FdmsGatewayApi.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace FdmsGatewayApi.Tests;

public class OfflineFileServiceTests
{
    private readonly Mock<IFdmsApiClient> _mockApiClient;
    private readonly Mock<IEncryptedKeyStore> _mockKeyStore;
    private readonly Mock<IFdmsCryptoService> _mockCryptoService;
    private readonly Mock<IAuditLogger> _mockAuditLogger;
    private readonly Mock<ILogger<FdmsOfflineFileService>> _mockLogger;
    private readonly IConfiguration _config;

    public OfflineFileServiceTests()
    {
        _mockApiClient = new Mock<IFdmsApiClient>();
        _mockKeyStore = new Mock<IEncryptedKeyStore>();
        _mockCryptoService = new Mock<IFdmsCryptoService>();
        _mockAuditLogger = new Mock<IAuditLogger>();
        _mockLogger = new Mock<ILogger<FdmsOfflineFileService>>();

        var myConfig = new Dictionary<string, string?>
        {
            { "FdmsGateway:PassPhrase", "FDMS_ZIMRA_SECRET_KEY_2026" }
        };
        _config = new ConfigurationBuilder().AddInMemoryCollection(myConfig).Build();
    }

    private FdmsDbContext CreateInMemoryDbContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<FdmsDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
        return new FdmsDbContext(options);
    }

    [Fact]
    public async Task PackageAndSubmitOfflineBatchAsync_NoQueuedItems_ThrowsInvalidOperationException()
    {
        using var db = CreateInMemoryDbContext(Guid.NewGuid().ToString());
        db.DeviceConfigs.Add(new DeviceConfigEntity
        {
            DeviceId = 100,
            FiscalDayStatus = "FiscalDayOpened",
            CurrentFiscalDayNo = 1
        });
        await db.SaveChangesAsync();

        var service = new FdmsOfflineFileService(
            db, _mockApiClient.Object, _mockKeyStore.Object, _mockCryptoService.Object,
            _mockAuditLogger.Object, _config, _mockLogger.Object);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.PackageAndSubmitOfflineBatchAsync(100));
    }

    [Fact]
    public async Task PackageAndSubmitOfflineBatchAsync_QueuedItems_PackagesBase64FileAndSubmitsToZimra()
    {
        using var db = CreateInMemoryDbContext(Guid.NewGuid().ToString());
        db.DeviceConfigs.Add(new DeviceConfigEntity
        {
            DeviceId = 200,
            FiscalDayStatus = "FiscalDayOpened",
            CurrentFiscalDayNo = 3,
            EncryptedPrivateKeyPem = "ENC_KEY"
        });

        var receiptPayload = new FdmsReceiptPayload
        {
            ReceiptType = "FiscalInvoice",
            ReceiptCurrency = "USD",
            ReceiptCounter = 1,
            ReceiptGlobalNo = 1001,
            InvoiceNo = "OFF-INV-001",
            ReceiptDate = DateTime.UtcNow,
            ReceiptTotal = 150.00m
        };

        db.OfflineQueue.Add(new OfflineQueueEntity
        {
            DeviceId = 200,
            InvoiceNo = "OFF-INV-001",
            ReceiptGlobalNo = 1001,
            ReceiptType = "FiscalInvoice",
            SubmitPayloadJson = JsonSerializer.Serialize(receiptPayload),
            Status = "Queued"
        });

        await db.SaveChangesAsync();

        _mockKeyStore.Setup(k => k.DecryptKey(It.IsAny<string>(), It.IsAny<string>())).Returns("DEC_KEY");
        _mockApiClient.Setup(a => a.SubmitFileAsync(It.IsAny<SubmitFileRequest>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubmitFileResponse { OperationID = "OP_OFFLINE_999" });

        var service = new FdmsOfflineFileService(
            db, _mockApiClient.Object, _mockKeyStore.Object, _mockCryptoService.Object,
            _mockAuditLogger.Object, _config, _mockLogger.Object);

        var result = await service.PackageAndSubmitOfflineBatchAsync(200);

        Assert.NotNull(result);
        Assert.Equal(200, result.DeviceId);
        Assert.Equal("OP_OFFLINE_999", result.OperationId);
        Assert.Equal(1, result.ReceiptCount);
        Assert.Equal("Submitted", result.Status);

        // Verify base64 decoding contains valid JSON header with device ID
        byte[] decodedBytes = Convert.FromBase64String(result.Base64FilePayload);
        string decodedJson = Encoding.UTF8.GetString(decodedBytes);
        Assert.Contains("\"deviceID\":200", decodedJson);
        Assert.Contains("OFF-INV-001", decodedJson);

        // Verify submitFile API call
        _mockApiClient.Verify(a => a.SubmitFileAsync(
            It.Is<SubmitFileRequest>(r => r.DeviceID == 200 && !string.IsNullOrEmpty(r.File)),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CheckFileStatusAsync_PollsZimraAndUpdatesLocalDbStatus()
    {
        using var db = CreateInMemoryDbContext(Guid.NewGuid().ToString());
        db.DeviceConfigs.Add(new DeviceConfigEntity { DeviceId = 300 });

        db.OfflineFiles.Add(new OfflineFileEntity
        {
            DeviceId = 300,
            OperationId = "OP_CHECK_777",
            FiscalDayNo = 5,
            FileSequence = 1,
            Status = "Submitted",
            SubmittedAt = DateTime.UtcNow.AddHours(-1)
        });

        await db.SaveChangesAsync();

        _mockApiClient.Setup(a => a.GetFileStatusAsync(It.IsAny<GetFileStatusRequest>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetFileStatusResponse
            {
                OperationID = "OP_CHECK_777",
                FileStatus = new List<FileStatusItemDto>
                {
                    new FileStatusItemDto
                    {
                        OperationID = "OP_CHECK_777",
                        DeviceId = 300,
                        FileProcessingStatus = "FileProcessingIsSuccessful",
                        FileProcessingDate = DateTime.UtcNow,
                        FiscalDayNo = 5
                    }
                }
            });

        var service = new FdmsOfflineFileService(
            db, _mockApiClient.Object, _mockKeyStore.Object, _mockCryptoService.Object,
            _mockAuditLogger.Object, _config, _mockLogger.Object);

        var statuses = await service.CheckFileStatusAsync(300, "OP_CHECK_777");

        Assert.NotEmpty(statuses);
        Assert.Equal("FileProcessingIsSuccessful", statuses[0].FileProcessingStatus);

        // Verify updated local record
        var updatedFile = await db.OfflineFiles.FirstOrDefaultAsync(f => f.OperationId == "OP_CHECK_777");
        Assert.NotNull(updatedFile);
        Assert.Equal("FileProcessingIsSuccessful", updatedFile.Status);
    }
}
