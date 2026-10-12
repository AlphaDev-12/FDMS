using FdmsGatewayApi.Models.Dtos;

namespace FdmsGatewayApi.Services.Interfaces;

public interface IFdmsApiClient
{
    Task<VerifyTaxpayerInfoResponse?> VerifyTaxpayerInformationAsync(VerifyTaxpayerInfoRequest req, CancellationToken cancellationToken = default);
    Task<RegisterDeviceResponse?> RegisterDeviceAsync(RegisterDeviceRequest req, CancellationToken cancellationToken = default);
    Task<GetConfigResponse?> GetConfigAsync(GetConfigRequest req, string certPem, string privateKeyPem, CancellationToken cancellationToken = default);
    Task<GetStatusResponse?> GetStatusAsync(GetStatusRequest req, string certPem, string privateKeyPem, CancellationToken cancellationToken = default);
    Task<OpenDayResponse?> OpenDayAsync(OpenDayRequest req, string certPem, string privateKeyPem, CancellationToken cancellationToken = default);
    Task<SubmitReceiptResponse?> SubmitReceiptAsync(SubmitReceiptRequest req, string certPem, string privateKeyPem, CancellationToken cancellationToken = default);
    Task<CloseDayResponse?> CloseDayAsync(CloseDayRequest req, string certPem, string privateKeyPem, CancellationToken cancellationToken = default);
    Task<SubmitFileResponse?> SubmitFileAsync(SubmitFileRequest req, string certPem, string privateKeyPem, CancellationToken cancellationToken = default);
    Task<GetFileStatusResponse?> GetFileStatusAsync(GetFileStatusRequest req, string certPem, string privateKeyPem, CancellationToken cancellationToken = default);
    Task<GetStockListResponse?> GetStockListAsync(GetStockListRequest req, string certPem, string privateKeyPem, CancellationToken cancellationToken = default);
    Task<GetServerCertificateResponse?> GetServerCertificateAsync(CancellationToken cancellationToken = default);

    Task<PingResponse?> PingAsync(int deviceId, string certPem, string privateKeyPem, CancellationToken cancellationToken = default);

    // Section 4.14 User Management APIs
    Task<OpenUserSessionResponse?> OpenUserSessionAsync(OpenUserSessionRequest req, string certPem, string privateKeyPem, CancellationToken cancellationToken = default);
    Task<RegisterZimraUserResponse?> RegisterUserAsync(RegisterZimraUserRequest req, string certPem, string privateKeyPem, CancellationToken cancellationToken = default);
    Task<ZimraGenericUserResponse?> VerifyUserSecCodeAsync(VerifyUserSecCodeRequest req, string certPem, string privateKeyPem, CancellationToken cancellationToken = default);
    Task<ZimraGenericUserResponse?> ResendUserSecCodeAsync(ResendUserSecCodeRequest req, string certPem, string privateKeyPem, CancellationToken cancellationToken = default);
    Task<GetUsersListResponse?> GetUsersListAsync(GetUsersListRequest req, string certPem, string privateKeyPem, CancellationToken cancellationToken = default);
    Task<GetZimraUserResponse?> GetUserAsync(GetZimraUserRequest req, string certPem, string privateKeyPem, CancellationToken cancellationToken = default);
    Task<ZimraGenericUserResponse?> UpdateUserAsync(UpdateZimraUserRequest req, string certPem, string privateKeyPem, CancellationToken cancellationToken = default);
    Task<ZimraGenericUserResponse?> ChangeUserPasswordAsync(ChangeUserPasswordRequest req, string certPem, string privateKeyPem, CancellationToken cancellationToken = default);
    Task<ZimraGenericUserResponse?> SaveUserPINAsync(SaveUserPINRequest req, string certPem, string privateKeyPem, CancellationToken cancellationToken = default);
    Task<VerifyUserPINResponse?> VerifyUserPINAsync(VerifyUserPINRequest req, string certPem, string privateKeyPem, CancellationToken cancellationToken = default);
    Task<ZimraGenericUserResponse?> ResetUserPasswordAsync(ResetUserPasswordRequest req, string certPem, string privateKeyPem, CancellationToken cancellationToken = default);
    Task<InitResetUserPasswordResponse?> InitResetUserPasswordAsync(InitResetUserPasswordRequest req, string certPem, string privateKeyPem, CancellationToken cancellationToken = default);
    Task<ZimraGenericUserResponse?> VerifyResetUserPasswordSecCodeAsync(VerifyResetUserPasswordSecCodeRequest req, string certPem, string privateKeyPem, CancellationToken cancellationToken = default);
}

