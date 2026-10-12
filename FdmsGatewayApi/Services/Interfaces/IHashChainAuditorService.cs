using FdmsGatewayApi.Models.Dtos;

namespace FdmsGatewayApi.Services.Interfaces;

public interface IHashChainAuditorService
{
    Task<HashChainVerificationResultDto> VerifyHashChainAsync(int deviceId, CancellationToken cancellationToken = default);
}
