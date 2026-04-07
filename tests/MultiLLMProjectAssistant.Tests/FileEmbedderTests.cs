using Xunit;
using MultiLLMProjectAssistant.Core.Services;

namespace MultiLLMProjectAssistant.Tests;

public class FileEmbedderTests : IDisposable
{
    private readonly string _tempPath;
    private readonly ProjectService _projectService;
    private readonly FileStoreService _fileStore;
    private readonly FileEmbedder _embedder;
    private readonly string _projectKey;

    public FileEmbedderTests()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), "MLLMEmbedderTests_" + Guid.NewGuid().ToString("N"));
        _projectService = new ProjectService(_tempPath);
        _fileStore = new FileStoreService(_projectService);
        _embedder = new FileEmbedder(_fileStore);

        var info = _projectService.CreateProject("EmbedderTest", "Testing FileEmbedder");
        _projectKey = info.ProjectKey;
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
    public async Task EmbedFileContent_ShortFile_NoTruncation()
    {
        var source = CreateSourceFile("notes.md", "Short content here");
        await _fileStore.ImportFileAsync(_projectKey, source);

        var result = _embedder.EmbedFileContent(_projectKey, "F001");

        Assert.Contains("--- File: notes.md (ID: F001) ---", result);
        Assert.Contains("Short content here", result);
        Assert.Contains("--- End of File ---", result);
        Assert.DoesNotContain("[truncated", result);
    }

    [Fact]
    public async Task EmbedFileContent_LongFile_Truncated()
    {
        var longContent = new string('X', 10_000);
        var source = CreateSourceFile("big.txt", longContent);
        await _fileStore.ImportFileAsync(_projectKey, source);

        var result = _embedder.EmbedFileContent(_projectKey, "F001", maxChars: 100);

        Assert.Contains("[truncated, 10000 chars total]", result);
        Assert.DoesNotContain(longContent, result);
    }

    [Fact]
    public async Task EmbedMultipleFiles_CombinesWithSeparator()
    {
        var src1 = CreateSourceFile("a.cs", "class A {}");
        var src2 = CreateSourceFile("b.cs", "class B {}");
        await _fileStore.ImportFileAsync(_projectKey, src1);
        await _fileStore.ImportFileAsync(_projectKey, src2);

        var result = _embedder.EmbedMultipleFiles(_projectKey, new List<string> { "F001", "F002" });

        Assert.Contains("--- File: a.cs (ID: F001) ---", result);
        Assert.Contains("--- File: b.cs (ID: F002) ---", result);
        Assert.Contains("class A {}", result);
        Assert.Contains("class B {}", result);
    }

    [Fact]
    public async Task GetAttachmentSummary_CorrectFormat()
    {
        var source = CreateSourceFile("spec.md", "# Spec");
        await _fileStore.ImportFileAsync(_projectKey, source);

        var summary = _embedder.GetAttachmentSummary(_projectKey, "F001");

        Assert.StartsWith("spec.md (", summary);
        Assert.Contains(".md,", summary);
        Assert.Contains("bytes,", summary);
        Assert.Contains("SHA-256:", summary);
    }
}
