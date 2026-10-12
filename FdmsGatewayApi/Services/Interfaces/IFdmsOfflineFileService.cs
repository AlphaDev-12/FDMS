using FdmsGatewayApi.Models.Dtos;
using FdmsGatewayApi.Models.Entities;

namespace FdmsGatewayApi.Services.Interfaces;

public interface IFdmsOfflineFileService
{
    /// <summary>
    /// Packages queued offline receipts into a base64 ZIMRA submitFile payload and transmits to ZIMRA FDMS Gateway.
    /// </summary>
    Task<OfflineFileEntity> PackageAndSubmitOfflineBatchAsync(int deviceId, int? fiscalDayNo = null, bool includeFooter = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Polls ZIMRA for processing status of previously submitted offline batch files via getFileStatus.
    /// </summary>
    Task<List<FileStatusItemDto>> CheckFileStatusAsync(int deviceId, string? operationId = null, DateTime? from = null, DateTime? till = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves local database log of submitted offline batch files for a device.
    /// </summary>
    Task<List<OfflineFileEntity>> GetLocalOfflineFileHistoryAsync(int deviceId, CancellationToken cancellationToken = default);
}
