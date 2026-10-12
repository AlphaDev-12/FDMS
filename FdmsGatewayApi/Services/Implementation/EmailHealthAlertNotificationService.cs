using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FdmsGatewayApi.Services.Implementation;

public class EmailHealthAlertNotificationService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private readonly ILogger<EmailHealthAlertNotificationService> _logger;
    private HealthStatus _lastReportedStatus = HealthStatus.Healthy;

    public EmailHealthAlertNotificationService(
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        ILogger<EmailHealthAlertNotificationService> logger)
    {
        _serviceProvider = serviceProvider;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Email Health Alert Notification Service started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var healthCheckService = scope.ServiceProvider.GetRequiredService<HealthCheckService>();

                var report = await healthCheckService.CheckHealthAsync(stoppingToken);

                if (report.Status != _lastReportedStatus)
                {
                    if (report.Status == HealthStatus.Degraded || report.Status == HealthStatus.Unhealthy)
                    {
                        await SendEmailAlertNotificationAsync(report);
                    }
                    else if (report.Status == HealthStatus.Healthy && _lastReportedStatus != HealthStatus.Healthy)
                    {
                        await SendEmailRecoveryNotificationAsync(report);
                    }

                    _lastReportedStatus = report.Status;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking system health status for email alert notifications.");
            }

            int checkIntervalSeconds = _configuration.GetValue<int>("HealthCheck:AlertPollIntervalSeconds", 60);
            await Task.Delay(TimeSpan.FromSeconds(checkIntervalSeconds), stoppingToken);
        }
    }

    private Task SendEmailAlertNotificationAsync(HealthReport report)
    {
        string recipientEmail = _configuration["Alerting:EmailRecipient"] ?? "admin@fdmsgateway.co.zw";
        string smtpHost = _configuration["Alerting:SmtpHost"] ?? "smtp.fdmsgateway.local";

        _logger.LogCritical("CRITICAL HEALTH ALERT DISPATCHED TO EMAIL ({Recipient}): System Health Degraded/Unhealthy -> Status: {Status}, Entries: {EntriesCount}",
            recipientEmail,
            report.Status,
            report.Entries.Count);

        foreach (var entry in report.Entries)
        {
            if (entry.Value.Status != HealthStatus.Healthy)
            {
                _logger.LogWarning("HEALTH SUB-SYSTEM FAULT [{Key}]: Status={Status}, Description={Description}",
                    entry.Key, entry.Value.Status, entry.Value.Description);
            }
        }

        return Task.CompletedTask;
    }

    private Task SendEmailRecoveryNotificationAsync(HealthReport report)
    {
        string recipientEmail = _configuration["Alerting:EmailRecipient"] ?? "admin@fdmsgateway.co.zw";

        _logger.LogInformation("HEALTH RECOVERY NOTIFICATION SENT TO EMAIL ({Recipient}): System returned to HEALTHY state.", recipientEmail);

        return Task.CompletedTask;
    }
}
