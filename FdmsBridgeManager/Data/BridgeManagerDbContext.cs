using FdmsBridgeManager.Models.Entities;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace FdmsBridgeManager.Data;

public class BridgeManagerDbContext : DbContext
{
    public BridgeManagerDbContext(DbContextOptions<BridgeManagerDbContext> options) : base(options)
    {
    }

    public DbSet<ClientEntity> Clients => Set<ClientEntity>();
    public DbSet<SubscriptionPlanEntity> SubscriptionPlans => Set<SubscriptionPlanEntity>();
    public DbSet<WhiteLabelConfigEntity> WhiteLabelConfigs => Set<WhiteLabelConfigEntity>();
    public DbSet<SubscriptionEntity> Subscriptions => Set<SubscriptionEntity>();
    public DbSet<PaymentTransactionEntity> PaymentTransactions => Set<PaymentTransactionEntity>();
    public DbSet<UserEntity> Users => Set<UserEntity>();
    public DbSet<EmailLogEntity> EmailLogs => Set<EmailLogEntity>();
    public DbSet<TokenLedgerEntity> TokenLedgers => Set<TokenLedgerEntity>();
    public DbSet<TokenImmutabilityLogEntity> TokenImmutabilityLogs => Set<TokenImmutabilityLogEntity>();
    public DbSet<HardwareTierConfigEntity> HardwareTierConfigs => Set<HardwareTierConfigEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ClientEntity>()
            .HasIndex(c => c.TaxPayerTIN)
            .IsUnique();

        modelBuilder.Entity<ClientEntity>()
            .HasIndex(c => c.Email);

        modelBuilder.Entity<SubscriptionPlanEntity>()
            .HasIndex(p => p.PlanCode)
            .IsUnique();

        modelBuilder.Entity<SubscriptionEntity>()
            .HasIndex(s => s.TaxPayerTIN);

        modelBuilder.Entity<SubscriptionEntity>()
            .HasIndex(s => s.IsActive);

        modelBuilder.Entity<PaymentTransactionEntity>()
            .HasIndex(t => t.PayNowReference);

