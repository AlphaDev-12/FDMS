using System.Net.Http.Json;
using FdmsBridgeManager.Data;
using FdmsBridgeManager.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace FdmsBridgeManager.Services.Implementation;

public class PhoneHomeBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _config;
    private readonly ILogger<PhoneHomeBackgroundService> _logger;
    private readonly HttpClient _httpClient;

    public PhoneHomeBackgroundService(
        IServiceProvider serviceProvider,
        IConfiguration config,
        ILogger<PhoneHomeBackgroundService> logger,
        IHttpClientFactory httpClientFactory)
    {
        _serviceProvider = serviceProvider;
        _config = config;
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient("BridgeAdminPhoneHome");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Phone Back Home Telemetry Background Service started (24-hour interval).");

        // Delay 10 seconds after application startup to allow DB initialization
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SendTelemetryHeartbeatAsync("24H_Heartbeat", stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send scheduled 24-hour phone-back-home telemetry heartbeat to Bridge Admin.");
            }

            // Sleep for 24 hours
            try
            {
                await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    public async Task TriggerImmediateTamperAlertAsync(string errorMessage, CancellationToken ct = default)
    {
        _logger.LogWarning("Triggering IMMEDIATE out-of-band phone-back-home alert for immutability tamper error: {Error}", errorMessage);
        try
        {
            await SendTelemetryHeartbeatAsync("TamperAlert", ct, errorMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to transmit immediate tamper alert to Bridge Admin.");
        }
    }

    private async Task SendTelemetryHeartbeatAsync(string logType, CancellationToken ct, string? tamperErrorMessage = null)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BridgeManagerDbContext>();
        var ledgerService = scope.ServiceProvider.GetRequiredService<IImmutableTokenLedgerService>();

        var status = await ledgerService.GetTokenStatusAsync(ct);
        int activeSubCount = await db.Subscriptions.CountAsync(s => s.IsActive, ct);

        string firmApiKey = _config["BridgeManager:FirmApiKey"] ?? "FDMS-FIRM-DEMO2026-KEY99";
        string firmName = _config["BridgeManager:FirmName"] ?? "Apex Fiscal Consultancy";
        string adminUrl = _config["BridgeManager:BridgeAdminUrl"] ?? "http://localhost:5005";

        var payload = new
        {
            FirmApiKey = firmApiKey,
            FirmName = firmName,
            TotalTokensPurchased = status.TotalTokensPurchased,
            TotalSubscriptionTokensPurchased = status.TotalSubscriptionTokensPurchased,
            TotalDeviceUpgradeTokensPurchased = status.TotalUpgradeTokensPurchased,
            TotalTokensConsumed = status.TotalTokensConsumed,
            CurrentTokenBalance = status.CurrentTokenBalance,
            ActiveClientSubscriptions = activeSubCount,
            ImmutabilityStatus = status.ImmutabilityStatus,
            LogType = logType,
            TamperErrorMessage = tamperErrorMessage ?? status.LastTamperError
        };

        string endpoint = $"{adminUrl.TrimEnd('/')}/api/v1/telemetry/heartbeat";
        try
        {
            var response = await _httpClient.PostAsJsonAsync(endpoint, payload, ct);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Successfully sent phone-back-home telemetry ({LogType}) to Bridge Admin ({Url})", logType, endpoint);

                // Mark immutability logs as reported
                var unreportedLogs = await db.TokenImmutabilityLogs.Where(l => !l.IsReportedToAdmin).ToListAsync(ct);
                foreach (var l in unreportedLogs)
                {
                    l.IsReportedToAdmin = true;
                    l.ReportedToAdminAt = DateTime.UtcNow;
                }
                if (unreportedLogs.Count > 0)
                {
                    await db.SaveChangesAsync(ct);
                }
            }
            else
            {
                _logger.LogWarning("Bridge Admin telemetry receiver returned HTTP status {StatusCode}", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not reach Bridge Admin telemetry endpoint at {Url}", endpoint);
        }
    }
}
