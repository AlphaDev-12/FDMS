using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FdmsBridgeAdmin.Models.Entities;

[Table("TokenVouchers")]
public class TokenVoucherEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid ConsultancyFirmId { get; set; }

    [Required]
    public string VoucherCode { get; set; } = string.Empty;

    public int TokenCount { get; set; }

    public string? TokenType { get; set; } = "Subscription"; // Subscription ($5) or DeviceUpgrade ($50)

    [Column(TypeName = "decimal(18, 2)")]
    public decimal UnitPriceUSD { get; set; } = 5.00m;

    [Column(TypeName = "decimal(18, 2)")]
    public decimal TotalPriceUSD { get; set; }

    public string Status { get; set; } = "Active"; // Active, Redeemed, Revoked

    public bool PurchasedViaPayNow { get; set; } = false;

    public string TransactionRef { get; set; } = string.Empty;

    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;

    public DateTime? RedeemedAt { get; set; }

    public string GeneratedBy { get; set; } = "SystemAdmin";
}
