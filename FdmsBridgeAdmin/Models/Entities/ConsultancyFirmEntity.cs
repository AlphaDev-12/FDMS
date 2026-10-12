using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FdmsBridgeAdmin.Models.Entities;

[Table("ConsultancyFirms")]
public class ConsultancyFirmEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public string FirmName { get; set; } = string.Empty;

    [Required]
    public string ContactEmail { get; set; } = string.Empty;

    public string ContactPhone { get; set; } = string.Empty;

    public string TaxPayerTIN { get; set; } = string.Empty;

    [Required]
    public string FirmApiKey { get; set; } = string.Empty;

    public int TotalTokensPurchased { get; set; } = 0;

    public int TotalSubscriptionTokensPurchased { get; set; } = 0;

    public int TotalDeviceUpgradeTokensPurchased { get; set; } = 0;

    public int TotalTokensConsumed { get; set; } = 0;

    public int CurrentTokenBalance { get; set; } = 0;

    public bool IsActive { get; set; } = true;

    public string UnlimitedTier { get; set; } = "None"; // None, Annual, Completely

    public DateTime? UnlimitedExpiresAt { get; set; }

    public string ImmutabilityStatus { get; set; } = "OK"; // OK, TAMPER_DETECTED

    public DateTime? LastHeartbeatAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
