namespace FdmsGatewayApi.Services.Interfaces;

public interface IAuditLogger
{
    Task LogAsync(
        string endpoint,
        string direction,
        int httpStatusCode,
        string errorCategory = "None",
        string? errorCode = null,
        string? errorMessage = null,
        string? invoiceNo = null,
        int? deviceId = null,
        string? rawRequestJson = null,
        string? rawResponseJson = null,
        long durationMs = 0,
        CancellationToken cancellationToken = default);
}
