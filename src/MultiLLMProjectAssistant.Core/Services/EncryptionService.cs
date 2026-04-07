using System.Security.Cryptography;
using System.Text;
using MultiLLMProjectAssistant.Core.Interfaces;

namespace MultiLLMProjectAssistant.Core.Services;

public class EncryptionService : IEncryptionService
{
    private const int SaltSize = 16;
    private const int IvSize = 16;
    private const int KeySize = 32;
    private const int Iterations = 100_000;

    private readonly string _passphrase;

    public EncryptionService()
    {
        _passphrase = Environment.MachineName + Environment.UserName;
    }

    public EncryptionService(string passphrase)
    {
        _passphrase = passphrase;
    }

    public byte[] Encrypt(string plainText)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var iv = RandomNumberGenerator.GetBytes(IvSize);

        using var keyDerivation = new Rfc2898DeriveBytes(
            _passphrase, salt, Iterations, HashAlgorithmName.SHA256);
        var key = keyDerivation.GetBytes(KeySize);

        using var aes = Aes.Create();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = key;
        aes.IV = iv;

        using var encryptor = aes.CreateEncryptor();
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var cipherText = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        // Format: [salt 16][IV 16][ciphertext]
        var result = new byte[SaltSize + IvSize + cipherText.Length];
        Buffer.BlockCopy(salt, 0, result, 0, SaltSize);
        Buffer.BlockCopy(iv, 0, result, SaltSize, IvSize);
        Buffer.BlockCopy(cipherText, 0, result, SaltSize + IvSize, cipherText.Length);

        return result;
    }

    public string Decrypt(byte[] cipherData)
    {
        if (cipherData.Length < SaltSize + IvSize + 1)
            throw new CryptographicException("Cipher data is too short.");

        var salt = new byte[SaltSize];
        var iv = new byte[IvSize];
        var cipherText = new byte[cipherData.Length - SaltSize - IvSize];

        Buffer.BlockCopy(cipherData, 0, salt, 0, SaltSize);
        Buffer.BlockCopy(cipherData, SaltSize, iv, 0, IvSize);
        Buffer.BlockCopy(cipherData, SaltSize + IvSize, cipherText, 0, cipherText.Length);

        using var keyDerivation = new Rfc2898DeriveBytes(
            _passphrase, salt, Iterations, HashAlgorithmName.SHA256);
        var key = keyDerivation.GetBytes(KeySize);

        using var aes = Aes.Create();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = key;
        aes.IV = iv;

        using var decryptor = aes.CreateDecryptor();
        var plainBytes = decryptor.TransformFinalBlock(cipherText, 0, cipherText.Length);

        return Encoding.UTF8.GetString(plainBytes);
    }
}
