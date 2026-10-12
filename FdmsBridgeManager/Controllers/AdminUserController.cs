using FdmsBridgeManager.Data;
using FdmsBridgeManager.Helpers;
using FdmsBridgeManager.Models.Dtos;
using FdmsBridgeManager.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace FdmsBridgeManager.Controllers;

[ApiController]
[Route("api/v1/admin")]
public class AdminUserController : ControllerBase
{
    private readonly IUserManagementService _userService;
    private readonly IEmailService _emailService;
    private readonly BridgeManagerDbContext _dbContext;

    public AdminUserController(
        IUserManagementService userService,
        IEmailService emailService,
        BridgeManagerDbContext dbContext)
    {
        _userService = userService;
        _emailService = emailService;
        _dbContext = dbContext;
    }

    [HttpPost("auth/login")]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        try
        {
            var res = await _userService.LoginAsync(request, ct);
            return Ok(res);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("users")]
    public async Task<ActionResult<List<UserDto>>> GetAllUsers(CancellationToken ct)
    {
        var users = await _userService.GetAllUsersAsync(ct);
        return Ok(users);
    }

    [HttpPost("users")]
    public async Task<ActionResult<UserDto>> CreateUser([FromBody] CreateUserRequest request, CancellationToken ct)
    {
        var (auth, err, _) = await HttpContext.ValidatePermissionAsync(_dbContext, _userService, "ManageUsers");
        if (!auth) return StatusCode(StatusCodes.Status403Forbidden, new { message = err });

        try
        {
            var user = await _userService.CreateUserAsync(request, ct);
            return CreatedAtAction(nameof(GetAllUsers), new { id = user.Id }, user);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("users/{id:guid}/permissions")]
    public async Task<ActionResult<UserDto>> UpdatePermissions(Guid id, [FromBody] UpdateUserPermissionsRequest request, CancellationToken ct)
    {
        var (auth, err, _) = await HttpContext.ValidatePermissionAsync(_dbContext, _userService, "ManageUsers");
        if (!auth) return StatusCode(StatusCodes.Status403Forbidden, new { message = err });

        try
        {
            var updated = await _userService.UpdateUserPermissionsAsync(id, request, ct);
            return Ok(updated);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpPut("users/{id:guid}")]
    public async Task<ActionResult<UserDto>> UpdateUser(Guid id, [FromBody] UpdateUserRequest request, CancellationToken ct)
    {
        var (auth, err, _) = await HttpContext.ValidatePermissionAsync(_dbContext, _userService, "ManageUsers");
        if (!auth) return StatusCode(StatusCodes.Status403Forbidden, new { message = err });

        try
        {
            var updated = await _userService.UpdateUserAsync(id, request, ct);
            return Ok(updated);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("email-logs")]
    public async Task<IActionResult> GetEmailLogs(CancellationToken ct)
    {
        var logs = await _emailService.GetEmailLogsAsync(ct);
        return Ok(logs.Select(l => new
        {
            l.Id,
            l.RecipientEmail,
            l.Subject,
            l.ApiKeySent,
            l.Status,
            l.SentAt,
            l.BodyHtml
        }));
    }
}