        modelBuilder.Entity<UserEntity>()
            .HasIndex(u => u.Username)
            .IsUnique();
    }

    public async Task SeedInitialDataAsync(CancellationToken ct = default)
    {
        await Database.EnsureCreatedAsync(ct);

        if (Database.IsSqlite())
        {
            await Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS [TokenLedger] (
                    [Id] TEXT NOT NULL PRIMARY KEY,
                    [TransactionType] TEXT NOT NULL,
                    [TokenType] TEXT NULL,
                    [TokenAmount] INTEGER NOT NULL,
                    [BalanceAfter] INTEGER NOT NULL,
                    [ReferenceKey] TEXT NOT NULL,
                    [PreviousHash] TEXT NOT NULL,
                    [Hash] TEXT NOT NULL,
                    [CreatedAt] TEXT NOT NULL,
                    [CreatedBy] TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS [TokenImmutabilityLogs] (
                    [Id] TEXT NOT NULL PRIMARY KEY,
                    [ErrorType] TEXT NOT NULL,
                    [Details] TEXT NOT NULL,
                    [IsReportedToAdmin] INTEGER NOT NULL,
                    [ReportedToAdminAt] TEXT NOT NULL,
                    [DetectedAt] TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS [HardwareTierConfigs] (
                    [Id] TEXT NOT NULL PRIMARY KEY,
                    [TierName] TEXT NOT NULL,
                    [PricePerSlotUSD] TEXT NOT NULL,
                    [TokensPerSlot] INTEGER NOT NULL,
                    [HardCap] INTEGER NOT NULL,
                    [DefaultDevices] INTEGER NOT NULL,
                    [Description] TEXT NOT NULL,
                    [UpdatedAt] TEXT NOT NULL,
                    [UpdatedBy] TEXT NOT NULL
                );
            ", ct);

            try
            {
                await Database.ExecuteSqlRawAsync("ALTER TABLE [TokenLedger] ADD COLUMN [TokenType] TEXT NULL;", ct);
            }
            catch
            {
                // Column may already exist
            }

            try
            {
                await Database.ExecuteSqlRawAsync("ALTER TABLE [Clients] ADD COLUMN [TenantApiKey] TEXT NULL;", ct);
            }
            catch
            {
                // Column may already exist
            }

            try
            {
                await Database.ExecuteSqlRawAsync("UPDATE [Clients] SET [TenantApiKey] = '' WHERE [TenantApiKey] IS NULL;", ct);
            }
            catch
            {
                // Ignore if table/column does not exist yet
            }
        }

        // 1. Seed White Label Configuration
        if (!await WhiteLabelConfigs.AnyAsync(ct))
        {
            WhiteLabelConfigs.Add(new WhiteLabelConfigEntity
            {
                Id = Guid.NewGuid(),
                FirmName = "Apex Fiscal Consultancy",
                Tagline = "Enterprise FDMS Bridge & Compliance Suite",
                LogoUrl = "https://images.unsplash.com/photo-1560179707-f14e90ef3623?auto=format&fit=crop&w=300&q=80",
                FaviconUrl = "",
                PrimaryColor = "#2563eb",
                SecondaryColor = "#1e40af",
                SupportEmail = "support@apexconsultancy.co.zw",
                SupportPhone = "+263 77 123 4567",
                Address = "100 Sam Nujoma Street, Harare, Zimbabwe",
                EmailFooterText = "Need assistance with your FDMS Bridge deployment? Contact our technical team at support@apexconsultancy.co.zw",
                UpdatedAt = DateTime.UtcNow
            });
        }


        // 2. Seed Dynamic Subscription Plans
        if (!await SubscriptionPlans.AnyAsync(ct))
        {
            SubscriptionPlans.AddRange(new List<SubscriptionPlanEntity>
            {
                new SubscriptionPlanEntity
                {
                    Id = Guid.NewGuid(),
                    PlanCode = "PLAN_MONTHLY",
                    PlanName = "Monthly Starter Bridge",
                    BillingCycle = "Monthly",
                    Price = 50.00m,
                    Currency = "USD",
                    DurationMonths = 1,
                    MaxDevices = 1,
                    GracePeriodDays = 7,
                    Description = "$50.00 / device / month. Scalable per device, 7-day grace period, and online support.",
                    IsActive = true,
                    SortOrder = 1,
                    CreatedAt = DateTime.UtcNow
                },
                new SubscriptionPlanEntity
                {
                    Id = Guid.NewGuid(),
                    PlanCode = "PLAN_QUARTERLY",
                    PlanName = "Quarterly Professional",
                    BillingCycle = "Quarterly",
                    Price = 135.00m,
                    Currency = "USD",
                    DurationMonths = 3,
                    MaxDevices = 1,
                    GracePeriodDays = 7,
                    Description = "$135.00 / device / quarter ($45/mo/dev). Save 10%! Scalable per device with 7-day grace period.",
                    IsActive = true,
                    SortOrder = 2,
                    CreatedAt = DateTime.UtcNow
                },
                new SubscriptionPlanEntity
                {
                    Id = Guid.NewGuid(),
                    PlanCode = "PLAN_ANNUAL",
                    PlanName = "Annual Enterprise Corporate",
                    BillingCycle = "Annual",
                    Price = 480.00m,
                    Currency = "USD",
                    DurationMonths = 12,
                    MaxDevices = 1,
                    GracePeriodDays = 14,
                    Description = "$480.00 / device / year ($40/mo/dev). Save 20%! Enterprise licensing per device with priority SLA.",
                    IsActive = true,
                    SortOrder = 3,
                    CreatedAt = DateTime.UtcNow
                }
            });
        }
        else
        {
            var existingPlans = await SubscriptionPlans.ToListAsync(ct);
            foreach (var p in existingPlans)
            {
                if (p.PlanCode == "PLAN_MONTHLY")
                {
                    p.Description = "$50.00 / device / month. Scalable per device, 7-day grace period, and online support.";
                    p.MaxDevices = 1;
                }
                else if (p.PlanCode == "PLAN_QUARTERLY")
                {
                    p.Description = "$135.00 / device / quarter ($45/mo/dev). Save 10%! Scalable per device with 7-day grace period.";
                    p.MaxDevices = 1;
                }
                else if (p.PlanCode == "PLAN_ANNUAL")
                {
                    p.Description = "$480.00 / device / year ($40/mo/dev). Save 20%! Enterprise licensing per device with priority SLA.";
                    p.MaxDevices = 1;
                }
            }
        }

        // 3. Seed Default Hardware Tier Configs
        if (!await HardwareTierConfigs.AnyAsync(ct))
        {
            HardwareTierConfigs.AddRange(new List<HardwareTierConfigEntity>
            {
                new HardwareTierConfigEntity
                {
                    Id = Guid.NewGuid(),
                    TierName = "TillStick",
                    PricePerSlotUSD = 50.00m,
                    TokensPerSlot = 1,
                    HardCap = 5,
                    DefaultDevices = 1,
                    Description = "Entry-level tier with up to 5 devices capacity.",
                    UpdatedAt = DateTime.UtcNow,
                    UpdatedBy = "System"
                },
                new HardwareTierConfigEntity
                {
                    Id = Guid.NewGuid(),
                    TierName = "BranchBox",
                    PricePerSlotUSD = 150.00m,
                    TokensPerSlot = 3,
                    HardCap = 25,
                    DefaultDevices = 5,
                    Description = "Enterprise multi-register tier with up to 25 devices capacity.",
                    UpdatedAt = DateTime.UtcNow,
                    UpdatedBy = "System"
                },
                new HardwareTierConfigEntity
                {
                    Id = Guid.NewGuid(),
                    TierName = "Cloud",
                    PricePerSlotUSD = 300.00m,
                    TokensPerSlot = 5,
                    HardCap = 100,
                    DefaultDevices = 10,
                    Description = "Elastic cloud-hosted enterprise gateway cluster with up to 100 devices capacity.",
                    UpdatedAt = DateTime.UtcNow,
                    UpdatedBy = "System"
                }
            });
        }
        else
        {
            var existingTiers = await HardwareTierConfigs.ToListAsync(ct);
            if (!existingTiers.Any(t => t.TierName.Equals("Cloud", StringComparison.OrdinalIgnoreCase)))
            {
                HardwareTierConfigs.Add(new HardwareTierConfigEntity
                {
                    Id = Guid.NewGuid(),
                    TierName = "Cloud",
                    PricePerSlotUSD = 300.00m,
                    TokensPerSlot = 5,
                    HardCap = 100,
                    DefaultDevices = 10,
                    Description = "Elastic cloud-hosted enterprise gateway cluster with up to 100 devices capacity.",
                    UpdatedAt = DateTime.UtcNow,
                    UpdatedBy = "System"
                });
            }
        }

        // 4. Seed Default SuperAdmin User (Username: admin, Password: Admin@123456)
        if (!await Users.AnyAsync(ct))
        {
            string adminPasswordHash = HashPassword("Admin@123456");
            var allPermissions = new List<string>
            {
                "ManageClients",
                "ManageSubscriptions",
                "GenerateCashKeys",
                "ManagePlans",
                "ManageWhiteLabel",
                "ManageUsers"
            };

            Users.Add(new UserEntity
            {
                Id = Guid.NewGuid(),
                Username = "admin",
                FullName = "System Administrator",
                Email = "admin@apexconsultancy.co.zw",
                PasswordHash = adminPasswordHash,
                Role = "SuperAdmin",
                PermissionsJson = JsonSerializer.Serialize(allPermissions),
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
        }

        await SaveChangesAsync(ct);
    }

    private static string HashPassword(string password)
    {
        byte[] salt = new byte[] { 0x10, 0x20, 0x30, 0x40, 0x50, 0x60, 0x70, 0x80 };
        byte[] hash = System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            10000,
            System.Security.Cryptography.HashAlgorithmName.SHA256,
            32);
        return Convert.ToBase64String(hash);
    }
}
