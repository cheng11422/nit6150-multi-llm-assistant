using System.Text.Json;
using MultiLLMProjectAssistant.Core.Interfaces;

namespace MultiLLMProjectAssistant.Core.Services;

public class KeyStore : IKeyStore
{
    private readonly IEncryptionService _encryption;
    private readonly IProjectService _projectService;

    private static readonly HashSet<string> ValidProviders = new(StringComparer.OrdinalIgnoreCase)
    {
        "chatgpt", "gemini", "grok"
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public KeyStore(IEncryptionService encryption, IProjectService projectService)
    {
        _encryption = encryption;
        _projectService = projectService;
    }

    public void SaveKey(string projectKey, string provider, string apiKey)
    {
        ValidateProvider(provider);

        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("API key cannot be empty.", nameof(apiKey));

        var keys = LoadKeys(projectKey);
        keys[provider] = apiKey;
        SaveKeys(projectKey, keys);
    }

    public string? GetKey(string projectKey, string provider)
    {
        ValidateProvider(provider);
        var keys = LoadKeys(projectKey);
        return keys.TryGetValue(provider, out var value) ? value : null;
    }

    public bool DeleteKey(string projectKey, string provider)
    {
        ValidateProvider(provider);
        var keys = LoadKeys(projectKey);
        if (!keys.Remove(provider))
            return false;

        SaveKeys(projectKey, keys);
        return true;
    }

    public List<string> ListProviders(string projectKey)
    {
        var keys = LoadKeys(projectKey);
        return keys.Keys.ToList();
    }

    private static void ValidateProvider(string provider)
    {
        if (!ValidProviders.Contains(provider))
            throw new ArgumentException(
                $"Invalid provider: '{provider}'. Allowed: {string.Join(", ", ValidProviders)}",
                nameof(provider));
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
