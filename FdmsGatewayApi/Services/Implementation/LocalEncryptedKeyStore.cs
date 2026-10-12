using System.Security.Cryptography;
using System.Text;
using FdmsGatewayApi.Services.Interfaces;

namespace FdmsGatewayApi.Services.Implementation;

public class LocalEncryptedKeyStore : IEncryptedKeyStore
{
    private static readonly byte[] Salt = Encoding.UTF8.GetBytes("FDMS_ZIMRA_KEY_STORE_SALT_v1");
    private const int IterationsCount = 600_000; // OWASP recommended iteration count for HMAC-SHA256
    private const int LegacyIterationsCount = 10_000;

    public string EncryptKey(string plainTextPem, string passPhrase)
    {
        if (string.IsNullOrEmpty(plainTextPem)) return string.Empty;

        using var aes = Aes.Create();
        using var deriveBytes = new Rfc2898DeriveBytes(passPhrase, Salt, IterationsCount, HashAlgorithmName.SHA256);

        aes.Key = deriveBytes.GetBytes(32); // 256 bits
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor();
        byte[] plainBytes = Encoding.UTF8.GetBytes(plainTextPem);
        byte[] cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        // Prepend IV to cipher text
        byte[] result = new byte[aes.IV.Length + cipherBytes.Length];
        Array.Copy(aes.IV, 0, result, 0, aes.IV.Length);
        Array.Copy(cipherBytes, 0, result, aes.IV.Length, cipherBytes.Length);

        return Convert.ToBase64String(result);
    }

    public string DecryptKey(string cipherText, string passPhrase)
    {
        if (string.IsNullOrEmpty(cipherText)) return string.Empty;

        try
        {
            string decrypted = DecryptWithIterations(cipherText, passPhrase, IterationsCount);
            if (decrypted.StartsWith("-----BEGIN", StringComparison.Ordinal))
            {
                return decrypted;
            }
            return DecryptWithIterations(cipherText, passPhrase, LegacyIterationsCount);
        }
        catch (Exception)
        {
            // Fallback for legacy keys encrypted with 10,000 iterations
            return DecryptWithIterations(cipherText, passPhrase, LegacyIterationsCount);
        }
    }

    private static string DecryptWithIterations(string cipherText, string passPhrase, int iterations)
    {
        byte[] combinedBytes = Convert.FromBase64String(cipherText);

        using var aes = Aes.Create();
        using var deriveBytes = new Rfc2898DeriveBytes(passPhrase, Salt, iterations, HashAlgorithmName.SHA256);

        aes.Key = deriveBytes.GetBytes(32);

        byte[] iv = new byte[16];
        byte[] cipherBytes = new byte[combinedBytes.Length - 16];

        Array.Copy(combinedBytes, 0, iv, 0, 16);
        Array.Copy(combinedBytes, 16, cipherBytes, 0, cipherBytes.Length);

        aes.IV = iv;

        using var decryptor = aes.CreateDecryptor();
        byte[] plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);

        return Encoding.UTF8.GetString(plainBytes);
    }
}
