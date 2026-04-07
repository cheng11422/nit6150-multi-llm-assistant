using Xunit;
using MultiLLMProjectAssistant.Core.Services;

namespace MultiLLMProjectAssistant.Tests;

public class RedactionFilterTests
{
    [Fact]
    public void Redact_OpenAIKey_IsRedacted()
    {
        var text = "My key is sk-abc123def456ghi789jkl012mno345pqr678";
        var result = RedactionFilter.Redact(text);

        Assert.DoesNotContain("sk-", result);
        Assert.Contains("[REDACTED]", result);
    }

    [Fact]
    public void Redact_GeminiKey_IsRedacted()
    {
        var text = "Key: AIzaSyA1B2C3D4E5F6G7H8I9J0KlMnOpQrStUvWxYz12";
        var result = RedactionFilter.Redact(text);

        Assert.DoesNotContain("AIza", result);
        Assert.Contains("[REDACTED]", result);
    }

    [Fact]
    public void Redact_GrokKey_IsRedacted()
    {
        var text = "Token: xai-abc123def456ghi789jkl012mno345";
        var result = RedactionFilter.Redact(text);

        Assert.DoesNotContain("xai-", result);
        Assert.Contains("[REDACTED]", result);
    }

    [Fact]
    public void Redact_BearerToken_IsRedacted()
    {
        var text = "Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9abcdef";
        var result = RedactionFilter.Redact(text);

        Assert.DoesNotContain("Bearer eyJ", result);
        Assert.Contains("[REDACTED]", result);
    }

    [Fact]
    public void Redact_MultipleKeysInOneString_AllRedacted()
    {
        var text = "OpenAI: sk-abc123def456ghi789jkl012mno345pqr678 " +
                   "Gemini: AIzaSyA1B2C3D4E5F6G7H8I9J0KlMnOpQrStUvWxYz12 " +
                   "Grok: xai-abc123def456ghi789jkl012mno345";
        var result = RedactionFilter.Redact(text);

        Assert.DoesNotContain("sk-", result);
        Assert.DoesNotContain("AIza", result);
        Assert.DoesNotContain("xai-", result);
        Assert.Equal(3, result.Split("[REDACTED]").Length - 1);
    }

    [Fact]
    public void Redact_NoKeys_ReturnsUnchanged()
    {
        var text = "This is a normal log message with no secrets.";
        var result = RedactionFilter.Redact(text);

        Assert.Equal(text, result);
    }

    [Fact]
    public void Redact_Null_ReturnsEmpty()
    {
        var result = RedactionFilter.Redact(null);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Redact_PartialKeyTooShort_NotMatched()
    {
        // "sk-" followed by only 10 chars — should NOT match (requires 20+)
        var text = "short key: sk-abc1234567";
        var result = RedactionFilter.Redact(text);

        Assert.Contains("sk-abc1234567", result);
        Assert.DoesNotContain("[REDACTED]", result);
    }
}
