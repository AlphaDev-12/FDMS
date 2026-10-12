using FdmsBridgeManager.Models.Dtos;

namespace FdmsBridgeManager.Services.Interfaces;

public interface IClientService
{
    Task<List<ClientDto>> GetAllClientsAsync(string? search = null, CancellationToken ct = default);
    Task<PagedClientResultDto> GetPagedClientsAsync(string? search = null, string? status = null, int page = 1, int pageSize = 10, CancellationToken ct = default);
    Task<ClientDto?> GetClientByIdAsync(Guid id, CancellationToken ct = default);
    Task<ClientDto?> GetClientByTinAsync(string tin, CancellationToken ct = default);
    Task<ClientDto> CreateClientAsync(UpsertClientRequest request, CancellationToken ct = default);
    Task<ClientDto> UpdateClientAsync(Guid id, UpsertClientRequest request, CancellationToken ct = default);
}
