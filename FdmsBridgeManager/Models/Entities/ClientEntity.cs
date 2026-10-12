using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FdmsBridgeManager.Models.Entities;

[Table("Clients")]
public class ClientEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public string CompanyName { get; set; } = string.Empty;

    [Required]
    [StringLength(20)]
    public string TaxPayerTIN { get; set; } = string.Empty;

    public string ContactName { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    public string PhoneNumber { get; set; } = string.Empty;

    public string Address { get; set; } = string.Empty;

    [StringLength(100)]
    public string? TenantApiKey { get; set; }

    public string Status { get; set; } = "Active"; // Active, Suspended, Inactive

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
