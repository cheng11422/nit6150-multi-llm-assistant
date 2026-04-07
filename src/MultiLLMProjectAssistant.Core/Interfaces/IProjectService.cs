using MultiLLMProjectAssistant.Core.Models;

namespace MultiLLMProjectAssistant.Core.Interfaces;

public interface IProjectService
{
    ProjectInfo CreateProject(string projectName, string description);
    ProjectInfo OpenProject(string projectKey);
    void CloseProject(string projectKey);
    List<ProjectInfo> ListProjects();
    bool DeleteProject(string projectKey);
    string GetProjectPath(string projectKey);
}
