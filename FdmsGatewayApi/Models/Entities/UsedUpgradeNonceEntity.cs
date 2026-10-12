using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FdmsGatewayApi.Models.Entities;

[Table("UsedUpgradeNonces")]
public class UsedUpgradeNonceEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public string Nonce { get; set; } = string.Empty;

    [Required]
    public string DriveInstanceId { get; set; } = string.Empty;

    public int SlotsAdded { get; set; }

    public int NewMaxDevices { get; set; }

    public DateTime AppliedAt { get; set; } = DateTime.UtcNow;

    public string AppliedBy { get; set; } = "System";
}
