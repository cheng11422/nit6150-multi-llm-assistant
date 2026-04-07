using System.Security.Cryptography;
using System.Text.Json;
using MultiLLMProjectAssistant.Core.Interfaces;
using MultiLLMProjectAssistant.Core.Models;

namespace MultiLLMProjectAssistant.Core.Services;

public class FileStoreService : IFileStore
{
    private readonly IProjectService _projectService;
    private readonly long _maxFileSizeBytes;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".md", ".txt", ".json", ".xml", ".html", ".css", ".js",
        ".py", ".java", ".cpp", ".h", ".log", ".csv", ".yaml", ".yml"
    };

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".md", ".txt", ".json", ".xml", ".html", ".css", ".js",
        ".py", ".java", ".cpp", ".h", ".log", ".csv", ".yaml", ".yml",
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".svg",
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".pptx",
        ".zip", ".tar", ".gz"
    };

    public FileStoreService(IProjectService projectService, long maxFileSizeBytes = 10 * 1024 * 1024)
    {
        _projectService = projectService;
        _maxFileSizeBytes = maxFileSizeBytes;
    }

    public async Task<FileMetadata> ImportFileAsync(string projectKey, string sourceFilePath)
    {
        if (!File.Exists(sourceFilePath))
            throw new FileNotFoundException("Source file not found.", sourceFilePath);

        var sourceInfo = new FileInfo(sourceFilePath);
        if (sourceInfo.Length > _maxFileSizeBytes)
            throw new InvalidOperationException(
                $"File size ({sourceInfo.Length} bytes) exceeds maximum allowed ({_maxFileSizeBytes} bytes).");

        var extension = Path.GetExtension(sourceFilePath);
        if (!string.IsNullOrEmpty(extension) && !AllowedExtensions.Contains(extension))
            throw new NotSupportedException($"File extension not allowed: {extension}");

        var filesDir = GetFilesDir(projectKey);
        Directory.CreateDirectory(filesDir);

        var index = LoadIndex(projectKey);
        var nextId = GenerateNextFileId(index);
        var originalName = Path.GetFileName(sourceFilePath);
        var destFileName = ResolveDestFileName(filesDir, nextId, originalName);
        var destPath = Path.Combine(filesDir, destFileName);

        await using (var sourceStream = File.OpenRead(sourceFilePath))
        await using (var destStream = File.Create(destPath))
        {
            await sourceStream.CopyToAsync(destStream);
        }

        var fileInfo = new FileInfo(destPath);
        var hash = ComputeSha256(destPath);

        var metadata = new FileMetadata
        {
            FileId = nextId,
            OriginalName = originalName,
            FileType = Path.GetExtension(originalName),
            FileSizeBytes = fileInfo.Length,
            Sha256Hash = hash,
            ImportedAt = DateTime.UtcNow,
            IsAttached = false
        };

        index.Add(metadata);
        SaveIndex(projectKey, index);

        return metadata;
    }

    public List<FileMetadata> GetFileList(string projectKey)
    {
        return LoadIndex(projectKey);
    }

    public string GetFileContent(string projectKey, string fileId)
    {
        var metadata = GetFileById(projectKey, fileId)
            ?? throw new FileNotFoundException($"File not found: {fileId}");

        if (!TextExtensions.Contains(metadata.FileType))
            throw new NotSupportedException(
                $"Cannot read binary file type: {metadata.FileType}");

        var filePath = GetStoredFilePath(projectKey, metadata);
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"File missing from disk: {fileId}", filePath);

        return File.ReadAllText(filePath);
    }

    public FileMetadata? GetFileById(string projectKey, string fileId)
    {
        var index = LoadIndex(projectKey);
        return index.FirstOrDefault(f => f.FileId == fileId);
    }

    public bool DeleteFile(string projectKey, string fileId)
    {
        var index = LoadIndex(projectKey);
        var metadata = index.FirstOrDefault(f => f.FileId == fileId);
        if (metadata is null)
            return false;

        var filePath = GetStoredFilePath(projectKey, metadata);
        if (File.Exists(filePath))
            File.Delete(filePath);

        index.Remove(metadata);
        SaveIndex(projectKey, index);

        return true;
    }

    private static string ResolveDestFileName(string filesDir, string fileId, string originalName)
    {
        var candidate = $"{fileId}_{originalName}";
        if (!File.Exists(Path.Combine(filesDir, candidate)))
            return candidate;

        var nameWithoutExt = Path.GetFileNameWithoutExtension(originalName);
        var ext = Path.GetExtension(originalName);
        var counter = 2;
        while (true)
        {
            candidate = $"{fileId}_{nameWithoutExt}_{counter}{ext}";
            if (!File.Exists(Path.Combine(filesDir, candidate)))
                return candidate;
            counter++;
        }
    }

    private string GetFilesDir(string projectKey)
    {
        return Path.Combine(_projectService.GetProjectPath(projectKey), "files");
    }

    private string GetIndexPath(string projectKey)
    {
        return Path.Combine(GetFilesDir(projectKey), "index.json");
    }

    private string GetStoredFilePath(string projectKey, FileMetadata metadata)
    {
        var fileName = $"{metadata.FileId}_{metadata.OriginalName}";
        return Path.Combine(GetFilesDir(projectKey), fileName);
    }

    private List<FileMetadata> LoadIndex(string projectKey)
    {
        var path = GetIndexPath(projectKey);
        if (!File.Exists(path))
            return new List<FileMetadata>();

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<List<FileMetadata>>(json, JsonOptions)
            ?? new List<FileMetadata>();
    }

    private void SaveIndex(string projectKey, List<FileMetadata> index)
    {
        var json = JsonSerializer.Serialize(index, JsonOptions);
        File.WriteAllText(GetIndexPath(projectKey), json);
    }

    private static string GenerateNextFileId(List<FileMetadata> index)
    {
        if (index.Count == 0)
            return "F001";

        var maxNum = index
            .Select(f => int.TryParse(f.FileId.AsSpan(1), out var n) ? n : 0)
            .Max();

        return $"F{maxNum + 1:D3}";
    }

    private static string ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var hashBytes = SHA256.HashData(stream);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
