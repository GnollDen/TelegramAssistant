using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Interfaces;

namespace TgAssistant.Intelligence.Stage5;

public class EditDiffAnalysisWorkerService : BackgroundService
{
    private readonly AnalysisSettings _settings;
    private readonly IMessageRepository _messageRepository;
    private readonly OpenRouterAnalysisService _analysisService;
    private readonly IExtractionErrorRepository _errorRepository;
    private readonly ILogger<EditDiffAnalysisWorkerService> _logger;

    public EditDiffAnalysisWorkerService(
        IOptions<AnalysisSettings> settings,
        IMessageRepository messageRepository,
        OpenRouterAnalysisService analysisService,
        IExtractionErrorRepository errorRepository,
        ILogger<EditDiffAnalysisWorkerService> logger)
    {
        _settings = settings.Value;
        _messageRepository = messageRepository;
        _analysisService = analysisService;
        _errorRepository = errorRepository;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled || !_settings.EditDiffEnabled)
        {
            _logger.LogInformation(
                "Edit-diff worker is disabled. analysis_enabled={AnalysisEnabled}, edit_diff_enabled={EditDiffEnabled}",
                _settings.Enabled,
                _settings.EditDiffEnabled);
            return;
        }

        _logger.LogInformation(
            "Edit-diff worker started. batch_size={BatchSize}, poll_s={PollSeconds}, model={Model}",
            Math.Max(1, _settings.EditDiffBatchSize),
            Math.Max(1, _settings.EditDiffPollIntervalSeconds),
            ResolveModel());

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var candidates = await _messageRepository.GetPendingEditDiffCandidatesAsync(
                    Math.Max(1, _settings.EditDiffBatchSize),
                    stoppingToken);

