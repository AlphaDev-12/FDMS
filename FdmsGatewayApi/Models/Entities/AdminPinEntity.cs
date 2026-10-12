using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FdmsGatewayApi.Models.Entities;

[Table("AdminPins")]
public class AdminPinEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required]
    public string PinHash { get; set; } = string.Empty;

    [Required]
    public string PinSalt { get; set; } = string.Empty;

    public bool IsDefaultPin { get; set; } = true;

    public int FailedAttempts { get; set; } = 0;

    public DateTime? LockoutTillUtc { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
