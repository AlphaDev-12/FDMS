using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FdmsBridgeManager.Models.Entities;

[Table("WhiteLabelConfigs")]
public class WhiteLabelConfigEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public string FirmName { get; set; } = "Apex Fiscal Consultancy";

    public string Tagline { get; set; } = "Enterprise FDMS Bridge & Compliance Suite";

    public string LogoUrl { get; set; } = "https://images.unsplash.com/photo-1560179707-f14e90ef3623?auto=format&fit=crop&w=300&q=80";

    public string FaviconUrl { get; set; } = "";

    public string PrimaryColor { get; set; } = "#3b82f6"; // Hex color

    public string SecondaryColor { get; set; } = "#1d4ed8"; // Hex color

    public string SupportEmail { get; set; } = "support@apexconsultancy.co.zw";

    public string SupportPhone { get; set; } = "+263 77 123 4567";

    public string Address { get; set; } = "100 Sam Nujoma Street, Harare, Zimbabwe";

    public string CopyrightText { get; set; } = "© 2026 Apex Fiscal Consultancy Ltd. All Rights Reserved.";

    public string EmailFooterText { get; set; } = "Need assistance with your FDMS Bridge deployment? Contact our technical team at support@apexconsultancy.co.zw";

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
