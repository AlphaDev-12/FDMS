using System.ComponentModel.DataAnnotations;

namespace FdmsGatewayApi.Models.Dtos;

public class TenantLoginRequestDto
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;

    public string? TenantApiKey { get; set; }
}

public class PublicTenantRegisterRequestDto
{
    [Required]
    public string TenantApiKey { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    [MinLength(6)]
    public string Password { get; set; } = string.Empty;

    public string? FullName { get; set; }
}

public class TenantLoginResponseDto
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? Token { get; set; }
    public int TenantId { get; set; }
    public string BusinessName { get; set; } = string.Empty;
    public TenantUserDto? User { get; set; }
}

public class TenantUserDto
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public List<string> Permissions { get; set; } = new();
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
}

public class CreateTenantUserRequestDto
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    [MinLength(6)]
    public string Password { get; set; } = string.Empty;

    [Required]
    public string FullName { get; set; } = string.Empty;

    public string Role { get; set; } = "TenantOperator"; // TenantAdmin, TenantOperator, TenantViewer

    public List<string>? Permissions { get; set; }

    public int? TargetTenantId { get; set; } // Optional if caller is PartnerAdmin, otherwise derived from session
}

public class UpdateTenantUserPermissionsDto
{
    public string Role { get; set; } = "TenantOperator";
    public List<string> Permissions { get; set; } = new();
    public bool? IsActive { get; set; }
}

public class TenantDashboardSummaryDto
{
    public int TenantId { get; set; }
    public string BusinessName { get; set; } = string.Empty;
    public int TotalDocuments { get; set; }
    public int TotalFiscalized { get; set; }
    public int TotalPending { get; set; }
    public int TotalFailed { get; set; }
    public decimal TodayUsdSales { get; set; }
    public decimal TodayZigSales { get; set; }
    public int RegisteredDevicesCount { get; set; }
    public List<TenantDeviceSummaryDto> Devices { get; set; } = new();
}

public class TenantDeviceSummaryDto
{
    public string ZimraDeviceId { get; set; } = string.Empty;
    public long LastSequenceNumber { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}
