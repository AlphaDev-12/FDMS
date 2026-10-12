using FdmsBridgeAdmin.Models.Dtos;

namespace FdmsBridgeAdmin.Services.Interfaces;

public interface IPayNowAdminService
{
    Task<PayNowTokenCheckoutResponse> InitiateTokenPurchaseAsync(PayNowTokenCheckoutRequest req, CancellationToken ct = default);
    Task<TokenVoucherDto> ProcessPayNowCallbackAsync(string payNowRef, string status, CancellationToken ct = default);
}
