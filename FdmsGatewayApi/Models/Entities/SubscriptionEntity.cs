using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FdmsGatewayApi.Models.Entities;

[Table("Subscriptions")]
public class SubscriptionEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public string ApiKey { get; set; } = string.Empty;

    [Required]
    public string PlanName { get; set; } = "Annual Enterprise";

    public string TaxPayerTIN { get; set; } = string.Empty;

    public DateTime ValidFrom { get; set; }

    public DateTime ValidTo { get; set; }

    public int GracePeriodDays { get; set; } = 7;

    public int MaxDevices { get; set; } = 10;

    public string DriveInstanceId { get; set; } = string.Empty;

    public string HardwareTier { get; set; } = "TillStick"; // TillStick or BranchBox

    public int HardCap { get; set; } = 5; // 5 for TillStick, 25 for BranchBox

    public bool IsActive { get; set; } = true;

    public DateTime ActivatedAt { get; set; } = DateTime.UtcNow;

    public string? ActivatedBy { get; set; }
}
