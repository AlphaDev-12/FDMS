using FdmsGatewayApi.Data;
using FdmsGatewayApi.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FdmsGatewayApi.Services.Implementation;

public class DatabaseHealthCheck : IHealthCheck
{
    private readonly IServiceProvider _serviceProvider;

    public DatabaseHealthCheck(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FdmsDbContext>();
            bool canConnect = await db.Database.CanConnectAsync(cancellationToken);

            if (canConnect)
            {
                return HealthCheckResult.Healthy("Database connection established successfully.");
            }

            return HealthCheckResult.Unhealthy("Database is not reachable.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Database check failed with exception.", ex);
        }
    }
}

public class OfflineQueueHealthCheck : IHealthCheck
{
    private readonly IServiceProvider _serviceProvider;

    public OfflineQueueHealthCheck(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FdmsDbContext>();

            int deadLetterCount = await db.OfflineQueue.CountAsync(q => q.Status == "DeadLetter", cancellationToken);
            int pendingCount = await db.OfflineQueue.CountAsync(q => q.Status == "Queued" || q.Status == "Processing", cancellationToken);

            var data = new Dictionary<string, object>
            {
                { "PendingQueueCount", pendingCount },
                { "DeadLetterCount", deadLetterCount }
            };

            if (deadLetterCount > 0)
            {
                return HealthCheckResult.Degraded($"Offline queue has {deadLetterCount} dead-lettered item(s) requiring intervention.", data: data);
            }

            return HealthCheckResult.Healthy($"Offline queue healthy. Pending: {pendingCount}, DeadLetter: {deadLetterCount}", data: data);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Offline queue health check failed.", ex);
        }
    }
}

public class ZimraGatewayHealthCheck : IHealthCheck
{
    private readonly IZimraHealthCheck _zimraHealthState;

    public ZimraGatewayHealthCheck(IZimraHealthCheck zimraHealthState)
    {
        _zimraHealthState = zimraHealthState;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var data = new Dictionary<string, object>
        {
            { "LastCheckedUtc", _zimraHealthState.LastCheckedUtc?.ToString("o") ?? "Never" },
            { "LatencyMs", _zimraHealthState.LastLatencyMs }
        };

        if (_zimraHealthState.LastCheckedUtc == null)
        {
            return Task.FromResult(HealthCheckResult.Healthy("ZIMRA Gateway probe pending initial execution.", data: data));
        }

        if (_zimraHealthState.IsHealthy)
        {
            return Task.FromResult(HealthCheckResult.Healthy($"ZIMRA Gateway online (Latency: {_zimraHealthState.LastLatencyMs}ms).", data: data));
        }

        data.Add("LastError", _zimraHealthState.LastError ?? "Unknown error");
        return Task.FromResult(HealthCheckResult.Degraded($"ZIMRA Gateway unreachable: {_zimraHealthState.LastError}", data: data));
    }
}
