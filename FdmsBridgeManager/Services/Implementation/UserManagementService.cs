using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FdmsBridgeManager.Data;
using FdmsBridgeManager.Models.Dtos;
using FdmsBridgeManager.Models.Entities;
using FdmsBridgeManager.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace FdmsBridgeManager.Services.Implementation;

public class UserManagementService : IUserManagementService
{
    private readonly BridgeManagerDbContext _dbContext;
    private readonly ILogger<UserManagementService> _logger;

    public UserManagementService(BridgeManagerDbContext dbContext, ILogger<UserManagementService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        {
            throw new ArgumentException("Username and password are required.");
        }

        string username = request.Username.Trim().ToLower();
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Username.ToLower() == username, ct);

        if (user == null || !user.IsActive || !VerifyPassword(request.Password, user.PasswordHash))
        {
            throw new UnauthorizedAccessException("Invalid username or password.");
        }

        user.LastLoginAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(ct);

        var permissions = ParsePermissions(user.PermissionsJson);
        string token = $"BM-SESSION-{Guid.NewGuid():N}.{user.Id}";

        return new LoginResponse
        {
            Token = token,
            Username = user.Username,
            FullName = user.FullName,
            Role = user.Role,
            Permissions = permissions
        };
    }

    public async Task<List<UserDto>> GetAllUsersAsync(CancellationToken ct = default)
    {
        var users = await _dbContext.Users.OrderBy(u => u.Username).ToListAsync(ct);
        return users.Select(MapToDto).ToList();
    }

    public async Task<UserDto> CreateUserAsync(CreateUserRequest request, CancellationToken ct = default)
    {
        string username = request.Username.Trim().ToLower();
        var existing = await _dbContext.Users.FirstOrDefaultAsync(u => u.Username.ToLower() == username, ct);
        if (existing != null)
        {
            throw new InvalidOperationException($"Username '{request.Username}' is already taken.");
        }

        var newUser = new UserEntity
        {
            Id = Guid.NewGuid(),
            Username = username,
            FullName = request.FullName.Trim(),
            Email = request.Email.Trim(),
            PasswordHash = HashPassword(request.Password),
            Role = string.IsNullOrWhiteSpace(request.Role) ? "Staff" : request.Role,
            PermissionsJson = JsonSerializer.Serialize(request.Permissions ?? new List<string>()),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Users.Add(newUser);
        await _dbContext.SaveChangesAsync(ct);
        return MapToDto(newUser);
    }

    public async Task<UserDto> UpdateUserPermissionsAsync(Guid id, UpdateUserPermissionsRequest request, CancellationToken ct = default)
    {
        var user = await _dbContext.Users.FindAsync(new object[] { id }, ct);
        if (user == null)
        {
            throw new KeyNotFoundException($"User with ID {id} not found.");
        }

        user.PermissionsJson = JsonSerializer.Serialize(request.Permissions ?? new List<string>());
        if (!string.IsNullOrWhiteSpace(request.Role))
        {
            user.Role = request.Role;
        }
        user.IsActive = request.IsActive;

        await _dbContext.SaveChangesAsync(ct);
        return MapToDto(user);
    }

    public async Task<UserDto> UpdateUserAsync(Guid id, UpdateUserRequest request, CancellationToken ct = default)
    {
        var user = await _dbContext.Users.FindAsync(new object[] { id }, ct);
        if (user == null)
        {
            throw new KeyNotFoundException($"User with ID {id} not found.");
        }

        if (!string.IsNullOrWhiteSpace(request.FullName))
        {
            user.FullName = request.FullName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(request.Email))
        {
            user.Email = request.Email.Trim();
        }

        if (!string.IsNullOrWhiteSpace(request.Password))
        {
            user.PasswordHash = HashPassword(request.Password);
        }

        if (!string.IsNullOrWhiteSpace(request.Role))
        {
            user.Role = request.Role;
        }

        user.PermissionsJson = JsonSerializer.Serialize(request.Permissions ?? new List<string>());
        user.IsActive = request.IsActive;

        await _dbContext.SaveChangesAsync(ct);
        return MapToDto(user);
    }

    public bool VerifyPermission(List<string> userPermissions, string requiredPermission)
    {
        if (userPermissions == null || userPermissions.Count == 0) return false;
        if (userPermissions.Contains("SuperAdmin") || userPermissions.Contains("*")) return true;
        return userPermissions.Contains(requiredPermission, StringComparer.OrdinalIgnoreCase);
    }

    private static string HashPassword(string password)
    {
        byte[] salt = new byte[] { 0x10, 0x20, 0x30, 0x40, 0x50, 0x60, 0x70, 0x80 };
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            10000,
            HashAlgorithmName.SHA256,
            32);
        return Convert.ToBase64String(hash);
    }

    private static bool VerifyPassword(string password, string storedHash)
    {
        string hash = HashPassword(password);
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(hash), Encoding.UTF8.GetBytes(storedHash));
    }

    private static List<string> ParsePermissions(string json)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(json)) return new List<string>();
            return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }

    private static UserDto MapToDto(UserEntity u)
    {
        return new UserDto
        {
            Id = u.Id,
            Username = u.Username,
            FullName = u.FullName,
            Email = u.Email,
            Role = u.Role,
            Permissions = ParsePermissions(u.PermissionsJson),
            IsActive = u.IsActive,
            CreatedAt = u.CreatedAt,
            LastLoginAt = u.LastLoginAt
        };
    }
}
