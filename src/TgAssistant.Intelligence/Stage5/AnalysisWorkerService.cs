using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage5;

public class AnalysisWorkerService : BackgroundService
{
    private const string WatermarkKey = "stage5:watermark";
    private readonly AnalysisSettings _settings;
    private readonly IMessageRepository _messageRepository;
    private readonly IEntityRepository _entityRepository;
    private readonly IFactRepository _factRepository;
    private readonly IRelationshipRepository _relationshipRepository;
    private readonly IMessageExtractionRepository _extractionRepository;
    private readonly IAnalysisStateRepository _stateRepository;
    private readonly IPromptTemplateRepository _promptRepository;
    private readonly OpenRouterAnalysisService _analysisService;
    private readonly ILogger<AnalysisWorkerService> _logger;

    public AnalysisWorkerService(
        IOptions<AnalysisSettings> settings,
        IMessageRepository messageRepository,
        IEntityRepository entityRepository,
        IFactRepository factRepository,
        IRelationshipRepository relationshipRepository,
        IMessageExtractionRepository extractionRepository,
        IAnalysisStateRepository stateRepository,
        IPromptTemplateRepository promptRepository,
        OpenRouterAnalysisService analysisService,
        ILogger<AnalysisWorkerService> logger)
    {
        _settings = settings.Value;
        _messageRepository = messageRepository;
        _entityRepository = entityRepository;
        _factRepository = factRepository;
        _relationshipRepository = relationshipRepository;
        _extractionRepository = extractionRepository;
        _stateRepository = stateRepository;
        _promptRepository = promptRepository;
        _analysisService = analysisService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled)
        {
            _logger.LogInformation("Stage5 analysis is disabled");
            return;
        }

        await EnsureDefaultPromptsAsync(stoppingToken);
        _logger.LogInformation("Stage5 analysis worker started. cheap_model={Cheap}, expensive_model={Expensive}", _settings.CheapModel, _settings.ExpensiveModel);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessExpensiveBacklogAsync(stoppingToken);

