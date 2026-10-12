using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FdmsGatewayApi.Models.Entities;

[Table("SharedPartnerProfiles")]
public class SharedPartnerProfileEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int PartnerId { get; set; }

    [Required]
    [MaxLength(100)]
    public string PartnerKey { get; set; } = string.Empty;

    public decimal WholesaleTokenBalance { get; set; } = 0m;

    public DateTime? LastReconciledAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual ICollection<TenantBusinessAccountEntity> TenantBusinessAccounts { get; set; } = new List<TenantBusinessAccountEntity>();
}

[Table("TenantBusinessAccounts")]
public class TenantBusinessAccountEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int TenantId { get; set; }

    public int PartnerId { get; set; }

    [Required]
    [MaxLength(100)]
    public string TenantApiKey { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string BusinessName { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(PartnerId))]
    public virtual SharedPartnerProfileEntity? PartnerProfile { get; set; }

    public virtual ICollection<TenantZimraDeviceEntity> ZimraDevices { get; set; } = new List<TenantZimraDeviceEntity>();

    public virtual ICollection<MultiTenantFiscalLedgerEntity> FiscalLedgers { get; set; } = new List<MultiTenantFiscalLedgerEntity>();

    public virtual ICollection<TenantUserEntity> TenantUsers { get; set; } = new List<TenantUserEntity>();
}

[Table("TenantZimraDevices")]
public class TenantZimraDeviceEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int TenantId { get; set; }

    [Required]
    [MaxLength(50)]
    public string ZimraDeviceId { get; set; } = string.Empty;

    [Required]
    public byte[] EncryptedPfxCertificate { get; set; } = Array.Empty<byte>();

    [Required]
    [MaxLength(100)]
    public string CertificateDecryptionSalt { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? CertificatePassword { get; set; }

    public long LastSequenceNumber { get; set; } = 0;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(TenantId))]
    public virtual TenantBusinessAccountEntity? TenantAccount { get; set; }
}

[Table("MultiTenantFiscalLedger")]
public class MultiTenantFiscalLedgerEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int TenantId { get; set; }

    [Required]
    [MaxLength(50)]
    public string ZimraDeviceId { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string InvoiceUuid { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string InvoiceNumber { get; set; } = string.Empty;

    [Required]
    [MaxLength(10)]
    public string CurrencyCode { get; set; } = "USD";

    [Column(TypeName = "decimal(18, 4)")]
    public decimal GrossInclusiveAmount { get; set; }

    [Required]
    public string CryptographicSignature { get; set; } = string.Empty;

    [MaxLength(50)]
    public string ZimraSyncStatus { get; set; } = "SYNCED";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(TenantId))]
    public virtual TenantBusinessAccountEntity? TenantAccount { get; set; }
}
