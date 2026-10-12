using FdmsBridgeAdmin.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace FdmsBridgeAdmin.Data;

public class BridgeAdminDbContext : DbContext
{
    public BridgeAdminDbContext(DbContextOptions<BridgeAdminDbContext> options) : base(options)
    {
    }

    public DbSet<ConsultancyFirmEntity> ConsultancyFirms { get; set; } = null!;
    public DbSet<TokenVoucherEntity> TokenVouchers { get; set; } = null!;
    public DbSet<SystemConfigEntity> SystemConfigs { get; set; } = null!;
    public DbSet<TelemetryLogEntity> TelemetryLogs { get; set; } = null!;
    public DbSet<UserEntity> Users { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<UserEntity>(entity =>
        {
            entity.HasIndex(e => e.Username).IsUnique();
            entity.HasIndex(e => e.Email);
        });

        modelBuilder.Entity<ConsultancyFirmEntity>(entity =>
        {
            entity.HasIndex(e => e.FirmApiKey).IsUnique();
            entity.HasIndex(e => e.ContactEmail);
        });

        modelBuilder.Entity<TokenVoucherEntity>(entity =>
        {
            entity.HasIndex(e => e.VoucherCode).IsUnique();
            entity.HasIndex(e => e.ConsultancyFirmId);
        });

        modelBuilder.Entity<TelemetryLogEntity>(entity =>
        {
            entity.HasIndex(e => e.ConsultancyFirmId);
            entity.HasIndex(e => e.ReportedAt);
        });
    }
}
