using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FdmsBridgeManager.Services.Interfaces;

public interface ISubscriptionKeyGenerator
{
    string GenerateSubscriptionKey(string tin, string planName, int durationMonths, int maxDevices, int gracePeriodDays, string driveId, string tier = "TillStick");
    string GenerateDeviceUpgradeKey(string tin, string driveId, string tier, int slotsAdded, int newMaxDevices, int hardCap);
    bool VerifySubscriptionKey(string apiKey, out TokenPayload? payload);
}

public class TokenPayload
{
    public string Tin { get; set; } = string.Empty;
    public string Plan { get; set; } = string.Empty;
    public string DriveId { get; set; } = string.Empty;
    public string Tier { get; set; } = "TillStick";
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public int MaxDev { get; set; } = 10;
    public int GraceDays { get; set; } = 7;
    public string Nonce { get; set; } = string.Empty;
}

public class UpgradeTokenPayload
{
    public string KeyType { get; set; } = "DeviceUpgrade";
    public string Tin { get; set; } = string.Empty;
    public string DriveId { get; set; } = string.Empty;
    public string Tier { get; set; } = "TillStick";
    public int SlotsAdded { get; set; } = 1;
    public int NewMaxDevices { get; set; } = 0;
    public int HardCap { get; set; } = 5;
    public string Nonce { get; set; } = string.Empty;
}
