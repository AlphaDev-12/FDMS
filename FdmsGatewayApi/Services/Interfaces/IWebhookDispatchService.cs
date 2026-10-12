using FdmsGatewayApi.Models.Dtos;

namespace FdmsGatewayApi.Services.Interfaces;

public interface IWebhookDispatchService
{
    /// <summary>
    /// Dispatches full fiscalized document response payload to all registered ERP webhook endpoints asynchronously.
    /// Includes HMAC-SHA256 signature in X-Webhook-Signature header using registered secret.
    /// </summary>
    Task DispatchFiscalResponseAsync(FiscalizedDocumentResponseDto responseDto, CancellationToken cancellationToken = default);
}