                var watermark = await _stateRepository.GetWatermarkAsync(WatermarkKey, stoppingToken);
                var messages = await _messageRepository.GetProcessedAfterIdAsync(watermark, _settings.BatchSize, stoppingToken);
                if (messages.Count == 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(_settings.PollIntervalSeconds), stoppingToken);
                    continue;
                }

                var batch = messages.Select(m => new AnalysisInputMessage
                {
                    MessageId = m.Id,
                    SenderName = m.SenderName,
                    Timestamp = m.Timestamp,
                    Text = BuildMessageText(m)
                }).ToList();

                var cheapPrompt = await GetPromptAsync("stage5_cheap_extract", DefaultCheapPrompt, stoppingToken);
                var cheapResult = await _analysisService.ExtractCheapAsync(_settings.CheapModel, cheapPrompt.SystemPrompt, batch, stoppingToken);
                var byId = cheapResult.Items.ToDictionary(x => x.MessageId, x => x);

                foreach (var message in messages)
                {
                    byId.TryGetValue(message.Id, out var extracted);
                    extracted ??= new ExtractionItem { MessageId = message.Id };

                    var needsExpensive = extracted.RequiresExpensive
                                         || extracted.Facts.Any(f => f.Confidence < _settings.CheapConfidenceThreshold)
                                         || extracted.Relationships.Any(r => r.Confidence < _settings.CheapConfidenceThreshold);

                    await _extractionRepository.UpsertCheapAsync(message.Id, JsonSerializer.Serialize(extracted), needsExpensive, stoppingToken);

                    if (!needsExpensive)
                    {
                        await ApplyExtractionAsync(message.Id, extracted, stoppingToken);
                    }
                }

                var maxId = messages.Max(x => x.Id);
                await _stateRepository.SetWatermarkAsync(WatermarkKey, maxId, stoppingToken);
                _logger.LogInformation("Stage5 cheap pass done: processed={Count}, watermark={Watermark}", messages.Count, maxId);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Stage5 analysis loop failed");
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(5, _settings.PollIntervalSeconds)), stoppingToken);
            }
        }
    }

    private async Task ProcessExpensiveBacklogAsync(CancellationToken ct)
    {
        var backlog = await _extractionRepository.GetExpensiveBacklogAsync(_settings.MaxExpensivePerBatch, ct);
        if (backlog.Count == 0)
        {
            return;
        }

        var expensivePrompt = await GetPromptAsync("stage5_expensive_reason", DefaultExpensivePrompt, ct);

        foreach (var row in backlog)
        {
            var candidate = JsonSerializer.Deserialize<ExtractionItem>(row.CheapJson) ?? new ExtractionItem { MessageId = row.MessageId };
            var currentFacts = await GetCurrentFactStringsAsync(candidate, ct);

            var resolved = await _analysisService.ResolveExpensiveAsync(
                _settings.ExpensiveModel,
                expensivePrompt.SystemPrompt,
                candidate,
                currentFacts,
                ct);

            var effective = resolved ?? candidate;
            await ApplyExtractionAsync(row.MessageId, effective, ct);
            await _extractionRepository.ResolveExpensiveAsync(row.Id, JsonSerializer.Serialize(effective), ct);
        }

        _logger.LogInformation("Stage5 expensive pass done: resolved={Count}", backlog.Count);
    }

    private async Task ApplyExtractionAsync(long messageId, ExtractionItem extraction, CancellationToken ct)
    {
        var entityByName = new Dictionary<string, Entity>(StringComparer.OrdinalIgnoreCase);

        foreach (var entity in extraction.Entities.Where(e => !string.IsNullOrWhiteSpace(e.Name)))
        {
            var upserted = await _entityRepository.UpsertAsync(new Entity
            {
                Name = entity.Name.Trim(),
                Type = ParseEntityType(entity.Type)
            }, ct);
            entityByName[upserted.Name] = upserted;
        }

        foreach (var fact in extraction.Facts.Where(f => !string.IsNullOrWhiteSpace(f.EntityName) && !string.IsNullOrWhiteSpace(f.Key)))
        {
            if (!entityByName.TryGetValue(fact.EntityName.Trim(), out var entity))
            {
                entity = await _entityRepository.UpsertAsync(new Entity
                {
                    Name = fact.EntityName.Trim(),
                    Type = EntityType.Person
                }, ct);
                entityByName[entity.Name] = entity;
            }

            var current = await _factRepository.GetCurrentByEntityAsync(entity.Id, ct);
            var sameKey = current.FirstOrDefault(x => x.Category.Equals((fact.Category ?? "general").Trim(), StringComparison.OrdinalIgnoreCase)
                                                 && x.Key.Equals(fact.Key.Trim(), StringComparison.OrdinalIgnoreCase));

            var newFact = new Fact
            {
                EntityId = entity.Id,
                Category = string.IsNullOrWhiteSpace(fact.Category) ? "general" : fact.Category.Trim(),
                Key = fact.Key.Trim(),
                Value = fact.Value.Trim(),
                Status = ConfidenceStatus.Inferred,
                Confidence = fact.Confidence,
                SourceMessageId = messageId,
                ValidFrom = DateTime.UtcNow,
                IsCurrent = true
            };

            if (sameKey != null && !string.Equals(sameKey.Value, newFact.Value, StringComparison.OrdinalIgnoreCase))
            {
                await _factRepository.SupersedeFactAsync(sameKey.Id, newFact, ct);
            }
            else
            {
                await _factRepository.UpsertAsync(newFact, ct);
            }
        }

        foreach (var rel in extraction.Relationships.Where(r => !string.IsNullOrWhiteSpace(r.FromEntityName) && !string.IsNullOrWhiteSpace(r.ToEntityName) && !string.IsNullOrWhiteSpace(r.Type)))
        {
            var from = await _entityRepository.UpsertAsync(new Entity { Name = rel.FromEntityName.Trim(), Type = EntityType.Person }, ct);
            var to = await _entityRepository.UpsertAsync(new Entity { Name = rel.ToEntityName.Trim(), Type = EntityType.Person }, ct);

            await _relationshipRepository.UpsertAsync(new Relationship
            {
                FromEntityId = from.Id,
                ToEntityId = to.Id,
                Type = rel.Type.Trim().ToLowerInvariant(),
                Status = ConfidenceStatus.Inferred,
                Confidence = rel.Confidence,
                SourceMessageId = messageId
            }, ct);
        }
    }

    private async Task<List<string>> GetCurrentFactStringsAsync(ExtractionItem item, CancellationToken ct)
    {
        var list = new List<string>();
        foreach (var name in item.Entities.Select(x => x.Name.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var entity = await _entityRepository.FindByNameOrAliasAsync(name, ct);
            if (entity == null)
            {
                continue;
            }

            var facts = await _factRepository.GetCurrentByEntityAsync(entity.Id, ct);
            list.AddRange(facts.Select(f => $"{entity.Name}:{f.Category}:{f.Key}={f.Value}"));
        }

        return list.Take(200).ToList();
    }

    private static string BuildMessageText(Message m)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(m.Text)) parts.Add(m.Text);
        if (!string.IsNullOrWhiteSpace(m.MediaTranscription)) parts.Add($"[media_transcription] {m.MediaTranscription}");
        if (!string.IsNullOrWhiteSpace(m.MediaDescription)) parts.Add($"[media_description] {m.MediaDescription}");
        return string.Join("\n", parts);
    }

    private async Task EnsureDefaultPromptsAsync(CancellationToken ct)
    {
        await EnsurePromptAsync("stage5_cheap_extract", "Stage5 Cheap Extraction", DefaultCheapPrompt, ct);
        await EnsurePromptAsync("stage5_expensive_reason", "Stage5 Expensive Reasoning", DefaultExpensivePrompt, ct);
    }

    private async Task EnsurePromptAsync(string id, string name, string systemPrompt, CancellationToken ct)
    {
        var existing = await _promptRepository.GetByIdAsync(id, ct);
        if (existing != null)
        {
            return;
        }

        await _promptRepository.UpsertAsync(new PromptTemplate
        {
            Id = id,
            Name = name,
            SystemPrompt = systemPrompt
        }, ct);
    }

    private async Task<PromptTemplate> GetPromptAsync(string id, string fallback, CancellationToken ct)
    {
        var prompt = await _promptRepository.GetByIdAsync(id, ct);
        return prompt ?? new PromptTemplate { Id = id, Name = id, SystemPrompt = fallback };
    }

    private static EntityType ParseEntityType(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "organization" => EntityType.Organization,
            "place" => EntityType.Place,
            "pet" => EntityType.Pet,
            "event" => EntityType.Event,
            _ => EntityType.Person
        };
    }

    private const string DefaultCheapPrompt = """
You are a low-cost extraction model.
Return ONLY valid JSON object with field `items`.
Each item corresponds to one input message and has:
- message_id (number)
- entities: [{name,type,confidence}]
- facts: [{entity_name,category,key,value,confidence}]
- relationships: [{from_entity_name,to_entity_name,type,confidence}]
- requires_expensive (boolean)
- reason (string, optional)

Rules:
- Keep only concrete personal context facts.
- Ignore generic chat noise.
- Set requires_expensive=true for ambiguous/conflicting content.
- Never include markdown.
""";

    private const string DefaultExpensivePrompt = """
You are a high-accuracy resolver.
Input includes one cheap candidate and current known facts.
Return ONLY valid JSON object with field `items` containing exactly one normalized item.
Prioritize consistency, deduplication and conflict-aware fact output.
If uncertainty remains, keep requires_expensive=true.
""";
}
