using FdmsBridgeManager.Data;
using FdmsBridgeManager.Helpers;
using FdmsBridgeManager.Models.Dtos;
using FdmsBridgeManager.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace FdmsBridgeManager.Controllers;

[ApiController]
[Route("api/v1/admin/clients")]
public class AdminClientController : ControllerBase
{
    private readonly IClientService _clientService;
    private readonly BridgeManagerDbContext _dbContext;
    private readonly IUserManagementService _userService;

    public AdminClientController(
        IClientService clientService,
        BridgeManagerDbContext dbContext,
        IUserManagementService userService)
    {
        _clientService = clientService;
        _dbContext = dbContext;
        _userService = userService;
    }

    [HttpGet]
    public async Task<ActionResult<PagedClientResultDto>> GetAllClients(
        [FromQuery] string? search = null,
        [FromQuery] string? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken ct = default)
    {
        var result = await _clientService.GetPagedClientsAsync(search, status, page, pageSize, ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ClientDto>> GetClientById(Guid id, CancellationToken ct)
    {
        var client = await _clientService.GetClientByIdAsync(id, ct);
        if (client == null) return NotFound(new { message = "Client record not found." });
        return Ok(client);
    }

    [HttpPost]
    public async Task<ActionResult<ClientDto>> CreateClient([FromBody] UpsertClientRequest request, CancellationToken ct)
    {
        var (auth, err, _) = await HttpContext.ValidatePermissionAsync(_dbContext, _userService, "ManageClients");
        if (!auth) return StatusCode(StatusCodes.Status403Forbidden, new { message = err });

        if (string.IsNullOrWhiteSpace(request.CompanyName) || string.IsNullOrWhiteSpace(request.TaxPayerTIN))
        {
            return BadRequest(new { message = "Company name and Taxpayer TIN are required." });
        }

        var client = await _clientService.CreateClientAsync(request, ct);
        return CreatedAtAction(nameof(GetClientById), new { id = client.Id }, client);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ClientDto>> UpdateClient(Guid id, [FromBody] UpsertClientRequest request, CancellationToken ct)
    {
        var (auth, err, _) = await HttpContext.ValidatePermissionAsync(_dbContext, _userService, "ManageClients");
        if (!auth) return StatusCode(StatusCodes.Status403Forbidden, new { message = err });

        try
        {
            var updated = await _clientService.UpdateClientAsync(id, request, ct);
            return Ok(updated);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }
}
