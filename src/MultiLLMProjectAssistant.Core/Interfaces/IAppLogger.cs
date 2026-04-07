namespace MultiLLMProjectAssistant.Core.Interfaces;

public interface IAppLogger
{
    void Log(string level, string message, string? requestId = null);
    void LogRequest(string requestId, string provider, int statusCode);
}
