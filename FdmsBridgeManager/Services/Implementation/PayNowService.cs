using FdmsBridgeManager.Data;
using FdmsBridgeManager.Models.Dtos;
using FdmsBridgeManager.Models.Entities;
using FdmsBridgeManager.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace FdmsBridgeManager.Services.Implementation;

public class PayNowService : IPayNowService
{
    private readonly BridgeManagerDbContext _dbContext;
    private readonly IClientService _clientService;
    private readonly IPlanService _planService;
    private readonly ISubscriptionKeyGenerator _keyGenerator;
    private readonly IEmailService _emailService;
    private readonly IImmutableTokenLedgerService _ledgerService;
    private readonly ILogger<PayNowService> _logger;

    public PayNowService(
        BridgeManagerDbContext _dbContext,
        IClientService clientService,
        IPlanService planService,
        ISubscriptionKeyGenerator keyGenerator,
        IEmailService emailService,
        IImmutableTokenLedgerService ledgerService,
        ILogger<PayNowService> logger)
    {
        this._dbContext = _dbContext;
        _clientService = clientService;
        _planService = planService;
        _keyGenerator = keyGenerator;
        _emailService = emailService;
        _ledgerService = ledgerService;
        _logger = logger;
    }

    public async Task<PublicCheckoutResponse> InitiateCheckoutAsync(PublicCheckoutRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.TaxPayerTIN) || string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.DriveInstanceId))
        {
            throw new ArgumentException("Taxpayer TIN, Delivery Email, and physical Drive Instance ID are required for subscription keys.");
        }

        var plan = await _planService.GetPlanByIdAsync(request.PlanId, ct);
        if (plan == null || !plan.IsActive)
        {
            throw new ArgumentException("The selected subscription plan is invalid or inactive.");
        }

        // 1. Require that Client is ALREADY registered by consultancy staff
        var client = await _clientService.GetClientByTinAsync(request.TaxPayerTIN, ct);
        if (client == null)
        {
            throw new InvalidOperationException($"Taxpayer TIN '{request.TaxPayerTIN}' is not registered in our system. Client registration must be completed by consultancy staff before subscribing.");
        }

        int deviceCount = request.DeviceCount > 0 ? request.DeviceCount : 1;
        decimal totalPrice = plan.Price * deviceCount;

        // 2. Create Payment Transaction
        string payNowRef = $"PAYNOW-REF-{Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper()}";
        var transaction = new PaymentTransactionEntity
        {
            Id = Guid.NewGuid(),
            ClientId = client.Id,
            Amount = totalPrice,
            Currency = plan.Currency,
            PayNowReference = payNowRef,
            PollUrl = $"/api/v1/public/checkout/status/{payNowRef}",
            Status = "Pending",
            PaymentType = "OnlinePayNow",
            PayerEmail = request.Email,
            TaxPayerTIN = request.TaxPayerTIN,
            CreatedAt = DateTime.UtcNow
        };

        // 3. Create Pending Subscription
        var validFrom = DateTime.UtcNow;
        var validTo = validFrom.AddMonths(plan.DurationMonths);
        string driveId = request.DriveInstanceId.Trim().ToUpperInvariant();
        string tier = string.Equals(request.HardwareTier, "Cloud", StringComparison.OrdinalIgnoreCase) ? "Cloud" : (string.Equals(request.HardwareTier, "BranchBox", StringComparison.OrdinalIgnoreCase) ? "BranchBox" : "TillStick");
        string apiKey = _keyGenerator.GenerateSubscriptionKey(client.TaxPayerTIN, plan.PlanName, plan.DurationMonths, deviceCount, plan.GracePeriodDays, driveId, tier);

        var pendingSubscription = new SubscriptionEntity
        {
            Id = Guid.NewGuid(),
            ClientId = client.Id,
            PlanId = plan.Id,
            ApiKey = apiKey,
            PlanName = plan.PlanName,
            BillingCycle = plan.BillingCycle,
            Price = totalPrice,
            TaxPayerTIN = client.TaxPayerTIN,
            ClientEmail = request.Email,
            ValidFrom = validFrom,
            ValidTo = validTo,
            GracePeriodDays = plan.GracePeriodDays,
            MaxDevices = deviceCount,
            IsActive = false, // Pending payment completion
            PaymentStatus = "Pending",
            PaymentMethod = "PayNow",
            TransactionRef = payNowRef,
            ActivatedAt = DateTime.UtcNow,
            ActivatedBy = "PublicPayNowCheckout"
        };

        transaction.SubscriptionId = pendingSubscription.Id;
        _dbContext.Subscriptions.Add(pendingSubscription);
        _dbContext.PaymentTransactions.Add(transaction);
        await _dbContext.SaveChangesAsync(ct);

        // Standard PayNow redirect URL or Sandbox simulator link
        string redirectUrl = $"/portal.html?action=sandbox_paynow&txId={transaction.Id}&ref={payNowRef}";

        return new PublicCheckoutResponse
        {
            TransactionId = transaction.Id,
            PayNowRedirectUrl = redirectUrl,
            PayNowReference = payNowRef,
            Amount = totalPrice,
            Currency = plan.Currency,
            PlanName = plan.PlanName
        };
    }

    public async Task<PublicCheckoutResponse> InitiateUpgradeCheckoutAsync(PublicUpgradeCheckoutRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.TaxPayerTIN) || string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.DriveInstanceId))
        {
            throw new ArgumentException("Taxpayer TIN, Delivery Email, and physical Drive Instance ID are required for hardware capacity upgrades.");
        }

        var client = await _clientService.GetClientByTinAsync(request.TaxPayerTIN, ct);
        if (client == null)
        {
            throw new InvalidOperationException($"Taxpayer TIN '{request.TaxPayerTIN}' is not registered in our system. Client registration must be completed by consultancy staff before purchasing upgrades.");
        }

        int slotsToAdd = request.SlotsToAdd > 0 ? request.SlotsToAdd : 1;
        string hwTier = string.Equals(request.HardwareTier, "Cloud", StringComparison.OrdinalIgnoreCase) ? "Cloud" : (string.Equals(request.HardwareTier, "BranchBox", StringComparison.OrdinalIgnoreCase) ? "BranchBox" : "TillStick");

        var tierConfig = await _dbContext.HardwareTierConfigs
            .FirstOrDefaultAsync(t => t.TierName.ToLower() == hwTier.ToLower(), ct);

        decimal pricePerSlot = tierConfig?.PricePerSlotUSD ?? (hwTier == "Cloud" ? 300.00m : (hwTier == "BranchBox" ? 150.00m : 50.00m));
        decimal totalPrice = slotsToAdd * pricePerSlot;

        string payNowRef = $"PAYNOW-UPG-{Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper()}";
        var transaction = new PaymentTransactionEntity
        {
            Id = Guid.NewGuid(),
            ClientId = client.Id,
            Amount = totalPrice,
            Currency = "USD",
            PayNowReference = payNowRef,
            PollUrl = $"/api/v1/public/checkout/status/{payNowRef}",
            Status = "Pending",
            PaymentType = "DeviceUpgradePayNow",
            PayerEmail = request.Email,
            TaxPayerTIN = request.TaxPayerTIN,
            CreatedAt = DateTime.UtcNow
        };

        string driveId = string.IsNullOrWhiteSpace(request.DriveInstanceId) ? string.Empty : request.DriveInstanceId.Trim().ToUpperInvariant();
        int currentMaxDevices = request.CurrentMaxDevices > 0 ? request.CurrentMaxDevices : (hwTier == "Cloud" ? 10 : (hwTier == "BranchBox" ? 5 : 1));
        int hardCap = tierConfig?.HardCap ?? (hwTier == "Cloud" ? 100 : (hwTier == "BranchBox" ? 25 : 5));
        int newMaxDevices = currentMaxDevices + slotsToAdd;
        string upgradeKey = _keyGenerator.GenerateDeviceUpgradeKey(client.TaxPayerTIN, driveId, hwTier, slotsToAdd, newMaxDevices, hardCap);

        var pendingSubscription = new SubscriptionEntity
        {
            Id = Guid.NewGuid(),
            ClientId = client.Id,
            PlanId = null,
            ApiKey = upgradeKey,
            PlanName = $"Device Upgrade (+{slotsToAdd} Slot{(slotsToAdd > 1 ? "s" : "")} [{hwTier}])",
            BillingCycle = "Lifetime",
            Price = totalPrice,
            TaxPayerTIN = client.TaxPayerTIN,
            ClientEmail = request.Email,
            ValidFrom = DateTime.UtcNow,
            ValidTo = DateTime.UtcNow.AddYears(10),
            GracePeriodDays = 0,
            MaxDevices = slotsToAdd,
            IsActive = false,
            PaymentStatus = "Pending",
            PaymentMethod = "PayNow",
            TransactionRef = payNowRef,
            ActivatedAt = DateTime.UtcNow,
            ActivatedBy = "PublicPayNowUpgradeCheckout"
        };

        transaction.SubscriptionId = pendingSubscription.Id;
        _dbContext.Subscriptions.Add(pendingSubscription);
        _dbContext.PaymentTransactions.Add(transaction);
        await _dbContext.SaveChangesAsync(ct);

        string redirectUrl = $"/portal.html?action=sandbox_paynow&txId={transaction.Id}&ref={payNowRef}";

        return new PublicCheckoutResponse
        {
            TransactionId = transaction.Id,
            PayNowRedirectUrl = redirectUrl,
            PayNowReference = payNowRef,
            Amount = totalPrice,
            Currency = "USD",
            PlanName = pendingSubscription.PlanName
        };
    }

    public async Task<SubscriptionEntity> ProcessPaymentCompletionAsync(Guid transactionId, string payNowRef, CancellationToken ct = default)
    {
        var transaction = await _dbContext.PaymentTransactions.FirstOrDefaultAsync(t => t.Id == transactionId, ct);
        if (transaction == null)
        {
            throw new KeyNotFoundException("Payment transaction record not found.");
        }

        if (transaction.Status == "Paid")
        {
            var existingSub = await _dbContext.Subscriptions.FirstOrDefaultAsync(s => s.Id == transaction.SubscriptionId, ct);
            if (existingSub != null) return existingSub;
        }

        transaction.Status = "Paid";
        transaction.PaidAt = DateTime.UtcNow;
        transaction.PayNowReference = string.IsNullOrWhiteSpace(payNowRef) ? transaction.PayNowReference : payNowRef;

        var sub = await _dbContext.Subscriptions.FirstOrDefaultAsync(s => s.Id == transaction.SubscriptionId, ct);
        if (sub == null)
        {
            throw new InvalidOperationException("Associated subscription record missing.");
        }

        if (transaction.PaymentType == "DeviceUpgradePayNow")
        {
            string planName = sub.PlanName ?? "";
            string hwTier = planName.Contains("Cloud") ? "Cloud" : (planName.Contains("BranchBox") ? "BranchBox" : "TillStick");
            var tierConfig = await _dbContext.HardwareTierConfigs
                .FirstOrDefaultAsync(t => t.TierName.ToLower() == hwTier.ToLower(), ct);

            int tokensPerSlot = tierConfig?.TokensPerSlot ?? (hwTier == "Cloud" ? 5 : (hwTier == "BranchBox" ? 3 : 1));
            int slotsCount = sub.MaxDevices > 0 ? sub.MaxDevices : 1;
            int totalTokensToDeduct = slotsCount * tokensPerSlot;

            await _ledgerService.DeductUpgradeTokensAsync(totalTokensToDeduct, sub.TaxPayerTIN, "PublicPayNowUpgradeCheckout", ct);
        }
        else
        {
            var plan = await _planService.GetPlanByIdAsync(sub.PlanId ?? Guid.Empty, ct);
            int durationMonths = plan?.DurationMonths ?? 12;
            int maxDevices = sub.MaxDevices > 0 ? sub.MaxDevices : (plan?.MaxDevices ?? 1);

            await _ledgerService.DeductTokensAsync(maxDevices, durationMonths, sub.TaxPayerTIN, "PublicPayNowCheckout", ct);
        }

        sub.IsActive = true;
        sub.PaymentStatus = "Paid";
        sub.ActivatedAt = DateTime.UtcNow;

        var client = await _dbContext.Clients.FirstOrDefaultAsync(c => c.Id == sub.ClientId, ct);
        string clientName = client?.CompanyName ?? sub.TaxPayerTIN;

        await _dbContext.SaveChangesAsync(ct);

        await _emailService.SendLicenseApiKeyEmailAsync(
            sub.ClientEmail,
            clientName,
            sub.TaxPayerTIN,
            sub.PlanName ?? "",
            sub.ApiKey,
            sub.ValidTo,
            "PayNow Online",
            transaction.PayNowReference,
            ct);

        _logger.LogInformation("Successfully completed PayNow transaction {TxId} for TIN {TIN}. Issued key {Key}", transactionId, sub.TaxPayerTIN, sub.ApiKey);

        return sub;
    }

    public async Task<PaymentTransactionEntity?> GetTransactionAsync(Guid transactionId, CancellationToken ct = default)
    {
        return await _dbContext.PaymentTransactions.FirstOrDefaultAsync(t => t.Id == transactionId, ct);
    }
}
