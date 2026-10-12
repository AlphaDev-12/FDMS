using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FdmsGatewayApi.Models.Entities;

[Table("DeviceConfigs")]
public class DeviceConfigEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int DeviceId { get; set; }

    [Required]
    public string TaxPayerName { get; set; } = string.Empty;

    [Required]
    [StringLength(10)]
    public string TaxPayerTIN { get; set; } = string.Empty;

    public string? VatNumber { get; set; }

    [Required]
    public string DeviceSerialNo { get; set; } = string.Empty;

    public string DeviceBranchName { get; set; } = string.Empty;

    [Required]
    public string ActivationKey { get; set; } = string.Empty;

    public string DeviceModelName { get; set; } = "EdERP-FDMS-Gateway";
    public string DeviceModelVersionNo { get; set; } = "v1.0";
    public string DeviceOperatingMode { get; set; } = "Online";

    // Encrypted Security Materials
    public string EncryptedPrivateKeyPem { get; set; } = string.Empty;
    public string CertificatePem { get; set; } = string.Empty;
    public string? ServerCertificatePem { get; set; }
    public DateTime? CertificateValidTill { get; set; }

    public string QrUrl { get; set; } = string.Empty;

    // Day & Counter State
    public int CurrentFiscalDayNo { get; set; } = 0;
    public int CurrentReceiptGlobalNo { get; set; } = 0;
    public int CurrentReceiptCounter { get; set; } = 0;
    public string? LastReceiptHash { get; set; }

    public string FiscalDayStatus { get; set; } = "FiscalDayClosed";
    public DateTime? FiscalDayOpenedAt { get; set; }
    public int TaxPayerDayMaxHrs { get; set; } = 24;
}

[Table("FiscalDocuments")]
public class FiscalDocumentEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public string InvoiceNo { get; set; } = string.Empty;

    [Required]
    public string ReceiptType { get; set; } = "FiscalInvoice"; // FiscalInvoice, CreditNote, DebitNote

    [Required]
    [StringLength(3)]
    public string Currency { get; set; } = "USD";

    [Column(TypeName = "decimal(18, 2)")]
    public decimal TotalAmount { get; set; }

    public int DeviceId { get; set; }

    public int FiscalDayNo { get; set; }
    public int ReceiptCounter { get; set; }
    public int ReceiptGlobalNo { get; set; }

    public string DeviceHash { get; set; } = string.Empty;
    public string? PreviousDeviceHash { get; set; }
    public string DeviceSignature { get; set; } = string.Empty;

    public long? ReceiptID { get; set; }
    public DateTime? ServerDate { get; set; }
    public string? FdmsSignature { get; set; }
    public string? QrCodeUrl { get; set; }
    public string? OperationID { get; set; }

    [Required]
    public string FiscalStatus { get; set; } = "Pending"; // Pending, Fiscalized, Failed

    public string? ErrorMessage { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public string RawPayloadJson { get; set; } = string.Empty;
}

[Table("WebhookRegistrations")]
public class WebhookRegistrationEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public string WebhookUrl { get; set; } = string.Empty;

    public string? Secret { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
