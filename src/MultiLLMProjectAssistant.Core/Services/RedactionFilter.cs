using System.Text.RegularExpressions;

namespace MultiLLMProjectAssistant.Core.Services;

public static class RedactionFilter
{
    private const string Replacement = "[REDACTED]";

    private static readonly Regex[] Patterns =
    {
        new(@"sk-[a-zA-Z0-9_-]{20,}", RegexOptions.Compiled),
        new(@"AIza[a-zA-Z0-9_-]{35}", RegexOptions.Compiled),
        new(@"xai-[a-zA-Z0-9_-]{20,}", RegexOptions.Compiled),
        new(@"Bearer\s+[A-Za-z0-9_-]{20,}", RegexOptions.Compiled),
    };

    public static string Redact(string? text)
    {
        if (text is null)
            return string.Empty;

        foreach (var pattern in Patterns)
        {
            text = pattern.Replace(text, Replacement);
        }

        return text;
    }
}
