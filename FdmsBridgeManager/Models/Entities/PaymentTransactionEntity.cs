using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FdmsBridgeManager.Models.Entities;

[Table("PaymentTransactions")]
public class PaymentTransactionEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid? SubscriptionId { get; set; }

    public Guid? ClientId { get; set; }

    [Column(TypeName = "decimal(18, 2)")]
    public decimal Amount { get; set; }

    public string Currency { get; set; } = "USD";

    public string PayNowReference { get; set; } = string.Empty;

    public string PollUrl { get; set; } = string.Empty;

    public string Status { get; set; } = "Created"; // Created, Pending, Paid, Failed, Cancelled

    public string PaymentType { get; set; } = "OnlinePayNow"; // OnlinePayNow, ManualCash

    public string PayerEmail { get; set; } = string.Empty;

    public string TaxPayerTIN { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? PaidAt { get; set; }
}
