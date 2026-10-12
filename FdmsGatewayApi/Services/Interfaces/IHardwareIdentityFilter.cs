namespace FdmsGatewayApi.Services.Interfaces;

public interface IHardwareIdentityFilter
{
    /// <summary>
    /// Verifies physical storage media descriptors (VID, PID, OEM Serial, Sector Count)
    /// against the signed license block at /app/secure_storage/license.sig.
    /// Halts process execution if hardware mismatch or tampering is detected.
    /// </summary>
    void VerifyCarrierMediaIntegrity();

    /// <summary>
    /// Scrapes current physical hardware descriptors from OS SCSI layer.
    /// </summary>
    string GetActiveHardwareSignature();
}
