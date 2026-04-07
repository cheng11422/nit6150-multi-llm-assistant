using MultiLLMProjectAssistant.Core.Models;

namespace MultiLLMProjectAssistant.Core.Interfaces;

public interface IFileStore
{
    Task<FileMetadata> ImportFileAsync(string projectKey, string sourceFilePath);
    List<FileMetadata> GetFileList(string projectKey);
    string GetFileContent(string projectKey, string fileId);
    FileMetadata? GetFileById(string projectKey, string fileId);
    bool DeleteFile(string projectKey, string fileId);
}
