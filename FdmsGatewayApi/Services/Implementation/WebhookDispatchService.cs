using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FdmsGatewayApi.Data;
using FdmsGatewayApi.Models.Dtos;
using FdmsGatewayApi.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace FdmsGatewayApi.Services.Implementation;

public class WebhookDispatchService : IWebhookDispatchService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WebhookDispatchService> _logger;

    public WebhookDispatchService(
        IServiceProvider serviceProvider,
        IHttpClientFactory httpClientFactory,
        ILogger<WebhookDispatchService> logger)
    {
        _serviceProvider = serviceProvider;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task DispatchFiscalResponseAsync(FiscalizedDocumentResponseDto responseDto, CancellationToken cancellationToken = default)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FdmsDbContext>();

        var webhooks = await db.WebhookRegistrations
            .Where(w => w.IsActive)
            .ToListAsync(cancellationToken);

        if (webhooks.Count == 0) return;

        string jsonPayload = JsonSerializer.Serialize(responseDto);
        byte[] payloadBytes = Encoding.UTF8.GetBytes(jsonPayload);

        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(10);

        foreach (var webhook in webhooks)
        {
            if (cancellationToken.IsCancellationRequested) break;

            int maxRetries = 3;
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                if (cancellationToken.IsCancellationRequested) break;

                try
                {
                    using var httpRequest = new HttpRequestMessage(HttpMethod.Post, webhook.WebhookUrl)
                    {
                        Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
                    };

                    if (!string.IsNullOrEmpty(webhook.Secret))
                    {
                        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(webhook.Secret));
                        byte[] hash = hmac.ComputeHash(payloadBytes);
                        string signatureHex = Convert.ToHexString(hash).ToLowerInvariant();
                        httpRequest.Headers.Add("X-Webhook-Signature", signatureHex);
                    }

                    var response = await client.SendAsync(httpRequest, cancellationToken);
                    if (response.IsSuccessStatusCode)
                    {
                        _logger.LogInformation("Successfully dispatched webhook to {Url} for invoice {InvoiceNo} (Attempt {Attempt})",
                            webhook.WebhookUrl, responseDto.InvoiceNo, attempt);
                        break;
                    }

                    _logger.LogWarning("Webhook to {Url} returned status {StatusCode} for invoice {InvoiceNo} (Attempt {Attempt}/{Max})",
                        webhook.WebhookUrl, response.StatusCode, responseDto.InvoiceNo, attempt, maxRetries);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Exception dispatching webhook to {Url} for invoice {InvoiceNo} (Attempt {Attempt}/{Max})",
                        webhook.WebhookUrl, responseDto.InvoiceNo, attempt, maxRetries);
                }

                if (attempt < maxRetries)
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), cancellationToken);
                }
            }
        }
    }
}
