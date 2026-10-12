using FdmsBridgeAdmin.Models.Dtos;

namespace FdmsBridgeAdmin.Services.Interfaces;

public interface IUserManagementService
{
    Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken ct = default);
    Task<List<UserDto>> GetAllUsersAsync(CancellationToken ct = default);
    Task<UserDto> CreateUserAsync(CreateUserRequest request, CancellationToken ct = default);
    Task<UserDto> UpdateUserAsync(Guid id, UpdateUserRequest request, CancellationToken ct = default);
    Task<UserDto> UpdateUserPermissionsAsync(Guid id, UpdateUserPermissionsRequest request, CancellationToken ct = default);
    Task<bool> DeleteUserAsync(Guid id, CancellationToken ct = default);
    bool VerifyPermission(List<string> userPermissions, string requiredPermission);
}
