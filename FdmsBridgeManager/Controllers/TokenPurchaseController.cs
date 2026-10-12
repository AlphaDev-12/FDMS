using FdmsBridgeManager.Data;
using FdmsBridgeManager.Helpers;
using FdmsBridgeManager.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace FdmsBridgeManager.Controllers;

[ApiController]
[Route("api/v1/admin/tokens")]
public class TokenPurchaseController : ControllerBase
{
    private readonly ITokenPurchaseService _tokenPurchaseService;
    private readonly IImmutableTokenLedgerService _ledgerService;
    private readonly BridgeManagerDbContext _dbContext;
    private readonly IUserManagementService _userService;

    public TokenPurchaseController(
        ITokenPurchaseService tokenPurchaseService,
        IImmutableTokenLedgerService ledgerService,
        BridgeManagerDbContext dbContext,
        IUserManagementService userService)
    {
        _tokenPurchaseService = tokenPurchaseService;
        _ledgerService = ledgerService;
        _dbContext = dbContext;
        _userService = userService;
    }

    [HttpGet("status")]
    public async Task<ActionResult<TokenLedgerStatusDto>> GetLedgerStatus(CancellationToken ct)
    {
        var status = await _ledgerService.GetTokenStatusAsync(ct);
        return Ok(status);
    }

    [HttpGet("pricing")]
    public async Task<ActionResult<TokenPricingInfoDto>> GetTokenPricing(CancellationToken ct)
    {
        var info = await _tokenPurchaseService.GetTokenPricingInfoAsync(ct);
        return Ok(info);
    }

    [HttpPost("purchase-paynow")]
    public async Task<ActionResult<InitiateTokenPurchaseResponseDto>> InitiatePayNowPurchase([FromBody] PurchaseTokenRequest req, CancellationToken ct)
    {
        var (auth, err, _) = await HttpContext.ValidatePermissionAsync(_dbContext, _userService, "ManageSubscriptions");
        if (!auth) return StatusCode(StatusCodes.Status403Forbidden, new { message = err });

        try
        {
            var res = await _tokenPurchaseService.InitiateTokenPurchaseAsync(req.TokenCount, req.BuyerEmail, req.TokenType, ct);
            return Ok(res);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("redeem-master-key")]
    public async Task<ActionResult<RedeemTokenVoucherResponseDto>> RedeemMasterKey([FromBody] RedeemMasterKeyRequest req, CancellationToken ct)
    {
        var (auth, err, authUser) = await HttpContext.ValidatePermissionAsync(_dbContext, _userService, "ManageSubscriptions");
        if (!auth) return StatusCode(StatusCodes.Status403Forbidden, new { message = err });

        try
        {
            string user = authUser?.Username ?? User.Identity?.Name ?? "AdminUser";
            var res = await _tokenPurchaseService.RedeemTokenMasterKeyAsync(req.VoucherCode, user, ct);
            if (!res.Success) return BadRequest(res);
            return Ok(res);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    public class PurchaseTokenRequest
    {
        public int TokenCount { get; set; }
        public string TokenType { get; set; } = "Subscription"; // Subscription ($5) or DeviceUpgrade ($50)
        public string BuyerEmail { get; set; } = string.Empty;
    }

    public class RedeemMasterKeyRequest
    {
        public string VoucherCode { get; set; } = string.Empty;
    }
}
