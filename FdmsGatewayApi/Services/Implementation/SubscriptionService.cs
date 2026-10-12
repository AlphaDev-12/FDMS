using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FdmsGatewayApi.Data;
using FdmsGatewayApi.Models.Dtos;
using FdmsGatewayApi.Models.Entities;
using FdmsGatewayApi.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace FdmsGatewayApi.Services.Implementation;

public class SubscriptionService : ISubscriptionService
{
    private readonly FdmsDbContext _dbContext;
    private readonly IConfiguration _config;
    private readonly IDriveInstanceProvider _driveProvider;
    private readonly ILogger<SubscriptionService> _logger;
    private readonly string _secretKey;

    public SubscriptionService(
        FdmsDbContext dbContext,
        IConfiguration config,
        IDriveInstanceProvider driveProvider,
        ILogger<SubscriptionService> logger)
    {
        _dbContext = dbContext;
        _config = config;
        _driveProvider = driveProvider;
        _logger = logger;
        _secretKey = _config["Subscription:SecretKey"] ?? "FDMS-Bridge-Subscription-Master-Secret-2026-SecurityKey";
    }

    public async Task EnsureSeedSubscriptionAsync(CancellationToken ct = default)
    {
        try
        {
            if (_dbContext.Database.IsSqlite())
            {
                await _dbContext.Database.ExecuteSqlRawAsync(@"
                    CREATE TABLE IF NOT EXISTS [Subscriptions] (
                        [Id] TEXT NOT NULL PRIMARY KEY,
                        [ApiKey] TEXT NOT NULL,
                        [PlanName] TEXT NOT NULL,
                        [TaxPayerTIN] TEXT NOT NULL,
                        [ValidFrom] TEXT NOT NULL,
                        [ValidTo] TEXT NOT NULL,
                        [GracePeriodDays] INTEGER NOT NULL,
                        [MaxDevices] INTEGER NOT NULL,
                        [DriveInstanceId] TEXT NULL,
                        [HardwareTier] TEXT NULL,
                        [HardCap] INTEGER NOT NULL DEFAULT 5,
                        [IsActive] INTEGER NOT NULL,
                        [ActivatedAt] TEXT NOT NULL,
                        [ActivatedBy] TEXT NULL
                    );
                    CREATE INDEX IF NOT EXISTS [IX_Subscriptions_IsActive] ON [Subscriptions] ([IsActive]);
                ", ct);
            }
            else
            {
                await _dbContext.Database.ExecuteSqlRawAsync(@"
                    IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Subscriptions')
                    BEGIN
                        CREATE TABLE [Subscriptions] (
                            [Id] uniqueidentifier NOT NULL CONSTRAINT [PK_Subscriptions] PRIMARY KEY,
                            [ApiKey] nvarchar(max) NOT NULL,
                            [PlanName] nvarchar(max) NOT NULL,
                            [TaxPayerTIN] nvarchar(max) NOT NULL,
                            [ValidFrom] datetime2 NOT NULL,
                            [ValidTo] datetime2 NOT NULL,
                            [GracePeriodDays] int NOT NULL,
                            [MaxDevices] int NOT NULL,
                            [DriveInstanceId] nvarchar(max) NULL,
                            [HardwareTier] nvarchar(max) NULL,
                            [HardCap] int NOT NULL DEFAULT 5,
                            [IsActive] bit NOT NULL,
                            [ActivatedAt] datetime2 NOT NULL,
                            [ActivatedBy] nvarchar(max) NULL
                        );
                        CREATE INDEX [IX_Subscriptions_IsActive] ON [Subscriptions] ([IsActive]);
                    END
                    ELSE
                    BEGIN
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Subscriptions') AND name = 'DriveInstanceId')
                            ALTER TABLE [Subscriptions] ADD [DriveInstanceId] nvarchar(max) NULL;
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Subscriptions') AND name = 'HardwareTier')
                            ALTER TABLE [Subscriptions] ADD [HardwareTier] nvarchar(max) NULL;
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Subscriptions') AND name = 'HardCap')
                            ALTER TABLE [Subscriptions] ADD [HardCap] int NOT NULL DEFAULT 5;
                    END
                ", ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Table creation check for Subscriptions returned a warning (table may already exist).");
        }

        bool hasSubscription = await _dbContext.Subscriptions.AnyAsync(ct);
        if (hasSubscription) return;

        _logger.LogInformation("No active subscription license key found in database. Initializing default 1-year starter subscription key.");

        string currentDriveId = _driveProvider.GetCurrentDriveInstanceId();
        var seedReq = new GenerateSubscriptionKeyRequest
        {
            TaxPayerTIN = "2000123456",
            PlanName = "Annual Enterprise Bridge",
            DurationMonths = 12,
            MaxDevices = 10,
            GracePeriodDays = 7,
            DriveInstanceId = currentDriveId,
            HardwareTier = "TillStick"
        };

        var seedKeyRes = GenerateSubscriptionKey(seedReq);

        var seedEntity = new SubscriptionEntity
        {
            Id = Guid.NewGuid(),
            ApiKey = seedKeyRes.ApiKey,
            PlanName = seedKeyRes.PlanName,
            TaxPayerTIN = seedReq.TaxPayerTIN,
            ValidFrom = seedKeyRes.ValidFrom,
            ValidTo = seedKeyRes.ValidTo,
            GracePeriodDays = seedReq.GracePeriodDays,
            MaxDevices = seedReq.MaxDevices,
            IsActive = true,
            ActivatedAt = DateTime.UtcNow,
            ActivatedBy = "SystemSeed"
        };

        _dbContext.Subscriptions.Add(seedEntity);
        await _dbContext.SaveChangesAsync(ct);
        _logger.LogInformation("Default starter subscription key seeded successfully until {ValidTo}.", seedKeyRes.ValidTo);
    }

    public async Task<SubscriptionStatusDto> GetCurrentSubscriptionStatusAsync(CancellationToken ct = default)
    {
        await EnsureSeedSubscriptionAsync(ct);

        var activeSub = await _dbContext.Subscriptions
            .Where(s => s.IsActive)
            .OrderByDescending(s => s.ActivatedAt)
            .FirstOrDefaultAsync(ct);

        if (activeSub == null)
        {
            return new SubscriptionStatusDto
            {
                Status = "Unlicensed",
                PlanName = "No Active License",
                IsExpired = true,
                IsHardExpired = true,
                WarningMessage = "No active subscription key configured. Please enter a valid subscription key."
            };
        }

        return MapToStatusDto(activeSub.ApiKey, activeSub.PlanName, activeSub.TaxPayerTIN, activeSub.ValidFrom, activeSub.ValidTo, activeSub.GracePeriodDays, activeSub.MaxDevices);
    }

    public async Task<SubscriptionStatusDto> ActivateSubscriptionAsync(string apiKey, string? activatedBy = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException("Subscription API Key cannot be empty.");
        }

        var (payload, isValidSig) = ValidateAndParseToken(apiKey);
        if (!isValidSig || payload == null)
        {
            throw new InvalidOperationException("Invalid or tampered Subscription API Key. Cryptographic signature verification failed.");
        }

        var now = DateTime.UtcNow;
        var validTo = DateTime.Parse(payload.To).ToUniversalTime();
        var validFrom = DateTime.Parse(payload.From).ToUniversalTime();
        var graceDays = payload.GraceDays > 0 ? payload.GraceDays : 7;
        var graceEndsAt = validTo.AddDays(graceDays);

        if (now > graceEndsAt)
        {
            throw new InvalidOperationException($"The provided Subscription API Key has already hard-expired on {validTo:yyyy-MM-dd HH:mm:ss} UTC (Grace period ended on {graceEndsAt:yyyy-MM-dd HH:mm:ss} UTC).");
        }

        var currentDriveId = _driveProvider.GetCurrentDriveInstanceId();
        if (string.IsNullOrWhiteSpace(payload.DriveId))
        {
            throw new InvalidOperationException("Subscription Key Rejection: This key is not hardware-bound (missing DriveInstanceId). All subscription keys must be hardware-bound to the physical drive instance.");
        }

        if (!string.Equals(payload.DriveId, currentDriveId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Drive Instance Mismatch! This Subscription Key was issued for Drive '{payload.DriveId}', but FDMS Bridge is running on Drive '{currentDriveId}'. Key sharing across drives is prohibited.");
        }

        // Check if key is already active (idempotent re-use)
        var activeSub = await _dbContext.Subscriptions
            .Where(s => s.IsActive)
            .OrderByDescending(s => s.ActivatedAt)
            .FirstOrDefaultAsync(ct);

        if (activeSub != null && string.Equals(activeSub.ApiKey.Trim(), apiKey.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("Subscription key activation requested for key already active. No changes made.");
            return MapToStatusDto(activeSub.ApiKey, activeSub.PlanName, activeSub.TaxPayerTIN, activeSub.ValidFrom, activeSub.ValidTo, activeSub.GracePeriodDays, activeSub.MaxDevices);
        }

        // Deactivate previous active keys
        var existingActive = await _dbContext.Subscriptions.Where(s => s.IsActive).ToListAsync(ct);
        foreach (var sub in existingActive)
        {
            sub.IsActive = false;
        }

        int existingMaxDevices = activeSub?.MaxDevices ?? 0;
        int effectiveMaxDevices = Math.Max(existingMaxDevices, payload.MaxDev);

        var newSub = new SubscriptionEntity
        {
            Id = Guid.NewGuid(),
            ApiKey = apiKey.Trim(),
            PlanName = payload.Plan,
            TaxPayerTIN = payload.Tin,
            DriveInstanceId = currentDriveId,
            HardwareTier = payload.Tier ?? "TillStick",
            HardCap = payload.Tier == "Cloud" ? 100 : (payload.Tier == "BranchBox" ? 25 : 5),
            ValidFrom = validFrom,
            ValidTo = validTo,
            GracePeriodDays = graceDays,
            MaxDevices = effectiveMaxDevices,
            IsActive = true,
            ActivatedAt = now,
            ActivatedBy = activatedBy ?? "DashboardUser"
        };

        _dbContext.Subscriptions.Add(newSub);
        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation("New subscription key activated successfully: Plan={Plan}, ValidTo={ValidTo}, TIN={TIN}, DriveId={DriveId}", payload.Plan, validTo, payload.Tin, currentDriveId);

        return MapToStatusDto(apiKey, payload.Plan, payload.Tin, validFrom, validTo, graceDays, payload.MaxDev);
    }

    public async Task<SubscriptionStatusDto> ApplyDeviceUpgradeKeyAsync(string upgradeKey, string? appliedBy = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(upgradeKey))
        {
            throw new ArgumentException("Device Upgrade Key cannot be empty.");
        }

        var (payload, isValidSig) = ValidateAndParseUpgradeToken(upgradeKey);
        if (!isValidSig || payload == null)
        {
            throw new InvalidOperationException("Invalid or tampered Device Upgrade Key. Cryptographic signature verification failed.");
        }

        string currentDriveId = _driveProvider.GetCurrentDriveInstanceId();
        if (!string.IsNullOrWhiteSpace(payload.DriveId) && !string.Equals(payload.DriveId, currentDriveId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Drive Instance Mismatch! This Device Upgrade Key was generated for Drive '{payload.DriveId}', but FDMS Bridge is running on Drive '{currentDriveId}'. Upgrade keys cannot be used on other drives.");
        }

        bool nonceUsed = await _dbContext.UsedUpgradeNonces.AnyAsync(n => n.Nonce == payload.Nonce, ct);
        if (nonceUsed)
        {
            throw new InvalidOperationException($"This Device Upgrade Key (Nonce: {payload.Nonce}) has already been activated on this system.");
        }

        var activeSub = await _dbContext.Subscriptions
            .Where(s => s.IsActive)
            .OrderByDescending(s => s.ActivatedAt)
            .FirstOrDefaultAsync(ct);

        int currentMaxDevices = activeSub?.MaxDevices ?? 1;
        string tier = payload.Tier ?? activeSub?.HardwareTier ?? "TillStick";
        int hardCap = tier.Equals("Cloud", StringComparison.OrdinalIgnoreCase) ? 100 : (tier.Equals("BranchBox", StringComparison.OrdinalIgnoreCase) ? 25 : 5);

        int newMaxDevices = payload.NewMaxDevices > 0 ? payload.NewMaxDevices : currentMaxDevices + payload.SlotsAdded;

        if (newMaxDevices > hardCap)
        {
            throw new InvalidOperationException($"Requested upgrade ({newMaxDevices} devices) exceeds the hard cap ceiling for {tier} tier (Maximum: {hardCap} devices). Upgrading beyond {hardCap} devices requires migrating to a Branch Box or Cloud tier.");
        }

        if (activeSub != null)
        {
            activeSub.MaxDevices = newMaxDevices;
            activeSub.HardwareTier = tier;
            activeSub.HardCap = hardCap;
            activeSub.DriveInstanceId = currentDriveId;
        }

        _dbContext.UsedUpgradeNonces.Add(new UsedUpgradeNonceEntity
        {
            Id = Guid.NewGuid(),
            Nonce = payload.Nonce,
            DriveInstanceId = currentDriveId,
            SlotsAdded = payload.SlotsAdded,
            NewMaxDevices = newMaxDevices,
            AppliedAt = DateTime.UtcNow,
            AppliedBy = appliedBy ?? "DashboardAdmin"
        });

        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation("Device Capacity Upgrade applied successfully! New MaxDevices={NewMax}, SlotsAdded={Slots}, DriveId={DriveId}", newMaxDevices, payload.SlotsAdded, currentDriveId);

        return await GetCurrentSubscriptionStatusAsync(ct);
    }

    public GenerateSubscriptionKeyResponse GenerateSubscriptionKey(GenerateSubscriptionKeyRequest request)
    {
        var now = DateTime.UtcNow;
        var validFrom = now.AddMinutes(-5); // allow 5 min clock skew
        var validTo = now.AddMonths(request.DurationMonths);
        var graceDays = request.GracePeriodDays > 0 ? request.GracePeriodDays : 7;

        var payloadObj = new TokenPayload
        {
            Tin = request.TaxPayerTIN,
            Plan = request.PlanName,
            DriveId = request.DriveInstanceId ?? string.Empty,
            Tier = request.HardwareTier ?? "TillStick",
            From = validFrom.ToString("o"),
            To = validTo.ToString("o"),
            MaxDev = request.MaxDevices,
            GraceDays = graceDays,
            Nonce = Guid.NewGuid().ToString("N")
        };

        string payloadJson = JsonSerializer.Serialize(payloadObj);
        string payloadB64 = Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson));
        string signature = ComputeHmacSignature(payloadB64, _secretKey);

        string apiKey = $"FDMS-SUB-{payloadB64}.{signature}";

        return new GenerateSubscriptionKeyResponse
        {
            ApiKey = apiKey,
            PlanName = request.PlanName,
            ValidFrom = validFrom,
            ValidTo = validTo,
            GracePeriodEndsAt = validTo.AddDays(graceDays)
        };
    }

    private SubscriptionStatusDto MapToStatusDto(string apiKey, string planName, string tin, DateTime validFrom, DateTime validTo, int graceDays, int maxDevices)
    {
        var now = DateTime.UtcNow;
        var gracePeriodEndsAt = validTo.AddDays(graceDays);

        bool isExpired = now > validTo;
        bool isInGracePeriod = isExpired && now <= gracePeriodEndsAt;
        bool isHardExpired = now > gracePeriodEndsAt;

        string status;
        string? warningMessage = null;

        if (isHardExpired)
        {
            status = "Expired";
            warningMessage = $"Subscription expired on {validTo:MMM dd, yyyy}. Grace period has ended. Fiscal operations are blocked until renewed.";
        }
        else if (isInGracePeriod)
        {
            status = "GracePeriod";
            int graceRemaining = (int)Math.Ceiling((gracePeriodEndsAt - now).TotalDays);
            warningMessage = $"Subscription expired on {validTo:MMM dd, yyyy}. Operating in 7-day grace period ({graceRemaining} day(s) remaining). Renew key immediately to avoid service interruption.";
        }
        else
        {
            status = "Active";
            int daysLeft = (int)Math.Ceiling((validTo - now).TotalDays);
            if (daysLeft <= 14)
            {
                warningMessage = $"Subscription expiring soon in {daysLeft} day(s) on {validTo:MMM dd, yyyy}.";
            }
        }

        int daysRemaining = (int)Math.Max(0, Math.Ceiling((validTo - now).TotalDays));
        int graceDaysRemaining = (int)Math.Max(0, Math.Ceiling((gracePeriodEndsAt - now).TotalDays));

        return new SubscriptionStatusDto
        {
            Status = status,
            PlanName = planName,
            TaxPayerTIN = tin,
            ValidFrom = validFrom,
            ValidTo = validTo,
            GracePeriodEndsAt = gracePeriodEndsAt,
            DaysRemaining = daysRemaining,
            GraceDaysRemaining = graceDaysRemaining,
            MaskedApiKey = MaskKey(apiKey),
            IsExpired = isExpired,
            IsInGracePeriod = isInGracePeriod,
            IsHardExpired = isHardExpired,
            MaxDevices = maxDevices,
            WarningMessage = warningMessage
        };
    }

    private (TokenPayload? Payload, bool IsValid) ValidateAndParseToken(string token)
    {
        try
        {
            string cleanToken = token.Trim();
            if (cleanToken.StartsWith("FDMS-SUB-"))
            {
                cleanToken = cleanToken.Substring("FDMS-SUB-".Length);
            }

            var parts = cleanToken.Split('.');
            if (parts.Length != 2) return (null, false);

            string payloadB64 = parts[0];
            string signature = parts[1];

            string expectedSig = ComputeHmacSignature(payloadB64, _secretKey);
            if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(signature),
                Encoding.UTF8.GetBytes(expectedSig)))
            {
                return (null, false);
            }

            byte[] jsonBytes = Base64UrlDecode(payloadB64);
            string payloadJson = Encoding.UTF8.GetString(jsonBytes);
            var payload = JsonSerializer.Deserialize<TokenPayload>(payloadJson);

            return (payload, payload != null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse or validate subscription token.");
            return (null, false);
        }
    }

