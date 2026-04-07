using MultiLLMProjectAssistant.Core.Interfaces;

namespace MultiLLMProjectAssistant.Core.Services;

public class FileEmbedder
{
    private readonly IFileStore _fileStore;

    public FileEmbedder(IFileStore fileStore)
    {
        _fileStore = fileStore;
    }

    public string EmbedFileContent(string projectKey, string fileId, int maxChars = 6000)
    {
        var metadata = _fileStore.GetFileById(projectKey, fileId)
            ?? throw new FileNotFoundException($"File not found: {fileId}");

        var content = _fileStore.GetFileContent(projectKey, fileId);
        var totalChars = content.Length;

        if (content.Length > maxChars)
        {
            content = content[..maxChars] + $"\n... [truncated, {totalChars} chars total]";
        }

        return $"--- File: {metadata.OriginalName} (ID: {fileId}) ---\n{content}\n--- End of File ---";
    }

    public string EmbedMultipleFiles(string projectKey, List<string> fileIds, int maxCharsPerFile = 6000)
    {
        var parts = fileIds.Select(id => EmbedFileContent(projectKey, id, maxCharsPerFile));
        return string.Join("\n\n", parts);
    }

    public string GetAttachmentSummary(string projectKey, string fileId)
    {
        var metadata = _fileStore.GetFileById(projectKey, fileId)
            ?? throw new FileNotFoundException($"File not found: {fileId}");

        return $"{metadata.OriginalName} ({metadata.FileType}, {metadata.FileSizeBytes} bytes, SHA-256: {metadata.Sha256Hash})";
    }
}
