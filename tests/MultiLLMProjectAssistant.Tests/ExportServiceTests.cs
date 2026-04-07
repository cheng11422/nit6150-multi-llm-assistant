using Xunit;
using MultiLLMProjectAssistant.Core.Models;
using MultiLLMProjectAssistant.Core.Services;

namespace MultiLLMProjectAssistant.Tests;

public class ExportServiceTests : IDisposable
{
    private readonly string _tempPath;
    private readonly ProjectService _projectService;
    private readonly FileStoreService _fileStore;
    private readonly ExportService _exportService;
    private readonly string _projectKey;

    public ExportServiceTests()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), "MLLMExportTests_" + Guid.NewGuid().ToString("N"));
        _projectService = new ProjectService(_tempPath);
        _fileStore = new FileStoreService(_projectService);
        _exportService = new ExportService(_projectService, _fileStore);

        var info = _projectService.CreateProject("ExportTest", "Testing ExportService");
        _projectKey = info.ProjectKey;
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempPath))
            Directory.Delete(_tempPath, recursive: true);
    }

    [Fact]
    public void ExportProjectSummary_ReturnsFormattedSummary()
    {
        var summary = _exportService.ExportProjectSummary(_projectKey);

        Assert.Contains("ExportTest", summary);
        Assert.Contains("Files: 0", summary);
        Assert.Contains("Memory Notes: 0", summary);
        Assert.Contains("Requests: 0", summary);
    }

    [Fact]
    public async Task ExportProjectSummary_WithFiles_ShowsFileCount()
    {
        var srcDir = Path.Combine(_tempPath, "_sources");
        Directory.CreateDirectory(srcDir);
        var srcPath = Path.Combine(srcDir, "test.txt");
        File.WriteAllText(srcPath, "content");
        await _fileStore.ImportFileAsync(_projectKey, srcPath);

        var summary = _exportService.ExportProjectSummary(_projectKey);
        Assert.Contains("Files: 1", summary);
    }

    [Fact]
    public void ExportRequestLog_WithTraceFile_ReturnsSanitisedContent()
    {
        var requestsDir = Path.Combine(_projectService.GetProjectPath(_projectKey), "requests");
        Directory.CreateDirectory(requestsDir);

        var traceContent = """
            {
              "requestId": "REQ-001",
              "apiKey": "sk-abc123def456ghi789jkl012mno345pqr678"
            }
            """;
        File.WriteAllText(Path.Combine(requestsDir, "REQ-001_trace.json"), traceContent);

        var result = _exportService.ExportRequestLog(_projectKey, "REQ-001");

        Assert.Contains("REQ-001", result);
        Assert.DoesNotContain("sk-abc123", result);
        Assert.Contains("[REDACTED]", result);
    }

    [Fact]
    public void ExportRequestLog_NoFiles_Throws()
    {
        Assert.Throws<FileNotFoundException>(() =>
            _exportService.ExportRequestLog(_projectKey, "REQ-999"));
    }

    [Fact]
    public void ExportAllToFolder_CopiesFilesButNotKeysEnc()
    {
        // Set up a log file with a secret
        var projectPath = _projectService.GetProjectPath(_projectKey);
        var logDir = Path.Combine(projectPath, "logs");
        Directory.CreateDirectory(logDir);
        File.WriteAllText(Path.Combine(logDir, "app.log"),
            "Used key sk-abc123def456ghi789jkl012mno345pqr678");

        var outputPath = Path.Combine(_tempPath, "export_output");
        _exportService.ExportAllToFolder(_projectKey, outputPath);

        // project.json copied
        Assert.True(File.Exists(Path.Combine(outputPath, "project.json")));

        // files/index.json copied
        Assert.True(File.Exists(Path.Combine(outputPath, "files", "index.json")));

        // keys.enc NOT copied
        Assert.False(File.Exists(Path.Combine(outputPath, "keys.enc")));

        // app.log is redacted
        var exportedLog = File.ReadAllText(Path.Combine(outputPath, "logs", "app.log"));
        Assert.DoesNotContain("sk-abc123", exportedLog);
        Assert.Contains("[REDACTED]", exportedLog);
    }

    [Fact]
    public void ExportAllToFolder_NonexistentProject_Throws()
    {
        var outputPath = Path.Combine(_tempPath, "bad_export");
        Assert.Throws<DirectoryNotFoundException>(() =>
            _exportService.ExportAllToFolder("nonexistent", outputPath));
    }
}
