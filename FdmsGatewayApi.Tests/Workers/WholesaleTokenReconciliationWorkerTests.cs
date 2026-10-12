using FdmsGatewayApi.Data;
using FdmsGatewayApi.Models.Entities;
using FdmsGatewayApi.Services.Implementation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FdmsGatewayApi.Tests.Workers;

public class WholesaleTokenReconciliationWorkerTests
{
    private (IServiceProvider serviceProvider, DbContextOptions<FdmsDbContext> dbOptions) CreateServiceProvider()
    {
        string dbName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();

        services.AddDbContext<FdmsDbContext>(options =>
            options.UseInMemoryDatabase(dbName));

        var provider = services.BuildServiceProvider();
        var options = new DbContextOptionsBuilder<FdmsDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        return (provider, options);
    }

    [Fact]
    public async Task ReconcilePartnerTokenWalletsAsync_HourlyProRata_DeductsFractionalDeviceTokens()
    {
        // Arrange
        var (provider, options) = CreateServiceProvider();
        var lastReconciled = DateTime.UtcNow.AddHours(-24); // Exactly 24 hours ago (1 day)

        using (var db = new FdmsDbContext(options))
        {
            var partner = new SharedPartnerProfileEntity
            {
                PartnerId = 1,
                PartnerKey = "partner_prorata",
                WholesaleTokenBalance = 100.0m,
                LastReconciledAt = lastReconciled
            };
            db.SharedPartnerProfiles.Add(partner);

            var tenant = new TenantBusinessAccountEntity
            {
                TenantId = 10,
                PartnerId = 1,
                TenantApiKey = "tkey_1",
                BusinessName = "Harare Pharmacy",
                IsActive = true
            };
            db.TenantBusinessAccounts.Add(tenant);

            // Add 30 active devices => Should burn 1 full token per 24 hours (30 devices / 30 days)
            for (int i = 1; i <= 30; i++)
            {
                db.TenantZimraDevices.Add(new TenantZimraDeviceEntity
                {
                    TenantId = 10,
                    ZimraDeviceId = $"DEV-{i}",
                    IsActive = true
                });
            }
            await db.SaveChangesAsync();
        }

        var worker = new WholesaleTokenReconciliationWorker(provider, NullLogger<WholesaleTokenReconciliationWorker>.Instance);

        // Act
        await worker.ReconcilePartnerTokenWalletsAsync();

        // Assert
        using (var db = new FdmsDbContext(options))
        {
            var partner = await db.SharedPartnerProfiles.FirstAsync(p => p.PartnerId == 1);
            // 30 devices over 24 hours = 30 * (24 / 720) = 1.0 token deducted
            Assert.True(Math.Abs(99.0m - partner.WholesaleTokenBalance) < 0.05m);
            Assert.NotNull(partner.LastReconciledAt);
            Assert.True((DateTime.UtcNow - partner.LastReconciledAt.Value).TotalMinutes < 5);
        }
    }

    [Fact]
    public async Task ReconcilePartnerTokenWalletsAsync_DepletedTokenBalance_SuspendsDownstreamTenants()
    {
        // Arrange
        var (provider, options) = CreateServiceProvider();

        using (var db = new FdmsDbContext(options))
        {
            var partner = new SharedPartnerProfileEntity
            {
                PartnerId = 3,
                PartnerKey = "partner_low_balance",
                WholesaleTokenBalance = 0.0m, // 0 tokens
                LastReconciledAt = DateTime.UtcNow
            };
            db.SharedPartnerProfiles.Add(partner);

            var tenant = new TenantBusinessAccountEntity
            {
                TenantId = 30,
                PartnerId = 3,
                TenantApiKey = "tkey_3",
                BusinessName = "Gweru Supermarket",
                IsActive = true
            };
            db.TenantBusinessAccounts.Add(tenant);
            db.TenantZimraDevices.Add(new TenantZimraDeviceEntity { TenantId = 30, ZimraDeviceId = "DEV-1", IsActive = true });
            await db.SaveChangesAsync();
        }

        var worker = new WholesaleTokenReconciliationWorker(provider, NullLogger<WholesaleTokenReconciliationWorker>.Instance);

        // Act
        await worker.ReconcilePartnerTokenWalletsAsync();

        // Assert
        using (var db = new FdmsDbContext(options))
        {
            var tenant = await db.TenantBusinessAccounts.FirstAsync(t => t.TenantId == 30);
            Assert.False(tenant.IsActive); // Tenant account suspended due to zero partner balance
        }
    }
}
