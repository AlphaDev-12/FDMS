namespace FdmsGatewayApi.Models.Dtos;

public class SubscriptionStatusDto
{
    public string Status { get; set; } = "Unlicensed"; // Active, GracePeriod, Expired, Unlicensed
    public string PlanName { get; set; } = "Unlicensed";
    public string TaxPayerTIN { get; set; } = string.Empty;
    public DateTime ValidFrom { get; set; }
    public DateTime ValidTo { get; set; }
    public DateTime GracePeriodEndsAt { get; set; }
    public int DaysRemaining { get; set; }
    public int GraceDaysRemaining { get; set; }
    public string MaskedApiKey { get; set; } = string.Empty;
    public bool IsExpired { get; set; }
    public bool IsInGracePeriod { get; set; }
    public bool IsHardExpired { get; set; }
    public int MaxDevices { get; set; } = 10;
    public string DriveInstanceId { get; set; } = string.Empty;
    public string HardwareTier { get; set; } = "TillStick";
    public int HardCap { get; set; } = 5;
    public string? WarningMessage { get; set; }
}

public class ActivateSubscriptionRequest
{
    public string ApiKey { get; set; } = string.Empty;
}

public class ApplyDeviceUpgradeRequest
{
    public string UpgradeKey { get; set; } = string.Empty;
}

public class GenerateSubscriptionKeyRequest
{
    public string TaxPayerTIN { get; set; } = "2000123456";
    public string PlanName { get; set; } = "Annual Enterprise";
    public int DurationMonths { get; set; } = 12; // 1 or 12 or custom
    public int MaxDevices { get; set; } = 10;
    public int GracePeriodDays { get; set; } = 7;
    public string DriveInstanceId { get; set; } = string.Empty;
    public string HardwareTier { get; set; } = "TillStick";
}

public class GenerateSubscriptionKeyResponse
{
    public string ApiKey { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public DateTime ValidFrom { get; set; }
    public DateTime ValidTo { get; set; }
    public DateTime GracePeriodEndsAt { get; set; }
}
