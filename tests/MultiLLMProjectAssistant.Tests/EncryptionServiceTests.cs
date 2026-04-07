using System.Security.Cryptography;
using Xunit;
using MultiLLMProjectAssistant.Core.Services;

namespace MultiLLMProjectAssistant.Tests;

public class EncryptionServiceTests
{
    private readonly EncryptionService _service = new("test-passphrase-123");

    [Fact]
    public void EncryptDecrypt_Roundtrip_ReturnsOriginalText()
    {
        var original = "Hello, World!";
        var encrypted = _service.Encrypt(original);
        var decrypted = _service.Decrypt(encrypted);
        Assert.Equal(original, decrypted);
    }

    [Fact]
    public void EncryptDecrypt_EmptyString_ReturnsEmpty()
    {
        var encrypted = _service.Encrypt(string.Empty);
        var decrypted = _service.Decrypt(encrypted);
        Assert.Equal(string.Empty, decrypted);
    }

    [Fact]
    public void EncryptDecrypt_UnicodeString_ReturnsOriginal()
    {
        var original = "สวัสดีครับ 🎉🚀 こんにちは";
        var encrypted = _service.Encrypt(original);
        var decrypted = _service.Decrypt(encrypted);
        Assert.Equal(original, decrypted);
    }

    [Fact]
    public void EncryptDecrypt_LongString_ReturnsOriginal()
    {
        var original = new string('A', 10_000);
        var encrypted = _service.Encrypt(original);
        var decrypted = _service.Decrypt(encrypted);
        Assert.Equal(original, decrypted);
    }

    [Fact]
    public void Encrypt_SameTextTwice_ProducesDifferentCiphertext()
    {
        var text = "same text";
        var encrypted1 = _service.Encrypt(text);
        var encrypted2 = _service.Encrypt(text);
        Assert.NotEqual(encrypted1, encrypted2);
    }

    [Fact]
    public void Decrypt_WrongPassphrase_ThrowsCryptographicException()
    {
        var encrypted = _service.Encrypt("secret data");
        var wrongService = new EncryptionService("wrong-passphrase");

        Assert.Throws<CryptographicException>(() => wrongService.Decrypt(encrypted));
    }

    [Fact]
    public void Decrypt_CorruptedData_ThrowsCryptographicException()
    {
        var encrypted = _service.Encrypt("some data");

        // Corrupt a byte in the ciphertext portion
        encrypted[encrypted.Length - 1] ^= 0xFF;

        Assert.Throws<CryptographicException>(() => _service.Decrypt(encrypted));
    }
}
