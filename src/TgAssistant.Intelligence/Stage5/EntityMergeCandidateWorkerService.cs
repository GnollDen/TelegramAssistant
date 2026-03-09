using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage5;

public class EntityMergeCandidateWorkerService : BackgroundService
{
    private readonly MergeSettings _settings;
    private readonly IEntityMergeRepository _mergeRepository;
    private readonly IEntityRepository _entityRepository;
    private readonly ILogger<EntityMergeCandidateWorkerService> _logger;

    public EntityMergeCandidateWorkerService(
        IOptions<MergeSettings> settings,
        IEntityMergeRepository mergeRepository,
        IEntityRepository entityRepository,
        ILogger<EntityMergeCandidateWorkerService> logger)
    {
        _settings = settings.Value;
        _mergeRepository = mergeRepository;
        _entityRepository = entityRepository;
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
