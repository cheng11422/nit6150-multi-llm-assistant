namespace MultiLLMProjectAssistant.Core.Interfaces;

public interface IKeyStore
{
    void SaveKey(string projectKey, string provider, string apiKey);
    string? GetKey(string projectKey, string provider);
    bool DeleteKey(string projectKey, string provider);
    List<string> ListProviders(string projectKey);
}
