using Xunit;
using MultiLLMProjectAssistant.Core.Models;
using MultiLLMProjectAssistant.Core.Services;

namespace MultiLLMProjectAssistant.Tests;

public class IntegrationTests : IDisposable
{
    private readonly string _tempPath;

    public IntegrationTests()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), "MLLMIntegration_" + Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempPath))
            Directory.Delete(_tempPath, recursive: true);
    }

    private string CreateSourceFile(string name, string content)
    {
        var dir = Path.Combine(_tempPath, "_sources");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public async Task FullWorkflow_EndToEnd()
    {
        // 1. Create project
        var projectService = new ProjectService(_tempPath);
        var project = projectService.CreateProject("Integration Test", "Full E2E test");
        var projectPath = projectService.GetProjectPath(project.ProjectKey);

        Assert.True(Directory.Exists(projectPath));
        Assert.True(File.Exists(Path.Combine(projectPath, "project.json")));

        // 2. Store API key → verify keys.enc is binary
        var encryption = new EncryptionService("integration-test-passphrase");
        var keyStore = new KeyStore(encryption, projectService);

        keyStore.SaveKey(project.ProjectKey, "chatgpt", "sk-integrationTestKey1234567890abcdef");
        keyStore.SaveKey(project.ProjectKey, "gemini", "AIzaSyIntegrationTestKey1234567890ABCDEFGHIJ");

        var keysEncPath = Path.Combine(projectPath, "keys.enc");
        var keysRaw = File.ReadAllBytes(keysEncPath);
        var keysRawText = System.Text.Encoding.UTF8.GetString(keysRaw);
        Assert.True(keysRaw.Length > 0);
        Assert.DoesNotContain("sk-integration", keysRawText);
        Assert.DoesNotContain("AIzaSy", keysRawText);

        // Verify round-trip
        Assert.Equal("sk-integrationTestKey1234567890abcdef",
            keyStore.GetKey(project.ProjectKey, "chatgpt"));

        // 3. Import 2 files → verify index.json
        var fileStore = new FileStoreService(projectService);
        var src1 = CreateSourceFile("spec.md", "# Project Specification\nThis is a test spec.");
        var src2 = CreateSourceFile("main.cs", "namespace Test { class Program { } }");

        var file1 = await fileStore.ImportFileAsync(project.ProjectKey, src1);
        var file2 = await fileStore.ImportFileAsync(project.ProjectKey, src2);

        Assert.Equal("F001", file1.FileId);
        Assert.Equal("F002", file2.FileId);
        Assert.Equal(64, file1.Sha256Hash.Length);

        var fileList = fileStore.GetFileList(project.ProjectKey);
        Assert.Equal(2, fileList.Count);

        // 4. Embed files → verify output format
        var embedder = new FileEmbedder(fileStore);
        var embedded = embedder.EmbedFileContent(project.ProjectKey, "F001");
        Assert.Contains("--- File: spec.md (ID: F001) ---", embedded);
        Assert.Contains("# Project Specification", embedded);
        Assert.Contains("--- End of File ---", embedded);

        var multiEmbed = embedder.EmbedMultipleFiles(project.ProjectKey,
            new List<string> { "F001", "F002" });
        Assert.Contains("spec.md", multiEmbed);
        Assert.Contains("main.cs", multiEmbed);

        var summary = embedder.GetAttachmentSummary(project.ProjectKey, "F001");
        Assert.Contains("spec.md", summary);
        Assert.Contains("SHA-256:", summary);

        // 5. Create TraceRecord
        var traceService = new TraceService(projectService);
        var trace = new TraceRecord
        {
            RequestId = "REQ-001",
            ReferencedFileIds = new List<string> { "F001", "F002" },
            InjectedMemoryIds = new List<string> { "MEM-001" },
            Timestamp = DateTime.UtcNow
        };
        traceService.SaveTrace(project.ProjectKey, trace);

        var loadedTrace = traceService.LoadTrace(project.ProjectKey, "REQ-001");
        Assert.NotNull(loadedTrace);
        Assert.Equal(2, loadedTrace.ReferencedFileIds.Count);

        // 6. Log request → verify no secrets in app.log
        var logger = new AppLogger(projectPath);
        logger.Log("INFO", "Project opened");
        logger.Log("WARN", "Key used: sk-integrationTestKey1234567890abcdef should be hidden");
        logger.LogRequest("REQ-001", "chatgpt", 200);

        var logContent = File.ReadAllText(Path.Combine(projectPath, "logs", "app.log"));
        Assert.Contains("[INFO]", logContent);
        Assert.Contains("[REQ-001]", logContent);
        Assert.DoesNotContain("sk-integration", logContent);
        Assert.Contains("[REDACTED]", logContent);

        // 7. Export → verify no keys.enc in output
        var exportService = new ExportService(projectService, fileStore);
        var exportPath = Path.Combine(_tempPath, "exported");
        exportService.ExportAllToFolder(project.ProjectKey, exportPath);

        Assert.True(File.Exists(Path.Combine(exportPath, "project.json")));
        Assert.True(File.Exists(Path.Combine(exportPath, "files", "index.json")));
        Assert.True(File.Exists(Path.Combine(exportPath, "logs", "app.log")));
        Assert.False(File.Exists(Path.Combine(exportPath, "keys.enc")));

        var exportedLog = File.ReadAllText(Path.Combine(exportPath, "logs", "app.log"));
        Assert.DoesNotContain("sk-integration", exportedLog);

        // 8. Delete project → verify folder removed
        var deleted = projectService.DeleteProject(project.ProjectKey);
        Assert.True(deleted);
        Assert.False(Directory.Exists(projectPath));
    }

    [Fact]
    public void SecurityAudit_NoSecretsLeaked()
    {
        // 1. Create project + store API keys
        var projectService = new ProjectService(_tempPath);
        var project = projectService.CreateProject("Security Audit", "Audit test");
        var projectPath = projectService.GetProjectPath(project.ProjectKey);

        var encryption = new EncryptionService("audit-passphrase");
        var keyStore = new KeyStore(encryption, projectService);

        var openaiKey = "sk-auditTest1234567890abcdefghijklmnopqrstuv";
        var geminiKey = "AIzaSyAuditTestKey1234567890ABCDEFGHIJKLMNO";
        var grokKey = "xai-auditTest1234567890abcdefghij";

        keyStore.SaveKey(project.ProjectKey, "chatgpt", openaiKey);
        keyStore.SaveKey(project.ProjectKey, "gemini", geminiKey);
        keyStore.SaveKey(project.ProjectKey, "grok", grokKey);

        // 2. Verify keys.enc is binary — no key patterns in raw bytes
        var keysEncPath = Path.Combine(projectPath, "keys.enc");
        var rawBytes = File.ReadAllBytes(keysEncPath);
        var rawText = System.Text.Encoding.UTF8.GetString(rawBytes);

        Assert.DoesNotContain("sk-", rawText);
        Assert.DoesNotContain("AIza", rawText);
        Assert.DoesNotContain("xai-", rawText);
        Assert.DoesNotContain("chatgpt", rawText);
        Assert.DoesNotContain("gemini", rawText);
        Assert.DoesNotContain("grok", rawText);

        // 3. Verify app.log is redacted
        var logger = new AppLogger(projectPath);
        logger.Log("INFO", $"OpenAI key: {openaiKey}");
        logger.Log("INFO", $"Gemini key: {geminiKey}");
        logger.Log("INFO", $"Grok key: {grokKey}");
        logger.Log("INFO", $"Bearer token: Bearer eyJhbGciOiJIUzI1NiJ9abcdefghijklmno");

        var logContent = File.ReadAllText(Path.Combine(projectPath, "logs", "app.log"));
        Assert.DoesNotContain("sk-audit", logContent);
        Assert.DoesNotContain("AIzaSy", logContent);
        Assert.DoesNotContain("xai-audit", logContent);
        Assert.DoesNotContain("Bearer eyJ", logContent);
        Assert.Equal(4, logContent.Split("[REDACTED]").Length - 1);

        // 4. Verify request JSON is redacted via RedactionFilter
        var requestJson = $"{{\"api_key\": \"{openaiKey}\", \"model\": \"gpt-4\"}}";
        var redacted = RedactionFilter.Redact(requestJson);
        Assert.DoesNotContain("sk-audit", redacted);
        Assert.Contains("[REDACTED]", redacted);
        Assert.Contains("gpt-4", redacted); // non-secret data preserved

        // 5. Verify .gitignore has required rules
        var gitignorePath = Path.Combine(
            Path.GetDirectoryName(_tempPath)!,
            "..", "..", "..", "..",
            "Multi-LLM-Project-Assistant", ".gitignore");
        // Use the known absolute path instead
        var knownGitignore = "/Users/pongporntakham/Documents/Claude/Projects/ADV project/Multi-LLM-Project-Assistant/.gitignore";
        if (File.Exists(knownGitignore))
        {
            var gitignoreContent = File.ReadAllText(knownGitignore);
            Assert.Contains("*.enc", gitignoreContent);
            Assert.Contains("ProjectData/", gitignoreContent);
            Assert.Contains("logs/", gitignoreContent);
        }
    }
}
