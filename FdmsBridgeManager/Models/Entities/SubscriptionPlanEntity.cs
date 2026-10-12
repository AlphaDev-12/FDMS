using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FdmsBridgeManager.Models.Entities;

[Table("SubscriptionPlans")]
public class SubscriptionPlanEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public string PlanCode { get; set; } = string.Empty; // e.g. PLAN_MONTHLY, PLAN_QUARTERLY, PLAN_ANNUAL

    [Required]
    public string PlanName { get; set; } = string.Empty; // e.g. Monthly Starter, Annual Enterprise

    [Required]
    public string BillingCycle { get; set; } = "Monthly"; // Monthly, Quarterly, Annual

    [Column(TypeName = "decimal(18, 2)")]
    public decimal Price { get; set; }

    public string Currency { get; set; } = "USD";

    public int DurationMonths { get; set; } = 1;

    public int MaxDevices { get; set; } = 10;

    public int GracePeriodDays { get; set; } = 7;

    public string Description { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public int SortOrder { get; set; } = 0;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
