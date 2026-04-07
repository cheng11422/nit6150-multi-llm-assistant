using Xunit;
using MultiLLMProjectAssistant.Core.Services;

namespace MultiLLMProjectAssistant.Tests;

public class ProductKeyServiceTests : IDisposable
{
    private readonly string _tempPath;
    private readonly ProjectService _projectService;
    private readonly EncryptionService _encryption;
    private readonly ProductKeyService _productKeyService;
    private readonly string _projectKey;

    public ProductKeyServiceTests()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), "MLLMProductKeyTests_" + Guid.NewGuid().ToString("N"));
        _projectService = new ProjectService(_tempPath);
        _encryption = new EncryptionService("test-productkey-passphrase");
        _productKeyService = new ProductKeyService(_encryption, _projectService);

        var info = _projectService.CreateProject("ProductKeyTest", "Testing ProductKeyService");
        _projectKey = info.ProjectKey;
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempPath))
            Directory.Delete(_tempPath, recursive: true);
    }

    [Fact]
    public void SaveAndGetProductKey_ReturnsCorrectValue()
    {
        _productKeyService.SaveProductKey(_projectKey, "EDU-TRIAL-2025");

        var result = _productKeyService.GetProductKey(_projectKey);

        Assert.Equal("EDU-TRIAL-2025", result);
    }

    [Fact]
    public void ValidateProductKey_EduTrial_ReturnsTrue()
    {
        _productKeyService.SaveProductKey(_projectKey, "EDU-TRIAL-2025");

        Assert.True(_productKeyService.ValidateProductKey(_projectKey));
    }

    [Fact]
    public void ValidateProductKey_Professional_ReturnsTrue()
    {
        _productKeyService.SaveProductKey(_projectKey, "PRO-2025-ABCD");

        Assert.True(_productKeyService.ValidateProductKey(_projectKey));
    }

    [Fact]
    public void ValidateProductKey_Invalid_ReturnsFalse()
    {
        _productKeyService.SaveProductKey(_projectKey, "WRONG-KEY-1234");

        Assert.False(_productKeyService.ValidateProductKey(_projectKey));
    }

    [Fact]
    public void ValidateProductKey_NoKeySet_ReturnsFalse()
    {
        Assert.False(_productKeyService.ValidateProductKey(_projectKey));
    }

    [Fact]
    public void GetStatus_EduTrial_AllFeaturesEnabled()
    {
        _productKeyService.SaveProductKey(_projectKey, "EDU-TRIAL-2025");

        var status = _productKeyService.GetStatus(_projectKey);

        Assert.True(status.IsValid);
        Assert.Equal("edu-trial", status.KeyType);
        Assert.True(status.CanSendRequests);
        Assert.Equal(int.MaxValue, status.MaxRequestsPerDay);
        Assert.Contains("chatgpt", status.EnabledProviders);
        Assert.Contains("gemini", status.EnabledProviders);
        Assert.Contains("grok", status.EnabledProviders);
    }

    [Fact]
    public void GetStatus_NoKey_CannotSend()
    {
        var status = _productKeyService.GetStatus(_projectKey);

        Assert.False(status.IsValid);
        Assert.Equal("none", status.KeyType);
        Assert.False(status.CanSendRequests);
        Assert.Equal(0, status.MaxRequestsPerDay);
        Assert.Empty(status.EnabledProviders);
    }

    [Fact]
    public void IsFeatureEnabled_ValidKey_SendRequest_True()
    {
        _productKeyService.SaveProductKey(_projectKey, "EDU-TRIAL-2025");

        Assert.True(_productKeyService.IsFeatureEnabled(_projectKey, "send_request"));
        Assert.True(_productKeyService.IsFeatureEnabled(_projectKey, "memory_injection"));
    }

    [Fact]
    public void IsFeatureEnabled_InvalidKey_SendRequest_False()
    {
        _productKeyService.SaveProductKey(_projectKey, "WRONG-KEY");

        Assert.False(_productKeyService.IsFeatureEnabled(_projectKey, "send_request"));
        Assert.False(_productKeyService.IsFeatureEnabled(_projectKey, "memory_injection"));
    }

    [Fact]
    public void IsFeatureEnabled_InvalidKey_Export_StillTrue()
    {
        _productKeyService.SaveProductKey(_projectKey, "WRONG-KEY");

        Assert.True(_productKeyService.IsFeatureEnabled(_projectKey, "export"));
        Assert.True(_productKeyService.IsFeatureEnabled(_projectKey, "file_import"));
    }

    [Fact]
    public void ProductKey_StoredEncrypted_NoPlainText()
    {
        _productKeyService.SaveProductKey(_projectKey, "EDU-TRIAL-2025");

        var keysEncPath = Path.Combine(
            _projectService.GetProjectPath(_projectKey), "keys.enc");
        var rawBytes = File.ReadAllBytes(keysEncPath);
        var rawText = System.Text.Encoding.UTF8.GetString(rawBytes);

        Assert.True(rawBytes.Length > 0);
        Assert.DoesNotContain("EDU-TRIAL-2025", rawText);
        Assert.DoesNotContain("__productKey", rawText);
    }
}
