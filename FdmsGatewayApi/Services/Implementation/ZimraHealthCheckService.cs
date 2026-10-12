using System.Diagnostics;
using FdmsGatewayApi.Data;
using FdmsGatewayApi.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace FdmsGatewayApi.Services.Implementation;

public class ZimraHealthCheckState : IZimraHealthCheck
{
    private bool _isHealthyInternal = true;
    private bool _isSimulatedOffline = false;
    private string _targetEnvironment = "Sandbox";

    public bool IsHealthy => (_targetEnvironment == "Simulated" || !_isSimulatedOffline) && _isHealthyInternal;
    public bool IsSimulatedOffline => _targetEnvironment == "Simulated" || _isSimulatedOffline;
    public string TargetEnvironment => _targetEnvironment;
    public DateTime? LastCheckedUtc { get; private set; }
    public long LastLatencyMs { get; private set; }
    public string? LastError { get; private set; }

    public void UpdateStatus(bool isHealthy, long latencyMs, string? error = null)
    {
        _isHealthyInternal = isHealthy;
        LastLatencyMs = latencyMs;
        LastError = error;
        LastCheckedUtc = DateTime.UtcNow;
    }

    public void SetSimulatedOffline(bool simulatedOffline)
    {
        _isSimulatedOffline = simulatedOffline;
        if (simulatedOffline)
        {
            _targetEnvironment = "Simulated";
        }
        else if (_targetEnvironment == "Simulated")
        {
            _targetEnvironment = "Sandbox";
        }
    }

    public void SetTargetEnvironment(string environment)
    {
        if (string.Equals(environment, "Live", StringComparison.OrdinalIgnoreCase))
        {
            _targetEnvironment = "Live";
            _isSimulatedOffline = false;
        }
        else if (string.Equals(environment, "Simulated", StringComparison.OrdinalIgnoreCase))
        {
            _targetEnvironment = "Simulated";
            _isSimulatedOffline = true;
        }
        else
        {
            _targetEnvironment = "Sandbox";
            _isSimulatedOffline = false;
        }
    }

    public string GetActiveBaseUrl()
    {
        if (_targetEnvironment == "Live")
            return "https://fdmsapi.zimra.co.zw/";
        return "https://fdmsapitest.zimra.co.zw/";
    }
}

public class ZimraHealthCheckService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IZimraHealthCheck _healthCheckState;
    private readonly IAuditLogger _auditLogger;
    private readonly ILogger<ZimraHealthCheckService> _logger;
    private bool? _lastReportedHealthyState = null;

    public ZimraHealthCheckService(
        IServiceProvider serviceProvider,
        IZimraHealthCheck healthCheckState,
        IAuditLogger auditLogger,
        ILogger<ZimraHealthCheckService> logger)
    {
        _serviceProvider = serviceProvider;
        _healthCheckState = healthCheckState;
        _auditLogger = auditLogger;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ZIMRA Health Check Service started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            var sw = Stopwatch.StartNew();
            bool currentHealthy = false;
            string? currentError = null;
            int? currentDeviceId = null;

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<FdmsDbContext>();
                var apiClient = scope.ServiceProvider.GetRequiredService<IFdmsApiClient>();
                var keyStore = scope.ServiceProvider.GetRequiredService<IEncryptedKeyStore>();
                var passPhraseProvider = scope.ServiceProvider.GetRequiredService<IPassPhraseProvider>();

                var devices = await db.DeviceConfigs.Where(d => !string.IsNullOrEmpty(d.CertificatePem)).ToListAsync(stoppingToken);

                if (devices.Count > 0)
                {
                    string passPhrase = passPhraseProvider.GetPassPhrase();

                    bool allSucceeded = true;
                    List<string> failedDeviceErrors = new();

                    foreach (var device in devices)
                    {
                        currentDeviceId = device.DeviceId;
                        string privateKeyPem = keyStore.DecryptKey(device.EncryptedPrivateKeyPem, passPhrase);
                        var pingResponse = await apiClient.PingAsync(device.DeviceId, device.CertificatePem, privateKeyPem);

                        if (pingResponse == null)
                        {
                            allSucceeded = false;
                            failedDeviceErrors.Add($"Device {device.DeviceId}: Empty response");
                        }
                    }

                    sw.Stop();

                    if (allSucceeded)
                    {
                        currentHealthy = true;
                        _healthCheckState.UpdateStatus(true, sw.ElapsedMilliseconds);
                    }
                    else
                    {
                        currentHealthy = false;
                        currentError = string.Join("; ", failedDeviceErrors);
                        _healthCheckState.UpdateStatus(false, sw.ElapsedMilliseconds, currentError);
                    }
                }
                else
                {
                    // No registered device yet
                    sw.Stop();
                    currentHealthy = true;
                    _healthCheckState.UpdateStatus(true, sw.ElapsedMilliseconds, "No registered device to ping");
                }
            }
            catch (Exception ex)
            {
                sw.Stop();
                currentHealthy = false;
                currentError = ex.Message;
                _healthCheckState.UpdateStatus(false, sw.ElapsedMilliseconds, ex.Message);
                _logger.LogWarning("ZIMRA ping failed: {Message}", ex.Message);
            }

            // Log state transition audit event when ZIMRA connectivity status changes
            if (_lastReportedHealthyState != currentHealthy)
            {
                _lastReportedHealthyState = currentHealthy;
                string msg = currentHealthy
                    ? "ZIMRA Gateway connection restored (Online)."
                    : $"ZIMRA Gateway connection lost / degraded: {currentError ?? "Server unreachable"}";

                await _auditLogger.LogAsync(
                    endpoint: "ZimraHealthStatus",
                    direction: "Outbound",
                    httpStatusCode: currentHealthy ? 200 : 503,
                    errorCategory: currentHealthy ? "None" : "Infrastructure",
                    errorMessage: msg,
                    deviceId: currentDeviceId,
                    durationMs: sw.ElapsedMilliseconds,
                    cancellationToken: stoppingToken);
            }

            await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
        }
    }
}
