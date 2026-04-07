using MultiLLMProjectAssistant.Core.Models;

namespace MultiLLMProjectAssistant.Core.Interfaces;

/// <summary>
/// Manages application-level Product Key (licensing simulation).
/// Brief Section 4.1: "Support an application-level Product Key
/// that can enable/disable features or enforce quotas (simulation is acceptable)."
/// </summary>
public interface IProductKeyService
{
    /// <summary>Save Product Key to encrypted storage (keys.enc).</summary>
    void SaveProductKey(string projectKey, string productKey);

    /// <summary>Get stored Product Key (or null if not set).</summary>
    string? GetProductKey(string projectKey);

    /// <summary>Validate the Product Key. Returns true if key is valid and not expired.</summary>
    bool ValidateProductKey(string projectKey);

    /// <summary>Check if a specific feature is enabled based on the current Product Key.</summary>
    bool IsFeatureEnabled(string projectKey, string featureName);

    /// <summary>Get license status summary (for UI display).</summary>
    ProductKeyStatus GetStatus(string projectKey);
}
