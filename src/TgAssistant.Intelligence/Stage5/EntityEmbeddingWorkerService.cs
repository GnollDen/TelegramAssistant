using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage5;

public class EntityEmbeddingWorkerService : BackgroundService
{
    private const string WatermarkKey = "stage5:entity_embedding_watermark_ms";

    private readonly EmbeddingSettings _settings;
    private readonly IEntityRepository _entityRepository;
    private readonly IFactRepository _factRepository;
    private readonly IEmbeddingRepository _embeddingRepository;
    private readonly ITextEmbeddingGenerator _embeddingGenerator;
    private readonly IAnalysisStateRepository _analysisStateRepository;
    private readonly ILogger<EntityEmbeddingWorkerService> _logger;

    public EntityEmbeddingWorkerService(
        IOptions<EmbeddingSettings> settings,
        IEntityRepository entityRepository,
        IFactRepository factRepository,
        IEmbeddingRepository embeddingRepository,
        ITextEmbeddingGenerator embeddingGenerator,
        IAnalysisStateRepository analysisStateRepository,
        ILogger<EntityEmbeddingWorkerService> logger)
    {
        _settings = settings.Value;
        _entityRepository = entityRepository;
        _factRepository = factRepository;
        _embeddingRepository = embeddingRepository;
        _embeddingGenerator = embeddingGenerator;
        _analysisStateRepository = analysisStateRepository;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled)
        {
            _logger.LogInformation("Entity embedding worker is disabled");
            return;
        }

        _logger.LogInformation("Entity embedding worker started. model={Model}, batch={Batch}", _settings.Model, _settings.BatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var watermarkMs = await _analysisStateRepository.GetWatermarkAsync(WatermarkKey, stoppingToken);
                var watermark = DateTimeOffset.FromUnixTimeMilliseconds(Math.Max(0, watermarkMs)).UtcDateTime;
                var entities = await _entityRepository.GetUpdatedSinceAsync(watermark, _settings.BatchSize, stoppingToken);
                if (entities.Count == 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(_settings.PollIntervalSeconds), stoppingToken);
                    continue;
                }

                foreach (var entity in entities)
                {
                    var facts = await _factRepository.GetCurrentByEntityAsync(entity.Id, stoppingToken);

                    var text = BuildEntityProfileText(entity, facts);
                    var vector = await _embeddingGenerator.GenerateAsync(_settings.Model, text, stoppingToken);
                    await _embeddingRepository.UpsertAsync(new TextEmbedding
                    {
                        OwnerType = "entity_profile",
                        OwnerId = entity.Id.ToString(),
                        SourceText = text,
                        Model = _settings.Model,
                        Vector = vector,
                        CreatedAt = DateTime.UtcNow
                    }, stoppingToken);

                    foreach (var fact in facts.OrderByDescending(f => f.UpdatedAt).Take(80))
                    {
                        var factText = BuildFactText(entity, fact);
                        var factVector = await _embeddingGenerator.GenerateAsync(_settings.Model, factText, stoppingToken);
                        await _embeddingRepository.UpsertAsync(new TextEmbedding
                        {
                            OwnerType = "fact",
                            OwnerId = fact.Id.ToString(),
                            SourceText = factText,
                            Model = _settings.Model,
                            Vector = factVector,
                            CreatedAt = DateTime.UtcNow
                        }, stoppingToken);
                    }
                }

                var nextMs = entities.Max(x => new DateTimeOffset(x.UpdatedAt).ToUnixTimeMilliseconds()) + 1;
                await _analysisStateRepository.SetWatermarkAsync(WatermarkKey, nextMs, stoppingToken);
                _logger.LogInformation("Entity embedding pass done: processed={Count}, watermark={Watermark}", entities.Count, nextMs);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Entity embedding loop failed");
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(5, _settings.PollIntervalSeconds)), stoppingToken);
            }
        }
    }

    private static string BuildEntityProfileText(Entity entity, List<Fact> facts)
    {
        var topFacts = facts
            .OrderByDescending(f => f.UpdatedAt)
            .ThenByDescending(f => f.Confidence)
            .Take(40)
            .Select(f => $"{f.Category}:{f.Key}={f.Value}");

        var aliases = entity.Aliases
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(20);

        return string.Join('\n', new[]
        {
            $"entity_id:{entity.Id}",
            $"name:{entity.Name}",
            $"type:{entity.Type}",
            $"aliases:{string.Join(", ", aliases)}",
            $"facts:{string.Join(" | ", topFacts)}"
        });
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
