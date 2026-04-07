using System.Security.Cryptography;
using Xunit;
using MultiLLMProjectAssistant.Core.Services;

namespace MultiLLMProjectAssistant.Tests;

public class FileStoreServiceTests : IDisposable
{
    private readonly string _tempPath;
    private readonly ProjectService _projectService;
    private readonly FileStoreService _fileStore;
    private readonly string _projectKey;

    public FileStoreServiceTests()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), "MLLMFileStoreTests_" + Guid.NewGuid().ToString("N"));
        _projectService = new ProjectService(_tempPath);
        _fileStore = new FileStoreService(_projectService);

        var info = _projectService.CreateProject("FileStoreTest", "Testing FileStore");
        _projectKey = info.ProjectKey;
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempPath))
            Directory.Delete(_tempPath, recursive: true);
    }

    private string CreateTempSourceFile(string name, string content)
    {
        var dir = Path.Combine(_tempPath, "_sources");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public async Task ImportFileAsync_CreatesCopyAndMetadata()
    {
        var source = CreateTempSourceFile("readme.md", "# Hello World");
        var meta = await _fileStore.ImportFileAsync(_projectKey, source);

        Assert.Equal("F001", meta.FileId);
        Assert.Equal("readme.md", meta.OriginalName);
        Assert.Equal(".md", meta.FileType);
        Assert.True(meta.FileSizeBytes > 0);

        var storedPath = Path.Combine(
            _projectService.GetProjectPath(_projectKey), "files", "F001_readme.md");
        Assert.True(File.Exists(storedPath));
        Assert.Equal("# Hello World", File.ReadAllText(storedPath));
    }

    [Fact]
    public async Task ImportFileAsync_GeneratesCorrectSha256()
    {
        var content = "Hash me please";
        var source = CreateTempSourceFile("test.txt", content);

        var meta = await _fileStore.ImportFileAsync(_projectKey, source);

        // Compute expected hash from the stored file
        var storedPath = Path.Combine(
            _projectService.GetProjectPath(_projectKey), "files", "F001_test.txt");
        using var stream = File.OpenRead(storedPath);
        var expectedHash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();

        Assert.Equal(expectedHash, meta.Sha256Hash);
        Assert.Equal(64, meta.Sha256Hash.Length);
    }

    [Fact]
    public async Task ImportFileAsync_IncrementsFileId()
    {
        var src1 = CreateTempSourceFile("a.txt", "file1");
        var src2 = CreateTempSourceFile("b.txt", "file2");
        var src3 = CreateTempSourceFile("c.txt", "file3");

        var m1 = await _fileStore.ImportFileAsync(_projectKey, src1);
        var m2 = await _fileStore.ImportFileAsync(_projectKey, src2);
        var m3 = await _fileStore.ImportFileAsync(_projectKey, src3);

        Assert.Equal("F001", m1.FileId);
        Assert.Equal("F002", m2.FileId);
        Assert.Equal("F003", m3.FileId);
    }

    [Fact]
    public async Task GetFileList_ReturnsAll()
    {
        var src1 = CreateTempSourceFile("x.cs", "class X {}");
        var src2 = CreateTempSourceFile("y.cs", "class Y {}");

        await _fileStore.ImportFileAsync(_projectKey, src1);
        await _fileStore.ImportFileAsync(_projectKey, src2);

        var list = _fileStore.GetFileList(_projectKey);
        Assert.Equal(2, list.Count);
    }

    [Fact]
    public async Task GetFileContent_ReadsText()
    {
        var source = CreateTempSourceFile("notes.txt", "Some notes here");
        await _fileStore.ImportFileAsync(_projectKey, source);

        var content = _fileStore.GetFileContent(_projectKey, "F001");
        Assert.Equal("Some notes here", content);
    }

    [Fact]
    public async Task GetFileContent_BinaryExtension_Throws()
    {
        var source = CreateTempSourceFile("image.png", "fakebinary");
        await _fileStore.ImportFileAsync(_projectKey, source);

        Assert.Throws<NotSupportedException>(() =>
            _fileStore.GetFileContent(_projectKey, "F001"));
    }

    [Fact]
    public async Task DeleteFile_RemovesFromDiskAndIndex()
    {
        var source = CreateTempSourceFile("delete_me.txt", "bye");
        await _fileStore.ImportFileAsync(_projectKey, source);

        var storedPath = Path.Combine(
            _projectService.GetProjectPath(_projectKey), "files", "F001_delete_me.txt");
        Assert.True(File.Exists(storedPath));

        var result = _fileStore.DeleteFile(_projectKey, "F001");

        Assert.True(result);
        Assert.False(File.Exists(storedPath));
        Assert.Empty(_fileStore.GetFileList(_projectKey));
    }

    [Fact]
    public async Task ImportFileAsync_NonexistentFile_Throws()
    {
        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            _fileStore.ImportFileAsync(_projectKey, "/nonexistent/file.txt"));
    }

    [Fact]
    public void GetFileById_NotFound_ReturnsNull()
    {
        var result = _fileStore.GetFileById(_projectKey, "F999");
        Assert.Null(result);
    }

    [Fact]
    public async Task ImportFileAsync_FileTooLarge_Throws()
    {
        var smallLimitStore = new FileStoreService(_projectService, maxFileSizeBytes: 10);
        var source = CreateTempSourceFile("big.txt", "This content is larger than 10 bytes");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            smallLimitStore.ImportFileAsync(_projectKey, source));
    }

    [Fact]
    public async Task ImportFileAsync_DisallowedExtension_Throws()
    {
        var source = CreateTempSourceFile("script.exe", "bad stuff");

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            _fileStore.ImportFileAsync(_projectKey, source));
    }
}
