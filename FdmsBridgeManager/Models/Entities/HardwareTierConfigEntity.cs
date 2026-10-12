using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FdmsBridgeManager.Models.Entities;

[Table("HardwareTierConfigs")]
public class HardwareTierConfigEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public string TierName { get; set; } = string.Empty; // e.g. TillStick, BranchBox

    [Column(TypeName = "decimal(18, 2)")]
    public decimal PricePerSlotUSD { get; set; } = 50.00m;

    public int TokensPerSlot { get; set; } = 1;

    public int HardCap { get; set; } = 5;

    public int DefaultDevices { get; set; } = 1;

    public string Description { get; set; } = string.Empty;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public string UpdatedBy { get; set; } = "System";
}
