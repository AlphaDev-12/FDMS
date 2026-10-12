using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using FdmsGatewayApi.Data;
using FdmsGatewayApi.Models.Entities;
using FdmsGatewayApi.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace FdmsGatewayApi.Services.Implementation;

public class AdminAuthService : IAdminAuthService
{
    private const string DEFAULT_PIN = "0000";
    private const int PBKDF2_ITERATIONS = 100_000;
    private const int SALT_SIZE = 16;
    private const int HASH_SIZE = 32;
    private const int RESET_CODE_PBKDF2_ITERATIONS = 210_000;
    private const int RESET_CODE_HASH_SIZE = 64;
    private const int MAX_FAILED_ATTEMPTS = 5;
    private static readonly TimeSpan LOCKOUT_DURATION = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan SESSION_EXPIRATION = TimeSpan.FromHours(8);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<AdminAuthService> _logger;
    private readonly ConcurrentDictionary<string, DateTime> _activeSessions = new();

    public AdminAuthService(IServiceScopeFactory scopeFactory, IConfiguration config, ILogger<AdminAuthService> logger)
    {
        _scopeFactory = scopeFactory;
        _config = config;
        _logger = logger;
    }

    public async Task EnsureSeedPinAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FdmsDbContext>();

        try
        {
            if (db.Database.IsSqlServer())
            {
                await db.Database.ExecuteSqlRawAsync(@"
                    IF OBJECT_ID(N'[AdminPins]', N'U') IS NULL
                    BEGIN
                        CREATE TABLE [AdminPins] (
                            [Id] int NOT NULL IDENTITY(1,1),
                            [PinHash] nvarchar(max) NOT NULL,
                            [PinSalt] nvarchar(max) NOT NULL,
                            [IsDefaultPin] bit NOT NULL,
                            [FailedAttempts] int NOT NULL,
                            [LockoutTillUtc] datetime2 NULL,
                            [UpdatedAt] datetime2 NOT NULL,
                            CONSTRAINT [PK_AdminPins] PRIMARY KEY ([Id])
                        );
                    END;", ct);
            }
            else if (db.Database.IsSqlite())
            {
                await db.Database.ExecuteSqlRawAsync(@"
                    CREATE TABLE IF NOT EXISTS [AdminPins] (
                        [Id] INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                        [PinHash] TEXT NOT NULL,
                        [PinSalt] TEXT NOT NULL,
                        [IsDefaultPin] INTEGER NOT NULL,
                        [FailedAttempts] INTEGER NOT NULL,
                        [LockoutTillUtc] TEXT NULL,
                        [UpdatedAt] TEXT NOT NULL
                    );", ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed executing table DDL for AdminPins.");
        }

        var existingPin = await db.AdminPins.FirstOrDefaultAsync(ct);
        if (existingPin == null)
        {
            _logger.LogInformation("No Admin PIN configured. Seeding default initial PIN ('0000')...");
            byte[] salt = RandomNumberGenerator.GetBytes(SALT_SIZE);
            byte[] hash = HashPin(DEFAULT_PIN, salt);

            var defaultEntity = new AdminPinEntity
            {
                PinHash = Convert.ToBase64String(hash),
                PinSalt = Convert.ToBase64String(salt),
                IsDefaultPin = true,
                FailedAttempts = 0,
                UpdatedAt = DateTime.UtcNow
            };

            db.AdminPins.Add(defaultEntity);
            await db.SaveChangesAsync(ct);
            _logger.LogWarning("SECURITY ALERT: Default Admin PIN initialized as '0000'. First login will require mandatory PIN change.");
        }
    }

    public async Task<AdminPinAuthResult> VerifyPinAsync(string pin, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(pin))
        {
            return new AdminPinAuthResult { Success = false, ErrorMessage = "PIN is required." };
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FdmsDbContext>();

        var pinEntity = await db.AdminPins.FirstOrDefaultAsync(ct);
        if (pinEntity == null)
        {
            await EnsureSeedPinAsync(ct);
            pinEntity = await db.AdminPins.FirstOrDefaultAsync(ct);
            if (pinEntity == null)
            {
                return new AdminPinAuthResult { Success = false, ErrorMessage = "System security state uninitialized." };
            }
        }

        // Check Lockout
        if (pinEntity.LockoutTillUtc.HasValue)
        {
            if (pinEntity.LockoutTillUtc.Value > DateTime.UtcNow)
            {
                var remainingLockout = (pinEntity.LockoutTillUtc.Value - DateTime.UtcNow).TotalMinutes;
                return new AdminPinAuthResult
                {
                    Success = false,
                    ErrorMessage = $"Account locked due to multiple failed attempts. Try again in {Math.Ceiling(remainingLockout)} minute(s).",
                    LockoutTillUtc = pinEntity.LockoutTillUtc
                };
            }
            else
            {
                // Lockout expired, reset counter
                pinEntity.LockoutTillUtc = null;
                pinEntity.FailedAttempts = 0;
                await db.SaveChangesAsync(ct);
            }
        }

        byte[] salt = Convert.FromBase64String(pinEntity.PinSalt);
        byte[] expectedHash = Convert.FromBase64String(pinEntity.PinHash);
        byte[] computedHash = HashPin(pin, salt);

        if (!CryptographicOperations.FixedTimeEquals(computedHash, expectedHash))
        {
            pinEntity.FailedAttempts++;
            int remaining = MAX_FAILED_ATTEMPTS - pinEntity.FailedAttempts;

            if (pinEntity.FailedAttempts >= MAX_FAILED_ATTEMPTS)
            {
                pinEntity.LockoutTillUtc = DateTime.UtcNow.Add(LOCKOUT_DURATION);
                await db.SaveChangesAsync(ct);
                _logger.LogWarning("Admin PIN authentication locked out after {Count} failed attempts.", pinEntity.FailedAttempts);
                return new AdminPinAuthResult
                {
                    Success = false,
                    ErrorMessage = "Too many incorrect PIN attempts. System locked for 15 minutes.",
                    LockoutTillUtc = pinEntity.LockoutTillUtc,
                    RemainingAttempts = 0
                };
            }

            await db.SaveChangesAsync(ct);
            _logger.LogWarning("Incorrect Admin PIN attempt. {Remaining} attempts remaining.", remaining);
            return new AdminPinAuthResult
            {
                Success = false,
                ErrorMessage = $"Incorrect PIN. {remaining} attempt(s) remaining.",
                RemainingAttempts = remaining
            };
        }

        // Successful authentication
        if (pinEntity.FailedAttempts > 0)
        {
            pinEntity.FailedAttempts = 0;
            await db.SaveChangesAsync(ct);
        }

        string token = CreateSessionToken();
        _activeSessions[token] = DateTime.UtcNow.Add(SESSION_EXPIRATION);

        return new AdminPinAuthResult
        {
            Success = true,
            IsDefaultPin = pinEntity.IsDefaultPin,
            Token = token
        };
    }

    public async Task<AdminPinAuthResult> ChangePinAsync(string currentPin, string newPin, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(currentPin) || string.IsNullOrWhiteSpace(newPin))
        {
            return new AdminPinAuthResult { Success = false, ErrorMessage = "Both current and new PIN are required." };
        }

        // Validate new PIN format & rules
        var valResult = ValidateNewPinPolicy(newPin);
        if (!valResult.IsValid)
        {
            return new AdminPinAuthResult { Success = false, ErrorMessage = valResult.Error };
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FdmsDbContext>();

        var pinEntity = await db.AdminPins.FirstOrDefaultAsync(ct);
        if (pinEntity == null)
        {
            return new AdminPinAuthResult { Success = false, ErrorMessage = "PIN entity not found." };
        }

        byte[] salt = Convert.FromBase64String(pinEntity.PinSalt);
        byte[] expectedHash = Convert.FromBase64String(pinEntity.PinHash);
        byte[] computedHash = HashPin(currentPin, salt);

        if (!CryptographicOperations.FixedTimeEquals(computedHash, expectedHash))
        {
            return new AdminPinAuthResult { Success = false, ErrorMessage = "Current PIN is incorrect." };
        }

        // Generate new salt and hash for the new PIN
        byte[] newSalt = RandomNumberGenerator.GetBytes(SALT_SIZE);
        byte[] newHash = HashPin(newPin, newSalt);

        pinEntity.PinSalt = Convert.ToBase64String(newSalt);
        pinEntity.PinHash = Convert.ToBase64String(newHash);
        pinEntity.IsDefaultPin = false;
        pinEntity.FailedAttempts = 0;
        pinEntity.LockoutTillUtc = null;
        pinEntity.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);

        _logger.LogInformation("Admin PIN successfully updated and initial default state cleared.");

        // Invalidate old sessions and issue new session token
        _activeSessions.Clear();
        string newToken = CreateSessionToken();
        _activeSessions[newToken] = DateTime.UtcNow.Add(SESSION_EXPIRATION);

        return new AdminPinAuthResult
        {
            Success = true,
            IsDefaultPin = false,
            Token = newToken
        };
    }

