using FdmsBridgeAdmin.Data;
using FdmsBridgeAdmin.Helpers;
using FdmsBridgeAdmin.Models.Dtos;
using FdmsBridgeAdmin.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace FdmsBridgeAdmin.Controllers;

[ApiController]
[Route("api/v1/admin/firms")]
public class FirmAdminController : ControllerBase
{
    private readonly IConsultancyFirmService _firmService;
    private readonly IUserManagementService _userService;
    private readonly BridgeAdminDbContext _dbContext;

    public FirmAdminController(
        IConsultancyFirmService firmService,
        IUserManagementService userService,
        BridgeAdminDbContext dbContext)
    {
        _firmService = firmService;
        _userService = userService;
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<ActionResult<List<ConsultancyFirmDto>>> GetAllFirms(CancellationToken ct)
    {
        var (auth, err, _) = await HttpContext.ValidatePermissionAsync(_dbContext, _userService, "ManageFirms");
        if (!auth) return StatusCode(StatusCodes.Status401Unauthorized, new { message = err });

        var list = await _firmService.GetAllFirmsAsync(ct);
        return Ok(list);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ConsultancyFirmDto>> GetFirmById(Guid id, CancellationToken ct)
    {
        var (auth, err, _) = await HttpContext.ValidatePermissionAsync(_dbContext, _userService, "ManageFirms");
        if (!auth) return StatusCode(StatusCodes.Status401Unauthorized, new { message = err });

        var firm = await _firmService.GetFirmByIdAsync(id, ct);
        if (firm == null) return NotFound(new { message = "Consultancy firm not found." });
        return Ok(firm);
    }

    [HttpPost]
    public async Task<ActionResult<ConsultancyFirmDto>> CreateFirm([FromBody] CreateConsultancyFirmRequest req, CancellationToken ct)
    {
        var (auth, err, _) = await HttpContext.ValidatePermissionAsync(_dbContext, _userService, "ManageFirms");
        if (!auth) return StatusCode(StatusCodes.Status403Forbidden, new { message = err });

        try
        {
            var firm = await _firmService.CreateFirmAsync(req, ct);
            return CreatedAtAction(nameof(GetFirmById), new { id = firm.Id }, firm);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("{id:guid}/toggle-status")]
    public async Task<ActionResult<ConsultancyFirmDto>> ToggleFirmStatus(Guid id, CancellationToken ct)
    {
        var (auth, err, _) = await HttpContext.ValidatePermissionAsync(_dbContext, _userService, "ManageFirms");
        if (!auth) return StatusCode(StatusCodes.Status403Forbidden, new { message = err });

        try
        {
            var firm = await _firmService.ToggleFirmStatusAsync(id, ct);
            return Ok(firm);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpPut("{id:guid}/unlimited-tier")]
    public async Task<ActionResult<ConsultancyFirmDto>> UpdateFirmUnlimitedTier(Guid id, [FromBody] UpdateFirmUnlimitedRequest req, CancellationToken ct)
    {
        var (auth, err, _) = await HttpContext.ValidatePermissionAsync(_dbContext, _userService, "ManageFirms");
        if (!auth) return StatusCode(StatusCodes.Status403Forbidden, new { message = err });

        try
        {
            var firm = await _firmService.UpdateFirmUnlimitedTierAsync(id, req, ct);
            return Ok(firm);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("/api/v1/public/tokens/firm-entitlement")]
    public async Task<ActionResult<FirmEntitlementDto>> GetFirmEntitlement([FromQuery] string firmApiKey, CancellationToken ct)
    {
        var entitlement = await _firmService.GetFirmEntitlementByApiKeyAsync(firmApiKey, ct);
        if (!entitlement.IsValid)
        {
            return NotFound(new { message = "Invalid or unknown Consultancy Firm API Key." });
        }
        return Ok(entitlement);
    }
}
