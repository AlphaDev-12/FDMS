using FdmsGatewayApi.Services.Interfaces;

namespace FdmsGatewayApi.Services.Implementation;

public class PassPhraseProvider : IPassPhraseProvider
{
    private readonly IConfiguration _config;
    private readonly ILogger<PassPhraseProvider> _logger;

    public PassPhraseProvider(IConfiguration config, ILogger<PassPhraseProvider> logger)
    {
        _config = config;
        _logger = logger;
    }

    public string GetPassPhrase()
    {
        string? pass = _config["FdmsGateway:PassPhrase"];
        if (string.IsNullOrWhiteSpace(pass))
        {
            var env = _config["ASPNETCORE_ENVIRONMENT"] ?? "Production";
            if (string.Equals(env, "Development", StringComparison.OrdinalIgnoreCase))
            {
                return "FDMS_ZIMRA_SECRET_KEY_2026";
            }

            _logger.LogError("FdmsGateway:PassPhrase configuration is missing in Production environment.");
            throw new InvalidOperationException("FdmsGateway:PassPhrase configuration is missing. Key decryption requires a valid passphrase in production environments.");
        }
        return pass;
    }
}