    public async Task<AdminPinAuthResult> ResetPinWithCodeAsync(string resetCode, CancellationToken ct = default)
    {
        await EnsureSeedPinAsync(ct);

        if (string.IsNullOrWhiteSpace(resetCode))
        {
            return new AdminPinAuthResult { Success = false, ErrorMessage = "16-digit master reset code is required." };
        }

        // Sanitize input (strip dashes, spaces, and non-digits)
        string sanitizedInput = Regex.Replace(resetCode, @"\D", "");
        if (sanitizedInput.Length != 16)
        {
            return new AdminPinAuthResult { Success = false, ErrorMessage = "Master reset code must be exactly 16 numeric digits." };
        }

        // Validate against securely stored PBKDF2 hash (never stores plaintext code)
        string? storedHash = _config["AdminAuth:MasterResetCodeHash"];
        string? storedSalt = _config["AdminAuth:MasterResetCodeSalt"];

        if (string.IsNullOrWhiteSpace(storedHash) || string.IsNullOrWhiteSpace(storedSalt))
        {
            _logger.LogError("SECURITY CONFIG ERROR: AdminAuth:MasterResetCodeHash or MasterResetCodeSalt not configured.");
            return new AdminPinAuthResult { Success = false, ErrorMessage = "Master reset is not configured. Contact system administrator." };
        }

        byte[] saltBytes = Convert.FromBase64String(storedSalt);
        byte[] expectedHash = Convert.FromBase64String(storedHash);

        byte[] inputHash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(sanitizedInput),
            saltBytes,
            RESET_CODE_PBKDF2_ITERATIONS,
            HashAlgorithmName.SHA512,
            RESET_CODE_HASH_SIZE);

