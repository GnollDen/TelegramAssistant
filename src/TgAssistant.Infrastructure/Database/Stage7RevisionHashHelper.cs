using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace TgAssistant.Infrastructure.Database;

public static class Stage7RevisionHashHelper
{
    public static string Compute(params string?[] parts)
    {
        var normalized = string.Join("|", parts.Select(x => x ?? string.Empty));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }

    public static string FormatFloat(float value)
        => value.ToString("0.0000", CultureInfo.InvariantCulture);

    public static string FormatDateTime(DateTime? value)
        => value?.ToUniversalTime().ToString("O") ?? string.Empty;
}
