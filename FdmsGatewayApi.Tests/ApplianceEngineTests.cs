using FdmsGatewayApi.Services.Implementation;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace FdmsGatewayApi.Tests;

public class ApplianceEngineTests
{
    private readonly Mock<ILogger<CloudKmsVerificationEngine>> _kmsLoggerMock;
    private readonly Mock<ILogger<HardwareIdentityFilter>> _hwLoggerMock;

    public ApplianceEngineTests()
    {
        _kmsLoggerMock = new Mock<ILogger<CloudKmsVerificationEngine>>();
        _hwLoggerMock = new Mock<ILogger<HardwareIdentityFilter>>();
    }

    [Fact]
    public void ResolveVerificationPublicKey_FallbackToEmbeddedKey_WhenFileMissing()
    {
        string nonExistantPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "missing_key.pem");
        var engine = new CloudKmsVerificationEngine(_kmsLoggerMock.Object, nonExistantPath);

        string resolvedKey = engine.ResolveVerificationPublicKey();

        Assert.NotNull(resolvedKey);
        Assert.Contains("BEGIN PUBLIC KEY", resolvedKey);
        Assert.Contains("MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEA0C9B8A7F6E5D4C3B2A1E", resolvedKey);
    }

    [Fact]
    public void ResolveVerificationPublicKey_UsesExternalKey_WhenValidFileExists()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        string externalKeyPath = Path.Combine(tempDir, "cloud_kms_public.pem");
        string customKeyPem = "-----BEGIN PUBLIC KEY-----\nCUSTOM_EXTERNAL_KEY_BYTES\n-----END PUBLIC KEY-----";

        File.WriteAllText(externalKeyPath, customKeyPem);

        try
        {
            var engine = new CloudKmsVerificationEngine(_kmsLoggerMock.Object, externalKeyPath);
            string resolvedKey = engine.ResolveVerificationPublicKey();

            Assert.Equal(customKeyPem, resolvedKey);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public void VerifyCloudNodeAuthenticity_ParsesFallbackPayload_InDevMode()
    {
        var engine = new CloudKmsVerificationEngine(_kmsLoggerMock.Object);

        engine.VerifyCloudNodeAuthenticity("valid-dev-cloud-token-string");

        Assert.Equal(50, engine.MaxLicensedLanes);
        Assert.NotNull(engine.AllowedCloudKmsId);
    }

    [Fact]
    public void HardwareIdentityFilter_GeneratesAndVerifiesMediaIntegrity()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        string licencePath = Path.Combine(tempDir, "license.sig");
        string certPath = Path.Combine(tempDir, "license_public.pem");

        try
        {
            var filter = new HardwareIdentityFilter(_hwLoggerMock.Object, licencePath, certPath);

            // Execute verification probe (auto-provisions license signature on first boot)
            filter.VerifyCarrierMediaIntegrity();

            Assert.True(File.Exists(licencePath));
            string savedHash = File.ReadAllText(licencePath);
            Assert.False(string.IsNullOrWhiteSpace(savedHash));

            string activeSig = filter.GetActiveHardwareSignature();
            Assert.Equal(activeSig, savedHash.Trim());
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }
}
