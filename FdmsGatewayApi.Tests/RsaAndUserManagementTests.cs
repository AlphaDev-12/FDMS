using FdmsGatewayApi.Models.Dtos;
using FdmsGatewayApi.Services.Implementation;
using FdmsGatewayApi.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FdmsGatewayApi.Tests;

public class RsaAndUserManagementTests
{
    private readonly FdmsCryptoService _cryptoService;

    public RsaAndUserManagementTests()
    {
        _cryptoService = new FdmsCryptoService();
    }

    [Fact]
    public void GenerateKeyPairAndCsr_RSA2048_GeneratesValidRsaKeyPairAndCsr()
    {
        var (privateKeyPem, csrPem) = _cryptoService.GenerateKeyPairAndCsr(12345, "TEST-SERIAL-001", "RSA");

        Assert.NotNull(privateKeyPem);
        Assert.NotNull(csrPem);
        Assert.Contains("BEGIN RSA PRIVATE KEY", privateKeyPem);
        Assert.Contains("BEGIN CERTIFICATE REQUEST", csrPem);
    }

    [Fact]
    public void SignReceipt_RSA2048_SignsHashSuccessfully()
    {
        var (privateKeyPem, _) = _cryptoService.GenerateKeyPairAndCsr(12345, "TEST-SERIAL-001", "RSA");
        string dummyHash = Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData("test-receipt"u8.ToArray()));

        var sigResult = _cryptoService.SignReceipt(dummyHash, privateKeyPem);

        Assert.NotNull(sigResult);
        Assert.Equal(dummyHash, sigResult.Hash);
        Assert.False(string.IsNullOrWhiteSpace(sigResult.Signature));
    }

    [Fact]
    public async Task FdmsApiClient_SimulatedUserManagementEndpoints_ReturnValidResponses()
    {
        var httpClientFactoryMock = new Mock<IHttpClientFactory>();
        var healthCheckMock = new Mock<IZimraHealthCheck>();
        healthCheckMock.Setup(h => h.TargetEnvironment).Returns("Simulated");

        var configMock = new Mock<IConfiguration>();
        var client = new FdmsApiClient(httpClientFactoryMock.Object, healthCheckMock.Object, configMock.Object, NullLogger<FdmsApiClient>.Instance);

        // Test User Session
        var sessionResp = await client.OpenUserSessionAsync(new OpenUserSessionRequest { DeviceID = 10, Username = "admin", Password = "secret" }, "", "");
        Assert.NotNull(sessionResp);
        Assert.False(string.IsNullOrWhiteSpace(sessionResp.SessionToken));

        // Test User List
        var listResp = await client.GetUsersListAsync(new GetUsersListRequest { DeviceID = 10 }, "", "");
        Assert.NotNull(listResp);
        Assert.NotEmpty(listResp.Users);

        // Test Verify PIN
        var pinResp = await client.VerifyUserPINAsync(new VerifyUserPINRequest { DeviceID = 10, UserID = 101, Pin = "1234" }, "", "");
        Assert.NotNull(pinResp);
        Assert.True(pinResp.IsValid);

        // Test GetStockList
        var stockResp = await client.GetStockListAsync(new GetStockListRequest { DeviceID = 10, Offset = 0, Limit = 10 }, "", "");
        Assert.NotNull(stockResp);
        Assert.Equal(2, stockResp.Total);
        Assert.Equal(2, stockResp.Rows.Count);
    }
}
