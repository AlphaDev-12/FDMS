using FdmsGatewayApi.Models.Dtos;

namespace FdmsGatewayApi.Services.Interfaces;

public interface ISubscriptionService
{
    Task<SubscriptionStatusDto> GetCurrentSubscriptionStatusAsync(CancellationToken ct = default);
    Task<SubscriptionStatusDto> ActivateSubscriptionAsync(string apiKey, string? activatedBy = null, CancellationToken ct = default);
    Task<SubscriptionStatusDto> ApplyDeviceUpgradeKeyAsync(string upgradeKey, string? appliedBy = null, CancellationToken ct = default);
    GenerateSubscriptionKeyResponse GenerateSubscriptionKey(GenerateSubscriptionKeyRequest request);
    Task EnsureSeedSubscriptionAsync(CancellationToken ct = default);
}
