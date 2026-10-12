using System.Reflection;
using System.Threading.RateLimiting;
using FdmsGatewayApi.Data;
using FdmsGatewayApi.Middleware;
using FdmsGatewayApi.Services.Implementation;
using FdmsGatewayApi.Services.Interfaces;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.OpenApi.Models;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace FdmsGatewayApi;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Add Controllers
        builder.Services.AddControllers();

        // Add Immutability Interceptor for Fail-Fast Security Enforcement
        builder.Services.AddSingleton<ImmutabilityInterceptor>();

        // Add EF Core Database (SQL Server, PostgreSQL via Npgsql, or SQLite with WAL mode support)
        string dbProvider = builder.Configuration["DatabaseOptions:Provider"] ?? "SqlServer";
        string connectionString = string.Equals(dbProvider, "PostgreSQL", StringComparison.OrdinalIgnoreCase) || string.Equals(dbProvider, "Postgres", StringComparison.OrdinalIgnoreCase)
            ? (builder.Configuration.GetConnectionString("PostgresDatabase") ?? builder.Configuration.GetConnectionString("FdmsDatabase") ?? "Host=localhost;Database=fdms_gateway;Username=postgres;Password=postgres")
            : (builder.Configuration.GetConnectionString("FdmsDatabase") ?? "Server=localhost;Database=FdmsGateway;Trusted_Connection=True;TrustServerCertificate=True;");

        builder.Services.AddDbContext<FdmsDbContext>((sp, options) =>
        {
            var interceptor = sp.GetRequiredService<ImmutabilityInterceptor>();
            if (string.Equals(dbProvider, "Sqlite", StringComparison.OrdinalIgnoreCase))
            {
                options.UseSqlite(connectionString);
            }
            else if (string.Equals(dbProvider, "PostgreSQL", StringComparison.OrdinalIgnoreCase) || string.Equals(dbProvider, "Postgres", StringComparison.OrdinalIgnoreCase))
            {
                options.UseNpgsql(connectionString);
            }
            else
            {
                options.UseSqlServer(connectionString);
            }
            options.AddInterceptors(interceptor);
        });

        // Configure OpenTelemetry Tracing & Metrics APM Subsystem
        string otelServiceName = builder.Configuration["OpenTelemetry:ServiceName"] ?? "FdmsGatewayApi";
        string otelServiceVersion = builder.Configuration["OpenTelemetry:ServiceVersion"] ?? "1.0.0";
        bool enablePrometheus = builder.Configuration.GetValue<bool>("OpenTelemetry:EnablePrometheus", true);
        string otlpEndpoint = builder.Configuration["OpenTelemetry:OtlpEndpoint"] ?? "http://localhost:4317";

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName: otelServiceName, serviceVersion: otelServiceVersion))
            .WithTracing(tracing =>
            {
                tracing
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation();

                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                {
                    tracing.AddOtlpExporter(options =>
                    {
                        options.Endpoint = new Uri(otlpEndpoint);
                    });
                }
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation();

                if (enablePrometheus)
                {
                    metrics.AddPrometheusExporter();
                }
            });

        // Add Enterprise Health Checks
        builder.Services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("Database")
            .AddCheck<OfflineQueueHealthCheck>("OfflineQueue")
            .AddCheck<ZimraGatewayHealthCheck>("ZimraGateway");

        // Add HttpClientFactory with pooled sockets and Polly transient error resilience policy
        builder.Services.AddHttpClient("ZimraFdmsClient", (sp, client) =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            string baseUrl = config["FdmsGateway:BaseUrl"] ?? "https://fdmsapitest.zimra.co.zw/";
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromSeconds(30);
        })
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(15),
            EnableMultipleHttp2Connections = true,
            ConnectTimeout = TimeSpan.FromSeconds(10)
        });

        builder.Services.AddHttpClient();

        // Register Application Services
        builder.Services.Configure<FdmsGatewayApi.Config.WhiteLabelOptions>(builder.Configuration.GetSection("WhiteLabel"));
        builder.Services.Configure<FdmsGatewayApi.Config.MultiTenancyOptions>(builder.Configuration.GetSection(FdmsGatewayApi.Config.MultiTenancyOptions.SectionName));
        builder.Services.AddSingleton<IBrandingService, BrandingService>();
        builder.Services.AddSingleton<IPassPhraseProvider, PassPhraseProvider>();
        builder.Services.AddSingleton<IEncryptedKeyStore, LocalEncryptedKeyStore>();
        builder.Services.AddSingleton<IFdmsCryptoService, FdmsCryptoService>();
        builder.Services.AddSingleton<IZimraHealthCheck, ZimraHealthCheckState>();
        builder.Services.AddSingleton<ICurrencySplitService, CurrencySplitService>();
        builder.Services.AddSingleton<ITaxCodeMappingService, TaxCodeMappingService>();
        builder.Services.AddSingleton<IFiscalDayScheduleService, FiscalDayScheduleService>();
        builder.Services.AddSingleton<IReportsConfigService, ReportsConfigService>();
        builder.Services.AddSingleton<IPdfReportGenerator, PdfReportGenerator>();
        builder.Services.AddSingleton<IWebhookDispatchService, WebhookDispatchService>();
        builder.Services.AddSingleton<IEscPosPrintService, EscPosPrintService>();
        builder.Services.AddSingleton<IAuditLogger, AuditLogger>();
        builder.Services.AddSingleton<IAdminAuthService, AdminAuthService>();
        builder.Services.AddSingleton<IDriveInstanceProvider, DriveInstanceProvider>();
        builder.Services.AddSingleton<IDistributedLockProvider, RedisDistributedLockProvider>();
        builder.Services.AddSingleton<IHardwareIdentityFilter, HardwareIdentityFilter>();
        builder.Services.AddSingleton<CloudKmsVerificationEngine>();

        builder.Services.AddScoped<IFdmsValidationService, FdmsValidationService>();
        builder.Services.AddScoped<IFdmsApiClient, FdmsApiClient>();
        builder.Services.AddScoped<IZReportService, ZReportService>();
        builder.Services.AddScoped<IXReportService, XReportService>();
        builder.Services.AddScoped<IFdmsDayManager, FdmsDayManager>();
        builder.Services.AddScoped<IFiscalDocumentOrchestrator, FiscalDocumentOrchestrator>();
        builder.Services.AddScoped<IHashChainAuditorService, HashChainAuditorService>();
        builder.Services.AddScoped<ISubscriptionService, SubscriptionService>();
        builder.Services.AddScoped<ITenantRegistryService, TenantRegistryService>();
        builder.Services.AddScoped<IPartnerManagementService, PartnerManagementService>();
        builder.Services.AddScoped<IFdmsOfflineFileService, FdmsOfflineFileService>();


        // Background Services
        builder.Services.AddHostedService<ZimraHealthCheckService>();
        builder.Services.AddHostedService<FiscalDayBackgroundService>();
        builder.Services.AddHostedService<OfflineQueueWorker>();
        builder.Services.AddHostedService<EmailHealthAlertNotificationService>();
        builder.Services.AddHostedService<WholesaleTokenReconciliationWorker>();
        builder.Services.AddHostedService<CertificateExpiryWorker>();

        // Tenant-Partitioned Quality of Service (QoS) Rate Limiter
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
            {
                string partitionKey = httpContext.Request.Headers["X-API-Key"].ToString();
                if (string.IsNullOrWhiteSpace(partitionKey))
                {
                    partitionKey = httpContext.Request.Headers["X-Tenant-ApiKey"].ToString();
                }
                if (string.IsNullOrWhiteSpace(partitionKey))
                {
                    partitionKey = httpContext.Request.Headers["tenantApiKey"].ToString();
                }
                if (string.IsNullOrWhiteSpace(partitionKey))
                {
                    partitionKey = httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
                }

                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: partitionKey,
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromSeconds(1),
                        QueueLimit = 20,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                    });
            });
        });

        builder.Services.AddOpenApi();
        builder.Services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "ZIMRA FDMS - ERP Ingestion API",
                Version = "v1.0",
                Description = @"### 📘 ERP Integration Documentation & Developer Guide
