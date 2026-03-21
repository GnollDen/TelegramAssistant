namespace TgAssistant.Core.Models;

public readonly record struct CaseScope(long CaseId, long ChatId);

public static class CaseScopeFactory
{
    public static CaseScope CreateSmokeScope(string scenario)
    {
        var scenarioHash = Math.Abs(scenario.GetHashCode(StringComparison.Ordinal)) % 1000;
        var unixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() % 1_000_000_000L;

        var caseId = 8_000_000_000_000L + (scenarioHash * 1_000_000L) + unixMs;
        var chatId = 9_000_000_000_000L + (scenarioHash * 1_000_000L) + unixMs;
        return new CaseScope(caseId, chatId);
    }
}
