namespace FdmsGatewayApi.Services.Interfaces;

public interface IZimraHealthCheck
{
    bool IsHealthy { get; }
    bool IsSimulatedOffline { get; }
    string TargetEnvironment { get; }
    DateTime? LastCheckedUtc { get; }
    long LastLatencyMs { get; }
    string? LastError { get; }
    void UpdateStatus(bool isHealthy, long latencyMs, string? error = null);
    void SetSimulatedOffline(bool simulatedOffline);
    void SetTargetEnvironment(string environment);
    string GetActiveBaseUrl();
}

