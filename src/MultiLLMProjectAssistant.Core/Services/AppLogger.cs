using MultiLLMProjectAssistant.Core.Interfaces;

namespace MultiLLMProjectAssistant.Core.Services;

public class AppLogger : IAppLogger
{
    private readonly string _logFilePath;
    private readonly object _lock = new();

    public AppLogger(string projectPath)
    {
        var logsDir = Path.Combine(projectPath, "logs");
        Directory.CreateDirectory(logsDir);
        _logFilePath = Path.Combine(logsDir, "app.log");
    }

    public void Log(string level, string message, string? requestId = null)
    {
        var redacted = RedactionFilter.Redact(message);
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

        var line = requestId is not null
            ? $"[{timestamp} UTC] [{level}] [{requestId}] {redacted}"
            : $"[{timestamp} UTC] [{level}] {redacted}";

        lock (_lock)
        {
            File.AppendAllText(_logFilePath, line + Environment.NewLine);
        }
    }

    public void LogRequest(string requestId, string provider, int statusCode)
    {
        var level = statusCode >= 200 && statusCode < 300 ? "INFO" : "ERROR";
        var message = $"Request {requestId} to {provider} returned {statusCode}";
        Log(level, message, requestId);
    }
}
