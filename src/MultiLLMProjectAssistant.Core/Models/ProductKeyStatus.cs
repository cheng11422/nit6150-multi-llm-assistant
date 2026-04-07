using System.Text.Json.Serialization;

namespace MultiLLMProjectAssistant.Core.Models;

/// <summary>
/// Represents the validation result of a Product Key.
/// Used by UI to enable/disable features.
/// </summary>
public class ProductKeyStatus
{
    [JsonPropertyName("isValid")]
    public bool IsValid { get; set; }

    [JsonPropertyName("keyType")]
    public string KeyType { get; set; } = "unknown";
    // Possible values: "edu-trial", "professional", "expired", "invalid", "none"

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";

    [JsonPropertyName("canSendRequests")]
    public bool CanSendRequests { get; set; }

    [JsonPropertyName("maxRequestsPerDay")]
    public int MaxRequestsPerDay { get; set; }

    [JsonPropertyName("enabledProviders")]
    public List<string> EnabledProviders { get; set; } = new();
}
