using FdmsBridgeManager.Data;
using FdmsBridgeManager.Helpers;
using FdmsBridgeManager.Models.Dtos;
using FdmsBridgeManager.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace FdmsBridgeManager.Controllers;

[ApiController]
[Route("api/v1/admin/plans")]
public class AdminPlanController : ControllerBase
{
    private readonly IPlanService _planService;
    private readonly BridgeManagerDbContext _dbContext;
    private readonly IUserManagementService _userService;

    public AdminPlanController(
        IPlanService planService,
        BridgeManagerDbContext dbContext,
        IUserManagementService userService)
    {
        _planService = planService;
        _dbContext = dbContext;
        _userService = userService;
    }

    [HttpGet]
    public async Task<ActionResult<List<PlanDto>>> GetAllPlans(CancellationToken ct)
    {
        var plans = await _planService.GetAllPlansAsync(includeInactive: true, ct);
        return Ok(plans);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PlanDto>> GetPlanById(Guid id, CancellationToken ct)
    {
        var plan = await _planService.GetPlanByIdAsync(id, ct);
        if (plan == null) return NotFound(new { message = "Plan not found." });
        return Ok(plan);
    }

    [HttpPost]
    public async Task<ActionResult<PlanDto>> CreatePlan([FromBody] UpsertPlanRequest request, CancellationToken ct)
    {
        var (auth, err, _) = await HttpContext.ValidatePermissionAsync(_dbContext, _userService, "ManagePlans");
        if (!auth) return StatusCode(StatusCodes.Status403Forbidden, new { message = err });

        if (string.IsNullOrWhiteSpace(request.PlanName) || request.Price < 0)
        {
            return BadRequest(new { message = "Valid plan name and price are required." });
        }

        var plan = await _planService.CreatePlanAsync(request, ct);
        return CreatedAtAction(nameof(GetPlanById), new { id = plan.Id }, plan);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<PlanDto>> UpdatePlan(Guid id, [FromBody] UpsertPlanRequest request, CancellationToken ct)
    {
        var (auth, err, _) = await HttpContext.ValidatePermissionAsync(_dbContext, _userService, "ManagePlans");
        if (!auth) return StatusCode(StatusCodes.Status403Forbidden, new { message = err });

        try
        {
            var updated = await _planService.UpdatePlanAsync(id, request, ct);
            return Ok(updated);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeletePlan(Guid id, CancellationToken ct)
    {
        var (auth, err, _) = await HttpContext.ValidatePermissionAsync(_dbContext, _userService, "ManagePlans");
        if (!auth) return StatusCode(StatusCodes.Status403Forbidden, new { message = err });

        bool deleted = await _planService.DeletePlanAsync(id, ct);
        if (!deleted) return NotFound(new { message = "Plan not found." });
        return Ok(new { message = "Plan deleted successfully." });
    }
}
