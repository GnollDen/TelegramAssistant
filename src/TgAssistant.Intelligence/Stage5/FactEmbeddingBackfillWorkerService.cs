using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage5;

/// <summary>
/// Gradually backfills fact embeddings for the current embedding model.
/// </summary>
public class FactEmbeddingBackfillWorkerService : BackgroundService
{
    private readonly EmbeddingSettings _settings;
    private readonly IFactRepository _factRepository;
    private readonly IEntityRepository _entityRepository;
    private readonly IEmbeddingRepository _embeddingRepository;
    private readonly ITextEmbeddingGenerator _embeddingGenerator;
    private readonly ILogger<FactEmbeddingBackfillWorkerService> _logger;

    /// <summary>
    /// Creates a new worker instance that backfills missing fact embeddings.
    /// </summary>
    public FactEmbeddingBackfillWorkerService(
        IOptions<EmbeddingSettings> settings,
        IFactRepository factRepository,
        IEntityRepository entityRepository,
        IEmbeddingRepository embeddingRepository,
        ITextEmbeddingGenerator embeddingGenerator,
        ILogger<FactEmbeddingBackfillWorkerService> logger)
    {
        _settings = settings.Value;
        _factRepository = factRepository;
        _entityRepository = entityRepository;
        _embeddingRepository = embeddingRepository;
        _embeddingGenerator = embeddingGenerator;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled)
        {
            _logger.LogInformation("Fact embedding backfill worker is disabled");
            return;
        }

        _logger.LogInformation("Fact embedding backfill worker started. model={Model}, batch={Batch}", _settings.Model, _settings.BatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var batchSize = Math.Max(1, _settings.BatchSize);
                var pendingFacts = await _factRepository.GetWithoutEmbeddingAsync(_settings.Model, batchSize, stoppingToken);
                var entityCache = new Dictionary<Guid, Entity?>();
                var processed = 0;

                foreach (var fact in pendingFacts)
                {
                    try
                    {
                        if (!entityCache.TryGetValue(fact.EntityId, out var entity))
                        {
                            entity = await _entityRepository.GetByIdAsync(fact.EntityId, stoppingToken);
                            entityCache[fact.EntityId] = entity;
                        }

                        if (entity == null)
                        {
                            _logger.LogWarning("Fact embedding backfill skipped missing entity. fact_id={FactId}, entity_id={EntityId}", fact.Id, fact.EntityId);
                            continue;
                        }

                        var factText = BuildFactText(entity, fact);
                        var factVector = await _embeddingGenerator.GenerateAsync(_settings.Model, factText, stoppingToken);
                        if (factVector.Length == 0)
                        {
                            _logger.LogWarning("Fact embedding backfill got empty vector. fact_id={FactId}", fact.Id);
                            continue;
                        }

                        await _embeddingRepository.UpsertAsync(new TextEmbedding
                        {
                            OwnerType = "fact",
                            OwnerId = fact.Id.ToString(),
                            SourceText = factText,
                            Model = _settings.Model,
                            Vector = factVector,
                            CreatedAt = DateTime.UtcNow
                        }, stoppingToken);

                        processed++;
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Fact embedding backfill item failed. fact_id={FactId}, entity_id={EntityId}", fact.Id, fact.EntityId);
                    }
                }

                var remaining = await _factRepository.CountWithoutEmbeddingAsync(_settings.Model, stoppingToken);
                _logger.LogInformation(
                    "Fact embedding backfill pass done: processed={Processed}, fetched={Fetched}, remaining={Remaining}, model={Model}",
                    processed,
                    pendingFacts.Count,
                    remaining,
                    _settings.Model);

                await Task.Delay(TimeSpan.FromSeconds(_settings.PollIntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Fact embedding backfill loop failed");
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(5, _settings.PollIntervalSeconds)), stoppingToken);
            }
        }
    }

    private static string BuildFactText(Entity entity, Fact fact)
    {
        return string.Join('\n', new[]
        {
            $"entity_id:{entity.Id}",
            $"entity_name:{entity.Name}",
            $"entity_type:{entity.Type}",
            $"category:{fact.Category}",
            $"key:{fact.Key}",
            $"value:{fact.Value}",
            $"confidence:{fact.Confidence:0.000}",
            $"is_current:{fact.IsCurrent}"
        });
    }
}
