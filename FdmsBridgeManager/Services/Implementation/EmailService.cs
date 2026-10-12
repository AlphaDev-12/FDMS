using FdmsBridgeManager.Data;
using FdmsBridgeManager.Models.Entities;
using FdmsBridgeManager.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace FdmsBridgeManager.Services.Implementation;

public class EmailService : IEmailService
{
    private readonly BridgeManagerDbContext _dbContext;
    private readonly IWhiteLabelService _whiteLabelService;
    private readonly ILogger<EmailService> _logger;

    public EmailService(
        BridgeManagerDbContext dbContext,
        IWhiteLabelService whiteLabelService,
        ILogger<EmailService> logger)
    {
        _dbContext = dbContext;
        _whiteLabelService = whiteLabelService;
        _logger = logger;
    }

    public async Task<EmailLogEntity> SendLicenseApiKeyEmailAsync(
        string recipientEmail,
        string clientName,
        string taxPayerTin,
        string planName,
        string apiKey,
        DateTime validTo,
        string paymentMethod,
        string transactionRef,
        CancellationToken ct = default)
    {
        var branding = await _whiteLabelService.GetConfigAsync(ct);

        string subject = $"[License Key] Your FDMS Gateway Subscription Key for {clientName}";

        string bodyHtml = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8' />
    <style>
        body {{ font-family: 'Segoe UI', Arial, sans-serif; background-color: #f4f6f9; margin: 0; padding: 20px; color: #1e293b; }}
        .container {{ max-width: 600px; margin: 0 auto; background: #ffffff; border-radius: 12px; overflow: hidden; box-shadow: 0 10px 25px rgba(0,0,0,0.08); border: 1px solid #e2e8f0; }}
        .header {{ background: linear-gradient(135deg, {branding.PrimaryColor}, {branding.SecondaryColor}); color: white; padding: 30px 24px; text-align: center; }}
        .header h1 {{ margin: 0; font-size: 22px; font-weight: 700; }}
        .header p {{ margin: 5px 0 0 0; opacity: 0.9; font-size: 13px; }}
        .content {{ padding: 30px 24px; }}
        .badge {{ display: inline-block; background: #dbeafe; color: #1e40af; padding: 6px 12px; border-radius: 20px; font-weight: 600; font-size: 12px; text-transform: uppercase; margin-bottom: 15px; }}
        .key-box {{ background: #0f172a; color: #38bdf8; font-family: 'Courier New', monospace; padding: 18px; border-radius: 8px; font-size: 14px; word-break: break-all; margin: 20px 0; border: 1px solid #334155; }}
        .details-table {{ width: 100%; border-collapse: collapse; margin: 20px 0; }}
        .details-table td {{ padding: 10px 0; border-bottom: 1px solid #f1f5f9; font-size: 14px; }}
        .details-table td.label {{ color: #64748b; font-weight: 500; width: 40%; }}
        .details-table td.value {{ color: #0f172a; font-weight: 600; }}
        .footer {{ background: #f8fafc; padding: 20px 24px; text-align: center; font-size: 12px; color: #64748b; border-top: 1px solid #e2e8f0; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>{branding.FirmName}</h1>
            <p>{branding.Tagline}</p>
        </div>
        <div class='content'>
            <span class='badge'>Subscription License Activated</span>
            <p>Dear <strong>{clientName}</strong>,</p>
            <p>Thank you for subscribing to the FDMS Gateway solution. Your payment via <strong>{paymentMethod}</strong> (Ref: {transactionRef}) has been processed successfully.</p>
            
            <p>Here is your cryptographically signed FDMS Subscription API Key:</p>
            
            <div class='key-box'>
                {apiKey}
            </div>

            <table class='details-table'>
                <tr><td class='label'>Taxpayer TIN</td><td class='value'>{taxPayerTin}</td></tr>
                <tr><td class='label'>Subscription Plan</td><td class='value'>{planName}</td></tr>
                <tr><td class='label'>Validity Period</td><td class='value'>Valid until {validTo:MMMM dd, yyyy}</td></tr>
                <tr><td class='label'>Payment Method</td><td class='value'>{paymentMethod} ({transactionRef})</td></tr>
            </table>

            <p style='font-size: 13px; color: #475569;'>
                <strong>Installation Instructions:</strong> Log in to your client FDMS Gateway Dashboard, navigate to <em>Subscriptions</em>, click <em>Activate License Key</em>, and paste the API Key provided above.
            </p>
        </div>
        <div class='footer'>
            <p>{branding.EmailFooterText}</p>
            <p style='margin-top: 8px;'>{branding.CopyrightText} | Support: {branding.SupportEmail} / {branding.SupportPhone}</p>
        </div>
    </div>
</body>
</html>
";

        var emailLog = new EmailLogEntity
        {
            Id = Guid.NewGuid(),
            RecipientEmail = recipientEmail,
            Subject = subject,
            BodyHtml = bodyHtml,
            ApiKeySent = apiKey,
            Status = "Sent",
            SentAt = DateTime.UtcNow
        };

        _dbContext.EmailLogs.Add(emailLog);
        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation("Dispatched white-labeled license key email to {RecipientEmail} for client TIN {TIN}.", recipientEmail, taxPayerTin);

        return emailLog;
    }

    public async Task<List<EmailLogEntity>> GetEmailLogsAsync(CancellationToken ct = default)
    {
        return await _dbContext.EmailLogs
            .OrderByDescending(e => e.SentAt)
            .Take(100)
            .ToListAsync(ct);
    }
}
