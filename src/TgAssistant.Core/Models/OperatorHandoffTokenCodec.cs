using System.Security.Cryptography;
using System.Text;
using TgAssistant.Core.Configuration;

namespace TgAssistant.Core.Models;

public static class OperatorHandoffTokenCodec
{
    public const string TelegramResolutionContext = "telegram_resolution_detail";
    public const string AssistantResolutionContext = "assistant_resolution_detail";
    private const string Version = "v1";

    public static string? ResolveSigningSecret(WebSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var explicitSecret = NormalizeOptional(settings.HandoffSigningKey);
        if (!string.IsNullOrWhiteSpace(explicitSecret))
        {
            return explicitSecret;
        }

        var accessTokenSecret = NormalizeOptional(settings.OperatorAccessToken);
        return string.IsNullOrWhiteSpace(accessTokenSecret) ? null : accessTokenSecret;
    }

    public static string CreateToken(
        string context,
        Guid trackedPersonId,
        string scopeItemKey,
        string sourceOperatorSessionId,
        string signingSecret,
        DateTime issuedAtUtc)
    {
        var normalizedContext = NormalizeRequired(context);
        var normalizedScopeItemKey = NormalizeRequired(scopeItemKey);
        var normalizedSessionId = NormalizeRequired(sourceOperatorSessionId);
        var normalizedSecret = NormalizeRequired(signingSecret);
        var normalizedIssuedAt = issuedAtUtc == default ? DateTime.UtcNow : issuedAtUtc.ToUniversalTime();
        var issuedUnixSeconds = new DateTimeOffset(normalizedIssuedAt).ToUnixTimeSeconds();

        var payload = string.Join(
            "|",
            Version,
            normalizedContext,
            trackedPersonId.ToString("D"),
            normalizedScopeItemKey,
            normalizedSessionId,
            issuedUnixSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var payloadEncoded = Base64UrlEncode(Encoding.UTF8.GetBytes(payload));
        var signatureEncoded = ComputeSignature(payloadEncoded, normalizedSecret);
        return $"{payloadEncoded}.{signatureEncoded}";
    }

    public static bool TryValidateToken(
        string token,
        string expectedContext,
        Guid expectedTrackedPersonId,
        string expectedScopeItemKey,
        string expectedSourceOperatorSessionId,
        string signingSecret,
        int tokenTtlMinutes)
    {
        if (string.IsNullOrWhiteSpace(token)
            || string.IsNullOrWhiteSpace(signingSecret)
            || string.IsNullOrWhiteSpace(expectedContext)
            || expectedTrackedPersonId == Guid.Empty
            || string.IsNullOrWhiteSpace(expectedScopeItemKey)
            || string.IsNullOrWhiteSpace(expectedSourceOperatorSessionId))
        {
            return false;
        }

        var parts = token.Split('.', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
        {
            return false;
        }

        var payloadEncoded = parts[0].Trim();
        var signatureEncoded = parts[1].Trim();
        var expectedSignature = ComputeSignature(payloadEncoded, signingSecret.Trim());
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(signatureEncoded),
                Encoding.UTF8.GetBytes(expectedSignature)))
        {
            return false;
        }

        string payloadText;
        try
        {
            payloadText = Encoding.UTF8.GetString(Base64UrlDecode(payloadEncoded));
        }
        catch
        {
            return false;
        }

        var payloadParts = payloadText.Split('|');
        if (payloadParts.Length != 6)
        {
            return false;
        }

        if (!string.Equals(payloadParts[0], Version, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.Equals(payloadParts[1], expectedContext.Trim(), StringComparison.Ordinal))
        {
            return false;
        }

        if (!Guid.TryParse(payloadParts[2], out var payloadTrackedPersonId)
            || payloadTrackedPersonId != expectedTrackedPersonId)
        {
            return false;
        }

        if (!string.Equals(payloadParts[3], expectedScopeItemKey.Trim(), StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.Equals(payloadParts[4], expectedSourceOperatorSessionId.Trim(), StringComparison.Ordinal))
        {
            return false;
        }

        if (!long.TryParse(payloadParts[5], out var payloadIssuedUnixSeconds))
        {
            return false;
        }

        var issuedAtUtc = DateTimeOffset.FromUnixTimeSeconds(payloadIssuedUnixSeconds).UtcDateTime;
        var ttl = Math.Clamp(tokenTtlMinutes, 1, 24 * 60);
        return issuedAtUtc >= DateTime.UtcNow.AddMinutes(-ttl)
            && issuedAtUtc <= DateTime.UtcNow.AddMinutes(5);
    }

    private static string ComputeSignature(string payloadEncoded, string signingSecret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(signingSecret));
        var bytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(payloadEncoded));
        return Base64UrlEncode(bytes);
    }

    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string encoded)
    {
        var normalized = encoded.Replace('-', '+').Replace('_', '/');
        var padding = 4 - (normalized.Length % 4);
        if (padding is > 0 and < 4)
        {
            normalized = normalized + new string('=', padding);
        }

        return Convert.FromBase64String(normalized);
    }

    private static string NormalizeRequired(string value)
        => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value is required.", nameof(value)) : value.Trim();

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
