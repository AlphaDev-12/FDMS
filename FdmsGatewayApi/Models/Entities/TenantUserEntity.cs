using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FdmsGatewayApi.Models.Entities;

[Table("TenantUsers")]
public class TenantUserEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int TenantId { get; set; }

    [Required]
    [MaxLength(150)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [MaxLength(256)]
    public string PasswordHash { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string PasswordSalt { get; set; } = string.Empty;

    [Required]
    [MaxLength(150)]
    public string FullName { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    public string Role { get; set; } = "TenantAdmin"; // TenantAdmin, TenantOperator, TenantViewer

    [Required]
    [MaxLength(500)]
    public string Permissions { get; set; } = "ViewDashboard,ViewDocuments,ManageDevices,ManageUsers";

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? LastLoginAt { get; set; }

    [ForeignKey(nameof(TenantId))]
    public virtual TenantBusinessAccountEntity? TenantAccount { get; set; }
}
