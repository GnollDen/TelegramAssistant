using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage5;

public class DailyKnowledgeCrystallizationWorkerService : BackgroundService
{
    private const string LastFinalizedDayKey = "stage5:cold_path:last_finalized_day";
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeOnly RunTimeLocal = new(3, 0);
    private static readonly Regex CyrillicRegex = new(@"[\p{IsCyrillic}]", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly AnalysisSettings _settings;
    private readonly IChatSessionRepository _chatSessionRepository;
    private readonly IIntelligenceRepository _intelligenceRepository;
    private readonly IChatDialogSummaryRepository _summaryRepository;
    private readonly IAnalysisStateRepository _stateRepository;
    private readonly OpenRouterAnalysisService _analysisService;
    private readonly SummaryHistoricalRetrievalService _historicalRetrievalService;
    private readonly IExtractionErrorRepository _extractionErrorRepository;
    private readonly ILogger<DailyKnowledgeCrystallizationWorkerService> _logger;

    public DailyKnowledgeCrystallizationWorkerService(
        IOptions<AnalysisSettings> settings,
        IChatSessionRepository chatSessionRepository,
        IIntelligenceRepository intelligenceRepository,
        IChatDialogSummaryRepository summaryRepository,
        IAnalysisStateRepository stateRepository,
        OpenRouterAnalysisService analysisService,
        SummaryHistoricalRetrievalService historicalRetrievalService,
        IExtractionErrorRepository extractionErrorRepository,
        ILogger<DailyKnowledgeCrystallizationWorkerService> logger)
    {
        _settings = settings.Value;
        _chatSessionRepository = chatSessionRepository;
        _intelligenceRepository = intelligenceRepository;
        _summaryRepository = summaryRepository;
        _stateRepository = stateRepository;
        _analysisService = analysisService;
        _historicalRetrievalService = historicalRetrievalService;
        _extractionErrorRepository = extractionErrorRepository;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled || !_settings.SummaryEnabled)
        {
            _logger.LogInformation("Stage5 cold-path worker is disabled");
            return;
        }

        _logger.LogInformation("Stage5 cold-path worker started. local_run_time={RunTime}", RunTimeLocal);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.Local);
                var nextRunLocal = ResolveNextRunLocal(nowLocal);
                _logger.LogInformation("Stage5 cold-path next run scheduled at local={NextRunLocal}", nextRunLocal);
                await DelayUntilAsync(nextRunLocal, stoppingToken);

