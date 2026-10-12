using System.Security.Cryptography;
using System.Text;
using FdmsGatewayApi.Data;
using FdmsGatewayApi.Models.Dtos;
using FdmsGatewayApi.Models.Entities;
using FdmsGatewayApi.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FdmsGatewayApi.Controllers;

/// <summary>
/// Multi-Tenant User Management and SME Authentication Controller.
/// </summary>
[ApiController]
[Route("api/v1/tenant")]
[Produces("application/json")]
[Tags("Multi-Tenant User Security")]
public class TenantUserController : ControllerBase
{
    private readonly FdmsDbContext _db;
    private readonly ITenantRegistryService _tenantRegistry;
    private readonly IAdminAuthService _adminAuthService;
    private readonly IAuditLogger _auditLogger;
    private readonly IConfiguration _config;
    private readonly ILogger<TenantUserController> _logger;

    private static readonly Dictionary<string, (int UserId, int TenantId, DateTime ExpiresAt)> SessionCache = new();

    public TenantUserController(
        FdmsDbContext db,
        ITenantRegistryService tenantRegistry,
        IAdminAuthService adminAuthService,
        IAuditLogger auditLogger,
        IConfiguration config,
        ILogger<TenantUserController> logger)
    {
        _db = db;
        _tenantRegistry = tenantRegistry;
        _adminAuthService = adminAuthService;
        _auditLogger = auditLogger;
        _config = config;
        _logger = logger;
    }

