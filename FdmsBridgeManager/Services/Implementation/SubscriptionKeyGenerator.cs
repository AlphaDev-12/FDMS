using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FdmsBridgeManager.Services.Interfaces;

namespace FdmsBridgeManager.Services.Implementation;

public class SubscriptionKeyGenerator : ISubscriptionKeyGenerator
{
    private readonly string _secretKey;
    private readonly ILogger<SubscriptionKeyGenerator> _logger;

    public SubscriptionKeyGenerator(IConfiguration config, ILogger<SubscriptionKeyGenerator> logger)
    {
        _logger = logger;
        _secretKey = config["Subscription:SecretKey"] ?? "FDMS-Bridge-Subscription-Master-Secret-2026-SecurityKey";
    }

    public string GenerateSubscriptionKey(string tin, string planName, int durationMonths, int maxDevices, int gracePeriodDays, string driveId, string tier = "TillStick")
    {
        if (string.IsNullOrWhiteSpace(driveId))
        {
            throw new ArgumentException("Drive Instance ID is required for hardware-bound subscription keys.", nameof(driveId));
        }
        var now = DateTime.UtcNow;
        var validFrom = now.AddMinutes(-5);
        var validTo = now.AddMonths(durationMonths > 0 ? durationMonths : 1);
        var graceDays = gracePeriodDays > 0 ? gracePeriodDays : 7;

        var payloadObj = new TokenPayload
        {
            Tin = tin,
            Plan = planName,
            DriveId = driveId,
            Tier = tier,
            From = validFrom.ToString("o"),
            To = validTo.ToString("o"),
            MaxDev = maxDevices,
            GraceDays = graceDays,
            Nonce = Guid.NewGuid().ToString("N")
        };

        string payloadJson = JsonSerializer.Serialize(payloadObj);
        string payloadB64 = Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson));
        string signature = ComputeHmacSignature(payloadB64, _secretKey);

        return $"FDMS-SUB-{payloadB64}.{signature}";
    }

    public string GenerateDeviceUpgradeKey(string tin, string driveId, string tier, int slotsAdded, int newMaxDevices, int hardCap)
    {
        var payloadObj = new UpgradeTokenPayload
        {
            KeyType = "DeviceUpgrade",
            Tin = tin,
            DriveId = driveId,
            Tier = tier,
            SlotsAdded = slotsAdded,
            NewMaxDevices = newMaxDevices,
            HardCap = hardCap,
            Nonce = Guid.NewGuid().ToString("N")
        };

        string payloadJson = JsonSerializer.Serialize(payloadObj);
        string payloadB64 = Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson));
        string signature = ComputeHmacSignature(payloadB64, _secretKey);

        return $"FDMS-UPG-{payloadB64}.{signature}";
    }

    public bool VerifySubscriptionKey(string apiKey, out TokenPayload? payload)
    {
        payload = null;
        try
        {
            string cleanToken = apiKey.Trim();
            if (cleanToken.StartsWith("FDMS-SUB-"))
            {
                cleanToken = cleanToken.Substring("FDMS-SUB-".Length);
            }

            var parts = cleanToken.Split('.');
            if (parts.Length != 2) return false;

            string payloadB64 = parts[0];
            string signature = parts[1];

            string expectedSig = ComputeHmacSignature(payloadB64, _secretKey);
            if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(signature),
                Encoding.UTF8.GetBytes(expectedSig)))
            {
                return false;
            }

            byte[] jsonBytes = Base64UrlDecode(payloadB64);
            string payloadJson = Encoding.UTF8.GetString(jsonBytes);
            payload = JsonSerializer.Deserialize<TokenPayload>(payloadJson);

            return payload != null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Subscription Key cryptographic validation error.");
            return false;
        }
    }

    private static string ComputeHmacSignature(string data, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        return Base64UrlEncode(hash);
    }

    private static string Base64UrlEncode(byte[] input)
    {
        return Convert.ToBase64String(input)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static byte[] Base64UrlDecode(string input)
    {
        string padded = input.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }
        return Convert.FromBase64String(padded);
    }
}
