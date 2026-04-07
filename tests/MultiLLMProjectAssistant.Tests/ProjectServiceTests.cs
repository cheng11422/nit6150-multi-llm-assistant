using Xunit;
using MultiLLMProjectAssistant.Core.Services;

namespace MultiLLMProjectAssistant.Tests;

public class ProjectServiceTests : IDisposable
{
    private readonly string _tempPath;
    private readonly ProjectService _service;

    public ProjectServiceTests()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), "MLLMTests_" + Guid.NewGuid().ToString("N"));
        _service = new ProjectService(_tempPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempPath))
            Directory.Delete(_tempPath, recursive: true);
    }

    [Fact]
    public void CreateProject_ShouldCreateFolderHierarchyAndFiles()
    {
        var info = _service.CreateProject("TestProject", "A test project");

        var projectPath = _service.GetProjectPath(info.ProjectKey);

        // Verify folders exist
        Assert.True(Directory.Exists(projectPath));
        Assert.True(Directory.Exists(Path.Combine(projectPath, "files")));
        Assert.True(Directory.Exists(Path.Combine(projectPath, "memory")));
        Assert.True(Directory.Exists(Path.Combine(projectPath, "requests")));
        Assert.True(Directory.Exists(Path.Combine(projectPath, "logs")));

        // Verify files exist
        Assert.True(File.Exists(Path.Combine(projectPath, "project.json")));
        Assert.True(File.Exists(Path.Combine(projectPath, "keys.enc")));
        Assert.True(File.Exists(Path.Combine(projectPath, "files", "index.json")));
        Assert.True(File.Exists(Path.Combine(projectPath, "memory", "items.json")));

        // Verify placeholder contents
        Assert.Equal("[]", File.ReadAllText(Path.Combine(projectPath, "files", "index.json")));
        Assert.Equal("[]", File.ReadAllText(Path.Combine(projectPath, "memory", "items.json")));
        Assert.Empty(File.ReadAllBytes(Path.Combine(projectPath, "keys.enc")));
    }

    [Fact]
    public void CreateProject_ShouldReturnValidProjectInfo()
    {
        var info = _service.CreateProject("MyProject", "Description here");

        Assert.Equal("MyProject", info.ProjectName);
        Assert.Equal("Description here", info.Description);
        Assert.Equal(32, info.ProjectKey.Length);
        Assert.NotEqual(default, info.CreatedAt);
        Assert.NotEqual(default, info.LastOpenedAt);
    }

    [Fact]
    public void OpenProject_ShouldReturnProjectInfoAndUpdateLastOpened()
    {
        var created = _service.CreateProject("OpenTest", "Test open");
        var originalLastOpened = created.LastOpenedAt;

        // Small delay to ensure timestamp differs
        Thread.Sleep(50);

        var opened = _service.OpenProject(created.ProjectKey);

        Assert.Equal(created.ProjectKey, opened.ProjectKey);
        Assert.Equal("OpenTest", opened.ProjectName);
        Assert.True(opened.LastOpenedAt > originalLastOpened);
    }

    [Fact]
    public void OpenProject_ShouldThrowIfNotFound()
    {
        Assert.Throws<DirectoryNotFoundException>(() =>
            _service.OpenProject("nonexistentkey"));
    }

    [Fact]
    public void ListProjects_ShouldReturnAllProjectsSortedByLastOpened()
    {
        _service.CreateProject("First", "1st");
        Thread.Sleep(50);
        _service.CreateProject("Second", "2nd");
        Thread.Sleep(50);
        _service.CreateProject("Third", "3rd");

        var list = _service.ListProjects();

        Assert.Equal(3, list.Count);
        Assert.Equal("Third", list[0].ProjectName);
        Assert.Equal("Second", list[1].ProjectName);
        Assert.Equal("First", list[2].ProjectName);
    }

    [Fact]
    public void ListProjects_ShouldReturnEmptyIfNoProjects()
    {
        var list = _service.ListProjects();
        Assert.Empty(list);
    }

    [Fact]
    public void DeleteProject_ShouldRemoveDirectoryAndReturnTrue()
    {
        var info = _service.CreateProject("ToDelete", "Delete me");
        var projectPath = _service.GetProjectPath(info.ProjectKey);

        Assert.True(Directory.Exists(projectPath));

        var result = _service.DeleteProject(info.ProjectKey);

        Assert.True(result);
        Assert.False(Directory.Exists(projectPath));
    }

    [Fact]
    public void DeleteProject_ShouldReturnFalseIfNotFound()
    {
        var result = _service.DeleteProject("nonexistentkey");
        Assert.False(result);
    }

    [Fact]
    public void CloseProject_ShouldUpdateLastOpenedAt()
    {
        var created = _service.CreateProject("CloseTest", "Test close");
        Thread.Sleep(50);

        _service.CloseProject(created.ProjectKey);

        var reopened = _service.OpenProject(created.ProjectKey);
        Assert.True(reopened.LastOpenedAt > created.LastOpenedAt);
    }

    [Fact]
    public void CreateProject_EmptyName_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            _service.CreateProject("", "desc"));
    }

    [Fact]
    public void CreateProject_WhitespaceName_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            _service.CreateProject("   ", "desc"));
    }

    [Fact]
    public void CreateProject_NameTooLong_Throws()
    {
        var longName = new string('A', 101);
        Assert.Throws<ArgumentException>(() =>
            _service.CreateProject(longName, "desc"));
    }

    [Fact]
    public void GetProjectPath_ShouldReturnCorrectPath()
    {
        var path = _service.GetProjectPath("abc123");
        Assert.Equal(Path.Combine(_tempPath, "abc123"), path);
    }
}