                await RunDailyCrystallizationAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Stage5 cold-path loop failed");
                await _extractionErrorRepository.LogAsync(
                    stage: "stage5_cold_path_loop",
                    reason: ex.Message,
                    payload: ex.GetType().Name,
                    ct: stoppingToken);
                await Task.Delay(PollInterval, stoppingToken);
            }
        }
    }

    private async Task RunDailyCrystallizationAsync(CancellationToken ct)
    {
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.Local);
        var targetDayLocal = DateOnly.FromDateTime(nowLocal.Date).AddDays(-1);
        var lastFinalizedDayNumber = await _stateRepository.GetWatermarkAsync(LastFinalizedDayKey, ct);
        if (targetDayLocal.DayNumber <= lastFinalizedDayNumber)
        {
            _logger.LogInformation(
                "Stage5 cold-path skip. day already finalized or not ready: target_day={TargetDay}, last_finalized_day_number={LastFinalizedDayNumber}",
                targetDayLocal,
                lastFinalizedDayNumber);
            return;
        }

        var (dayStartUtc, dayEndUtc) = ResolveUtcBoundsForLocalDay(targetDayLocal);
        var sessionsByChat = await _chatSessionRepository.GetByPeriodAsync(dayStartUtc, dayEndUtc, ct);
        if (sessionsByChat.Count == 0)
        {
            _logger.LogInformation(
                "Stage5 cold-path no sessions found for day. target_day={TargetDay}, utc_start={UtcStart}, utc_end={UtcEnd}",
                targetDayLocal,
                dayStartUtc,
                dayEndUtc);
            await _stateRepository.SetWatermarkAsync(LastFinalizedDayKey, targetDayLocal.DayNumber, ct);
            return;
        }

        foreach (var (chatId, sessions) in sessionsByChat.OrderBy(x => x.Key))
        {
            try
            {
                var sessionSummaries = sessions
                    .Where(x => !string.IsNullOrWhiteSpace(x.Summary))
                    .OrderBy(x => x.StartDate)
                    .ToList();
                if (sessionSummaries.Count == 0)
                {
                    _logger.LogInformation(
                        "Stage5 cold-path skip chat with empty session summaries. chat_id={ChatId}, day={TargetDay}",
                        chatId,
                        targetDayLocal);
                    continue;
                }

                var existingDaySummary = await _summaryRepository.GetByScopeAsync(
                    chatId,
                    ChatDialogSummaryType.Day,
                    dayStartUtc,
                    dayEndUtc,
                    ct);
                if (existingDaySummary?.IsFinalized == true)
                {
                    _logger.LogInformation(
                        "Stage5 cold-path skip finalized day summary. chat_id={ChatId}, day={TargetDay}",
                        chatId,
                        targetDayLocal);
                    continue;
                }

                var claims = await _intelligenceRepository.GetClaimsByChatAndPeriodAsync(
                    chatId,
                    dayStartUtc,
                    dayEndUtc,
                    limit: 120,
                    ct: ct);

                var finalSummary = await BuildFinalDailySummaryAsync(chatId, targetDayLocal, sessionSummaries, claims, ct);
                if (string.IsNullOrWhiteSpace(finalSummary))
                {
                    finalSummary = BuildFallbackFinalSummary(sessionSummaries, claims);
                }

                if (!ContainsCyrillic(finalSummary))
                {
                    _logger.LogWarning(
                        "Stage5 cold-path final daily summary has no Cyrillic symbols. chat_id={ChatId}, day={TargetDay}",
                        chatId,
                        targetDayLocal);
                }

                await _summaryRepository.UpsertAsync(new ChatDialogSummary
                {
                    ChatId = chatId,
                    SummaryType = ChatDialogSummaryType.Day,
                    PeriodStart = dayStartUtc,
                    PeriodEnd = dayEndUtc,
                    StartMessageId = existingDaySummary?.StartMessageId ?? 0,
                    EndMessageId = existingDaySummary?.EndMessageId ?? 0,
                    MessageCount = sessionSummaries.Count,
                    Summary = finalSummary,
                    IsFinalized = true
                }, ct);

                await _historicalRetrievalService.UpsertDailyFinalSummaryEmbeddingAsync(
                    chatId,
                    targetDayLocal,
                    finalSummary,
                    ct);

                _logger.LogInformation(
                    "Stage5 cold-path finalized day summary. chat_id={ChatId}, day={TargetDay}, sessions={SessionCount}, claims={ClaimCount}",
                    chatId,
                    targetDayLocal,
                    sessionSummaries.Count,
                    claims.Count);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Stage5 cold-path chat finalization failed. chat_id={ChatId}, day={TargetDay}",
                    chatId,
                    targetDayLocal);
                await _extractionErrorRepository.LogAsync(
                    stage: "stage5_cold_path_chat",
                    reason: ex.Message,
                    payload: $"chat_id={chatId};day={targetDayLocal:yyyy-MM-dd}",
                    ct: ct);
            }
        }

        await _stateRepository.SetWatermarkAsync(LastFinalizedDayKey, targetDayLocal.DayNumber, ct);
    }

    private async Task<string> BuildFinalDailySummaryAsync(
        long chatId,
        DateOnly day,
        List<ChatSession> sessions,
        List<IntelligenceClaim> claims,
        CancellationToken ct)
    {
        var model = string.IsNullOrWhiteSpace(_settings.SummaryModel)
            ? _settings.ExpensiveModel
            : _settings.SummaryModel;
        var userPrompt = BuildFinalizationPrompt(chatId, day, sessions, claims);

        try
        {
            var raw = await _analysisService.CompleteTextAsync(
                model,
                FinalSummarySystemPrompt,
                userPrompt,
                Math.Max(300, _settings.SummaryMaxTokens),
                ct);
            return ExtractFinalSummary(raw);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Stage5 cold-path LLM finalization failed. chat_id={ChatId}, day={Day}",
                chatId,
                day);
            return string.Empty;
        }
    }

    private static string BuildFinalizationPrompt(
        long chatId,
        DateOnly day,
        IReadOnlyCollection<ChatSession> sessions,
        IReadOnlyCollection<IntelligenceClaim> claims)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"[meta] chat_id={chatId} day={day:yyyy-MM-dd} sessions={sessions.Count} claims={claims.Count}");
        sb.AppendLine("[episodic_summaries]");
        foreach (var session in sessions.OrderBy(x => x.StartDate).Take(64))
        {
            var compact = MessageContentBuilder.TruncateForContext(
                MessageContentBuilder.CollapseWhitespace(session.Summary),
                420);
            if (string.IsNullOrWhiteSpace(compact))
            {
                continue;
            }

            sb.AppendLine($"- session_index={session.SessionIndex}; period={session.StartDate:O}..{session.EndDate:O}; summary=\"{compact}\"");
        }

        sb.AppendLine("[/episodic_summaries]");
        sb.AppendLine("[key_claims]");
        var claimLines = claims
            .OrderByDescending(x => x.Confidence)
            .Select(x =>
                MessageContentBuilder.CollapseWhitespace(
                    $"{x.EntityName} | {x.Category}:{x.Key}={x.Value} | confidence={x.Confidence:0.00}"))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(64)
            .ToList();
        if (claimLines.Count == 0)
        {
            sb.AppendLine("- none");
        }
        else
        {
            foreach (var line in claimLines)
            {
                sb.AppendLine($"- {line}");
            }
        }

        sb.AppendLine("[/key_claims]");
        return sb.ToString();
    }

    private static string BuildFallbackFinalSummary(
        IReadOnlyCollection<ChatSession> sessions,
        IReadOnlyCollection<IntelligenceClaim> claims)
    {
        var sessionPart = sessions
            .OrderBy(x => x.StartDate)
            .Select(x => MessageContentBuilder.TruncateForContext(MessageContentBuilder.CollapseWhitespace(x.Summary), 240))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Take(6);
        var claimsPart = claims
            .OrderByDescending(x => x.Confidence)
            .Select(x => $"{x.EntityName}: {x.Category}/{x.Key}={x.Value}")
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(6);

        var merged = sessionPart.Concat(claimsPart).ToList();
        return merged.Count == 0
            ? "Итог дня недоступен."
            : MessageContentBuilder.CollapseWhitespace(string.Join(" | ", merged));
    }

    private static string ExtractFinalSummary(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.TryGetProperty("summary", out var summaryNode) && summaryNode.ValueKind == JsonValueKind.String)
            {
                return MessageContentBuilder.CollapseWhitespace(summaryNode.GetString() ?? string.Empty);
            }
        }
        catch (JsonException)
        {
            // fallback to plain text
        }

        return MessageContentBuilder.CollapseWhitespace(raw);
    }

    private static bool ContainsCyrillic(string value)
    {
        return !string.IsNullOrWhiteSpace(value) && CyrillicRegex.IsMatch(value);
    }

    private static (DateTime startUtc, DateTime endUtc) ResolveUtcBoundsForLocalDay(DateOnly localDay)
    {
        var dayStartLocal = localDay.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var nextDayStartLocal = localDay.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);

        var dayStartUtc = TimeZoneInfo.ConvertTimeToUtc(dayStartLocal, TimeZoneInfo.Local);
        var nextDayStartUtc = TimeZoneInfo.ConvertTimeToUtc(nextDayStartLocal, TimeZoneInfo.Local);
        var dayEndUtc = nextDayStartUtc.AddTicks(-1);
        return (dayStartUtc, dayEndUtc);
    }

    private static DateTime ResolveNextRunLocal(DateTime nowLocal)
    {
        var todayRun = nowLocal.Date.Add(RunTimeLocal.ToTimeSpan());
        return nowLocal < todayRun
            ? todayRun
            : todayRun.AddDays(1);
    }

    private static async Task DelayUntilAsync(DateTime targetLocalTime, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.Local);
            var remaining = targetLocalTime - nowLocal;
            if (remaining <= TimeSpan.Zero)
            {
                return;
            }

            var delay = remaining < PollInterval ? remaining : PollInterval;
            await Task.Delay(delay, ct);
        }
    }

    private const string FinalSummarySystemPrompt = """
You are a nightly memory crystallization module.
Return ONLY JSON object: {"summary":"..."}.

Task:
- merge episodic summaries and key claims into one final daily dossier summary
- keep durable facts, commitments, plan/status changes, conflicts, contacts, finance/work/health/location updates
- remove noise and duplicates
- preserve names as in source
- structure output as coherent day narrative with key outcomes and unresolved items
- write in Russian only (Cyrillic)
- no markdown, no extra fields
""";
}
