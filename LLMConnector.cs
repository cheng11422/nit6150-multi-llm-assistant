using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MultiLLMProjectAssistant.UI
{
    public sealed class LlmRequest
    {
        public string Provider { get; set; } = "";
        public string ProjectName { get; set; } = "";
        public string Template { get; set; } = "";
        public int TopK { get; set; } = 5;
        public bool MemoryEnabled { get; set; }
        public string JsonRequest { get; set; } = "";
        public string[] ProjectMemoryItems { get; set; } = Array.Empty<string>();
        public string[] AttachedFiles { get; set; } = Array.Empty<string>();
    }

    public sealed class LlmResponse
    {
        public bool IsSuccess { get; set; }
        public string Status { get; set; } = "Error";
        public int? StatusCode { get; set; }
        public string Provider { get; set; } = "";
        public string Model { get; set; } = "";
        public string RawJson { get; set; } = "";
        public string NormalizedJson { get; set; } = "";
        public string Summary { get; set; } = "";
    }

    public sealed class LLMConnector
    {
        private const string GeminiTextModel = "gemini-2.5-flash";

        private sealed class SettingsModel
        {
            public int TimeoutSeconds { get; set; } = 60;
            public int RetryCount { get; set; } = 2;
            public ApiKeyItem[] ApiKeys { get; set; } = Array.Empty<ApiKeyItem>();
        }

        private sealed class ApiKeyItem
        {
            public string Provider { get; set; } = "";
            public string EncryptedValue { get; set; } = "";
        }

        private static readonly JsonSerializerOptions PrettyJson = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        private readonly HttpClient _httpClient;
        private readonly string _settingsPath;

        public LLMConnector()
            : this(
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "MultiLLMProjectAssistant",
                    "settings.json"),
                new HttpClient())
        {
        }

        internal LLMConnector(string settingsPath, HttpClient? httpClient = null)
        {
            _settingsPath = settingsPath;
            _httpClient = httpClient ?? new HttpClient();
        }

        public async Task<LlmResponse> SendRequestAsync(LlmRequest request, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(request.Provider))
                return BuildClientError(request, "Select a provider before submitting the request.");

            if (string.IsNullOrWhiteSpace(request.JsonRequest))
                return BuildClientError(request, "Enter a request before submitting.");

            SettingsModel settings;
            try
            {
                settings = LoadSettings();
            }
            catch (Exception ex)
            {
                return BuildClientError(request, $"Unable to load settings: {ex.Message}");
            }

            string apiKey;
            try
            {
                apiKey = ResolveApiKey(settings, request.Provider);
            }
            catch (Exception ex)
            {
                return BuildClientError(request, $"Unable to decrypt the API key for {request.Provider}: {ex.Message}");
            }

            if (string.IsNullOrWhiteSpace(apiKey))
                return BuildClientError(request, $"No API key is saved for provider {request.Provider}.");

            var attempts = Math.Max(0, settings.RetryCount) + 1;
            var timeoutSeconds = Math.Max(5, settings.TimeoutSeconds);

            for (var attempt = 1; attempt <= attempts; attempt++)
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

                try
                {
                    var response = request.Provider switch
                    {
                        "OpenAI-GPT4" => await SendOpenAiAsync(request, apiKey, timeoutCts.Token),
                        "Gemini-Pro" => await SendGeminiAsync(request, apiKey, timeoutCts.Token),
                        _ => BuildClientError(request, $"Provider {request.Provider} is not supported yet.")
                    };

                    if (!ShouldRetry(response.StatusCode) || attempt >= attempts)
                        return response;

                    await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken);
                }
                catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    if (attempt >= attempts)
                        return BuildClientError(request, $"The request timed out after {timeoutSeconds} seconds.");

                    await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken);
                }
                catch (HttpRequestException ex)
                {
                    if (attempt >= attempts)
                        return BuildClientError(request, $"Network error while calling {request.Provider}: {ex.Message}");

                    await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken);
                }
                catch (Exception ex)
                {
                    return BuildClientError(request, $"Unexpected error while calling {request.Provider}: {ex.Message}");
                }
            }

            return BuildClientError(request, "The request failed before a provider response was returned.");
        }

        private SettingsModel LoadSettings()
        {
            if (!File.Exists(_settingsPath))
                return new SettingsModel();

            var json = File.ReadAllText(_settingsPath);
            return JsonSerializer.Deserialize<SettingsModel>(json) ?? new SettingsModel();
        }

        private static string ResolveApiKey(SettingsModel settings, string provider)
        {
            var encrypted = settings.ApiKeys?
                .FirstOrDefault(k => string.Equals(k.Provider, provider, StringComparison.OrdinalIgnoreCase))
                ?.EncryptedValue;

            if (string.IsNullOrWhiteSpace(encrypted))
                return "";

            var protectedBytes = Convert.FromBase64String(encrypted);
            var data = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(data);
        }

        private async Task<LlmResponse> SendOpenAiAsync(LlmRequest request, string apiKey, CancellationToken cancellationToken)
        {
            var payload = new
            {
                model = "gpt-4o",
                temperature = 0.2,
                messages = new object[]
                {
                    new
                    {
                        role = "system",
                        content = BuildSystemPrompt(request)
                    },
                    new
                    {
                        role = "user",
                        content = request.JsonRequest.Trim()
                    }
                }
            };

            using var message = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            message.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(message, cancellationToken);
            var raw = await response.Content.ReadAsStringAsync(cancellationToken);
            var outputText = ExtractOpenAiText(raw);
            var model = ExtractJsonString(raw, "model") ?? "gpt-4o";
            var error = response.IsSuccessStatusCode ? "" : ExtractErrorMessage(raw);

            return BuildProviderResponse(request, response.StatusCode, raw, outputText, model, error);
        }

        private async Task<LlmResponse> SendGeminiAsync(LlmRequest request, string apiKey, CancellationToken cancellationToken)
        {
            var payload = new
            {
                systemInstruction = new
                {
                    parts = new[]
                    {
                        new { text = BuildSystemPrompt(request) }
                    }
                },
                contents = new[]
                {
                    new
                    {
                        role = "user",
                        parts = new[]
                        {
                            new { text = request.JsonRequest.Trim() }
                        }
                    }
                },
                generationConfig = new
                {
                    temperature = 0.2
                }
            };

            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{GeminiTextModel}:generateContent?key={Uri.EscapeDataString(apiKey)}";
            using var message = new HttpRequestMessage(HttpMethod.Post, url);
            message.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(message, cancellationToken);
            var raw = await response.Content.ReadAsStringAsync(cancellationToken);
            var outputText = ExtractGeminiText(raw);
            var error = response.IsSuccessStatusCode ? "" : ExtractErrorMessage(raw);

            return BuildProviderResponse(request, response.StatusCode, raw, outputText, GeminiTextModel, error);
        }

        private static LlmResponse BuildProviderResponse(
            LlmRequest request,
            HttpStatusCode statusCode,
            string raw,
            string outputText,
            string model,
            string error)
        {
            var ok = (int)statusCode >= 200 && (int)statusCode < 300;
            var status = ok
                ? "Success"
                : statusCode == HttpStatusCode.TooManyRequests ? "Rate Limit" : "Error";

            var summary = ok
                ? (string.IsNullOrWhiteSpace(outputText) ? "The provider returned an empty response." : outputText)
                : (string.IsNullOrWhiteSpace(error) ? $"Provider returned HTTP {(int)statusCode}." : error);

            var normalized = new
            {
                provider = request.Provider,
                model,
                ok,
                status,
                statusCode = (int)statusCode,
                template = request.Template,
                projectName = request.ProjectName,
                topK = request.TopK,
                memoryEnabled = request.MemoryEnabled,
                memoryItemsCount = request.ProjectMemoryItems.Length,
                attachedFiles = request.AttachedFiles,
                outputText,
                error = ok ? "" : summary
            };

            return new LlmResponse
            {
                IsSuccess = ok,
                Status = status,
                StatusCode = (int)statusCode,
                Provider = request.Provider,
                Model = model,
                RawJson = TryPrettyPrintJson(raw),
                NormalizedJson = JsonSerializer.Serialize(normalized, PrettyJson),
                Summary = summary
            };
        }

        private static LlmResponse BuildClientError(LlmRequest request, string message)
        {
            var normalized = new
            {
                provider = request.Provider,
                ok = false,
                status = "Error",
                statusCode = (int?)null,
                template = request.Template,
                projectName = request.ProjectName,
                topK = request.TopK,
                memoryEnabled = request.MemoryEnabled,
                memoryItemsCount = request.ProjectMemoryItems.Length,
                attachedFiles = request.AttachedFiles,
                outputText = "",
                error = message
            };

            return new LlmResponse
            {
                IsSuccess = false,
                Status = "Error",
                StatusCode = null,
                Provider = request.Provider,
                RawJson = JsonSerializer.Serialize(new { error = message }, PrettyJson),
                NormalizedJson = JsonSerializer.Serialize(normalized, PrettyJson),
                Summary = message
            };
        }

        private static bool ShouldRetry(int? statusCode)
        {
            if (!statusCode.HasValue)
                return false;

            return statusCode == 429 || statusCode >= 500;
        }

        private static string BuildSystemPrompt(LlmRequest request)
        {
            var sb = new StringBuilder();
            sb.AppendLine("You are an assistant inside a desktop multi-provider project workspace.");
            if (!string.IsNullOrWhiteSpace(request.ProjectName))
                sb.AppendLine($"Project: {request.ProjectName}");
            sb.AppendLine($"Template: {request.Template}");
            sb.AppendLine($"Project memory enabled: {request.MemoryEnabled}");
            sb.AppendLine($"Top-K memory setting: {request.TopK}");

            if (request.MemoryEnabled && request.ProjectMemoryItems.Length > 0)
            {
                sb.AppendLine("Relevant project memory:");
                foreach (var memoryItem in request.ProjectMemoryItems)
                    sb.AppendLine(memoryItem);
            }

            if (request.AttachedFiles.Length > 0)
                sb.AppendLine($"Attached files: {string.Join(", ", request.AttachedFiles.Select(Path.GetFileName))}");

            sb.AppendLine("Respond clearly and directly to the user request.");
            return sb.ToString().Trim();
        }

        private static string ExtractOpenAiText(string raw)
        {
            try
            {
                using var doc = JsonDocument.Parse(raw);
                var choices = doc.RootElement.GetProperty("choices");
                if (choices.GetArrayLength() == 0)
                    return "";

                var message = choices[0].GetProperty("message");
                if (!message.TryGetProperty("content", out var content))
                    return "";

                return content.ValueKind switch
                {
                    JsonValueKind.String => content.GetString() ?? "",
                    JsonValueKind.Array => string.Join(
                        Environment.NewLine,
                        content.EnumerateArray()
                            .Where(item => item.TryGetProperty("text", out _))
                            .Select(item => item.GetProperty("text").GetString() ?? "")
                            .Where(text => !string.IsNullOrWhiteSpace(text))),
                    _ => ""
                };
            }
            catch
            {
                return "";
            }
        }

        private static string ExtractGeminiText(string raw)
        {
            try
            {
                using var doc = JsonDocument.Parse(raw);
                if (!doc.RootElement.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
                    return "";

                var parts = candidates[0].GetProperty("content").GetProperty("parts");
                return string.Join(
                    Environment.NewLine,
                    parts.EnumerateArray()
                        .Where(item => item.TryGetProperty("text", out _))
                        .Select(item => item.GetProperty("text").GetString() ?? "")
                        .Where(text => !string.IsNullOrWhiteSpace(text)));
            }
            catch
            {
                return "";
            }
        }

        private static string ExtractErrorMessage(string raw)
        {
            try
            {
                using var doc = JsonDocument.Parse(raw);

                if (doc.RootElement.TryGetProperty("error", out var errorElement))
                {
                    if (errorElement.ValueKind == JsonValueKind.String)
                        return errorElement.GetString() ?? "";

                    if (errorElement.ValueKind == JsonValueKind.Object)
                    {
                        if (errorElement.TryGetProperty("message", out var messageElement))
                            return messageElement.GetString() ?? "";

                        if (errorElement.TryGetProperty("status", out var statusElement))
                            return statusElement.GetString() ?? "";
                    }
                }
            }
            catch
            {
            }

            return "";
        }

        private static string? ExtractJsonString(string raw, string propertyName)
        {
            try
            {
                using var doc = JsonDocument.Parse(raw);
                if (doc.RootElement.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String)
                    return value.GetString();
            }
            catch
            {
            }

            return null;
        }

        private static string TryPrettyPrintJson(string raw)
        {
            try
            {
                using var doc = JsonDocument.Parse(raw);
                return JsonSerializer.Serialize(doc.RootElement, PrettyJson);
            }
            catch
            {
                return raw;
            }
        }
    }
}