    [HttpPost("auth/login")]
    public async Task<IActionResult> Login([FromBody] TenantLoginRequestDto request, CancellationToken cancellationToken)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new TenantLoginResponseDto { Success = false, Message = "Email and password are required." });
        }

        string normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var query = _db.TenantUsers
            .Include(u => u.TenantAccount)
            .Where(u => u.Email.ToLower() == normalizedEmail);

        if (!string.IsNullOrWhiteSpace(request.TenantApiKey))
        {
            var tenantContext = await _tenantRegistry.GetTenantContextAsync(request.TenantApiKey, cancellationToken);
            if (tenantContext != null)
            {
                query = query.Where(u => u.TenantId == tenantContext.TenantId);
            }
        }

        var user = await query.FirstOrDefaultAsync(cancellationToken);
        if (user == null)
        {
            return Unauthorized(new TenantLoginResponseDto { Success = false, Message = "Invalid email or password." });
        }

        if (!user.IsActive)
        {
            return StatusCode(403, new TenantLoginResponseDto { Success = false, Message = "Your user account has been deactivated. Contact your administrator." });
        }

        if (user.TenantAccount == null || !user.TenantAccount.IsActive)
        {
            return StatusCode(403, new TenantLoginResponseDto { Success = false, Message = "Subscription Suspended: Tenant business account is inactive or token balance depleted." });
        }

        bool isValidPassword = VerifyPassword(request.Password, user.PasswordHash, user.PasswordSalt);
        if (!isValidPassword)
        {
            await _auditLogger.LogAsync("TenantUserLoginFailed", "TenantAuth", 401, errorMessage: $"Failed login attempt for {normalizedEmail}", cancellationToken: cancellationToken);
            return Unauthorized(new TenantLoginResponseDto { Success = false, Message = "Invalid email or password." });
        }

        user.LastLoginAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        string sessionToken = GenerateSessionToken(user.Id, user.TenantId);

        await _auditLogger.LogAsync("TenantUserLoginSuccess", "TenantAuth", 200, errorMessage: $"User {normalizedEmail} logged in to TenantId {user.TenantId}", cancellationToken: cancellationToken);

        var userDto = MapToUserDto(user);

        return Ok(new TenantLoginResponseDto
        {
            Success = true,
            Message = "Authentication successful.",
            Token = sessionToken,
            TenantId = user.TenantId,
            BusinessName = user.TenantAccount.BusinessName,
            User = userDto
        });
    }

    [HttpPost("auth/register")]
    public async Task<IActionResult> PublicRegister([FromBody] PublicTenantRegisterRequestDto request, CancellationToken cancellationToken)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.TenantApiKey) || string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new TenantLoginResponseDto { Success = false, Message = "Tenant API Key, Email, and Password are required." });
        }

        if (request.Password.Length < 6)
        {
            return BadRequest(new TenantLoginResponseDto { Success = false, Message = "Password must be at least 6 characters in length." });
        }

        string cleanApiKey = request.TenantApiKey.Trim();
        var tenantContext = await _tenantRegistry.GetTenantContextAsync(cleanApiKey, cancellationToken);
        if (tenantContext == null || !tenantContext.IsActive)
        {
            if (cleanApiKey.StartsWith("css_", StringComparison.OrdinalIgnoreCase))
            {
                var partner = await _db.SharedPartnerProfiles.FirstOrDefaultAsync(cancellationToken);
                if (partner != null)
                {
                    var newTenantAccount = new TenantBusinessAccountEntity
                    {
                        PartnerId = partner.PartnerId,
                        TenantApiKey = cleanApiKey,
                        BusinessName = !string.IsNullOrWhiteSpace(request.FullName) ? $"{request.FullName.Trim()}'s Enterprise" : "SME Business Account",
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    };
                    _db.TenantBusinessAccounts.Add(newTenantAccount);
                    await _db.SaveChangesAsync(cancellationToken);
                    tenantContext = await _tenantRegistry.GetTenantContextAsync(cleanApiKey, cancellationToken);
                }
            }

            if (tenantContext == null || !tenantContext.IsActive)
            {
                return BadRequest(new TenantLoginResponseDto { Success = false, Message = "Invalid or inactive Tenant API Key. Registration failed." });
            }
        }

        string normalizedEmail = request.Email.Trim().ToLowerInvariant();

        bool exists = await _db.TenantUsers.AnyAsync(u => u.TenantId == tenantContext.TenantId && u.Email.ToLower() == normalizedEmail, cancellationToken);
        if (exists)
        {
            return BadRequest(new TenantLoginResponseDto { Success = false, Message = $"A user account with email '{normalizedEmail}' already exists for this business." });
        }

        // Users registered using the register view with Tenant API Key are granted full admin permissions for FDMS Bridge
        string assignedRole = "TenantAdmin";
        string permissions = "ViewDashboard,ViewDocuments,SubmitReceipts,ManageDevices,ManageUsers";

        string salt = GenerateSalt();
        string hash = HashPassword(request.Password, salt);
        string fullName = !string.IsNullOrWhiteSpace(request.FullName) ? request.FullName.Trim() : normalizedEmail.Split('@')[0];

        var newUser = new TenantUserEntity
        {
            TenantId = tenantContext.TenantId,
            Email = normalizedEmail,
            PasswordHash = hash,
            PasswordSalt = salt,
            FullName = fullName,
            Role = assignedRole,
            Permissions = permissions,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _db.TenantUsers.Add(newUser);
        await _db.SaveChangesAsync(cancellationToken);

        string sessionToken = GenerateSessionToken(newUser.Id, newUser.TenantId);

        await _auditLogger.LogAsync("TenantUserPublicRegistered", "TenantAuth", 200, errorMessage: $"Public registration successful for {normalizedEmail} under TenantId {tenantContext.TenantId}", cancellationToken: cancellationToken);

        var userDto = MapToUserDto(newUser);

        return Ok(new TenantLoginResponseDto
        {
            Success = true,
            Message = "Account registration successful. Welcome to the portal!",
            Token = sessionToken,
            TenantId = newUser.TenantId,
            BusinessName = tenantContext.BusinessName,
            User = userDto
        });
    }

    [HttpGet("auth/me")]
    public async Task<IActionResult> GetCurrentUser(CancellationToken cancellationToken)
    {
        var session = ValidateSessionToken();
        if (session == null)
        {
            return Unauthorized(new { message = "Invalid or expired tenant session token." });
        }

        var user = await _db.TenantUsers
            .Include(u => u.TenantAccount)
            .FirstOrDefaultAsync(u => u.Id == session.Value.UserId && u.TenantId == session.Value.TenantId, cancellationToken);

        if (user == null || !user.IsActive || user.TenantAccount == null || !user.TenantAccount.IsActive)
        {
            return Unauthorized(new { message = "User or tenant account is no longer active." });
        }

        return Ok(new
        {
            user = MapToUserDto(user),
            businessName = user.TenantAccount.BusinessName,
            tenantApiKey = user.TenantAccount.TenantApiKey
        });
    }

    [HttpGet("users")]
    public async Task<IActionResult> GetTenantUsers(CancellationToken cancellationToken)
    {
        var session = ValidateSessionToken();
        if (session == null)
        {
            return Unauthorized(new { message = "Invalid or expired tenant session token." });
        }

        if (!session.Value.IsAdminSession)
        {
            var currentUser = await _db.TenantUsers.FirstOrDefaultAsync(u => u.Id == session.Value.UserId, cancellationToken);
            if (currentUser == null || !HasPermission(currentUser, "ManageUsers"))
            {
                return StatusCode(403, new { message = "Forbidden: Insufficient permissions to view tenant user accounts." });
            }
        }

        var users = await _db.TenantUsers
            .Where(u => session.Value.IsAdminSession || u.TenantId == session.Value.TenantId)
            .OrderBy(u => u.FullName)
            .ToListAsync(cancellationToken);

        return Ok(users.Select(MapToUserDto));
    }

    [HttpPost("users")]
    public async Task<IActionResult> CreateTenantUser([FromBody] CreateTenantUserRequestDto request, CancellationToken cancellationToken)
    {
        var session = ValidateSessionToken();
        if (session == null)
        {
            return Unauthorized(new { message = "Invalid or expired tenant session token." });
        }

        int targetTenantId = session.Value.TenantId;

        if (!session.Value.IsAdminSession)
        {
            var currentUser = await _db.TenantUsers.FirstOrDefaultAsync(u => u.Id == session.Value.UserId, cancellationToken);
            if (currentUser == null || !HasPermission(currentUser, "ManageUsers"))
            {
                return StatusCode(403, new { message = "Forbidden: Insufficient permissions to register tenant users." });
            }
        }

        if (request == null || string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password) || string.IsNullOrWhiteSpace(request.FullName))
        {
            return BadRequest(new { message = "Email, Password, and FullName are required." });
        }

        string normalizedEmail = request.Email.Trim().ToLowerInvariant();

        bool exists = await _db.TenantUsers.AnyAsync(u => u.TenantId == targetTenantId && u.Email.ToLower() == normalizedEmail, cancellationToken);
        if (exists)
        {
            return BadRequest(new { message = $"A user with email '{normalizedEmail}' already exists for this business account." });
        }

        string salt = GenerateSalt();
        string hash = HashPassword(request.Password, salt);

        var permissionsList = request.Permissions ?? new List<string> { "ViewDashboard", "ViewDocuments" };
        string permsString = string.Join(",", permissionsList.Distinct());

        var newUser = new TenantUserEntity
        {
            TenantId = targetTenantId,
            Email = normalizedEmail,
            PasswordHash = hash,
            PasswordSalt = salt,
            FullName = request.FullName.Trim(),
            Role = string.IsNullOrWhiteSpace(request.Role) ? "TenantOperator" : request.Role.Trim(),
            Permissions = permsString,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _db.TenantUsers.Add(newUser);
        await _db.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync("TenantUserCreated", "TenantAdmin", 200, errorMessage: $"Registered user {normalizedEmail} for TenantId {targetTenantId}", cancellationToken: cancellationToken);

        return CreatedAtAction(nameof(GetTenantUsers), new { id = newUser.Id }, MapToUserDto(newUser));
    }

    [HttpPut("users/{id:int}/permissions")]
    public async Task<IActionResult> UpdatePermissions(int id, [FromBody] UpdateTenantUserPermissionsDto request, CancellationToken cancellationToken)
    {
        var session = ValidateSessionToken();
        if (session == null)
        {
            return Unauthorized(new { message = "Invalid or expired tenant session token." });
        }

        if (!session.Value.IsAdminSession)
        {
            var currentUser = await _db.TenantUsers.FirstOrDefaultAsync(u => u.Id == session.Value.UserId, cancellationToken);
            if (currentUser == null || !HasPermission(currentUser, "ManageUsers"))
            {
                return StatusCode(403, new { message = "Forbidden: Insufficient permissions to update user permissions." });
            }
        }

        var targetUser = await _db.TenantUsers.FirstOrDefaultAsync(u => u.Id == id && (session.Value.IsAdminSession || u.TenantId == session.Value.TenantId), cancellationToken);
        if (targetUser == null)
        {
            return NotFound(new { message = "Tenant user not found." });
        }

        if (!string.IsNullOrWhiteSpace(request.Role))
        {
            targetUser.Role = request.Role.Trim();
        }

        if (request.Permissions != null && request.Permissions.Count > 0)
        {
            targetUser.Permissions = string.Join(",", request.Permissions.Distinct());
        }

        if (request.IsActive.HasValue)
        {
            targetUser.IsActive = request.IsActive.Value;
        }

        await _db.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync("TenantUserPermissionsUpdated", "TenantAdmin", 200, errorMessage: $"Updated permissions for user {targetUser.Email}", cancellationToken: cancellationToken);

        return Ok(MapToUserDto(targetUser));
    }

    [HttpDelete("users/{id:int}")]
    public async Task<IActionResult> DeleteTenantUser(int id, CancellationToken cancellationToken)
    {
        var session = ValidateSessionToken();
        if (session == null)
        {
            return Unauthorized(new { message = "Invalid or expired tenant session token." });
        }

        if (!session.Value.IsAdminSession && session.Value.UserId == id)
        {
            return BadRequest(new { message = "You cannot delete your own active session account." });
        }

        if (!session.Value.IsAdminSession)
        {
            var currentUser = await _db.TenantUsers.FirstOrDefaultAsync(u => u.Id == session.Value.UserId, cancellationToken);
            if (currentUser == null || !HasPermission(currentUser, "ManageUsers"))
            {
                return StatusCode(403, new { message = "Forbidden: Insufficient permissions to remove user accounts." });
            }
        }

        var targetUser = await _db.TenantUsers.FirstOrDefaultAsync(u => u.Id == id && (session.Value.IsAdminSession || u.TenantId == session.Value.TenantId), cancellationToken);
        if (targetUser == null)
        {
            return NotFound(new { message = "Tenant user not found." });
        }

        _db.TenantUsers.Remove(targetUser);
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "Tenant user account removed successfully." });
    }

    private (int UserId, int TenantId, bool IsAdminSession)? ValidateSessionToken()
    {
        string? token = null;
        if (Request.Headers.TryGetValue("X-Tenant-Session", out var headerVal))
        {
            token = headerVal.ToString();
        }
        else if (Request.Headers.TryGetValue("X-Dashboard-Session", out var dashHeaderVal))
        {
            token = dashHeaderVal.ToString();
        }
        else if (Request.Headers.TryGetValue("Authorization", out var authVal))
        {
            string str = authVal.ToString();
            if (str.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                token = str.Substring(7).Trim();
            }
        }
        else if (Request.Headers.TryGetValue("X-API-Key", out var apiKeyVal))
        {
            token = apiKeyVal.ToString();
        }

        if (string.IsNullOrWhiteSpace(token)) return null;

        lock (SessionCache)
        {
            if (SessionCache.TryGetValue(token, out var sess))
            {
                if (sess.ExpiresAt > DateTime.UtcNow)
                {
                    return (sess.UserId, sess.TenantId, false);
                }
                SessionCache.Remove(token);
            }
        }

        var parsed = ParseToken(token);
        if (parsed.HasValue)
        {
            lock (SessionCache)
            {
                SessionCache[token] = (parsed.Value.UserId, parsed.Value.TenantId, DateTime.UtcNow.AddDays(7));
            }
            return (parsed.Value.UserId, parsed.Value.TenantId, false);
        }

        if (_adminAuthService.ValidateSession(token))
        {
            var tenantUser = _db.TenantUsers.FirstOrDefault(u => u.IsActive);
            int tenantId = tenantUser?.TenantId ?? 1;
            int userId = tenantUser?.Id ?? 1;
            return (userId, tenantId, true);
        }

        var adminKeys = _config.GetSection("ApiKeys:Admin").Get<string[]>() ?? Array.Empty<string>();
        var legacyKeys = _config.GetSection("ApiKeys:Keys").Get<string[]>() ?? Array.Empty<string>();
        if (adminKeys.Contains(token) || legacyKeys.Contains(token))
        {
            var tenantUser = _db.TenantUsers.FirstOrDefault(u => u.IsActive);
            int tenantId = tenantUser?.TenantId ?? 1;
            int userId = tenantUser?.Id ?? 1;
            return (userId, tenantId, true);
        }

        return null;
    }

    private static (int UserId, int TenantId)? ParseToken(string token)
    {
        try
        {
            var parts = token.Split(':');
            if ((parts.Length == 4 || parts.Length == 5) && parts[0] == "TENANT_SESSION")
            {
                int userId = int.Parse(parts[1]);
                int tenantId = int.Parse(parts[2]);
                long expiryTicks = long.Parse(parts[3]);
                if (new DateTime(expiryTicks, DateTimeKind.Utc) > DateTime.UtcNow)
                {
                    return (userId, tenantId);
                }
            }
        }
        catch { }
        return null;
    }

    private static string GenerateSessionToken(int userId, int tenantId)
    {
        var expires = DateTime.UtcNow.AddDays(7);
        string token = $"TENANT_SESSION:{userId}:{tenantId}:{expires.Ticks}:{Guid.NewGuid():N}";
        lock (SessionCache)
        {
            SessionCache[token] = (userId, tenantId, expires);
        }
        return token;
    }

    private static TenantUserDto MapToUserDto(TenantUserEntity entity)
    {
        var perms = (entity.Permissions ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        return new TenantUserDto
        {
            Id = entity.Id,
            TenantId = entity.TenantId,
            Email = entity.Email,
            FullName = entity.FullName,
            Role = entity.Role,
            Permissions = perms,
            IsActive = entity.IsActive,
            CreatedAt = entity.CreatedAt,
            LastLoginAt = entity.LastLoginAt
        };
    }

    private static bool HasPermission(TenantUserEntity user, string permissionName)
    {
        if (string.Equals(user.Role, "TenantAdmin", StringComparison.OrdinalIgnoreCase)) return true;
        var perms = (user.Permissions ?? string.Empty).Split(',');
        return perms.Any(p => string.Equals(p.Trim(), permissionName, StringComparison.OrdinalIgnoreCase));
    }

    public static string GenerateSalt()
    {
        byte[] bytes = new byte[16];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes);
    }

    public static string HashPassword(string password, string salt)
    {
        byte[] saltBytes = Convert.FromBase64String(salt);
        using var pbkdf2 = new Rfc2898DeriveBytes(password, saltBytes, 10000, HashAlgorithmName.SHA256);
        byte[] hash = pbkdf2.GetBytes(32);
        return Convert.ToBase64String(hash);
    }

    public static bool VerifyPassword(string password, string hash, string salt)
    {
        string newHash = HashPassword(password, salt);
        return string.Equals(newHash, hash);
    }
}
