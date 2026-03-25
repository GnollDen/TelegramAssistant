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
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);
    private static readonly Regex CyrillicRegex = new(@"[\p{IsCyrillic}]", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly AnalysisSettings _settings;
    private readonly AggregationSettings _aggregationSettings;
    private readonly IMessageRepository _messageRepository;
    private readonly IChatSessionRepository _chatSessionRepository;
    private readonly IIntelligenceRepository _intelligenceRepository;
    private readonly IChatDialogSummaryRepository _summaryRepository;
    private readonly IPromptTemplateRepository _promptRepository;
    private readonly OpenRouterAnalysisService _analysisService;
    private readonly SummaryHistoricalRetrievalService _historicalRetrievalService;
    private readonly IExtractionErrorRepository _extractionErrorRepository;
    private readonly ILogger<DailyKnowledgeCrystallizationWorkerService> _logger;

    public DailyKnowledgeCrystallizationWorkerService(
        IOptions<AnalysisSettings> settings,
        IOptions<AggregationSettings> aggregationSettings,
        IMessageRepository messageRepository,
        IChatSessionRepository chatSessionRepository,
        IIntelligenceRepository intelligenceRepository,
        IChatDialogSummaryRepository summaryRepository,
        IPromptTemplateRepository promptRepository,
        OpenRouterAnalysisService analysisService,
        SummaryHistoricalRetrievalService historicalRetrievalService,
        IExtractionErrorRepository extractionErrorRepository,
        ILogger<DailyKnowledgeCrystallizationWorkerService> logger)
    {
        _settings = settings.Value;
        _aggregationSettings = aggregationSettings.Value;
        _messageRepository = messageRepository;
        _chatSessionRepository = chatSessionRepository;
        _intelligenceRepository = intelligenceRepository;
        _summaryRepository = summaryRepository;
        _promptRepository = promptRepository;
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

        _logger.LogInformation(
            "Stage5 cold-path worker started. min_idle_h={MinIdleHours}, archive_threshold_h={ArchiveThresholdHours}",
            Math.Max(1, _aggregationSettings.MinIdleAgeHours),
            Math.Max(1, _aggregationSettings.ArchiveThresholdHours));
        await EnsureDefaultPromptAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunDailyCrystallizationAsync(stoppingToken);
                await Task.Delay(PollInterval, stoppingToken);
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
        var staleBeforeUtc = DateTime.UtcNow.AddHours(-Math.Max(1, _aggregationSettings.MinIdleAgeHours));
        var sessionsByChat = await _chatSessionRepository.GetPendingAggregationCandidatesAsync(staleBeforeUtc, ct);
        if (sessionsByChat.Count == 0)
        {
            _logger.LogInformation(
                "Stage5 cold-path no pending sessions. stale_before_utc={StaleBeforeUtc}",
                staleBeforeUtc);
            return;
        }

        foreach (var (chatId, sessions) in sessionsByChat.OrderBy(x => x.Key))
        {
            try
            {
                var sessionsByLocalDay = sessions
                    .Where(x => !x.IsFinalized && !string.IsNullOrWhiteSpace(x.Summary))
                    .GroupBy(x => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(x.LastMessageAt, TimeZoneInfo.Local)))
                    .ToList();
                if (sessionsByLocalDay.Count == 0)
                {
                    continue;
                }

                foreach (var dayGroup in sessionsByLocalDay)
                {
                    var targetDayLocal = dayGroup.Key;
                    var (dayStartUtc, dayEndUtc) = ResolveUtcBoundsForLocalDay(targetDayLocal);
                    var sessionSummaries = dayGroup
                        .OrderBy(x => x.StartDate)
                        .ToList();
                    if (sessionSummaries.Count == 0)
                    {
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

                    var startMessageId = existingDaySummary?.StartMessageId
                                         ?? await ResolveBoundaryMessageIdAsync(chatId, dayStartUtc, preferMin: true, ct);
                    var endMessageId = existingDaySummary?.EndMessageId
                                       ?? await ResolveBoundaryMessageIdAsync(chatId, dayEndUtc, preferMin: false, ct);

                    await _summaryRepository.UpsertAndFinalizeSessionsAsync(new ChatDialogSummary
                    {
                        ChatId = chatId,
                        SummaryType = ChatDialogSummaryType.Day,
                        PeriodStart = dayStartUtc,
                        PeriodEnd = dayEndUtc,
                        StartMessageId = startMessageId,
                        EndMessageId = endMessageId,
                        MessageCount = sessionSummaries.Count,
                        Summary = finalSummary,
                        IsFinalized = true
                    }, sessionSummaries.Select(x => x.Id).ToArray(), ct);

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
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Stage5 cold-path chat finalization failed. chat_id={ChatId}",
                    chatId);
                await _extractionErrorRepository.LogAsync(
                    stage: "stage5_cold_path_chat",
                    reason: ex.Message,
                    payload: $"chat_id={chatId}",
                    ct: ct);
            }
        }
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
        var promptTemplate = await GetPromptAsync(Stage5PromptCatalog.DailyAggregate, ct);
        var userPrompt = BuildFinalizationPrompt(chatId, day, sessions, claims);

        try
        {
            var raw = await _analysisService.CompleteTextAsync(
                model,
                promptTemplate.SystemPrompt,
                userPrompt,
                Math.Max(300, _settings.SummaryMaxTokens),
                "daily_crystallization",
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

    private async Task EnsureDefaultPromptAsync(CancellationToken ct)
    {
        var contract = Stage5PromptCatalog.DailyAggregate;
        var existing = await _promptRepository.GetByIdAsync(contract.Id, ct);
        var managed = contract.ToTemplate();
        if (existing != null &&
            string.Equals(existing.Version, managed.Version, StringComparison.Ordinal) &&
            string.Equals(existing.Checksum, managed.Checksum, StringComparison.Ordinal) &&
            string.Equals(existing.SystemPrompt, managed.SystemPrompt, StringComparison.Ordinal))
        {
            return;
        }

        await _promptRepository.UpsertAsync(managed, ct);
    }

    private async Task<PromptTemplate> GetPromptAsync(ManagedPromptTemplate contract, CancellationToken ct)
    {
        var prompt = await _promptRepository.GetByIdAsync(contract.Id, ct);
        return prompt ?? contract.ToTemplate();
    }

    private async Task<long> ResolveBoundaryMessageIdAsync(long chatId, DateTime pivotUtc, bool preferMin, CancellationToken ct)
    {
        var fromUtc = pivotUtc.AddHours(-12);
        var toUtc = pivotUtc.AddHours(12);
        var messages = await _messageRepository.GetByChatAndPeriodAsync(chatId, fromUtc, toUtc, 4000, ct);
        if (messages.Count == 0)
        {
            return 0;
        }

        return preferMin
            ? messages.Min(x => x.Id)
            : messages.Max(x => x.Id);
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

}
