using System.Security.Cryptography;
using System.Text;

namespace TgAssistant.Core.Prompts;

public static class PromptTemplateChecksum
{
    public static string Compute(string systemPrompt)
    {
        var normalized = Normalize(systemPrompt);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes);
    }

    public static string Normalize(string? systemPrompt)
    {
        if (string.IsNullOrWhiteSpace(systemPrompt))
        {
            return string.Empty;
        }

        return systemPrompt
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Trim();
    }
}
