using Xunit;
using MultiLLMProjectAssistant.Core.Services;

namespace MultiLLMProjectAssistant.Tests;

public class AppLoggerTests : IDisposable
{
    private readonly string _tempPath;
    private readonly string _logFilePath;
    private readonly AppLogger _logger;

    public AppLoggerTests()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), "MLLMLoggerTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempPath);
        _logger = new AppLogger(_tempPath);
        _logFilePath = Path.Combine(_tempPath, "logs", "app.log");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempPath))
            Directory.Delete(_tempPath, recursive: true);
    }

    [Fact]
    public void Log_CreatesFileAndWrites()
    {
        _logger.Log("INFO", "Application started");

        Assert.True(File.Exists(_logFilePath));
        var content = File.ReadAllText(_logFilePath);
        Assert.Contains("[INFO]", content);
        Assert.Contains("Application started", content);
        Assert.Contains("UTC]", content);
    }

    [Fact]
    public void Log_AppendsNotOverwrites()
    {
        _logger.Log("INFO", "First message");
        _logger.Log("WARN", "Second message");

        var lines = File.ReadAllLines(_logFilePath);
        Assert.Equal(2, lines.Length);
        Assert.Contains("First message", lines[0]);
        Assert.Contains("Second message", lines[1]);
    }

    [Fact]
    public void Log_RedactsApiKeys()
    {
        _logger.Log("INFO", "Using key sk-abc123def456ghi789jkl012mno345pqr678");

        var content = File.ReadAllText(_logFilePath);
        Assert.DoesNotContain("sk-abc123", content);
        Assert.Contains("[REDACTED]", content);
    }

    [Fact]
    public void LogRequest_FormatsCorrectly()
    {
        _logger.LogRequest("REQ-001", "chatgpt", 200);

        var content = File.ReadAllText(_logFilePath);
        Assert.Contains("[INFO]", content);
        Assert.Contains("[REQ-001]", content);
        Assert.Contains("Request REQ-001 to chatgpt returned 200", content);
    }

    [Fact]
    public void LogRequest_ErrorStatusCode_LogsAsError()
    {
        _logger.LogRequest("REQ-002", "gemini", 500);

        var content = File.ReadAllText(_logFilePath);
        Assert.Contains("[ERROR]", content);
    }

    [Fact]
    public void Log_WithRequestId_IncludesIdInOutput()
    {
        _logger.Log("INFO", "Processing files", "REQ-005");

        var content = File.ReadAllText(_logFilePath);
        Assert.Contains("[REQ-005]", content);
        Assert.Contains("Processing files", content);
    }
}
