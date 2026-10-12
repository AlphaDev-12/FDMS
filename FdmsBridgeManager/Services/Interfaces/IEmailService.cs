using FdmsBridgeManager.Models.Entities;

namespace FdmsBridgeManager.Services.Interfaces;

public interface IEmailService
{
    Task<EmailLogEntity> SendLicenseApiKeyEmailAsync(
        string recipientEmail,
        string clientName,
        string taxPayerTin,
        string planName,
        string apiKey,
        DateTime validTo,
        string paymentMethod,
        string transactionRef,
        CancellationToken ct = default);

    Task<List<EmailLogEntity>> GetEmailLogsAsync(CancellationToken ct = default);
}
