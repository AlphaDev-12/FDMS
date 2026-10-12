using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FdmsBridgeManager.Models.Entities;

[Table("Subscriptions")]
public class SubscriptionEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid? ClientId { get; set; }
    public Guid? PlanId { get; set; }

    [Required]
    public string ApiKey { get; set; } = string.Empty;

    [Required]
    public string PlanName { get; set; } = string.Empty;

    public string BillingCycle { get; set; } = "Monthly";

    [Column(TypeName = "decimal(18, 2)")]
    public decimal Price { get; set; }

    public string TaxPayerTIN { get; set; } = string.Empty;

    public string ClientEmail { get; set; } = string.Empty;

    public DateTime ValidFrom { get; set; }

    public DateTime ValidTo { get; set; }

    public int GracePeriodDays { get; set; } = 7;

    public int MaxDevices { get; set; } = 10;

    public bool IsActive { get; set; } = true;

    public string PaymentStatus { get; set; } = "Paid"; // Paid, Pending, Failed, CashPayment

    public string PaymentMethod { get; set; } = "PayNow"; // PayNow, Cash, BankTransfer

    public string TransactionRef { get; set; } = string.Empty;

    public DateTime ActivatedAt { get; set; } = DateTime.UtcNow;

    public string? ActivatedBy { get; set; }
}
