using FdmsBridgeManager.Models.Dtos;
using FdmsBridgeManager.Models.Entities;

namespace FdmsBridgeManager.Services.Interfaces;

public interface IPayNowService
{
    Task<PublicCheckoutResponse> InitiateCheckoutAsync(PublicCheckoutRequest request, CancellationToken ct = default);
    Task<PublicCheckoutResponse> InitiateUpgradeCheckoutAsync(PublicUpgradeCheckoutRequest request, CancellationToken ct = default);
    Task<SubscriptionEntity> ProcessPaymentCompletionAsync(Guid transactionId, string payNowRef, CancellationToken ct = default);
    Task<PaymentTransactionEntity?> GetTransactionAsync(Guid transactionId, CancellationToken ct = default);
}
