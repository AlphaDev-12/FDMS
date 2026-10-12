using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using FdmsGatewayApi.Services.Interfaces;

namespace FdmsGatewayApi.Services.Implementation;

public class DriveInstanceProvider : IDriveInstanceProvider
{
    private readonly ILogger<DriveInstanceProvider> _logger;

    public DriveInstanceProvider(ILogger<DriveInstanceProvider> logger)
    {
        _logger = logger;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool GetVolumeInformation(
        string rootPathName,
        StringBuilder? volumeNameBuffer,
        int volumeNameSize,
        out uint volumeSerialNumber,
        out uint maximumComponentLength,
        out uint fileSystemFlags,
        StringBuilder? fileSystemNameBuffer,
        int nFileSystemNameSize);

    public string GetCurrentDriveInstanceId()
    {
        try
        {
            string baseDir = AppContext.BaseDirectory;
            string driveRoot = Path.GetPathRoot(baseDir) ?? "C:\\";

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (GetVolumeInformation(driveRoot, null, 0, out uint serialNumber, out _, out _, null, 0))
                {
                    string driveId = $"DRV-{serialNumber:X8}";
                    return driveId;
                }
            }

            // Fallback for non-Windows or virtual paths
            var driveInfo = new DriveInfo(driveRoot);
            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes($"{driveInfo.Name}-{driveInfo.DriveFormat}"));
            return $"DRV-{Convert.ToHexString(hash).Substring(0, 8)}";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not determine physical volume serial number. Using fallback instance ID.");
            return "DRV-DEFAULT-INSTANCE-001";
        }
    }
}
