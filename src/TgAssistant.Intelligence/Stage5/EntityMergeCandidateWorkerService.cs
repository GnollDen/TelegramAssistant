using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage5;

public class EntityMergeCandidateWorkerService : BackgroundService
{
    private const string EmbeddingOwnerType = "entity_profile";
    private readonly MergeSettings _settings;
    private readonly IEntityMergeRepository _mergeRepository;
    private readonly IEntityRepository _entityRepository;
    private readonly IEmbeddingRepository _embeddingRepository;
    private readonly ILogger<EntityMergeCandidateWorkerService> _logger;

    public EntityMergeCandidateWorkerService(
        IOptions<MergeSettings> settings,
        IEntityMergeRepository mergeRepository,
        IEntityRepository entityRepository,
        IEmbeddingRepository embeddingRepository,
        ILogger<EntityMergeCandidateWorkerService> logger)
    {
        _settings = settings.Value;
        _mergeRepository = mergeRepository;
        _entityRepository = entityRepository;
        _embeddingRepository = embeddingRepository;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled)
        {
            _logger.LogInformation("Entity merge candidate worker is disabled");
            return;
        }

        _logger.LogInformation(
            "Entity merge candidate worker started. poll={Poll}s max_candidates={Max}",
            _settings.PollIntervalSeconds,
            _settings.MaxCandidatesPerRun);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var affected = await _mergeRepository.RefreshAliasMergeCandidatesAsync(_settings.MaxCandidatesPerRun, stoppingToken);
                await _mergeRepository.RecomputeScoresAsync(_settings.MaxCandidatesPerRun, stoppingToken);
                await ProcessAutoMergeAsync(stoppingToken);
                if (affected > 0)
                {
                    _logger.LogInformation("Entity merge candidate refresh done: affected={Count}", affected);
                }
                else
                {
                    _logger.LogDebug("Entity merge candidate refresh done: no changes");
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Entity merge candidate refresh failed");
            }

