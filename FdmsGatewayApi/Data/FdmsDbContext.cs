using FdmsGatewayApi.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace FdmsGatewayApi.Data;

public class FdmsDbContext : DbContext
{
    public FdmsDbContext(DbContextOptions<FdmsDbContext> options) : base(options)
    {
    }

    public DbSet<DeviceConfigEntity> DeviceConfigs => Set<DeviceConfigEntity>();
    public DbSet<FiscalDocumentEntity> FiscalDocuments => Set<FiscalDocumentEntity>();
    public DbSet<WebhookRegistrationEntity> WebhookRegistrations => Set<WebhookRegistrationEntity>();
    public DbSet<OfflineQueueEntity> OfflineQueue => Set<OfflineQueueEntity>();
    public DbSet<AuditLogEntity> AuditLogs => Set<AuditLogEntity>();
    public DbSet<SubscriptionEntity> Subscriptions => Set<SubscriptionEntity>();
    public DbSet<AdminPinEntity> AdminPins => Set<AdminPinEntity>();
    public DbSet<ZReportEntity> ZReports => Set<ZReportEntity>();
    public DbSet<UsedUpgradeNonceEntity> UsedUpgradeNonces => Set<UsedUpgradeNonceEntity>();
    public DbSet<SharedPartnerProfileEntity> SharedPartnerProfiles => Set<SharedPartnerProfileEntity>();
    public DbSet<TenantBusinessAccountEntity> TenantBusinessAccounts => Set<TenantBusinessAccountEntity>();
    public DbSet<TenantZimraDeviceEntity> TenantZimraDevices => Set<TenantZimraDeviceEntity>();
    public DbSet<MultiTenantFiscalLedgerEntity> MultiTenantFiscalLedger => Set<MultiTenantFiscalLedgerEntity>();
    public DbSet<OfflineFileEntity> OfflineFiles => Set<OfflineFileEntity>();
    public DbSet<TenantUserEntity> TenantUsers => Set<TenantUserEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<OfflineFileEntity>()
            .HasIndex(o => new { o.DeviceId, o.OperationId });


        modelBuilder.Entity<ZReportEntity>()
            .HasIndex(z => new { z.DeviceId, z.FiscalDayNo });

        modelBuilder.Entity<SubscriptionEntity>()
            .HasIndex(s => s.IsActive);

        modelBuilder.Entity<FiscalDocumentEntity>()
            .HasIndex(f => f.InvoiceNo)
            .IsUnique();

        modelBuilder.Entity<FiscalDocumentEntity>()
            .HasIndex(f => f.FiscalStatus);

        modelBuilder.Entity<OfflineQueueEntity>()
            .HasIndex(q => new { q.DeviceId, q.Status, q.ReceiptGlobalNo });

        modelBuilder.Entity<AuditLogEntity>()
            .HasIndex(a => a.Timestamp);

        modelBuilder.Entity<AuditLogEntity>()
            .HasIndex(a => a.InvoiceNo);

        // Multi-Tenant Indexes & Boundary Constraints
        modelBuilder.Entity<TenantZimraDeviceEntity>()
            .HasIndex(d => new { d.TenantId, d.ZimraDeviceId })
            .HasDatabaseName("idx_tenant_routing_lookup")
            .IsUnique();

        modelBuilder.Entity<MultiTenantFiscalLedgerEntity>()
            .HasIndex(l => new { l.TenantId, l.ZimraDeviceId, l.CreatedAt })
            .HasDatabaseName("idx_ledger_tenant_isolation");

        modelBuilder.Entity<TenantUserEntity>()
            .HasIndex(u => new { u.TenantId, u.Email })
            .HasDatabaseName("idx_tenant_user_email")
            .IsUnique();
    }
}