[**👉 Click here to view the full ERP Integrator Guide & Code Samples (/guide.html)**](/guide.html)

Official public API documentation for ERP Integrators (Sage, SAP, QuickBooks, Odoo, custom ERPs) to fiscalize invoices, credit/debit notes, query document verification status, and register webhooks.",
                Contact = new OpenApiContact
                {
                    Name = "FDMS Gateway Technical Support",
                    Email = "support@fdmsgateway.co.zw",
                    Url = new Uri("http://localhost:5045/guide.html")
                }
            });

            // Strictly restrict Swagger documentation to ERP Ingestion endpoints only (/api/v1/erp/*)
            c.DocInclusionPredicate((docName, apiDesc) =>
            {
                var relativePath = apiDesc.RelativePath?.ToLowerInvariant() ?? string.Empty;
                return relativePath.StartsWith("api/v1/erp");
            });

            // Security Scheme Definition for ERP Integrators
            c.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
            {
                Description = "API Key authentication header for ERP integrators. Pass key via 'X-API-Key: <your-key>' or 'Authorization: Bearer <your-key>'.",
                Name = "X-API-Key",
                In = ParameterLocation.Header,
                Type = SecuritySchemeType.ApiKey,
                Scheme = "ApiKey"
            });

            c.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme,
                            Id = "ApiKey"
                        }
                    },
                    Array.Empty<string>()
                }
            });

            // Include XML comments if generated
            var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
            var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
            if (File.Exists(xmlPath))
            {
                c.IncludeXmlComments(xmlPath);
            }
        });

        var app = builder.Build();

