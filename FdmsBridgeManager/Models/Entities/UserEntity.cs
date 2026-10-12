using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FdmsBridgeManager.Models.Entities;

[Table("Users")]
public class UserEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public string Username { get; set; } = string.Empty;

    [Required]
    public string FullName { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string PasswordHash { get; set; } = string.Empty;

    public string Role { get; set; } = "Staff"; // SuperAdmin, Manager, Cashier, Staff

    // Comma-separated or JSON list of permission keys:
    // ManageClients, ManageSubscriptions, GenerateCashKeys, ManagePlans, ManageWhiteLabel, ManageUsers
    public string PermissionsJson { get; set; } = "[]";

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? LastLoginAt { get; set; }
}
