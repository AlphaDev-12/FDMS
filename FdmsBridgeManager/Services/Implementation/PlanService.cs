using FdmsBridgeManager.Data;
using FdmsBridgeManager.Models.Dtos;
using FdmsBridgeManager.Models.Entities;
using FdmsBridgeManager.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace FdmsBridgeManager.Services.Implementation;

public class PlanService : IPlanService
{
    private readonly BridgeManagerDbContext _dbContext;

    public PlanService(BridgeManagerDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<PlanDto>> GetAllPlansAsync(bool includeInactive = false, CancellationToken ct = default)
    {
        var query = _dbContext.SubscriptionPlans.AsQueryable();
        if (!includeInactive)
        {
            query = query.Where(p => p.IsActive);
        }

        var plansList = await query
            .OrderBy(p => p.SortOrder)
            .ToListAsync(ct);

        var plans = plansList
            .OrderBy(p => p.SortOrder)
            .ThenBy(p => p.Price)
            .ToList();

        return plans.Select(MapToDto).ToList();
    }

    public async Task<PlanDto?> GetPlanByIdAsync(Guid id, CancellationToken ct = default)
    {
        var plan = await _dbContext.SubscriptionPlans.FindAsync(new object[] { id }, ct);
        return plan != null ? MapToDto(plan) : null;
    }

    public async Task<PlanDto> CreatePlanAsync(UpsertPlanRequest request, CancellationToken ct = default)
    {
        var entity = new SubscriptionPlanEntity
        {
            Id = Guid.NewGuid(),
            PlanCode = string.IsNullOrWhiteSpace(request.PlanCode) ? $"PLAN_{Guid.NewGuid().ToString("N").Substring(0, 6).ToUpper()}" : request.PlanCode.Trim().ToUpper(),
            PlanName = request.PlanName.Trim(),
            BillingCycle = request.BillingCycle,
            Price = request.Price,
            Currency = string.IsNullOrWhiteSpace(request.Currency) ? "USD" : request.Currency.Trim().ToUpper(),
            DurationMonths = request.DurationMonths > 0 ? request.DurationMonths : 1,
            MaxDevices = request.MaxDevices > 0 ? request.MaxDevices : 10,
            GracePeriodDays = request.GracePeriodDays >= 0 ? request.GracePeriodDays : 7,
            Description = request.Description,
            IsActive = request.IsActive,
            SortOrder = request.SortOrder,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.SubscriptionPlans.Add(entity);
        await _dbContext.SaveChangesAsync(ct);
        return MapToDto(entity);
    }

    public async Task<PlanDto> UpdatePlanAsync(Guid id, UpsertPlanRequest request, CancellationToken ct = default)
    {
        var plan = await _dbContext.SubscriptionPlans.FindAsync(new object[] { id }, ct);
        if (plan == null)
        {
            throw new KeyNotFoundException($"Plan with ID {id} was not found.");
        }

        if (!string.IsNullOrWhiteSpace(request.PlanCode))
        {
            plan.PlanCode = request.PlanCode.Trim().ToUpper();
        }

        plan.PlanName = request.PlanName.Trim();
        plan.BillingCycle = request.BillingCycle;
        plan.Price = request.Price;
        plan.Currency = string.IsNullOrWhiteSpace(request.Currency) ? "USD" : request.Currency.Trim().ToUpper();
        plan.DurationMonths = request.DurationMonths > 0 ? request.DurationMonths : 1;
        plan.MaxDevices = request.MaxDevices > 0 ? request.MaxDevices : 10;
        plan.GracePeriodDays = request.GracePeriodDays >= 0 ? request.GracePeriodDays : 7;
        plan.Description = request.Description;
        plan.IsActive = request.IsActive;
        plan.SortOrder = request.SortOrder;

        await _dbContext.SaveChangesAsync(ct);
        return MapToDto(plan);
    }

    public async Task<bool> DeletePlanAsync(Guid id, CancellationToken ct = default)
    {
        var plan = await _dbContext.SubscriptionPlans.FindAsync(new object[] { id }, ct);
        if (plan == null) return false;

        _dbContext.SubscriptionPlans.Remove(plan);
        await _dbContext.SaveChangesAsync(ct);
        return true;
    }

    private static PlanDto MapToDto(SubscriptionPlanEntity p)
    {
        return new PlanDto
        {
            Id = p.Id,
            PlanCode = p.PlanCode,
            PlanName = p.PlanName,
            BillingCycle = p.BillingCycle,
            Price = p.Price,
            Currency = p.Currency,
            DurationMonths = p.DurationMonths,
            MaxDevices = p.MaxDevices,
            GracePeriodDays = p.GracePeriodDays,
            Description = p.Description,
            IsActive = p.IsActive,
            SortOrder = p.SortOrder
        };
    }
}
