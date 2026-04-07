using Xunit;
using MultiLLMProjectAssistant.Core.Models;
using MultiLLMProjectAssistant.Core.Services;

namespace MultiLLMProjectAssistant.Tests;

public class TraceServiceTests : IDisposable
{
    private readonly string _tempPath;
    private readonly ProjectService _projectService;
    private readonly TraceService _traceService;
    private readonly string _projectKey;

    public TraceServiceTests()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), "MLLMTraceTests_" + Guid.NewGuid().ToString("N"));
        _projectService = new ProjectService(_tempPath);
        _traceService = new TraceService(_projectService);

        var info = _projectService.CreateProject("TraceTest", "Testing TraceService");
        _projectKey = info.ProjectKey;
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempPath))
            Directory.Delete(_tempPath, recursive: true);
    }

    [Fact]
    public void SaveAndLoadTrace_RoundTrip()
    {
        var trace = new TraceRecord
        {
            RequestId = "REQ-001",
            ReferencedFileIds = new List<string> { "F001", "F002" },
            InjectedMemoryIds = new List<string> { "MEM-001" },
            Timestamp = new DateTime(2026, 3, 27, 10, 0, 0, DateTimeKind.Utc)
        };

        _traceService.SaveTrace(_projectKey, trace);
        var loaded = _traceService.LoadTrace(_projectKey, "REQ-001");

        Assert.NotNull(loaded);
        Assert.Equal("REQ-001", loaded.RequestId);
        Assert.Equal(2, loaded.ReferencedFileIds.Count);
        Assert.Contains("F001", loaded.ReferencedFileIds);
        Assert.Single(loaded.InjectedMemoryIds);
        Assert.Equal(trace.Timestamp, loaded.Timestamp);
    }

    [Fact]
    public void LoadTrace_Nonexistent_ReturnsNull()
    {
        var result = _traceService.LoadTrace(_projectKey, "REQ-999");
        Assert.Null(result);
    }

    [Fact]
    public void ListTraces_SortedByTimestampDescending()
    {
        var t1 = new TraceRecord
        {
            RequestId = "REQ-001",
            Timestamp = new DateTime(2026, 3, 25, 0, 0, 0, DateTimeKind.Utc)
        };
        var t2 = new TraceRecord
        {
            RequestId = "REQ-002",
            Timestamp = new DateTime(2026, 3, 27, 0, 0, 0, DateTimeKind.Utc)
        };
        var t3 = new TraceRecord
        {
            RequestId = "REQ-003",
            Timestamp = new DateTime(2026, 3, 26, 0, 0, 0, DateTimeKind.Utc)
        };

        _traceService.SaveTrace(_projectKey, t1);
        _traceService.SaveTrace(_projectKey, t2);
        _traceService.SaveTrace(_projectKey, t3);

        var list = _traceService.ListTraces(_projectKey);

        Assert.Equal(3, list.Count);
        Assert.Equal("REQ-002", list[0].RequestId);
        Assert.Equal("REQ-003", list[1].RequestId);
        Assert.Equal("REQ-001", list[2].RequestId);
    }
}
