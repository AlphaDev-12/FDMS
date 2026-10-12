using FdmsBridgeAdmin.Models.Dtos;

namespace FdmsBridgeAdmin.Services.Interfaces;

public interface IConsultancyFirmService
{
    Task<ConsultancyFirmDto> CreateFirmAsync(CreateConsultancyFirmRequest req, CancellationToken ct = default);
    Task<List<ConsultancyFirmDto>> GetAllFirmsAsync(CancellationToken ct = default);
    Task<ConsultancyFirmDto?> GetFirmByIdAsync(Guid id, CancellationToken ct = default);
    Task<ConsultancyFirmDto?> GetFirmByApiKeyAsync(string apiKey, CancellationToken ct = default);
    Task<FirmEntitlementDto> GetFirmEntitlementByApiKeyAsync(string apiKey, CancellationToken ct = default);
    Task<ConsultancyFirmDto> ToggleFirmStatusAsync(Guid id, CancellationToken ct = default);
    Task<ConsultancyFirmDto> UpdateFirmUnlimitedTierAsync(Guid id, UpdateFirmUnlimitedRequest req, CancellationToken ct = default);
}
