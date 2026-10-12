using FdmsBridgeAdmin.Models.Dtos;
using FdmsBridgeAdmin.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace FdmsBridgeAdmin.Controllers;

[ApiController]
[Route("api/v1/public/tokens")]
public class PublicTokenPurchaseController : ControllerBase
{
    private readonly IPayNowAdminService _payNowService;
    private readonly ITokenAdminService _tokenService;

    public PublicTokenPurchaseController(IPayNowAdminService payNowService, ITokenAdminService tokenService)
    {
        _payNowService = payNowService;
        _tokenService = tokenService;
    }

    [HttpGet("pricing")]
    public async Task<ActionResult> GetPublicTokenPricing(CancellationToken ct)
    {
        decimal subPrice = await _tokenService.GetPricePerTokenAsync(ct);
        decimal upgPrice = await _tokenService.GetPricePerDeviceUpgradeTokenAsync(ct);
        return Ok(new
        {
            pricePerTokenUSD = subPrice,
            pricePerDeviceUpgradeTokenUSD = upgPrice,
            unitDescription = "1 Subscription Token = 1 Device/Mo, 1 Device Upgrade Token = +1 Capacity Slot"
        });
    }

    [HttpPost("purchase")]
    public async Task<ActionResult<PayNowTokenCheckoutResponse>> InitiateTokenPurchase([FromBody] PayNowTokenCheckoutRequest req, CancellationToken ct)
    {
        try
        {
            var res = await _payNowService.InitiateTokenPurchaseAsync(req, ct);
            return Ok(res);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("redeem")]
    public async Task<ActionResult<ValidateAndRedeemVoucherResponse>> RedeemTokenMasterKey([FromBody] ValidateAndRedeemVoucherRequest req, CancellationToken ct)
    {
        var res = await _tokenService.RedeemVoucherAsync(req, ct);
        if (!res.Success) return BadRequest(res);
        return Ok(res);
    }
}
