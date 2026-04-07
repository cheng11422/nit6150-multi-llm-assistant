using System.Text.Json;
using MultiLLMProjectAssistant.Core.Interfaces;
using MultiLLMProjectAssistant.Core.Models;

namespace MultiLLMProjectAssistant.Core.Services;

public class ProductKeyService : IProductKeyService
{
    private const string ProductKeyEntry = "__productKey";

    private static readonly List<string> AllProviders = new() { "chatgpt", "gemini", "grok" };

    private static readonly HashSet<string> AlwaysEnabledFeatures = new(StringComparer.OrdinalIgnoreCase)
    {
        "export", "file_import"
    };

    private static readonly HashSet<string> LicensedFeatures = new(StringComparer.OrdinalIgnoreCase)
    {
        "send_request", "memory_injection"
    };

    private readonly IEncryptionService _encryption;
    private readonly IProjectService _projectService;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public ProductKeyService(IEncryptionService encryption, IProjectService projectService)
    {
        _encryption = encryption;
        _projectService = projectService;
    }

    public void SaveProductKey(string projectKey, string productKey)
    {
        if (string.IsNullOrWhiteSpace(productKey))
            throw new ArgumentException("Product key cannot be empty.", nameof(productKey));

        var keys = LoadKeys(projectKey);
        keys[ProductKeyEntry] = productKey;
        SaveKeys(projectKey, keys);
    }

    public string? GetProductKey(string projectKey)
    {
        var keys = LoadKeys(projectKey);
        return keys.TryGetValue(ProductKeyEntry, out var value) ? value : null;
    }

    public bool ValidateProductKey(string projectKey)
    {
        var productKey = GetProductKey(projectKey);
        return IsKeyValid(productKey);
    }

    public bool IsFeatureEnabled(string projectKey, string featureName)
    {
        if (AlwaysEnabledFeatures.Contains(featureName))
            return true;

        if (LicensedFeatures.Contains(featureName))
            return ValidateProductKey(projectKey);

        return false;
    }

    public ProductKeyStatus GetStatus(string projectKey)
    {
        var productKey = GetProductKey(projectKey);

        if (string.IsNullOrEmpty(productKey))
        {
            return new ProductKeyStatus
            {
                IsValid = false,
                KeyType = "none",
                Message = "No product key set. Please enter a valid product key.",
                CanSendRequests = false,
                MaxRequestsPerDay = 0,
                EnabledProviders = new List<string>()
            };
        }

        if (productKey == "EDU-TRIAL-2025")
        {
            return new ProductKeyStatus
            {
                IsValid = true,
                KeyType = "edu-trial",
                Message = "Educational trial license active.",
                CanSendRequests = true,
                MaxRequestsPerDay = int.MaxValue,
                EnabledProviders = new List<string>(AllProviders)
            };
        }

        if (productKey.StartsWith("PRO-2025-", StringComparison.Ordinal))
        {
            return new ProductKeyStatus
            {
                IsValid = true,
                KeyType = "professional",
                Message = "Professional license active.",
                CanSendRequests = true,
                MaxRequestsPerDay = int.MaxValue,
                EnabledProviders = new List<string>(AllProviders)
            };
        }

        return new ProductKeyStatus
        {
            IsValid = false,
            KeyType = "invalid",
            Message = "Invalid product key.",
            CanSendRequests = false,
            MaxRequestsPerDay = 0,
            EnabledProviders = new List<string>()
        };
    }

    private static bool IsKeyValid(string? productKey)
    {
        if (string.IsNullOrEmpty(productKey))
            return false;

        if (productKey == "EDU-TRIAL-2025")
            return true;

        if (productKey.StartsWith("PRO-2025-", StringComparison.Ordinal))
            return true;

        return false;
    }

    private string GetKeysEncPath(string projectKey)
    {
        return Path.Combine(_projectService.GetProjectPath(projectKey), "keys.enc");
    }

    private Dictionary<string, string> LoadKeys(string projectKey)
    {
        var path = GetKeysEncPath(projectKey);

        if (!File.Exists(path))
            return new Dictionary<string, string>();

        var fileBytes = File.ReadAllBytes(path);
        if (fileBytes.Length == 0)
            return new Dictionary<string, string>();

        var json = _encryption.Decrypt(fileBytes);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions)
            ?? new Dictionary<string, string>();
    }

    private void SaveKeys(string projectKey, Dictionary<string, string> keys)
    {
        var json = JsonSerializer.Serialize(keys, JsonOptions);
        var encrypted = _encryption.Encrypt(json);
        File.WriteAllBytes(GetKeysEncPath(projectKey), encrypted);
    }
}
