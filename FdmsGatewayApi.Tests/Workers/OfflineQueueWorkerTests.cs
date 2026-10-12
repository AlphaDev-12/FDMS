using System.Text.Json;
using FdmsGatewayApi.Data;
using FdmsGatewayApi.Models.Dtos;
using FdmsGatewayApi.Models.Entities;
using FdmsGatewayApi.Services.Implementation;
using FdmsGatewayApi.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace FdmsGatewayApi.Tests.Workers;

public class OfflineQueueWorkerTests
{
    private readonly Mock<IZimraHealthCheck> _mockHealthCheck;
    private readonly Mock<IAuditLogger> _mockAuditLogger;
    private readonly Mock<ILogger<OfflineQueueWorker>> _mockLogger;
    private readonly Mock<IFdmsApiClient> _mockApiClient;
    private readonly Mock<IFdmsCryptoService> _mockCryptoService;
    private readonly Mock<IWebhookDispatchService> _mockWebhookService;
    private readonly Mock<IEncryptedKeyStore> _mockKeyStore;
    private readonly IConfiguration _config;

    public OfflineQueueWorkerTests()
    {
        _mockHealthCheck = new Mock<IZimraHealthCheck>();
        _mockHealthCheck.Setup(h => h.IsHealthy).Returns(true);

        _mockAuditLogger = new Mock<IAuditLogger>();
        _mockLogger = new Mock<ILogger<OfflineQueueWorker>>();
        _mockApiClient = new Mock<IFdmsApiClient>();
        _mockCryptoService = new Mock<IFdmsCryptoService>();
        _mockWebhookService = new Mock<IWebhookDispatchService>();
        _mockKeyStore = new Mock<IEncryptedKeyStore>();

        var myConfig = new Dictionary<string, string?>
        {
            { "FdmsGateway:PassPhrase", "FDMS_ZIMRA_SECRET_KEY_2026" }
        };
        _config = new ConfigurationBuilder().AddInMemoryCollection(myConfig).Build();
    }

    private (ServiceProvider provider, FdmsDbContext db) CreateTestServices(string dbName)
    {
        var services = new ServiceCollection();

        services.AddDbContext<FdmsDbContext>(options =>
            options.UseInMemoryDatabase(dbName));

        services.AddSingleton(_mockHealthCheck.Object);
        services.AddSingleton(_mockAuditLogger.Object);
        services.AddSingleton(_mockApiClient.Object);
        services.AddSingleton(_mockCryptoService.Object);
        services.AddSingleton(_mockWebhookService.Object);
        services.AddSingleton(_mockKeyStore.Object);
        services.AddSingleton(_config);
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var db = provider.GetRequiredService<FdmsDbContext>();

        return (provider, db);
    }

    [Fact]
    public async Task ProcessNextQueueBatchAsync_MultiDevice_IsolatesFailuresAndProcessesInOrder()
    {
        var dbName = Guid.NewGuid().ToString();
        var (provider, db) = CreateTestServices(dbName);

        // Device 1: Missing certificate (will fail)
        db.DeviceConfigs.Add(new DeviceConfigEntity
        {
            DeviceId = 101,
            CertificatePem = "",
            FiscalDayStatus = "FiscalDayOpened",
            CurrentFiscalDayNo = 1
        });

        // Device 2: Valid setup (will succeed)
        db.DeviceConfigs.Add(new DeviceConfigEntity
        {
            DeviceId = 202,
            CertificatePem = "VALID_CERT_PEM",
            EncryptedPrivateKeyPem = "ENC_KEY_PEM",
            FiscalDayStatus = "FiscalDayOpened",
            CurrentFiscalDayNo = 1
        });

        // Queue items for Device 1
        db.OfflineQueue.Add(new OfflineQueueEntity
        {
            DeviceId = 101,
            InvoiceNo = "DEV1-INV-001",
            ReceiptGlobalNo = 10,
            ReceiptType = "FiscalInvoice",
            SubmitPayloadJson = JsonSerializer.Serialize(new FdmsReceiptPayload { InvoiceNo = "DEV1-INV-001" }),
            Status = "Queued",
            RetryCount = 0
        });

        // Queue items for Device 2 (Out of order inserted, should be processed ordered by ReceiptGlobalNo)
        db.OfflineQueue.Add(new OfflineQueueEntity
        {
            DeviceId = 202,
            InvoiceNo = "DEV2-INV-002",
            ReceiptGlobalNo = 20,
            ReceiptType = "FiscalInvoice",
            SubmitPayloadJson = JsonSerializer.Serialize(new FdmsReceiptPayload { InvoiceNo = "DEV2-INV-002" }),
            Status = "Queued",
            RetryCount = 0
        });

        db.OfflineQueue.Add(new OfflineQueueEntity
        {
            DeviceId = 202,
            InvoiceNo = "DEV2-INV-001",
            ReceiptGlobalNo = 19,
            ReceiptType = "FiscalInvoice",
            SubmitPayloadJson = JsonSerializer.Serialize(new FdmsReceiptPayload { InvoiceNo = "DEV2-INV-001" }),
            Status = "Queued",
            RetryCount = 0
        });

        await db.SaveChangesAsync();

        _mockKeyStore.Setup(k => k.DecryptKey(It.IsAny<string>(), It.IsAny<string>())).Returns("DEC_KEY");

        _mockApiClient.Setup(a => a.SubmitReceiptAsync(It.IsAny<SubmitReceiptRequest>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubmitReceiptResponse
            {
                OperationID = "OP_DEV2_100",
                ReceiptID = 5001,
                ServerDate = DateTime.UtcNow,
                ReceiptServerSignature = new SignatureDataDto { Signature = "SERVER_SIG_DEV2" }
            });

        var worker = new OfflineQueueWorker(
            provider,
            _mockHealthCheck.Object,
            _mockAuditLogger.Object,
            _mockLogger.Object);

        // Execute batch processing via Reflection method trigger
        var method = typeof(OfflineQueueWorker).GetMethod("ProcessNextQueueBatchAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(method);

        var task = (Task)method.Invoke(worker, new object[] { CancellationToken.None })!;
        await task;

        // Verify Device 1 item failed due to missing certificate
        var dev1Item = await db.OfflineQueue.AsNoTracking().FirstAsync(q => q.DeviceId == 101);
        Assert.Equal("Failed", dev1Item.Status);
        Assert.Contains("missing client certificate", dev1Item.ErrorMessage);

        // Verify Device 2 items were completed successfully
        var dev2Items = await db.OfflineQueue.AsNoTracking().Where(q => q.DeviceId == 202).OrderBy(q => q.ReceiptGlobalNo).ToListAsync();
        Assert.Equal(2, dev2Items.Count);
        Assert.All(dev2Items, item => Assert.Equal("Completed", item.Status));

        // Verify DEV2-INV-001 (GlobalNo 19) was processed before DEV2-INV-002 (GlobalNo 20)
        Assert.Equal("DEV2-INV-001", dev2Items[0].InvoiceNo);
        Assert.Equal("DEV2-INV-002", dev2Items[1].InvoiceNo);
    }
}
