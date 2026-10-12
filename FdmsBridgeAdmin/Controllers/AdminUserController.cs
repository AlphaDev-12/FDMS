using FdmsBridgeAdmin.Data;
using FdmsBridgeAdmin.Helpers;
using FdmsBridgeAdmin.Models.Dtos;
using FdmsBridgeAdmin.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace FdmsBridgeAdmin.Controllers;

[ApiController]
[Route("api/v1/admin")]
public class AdminUserController : ControllerBase
{
    private readonly IUserManagementService _userService;
    private readonly BridgeAdminDbContext _dbContext;

    public AdminUserController(IUserManagementService userService, BridgeAdminDbContext dbContext)
    {
        _userService = userService;
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

    [HttpGet("auth/me")]
    public async Task<ActionResult<UserDto>> GetCurrentUser(CancellationToken ct)
    {
        var (auth, err, user) = await HttpContext.ValidatePermissionAsync(_dbContext, _userService, "ViewDashboard");
        if (!auth || user == null) return Unauthorized(new { message = err });

        var users = await _userService.GetAllUsersAsync(ct);
        var activeUser = users.FirstOrDefault(u => u.Id == user.Id);
        if (activeUser == null) return NotFound(new { message = "User not found." });

        return Ok(activeUser);
    }

    [HttpGet("users")]
    public async Task<ActionResult<List<UserDto>>> GetAllUsers(CancellationToken ct)
    {
        var (auth, err, _) = await HttpContext.ValidatePermissionAsync(_dbContext, _userService, "ManageUsers");
        if (!auth) return StatusCode(StatusCodes.Status403Forbidden, new { message = err });

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
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
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

    [HttpPut("users/{id:guid}/permissions")]
    public async Task<ActionResult<UserDto>> UpdateUserPermissions(Guid id, [FromBody] UpdateUserPermissionsRequest request, CancellationToken ct)
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

    [HttpDelete("users/{id:guid}")]
    public async Task<ActionResult> DeleteUser(Guid id, CancellationToken ct)
    {
        var (auth, err, currentUser) = await HttpContext.ValidatePermissionAsync(_dbContext, _userService, "ManageUsers");
        if (!auth) return StatusCode(StatusCodes.Status403Forbidden, new { message = err });

        if (currentUser != null && currentUser.Id == id)
        {
            return BadRequest(new { message = "You cannot delete your own active administrator account." });
        }

        bool deleted = await _userService.DeleteUserAsync(id, ct);
        if (!deleted)
        {
            return NotFound(new { message = "User not found." });
        }

        return Ok(new { message = "User account removed successfully." });
    }
}
