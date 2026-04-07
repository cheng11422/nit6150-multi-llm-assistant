using System.Text.Json;
using MultiLLMProjectAssistant.Core.Interfaces;
using MultiLLMProjectAssistant.Core.Models;

namespace MultiLLMProjectAssistant.Core.Services;

public class ProjectService : IProjectService
{
    private readonly string _basePath;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public ProjectService(string? basePath = null)
    {
        _basePath = basePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MultiLLMProjectAssistant",
            "ProjectData");
    }

    public ProjectInfo CreateProject(string projectName, string description)
    {
        if (string.IsNullOrWhiteSpace(projectName))
            throw new ArgumentException("Project name cannot be empty.", nameof(projectName));

        if (projectName.Length > 100)
            throw new ArgumentException("Project name cannot exceed 100 characters.", nameof(projectName));

        var projectKey = Guid.NewGuid().ToString("N");
        var now = DateTime.UtcNow;

        var info = new ProjectInfo
        {
            ProjectKey = projectKey,
            ProjectName = projectName,
            Description = description,
            CreatedAt = now,
            LastOpenedAt = now,
            Providers = new List<string>()
        };

        var projectPath = GetProjectPath(projectKey);

        // Create folder hierarchy
        Directory.CreateDirectory(projectPath);
        Directory.CreateDirectory(Path.Combine(projectPath, "files"));
        Directory.CreateDirectory(Path.Combine(projectPath, "memory"));
        Directory.CreateDirectory(Path.Combine(projectPath, "requests"));
        Directory.CreateDirectory(Path.Combine(projectPath, "logs"));

        // Write project.json
        var json = JsonSerializer.Serialize(info, JsonOptions);
        File.WriteAllText(Path.Combine(projectPath, "project.json"), json);

        // Create placeholder files
        File.Create(Path.Combine(projectPath, "keys.enc")).Dispose();
        File.WriteAllText(Path.Combine(projectPath, "files", "index.json"), "[]");
        File.WriteAllText(Path.Combine(projectPath, "memory", "items.json"), "[]");

        return info;
    }

    public ProjectInfo OpenProject(string projectKey)
    {
        var projectPath = GetProjectPath(projectKey);

        if (!Directory.Exists(projectPath))
            throw new DirectoryNotFoundException($"Project not found: {projectKey}");

        var json = File.ReadAllText(Path.Combine(projectPath, "project.json"));
        var info = JsonSerializer.Deserialize<ProjectInfo>(json, JsonOptions)
            ?? throw new InvalidOperationException($"Failed to deserialize project.json for {projectKey}");

        info.LastOpenedAt = DateTime.UtcNow;
        SaveProjectInfo(projectPath, info);

        return info;
    }

    public void CloseProject(string projectKey)
    {
        var projectPath = GetProjectPath(projectKey);

        if (!Directory.Exists(projectPath))
            throw new DirectoryNotFoundException($"Project not found: {projectKey}");

        var json = File.ReadAllText(Path.Combine(projectPath, "project.json"));
        var info = JsonSerializer.Deserialize<ProjectInfo>(json, JsonOptions)
            ?? throw new InvalidOperationException($"Failed to deserialize project.json for {projectKey}");

        info.LastOpenedAt = DateTime.UtcNow;
        SaveProjectInfo(projectPath, info);
    }

    public List<ProjectInfo> ListProjects()
    {
        if (!Directory.Exists(_basePath))
            return new List<ProjectInfo>();

        var projects = new List<ProjectInfo>();

        foreach (var dir in Directory.GetDirectories(_basePath))
        {
            var projectJsonPath = Path.Combine(dir, "project.json");
            if (!File.Exists(projectJsonPath))
                continue;

            var json = File.ReadAllText(projectJsonPath);
            var info = JsonSerializer.Deserialize<ProjectInfo>(json, JsonOptions);
            if (info != null)
                projects.Add(info);
        }

        return projects.OrderByDescending(p => p.LastOpenedAt).ToList();
    }

    public bool DeleteProject(string projectKey)
    {
        var projectPath = GetProjectPath(projectKey);

        if (!Directory.Exists(projectPath))
            return false;

        Directory.Delete(projectPath, recursive: true);
        return true;
    }

    public string GetProjectPath(string projectKey)
    {
        return Path.Combine(_basePath, projectKey);
    }

    private void SaveProjectInfo(string projectPath, ProjectInfo info)
    {
        var json = JsonSerializer.Serialize(info, JsonOptions);
        File.WriteAllText(Path.Combine(projectPath, "project.json"), json);
    }
}
