namespace FdmsGatewayApi.Services.Interfaces;

public interface IEncryptedKeyStore
{
    string EncryptKey(string plainTextPem, string passPhrase);
    string DecryptKey(string cipherText, string passPhrase);
}
