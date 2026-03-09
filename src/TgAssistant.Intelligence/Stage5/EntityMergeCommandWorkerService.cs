using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage5;

public class EntityMergeCommandWorkerService : BackgroundService
{
    private readonly MergeSettings _settings;
    private readonly IEntityMergeCommandRepository _commandRepository;
    private readonly IEntityMergeRepository _mergeRepository;
    private readonly IEntityRepository _entityRepository;
    private readonly ILogger<EntityMergeCommandWorkerService> _logger;

    public EntityMergeCommandWorkerService(
        IOptions<MergeSettings> settings,
        IEntityMergeCommandRepository commandRepository,
        IEntityMergeRepository mergeRepository,
        IEntityRepository entityRepository,
        ILogger<EntityMergeCommandWorkerService> logger)
    {
        _settings = settings.Value;
        _commandRepository = commandRepository;
        _mergeRepository = mergeRepository;
        _entityRepository = entityRepository;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled)
        {
            _logger.LogInformation("Entity merge command worker is disabled");
            return;
        }

        _logger.LogInformation("Entity merge command worker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            var batchSize = Math.Max(1, _settings.CommandBatchSize);
            var pending = await _commandRepository.GetPendingAsync(batchSize, stoppingToken);
            var processed = 0;
            foreach (var cmd in pending)
            {
                try
                {
                    await ExecuteCommandAsync(cmd, stoppingToken);
                    await _commandRepository.MarkDoneAsync(cmd.Id, stoppingToken);
                    processed++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Entity merge command failed. command_id={CommandId}", cmd.Id);
                    await _commandRepository.MarkFailedAsync(cmd.Id, ex.Message, stoppingToken);
                }
            }

            if (processed > 0)
            {
                _logger.LogInformation("Entity merge command pass done: processed={Processed}, batch_size={BatchSize}", processed, batchSize);
            }

            await Task.Delay(TimeSpan.FromSeconds(Math.Max(10, _settings.PollIntervalSeconds)), stoppingToken);
        }
    }

    private async Task ExecuteCommandAsync(EntityMergeCommand cmd, CancellationToken ct)
    {
        var candidate = await _mergeRepository.GetByIdAsync(cmd.CandidateId, ct)
                        ?? throw new InvalidOperationException($"candidate_not_found:{cmd.CandidateId}");
        if (candidate.Status != (short)MergeDecision.Pending)
        {
            throw new InvalidOperationException($"candidate_not_pending:{cmd.CandidateId}");
        }

        var action = (cmd.Command ?? string.Empty).Trim().ToLowerInvariant();
        if (action is "reject" or "decline")
        {
            await _mergeRepository.MarkDecisionAsync(candidate.Id, MergeDecision.Rejected, $"manual_reject:{cmd.Reason}", ct);
            _logger.LogInformation("Manual merge rejected: candidate_id={CandidateId}", candidate.Id);
            return;
        }

        if (action is "approve" or "merge")
        {
            var low = await _entityRepository.GetByIdAsync(candidate.EntityLowId, ct)
                      ?? throw new InvalidOperationException("entity_low_not_found");
            var high = await _entityRepository.GetByIdAsync(candidate.EntityHighId, ct)
                       ?? throw new InvalidOperationException("entity_high_not_found");

            var target = ChooseTarget(low, high);
            var source = target.Id == low.Id ? high : low;

            await _entityRepository.MergeIntoAsync(target.Id, source.Id, ct);
            await _mergeRepository.MarkDecisionAsync(candidate.Id, MergeDecision.Merged, $"manual_approve:{cmd.Reason}", ct);
            _logger.LogInformation("Manual merge approved: candidate_id={CandidateId} target={Target} source={Source}", candidate.Id, target.Id, source.Id);
            return;
        }

        throw new InvalidOperationException($"unsupported_command:{cmd.Command}");
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
}
