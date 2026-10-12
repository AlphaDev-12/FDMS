using FdmsBridgeManager.Models.Dtos;

namespace FdmsBridgeManager.Services.Interfaces;

public interface IPlanService
{
    Task<List<PlanDto>> GetAllPlansAsync(bool includeInactive = false, CancellationToken ct = default);
    Task<PlanDto?> GetPlanByIdAsync(Guid id, CancellationToken ct = default);
    Task<PlanDto> CreatePlanAsync(UpsertPlanRequest request, CancellationToken ct = default);
    Task<PlanDto> UpdatePlanAsync(Guid id, UpsertPlanRequest request, CancellationToken ct = default);
    Task<bool> DeletePlanAsync(Guid id, CancellationToken ct = default);
}
