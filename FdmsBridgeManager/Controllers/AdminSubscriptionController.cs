using FdmsBridgeManager.Data;
using FdmsBridgeManager.Helpers;
using FdmsBridgeManager.Models.Dtos;
using FdmsBridgeManager.Models.Entities;
using FdmsBridgeManager.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FdmsBridgeManager.Controllers;

[ApiController]
[Route("api/v1/admin/subscriptions")]
public class AdminSubscriptionController : ControllerBase
{
    private readonly BridgeManagerDbContext _dbContext;
    private readonly IClientService _clientService;
    private readonly IPlanService _planService;
    private readonly ISubscriptionKeyGenerator _keyGenerator;
    private readonly IEmailService _emailService;
    private readonly IImmutableTokenLedgerService _ledgerService;
    private readonly IUserManagementService _userService;

    public AdminSubscriptionController(
        BridgeManagerDbContext dbContext,
        IClientService clientService,
        IPlanService planService,
        ISubscriptionKeyGenerator keyGenerator,
        IEmailService emailService,
        IImmutableTokenLedgerService ledgerService,
        IUserManagementService userService)
    {
        _dbContext = dbContext;
        _clientService = clientService;
        _planService = planService;
        _keyGenerator = keyGenerator;
        _emailService = emailService;
        _ledgerService = ledgerService;
        _userService = userService;
    }

