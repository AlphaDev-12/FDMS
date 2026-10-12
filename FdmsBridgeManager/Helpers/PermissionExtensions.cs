using FdmsBridgeManager.Data;
using FdmsBridgeManager.Models.Entities;
using FdmsBridgeManager.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace FdmsBridgeManager.Helpers;

public static class PermissionExtensions
{
    public static async Task<(bool Authorized, string ErrorMessage, UserEntity? User)> ValidatePermissionAsync(
        this HttpContext context,
        BridgeManagerDbContext dbContext,
        IUserManagementService userService,
        string requiredPermission)
    {
        string? token = context.Request.Headers["X-Session-Token"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(token))
        {
            var authHeader = context.Request.Headers["Authorization"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                token = authHeader.Substring(7).Trim();
            }
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            return (false, "Missing authentication session token. Please log in.", null);
        }

        int lastDot = token.LastIndexOf('.');
        if (lastDot < 0 || !Guid.TryParse(token.Substring(lastDot + 1), out var userId))
        {
            return (false, "Invalid authentication session token.", null);
        }

        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null || !user.IsActive)
        {
            return (false, "User account is disabled or does not exist.", null);
        }

        if (string.Equals(user.Role, "SuperAdmin", StringComparison.OrdinalIgnoreCase))
        {
            return (true, string.Empty, user);
        }

        var permissions = JsonSerializer.Deserialize<List<string>>(user.PermissionsJson ?? "[]") ?? new List<string>();
        if (permissions.Contains("SuperAdmin") || permissions.Contains("*"))
        {
            return (true, string.Empty, user);
        }

        if (!userService.VerifyPermission(permissions, requiredPermission))
        {
            return (false, $"Permission Denied: You do not have the required permission ({requiredPermission}).", user);
        }

        return (true, string.Empty, user);
    }
}
