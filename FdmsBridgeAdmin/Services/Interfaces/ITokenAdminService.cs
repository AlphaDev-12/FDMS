using FdmsBridgeAdmin.Models.Dtos;

namespace FdmsBridgeAdmin.Services.Interfaces;

public interface ITokenAdminService
{
    Task<decimal> GetPricePerTokenAsync(CancellationToken ct = default);
    Task<decimal> GetPricePerDeviceUpgradeTokenAsync(CancellationToken ct = default);
    Task<decimal> UpdatePricePerTokenAsync(decimal newPrice, CancellationToken ct = default);
    Task<decimal> UpdatePricePerDeviceUpgradeTokenAsync(decimal newPrice, CancellationToken ct = default);
    Task<TokenVoucherDto> GenerateTokenVoucherAsync(GenerateTokenVoucherRequest req, string generatedBy = "SystemAdmin", CancellationToken ct = default);
    Task<ValidateAndRedeemVoucherResponse> RedeemVoucherAsync(ValidateAndRedeemVoucherRequest req, CancellationToken ct = default);
    Task<List<TokenVoucherDto>> GetVouchersAsync(Guid? firmId = null, CancellationToken ct = default);
}