    /// <summary>
    /// Returns client subscriptions with expiry dates, status calculations, payment tracking, search, and pagination.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<PagedSubscriptionResultDto>> GetAllSubscriptions(
        [FromQuery] string? tin = null,
        [FromQuery] string? search = null,
        [FromQuery] string? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken ct = default)
    {
        var query = _dbContext.Subscriptions.AsQueryable();

        if (!string.IsNullOrWhiteSpace(tin))
        {
            var tinTrimmed = tin.Trim();
            query = query.Where(s => s.TaxPayerTIN == tinTrimmed);
        }

        var subs = await query.OrderByDescending(s => s.ActivatedAt).ToListAsync(ct);
        var clientsMap = await _dbContext.Clients.Select(c => new { c.Id, c.CompanyName }).ToDictionaryAsync(c => c.Id, c => c.CompanyName, ct);
        var now = DateTime.UtcNow;

        var dtos = new List<SubscriptionDto>();
        foreach (var s in subs)
        {
            string? clientName = null;
            if (s.ClientId.HasValue) clientsMap.TryGetValue(s.ClientId.Value, out clientName);
            var graceEndsAt = s.ValidTo.AddDays(s.GracePeriodDays);

            string calcStatus;
            if (!s.IsActive) calcStatus = "Inactive";
            else if (now > graceEndsAt) calcStatus = "Expired";
            else if (now > s.ValidTo) calcStatus = "GracePeriod";
            else calcStatus = "Active";

            int daysRemaining = (int)Math.Max(0, Math.Ceiling((s.ValidTo - now).TotalDays));
            int durationMonths = (int)Math.Max(1, Math.Round((s.ValidTo - s.ValidFrom).TotalDays / 30.0));
            int tokensConsumed = (s.MaxDevices > 0 ? s.MaxDevices : 1) * durationMonths;

            dtos.Add(new SubscriptionDto
            {
                Id = s.Id,
                ClientId = s.ClientId,
                ClientName = clientName ?? s.TaxPayerTIN,
                TaxPayerTIN = s.TaxPayerTIN,
                ClientEmail = s.ClientEmail,
                PlanName = s.PlanName,
                BillingCycle = s.BillingCycle,
                Price = s.Price,
                MaxDevices = s.MaxDevices,
                TokensConsumed = tokensConsumed,
                MaskedApiKey = MaskKey(s.ApiKey),
                FullApiKey = s.ApiKey,
                ValidFrom = s.ValidFrom,
                ValidTo = s.ValidTo,
                DaysRemaining = daysRemaining,
                Status = calcStatus,
                IsActive = s.IsActive,
                PaymentStatus = s.PaymentStatus,
                PaymentMethod = s.PaymentMethod,
                TransactionRef = s.TransactionRef,
                ActivatedAt = s.ActivatedAt,
                ActivatedBy = s.ActivatedBy ?? "AdminUser"
            });
        }

        // Apply Search Filter across key fields
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            dtos = dtos.Where(d =>
                (d.ClientName != null && d.ClientName.ToLowerInvariant().Contains(term)) ||
                (d.TaxPayerTIN != null && d.TaxPayerTIN.ToLowerInvariant().Contains(term)) ||
                (d.ClientEmail != null && d.ClientEmail.ToLowerInvariant().Contains(term)) ||
                (d.PlanName != null && d.PlanName.ToLowerInvariant().Contains(term)) ||
                (d.TransactionRef != null && d.TransactionRef.ToLowerInvariant().Contains(term)) ||
                (d.FullApiKey != null && d.FullApiKey.ToLowerInvariant().Contains(term)) ||
                (d.MaskedApiKey != null && d.MaskedApiKey.ToLowerInvariant().Contains(term)) ||
                (d.PaymentMethod != null && d.PaymentMethod.ToLowerInvariant().Contains(term))
            ).ToList();
        }

        // Apply Status Filter
        if (!string.IsNullOrWhiteSpace(status) && !status.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            dtos = dtos.Where(d => d.Status.Equals(status, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 500) pageSize = 10;

        int totalItems = dtos.Count;
        int totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);
        if (totalPages == 0) totalPages = 1;

        var items = dtos.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return Ok(new PagedSubscriptionResultDto
        {
            Page = page,
            PageSize = pageSize,
            TotalItems = totalItems,
            TotalPages = totalPages,
            Items = items
        });
    }

    /// <summary>
    /// Processes a manual cash payment and generates a signed subscription API key immediately.
    /// Requires permission: GenerateCashKeys.
    /// </summary>
    [HttpPost("cash-payment")]
    public async Task<ActionResult<SubscriptionDto>> GenerateCashKey([FromBody] GenerateCashKeyRequest request, CancellationToken ct)
    {
        var (auth, err, authUser) = await HttpContext.ValidatePermissionAsync(_dbContext, _userService, "GenerateCashKeys");
        if (!auth) return StatusCode(StatusCodes.Status403Forbidden, new { message = err });

        if (string.IsNullOrWhiteSpace(request.TaxPayerTIN) || string.IsNullOrWhiteSpace(request.CompanyName) || string.IsNullOrWhiteSpace(request.DriveInstanceId))
        {
            return BadRequest(new { message = "Taxpayer TIN, Company Name, and physical Drive Instance ID are required for hardware-bound subscription keys." });
        }

        var plan = await _planService.GetPlanByIdAsync(request.PlanId, ct);
        if (plan == null)
        {
            return BadRequest(new { message = "Selected subscription plan not found." });
        }

        int deviceCount = request.DeviceCount > 0 ? request.DeviceCount : 1;
        decimal totalPrice = plan.Price * deviceCount;

        // 1. Ensure Client entity exists
        var client = await _clientService.CreateClientAsync(new UpsertClientRequest
        {
            CompanyName = request.CompanyName,
            TaxPayerTIN = request.TaxPayerTIN,
            ContactName = request.ContactName,
            Email = request.ClientEmail,
            Status = "Active"
        }, ct);

        // 2. Deduct Tokens from Immutable Ledger (DeviceCount * DurationMonths)
        try
        {
            await _ledgerService.DeductTokensAsync(deviceCount, plan.DurationMonths, client.TaxPayerTIN, User.Identity?.Name ?? "CashierAdmin", ct);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }

        // 3. Generate signed API Key (Bound to DriveInstanceId)
        var validFrom = DateTime.UtcNow;
        var validTo = validFrom.AddMonths(plan.DurationMonths);
        string driveId = request.DriveInstanceId.Trim().ToUpperInvariant();
        string tier = string.Equals(request.HardwareTier, "Cloud", StringComparison.OrdinalIgnoreCase) ? "Cloud" : (string.Equals(request.HardwareTier, "BranchBox", StringComparison.OrdinalIgnoreCase) ? "BranchBox" : "TillStick");
        string apiKey = _keyGenerator.GenerateSubscriptionKey(client.TaxPayerTIN, plan.PlanName, plan.DurationMonths, deviceCount, plan.GracePeriodDays, driveId, tier);

        string cashRef = string.IsNullOrWhiteSpace(request.CashReceiptRef) ? $"CASH-{Guid.NewGuid():N}".Substring(0, 12).ToUpper() : request.CashReceiptRef.Trim();

        var newSub = new SubscriptionEntity
        {
            Id = Guid.NewGuid(),
            ClientId = client.Id,
            PlanId = plan.Id,
            ApiKey = apiKey,
            PlanName = plan.PlanName,
            BillingCycle = plan.BillingCycle,
            Price = totalPrice,
            TaxPayerTIN = client.TaxPayerTIN,
            ClientEmail = request.ClientEmail,
            ValidFrom = validFrom,
            ValidTo = validTo,
            GracePeriodDays = plan.GracePeriodDays,
            MaxDevices = deviceCount,
            IsActive = true,
            PaymentStatus = "Paid",
            PaymentMethod = "Cash",
            TransactionRef = cashRef,
            ActivatedAt = validFrom,
            ActivatedBy = User.Identity?.Name ?? "CashierAdmin"
        };

        // 4. Record Cash Payment Transaction
        var tx = new PaymentTransactionEntity
        {
            Id = Guid.NewGuid(),
            SubscriptionId = newSub.Id,
            ClientId = client.Id,
            Amount = totalPrice,
            Currency = plan.Currency,
            PayNowReference = cashRef,
            PollUrl = "",
            Status = "Paid",
            PaymentType = "ManualCash",
            PayerEmail = request.ClientEmail,
            TaxPayerTIN = client.TaxPayerTIN,
            CreatedAt = validFrom,
            PaidAt = validFrom
        };

        _dbContext.Subscriptions.Add(newSub);
        _dbContext.PaymentTransactions.Add(tx);
        await _dbContext.SaveChangesAsync(ct);

        // 5. Send white-labeled email if requested
        if (request.SendEmailReceipt && !string.IsNullOrWhiteSpace(request.ClientEmail))
        {
            await _emailService.SendLicenseApiKeyEmailAsync(
                request.ClientEmail,
                client.CompanyName,
                client.TaxPayerTIN,
                plan.PlanName,
                apiKey,
                validTo,
                "Cash Payment",
                cashRef,
                ct);
        }

        return Ok(new SubscriptionDto
        {
            Id = newSub.Id,
            ClientId = client.Id,
            ClientName = client.CompanyName,
            TaxPayerTIN = client.TaxPayerTIN,
            ClientEmail = request.ClientEmail,
            PlanName = plan.PlanName,
            BillingCycle = plan.BillingCycle,
            Price = totalPrice,
            MaxDevices = deviceCount,
            TokensConsumed = deviceCount * plan.DurationMonths,
            MaskedApiKey = MaskKey(apiKey),
            FullApiKey = apiKey,
            ValidFrom = validFrom,
            ValidTo = validTo,
            DaysRemaining = (int)Math.Ceiling((validTo - validFrom).TotalDays),
            Status = "Active",
            IsActive = true,
            PaymentStatus = "Paid",
            PaymentMethod = "Cash",
            TransactionRef = cashRef,
            ActivatedAt = validFrom,
            ActivatedBy = newSub.ActivatedBy
        });
    }

    /// <summary>
    /// Retrieves current dynamic hardware tier pricing rules (TillStick, BranchBox, etc.).
    /// </summary>
    [HttpGet("tier-pricing")]
    public async Task<ActionResult<List<HardwareTierConfigDto>>> GetHardwareTierPrices(CancellationToken ct)
    {
        var tiers = await _dbContext.HardwareTierConfigs
            .OrderBy(t => t.HardCap)
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
    /// Updates hardware tier pricing rules ($ USD per slot and tokens per slot).
    /// </summary>
    [HttpPut("tier-pricing")]
    public async Task<ActionResult<List<HardwareTierConfigDto>>> UpdateHardwareTierPrices(
        [FromBody] UpdateHardwareTierPricesRequest request,
        CancellationToken ct)
    {
        var (auth, err, user) = await HttpContext.ValidatePermissionAsync(_dbContext, _userService, "ManagePlans");
        if (!auth)
        {
            (auth, err, user) = await HttpContext.ValidatePermissionAsync(_dbContext, _userService, "ManageSubscriptions");
            if (!auth) return StatusCode(StatusCodes.Status403Forbidden, new { message = err });
        }

        if (request == null || request.Tiers == null || request.Tiers.Count == 0)
        {
            return BadRequest(new { message = "At least one tier configuration must be supplied." });
        }

        foreach (var dto in request.Tiers)
        {
            var tier = await _dbContext.HardwareTierConfigs.FirstOrDefaultAsync(t => t.TierName.ToLower() == dto.TierName.ToLower(), ct);
            if (tier != null)
            {
                if (dto.PricePerSlotUSD > 0) tier.PricePerSlotUSD = dto.PricePerSlotUSD;
                if (dto.TokensPerSlot > 0) tier.TokensPerSlot = dto.TokensPerSlot;
                if (dto.HardCap > 0) tier.HardCap = dto.HardCap;
                if (dto.DefaultDevices > 0) tier.DefaultDevices = dto.DefaultDevices;
                if (!string.IsNullOrWhiteSpace(dto.Description)) tier.Description = dto.Description;
                tier.UpdatedAt = DateTime.UtcNow;
                tier.UpdatedBy = user?.Username ?? "Admin";
            }
        }

        await _dbContext.SaveChangesAsync(ct);
        return await GetHardwareTierPrices(ct);
    }

    /// <summary>
    /// Generates a hardware-bound Device Upgrade Key (FDMS-UPG-...) by deducting Device Upgrade Tokens based on hardware tier pricing.
    /// </summary>
    [HttpPost("generate-upgrade-key")]
    public async Task<ActionResult<GenerateDeviceUpgradeKeyResponse>> GenerateDeviceUpgradeKey(
        [FromBody] GenerateDeviceUpgradeKeyRequest request,
        CancellationToken ct)
    {
        var (auth, err, user) = await HttpContext.ValidatePermissionAsync(_dbContext, _userService, "ManageSubscriptions");
        if (!auth) return StatusCode(StatusCodes.Status403Forbidden, new { message = err });

        if (request == null || string.IsNullOrWhiteSpace(request.TaxPayerTIN) || string.IsNullOrWhiteSpace(request.DriveInstanceId))
        {
            return BadRequest(new { message = "TaxPayer TIN and Drive Instance ID are required." });
        }

        if (request.SlotsToAdd <= 0)
        {
            return BadRequest(new { message = "Slots to add must be at least 1." });
        }

        string tier = string.Equals(request.HardwareTier, "Cloud", StringComparison.OrdinalIgnoreCase) ? "Cloud" : (string.Equals(request.HardwareTier, "BranchBox", StringComparison.OrdinalIgnoreCase) ? "BranchBox" : "TillStick");

        var tierConfig = await _dbContext.HardwareTierConfigs
            .FirstOrDefaultAsync(t => t.TierName.ToLower() == tier.ToLower(), ct);

        int hardCap = tierConfig?.HardCap ?? (tier == "Cloud" ? 100 : (tier == "BranchBox" ? 25 : 5));
        decimal pricePerSlot = tierConfig?.PricePerSlotUSD ?? (tier == "Cloud" ? 300.00m : (tier == "BranchBox" ? 150.00m : 50.00m));
        int tokensPerSlot = tierConfig?.TokensPerSlot ?? (tier == "Cloud" ? 5 : (tier == "BranchBox" ? 3 : 1));

        int currentMax = request.CurrentMaxDevices > 0 ? request.CurrentMaxDevices : (tierConfig?.DefaultDevices ?? (tier == "Cloud" ? 10 : (tier == "BranchBox" ? 5 : 1)));
        int newMax = currentMax + request.SlotsToAdd;

        if (newMax > hardCap)
        {
            return BadRequest(new { message = $"Cannot exceed hardware tier hard cap of {hardCap} devices for {tier}." });
        }

        string cashRef = string.IsNullOrWhiteSpace(request.CashReceiptRef)
            ? $"UPG-CASH-{Guid.NewGuid():N}".Substring(0, 16).ToUpper()
            : request.CashReceiptRef.Trim();

        int tokensToDeduct = request.SlotsToAdd * tokensPerSlot;
        decimal pricePaid = request.SlotsToAdd * pricePerSlot;

        try
        {
            // Deduct Upgrade Tokens based on tier rate
            await _ledgerService.DeductUpgradeTokensAsync(tokensToDeduct, cashRef, user?.Username ?? "AdminUser", ct);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }

        string upgradeKey = _keyGenerator.GenerateDeviceUpgradeKey(
            request.TaxPayerTIN.Trim(),
            request.DriveInstanceId.Trim().ToUpperInvariant(),
            tier,
            request.SlotsToAdd,
            newMax,
            hardCap);

        return Ok(new GenerateDeviceUpgradeKeyResponse
        {
            UpgradeKey = upgradeKey,
            TaxPayerTIN = request.TaxPayerTIN.Trim(),
            DriveInstanceId = request.DriveInstanceId.Trim().ToUpperInvariant(),
            HardwareTier = tier,
            SlotsAdded = request.SlotsToAdd,
            NewMaxDevices = newMax,
            HardCap = hardCap,
            PricePerSlotUSD = pricePerSlot,
            PricePaidUSD = pricePaid,
            UpgradeTokensConsumed = tokensToDeduct
        });
    }

    /// <summary>
    /// Toggles or sets the active status of a subscription key.
    /// </summary>
    [HttpPut("{id:guid}/toggle-status")]
    public async Task<ActionResult> ToggleSubscriptionStatus(Guid id, CancellationToken ct)
    {
        var (auth, err, _) = await HttpContext.ValidatePermissionAsync(_dbContext, _userService, "ManageSubscriptions");
        if (!auth) return StatusCode(StatusCodes.Status403Forbidden, new { message = err });

        var sub = await _dbContext.Subscriptions.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (sub == null) return NotFound(new { message = "Subscription record not found." });

        sub.IsActive = !sub.IsActive;
        await _dbContext.SaveChangesAsync(ct);

        return Ok(new { id = sub.Id, isActive = sub.IsActive, message = $"Subscription is now {(sub.IsActive ? "Active" : "Inactive")}." });
    }

    private static string MaskKey(string key)
    {
        if (string.IsNullOrEmpty(key) || key.Length <= 14) return "FDMS-SUB-****";
        return key.Substring(0, 12) + "..." + key.Substring(key.Length - 6);
    }
}
