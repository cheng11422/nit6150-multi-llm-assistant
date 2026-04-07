using System.Text.Json;
using MultiLLMProjectAssistant.Core.Interfaces;
using MultiLLMProjectAssistant.Core.Models;

namespace MultiLLMProjectAssistant.Core.Services;

public class TraceService
{
    private readonly IProjectService _projectService;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public TraceService(IProjectService projectService)
    {
        _projectService = projectService;
    }

    public void SaveTrace(string projectKey, TraceRecord traceRecord)
    {
        var requestsDir = GetRequestsDir(projectKey);
        Directory.CreateDirectory(requestsDir);

        var filePath = Path.Combine(requestsDir, $"{traceRecord.RequestId}_trace.json");
        var json = JsonSerializer.Serialize(traceRecord, JsonOptions);
        File.WriteAllText(filePath, json);
    }

    public TraceRecord? LoadTrace(string projectKey, string requestId)
    {
        var filePath = Path.Combine(GetRequestsDir(projectKey), $"{requestId}_trace.json");
        if (!File.Exists(filePath))
            return null;

        var json = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<TraceRecord>(json, JsonOptions);
    }

    public List<TraceRecord> ListTraces(string projectKey)
    {
        var requestsDir = GetRequestsDir(projectKey);
        if (!Directory.Exists(requestsDir))
            return new List<TraceRecord>();

        var traces = new List<TraceRecord>();

        foreach (var file in Directory.GetFiles(requestsDir, "*_trace.json"))
        {
            var json = File.ReadAllText(file);
            var trace = JsonSerializer.Deserialize<TraceRecord>(json, JsonOptions);
            if (trace != null)
                traces.Add(trace);
        }

        return traces.OrderByDescending(t => t.Timestamp).ToList();
    }

    private string GetRequestsDir(string projectKey)
    {
        return Path.Combine(_projectService.GetProjectPath(projectKey), "requests");
    }
}
