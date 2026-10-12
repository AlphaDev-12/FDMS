using FdmsBridgeManager.Data;
using FdmsBridgeManager.Helpers;
using FdmsBridgeManager.Models.Dtos;
using FdmsBridgeManager.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace FdmsBridgeManager.Controllers;

[ApiController]
[Route("api/v1/admin/whitelabel")]
public class AdminWhiteLabelController : ControllerBase
{
    private readonly IWhiteLabelService _whiteLabelService;
    private readonly BridgeManagerDbContext _dbContext;
    private readonly IUserManagementService _userService;

    public AdminWhiteLabelController(
        IWhiteLabelService whiteLabelService,
        BridgeManagerDbContext dbContext,
        IUserManagementService userService)
    {
        _whiteLabelService = whiteLabelService;
        _dbContext = dbContext;
        _userService = userService;
    }

    [HttpGet]
    public async Task<ActionResult<WhiteLabelConfigDto>> GetConfig(CancellationToken ct)
    {
        var config = await _whiteLabelService.GetConfigAsync(ct);
        return Ok(config);
    }

    [HttpPut]
    public async Task<ActionResult<WhiteLabelConfigDto>> UpdateConfig([FromBody] UpdateWhiteLabelRequest request, CancellationToken ct)
    {
        var (auth, err, _) = await HttpContext.ValidatePermissionAsync(_dbContext, _userService, "ManageWhiteLabel");
        if (!auth) return StatusCode(StatusCodes.Status403Forbidden, new { message = err });

        var updated = await _whiteLabelService.UpdateConfigAsync(request, ct);
        return Ok(updated);
    }
}
