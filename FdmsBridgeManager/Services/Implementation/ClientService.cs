using FdmsBridgeManager.Data;
using FdmsBridgeManager.Models.Dtos;
using FdmsBridgeManager.Models.Entities;
using FdmsBridgeManager.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace FdmsBridgeManager.Services.Implementation;

public class ClientService : IClientService
{
    private readonly BridgeManagerDbContext _dbContext;

    public ClientService(BridgeManagerDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<ClientDto>> GetAllClientsAsync(string? search = null, CancellationToken ct = default)
    {
        var query = _dbContext.Clients.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            string s = search.Trim().ToLower();
            query = query.Where(c => c.CompanyName.ToLower().Contains(s) ||
                                     c.TaxPayerTIN.ToLower().Contains(s) ||
                                     c.Email.ToLower().Contains(s) ||
                                     c.ContactName.ToLower().Contains(s));
        }

        var clients = await query.OrderByDescending(c => c.CreatedAt).ToListAsync(ct);

        var clientDtos = new List<ClientDto>();
        foreach (var c in clients)
        {
            int subCount = await GetActiveSubscriptionsCountAsync(c.Id, c.TaxPayerTIN, ct);
            clientDtos.Add(MapToDto(c, subCount));
        }

        return clientDtos;
    }

    public async Task<PagedClientResultDto> GetPagedClientsAsync(string? search = null, string? status = null, int page = 1, int pageSize = 10, CancellationToken ct = default)
    {
        var query = _dbContext.Clients.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            string s = search.Trim().ToLower();
            query = query.Where(c => c.CompanyName.ToLower().Contains(s) ||
                                     c.TaxPayerTIN.ToLower().Contains(s) ||
                                     c.Email.ToLower().Contains(s) ||
                                     c.ContactName.ToLower().Contains(s) ||
                                     c.PhoneNumber.ToLower().Contains(s));
        }

        if (!string.IsNullOrWhiteSpace(status) && !status.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(c => c.Status == status);
        }

        var allClients = await query.OrderByDescending(c => c.CreatedAt).ToListAsync(ct);

        var clientDtos = new List<ClientDto>();
        foreach (var c in allClients)
        {
            int subCount = await GetActiveSubscriptionsCountAsync(c.Id, c.TaxPayerTIN, ct);
            clientDtos.Add(MapToDto(c, subCount));
        }

        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 500) pageSize = 10;

        int totalItems = clientDtos.Count;
        int totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);
        if (totalPages == 0) totalPages = 1;

        var items = clientDtos.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return new PagedClientResultDto
        {
            Page = page,
            PageSize = pageSize,
            TotalItems = totalItems,
            TotalPages = totalPages,
            Items = items
        };
    }

    public async Task<ClientDto?> GetClientByIdAsync(Guid id, CancellationToken ct = default)
    {
        var client = await _dbContext.Clients.FindAsync(new object[] { id }, ct);
        if (client == null) return null;

        int subCount = await GetActiveSubscriptionsCountAsync(client.Id, client.TaxPayerTIN, ct);
        return MapToDto(client, subCount);
    }

    public async Task<ClientDto?> GetClientByTinAsync(string tin, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tin)) return null;
        string cleanTin = tin.Trim();
        var client = await _dbContext.Clients.FirstOrDefaultAsync(c => c.TaxPayerTIN == cleanTin, ct);
        if (client == null) return null;

        int subCount = await GetActiveSubscriptionsCountAsync(client.Id, client.TaxPayerTIN, ct);
        return MapToDto(client, subCount);
    }

    public async Task<ClientDto> CreateClientAsync(UpsertClientRequest request, CancellationToken ct = default)
    {
        string cleanTin = request.TaxPayerTIN.Trim();
        var existing = await _dbContext.Clients.FirstOrDefaultAsync(c => c.TaxPayerTIN == cleanTin, ct);
        if (existing != null)
        {
            int subCount = await GetActiveSubscriptionsCountAsync(existing.Id, existing.TaxPayerTIN, ct);
            return MapToDto(existing, subCount);
        }

        string apiKey = !string.IsNullOrWhiteSpace(request.TenantApiKey)
            ? request.TenantApiKey.Trim()
            : $"css_tenant_prod_{Guid.NewGuid():N}".Substring(0, 28);

        var newClient = new ClientEntity
        {
            Id = Guid.NewGuid(),
            CompanyName = request.CompanyName.Trim(),
            TaxPayerTIN = cleanTin,
            ContactName = request.ContactName.Trim(),
            Email = request.Email.Trim(),
            PhoneNumber = request.PhoneNumber.Trim(),
            Address = request.Address.Trim(),
            TenantApiKey = apiKey,
            Status = string.IsNullOrWhiteSpace(request.Status) ? "Active" : request.Status,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _dbContext.Clients.Add(newClient);
        await _dbContext.SaveChangesAsync(ct);
        return MapToDto(newClient, 0);
    }

    public async Task<ClientDto> UpdateClientAsync(Guid id, UpsertClientRequest request, CancellationToken ct = default)
    {
        var client = await _dbContext.Clients.FindAsync(new object[] { id }, ct);
        if (client == null)
        {
            throw new KeyNotFoundException($"Client with ID {id} was not found.");
        }

        client.CompanyName = request.CompanyName.Trim();
        client.ContactName = request.ContactName.Trim();
        client.Email = request.Email.Trim();
        client.PhoneNumber = request.PhoneNumber.Trim();
        client.Address = request.Address.Trim();
        if (!string.IsNullOrWhiteSpace(request.TenantApiKey))
        {
            client.TenantApiKey = request.TenantApiKey.Trim();
        }
        client.Status = request.Status;
        client.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(ct);
        int subCount = await GetActiveSubscriptionsCountAsync(client.Id, client.TaxPayerTIN, ct);
        return MapToDto(client, subCount);
    }

    private async Task<int> GetActiveSubscriptionsCountAsync(Guid clientId, string tin, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        // Self-healing: Ensure paid subscriptions within their valid date range are set to active
        var subsToRepair = await _dbContext.Subscriptions
            .Where(s => (s.ClientId == clientId || s.TaxPayerTIN == tin) && !s.IsActive && s.PaymentStatus == "Paid" && now <= s.ValidTo.AddDays(s.GracePeriodDays))
            .ToListAsync(ct);

        if (subsToRepair.Any())
        {
            foreach (var sub in subsToRepair)
            {
                sub.IsActive = true;
            }
            await _dbContext.SaveChangesAsync(ct);
        }

        return await _dbContext.Subscriptions.CountAsync(s =>
            (s.ClientId == clientId || s.TaxPayerTIN == tin) &&
            s.IsActive &&
            now <= s.ValidTo.AddDays(s.GracePeriodDays), ct);
    }

    private static ClientDto MapToDto(ClientEntity c, int activeSubsCount)
    {
        string apiKey = string.IsNullOrWhiteSpace(c.TenantApiKey)
            ? $"css_tenant_prod_{c.Id:N}".Substring(0, 28)
            : c.TenantApiKey;

        return new ClientDto
        {
            Id = c.Id,
            CompanyName = c.CompanyName,
            TaxPayerTIN = c.TaxPayerTIN,
            ContactName = c.ContactName,
            Email = c.Email,
            PhoneNumber = c.PhoneNumber,
            Address = c.Address,
            TenantApiKey = apiKey,
            Status = c.Status,
            CreatedAt = c.CreatedAt,
            ActiveSubscriptionsCount = activeSubsCount
        };
    }
}
