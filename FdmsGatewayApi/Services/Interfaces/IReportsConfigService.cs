using FdmsGatewayApi.Models.Dtos;

namespace FdmsGatewayApi.Services.Interfaces;

public interface IReportsConfigService
{
    ReportsConfigDto GetConfig();
    Task<ReportsConfigDto> UpdateStoragePathAsync(string newPath, CancellationToken cancellationToken = default);
    string GetResolvedStoragePath();
}
