using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage5;

public class SummaryHistoricalRetrievalService
{
    private const string DefaultEmbeddingModel = "text-embedding-3-small";
    private const string DailyFinalOwnerTypePrefix = "priority:chat_daily_final";

    private readonly AnalysisSettings _analysisSettings;
    private readonly EmbeddingSettings _embeddingSettings;
    private readonly ITextEmbeddingGenerator _embeddingGenerator;
    private readonly IEmbeddingRepository _embeddingRepository;
    private readonly IIntelligenceRepository _intelligenceRepository;
    private readonly ILogger<SummaryHistoricalRetrievalService> _logger;

    public SummaryHistoricalRetrievalService(
        IOptions<AnalysisSettings> analysisSettings,
        IOptions<EmbeddingSettings> embeddingSettings,
        ITextEmbeddingGenerator embeddingGenerator,
        IEmbeddingRepository embeddingRepository,
        IIntelligenceRepository intelligenceRepository,
        ILogger<SummaryHistoricalRetrievalService> logger)
    {
        _analysisSettings = analysisSettings.Value;
        _embeddingSettings = embeddingSettings.Value;
        _embeddingGenerator = embeddingGenerator;
        _embeddingRepository = embeddingRepository;
        _intelligenceRepository = intelligenceRepository;
        _logger = logger;
    }

