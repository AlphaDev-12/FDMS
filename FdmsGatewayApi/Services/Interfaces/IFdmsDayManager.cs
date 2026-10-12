using FdmsGatewayApi.Models.Entities;

namespace FdmsGatewayApi.Services.Interfaces;

public interface IFdmsDayManager
{
    Task<DeviceConfigEntity> OpenFiscalDayAsync(int deviceId, CancellationToken cancellationToken = default);
    Task<DeviceConfigEntity> CloseFiscalDayAsync(int deviceId, CancellationToken cancellationToken = default);
    Task<DeviceConfigEntity> SyncConfigAsync(int deviceId, CancellationToken cancellationToken = default);
    Task<DeviceConfigEntity> GetStatusAsync(int deviceId, CancellationToken cancellationToken = default);
}
