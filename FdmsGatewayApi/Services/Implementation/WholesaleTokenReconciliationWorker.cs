using FdmsGatewayApi.Data;
using Microsoft.EntityFrameworkCore;

namespace FdmsGatewayApi.Services.Implementation;

public class WholesaleTokenReconciliationWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<WholesaleTokenReconciliationWorker> _logger;
    private readonly TimeSpan _checkInterval = TimeSpan.FromHours(1);

    public WholesaleTokenReconciliationWorker(
        IServiceProvider serviceProvider,
        ILogger<WholesaleTokenReconciliationWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[RECONCILIATION DAEMON] Multi-Tenant Wholesale Token Pro-Rata Reconciliation Worker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ReconcilePartnerTokenWalletsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RECONCILIATION ERROR] Failure during Wholesale Token Wallet Reconciliation execution cycle.");
            }

            await Task.Delay(_checkInterval, stoppingToken);
        }
    }

    public async Task ReconcilePartnerTokenWalletsAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FdmsDbContext>();

        var partners = await db.SharedPartnerProfiles
            .Include(p => p.TenantBusinessAccounts)
                .ThenInclude(a => a.ZimraDevices)
            .ToListAsync(cancellationToken);

        if (partners.Count == 0)
        {
            _logger.LogDebug("[RECONCILIATION DAEMON] No shared partner profiles currently provisioned.");
            return;
        }

        DateTime now = DateTime.UtcNow;

        foreach (var partner in partners)
        {
            // Calculate total active provisioned devices across all tenant accounts for this wholesale partner
            int activeDeviceCount = partner.TenantBusinessAccounts
                .SelectMany(a => a.ZimraDevices)
                .Count(d => d.IsActive);

            DateTime lastRun = partner.LastReconciledAt ?? now;
            double hoursElapsed = (now - lastRun).TotalHours;

            if (!partner.LastReconciledAt.HasValue)
            {
                partner.LastReconciledAt = now;
            }
            else if (hoursElapsed >= 0.05 && activeDeviceCount > 0)
            {
                // 1 device consumes 1 token per 30 days (720 hours) => 1.0 / 720.0 tokens per hour
                decimal hourlyRatePerDevice = 1.0m / 720.0m;
                decimal tokensToDeduct = activeDeviceCount * hourlyRatePerDevice * (decimal)hoursElapsed;

                partner.WholesaleTokenBalance -= tokensToDeduct;
                partner.LastReconciledAt = now;

                _logger.LogInformation(
                    "[PRO-RATA DEDUCTION] Partner '{PartnerKey}' (ID: {PartnerId}): Deducted {Tokens:F4} tokens across {Devices} devices ({Hours:F2} hrs elapsed). New Balance = {Balance:F2}.",
                    partner.PartnerKey, partner.PartnerId, tokensToDeduct, activeDeviceCount, hoursElapsed, partner.WholesaleTokenBalance);
            }

            if (partner.WholesaleTokenBalance <= 0m)
            {
                _logger.LogWarning(
                    "[BILLING ENFORCEMENT] Partner '{PartnerKey}' WholesaleTokenBalance depleted (Current: {Balance:F2}). Suspending downstream tenant business accounts.",
                    partner.PartnerKey, partner.WholesaleTokenBalance);

                // Suspend all downstream tenant accounts under this partner
                foreach (var tenant in partner.TenantBusinessAccounts)
                {
                    tenant.IsActive = false;
                }
            }
            else
            {
                // Re-activate downstream tenant accounts if balance is positive
                foreach (var tenant in partner.TenantBusinessAccounts)
                {
                    if (!tenant.IsActive)
                    {
                        tenant.IsActive = true;
                        _logger.LogInformation("[RE-ACTIVATION] Restored active status for tenant '{BusinessName}' (TenantKey: {TenantKey}).", tenant.BusinessName, tenant.TenantApiKey);
                    }
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("[RECONCILIATION COMPLETE] Successfully processed wholesale token wallet balance updates.");
    }
}
