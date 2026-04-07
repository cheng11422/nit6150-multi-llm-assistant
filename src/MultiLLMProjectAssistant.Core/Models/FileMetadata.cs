using System.Text.Json.Serialization;

namespace MultiLLMProjectAssistant.Core.Models;

public class FileMetadata
{
    [JsonPropertyName("fileId")]
    public string FileId { get; set; } = string.Empty;

    [JsonPropertyName("originalName")]
    public string OriginalName { get; set; } = string.Empty;

    [JsonPropertyName("fileType")]
    public string FileType { get; set; } = string.Empty;

    [JsonPropertyName("fileSizeBytes")]
    public long FileSizeBytes { get; set; }

    [JsonPropertyName("sha256Hash")]
    public string Sha256Hash { get; set; } = string.Empty;

    [JsonPropertyName("importedAt")]
    public DateTime ImportedAt { get; set; }

    [JsonPropertyName("isAttached")]
    public bool IsAttached { get; set; }
}
