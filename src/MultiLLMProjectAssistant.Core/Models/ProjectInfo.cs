using System.Text.Json.Serialization;

namespace MultiLLMProjectAssistant.Core.Models;

public class ProjectInfo
{
    [JsonPropertyName("projectKey")]
    public string ProjectKey { get; set; } = string.Empty;

    [JsonPropertyName("projectName")]
    public string ProjectName { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("lastOpenedAt")]
    public DateTime LastOpenedAt { get; set; }

    [JsonPropertyName("providers")]
    public List<string> Providers { get; set; } = new();
}
