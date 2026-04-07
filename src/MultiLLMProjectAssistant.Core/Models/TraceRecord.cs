using System.Text.Json.Serialization;

namespace MultiLLMProjectAssistant.Core.Models;

public class TraceRecord
{
    [JsonPropertyName("requestId")]
    public string RequestId { get; set; } = string.Empty;

    [JsonPropertyName("referencedFileIds")]
    public List<string> ReferencedFileIds { get; set; } = new();

    [JsonPropertyName("injectedMemoryIds")]
    public List<string> InjectedMemoryIds { get; set; } = new();

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }
}
