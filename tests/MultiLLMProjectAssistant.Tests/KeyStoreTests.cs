using Xunit;
using MultiLLMProjectAssistant.Core.Services;

namespace MultiLLMProjectAssistant.Tests;

public class KeyStoreTests : IDisposable
{
    private readonly string _tempPath;
    private readonly ProjectService _projectService;
    private readonly EncryptionService _encryption;
    private readonly KeyStore _keyStore;
    private readonly string _projectKey;

    public KeyStoreTests()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), "MLLMKeyStoreTests_" + Guid.NewGuid().ToString("N"));
        _projectService = new ProjectService(_tempPath);
        _encryption = new EncryptionService("test-keystore-passphrase");
        _keyStore = new KeyStore(_encryption, _projectService);

        var info = _projectService.CreateProject("KeyStoreTestProject", "Testing KeyStore");
        _projectKey = info.ProjectKey;
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempPath))
            Directory.Delete(_tempPath, recursive: true);
    }

    [Fact]
    public void SaveAndGetKey_ReturnsCorrectValue()
    {
        _keyStore.SaveKey(_projectKey, "chatgpt", "sk-test-api-key-12345678901234567890");

        var result = _keyStore.GetKey(_projectKey, "chatgpt");

        Assert.Equal("sk-test-api-key-12345678901234567890", result);
    }

    [Fact]
    public void GetKey_NoKeySaved_ReturnsNull()
    {
        var result = _keyStore.GetKey(_projectKey, "gemini");
        Assert.Null(result);
    }

    [Fact]
    public void DeleteKey_ExistingKey_ReturnsTrueAndRemoves()
    {
        _keyStore.SaveKey(_projectKey, "gemini", "AIzaTestKey123456");

        var deleted = _keyStore.DeleteKey(_projectKey, "gemini");

        Assert.True(deleted);
        Assert.Null(_keyStore.GetKey(_projectKey, "gemini"));
    }

    [Fact]
    public void DeleteKey_NoKeySaved_ReturnsFalse()
    {
        var result = _keyStore.DeleteKey(_projectKey, "grok");
        Assert.False(result);
    }

    [Fact]
    public void ListProviders_ReturnsAllSavedProviders()
    {
        _keyStore.SaveKey(_projectKey, "chatgpt", "key1");
        _keyStore.SaveKey(_projectKey, "gemini", "key2");
        _keyStore.SaveKey(_projectKey, "grok", "key3");

        var providers = _keyStore.ListProviders(_projectKey);

        Assert.Equal(3, providers.Count);
        Assert.Contains("chatgpt", providers);
        Assert.Contains("gemini", providers);
        Assert.Contains("grok", providers);
    }

    [Fact]
    public void SaveKey_OverwriteExisting_UpdatesValue()
    {
        _keyStore.SaveKey(_projectKey, "chatgpt", "old-key");
        _keyStore.SaveKey(_projectKey, "chatgpt", "new-key");

        var result = _keyStore.GetKey(_projectKey, "chatgpt");
        Assert.Equal("new-key", result);
    }

    [Fact]
    public void SaveKey_InvalidProvider_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            _keyStore.SaveKey(_projectKey, "invalid_provider", "somekey"));
    }

    [Fact]
    public void SaveKey_EmptyApiKey_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            _keyStore.SaveKey(_projectKey, "chatgpt", ""));
    }

    [Fact]
    public void GetKey_InvalidProvider_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            _keyStore.GetKey(_projectKey, "openai"));
    }

    [Fact]
    public void KeysEnc_IsBinary_NoPainTextVisible()
    {
        _keyStore.SaveKey(_projectKey, "chatgpt", "sk-mysecretapikey1234567890abcdef");

        var keysEncPath = Path.Combine(
            _projectService.GetProjectPath(_projectKey), "keys.enc");
        var rawBytes = File.ReadAllBytes(keysEncPath);
        var rawText = System.Text.Encoding.UTF8.GetString(rawBytes);

        Assert.True(rawBytes.Length > 0, "keys.enc should not be empty");
        Assert.DoesNotContain("sk-mysecretapikey", rawText);
        Assert.DoesNotContain("chatgpt", rawText);
    }
}
