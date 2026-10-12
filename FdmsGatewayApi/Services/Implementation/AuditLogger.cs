using FdmsGatewayApi.Data;
using FdmsGatewayApi.Models.Entities;
using FdmsGatewayApi.Services.Interfaces;

namespace FdmsGatewayApi.Services.Implementation;

public class AuditLogger : IAuditLogger
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AuditLogger> _logger;

    public AuditLogger(IServiceProvider serviceProvider, ILogger<AuditLogger> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task LogAsync(
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
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FdmsDbContext>();

            var entity = new AuditLogEntity
            {
                Id = Guid.NewGuid(),
                Timestamp = DateTime.UtcNow,
                DeviceId = deviceId,
                InvoiceNo = invoiceNo,
                Direction = direction,
                Endpoint = endpoint,
                RawRequestJson = rawRequestJson ?? string.Empty,
                RawResponseJson = rawResponseJson,
                HttpStatusCode = httpStatusCode,
                ErrorCategory = errorCategory,
                ErrorCode = errorCode,
                ErrorMessage = errorMessage,
                DurationMs = durationMs
            };

            db.AuditLogs.Add(entity);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write audit log entry for endpoint {Endpoint}", endpoint);
        }
    }
}
