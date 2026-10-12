using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FdmsBridgeManager.Models.Entities;

[Table("TokenLedger")]
public class TokenLedgerEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public string TransactionType { get; set; } = "TOPUP"; // TOPUP, DEDUCTION, CORRECTION

    public string? TokenType { get; set; } = "Subscription"; // Subscription ($5) or DeviceUpgrade ($50)

    public int TokenAmount { get; set; }

    public int BalanceAfter { get; set; }

    public string ReferenceKey { get; set; } = string.Empty; // Token Master Key or Client Subscription Key

    public string PreviousHash { get; set; } = "GENESIS_HASH_00000000000000000000000000000000";

    [Required]
    public string Hash { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public string? CreatedBy { get; set; } = "System";
}
