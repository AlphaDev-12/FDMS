using FdmsBridgeAdmin.Data;
using FdmsBridgeAdmin.Models.Entities;
using FdmsBridgeAdmin.Services.Implementation;
using FdmsBridgeAdmin.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();

string connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? "Data Source=BridgeAdmin.db";
string dbProvider = builder.Configuration["DatabaseOptions:Provider"] ?? "Sqlite";

builder.Services.AddDbContext<BridgeAdminDbContext>(options =>
{
    if (string.Equals(dbProvider, "SqlServer", StringComparison.OrdinalIgnoreCase))
    {
        options.UseSqlServer(connectionString);
    }
    else
    {
        options.UseSqlite(connectionString.Contains("Data Source") ? connectionString : "Data Source=BridgeAdmin.db");
    }
});

// Register DI Services
builder.Services.AddScoped<IConsultancyFirmService, ConsultancyFirmService>();
builder.Services.AddScoped<ITokenAdminService, TokenAdminService>();
builder.Services.AddScoped<IPayNowAdminService, PayNowAdminService>();
builder.Services.AddScoped<ITelemetryService, TelemetryService>();
builder.Services.AddScoped<IUserManagementService, UserManagementService>();

// Production Hardened CORS Policy
var allowedOrigins = builder.Configuration.GetSection("AllowedCorsOrigins").Get<string[]>()
    ?? new[] { "http://localhost:5070", "http://localhost:5050", "http://localhost:5045" };

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

builder.Services.AddOpenApi();

var app = builder.Build();

// Ensure Database Created & Seeded
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<BridgeAdminDbContext>();
    db.Database.EnsureCreated();

    db.Database.ExecuteSqlRaw(@"
        CREATE TABLE IF NOT EXISTS Users (
            Id TEXT NOT NULL PRIMARY KEY,
            Username TEXT NOT NULL,
            FullName TEXT NOT NULL,
            Email TEXT NOT NULL,
            PasswordHash TEXT NOT NULL,
            Role TEXT NOT NULL,
            PermissionsJson TEXT NOT NULL,
            IsActive INTEGER NOT NULL,
            CreatedAt TEXT NOT NULL,
            LastLoginAt TEXT NULL
        );
        CREATE UNIQUE INDEX IF NOT EXISTS IX_Users_Username ON Users(Username);
        CREATE INDEX IF NOT EXISTS IX_Users_Email ON Users(Email);
    ");

    try { db.Database.ExecuteSqlRaw("ALTER TABLE TokenVouchers ADD COLUMN TokenType TEXT NULL;"); } catch {}
    try { db.Database.ExecuteSqlRaw("UPDATE TokenVouchers SET TokenType = 'Subscription' WHERE TokenType IS NULL OR TokenType = '';"); } catch {}
    try { db.Database.ExecuteSqlRaw("ALTER TABLE ConsultancyFirms ADD COLUMN TotalSubscriptionTokensPurchased INTEGER NOT NULL DEFAULT 0;"); } catch {}
    try { db.Database.ExecuteSqlRaw("ALTER TABLE ConsultancyFirms ADD COLUMN TotalDeviceUpgradeTokensPurchased INTEGER NOT NULL DEFAULT 0;"); } catch {}
    try { db.Database.ExecuteSqlRaw("ALTER TABLE ConsultancyFirms ADD COLUMN UnlimitedTier TEXT NOT NULL DEFAULT 'None';"); } catch {}
    try { db.Database.ExecuteSqlRaw("ALTER TABLE ConsultancyFirms ADD COLUMN UnlimitedExpiresAt TEXT NULL;"); } catch {}

    // Ensure default SystemConfig for PricePerToken ($5.00 default)
    var priceConfig = db.SystemConfigs.FirstOrDefault(c => c.ConfigKey == "PricePerTokenUSD");
    if (priceConfig == null)
    {
        db.SystemConfigs.Add(new SystemConfigEntity
        {
            ConfigKey = "PricePerTokenUSD",
            ConfigValue = "5.00",
            Description = "Price per token in USD (1 token = 1 device per month)",
            UpdatedAt = DateTime.UtcNow
        });
    }

    // Seed initial default administrator user if empty
    if (!db.Users.Any())
    {
        var defaultAdmin = new UserEntity
        {
            Id = Guid.NewGuid(),
            Username = "admin",
            FullName = "Master System Administrator",
            Email = "admin@fdmsbridge.co.zw",
            PasswordHash = UserManagementService.HashPassword("Admin@2026!"),
            Role = "SuperAdmin",
            PermissionsJson = JsonSerializer.Serialize(new List<string> { "SuperAdmin", "ManageFirms", "ManageTokens", "ManageSettings", "ManageUsers" }),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.Users.Add(defaultAdmin);
    }


    db.SaveChanges();
}

app.UseCors("AllowAll");
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();
app.MapControllers();
app.MapFallbackToFile("index.html");

app.Run("http://0.0.0.0:5070");
