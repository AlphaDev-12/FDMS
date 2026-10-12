using FdmsTestIntegration.Models;
using Microsoft.EntityFrameworkCore;

namespace FdmsTestIntegration.Database;

public class MockErpDbContext : DbContext
{
    public DbSet<ErpDocument> Documents { get; set; } = null!;
    public DbSet<ErpLineItem> LineItems { get; set; } = null!;

    public string DbPath { get; }

    public MockErpDbContext(string dbPath = "mock_erp.db")
    {
        DbPath = dbPath;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        options.UseSqlite($"Data Source={DbPath}");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ErpDocument>()
            .HasKey(d => d.Id);

        modelBuilder.Entity<ErpDocument>()
            .HasIndex(d => d.InvoiceNo)
            .IsUnique();

        modelBuilder.Entity<ErpLineItem>()
            .HasOne(l => l.ErpDocument)
            .WithMany(d => d.Lines)
            .HasForeignKey(l => l.ErpDocumentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
