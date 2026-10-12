using FdmsBridgeAdmin.Data;
using FdmsBridgeAdmin.Helpers;
using FdmsBridgeAdmin.Models.Dtos;
using FdmsBridgeAdmin.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace FdmsBridgeAdmin.Controllers;

[ApiController]
[Route("api/v1/admin/tokens")]
public class TokenAdminController : ControllerBase
{
    private readonly ITokenAdminService _tokenService;
    private readonly IUserManagementService _userService;
    private readonly BridgeAdminDbContext _dbContext;

    public TokenAdminController(
        ITokenAdminService tokenService,
        IUserManagementService userService,
        BridgeAdminDbContext dbContext)
    {
        _tokenService = tokenService;
        _userService = userService;
        _dbContext = dbContext;
    }

    [HttpGet("price")]
    public async Task<ActionResult> GetPricePerToken(CancellationToken ct)
    {
        var (auth, err, _) = await HttpContext.ValidatePermissionAsync(_dbContext, _userService, "ManageTokens");
        if (!auth) return StatusCode(StatusCodes.Status401Unauthorized, new { message = err });

        decimal subPrice = await _tokenService.GetPricePerTokenAsync(ct);
        decimal upgPrice = await _tokenService.GetPricePerDeviceUpgradeTokenAsync(ct);
        return Ok(new 
        { 
            pricePerTokenUSD = subPrice, 
            pricePerDeviceUpgradeTokenUSD = upgPrice,
            unit = "1 Subscription Token = 1 Device / Month, 1 Upgrade Token = +1 Capacity Slot" 
        });
    }

    [HttpPost("price")]
    public async Task<ActionResult> UpdatePricePerToken([FromBody] UpdatePricePerTokenRequest req, CancellationToken ct)
    {
        var (auth, err, _) = await HttpContext.ValidatePermissionAsync(_dbContext, _userService, "ManageSettings");
        if (!auth) return StatusCode(StatusCodes.Status403Forbidden, new { message = err });

        try
        {
            decimal subUpdated = 0;
            decimal upgUpdated = 0;

            if (req.PricePerTokenUSD.HasValue && req.PricePerTokenUSD.Value > 0)
            {
                subUpdated = await _tokenService.UpdatePricePerTokenAsync(req.PricePerTokenUSD.Value, ct);
            }
            else
            {
                subUpdated = await _tokenService.GetPricePerTokenAsync(ct);
            }

            if (req.PricePerDeviceUpgradeTokenUSD.HasValue && req.PricePerDeviceUpgradeTokenUSD.Value > 0)
            {
                upgUpdated = await _tokenService.UpdatePricePerDeviceUpgradeTokenAsync(req.PricePerDeviceUpgradeTokenUSD.Value, ct);
            }
            else
            {
                upgUpdated = await _tokenService.GetPricePerDeviceUpgradeTokenAsync(ct);
            }

            return Ok(new 
            { 
                pricePerTokenUSD = subUpdated, 
                pricePerDeviceUpgradeTokenUSD = upgUpdated, 
                message = $"Token pricing updated successfully: Subscription=${subUpdated:F2} USD, Device Upgrade=${upgUpdated:F2} USD." 
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("generate")]
    public async Task<ActionResult<TokenVoucherDto>> GenerateTokenVoucher([FromBody] GenerateTokenVoucherRequest req, CancellationToken ct)
    {
        var (auth, err, user) = await HttpContext.ValidatePermissionAsync(_dbContext, _userService, "ManageTokens");
        if (!auth) return StatusCode(StatusCodes.Status403Forbidden, new { message = err });

        try
        {
            string adminUser = user?.Username ?? "SystemAdmin";
            var voucher = await _tokenService.GenerateTokenVoucherAsync(req, adminUser, ct);
            return Ok(voucher);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("vouchers")]
    public async Task<ActionResult<List<TokenVoucherDto>>> GetVouchers([FromQuery] Guid? firmId, CancellationToken ct)
    {
        var (auth, err, _) = await HttpContext.ValidatePermissionAsync(_dbContext, _userService, "ManageTokens");
        if (!auth) return StatusCode(StatusCodes.Status401Unauthorized, new { message = err });

        var list = await _tokenService.GetVouchersAsync(firmId, ct);
        return Ok(list);
    }

    [HttpPost("redeem")]
    public async Task<ActionResult<ValidateAndRedeemVoucherResponse>> RedeemVoucher([FromBody] ValidateAndRedeemVoucherRequest req, CancellationToken ct)
    {
        var res = await _tokenService.RedeemVoucherAsync(req, ct);
        if (!res.Success) return BadRequest(res);
        return Ok(res);
    }
}
