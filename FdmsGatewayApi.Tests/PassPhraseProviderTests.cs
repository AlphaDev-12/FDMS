using FdmsGatewayApi.Services.Implementation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FdmsGatewayApi.Tests;

public class PassPhraseProviderTests
{
    [Fact]
    public void GetPassPhrase_ReturnsConfiguredPassPhrase_WhenConfigured()
    {
        var inMemorySettings = new Dictionary<string, string?>
        {
            { "FdmsGateway:PassPhrase", "MyCustomProductionSecret123!" }
        };

        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        var provider = new PassPhraseProvider(config, NullLogger<PassPhraseProvider>.Instance);

        string result = provider.GetPassPhrase();

        Assert.Equal("MyCustomProductionSecret123!", result);
    }

    [Fact]
    public void GetPassPhrase_ReturnsDevDefault_WhenInDevelopmentEnvironment()
    {
        var inMemorySettings = new Dictionary<string, string?>
        {
            { "ASPNETCORE_ENVIRONMENT", "Development" }
        };

        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        var provider = new PassPhraseProvider(config, NullLogger<PassPhraseProvider>.Instance);

        string result = provider.GetPassPhrase();

        Assert.Equal("FDMS_ZIMRA_SECRET_KEY_2026", result);
    }

    [Fact]
    public void GetPassPhrase_ThrowsInvalidOperationException_WhenInProductionAndPassPhraseMissing()
    {
        var inMemorySettings = new Dictionary<string, string?>
        {
            { "ASPNETCORE_ENVIRONMENT", "Production" }
        };

        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        var provider = new PassPhraseProvider(config, NullLogger<PassPhraseProvider>.Instance);

        Assert.Throws<InvalidOperationException>(() => provider.GetPassPhrase());
    }
}
