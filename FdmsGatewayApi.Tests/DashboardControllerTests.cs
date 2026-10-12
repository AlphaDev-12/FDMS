using System.Text.Json;
using FdmsGatewayApi.Controllers;
using FdmsGatewayApi.Data;
using FdmsGatewayApi.Models.Entities;
using FdmsGatewayApi.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FdmsGatewayApi.Tests;

public class DashboardControllerTests
{
    private FdmsDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<FdmsDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new FdmsDbContext(options);
    }

    [Fact]
    public async Task GetDashboardSummary_CategorizesRetryingItemsAsFailedRetrying()
    {
        // Arrange
        using var db = CreateInMemoryDbContext();

        // 1. Fresh item waiting for 1st attempt (Retry #0)
        db.OfflineQueue.Add(new OfflineQueueEntity
        {
            InvoiceNo = "INV-001",
            Status = "Queued",
            RetryCount = 0,
            ErrorMessage = null,
            QueuedAt = DateTime.UtcNow
        });

        // 2. Retrying item (Retry #1, with last error)
        db.OfflineQueue.Add(new OfflineQueueEntity
        {
            InvoiceNo = "INV-002",
            Status = "Queued",
            RetryCount = 1,
            ErrorMessage = "FDMS Gateway returned status NotFound:",
            QueuedAt = DateTime.UtcNow
        });

        // 3. Failed/DeadLetter item
        db.OfflineQueue.Add(new OfflineQueueEntity
        {
            InvoiceNo = "INV-003",
            Status = "DeadLetter",
            RetryCount = 10,
            ErrorMessage = "Max retries exceeded",
            QueuedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();

        var healthMock = new Mock<IZimraHealthCheck>();
        var cryptoMock = new Mock<IFdmsCryptoService>();
        var auditMock = new Mock<IAuditLogger>();
        var brandingMock = new Mock<IBrandingService>();

        var controller = new DashboardController(
            db, healthMock.Object, cryptoMock.Object, auditMock.Object, brandingMock.Object, NullLogger<DashboardController>.Instance);

        // Act
        var result = await controller.GetDashboardSummary();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(okResult.Value);
        using var doc = JsonDocument.Parse(json);
        var queue = doc.RootElement.GetProperty("queue");

        Assert.Equal(1, queue.GetProperty("queued").GetInt32());         // Fresh Retry #0 item
        Assert.Equal(2, queue.GetProperty("failed").GetInt32());         // Retrying Retry #1 + DeadLetter item
        Assert.Equal(3, queue.GetProperty("totalPending").GetInt32());   // Combined queue depth
    }
}
