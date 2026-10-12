using FdmsBridgeManager.Data;
using FdmsBridgeManager.Services.Implementation;
using FdmsBridgeManager.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add Controllers and API services
builder.Services.AddControllers();
builder.Services.AddHttpClient();

// Configure Database (SQL Server or SQLite with WAL mode)
string connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? $"Data Source={Path.Combine(builder.Environment.ContentRootPath, "BridgeManager.db")}";
string dbProvider = builder.Configuration["DatabaseOptions:Provider"] ?? "Sqlite";

builder.Services.AddDbContext<BridgeManagerDbContext>(options =>
{
    if (string.Equals(dbProvider, "SqlServer", StringComparison.OrdinalIgnoreCase))
    {
        options.UseSqlServer(connectionString);
    }
    else
    {
        options.UseSqlite(connectionString.Contains("Data Source") ? connectionString : $"Data Source={Path.Combine(builder.Environment.ContentRootPath, "BridgeManager.db")}");
    }
});

// Register Application Services
builder.Services.AddScoped<IWhiteLabelService, WhiteLabelService>();
builder.Services.AddScoped<IPlanService, PlanService>();
builder.Services.AddScoped<IClientService, ClientService>();
builder.Services.AddScoped<ISubscriptionKeyGenerator, SubscriptionKeyGenerator>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IPayNowService, PayNowService>();
builder.Services.AddScoped<IUserManagementService, UserManagementService>();
builder.Services.AddScoped<IImmutableTokenLedgerService, ImmutableTokenLedgerService>();
builder.Services.AddScoped<ITokenPurchaseService, TokenPurchaseService>();

// Register Phone Back Home 24H Telemetry Background Service
builder.Services.AddSingleton<PhoneHomeBackgroundService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<PhoneHomeBackgroundService>());

// Configure Production Hardened CORS
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

var app = builder.Build();

// Configure Middleware Pipeline
app.UseCors("AllowAll");

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthorization();
app.MapControllers();
app.MapFallbackToFile("index.html");

// Auto-migrate and Seed Initial Database State
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<BridgeManagerDbContext>();
    await dbContext.SeedInitialDataAsync();

}

app.Run("http://0.0.0.0:5050");
