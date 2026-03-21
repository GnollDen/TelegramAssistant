using System.Net;

namespace TgAssistant.Core.Models;

public static class BudgetErrorClassifier
{
    public static bool IsQuotaLike(HttpStatusCode statusCode, string? body)
    {
        if (statusCode == HttpStatusCode.PaymentRequired)
        {
            return true;
        }

        if (statusCode is not (HttpStatusCode.TooManyRequests or HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized or HttpStatusCode.BadRequest))
        {
            return false;
        }

        return IsQuotaLike(body);
    }

    public static bool IsQuotaLike(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var normalized = text.ToLowerInvariant();
        return normalized.Contains("insufficient", StringComparison.Ordinal)
               || normalized.Contains("credit", StringComparison.Ordinal)
               || normalized.Contains("quota", StringComparison.Ordinal)
               || normalized.Contains("balance", StringComparison.Ordinal)
               || normalized.Contains("billing", StringComparison.Ordinal)
               || normalized.Contains("payment", StringComparison.Ordinal)
               || normalized.Contains("funds", StringComparison.Ordinal)
               || normalized.Contains("exhausted", StringComparison.Ordinal)
               || normalized.Contains("limit reached", StringComparison.Ordinal)
               || normalized.Contains("out of credits", StringComparison.Ordinal);
    }
}
