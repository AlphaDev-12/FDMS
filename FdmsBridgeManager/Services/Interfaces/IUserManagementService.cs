using FdmsBridgeManager.Models.Dtos;

namespace FdmsBridgeManager.Services.Interfaces;

public interface IUserManagementService
{
    Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken ct = default);
    Task<List<UserDto>> GetAllUsersAsync(CancellationToken ct = default);
    Task<UserDto> CreateUserAsync(CreateUserRequest request, CancellationToken ct = default);
    Task<UserDto> UpdateUserPermissionsAsync(Guid id, UpdateUserPermissionsRequest request, CancellationToken ct = default);
    Task<UserDto> UpdateUserAsync(Guid id, UpdateUserRequest request, CancellationToken ct = default);
    bool VerifyPermission(List<string> userPermissions, string requiredPermission);
}