#if DEBUG
        // =========================================================================
        // DEV / SANDBOX MODE: Bypasses hardware identity probes during debug builds
        // =========================================================================
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"[DEBUG WARNING] [{DateTime.UtcNow:yyyy-MM-dd HH:mm:ssZ}] FdmsGateway operating in local development playground. Hardware validation checks soft-bypassed.");
        Console.ResetColor();
#else
        // =========================================================================
        // PRODUCTION APPLIANCE MODE: Hardcoded inside release binary execution path
        // =========================================================================
        Console.WriteLine($"[SECURITY INFO] [{DateTime.UtcNow:yyyy-MM-dd HH:mm:ssZ}] Bootstrapping Hardware Appliance Identity Protection Subsystem...");
        
        using (var scope = app.Services.CreateScope())
        {
            var identityFilter = scope.ServiceProvider.GetRequiredService<IHardwareIdentityFilter>();
            var kmsEngine = scope.ServiceProvider.GetRequiredService<CloudKmsVerificationEngine>();

            try
            {
                string? cloudToken = Environment.GetEnvironmentVariable("CLOUD_ENV_VALIDATION_TOKEN");

                if (string.IsNullOrWhiteSpace(cloudToken))
                {
                    // Topology A: On-Premises USB Appliance Drive Verification
                    identityFilter.VerifyCarrierMediaIntegrity();
                }
                else
                {
                    // Topology B: Cloud-Native Micro-Cluster Verification
                    kmsEngine.VerifyCloudNodeAuthenticity(cloudToken);
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine($"[CRITICAL INFRASTRUCTURE FAILURE] [{DateTime.UtcNow:yyyy-MM-dd HH:mm:ssZ}] Environment validation halted: {ex.Message}");
                Console.ResetColor();
                Environment.Exit(403);
            }
        }
#endif

        // Hybrid Database Setup (AutoMigrateOnStartup vs DBA manual script mode)
        bool autoMigrate = app.Configuration.GetValue<bool>("DatabaseOptions:AutoMigrateOnStartup", false);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FdmsDbContext>();
            var subService = scope.ServiceProvider.GetRequiredService<ISubscriptionService>();
            var authService = scope.ServiceProvider.GetRequiredService<IAdminAuthService>();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
            
            try
            {
                if (db.Database.IsSqlite())
                {
                    logger.LogInformation("SQLite database provider detected. Setting PRAGMA journal_mode=WAL and PRAGMA synchronous=NORMAL for zero-loss transactional durability...");
                    db.Database.EnsureCreated();
                    db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;");
                }
                else
                {
                    logger.LogInformation("Ensuring database schema creation via db.Database.EnsureCreated()...");
                    db.Database.EnsureCreated();
                }

                if (!db.Database.IsSqlite())
                {
                    try
                    {
                        db.Database.ExecuteSqlRaw(@"
                            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'SharedPartnerProfiles')
                            BEGIN
                                CREATE TABLE [SharedPartnerProfiles] (
                                    [PartnerId] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                                    [PartnerKey] NVARCHAR(100) NOT NULL,
                                    [WholesaleTokenBalance] DECIMAL(18,2) NOT NULL DEFAULT 0,
                                    [LastReconciledAt] DATETIME2 NULL,
                                    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE()
                                );
                            END");
                    }
                    catch (Exception ddlEx) { logger.LogWarning(ddlEx, "Execution of SharedPartnerProfiles DDL failed."); }

                    try
                    {
                        db.Database.ExecuteSqlRaw(@"
                            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'TenantBusinessAccounts')
                            BEGIN
                                CREATE TABLE [TenantBusinessAccounts] (
                                    [TenantId] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                                    [PartnerId] INT NOT NULL,
                                    [TenantApiKey] NVARCHAR(100) NOT NULL,
                                    [BusinessName] NVARCHAR(200) NOT NULL,
                                    [IsActive] BIT NOT NULL DEFAULT 1,
                                    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                                    CONSTRAINT [FK_TenantBusinessAccounts_SharedPartnerProfiles] FOREIGN KEY ([PartnerId]) REFERENCES [SharedPartnerProfiles] ([PartnerId]) ON DELETE CASCADE
                                );
                            END");
                    }
                    catch (Exception ddlEx) { logger.LogWarning(ddlEx, "Execution of TenantBusinessAccounts DDL failed."); }

                    try
                    {
                        db.Database.ExecuteSqlRaw(@"
                            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'TenantUsers')
                            BEGIN
                                CREATE TABLE [TenantUsers] (
                                    [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                                    [TenantId] INT NOT NULL,
                                    [Email] NVARCHAR(150) NOT NULL,
                                    [PasswordHash] NVARCHAR(256) NOT NULL,
                                    [PasswordSalt] NVARCHAR(100) NOT NULL,
                                    [FullName] NVARCHAR(150) NOT NULL,
                                    [Role] NVARCHAR(50) NOT NULL,
                                    [Permissions] NVARCHAR(500) NOT NULL,
                                    [IsActive] BIT NOT NULL DEFAULT 1,
                                    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                                    [LastLoginAt] DATETIME2 NULL,
                                    CONSTRAINT [FK_TenantUsers_TenantBusinessAccounts] FOREIGN KEY ([TenantId]) REFERENCES [TenantBusinessAccounts] ([TenantId]) ON DELETE CASCADE
                                );
                            END");
                    }
                    catch (Exception ddlEx) { logger.LogWarning(ddlEx, "Execution of TenantUsers DDL failed."); }

                    try
                    {
                        db.Database.ExecuteSqlRaw(@"
                            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'TenantZimraDevices')
                            BEGIN
                                CREATE TABLE [TenantZimraDevices] (
                                    [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                                    [TenantId] INT NOT NULL,
                                    [ZimraDeviceId] NVARCHAR(50) NOT NULL,
                                    [EncryptedPfxCertificate] VARBINARY(MAX) NOT NULL,
                                    [CertificateDecryptionSalt] NVARCHAR(100) NOT NULL,
                                    [CertificatePassword] NVARCHAR(100) NULL,
                                    [LastSequenceNumber] BIGINT NOT NULL DEFAULT 0,
                                    [IsActive] BIT NOT NULL DEFAULT 1,
                                    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                                    CONSTRAINT [FK_TenantZimraDevices_TenantBusinessAccounts] FOREIGN KEY ([TenantId]) REFERENCES [TenantBusinessAccounts] ([TenantId]) ON DELETE CASCADE
                                );
                            END");
                    }
                    catch (Exception ddlEx) { logger.LogWarning(ddlEx, "Execution of TenantZimraDevices DDL failed."); }

                    try
                    {
                        db.Database.ExecuteSqlRaw(@"
                            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'MultiTenantFiscalLedger')
                            BEGIN
                                CREATE TABLE [MultiTenantFiscalLedger] (
                                    [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                                    [TenantId] INT NOT NULL,
                                    [ZimraDeviceId] NVARCHAR(50) NOT NULL,
                                    [OperationType] NVARCHAR(50) NOT NULL,
                                    [ReceiptGlobalNo] BIGINT NOT NULL DEFAULT 0,
                                    [TokenBurnCost] DECIMAL(18,2) NOT NULL DEFAULT 0,
                                    [PreviousBalance] DECIMAL(18,2) NOT NULL DEFAULT 0,
                                    [NewBalance] DECIMAL(18,2) NOT NULL DEFAULT 0,
                                    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                                    CONSTRAINT [FK_MultiTenantFiscalLedger_TenantBusinessAccounts] FOREIGN KEY ([TenantId]) REFERENCES [TenantBusinessAccounts] ([TenantId]) ON DELETE CASCADE
                                );
                            END");
                    }
                    catch (Exception ddlEx) { logger.LogWarning(ddlEx, "Execution of MultiTenantFiscalLedger DDL failed."); }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Migration/Schema check warning during startup. Continuing with service seeders...");
            }

            try
            {
                // Ensure initial default subscription seed key exists
                subService.EnsureSeedSubscriptionAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed seeding subscription status during startup.");
            }

            try
            {
                // Ensure initial default PIN (0000) exists
                authService.EnsureSeedPinAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed seeding admin PIN during startup.");
            }

            try
            {
                // Ensure default fiscal device (DeviceId = 1) exists for development/partner integration
                if (!db.DeviceConfigs.Any())
                {
                    logger.LogInformation("Seeding default Fiscal Device ID = 1 with cryptographic keys for local development / partner testing...");
                    var cryptoService = new FdmsCryptoService();
                    var passPhraseProvider = scope.ServiceProvider.GetRequiredService<IPassPhraseProvider>();
                    var keyStore = scope.ServiceProvider.GetRequiredService<IEncryptedKeyStore>();

                    var (privKeyPem, csrPem) = cryptoService.GenerateKeyPairAndCsr(1, "DEV-FDMS-2026-001", "ECDSA");
                    string encryptedPrivKey = keyStore.EncryptKey(privKeyPem, passPhraseProvider.GetPassPhrase());

                    db.DeviceConfigs.Add(new Models.Entities.DeviceConfigEntity
                    {
                        DeviceId = 1,
                        TaxPayerName = "M&J SERPA ERP Consultancy",
                        TaxPayerTIN = "2000123456",
                        VatNumber = "10001234",
                        DeviceSerialNo = "DEV-FDMS-2026-001",
                        DeviceBranchName = "Harare Main Branch",
                        ActivationKey = "DEV-KEY-123456",
                        EncryptedPrivateKeyPem = encryptedPrivKey,
                        CertificatePem = csrPem,
                        QrUrl = "https://invoice.zimra.co.zw",
                        FiscalDayStatus = "FiscalDayClosed",
                        CurrentFiscalDayNo = 0,
                        CurrentReceiptCounter = 0,
                        CurrentReceiptGlobalNo = 0
                    });
                    db.SaveChanges();
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed seeding default fiscal device during startup.");
            }
        }

        // Middleware Pipeline
        app.UseMiddleware<SecurityHeadersMiddleware>();
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseMiddleware<GlobalExceptionMiddleware>();
        app.UseMiddleware<ApiKeyAuthMiddleware>();
        app.UseRateLimiter();

        app.UseDefaultFiles();
        app.UseStaticFiles();

        app.UseSwagger();
        app.UseSwaggerUI(c =>
        {
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "ERP Ingestion API v1.0");
            c.RoutePrefix = "swagger";
            c.DocumentTitle = "ZIMRA FDMS ERP Ingestion API Documentation";
            c.DisplayRequestDuration();
            c.EnableDeepLinking();
            c.DefaultModelsExpandDepth(2);
            c.HeadContent = @"
                <style>
                    .swagger-ui .topbar { background-color: #0f172a !important; border-bottom: 2px solid #38bdf8; }
                    .swagger-ui .topbar-wrapper a span { font-weight: 700; color: #f8fafc !important; }
                    .guide-nav-btn {
                        background-color: #10b981;
                        color: #ffffff !important;
                        font-weight: 600;
                        padding: 6px 14px;
                        border-radius: 6px;
                        text-decoration: none !important;
                        margin-left: auto;
                        margin-right: 15px;
                        display: inline-flex;
                        align-items: center;
                        gap: 6px;
                        font-size: 13px;
                        transition: background 0.2s;
                        box-shadow: 0 2px 4px rgba(0,0,0,0.2);
                    }
                    .guide-nav-btn:hover { background-color: #059669; }
                </style>
                <script>
                    window.addEventListener('DOMContentLoaded', function() {
                        var topbar = document.querySelector('.swagger-ui .topbar-wrapper');
                        if (topbar && !document.getElementById('swagger-guide-link')) {
                            var link = document.createElement('a');
                            link.id = 'swagger-guide-link';
                            link.href = '/guide.html';
                            link.target = '_blank';
                            link.className = 'guide-nav-btn';
                            link.innerHTML = '📘 ERP Integrator Guide';
                            topbar.appendChild(link);
                        }
                    });
                </script>
            ";
        });

        app.MapOpenApi();

        app.UseHttpsRedirection();
        app.UseAuthorization();
        app.MapControllers();
        app.MapHealthChecks("/healthz");

        if (app.Configuration.GetValue<bool>("OpenTelemetry:EnablePrometheus", true))
        {
            app.MapPrometheusScrapingEndpoint();
        }

        app.Run();
    }
}

