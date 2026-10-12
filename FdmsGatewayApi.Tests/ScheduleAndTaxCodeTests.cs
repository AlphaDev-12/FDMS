using System.Text.Json;
using FdmsGatewayApi.Controllers;
using FdmsGatewayApi.Data;
using FdmsGatewayApi.Services.Implementation;
using FdmsGatewayApi.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FdmsGatewayApi.Tests;

public class ScheduleAndTaxCodeTests
{
    private FdmsDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<FdmsDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new FdmsDbContext(options);
    }

    [Fact]
    public async Task ScheduleService_ReadsAndWritesScheduleCorrectly()
    {
        // Arrange
        var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempPath);

        try
        {
            var envMock = new Mock<IHostEnvironment>();
            envMock.Setup(e => e.ContentRootPath).Returns(tempPath);

            var service = new FiscalDayScheduleService(NullLogger<FiscalDayScheduleService>.Instance, envMock.Object);

            // Initial load (default)
            var initial = service.GetSchedule();
            Assert.False(initial.Enabled);
            Assert.Equal("06:00", initial.AutoOpenTimeCAT);

            // Save new schedule
            var newSchedule = new ScheduleDetails
            {
                Enabled = true,
                AutoOpenTimeCAT = "07:30",
                AutoCloseTimeCAT = "21:00",
                TaxPayerDayMaxHrs = 20,
                TimeZone = "Africa/Harare"
            };

            await service.SaveScheduleAsync(newSchedule);

            // Re-read
            var reloaded = service.GetSchedule();
            Assert.True(reloaded.Enabled);
            Assert.Equal("07:30", reloaded.AutoOpenTimeCAT);
            Assert.Equal("21:00", reloaded.AutoCloseTimeCAT);
            Assert.Equal(20, reloaded.TaxPayerDayMaxHrs);
        }
        finally
        {
            if (Directory.Exists(tempPath))
            {
                Directory.Delete(tempPath, true);
            }
        }
    }

    [Fact]
    public async Task TaxCodeMappingService_ReadsAndWritesMappingsCorrectly()
    {
        // Arrange
        var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempPath);

        try
        {
            var envMock = new Mock<IHostEnvironment>();
            envMock.Setup(e => e.ContentRootPath).Returns(tempPath);

            var service = new TaxCodeMappingService(NullLogger<TaxCodeMappingService>.Instance, envMock.Object);

            var mappings = service.GetTaxMappings();
            Assert.NotEmpty(mappings);

            var newMappings = new List<TaxMappingConfigItem>
            {
                new() { ErpTaxCode = "CUSTOM_15", ZimraTaxCode = "A", TaxID = 1, TaxPercent = 15.00m, Description = "Custom Standard VAT" },
                new() { ErpTaxCode = "ZERO_TAX", ZimraTaxCode = "B", TaxID = 2, TaxPercent = 0.00m, Description = "Zero rated" }
            };

            await service.SaveTaxMappingsAsync(newMappings);

            var reloaded = service.GetTaxMappings();
            Assert.Equal(2, reloaded.Count);
            Assert.Equal("CUSTOM_15", reloaded[0].ErpTaxCode);

            var mappedResult = service.MapErpTaxCode("CUSTOM_15");
            Assert.Equal("A", mappedResult.ZimraTaxCode);
            Assert.Equal(1, mappedResult.TaxID);
        }
        finally
        {
            if (Directory.Exists(tempPath))
            {
                Directory.Delete(tempPath, true);
            }
        }
    }

    [Fact]
    public async Task DeviceAdminController_ScheduleAndTaxCodeEndpointsWork()
    {
        // Arrange
        using var db = CreateInMemoryDbContext();
        var cryptoMock = new Mock<IFdmsCryptoService>();
        var keyStoreMock = new Mock<IEncryptedKeyStore>();
        var apiMock = new Mock<IFdmsApiClient>();
        var dayManagerMock = new Mock<IFdmsDayManager>();
        var scheduleMock = new Mock<IFiscalDayScheduleService>();
        var taxCodeMock = new Mock<ITaxCodeMappingService>();
        var auditMock = new Mock<IAuditLogger>();
        var subscriptionMock = new Mock<ISubscriptionService>();
        var configMock = new Mock<IConfiguration>();

        scheduleMock.Setup(s => s.GetSchedule()).Returns(new ScheduleDetails { Enabled = true, AutoOpenTimeCAT = "08:00" });
        taxCodeMock.Setup(t => t.GetTaxMappings()).Returns(new List<TaxMappingConfigItem>
        {
            new() { ErpTaxCode = "VAT15", ZimraTaxCode = "A", TaxID = 1, TaxPercent = 15.00m }
        });

        var controller = new DeviceAdminController(
            db,
            cryptoMock.Object,
            keyStoreMock.Object,
            apiMock.Object,
            dayManagerMock.Object,
            scheduleMock.Object,
            taxCodeMock.Object,
            auditMock.Object,
            subscriptionMock.Object,
            configMock.Object,
            NullLogger<DeviceAdminController>.Instance);

        // Act & Assert - Get Schedule
        var getSchedResult = controller.GetSchedule();
        var okSched = Assert.IsType<OkObjectResult>(getSchedResult);
        var schedDetails = Assert.IsType<ScheduleDetails>(okSched.Value);
        Assert.Equal("08:00", schedDetails.AutoOpenTimeCAT);

        // Act & Assert - Update Schedule
        var newSched = new ScheduleDetails { Enabled = false, AutoOpenTimeCAT = "09:00" };
        var postSchedResult = await controller.UpdateSchedule(newSched);
        Assert.IsType<OkObjectResult>(postSchedResult);
        scheduleMock.Verify(s => s.SaveScheduleAsync(newSched, It.IsAny<CancellationToken>()), Times.Once);

        // Act & Assert - Get Tax Codes
        var getTaxResult = controller.GetTaxCodes();
        var okTax = Assert.IsType<OkObjectResult>(getTaxResult);
        var taxList = Assert.IsType<List<TaxMappingConfigItem>>(okTax.Value);
        Assert.Single(taxList);

        // Act & Assert - Update Tax Codes
        var postTaxResult = await controller.UpdateTaxCodes(taxList);
        Assert.IsType<OkObjectResult>(postTaxResult);
        taxCodeMock.Verify(t => t.SaveTaxMappingsAsync(taxList, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeviceAdminController_OpenDayAndCloseDay_AllDevices_OpensAndClosesAllRegisteredDevices()
    {
        // Arrange
        using var db = CreateInMemoryDbContext();
        db.DeviceConfigs.Add(new FdmsGatewayApi.Models.Entities.DeviceConfigEntity
        {
            DeviceId = 101,
            FiscalDayStatus = "FiscalDayClosed",
            CurrentFiscalDayNo = 1
        });
        db.DeviceConfigs.Add(new FdmsGatewayApi.Models.Entities.DeviceConfigEntity
        {
            DeviceId = 102,
            FiscalDayStatus = "FiscalDayClosed",
            CurrentFiscalDayNo = 5
        });
        await db.SaveChangesAsync();

        var cryptoMock = new Mock<IFdmsCryptoService>();
        var keyStoreMock = new Mock<IEncryptedKeyStore>();
        var apiMock = new Mock<IFdmsApiClient>();
        var dayManagerMock = new Mock<IFdmsDayManager>();
        var scheduleMock = new Mock<IFiscalDayScheduleService>();
        var taxCodeMock = new Mock<ITaxCodeMappingService>();
        var auditMock = new Mock<IAuditLogger>();
        var subscriptionMock = new Mock<ISubscriptionService>();
        var configMock = new Mock<IConfiguration>();

        dayManagerMock.Setup(m => m.OpenFiscalDayAsync(101, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FdmsGatewayApi.Models.Entities.DeviceConfigEntity { DeviceId = 101, CurrentFiscalDayNo = 2, FiscalDayStatus = "FiscalDayOpened" });
        dayManagerMock.Setup(m => m.OpenFiscalDayAsync(102, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FdmsGatewayApi.Models.Entities.DeviceConfigEntity { DeviceId = 102, CurrentFiscalDayNo = 6, FiscalDayStatus = "FiscalDayOpened" });

        var controller = new DeviceAdminController(
            db,
            cryptoMock.Object,
            keyStoreMock.Object,
            apiMock.Object,
            dayManagerMock.Object,
            scheduleMock.Object,
            taxCodeMock.Object,
            auditMock.Object,
            subscriptionMock.Object,
            configMock.Object,
            NullLogger<DeviceAdminController>.Instance);

        // Act - Open fiscal day for ALL devices (deviceId = null)
        var openAllResult = await controller.OpenDay(deviceId: null);
        var okOpenAll = Assert.IsType<OkObjectResult>(openAllResult);
        
        dayManagerMock.Verify(m => m.OpenFiscalDayAsync(101, It.IsAny<CancellationToken>()), Times.Once);
        dayManagerMock.Verify(m => m.OpenFiscalDayAsync(102, It.IsAny<CancellationToken>()), Times.Once);

        // Setup Close Day
        var dev101 = await db.DeviceConfigs.FindAsync(101);
        var dev102 = await db.DeviceConfigs.FindAsync(102);
        dev101!.FiscalDayStatus = "FiscalDayOpened";
        dev102!.FiscalDayStatus = "FiscalDayOpened";
        await db.SaveChangesAsync();

        dayManagerMock.Setup(m => m.CloseFiscalDayAsync(101, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FdmsGatewayApi.Models.Entities.DeviceConfigEntity { DeviceId = 101, FiscalDayStatus = "FiscalDayClosed" });
        dayManagerMock.Setup(m => m.CloseFiscalDayAsync(102, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FdmsGatewayApi.Models.Entities.DeviceConfigEntity { DeviceId = 102, FiscalDayStatus = "FiscalDayClosed" });

        // Act - Close fiscal day for ALL devices (deviceId = null)
        var closeAllResult = await controller.CloseDay(deviceId: null);
        var okCloseAll = Assert.IsType<OkObjectResult>(closeAllResult);

        dayManagerMock.Verify(m => m.CloseFiscalDayAsync(101, It.IsAny<CancellationToken>()), Times.Once);
        dayManagerMock.Verify(m => m.CloseFiscalDayAsync(102, It.IsAny<CancellationToken>()), Times.Once);
    }
}