                if (candidates.Count == 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, _settings.EditDiffPollIntervalSeconds)), stoppingToken);
                    continue;
                }

                var processed = 0;
                foreach (var candidate in candidates)
                {
                    var analysis = await AnalyzeAsync(candidate, stoppingToken);
                    await _messageRepository.SaveEditDiffAnalysisAsync(
                        candidate.MessageId,
                        analysis.Classification,
                        analysis.Summary,
                        analysis.ShouldAffectMemory,
                        analysis.AddedImportant,
                        analysis.RemovedImportant,
                        analysis.Confidence,
                        stoppingToken);
                    processed++;
                }

                _logger.LogInformation("Edit-diff pass done: processed={Count}", processed);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Edit-diff worker loop failed");
                await _errorRepository.LogAsync(
                    stage: "stage5_edit_diff_loop",
                    reason: ex.Message,
                    payload: ex.GetType().Name,
                    ct: stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(2, _settings.EditDiffPollIntervalSeconds)), stoppingToken);
            }
        }
    }

    private async Task<EditDiffAnalysis> AnalyzeAsync(EditDiffCandidate candidate, CancellationToken ct)
    {
        var systemPrompt = """
You analyze Telegram message edits for memory impact.
Return ONLY JSON with fields:
- classification: typo | formatting | minor_rephrase | meaning_changed | important_added | important_removed | message_deleted | unknown
- summary: concise Russian summary of what changed (max 220 chars)
- should_affect_memory: boolean
- added_important: boolean
- removed_important: boolean
- confidence: number 0..1
Rules:
- Pure typo/punctuation/casing fixes => should_affect_memory=false
- If meaningful facts/time/place/person/commitments changed => should_affect_memory=true
- If deletion removed meaningful content => removed_important=true, should_affect_memory=true
""";

        var userPrompt = $"""
chat_id: {candidate.ChatId}
message_id: {candidate.MessageId}
edited_at_utc: {candidate.EditedAtUtc:O}

BEFORE:
{candidate.BeforeText}

AFTER:
{candidate.AfterText}
""";

        var raw = await _analysisService.CompleteTextAsync(
            ResolveModel(),
            systemPrompt,
            userPrompt,
            Math.Clamp(_settings.EditDiffMaxTokens, 128, 800),
            ct);

        return ParseAnalysis(raw, candidate);
    }

    private static EditDiffAnalysis ParseAnalysis(string raw, EditDiffCandidate candidate)
    {
        if (!string.IsNullOrWhiteSpace(raw))
        {
            try
            {
                using var doc = JsonDocument.Parse(raw);
                var root = doc.RootElement;
                var classification = root.TryGetProperty("classification", out var c) && c.ValueKind == JsonValueKind.String
                    ? (c.GetString() ?? "unknown").Trim()
                    : "unknown";
                var summary = root.TryGetProperty("summary", out var s) && s.ValueKind == JsonValueKind.String
                    ? (s.GetString() ?? string.Empty).Trim()
                    : string.Empty;
                var shouldAffect = root.TryGetProperty("should_affect_memory", out var m) && m.ValueKind is JsonValueKind.True or JsonValueKind.False
                    ? m.GetBoolean()
                    : false;
                var addedImportant = root.TryGetProperty("added_important", out var a) && a.ValueKind is JsonValueKind.True or JsonValueKind.False
                    ? a.GetBoolean()
                    : false;
                var removedImportant = root.TryGetProperty("removed_important", out var r) && r.ValueKind is JsonValueKind.True or JsonValueKind.False
                    ? r.GetBoolean()
                    : false;
                var confidence = root.TryGetProperty("confidence", out var conf) && conf.ValueKind == JsonValueKind.Number
                    ? Math.Clamp((float)conf.GetDouble(), 0f, 1f)
                    : 0.6f;

                if (string.IsNullOrWhiteSpace(summary))
                {
                    summary = BuildFallbackSummary(candidate, classification, shouldAffect, addedImportant, removedImportant);
                }

                return new EditDiffAnalysis(
                    string.IsNullOrWhiteSpace(classification) ? "unknown" : classification,
                    summary,
                    shouldAffect,
                    addedImportant,
                    removedImportant,
                    confidence);
            }
            catch (JsonException)
            {
                // fall through
            }
        }

        return FallbackAnalysis(candidate);
    }

    private static EditDiffAnalysis FallbackAnalysis(EditDiffCandidate candidate)
    {
        var deleted = string.Equals(candidate.AfterText?.Trim(), "[DELETED]", StringComparison.OrdinalIgnoreCase);
        if (deleted)
        {
            return new EditDiffAnalysis(
                "message_deleted",
                "Сообщение удалено после публикации; возможное сокрытие значимого контекста.",
                true,
                false,
                true,
                0.8f);
        }

        var before = (candidate.BeforeText ?? string.Empty).Trim();
        var after = (candidate.AfterText ?? string.Empty).Trim();
        if (string.Equals(before, after, StringComparison.OrdinalIgnoreCase))
        {
            return new EditDiffAnalysis("formatting", "Незначительная правка формата/регистра.", false, false, false, 0.7f);
        }

        return new EditDiffAnalysis(
            "meaning_changed",
            "Сообщение было отредактировано; возможны смысловые изменения.",
            true,
            false,
            false,
            0.55f);
    }

    private static string BuildFallbackSummary(
        EditDiffCandidate candidate,
        string classification,
        bool shouldAffectMemory,
        bool addedImportant,
        bool removedImportant)
    {
        if (string.Equals(classification, "message_deleted", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(candidate.AfterText?.Trim(), "[DELETED]", StringComparison.OrdinalIgnoreCase))
        {
            return "Сообщение удалено после публикации; возможное сокрытие значимого контекста.";
        }

        if (!shouldAffectMemory && !addedImportant && !removedImportant)
        {
            return "Редактирование похоже на косметическую правку без изменения смысла.";
        }

        return "Редактирование содержит потенциально значимое изменение содержания.";
    }

    private string ResolveModel()
    {
        if (!string.IsNullOrWhiteSpace(_settings.CheapModel))
        {
            return _settings.CheapModel.Trim();
        }

        return !string.IsNullOrWhiteSpace(_settings.CheapBaselineModel)
            ? _settings.CheapBaselineModel.Trim()
            : "openai/gpt-4o-mini";
    }

    private sealed record EditDiffAnalysis(
        string Classification,
        string Summary,
        bool ShouldAffectMemory,
        bool AddedImportant,
        bool RemovedImportant,
        float Confidence);
}