        if (!CryptographicOperations.FixedTimeEquals(inputHash, expectedHash))
        {
            _logger.LogWarning("Failed Admin PIN reset attempt with invalid master recovery code.");
            return new AdminPinAuthResult { Success = false, ErrorMessage = "Invalid 16-digit master reset code." };
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FdmsDbContext>();

        var pinEntity = await db.AdminPins.FirstOrDefaultAsync(ct);
        if (pinEntity == null)
        {
            await EnsureSeedPinAsync(ct);
            pinEntity = await db.AdminPins.FirstOrDefaultAsync(ct);
        }

        if (pinEntity != null)
        {
            byte[] salt = RandomNumberGenerator.GetBytes(SALT_SIZE);
            byte[] hash = HashPin(DEFAULT_PIN, salt);

            pinEntity.PinHash = Convert.ToBase64String(hash);
            pinEntity.PinSalt = Convert.ToBase64String(salt);
            pinEntity.IsDefaultPin = true;
            pinEntity.FailedAttempts = 0;
            pinEntity.LockoutTillUtc = null;
            pinEntity.UpdatedAt = DateTime.UtcNow;

            await db.SaveChangesAsync(ct);
        }

        _activeSessions.Clear();
        _logger.LogWarning("SECURITY ACTION: Admin PIN successfully reset back to default '0000' via 16-digit Master Reset Code.");

        return new AdminPinAuthResult
        {
            Success = true,
            IsDefaultPin = true,
            ErrorMessage = "Admin PIN reset to default '0000'. Please log in with default PIN '0000' and set a new secure PIN."
        };
    }

    public bool ValidateSession(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;

        CleanExpiredSessions();

        if (_activeSessions.TryGetValue(token, out var expTime))
        {
            if (expTime > DateTime.UtcNow)
            {
                // Sliding expiration
                _activeSessions[token] = DateTime.UtcNow.Add(SESSION_EXPIRATION);
                return true;
            }
            else
            {
                _activeSessions.TryRemove(token, out _);
            }
        }

        return false;
    }

    public void RevokeSession(string token)
    {
        if (!string.IsNullOrWhiteSpace(token))
        {
            _activeSessions.TryRemove(token, out _);
        }
    }

    private static byte[] HashPin(string pin, byte[] salt)
    {
        using var pbkdf2 = new Rfc2898DeriveBytes(pin, salt, PBKDF2_ITERATIONS, HashAlgorithmName.SHA256);
        return pbkdf2.GetBytes(HASH_SIZE);
    }

    private static string CreateSessionToken()
    {
        byte[] bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static (bool IsValid, string? Error) ValidateNewPinPolicy(string newPin)
    {
        if (!Regex.IsMatch(newPin, @"^\d{4,8}$"))
        {
            return (false, "New PIN must be 4 to 8 numeric digits only.");
        }

        if (newPin == DEFAULT_PIN)
        {
            return (false, "New PIN cannot be the default '0000' PIN.");
        }

        // Repetitive check (e.g. 1111, 2222)
        if (newPin.Distinct().Count() == 1)
        {
            return (false, "New PIN cannot consist of a single repeated digit (e.g., 1111).");
        }

        // Sequential check (e.g. 1234, 4321)
        if (newPin == "1234" || newPin == "4321" || newPin == "12345678" || newPin == "87654321")
        {
            return (false, "New PIN cannot be a simple sequential sequence.");
        }

        return (true, null);
    }

    private void CleanExpiredSessions()
    {
        var now = DateTime.UtcNow;
        foreach (var kvp in _activeSessions)
        {
            if (kvp.Value <= now)
            {
                _activeSessions.TryRemove(kvp.Key, out _);
            }
        }
    }
}
