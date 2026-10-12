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

public class DayManagerTests
{
    private readonly Mock<IFdmsApiClient> _mockApiClient;
    private readonly Mock<IFdmsCryptoService> _mockCryptoService;
    private readonly Mock<IEncryptedKeyStore> _mockKeyStore;
    private readonly Mock<IZReportService> _mockZReportService;
    private readonly Mock<ILogger<FdmsDayManager>> _mockLogger;
    private readonly IConfiguration _config;

    public DayManagerTests()
    {
        _mockApiClient = new Mock<IFdmsApiClient>();
        _mockCryptoService = new Mock<IFdmsCryptoService>();
        _mockKeyStore = new Mock<IEncryptedKeyStore>();
        _mockZReportService = new Mock<IZReportService>();
        _mockLogger = new Mock<ILogger<FdmsDayManager>>();

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
    public async Task OpenFiscalDayAsync_AlreadyOpen_ReturnsDeviceWithoutApiCall()
    {
        using var db = CreateInMemoryDbContext(Guid.NewGuid().ToString());
        db.DeviceConfigs.Add(new DeviceConfigEntity
        {
            DeviceId = 10,
            FiscalDayStatus = "FiscalDayOpened",
            CurrentFiscalDayNo = 5,
            EncryptedPrivateKeyPem = "ENC_KEY"
        });
        await db.SaveChangesAsync();

        var manager = new FdmsDayManager(db, _mockApiClient.Object, _mockCryptoService.Object, _mockKeyStore.Object, _mockZReportService.Object, _config, _mockLogger.Object);

        var result = await manager.OpenFiscalDayAsync(10);

        Assert.Equal("FiscalDayOpened", result.FiscalDayStatus);
        Assert.Equal(5, result.CurrentFiscalDayNo);

        // Verify no call made to ZIMRA openDay API
        _mockApiClient.Verify(a => a.OpenDayAsync(It.IsAny<OpenDayRequest>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task OpenFiscalDayAsync_ClosedDay_OpensDayAndResetsCounter()
    {
        using var db = CreateInMemoryDbContext(Guid.NewGuid().ToString());
        db.DeviceConfigs.Add(new DeviceConfigEntity
        {
            DeviceId = 20,
            FiscalDayStatus = "FiscalDayClosed",
            CurrentFiscalDayNo = 10,
            CurrentReceiptCounter = 50,
            EncryptedPrivateKeyPem = "ENC_KEY"
        });
        await db.SaveChangesAsync();

        _mockKeyStore.Setup(k => k.DecryptKey(It.IsAny<string>(), It.IsAny<string>())).Returns("DEC_KEY");
        _mockApiClient.Setup(a => a.OpenDayAsync(It.IsAny<OpenDayRequest>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OpenDayResponse { FiscalDayNo = 11, OperationID = "OP123" });

        var manager = new FdmsDayManager(db, _mockApiClient.Object, _mockCryptoService.Object, _mockKeyStore.Object, _mockZReportService.Object, _config, _mockLogger.Object);

        var result = await manager.OpenFiscalDayAsync(20);

        Assert.Equal("FiscalDayOpened", result.FiscalDayStatus);
        Assert.Equal(11, result.CurrentFiscalDayNo);
        Assert.Equal(0, result.CurrentReceiptCounter); // Daily counter reset
    }

    [Fact]
    public async Task CloseFiscalDayAsync_NotOpen_ThrowsInvalidOperationException()
    {
        using var db = CreateInMemoryDbContext(Guid.NewGuid().ToString());
        db.DeviceConfigs.Add(new DeviceConfigEntity
        {
            DeviceId = 30,
            FiscalDayStatus = "FiscalDayClosed",
            CurrentFiscalDayNo = 11
        });
        await db.SaveChangesAsync();

        var manager = new FdmsDayManager(db, _mockApiClient.Object, _mockCryptoService.Object, _mockKeyStore.Object, _mockZReportService.Object, _config, _mockLogger.Object);

        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.CloseFiscalDayAsync(30));
    }

    [Fact]
    public async Task CloseFiscalDayAsync_OpenDay_CalculatesZReportCountersAndCloses()
    {
        using var db = CreateInMemoryDbContext(Guid.NewGuid().ToString());
        db.DeviceConfigs.Add(new DeviceConfigEntity
        {
            DeviceId = 40,
            FiscalDayStatus = "FiscalDayOpened",
            CurrentFiscalDayNo = 12,
            EncryptedPrivateKeyPem = "ENC_KEY"
        });

        // Add today's fiscalized documents
        db.FiscalDocuments.Add(new FiscalDocumentEntity
        {
            InvoiceNo = "INV-001",
            DeviceId = 40,
            FiscalDayNo = 12,
            Currency = "USD",
            TotalAmount = 100.00m,
            FiscalStatus = "Fiscalized"
        });
        db.FiscalDocuments.Add(new FiscalDocumentEntity
        {
            InvoiceNo = "INV-002",
            DeviceId = 40,
            FiscalDayNo = 12,
            Currency = "ZIG",
            TotalAmount = 500.00m,
            FiscalStatus = "Fiscalized"
        });
        await db.SaveChangesAsync();

        _mockKeyStore.Setup(k => k.DecryptKey(It.IsAny<string>(), It.IsAny<string>())).Returns("DEC_KEY");
        _mockCryptoService.Setup(c => c.ComputeFiscalDayHash(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<List<FiscalDayCounterDto>>()))
            .Returns("DAY_HASH");
        _mockCryptoService.Setup(c => c.SignFiscalDay(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(new SignatureDataDto { Hash = "DAY_HASH", Signature = "DAY_SIG" });

        _mockApiClient.Setup(a => a.CloseDayAsync(It.IsAny<CloseDayRequest>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CloseDayResponse { OperationID = "OP456" });

        var manager = new FdmsDayManager(db, _mockApiClient.Object, _mockCryptoService.Object, _mockKeyStore.Object, _mockZReportService.Object, _config, _mockLogger.Object);

        var result = await manager.CloseFiscalDayAsync(40);

        Assert.Equal("FiscalDayClosed", result.FiscalDayStatus);

        // Verify Z-Report counters submitted contain USD 100 and ZIG 500
        _mockApiClient.Verify(a => a.CloseDayAsync(
            It.Is<CloseDayRequest>(req => req.FiscalDayCounters.Any(c => c.FiscalCounterCurrency == "USD" && c.FiscalCounterValue == 100.00m) &&
                                         req.FiscalDayCounters.Any(c => c.FiscalCounterCurrency == "ZIG" && c.FiscalCounterValue == 500.00m)),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CloseFiscalDayAsync_MultipleDevices_OnlySummarizesTargetDeviceDocuments()
    {
        using var db = CreateInMemoryDbContext(Guid.NewGuid().ToString());
        db.DeviceConfigs.Add(new DeviceConfigEntity
        {
            DeviceId = 50,
            FiscalDayStatus = "FiscalDayOpened",
            CurrentFiscalDayNo = 5,
            EncryptedPrivateKeyPem = "ENC_KEY"
        });
        db.DeviceConfigs.Add(new DeviceConfigEntity
        {
            DeviceId = 51,
            FiscalDayStatus = "FiscalDayOpened",
            CurrentFiscalDayNo = 5,
            EncryptedPrivateKeyPem = "ENC_KEY"
        });

        // Add documents for Device 50 and Device 51 with same FiscalDayNo = 5
        db.FiscalDocuments.Add(new FiscalDocumentEntity
        {
            InvoiceNo = "INV-50-1",
            DeviceId = 50,
            FiscalDayNo = 5,
            Currency = "USD",
            TotalAmount = 200.00m,
            FiscalStatus = "Fiscalized"
        });
        db.FiscalDocuments.Add(new FiscalDocumentEntity
        {
            InvoiceNo = "INV-51-1",
            DeviceId = 51,
            FiscalDayNo = 5,
            Currency = "USD",
            TotalAmount = 800.00m,
            FiscalStatus = "Fiscalized"
        });
        await db.SaveChangesAsync();

        _mockKeyStore.Setup(k => k.DecryptKey(It.IsAny<string>(), It.IsAny<string>())).Returns("DEC_KEY");
        _mockCryptoService.Setup(c => c.ComputeFiscalDayHash(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<List<FiscalDayCounterDto>>()))
            .Returns("DAY_HASH");
        _mockCryptoService.Setup(c => c.SignFiscalDay(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(new SignatureDataDto { Hash = "DAY_HASH", Signature = "DAY_SIG" });

        _mockApiClient.Setup(a => a.CloseDayAsync(It.IsAny<CloseDayRequest>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CloseDayResponse { OperationID = "OP50" });

        var manager = new FdmsDayManager(db, _mockApiClient.Object, _mockCryptoService.Object, _mockKeyStore.Object, _mockZReportService.Object, _config, _mockLogger.Object);

        var result = await manager.CloseFiscalDayAsync(50);

        Assert.Equal("FiscalDayClosed", result.FiscalDayStatus);

        // Verify Device 50 close ONLY counted Device 50's total (200.00m), not Device 51's (800.00m)
        _mockApiClient.Verify(a => a.CloseDayAsync(
            It.Is<CloseDayRequest>(req => req.DeviceID == 50 &&
                                         req.FiscalDayCounters.Any(c => c.FiscalCounterValue == 200.00m) &&
                                         !req.FiscalDayCounters.Any(c => c.FiscalCounterValue == 800.00m)),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CloseFiscalDayAsync_FiscalDayCloseFailedStatus_AllowsRetryClose()
    {
        using var db = CreateInMemoryDbContext(Guid.NewGuid().ToString());
        db.DeviceConfigs.Add(new DeviceConfigEntity
        {
            DeviceId = 60,
            FiscalDayStatus = "FiscalDayCloseFailed",
            CurrentFiscalDayNo = 15,
            EncryptedPrivateKeyPem = "ENC_KEY"
        });
        await db.SaveChangesAsync();

        _mockKeyStore.Setup(k => k.DecryptKey(It.IsAny<string>(), It.IsAny<string>())).Returns("DEC_KEY");
        _mockCryptoService.Setup(c => c.ComputeFiscalDayHash(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<List<FiscalDayCounterDto>>()))
            .Returns("DAY_HASH");
        _mockCryptoService.Setup(c => c.SignFiscalDay(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(new SignatureDataDto { Hash = "DAY_HASH", Signature = "DAY_SIG" });

        _mockApiClient.Setup(a => a.CloseDayAsync(It.IsAny<CloseDayRequest>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CloseDayResponse { OperationID = "OP60" });

        var manager = new FdmsDayManager(db, _mockApiClient.Object, _mockCryptoService.Object, _mockKeyStore.Object, _mockZReportService.Object, _config, _mockLogger.Object);

        var result = await manager.CloseFiscalDayAsync(60);

        Assert.Equal("FiscalDayClosed", result.FiscalDayStatus);
    }

    [Fact]
    public async Task OpenFiscalDayAsync_FiscalDayCloseFailedStatus_ThrowsInvalidOperationException()
    {
        using var db = CreateInMemoryDbContext(Guid.NewGuid().ToString());
        db.DeviceConfigs.Add(new DeviceConfigEntity
        {
            DeviceId = 70,
            FiscalDayStatus = "FiscalDayCloseFailed",
            CurrentFiscalDayNo = 15,
            EncryptedPrivateKeyPem = "ENC_KEY"
        });
        await db.SaveChangesAsync();

        var manager = new FdmsDayManager(db, _mockApiClient.Object, _mockCryptoService.Object, _mockKeyStore.Object, _mockZReportService.Object, _config, _mockLogger.Object);

        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.OpenFiscalDayAsync(70));
    }
}
