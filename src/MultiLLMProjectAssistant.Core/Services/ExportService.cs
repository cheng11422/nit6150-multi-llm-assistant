using System.Text.Json;
using MultiLLMProjectAssistant.Core.Interfaces;

namespace MultiLLMProjectAssistant.Core.Services;

public class ExportService
{
    private readonly IProjectService _projectService;
    private readonly IFileStore _fileStore;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public ExportService(IProjectService projectService, IFileStore fileStore)
    {
        _projectService = projectService;
        _fileStore = fileStore;
    }

    public string ExportProjectSummary(string projectKey)
    {
        var projectPath = _projectService.GetProjectPath(projectKey);
        var projectJsonPath = Path.Combine(projectPath, "project.json");

        if (!File.Exists(projectJsonPath))
            throw new DirectoryNotFoundException($"Project not found: {projectKey}");

        var projectJson = RedactionFilter.Redact(File.ReadAllText(projectJsonPath));
        var doc = JsonSerializer.Deserialize<JsonElement>(projectJson);

        var projectName = doc.GetProperty("projectName").GetString() ?? "Unknown";
        var description = doc.GetProperty("description").GetString() ?? "";
        var createdAt = doc.GetProperty("createdAt").GetString() ?? "";

        var files = _fileStore.GetFileList(projectKey);
        var fileCount = files.Count;

        var memoryItemsPath = Path.Combine(projectPath, "memory", "items.json");
        var memoryCount = 0;
        if (File.Exists(memoryItemsPath))
        {
            var memoryJson = File.ReadAllText(memoryItemsPath);
            var memoryArray = JsonSerializer.Deserialize<JsonElement>(memoryJson);
            memoryCount = memoryArray.GetArrayLength();
        }

        var requestsDir = Path.Combine(projectPath, "requests");
        var requestCount = 0;
        if (Directory.Exists(requestsDir))
        {
            requestCount = Directory.GetFiles(requestsDir, "*_trace.json").Length;
        }

        return $"""
            === Project Summary ===
            Name: {projectName}
            Description: {description}
            Created: {createdAt}
            Files: {fileCount}
            Memory Notes: {memoryCount}
            Requests: {requestCount}
            """;
    }

    public string ExportRequestLog(string projectKey, string requestId)
    {
        var requestsDir = Path.Combine(_projectService.GetProjectPath(projectKey), "requests");
        var parts = new List<string>();

        var requestFile = Path.Combine(requestsDir, $"{requestId}_request.json");
        if (File.Exists(requestFile))
            parts.Add("=== Request ===\n" + RedactionFilter.Redact(File.ReadAllText(requestFile)));

        var responseFile = Path.Combine(requestsDir, $"{requestId}_response_normalised.json");
        if (File.Exists(responseFile))
            parts.Add("=== Response ===\n" + RedactionFilter.Redact(File.ReadAllText(responseFile)));

        var traceFile = Path.Combine(requestsDir, $"{requestId}_trace.json");
        if (File.Exists(traceFile))
            parts.Add("=== Trace ===\n" + RedactionFilter.Redact(File.ReadAllText(traceFile)));

        if (parts.Count == 0)
            throw new FileNotFoundException($"No request files found for {requestId}");

        return string.Join("\n\n", parts);
    }

    public void ExportAllToFolder(string projectKey, string outputPath)
    {
        var projectPath = _projectService.GetProjectPath(projectKey);

        if (!Directory.Exists(projectPath))
            throw new DirectoryNotFoundException($"Project not found: {projectKey}");

        Directory.CreateDirectory(outputPath);

        // Copy project.json (redacted)
        var projectJsonPath = Path.Combine(projectPath, "project.json");
        if (File.Exists(projectJsonPath))
        {
            var redacted = RedactionFilter.Redact(File.ReadAllText(projectJsonPath));
            File.WriteAllText(Path.Combine(outputPath, "project.json"), redacted);
        }

        // Copy files/index.json
        var indexPath = Path.Combine(projectPath, "files", "index.json");
        if (File.Exists(indexPath))
        {
            var filesDir = Path.Combine(outputPath, "files");
            Directory.CreateDirectory(filesDir);
            File.Copy(indexPath, Path.Combine(filesDir, "index.json"));
        }

        // Copy request/response JSON (redacted) and trace files
        var requestsDir = Path.Combine(projectPath, "requests");
        if (Directory.Exists(requestsDir))
        {
            var outRequestsDir = Path.Combine(outputPath, "requests");
            Directory.CreateDirectory(outRequestsDir);

            foreach (var file in Directory.GetFiles(requestsDir, "*.json"))
            {
                var fileName = Path.GetFileName(file);
                var content = RedactionFilter.Redact(File.ReadAllText(file));
                File.WriteAllText(Path.Combine(outRequestsDir, fileName), content);
            }
        }

        // Copy app.log (redacted)
        var logPath = Path.Combine(projectPath, "logs", "app.log");
        if (File.Exists(logPath))
        {
            var logsDir = Path.Combine(outputPath, "logs");
            Directory.CreateDirectory(logsDir);
            var redacted = RedactionFilter.Redact(File.ReadAllText(logPath));
            File.WriteAllText(Path.Combine(logsDir, "app.log"), redacted);
        }

        // keys.enc is NEVER copied
    }
}