            await Task.Delay(TimeSpan.FromSeconds(Math.Max(10, _settings.PollIntervalSeconds)), stoppingToken);
        }
    }

    private async Task ProcessAutoMergeAsync(CancellationToken ct)
    {
        var candidates = await _mergeRepository.GetPendingAsync(Math.Max(10, _settings.MaxCandidatesPerRun), ct);
        foreach (var c in candidates)
        {
            var low = await _entityRepository.GetByIdAsync(c.EntityLowId, ct);
            var high = await _entityRepository.GetByIdAsync(c.EntityHighId, ct);
            if (low == null || high == null)
            {
                await _mergeRepository.MarkDecisionAsync(c.Id, MergeDecision.Rejected, "entity_not_found", ct);
                continue;
            }

            var sameTelegram = low.TelegramUserId.HasValue
                               && high.TelegramUserId.HasValue
                               && low.TelegramUserId.Value == high.TelegramUserId.Value;
            var sameActor = !string.IsNullOrWhiteSpace(low.ActorKey)
                            && !string.IsNullOrWhiteSpace(high.ActorKey)
                            && string.Equals(low.ActorKey, high.ActorKey, StringComparison.OrdinalIgnoreCase);

            if (!sameTelegram && !sameActor)
            {
                if (_settings.SemanticGateEnabled)
                {
                    var similarity = await GetSemanticSimilarityAsync(low, high, ct);
                    if (similarity.HasValue)
                    {
                        if (similarity.Value < _settings.SemanticRejectSimilarityThreshold && IsWeakCandidate(c, low, high, _settings))
                        {
                            await _mergeRepository.MarkDecisionAsync(
                                c.Id,
                                MergeDecision.Rejected,
                                $"semantic_reject similarity={similarity.Value:0.000}",
                                ct);
                            continue;
                        }

                        if (similarity.Value >= _settings.SemanticAutoMergeSimilarityThreshold
                            && c.EvidenceCount >= Math.Max(1, _settings.SemanticAutoMergeMinEvidence)
                            && CanSemanticAutoMerge(low, high))
                        {
                            var targetBySemantic = ChooseTarget(low, high);
                            var sourceBySemantic = targetBySemantic.Id == low.Id ? high : low;
                            await _entityRepository.MergeIntoAsync(targetBySemantic.Id, sourceBySemantic.Id, ct);
                            await _mergeRepository.MarkDecisionAsync(
                                c.Id,
                                MergeDecision.Merged,
                                $"auto_semantic similarity={similarity.Value:0.000}",
                                ct);
                            _logger.LogInformation(
                                "Entity semantic auto-merge done: candidate={CandidateId} target={Target} source={Source} similarity={Similarity}",
                                c.Id,
                                targetBySemantic.Id,
                                sourceBySemantic.Id,
                                similarity.Value);
                            continue;
                        }
                    }
                }

                if (IsWeakCandidate(c, low, high, _settings))
                {
                    await _mergeRepository.MarkDecisionAsync(c.Id, MergeDecision.Rejected, "auto_weak_alias_evidence", ct);
                }
                continue;
            }

            var target = ChooseTarget(low, high);
            var source = target.Id == low.Id ? high : low;

            await _entityRepository.MergeIntoAsync(target.Id, source.Id, ct);
            await _mergeRepository.MarkDecisionAsync(c.Id, MergeDecision.Merged, sameActor ? "auto_same_actor_key" : "auto_same_telegram_user", ct);
            _logger.LogInformation(
                "Entity auto-merge done: candidate={CandidateId} target={Target} source={Source} reason={Reason}",
                c.Id,
                target.Id,
                source.Id,
                sameActor ? "same_actor_key" : "same_telegram_user");
        }
    }

    private async Task<float?> GetSemanticSimilarityAsync(Entity low, Entity high, CancellationToken ct)
    {
        var lowEmbedding = await _embeddingRepository.GetByOwnerAsync(EmbeddingOwnerType, low.Id.ToString(), ct: ct);
        var highEmbedding = await _embeddingRepository.GetByOwnerAsync(EmbeddingOwnerType, high.Id.ToString(), ct: ct);
        if (lowEmbedding?.Vector == null || highEmbedding?.Vector == null)
        {
            return null;
        }

        return Cosine(lowEmbedding.Vector, highEmbedding.Vector);
    }

    private static bool CanSemanticAutoMerge(Entity low, Entity high)
    {
        // Never merge if two explicit Telegram IDs conflict.
        if (low.TelegramUserId.HasValue
            && high.TelegramUserId.HasValue
            && low.TelegramUserId.Value != high.TelegramUserId.Value)
        {
            return false;
        }

        return true;
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

    private static Entity ChooseTarget(Entity a, Entity b)
    {
        var scoreA = Score(a);
        var scoreB = Score(b);
        if (scoreA > scoreB) return a;
        if (scoreB > scoreA) return b;
        return a.CreatedAt <= b.CreatedAt ? a : b;
    }

    private static int Score(Entity e)
    {
        var score = 0;
        if (!string.IsNullOrWhiteSpace(e.ActorKey)) score += 4;
        if (e.TelegramUserId.HasValue) score += 3;
        if (!string.IsNullOrWhiteSpace(e.TelegramUsername)) score += 1;
        score += Math.Min(3, e.Aliases.Count);
        score += Math.Min(2, e.Name.Length / 8);
        return score;
    }

    private static bool IsWeakCandidate(EntityMergeCandidate c, Entity low, Entity high, MergeSettings settings)
    {
        if (c.EvidenceCount > 1 || c.Score >= settings.AutoRejectScoreThreshold)
        {
            return false;
        }

        if (c.AliasNorm.Length > Math.Max(1, settings.AutoRejectAliasLengthMax))
        {
            return false;
        }

        var sameName = string.Equals(low.Name, high.Name, StringComparison.OrdinalIgnoreCase);
        return !sameName;
    }
}