    public async Task<List<SummaryHistoricalHint>> GetHintsAsync(
        long chatId,
        int currentSessionIndex,
        List<Message> sessionMessages,
        CancellationToken ct)
    {
        if (!_analysisSettings.SummaryHistoricalHintsEnabled ||
            currentSessionIndex <= 0 ||
            sessionMessages.Count == 0)
        {
            return [];
        }

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(Math.Max(250, _analysisSettings.SummaryHistoricalHintsTimeoutMs));
            var embeddingModel = ResolveEmbeddingModel();

            var query = await BuildQueryAsync(sessionMessages, timeoutCts.Token);
            if (string.IsNullOrWhiteSpace(query))
            {
                return [];
            }

            var vector = await _embeddingGenerator.GenerateAsync(embeddingModel, query, timeoutCts.Token);
            if (vector.Length == 0)
            {
                return [];
            }

            var ownerType = BuildOwnerType(chatId);
            var nearest = await _embeddingRepository.FindNearestAsync(
                ownerType,
                embeddingModel,
                vector,
                Math.Max(1, _analysisSettings.SummaryHistoricalHintsCandidatePool),
                timeoutCts.Token);

            return nearest
                .Select(candidate => new
                {
                    Candidate = candidate,
                    SessionIndex = TryParseSessionIndex(candidate.OwnerId),
                    Similarity = Cosine(vector, candidate.Vector)
                })
                .Where(x => x.SessionIndex.HasValue
                            && x.SessionIndex.Value >= 0
                            && x.SessionIndex.Value < currentSessionIndex
                            && x.Similarity >= _analysisSettings.SummaryHistoricalHintsMinSimilarity)
                .OrderByDescending(x => x.Similarity)
                .ThenByDescending(x => x.SessionIndex)
                .Take(Math.Max(1, _analysisSettings.SummaryHistoricalHintsTopK))
                .Select(x => new SummaryHistoricalHint
                {
                    SessionIndex = x.SessionIndex!.Value,
                    Similarity = x.Similarity,
                    Summary = MessageContentBuilder.TruncateForContext(
                        MessageContentBuilder.CollapseWhitespace(x.Candidate.SourceText),
                        Math.Max(80, _analysisSettings.SummaryHistoricalHintsMaxCharsPerItem))
                })
                .ToList();
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogDebug(
                "Stage5 summary historical retrieval timed out: chat_id={ChatId}, session_index={SessionIndex}",
                chatId,
                currentSessionIndex);
            return [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Stage5 summary historical retrieval failed: chat_id={ChatId}, session_index={SessionIndex}",
                chatId,
                currentSessionIndex);
            return [];
        }
    }

    public async Task UpsertSessionSummaryEmbeddingAsync(
        long chatId,
        int sessionIndex,
        string summary,
        CancellationToken ct)
    {
        if (!_analysisSettings.SummaryHistoricalHintsEnabled || string.IsNullOrWhiteSpace(summary))
        {
            return;
        }

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(Math.Max(250, _analysisSettings.SummaryHistoricalHintsTimeoutMs));
            var embeddingModel = ResolveEmbeddingModel();

            var compact = MessageContentBuilder.CollapseWhitespace(summary);
            var vector = await _embeddingGenerator.GenerateAsync(embeddingModel, compact, timeoutCts.Token);
            if (vector.Length == 0)
            {
                return;
            }

            await _embeddingRepository.UpsertAsync(new TextEmbedding
            {
                OwnerType = BuildOwnerType(chatId),
                OwnerId = sessionIndex.ToString(),
                SourceText = compact,
                Model = embeddingModel,
                Vector = vector,
                CreatedAt = DateTime.UtcNow
            }, timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogDebug(
                "Stage5 summary embedding upsert timed out: chat_id={ChatId}, session_index={SessionIndex}",
                chatId,
                sessionIndex);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Stage5 summary embedding upsert failed: chat_id={ChatId}, session_index={SessionIndex}",
                chatId,
                sessionIndex);
        }
    }

    public async Task UpsertDailyFinalSummaryEmbeddingAsync(
        long chatId,
        DateOnly day,
        string summary,
        CancellationToken ct)
    {
        if (!_analysisSettings.SummaryHistoricalHintsEnabled || string.IsNullOrWhiteSpace(summary))
        {
            return;
        }

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(Math.Max(250, _analysisSettings.SummaryHistoricalHintsTimeoutMs));
            var embeddingModel = ResolveEmbeddingModel();

            var compact = MessageContentBuilder.CollapseWhitespace(summary);
            var vector = await _embeddingGenerator.GenerateAsync(embeddingModel, compact, timeoutCts.Token);
            if (vector.Length == 0)
            {
                return;
            }

            await _embeddingRepository.UpsertAsync(new TextEmbedding
            {
                OwnerType = BuildDailyFinalOwnerType(chatId),
                OwnerId = day.ToString("yyyy-MM-dd"),
                SourceText = compact,
                Model = embeddingModel,
                Vector = vector,
                CreatedAt = DateTime.UtcNow
            }, timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogDebug(
                "Stage5 daily final summary embedding upsert timed out: chat_id={ChatId}, day={Day}",
                chatId,
                day);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Stage5 daily final summary embedding upsert failed: chat_id={ChatId}, day={Day}",
                chatId,
                day);
        }
    }

    private async Task<string> BuildQueryAsync(List<Message> sessionMessages, CancellationToken ct)
    {
        var messageIds = sessionMessages.Select(x => x.Id).ToArray();
        var claims = await _intelligenceRepository.GetClaimsByMessagesAsync(messageIds, ct);
        var claimLines = claims
            .OrderByDescending(x => x.Confidence)
            .Select(x => $"{x.EntityName} | {x.Category}:{x.Key}={x.Value}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8);
        var messageLines = sessionMessages
            .OrderByDescending(x => x.Id)
            .Select(MessageContentBuilder.BuildSemanticContent)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => MessageContentBuilder.TruncateForContext(x, 180))
            .Distinct(StringComparer.Ordinal)
            .Take(6);

        var parts = claimLines
            .Concat(messageLines)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();
        if (parts.Count == 0)
        {
            return string.Empty;
        }

        return MessageContentBuilder.TruncateForContext(
            MessageContentBuilder.CollapseWhitespace(string.Join(" | ", parts)),
            Math.Max(400, _analysisSettings.SummaryHistoricalHintsQueryMaxChars));
    }

    private static string BuildOwnerType(long chatId) => $"chat_session_summary:{chatId}";
    private static string BuildDailyFinalOwnerType(long chatId) => $"{DailyFinalOwnerTypePrefix}:{chatId}";
    private string ResolveEmbeddingModel()
    {
        var configured = _embeddingSettings.Model?.Trim();
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        return DefaultEmbeddingModel;
    }

    public Stage5SummaryHistoricalRoutingState GetOperationalState()
    {
        return new Stage5SummaryHistoricalRoutingState
        {
            HistoricalHintsEnabled = _analysisSettings.SummaryHistoricalHintsEnabled,
            EmbeddingModel = ResolveEmbeddingModel(),
            EmbeddingModelFromConfig = !string.IsNullOrWhiteSpace(_embeddingSettings.Model)
        };
    }

    private static int? TryParseSessionIndex(string ownerId)
    {
        return int.TryParse(ownerId, out var value) ? value : null;
    }

    private static float Cosine(float[] a, float[] b)
    {
        if (a.Length == 0 || b.Length == 0 || a.Length != b.Length)
        {
            return -1f;
        }

        double dot = 0;
        double normA = 0;
        double normB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        if (normA <= 0 || normB <= 0)
        {
            return -1f;
        }

        return (float)(dot / (Math.Sqrt(normA) * Math.Sqrt(normB)));
    }
}

public sealed class Stage5SummaryHistoricalRoutingState
{
    public bool HistoricalHintsEnabled { get; init; }
    public string EmbeddingModel { get; init; } = string.Empty;
    public bool EmbeddingModelFromConfig { get; init; }
}
