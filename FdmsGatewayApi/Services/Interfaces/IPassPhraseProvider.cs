namespace FdmsGatewayApi.Services.Interfaces;

public interface IPassPhraseProvider
{
    /// <summary>
    /// Get the encryption passphrase for device key store decryption.
    /// </summary>
    string GetPassPhrase();
}
