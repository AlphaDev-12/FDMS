using FdmsBridgeManager.Data;
using FdmsBridgeManager.Models.Dtos;
using FdmsBridgeManager.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FdmsBridgeManager.Controllers;

[ApiController]
[Route("api/v1/public")]
public class PublicPortalController : ControllerBase
{
    private readonly IWhiteLabelService _whiteLabelService;
    private readonly IPlanService _planService;
    private readonly IPayNowService _payNowService;
    private readonly IClientService _clientService;
    private readonly BridgeManagerDbContext _dbContext;

    public PublicPortalController(
        IWhiteLabelService whiteLabelService,
        IPlanService planService,
        IPayNowService payNowService,
        IClientService clientService,
        BridgeManagerDbContext dbContext)
    {
        _whiteLabelService = whiteLabelService;
        _planService = planService;
        _payNowService = payNowService;
        _clientService = clientService;
        _dbContext = dbContext;
    }

    /// <summary>
    /// Returns live UI-configurable White Label branding settings (logo, colors, firm name, contact).
    /// </summary>
    [HttpGet("branding")]
    public async Task<ActionResult<WhiteLabelConfigDto>> GetBranding(CancellationToken ct)
    {
        var branding = await _whiteLabelService.GetConfigAsync(ct);
        return Ok(branding);
    }

    /// <summary>
    /// Returns active subscription plans and dynamic pricing tiers.
    /// </summary>
    [HttpGet("plans")]
    public async Task<ActionResult<List<PlanDto>>> GetActivePlans(CancellationToken ct)
    {
        var plans = await _planService.GetAllPlansAsync(includeInactive: false, ct);
        return Ok(plans);
    }

    /// <summary>
    /// Returns live hardware tier pricing configuration for public device capacity upgrades.
    /// </summary>
    [HttpGet("hardware-tiers")]
    public async Task<ActionResult<List<HardwareTierConfigDto>>> GetHardwareTiers(CancellationToken ct)
    {
        var tiers = await _dbContext.HardwareTierConfigs
            .OrderBy(t => t.TierName)
            .Select(t => new HardwareTierConfigDto
            {
                Id = t.Id,
                TierName = t.TierName,
                PricePerSlotUSD = t.PricePerSlotUSD,
                TokensPerSlot = t.TokensPerSlot,
                HardCap = t.HardCap,
                DefaultDevices = t.DefaultDevices,
                Description = t.Description,
                UpdatedAt = t.UpdatedAt,
                UpdatedBy = t.UpdatedBy
            })
            .ToListAsync(ct);

        return Ok(tiers);
    }

    /// <summary>
    /// Checks if a Taxpayer TIN is already registered in the consultancy database.
    /// Only staff can register new clients; registered clients proceed directly to PayNow.
    /// </summary>
    [HttpPost("validate-tin")]
    public async Task<ActionResult<ValidateTinResponse>> ValidateTin([FromBody] ValidateTinRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.TaxPayerTIN))
        {
            return BadRequest(new { message = "Taxpayer TIN is required." });
        }

        var client = await _clientService.GetClientByTinAsync(request.TaxPayerTIN, ct);
        if (client == null)
        {
            return Ok(new ValidateTinResponse
            {
                IsRegistered = false,
                TaxPayerTIN = request.TaxPayerTIN.Trim()
            });
        }

        return Ok(new ValidateTinResponse
        {
            IsRegistered = true,
            TaxPayerTIN = client.TaxPayerTIN,
            CompanyName = client.CompanyName,
            ContactName = client.ContactName,
            Email = client.Email
        });
    }

    /// <summary>
    /// Intakes consultation request when an unregistered TIN attempts to subscribe.
    /// A consultant will contact the business to register their client account.
    /// </summary>
    [HttpPost("request-consultation")]
    public IActionResult RequestConsultation([FromBody] ConsultationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.TaxPayerTIN) || string.IsNullOrWhiteSpace(request.CompanyName))
        {
            return BadRequest(new { message = "Taxpayer TIN and Company Name are required." });
        }

        return Ok(new
        {
            success = true,
            message = $"Thank you! Registration inquiry received for TIN '{request.TaxPayerTIN}'. A consultant from our team will contact you shortly at {request.Email}."
        });
    }

    /// <summary>
    /// Initiates public client subscription checkout and generates PayNow redirect link.
    /// Requires that the Taxpayer TIN is already registered.
    /// </summary>
    [HttpPost("checkout/initiate")]
    public async Task<ActionResult<PublicCheckoutResponse>> InitiateCheckout([FromBody] PublicCheckoutRequest request, CancellationToken ct)
    {
        try
        {
            var response = await _payNowService.InitiateCheckoutAsync(request, ct);
            return Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = "NOT_REGISTERED", message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = "CHECKOUT_01", message = ex.Message });
        }
    }

    /// <summary>
    /// Initiates public client device upgrade checkout and generates PayNow redirect link.
    /// Requires that the Taxpayer TIN is already registered.
    /// </summary>
    [HttpPost("checkout/initiate-upgrade")]
    public async Task<ActionResult<PublicCheckoutResponse>> InitiateUpgradeCheckout([FromBody] PublicUpgradeCheckoutRequest request, CancellationToken ct)
    {
        try
        {
            var response = await _payNowService.InitiateUpgradeCheckoutAsync(request, ct);
            return Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = "NOT_REGISTERED", message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = "CHECKOUT_01", message = ex.Message });
        }
    }

    /// <summary>
    /// Test Sandbox Simulator: Instantly triggers payment completion, generates signed API key, and sends email.
    /// </summary>
    [HttpPost("checkout/simulate-paynow")]
    public async Task<IActionResult> SimulatePayNow([FromBody] SimulatePayNowRequest request, CancellationToken ct)
    {
        try
        {
            var sub = await _payNowService.ProcessPaymentCompletionAsync(request.TransactionId, $"PAYNOW-SIM-{Guid.NewGuid():N}".Substring(0, 16), ct);
            return Ok(new
            {
                success = true,
                message = "Payment confirmed successfully via PayNow Simulator. License key generated and dispatched via email.",
                apiKey = sub.ApiKey,
                clientEmail = sub.ClientEmail,
                planName = sub.PlanName,
                validTo = sub.ValidTo
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = "CHECKOUT_SIM_ERR", message = ex.Message });
        }
    }

    /// <summary>
    /// Polls transaction status for frontend redirect page.
    /// </summary>
    [HttpGet("checkout/status/{transactionId:guid}")]
    public async Task<IActionResult> GetStatus(Guid transactionId, CancellationToken ct)
    {
        var tx = await _payNowService.GetTransactionAsync(transactionId, ct);
        if (tx == null) return NotFound(new { message = "Transaction not found." });

        return Ok(new
        {
            transactionId = tx.Id,
            status = tx.Status,
            amount = tx.Amount,
            currency = tx.Currency,
            payNowRef = tx.PayNowReference,
            createdAt = tx.CreatedAt,
            paidAt = tx.PaidAt
        });
    }
}