    private static string ComputeHmacSignature(string data, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        return Base64UrlEncode(hash);
    }

    private static string Base64UrlEncode(byte[] input)
    {
        return Convert.ToBase64String(input)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static byte[] Base64UrlDecode(string input)
    {
        string padded = input.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }
        return Convert.FromBase64String(padded);
    }

    private (UpgradeTokenPayload? Payload, bool IsValid) ValidateAndParseUpgradeToken(string token)
    {
        try
        {
            string cleanToken = token.Trim();
            if (cleanToken.StartsWith("FDMS-UPG-"))
            {
                cleanToken = cleanToken.Substring("FDMS-UPG-".Length);
            }

            var parts = cleanToken.Split('.');
            if (parts.Length != 2) return (null, false);

            string payloadB64 = parts[0];
            string signature = parts[1];

            string expectedSig = ComputeHmacSignature(payloadB64, _secretKey);
            if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(signature),
                Encoding.UTF8.GetBytes(expectedSig)))
            {
                return (null, false);
            }

            byte[] jsonBytes = Base64UrlDecode(payloadB64);
            string payloadJson = Encoding.UTF8.GetString(jsonBytes);
            var payload = JsonSerializer.Deserialize<UpgradeTokenPayload>(payloadJson);

            return (payload, payload != null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse or validate upgrade token.");
            return (null, false);
        }
    }

    private static string MaskKey(string key)
    {
        if (string.IsNullOrEmpty(key) || key.Length <= 14) return "FDMS-SUB-****";
        return key.Substring(0, 12) + "..." + key.Substring(key.Length - 6);
    }

    private class TokenPayload
    {
        public string Tin { get; set; } = string.Empty;
        public string Plan { get; set; } = string.Empty;
        public string DriveId { get; set; } = string.Empty;
        public string Tier { get; set; } = "TillStick";
        public string From { get; set; } = string.Empty;
        public string To { get; set; } = string.Empty;
        public int MaxDev { get; set; } = 10;
        public int GraceDays { get; set; } = 7;
        public string Nonce { get; set; } = string.Empty;
    }

    private class UpgradeTokenPayload
    {
        public string KeyType { get; set; } = "DeviceUpgrade";
        public string Tin { get; set; } = string.Empty;
        public string DriveId { get; set; } = string.Empty;
        public string Tier { get; set; } = "TillStick";
        public int SlotsAdded { get; set; } = 1;
        public int NewMaxDevices { get; set; } = 0;
        public int HardCap { get; set; } = 5;
        public string Nonce { get; set; } = string.Empty;
    }
}
