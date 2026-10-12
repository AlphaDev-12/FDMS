using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FdmsBridgeManager.Models.Entities;

[Table("EmailLogs")]
public class EmailLogEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public string RecipientEmail { get; set; } = string.Empty;

    public string Subject { get; set; } = string.Empty;

    public string BodyHtml { get; set; } = string.Empty;

    public string ApiKeySent { get; set; } = string.Empty;

    public string Status { get; set; } = "Sent"; // Sent, Failed

    public DateTime SentAt { get; set; } = DateTime.UtcNow;
}
