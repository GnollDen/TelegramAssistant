using System.Text.Json;
using System.Text.RegularExpressions;
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
    private const string CheapPromptId = "stage5_cheap_extract_v2";
    private const string FactEmbeddingOwnerType = "fact";
    private readonly AnalysisSettings _settings;
    private readonly EmbeddingSettings _embeddingSettings;
    private readonly IMessageRepository _messageRepository;
    private readonly IEntityRepository _entityRepository;
    private readonly IEntityAliasRepository _entityAliasRepository;
    private readonly IFactRepository _factRepository;
    private readonly IEmbeddingRepository _embeddingRepository;
    private readonly ITextEmbeddingGenerator _embeddingGenerator;
    private readonly ICommunicationEventRepository _communicationEventRepository;
    private readonly IRelationshipRepository _relationshipRepository;
    private readonly IFactReviewCommandRepository _factReviewCommandRepository;
    private readonly IMessageExtractionRepository _extractionRepository;
    private readonly IExtractionErrorRepository _extractionErrorRepository;
    private readonly IAnalysisStateRepository _stateRepository;
    private readonly IPromptTemplateRepository _promptRepository;
    private readonly IAnalysisUsageRepository _analysisUsageRepository;
    private readonly OpenRouterAnalysisService _analysisService;
    private readonly ILogger<AnalysisWorkerService> _logger;
    private readonly Dictionary<string, DateTimeOffset> _expensiveBlockedUntilByModel = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _expensiveFailureStreakByModel = new(StringComparer.OrdinalIgnoreCase);

    public AnalysisWorkerService(
        IOptions<AnalysisSettings> settings,
        IOptions<EmbeddingSettings> embeddingSettings,
        IMessageRepository messageRepository,
        IEntityRepository entityRepository,
        IEntityAliasRepository entityAliasRepository,
        IFactRepository factRepository,
        IEmbeddingRepository embeddingRepository,
        ITextEmbeddingGenerator embeddingGenerator,
        ICommunicationEventRepository communicationEventRepository,
        IRelationshipRepository relationshipRepository,
        IFactReviewCommandRepository factReviewCommandRepository,
        IMessageExtractionRepository extractionRepository,
        IExtractionErrorRepository extractionErrorRepository,
        IAnalysisStateRepository stateRepository,
        IPromptTemplateRepository promptRepository,
        IAnalysisUsageRepository analysisUsageRepository,
        OpenRouterAnalysisService analysisService,
        ILogger<AnalysisWorkerService> logger)
    {
        _settings = settings.Value;
        _embeddingSettings = embeddingSettings.Value;
        _messageRepository = messageRepository;
        _entityRepository = entityRepository;
        _entityAliasRepository = entityAliasRepository;
        _factRepository = factRepository;
        _embeddingRepository = embeddingRepository;
        _embeddingGenerator = embeddingGenerator;
        _communicationEventRepository = communicationEventRepository;
        _relationshipRepository = relationshipRepository;
        _factReviewCommandRepository = factReviewCommandRepository;
        _extractionRepository = extractionRepository;
        _extractionErrorRepository = extractionErrorRepository;
        _stateRepository = stateRepository;
        _promptRepository = promptRepository;
        _analysisUsageRepository = analysisUsageRepository;
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

                var reanalysis = await _messageRepository.GetNeedsReanalysisAsync(_settings.BatchSize, stoppingToken);
                if (reanalysis.Count > 0)
                {
                    await ProcessCheapBatchAsync(reanalysis, stoppingToken);
                    await _messageRepository.MarkNeedsReanalysisDoneAsync(reanalysis.Select(x => x.Id), stoppingToken);
                    _logger.LogInformation("Stage5 reanalysis pass done: processed={Count}", reanalysis.Count);
                    continue;
                }

                var watermark = await _stateRepository.GetWatermarkAsync(WatermarkKey, stoppingToken);
                var messages = await _messageRepository.GetProcessedAfterIdAsync(watermark, _settings.BatchSize, stoppingToken);
                if (messages.Count == 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(_settings.PollIntervalSeconds), stoppingToken);
                    continue;
                }

                await ProcessCheapBatchAsync(messages, stoppingToken);

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
                await _extractionErrorRepository.LogAsync(
                    stage: "stage5_loop",
                    reason: ex.Message,
                    payload: ex.GetType().Name,
                    ct: stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(5, _settings.PollIntervalSeconds)), stoppingToken);
            }
        }
    }

    private async Task ProcessExpensiveBacklogAsync(CancellationToken ct)
    {
        if (_settings.MaxExpensivePerBatch <= 0)
        {
            return;
        }

        if (!await CanRunExpensivePassAsync(ct))
        {
            return;
        }

        if (AreAllExpensiveModelsBlocked())
        {
            return;
        }

        var backlog = await _extractionRepository.GetExpensiveBacklogAsync(_settings.MaxExpensivePerBatch, ct);
        if (backlog.Count == 0)
        {
            return;
        }

        var expensivePrompt = await GetPromptAsync("stage5_expensive_reason", DefaultExpensivePrompt, ct);
        var resolvedCount = 0;

        foreach (var row in backlog)
        {
            if (!await CanRunExpensivePassAsync(ct))
            {
                break;
            }

            var candidate = JsonSerializer.Deserialize<ExtractionItem>(row.CheapJson) ?? new ExtractionItem { MessageId = row.MessageId };
            var currentFacts = await GetCurrentFactStringsAsync(candidate, ct);

            try
            {
                var resolved = await ResolveWithFallbackAsync(candidate, currentFacts, expensivePrompt.SystemPrompt, ct);
                var effective = SanitizeExtraction(resolved ?? candidate);
                effective.MessageId = row.MessageId;
                if (!ValidateExtractionRecord(effective, out var validationError))
                {
                    _logger.LogWarning(
                        "Stage5 expensive validation rejected extraction_id={ExtractionId} message_id={MessageId}: {Reason}",
                        row.Id,
                        row.MessageId,
                        validationError);

                    await _extractionErrorRepository.LogAsync(
                        stage: "stage5_expensive_validation",
                        reason: validationError ?? "invalid_extraction",
                        messageId: row.MessageId,
                        payload: JsonSerializer.Serialize(effective),
                        ct: ct);

                    effective = new ExtractionItem { MessageId = row.MessageId };
                }
                var sourceMessage = await _messageRepository.GetByIdAsync(row.MessageId, ct);
                await ApplyExtractionAsync(row.MessageId, effective, sourceMessage, ct);
                await _extractionRepository.ResolveExpensiveAsync(row.Id, JsonSerializer.Serialize(effective), ct);
                resolvedCount++;
            }
            catch (Exception ex) when (IsProviderDenied(ex))
            {
                _logger.LogWarning(ex,
                    "Stage5 expensive models access denied. message_id={MessageId}",
                    row.MessageId);

                // Do not block the whole pipeline: finalize with cheap candidate.
                var sourceMessage = await _messageRepository.GetByIdAsync(row.MessageId, ct);
                var sanitized = SanitizeExtraction(candidate);
                await ApplyExtractionAsync(row.MessageId, sanitized, sourceMessage, ct);
                await _extractionRepository.ResolveExpensiveAsync(row.Id, JsonSerializer.Serialize(sanitized), ct);
                resolvedCount++;
                await _extractionErrorRepository.LogAsync(
                    stage: "stage5_expensive_denied",
                    reason: ex.Message,
                    messageId: row.MessageId,
                    payload: BuildExpensiveErrorPayload(ex),
                    ct: ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Stage5 expensive item failed for message_id={MessageId}", row.MessageId);
                var retry = await _extractionRepository.MarkExpensiveFailedAsync(
                    row.Id,
                    ex.Message,
                    _settings.MaxExpensiveRetryCount,
                    _settings.ExpensiveRetryBaseSeconds,
                    ct);

                if (retry.IsExhausted)
                {
                    var sourceMessage = await _messageRepository.GetByIdAsync(row.MessageId, ct);
                    var sanitized = SanitizeExtraction(candidate);
                    await ApplyExtractionAsync(row.MessageId, sanitized, sourceMessage, ct);
                    await _extractionRepository.ResolveExpensiveAsync(row.Id, JsonSerializer.Serialize(sanitized), ct);
                    _logger.LogWarning(
                        "Stage5 expensive retries exhausted. Finalized with cheap extraction. message_id={MessageId}, retry_count={RetryCount}",
                        row.MessageId,
                        retry.RetryCount);
                }

                await _extractionErrorRepository.LogAsync(
                    stage: "stage5_expensive_item",
                    reason: ex.Message,
                    messageId: row.MessageId,
                    payload: $"{BuildExpensiveErrorPayload(ex)};retry_count={retry.RetryCount};next_retry_at={retry.NextRetryAt:O};exhausted={retry.IsExhausted}",
                    ct: ct);
            }
        }

        _logger.LogInformation("Stage5 expensive pass done: resolved={Count} of {Total}", resolvedCount, backlog.Count);
    }

    private async Task ProcessCheapBatchAsync(List<Message> messages, CancellationToken ct)
    {
        var replyContext = await LoadReplyContextAsync(messages, ct);
        var batch = messages.Select(m => new AnalysisInputMessage
        {
            MessageId = m.Id,
            SenderName = m.SenderName,
            Timestamp = m.Timestamp,
            Text = BuildMessageText(m, replyContext.GetValueOrDefault(m.Id))
        }).ToList();

        var cheapPrompt = await GetPromptAsync(CheapPromptId, DefaultCheapPrompt, ct);
        var modelByMessageId = BuildCheapModelMap(messages);
        var byId = new Dictionary<long, ExtractionItem>();

        foreach (var modelGroup in batch.GroupBy(x => modelByMessageId.GetValueOrDefault(x.MessageId, _settings.CheapModel)))
        {
            var model = modelGroup.Key;
            var modelBatch = modelGroup.ToList();
            try
            {
                var cheapResult = await _analysisService.ExtractCheapAsync(model, cheapPrompt.SystemPrompt, modelBatch, ct);
                foreach (var item in cheapResult.Items.Where(x => x.MessageId > 0))
                {
                    byId[item.MessageId] = item;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Stage5 cheap batch failed for model={Model}, count={Count}", model, modelBatch.Count);
                await _extractionErrorRepository.LogAsync(
                    stage: "stage5_cheap_batch_model",
                    reason: ex.Message,
                    payload: $"model={model};count={modelBatch.Count}",
                    ct: ct);
            }
        }

        foreach (var message in messages)
        {
            try
            {
                byId.TryGetValue(message.Id, out var extracted);
                extracted ??= new ExtractionItem { MessageId = message.Id };
                extracted = NormalizeExtractionForMessage(extracted, message);
                extracted = SanitizeExtraction(extracted);
                extracted.MessageId = message.Id;

                if (!ValidateExtractionForMessage(extracted, message, out var validationError))
                {
                    _logger.LogWarning(
                        "Stage5 extraction validation rejected message_id={MessageId}: {Reason}",
                        message.Id,
                        validationError);

                    await _extractionErrorRepository.LogAsync(
                        stage: "stage5_validation",
                        reason: validationError ?? "invalid_extraction",
                        messageId: message.Id,
                        payload: $"model={modelByMessageId.GetValueOrDefault(message.Id, _settings.CheapModel)};json={JsonSerializer.Serialize(extracted)}",
                        ct: ct);

                    extracted = new ExtractionItem { MessageId = message.Id };
                }

                var needsExpensive = extracted.RequiresExpensive
                                     || extracted.Facts.Any(f => f.Confidence < _settings.CheapConfidenceThreshold)
                                     || extracted.Relationships.Any(r => r.Confidence < _settings.CheapConfidenceThreshold)
                                     || ShouldEscalateLowSignalExtraction(message, extracted);

                await _extractionRepository.UpsertCheapAsync(message.Id, JsonSerializer.Serialize(extracted), needsExpensive, ct);

                if (!needsExpensive)
                {
                    await ApplyExtractionAsync(message.Id, extracted, message, ct);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Stage5 cheap item failed for message_id={MessageId}", message.Id);
                await _extractionErrorRepository.LogAsync(
                    stage: "stage5_cheap_item",
                    reason: ex.Message,
                    messageId: message.Id,
                    payload: $"model={modelByMessageId.GetValueOrDefault(message.Id, _settings.CheapModel)};exception={ex.GetType().Name}",
                    ct: ct);
            }
        }
    }

    private Dictionary<long, string> BuildCheapModelMap(List<Message> messages)
    {
        var map = new Dictionary<long, string>(messages.Count);
        if (!_settings.CheapModelAbEnabled)
        {
            foreach (var message in messages)
            {
                map[message.Id] = _settings.CheapModel;
            }

            return map;
        }

        var baseline = string.IsNullOrWhiteSpace(_settings.CheapBaselineModel)
            ? "openai/gpt-4o-mini"
            : _settings.CheapBaselineModel.Trim();
        var candidate = string.IsNullOrWhiteSpace(_settings.CheapCandidateModel)
            ? _settings.CheapModel
            : _settings.CheapCandidateModel.Trim();
        var candidatePercent = Math.Clamp(_settings.CheapAbCandidatePercent, 0, 100);

        foreach (var message in messages)
        {
            var bucket = (int)(Math.Abs(message.Id % 100));
            map[message.Id] = bucket < candidatePercent ? candidate : baseline;
        }

        return map;
    }

    private async Task<ExtractionItem?> ResolveWithFallbackAsync(
        ExtractionItem candidate,
        List<string> currentFacts,
        string systemPrompt,
        CancellationToken ct)
    {
        Exception? lastTransient = null;
        var models = GetDistinctExpensiveModels();
        foreach (var model in models)
        {
            if (TryGetModelBlockedUntil(model, out var blockedUntil))
            {
                _logger.LogDebug(
                    "Stage5 expensive model is in backoff. model={Model}, blocked_until={Until}",
                    model,
                    blockedUntil);
                continue;
            }

            try
            {
                var resolved = await _analysisService.ResolveExpensiveAsync(
                    model,
                    systemPrompt,
                    candidate,
                    currentFacts,
                    ct);
                RegisterModelSuccess(model);
                return resolved;
            }
            catch (Exception ex) when (ShouldFallback(ex) || IsProviderDenied(ex))
            {
                var denied = IsProviderDenied(ex);
                ex.Data["expensive_model"] = model;
                RegisterModelFailure(model, denied);
                lastTransient = ex;
                _logger.LogWarning(
                    ex,
                    "Stage5 expensive model failed. model={Model}, denied={Denied}, fallback_candidates_left={Left}",
                    model,
                    denied,
                    models.Count - models.IndexOf(model) - 1);
            }
        }

        if (lastTransient != null)
        {
            throw lastTransient;
        }

        throw new InvalidOperationException("no_expensive_model_available");
    }

    private static bool ShouldFallback(Exception ex)
    {
        if (ex is TaskCanceledException)
        {
            return true;
        }

        if (ex is HttpRequestException hre)
        {
            var msg = hre.Message;
            return msg.Contains(" 403", StringComparison.OrdinalIgnoreCase)
                   || msg.Contains(" 402", StringComparison.OrdinalIgnoreCase)
                   || msg.Contains("forbidden", StringComparison.OrdinalIgnoreCase)
                   || msg.Contains("timeout", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static bool IsProviderDenied(Exception ex)
    {
        return ex is HttpRequestException hre &&
               (hre.Message.Contains("forbidden", StringComparison.OrdinalIgnoreCase)
                || hre.Message.Contains(" 403", StringComparison.OrdinalIgnoreCase)
                || hre.Message.Contains("\"code\":403", StringComparison.OrdinalIgnoreCase)
                || hre.Message.Contains(" 402", StringComparison.OrdinalIgnoreCase)
                || hre.Message.Contains("\"code\":402", StringComparison.OrdinalIgnoreCase)
                || hre.Message.Contains("more credits", StringComparison.OrdinalIgnoreCase));
    }

    private void RegisterModelFailure(string model, bool denied)
    {
        var key = NormalizeModelKey(model);
        var current = _expensiveFailureStreakByModel.TryGetValue(key, out var streak) ? streak : 0;
        var next = Math.Max(1, current + 1);
        _expensiveFailureStreakByModel[key] = next;

        var backoff = ComputeExpensiveBackoff(next);
        var deniedCooldown = TimeSpan.FromMinutes(Math.Max(1, _settings.ExpensiveCooldownMinutes));
        var effective = denied ? Max(backoff, deniedCooldown) : backoff;
        _expensiveBlockedUntilByModel[key] = DateTimeOffset.UtcNow.Add(effective);
    }

    private void RegisterModelSuccess(string model)
    {
        var key = NormalizeModelKey(model);
        _expensiveFailureStreakByModel.Remove(key);
        _expensiveBlockedUntilByModel.Remove(key);
    }

    private TimeSpan ComputeExpensiveBackoff(int streak)
    {
        var baseSeconds = Math.Max(5, _settings.ExpensiveFailureBackoffBaseSeconds);
        var maxSeconds = Math.Max(60, _settings.ExpensiveFailureBackoffMaxMinutes * 60);

        var shift = Math.Min(10, Math.Max(0, streak - 1));
        var multiplier = 1 << shift;
        var rawSeconds = (long)baseSeconds * multiplier;
        var seconds = Math.Min(maxSeconds, rawSeconds);
        return TimeSpan.FromSeconds(seconds);
    }

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a >= b ? a : b;

    private bool TryGetModelBlockedUntil(string model, out DateTimeOffset until)
    {
        until = default;
        var key = NormalizeModelKey(model);
        if (!_expensiveBlockedUntilByModel.TryGetValue(key, out var value))
        {
            return false;
        }

        if (DateTimeOffset.UtcNow >= value)
        {
            _expensiveBlockedUntilByModel.Remove(key);
            _expensiveFailureStreakByModel.Remove(key);
            return false;
        }

        until = value;
        return true;
    }

    private bool AreAllExpensiveModelsBlocked()
    {
        var models = GetDistinctExpensiveModels();
        if (models.Count == 0)
        {
            return false;
        }

        return models.All(model => TryGetModelBlockedUntil(model, out _));
    }

    private List<string> GetDistinctExpensiveModels()
    {
        var result = new List<string>();
        if (!string.IsNullOrWhiteSpace(_settings.ExpensiveModel))
        {
            result.Add(_settings.ExpensiveModel.Trim());
        }

        if (!string.IsNullOrWhiteSpace(_settings.ExpensiveFallbackModel))
        {
            var fallback = _settings.ExpensiveFallbackModel.Trim();
            if (result.All(x => !string.Equals(x, fallback, StringComparison.OrdinalIgnoreCase)))
            {
                result.Add(fallback);
            }
        }

        return result;
    }

    private static string NormalizeModelKey(string model) => model.Trim();

    private static string BuildExpensiveErrorPayload(Exception ex)
    {
        var model = ex.Data.Contains("expensive_model")
            ? ex.Data["expensive_model"]?.ToString()
            : null;
        var modelPart = string.IsNullOrWhiteSpace(model) ? "unknown" : model.Trim();
        return $"model={modelPart};exception={ex.GetType().Name}";
    }

    private async Task ApplyExtractionAsync(long messageId, ExtractionItem extraction, Message? sourceMessage, CancellationToken ct)
    {
        var entityByName = new Dictionary<string, Entity>(StringComparer.OrdinalIgnoreCase);
        var eventBuffer = new List<CommunicationEvent>();
        var senderName = sourceMessage?.SenderName?.Trim();

        foreach (var entity in extraction.Entities.Where(e => !string.IsNullOrWhiteSpace(e.Name)))
        {
            if (IsGenericEntityToken(entity.Name))
            {
                continue;
            }

            var upserted = await UpsertEntityWithActorContextAsync(
                entity.Name.Trim(),
                ParseEntityType(entity.Type),
                sourceMessage,
                senderName,
                ct);
            entityByName[upserted.Name] = upserted;
        }

        foreach (var fact in extraction.Facts.Where(f => !string.IsNullOrWhiteSpace(f.EntityName) && !string.IsNullOrWhiteSpace(f.Key)))
        {
            if (IsGenericEntityToken(fact.EntityName))
            {
                continue;
            }

            var normalizedCategory = string.IsNullOrWhiteSpace(fact.Category) ? "general" : fact.Category.Trim();
            var factThreshold = IsSensitiveCategory(normalizedCategory)
                ? _settings.MinSensitiveFactConfidence
                : _settings.MinFactConfidence;
            if (fact.Confidence < factThreshold)
            {
                continue;
            }

            if (!entityByName.TryGetValue(fact.EntityName.Trim(), out var entity))
            {
                entity = await UpsertEntityWithActorContextAsync(
                    fact.EntityName.Trim(),
                    EntityType.Person,
                    sourceMessage,
                    senderName,
                    ct);
                entityByName[entity.Name] = entity;
            }

            var current = await _factRepository.GetCurrentByEntityAsync(entity.Id, ct);
            var sameKey = current.FirstOrDefault(x => x.Category.Equals(normalizedCategory, StringComparison.OrdinalIgnoreCase)
                                                 && x.Key.Equals(fact.Key.Trim(), StringComparison.OrdinalIgnoreCase));
            var strategy = GetFactConflictStrategy(normalizedCategory);
            var status = ResolveFactStatus(normalizedCategory, fact.Confidence);

            var newFact = new Fact
            {
                EntityId = entity.Id,
                Category = normalizedCategory,
                Key = fact.Key.Trim(),
                Value = (fact.Value ?? string.Empty).Trim(),
                Status = status,
                Confidence = fact.Confidence,
                SourceMessageId = messageId,
                ValidFrom = DateTime.UtcNow,
                IsCurrent = true
            };

            if (sameKey != null && !string.Equals(sameKey.Value, newFact.Value, StringComparison.OrdinalIgnoreCase))
            {
                eventBuffer.Add(new CommunicationEvent
                {
                    MessageId = messageId,
                    EntityId = entity.Id,
                    EventType = "fact_contradiction",
                    ObjectName = $"{normalizedCategory}:{fact.Key.Trim()}",
                    Sentiment = null,
                    Summary = $"value changed from '{sameKey.Value}' to '{newFact.Value}'",
                    Confidence = Math.Max(0.6f, fact.Confidence)
                });

                switch (strategy)
                {
                    case FactConflictStrategy.Supersede:
                        await _factRepository.SupersedeFactAsync(sameKey.Id, newFact, ct);
                        await QueueFactReviewIfNeededAsync(newFact, factThreshold, ct);
                        break;
                    case FactConflictStrategy.Parallel:
                    case FactConflictStrategy.Tentative:
                        var parallelSaved = await _factRepository.UpsertAsync(newFact, ct);
                        await QueueFactReviewIfNeededAsync(parallelSaved, factThreshold, ct);
                        break;
                }
            }
            else
            {
                var saved = await _factRepository.UpsertAsync(newFact, ct);
                await QueueFactReviewIfNeededAsync(saved, factThreshold, ct);
            }
        }

        foreach (var rel in extraction.Relationships.Where(r => !string.IsNullOrWhiteSpace(r.FromEntityName) && !string.IsNullOrWhiteSpace(r.ToEntityName) && !string.IsNullOrWhiteSpace(r.Type)))
        {
            if (IsGenericEntityToken(rel.FromEntityName) || IsGenericEntityToken(rel.ToEntityName))
            {
                continue;
            }

            if (rel.Confidence < _settings.MinRelationshipConfidence)
            {
                continue;
            }

            var from = await UpsertEntityWithActorContextAsync(rel.FromEntityName.Trim(), EntityType.Person, sourceMessage, senderName, ct);
            var to = await UpsertEntityWithActorContextAsync(rel.ToEntityName.Trim(), EntityType.Person, sourceMessage, senderName, ct);

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

        foreach (var evt in extraction.Events.Where(e => !string.IsNullOrWhiteSpace(e.Type) && !string.IsNullOrWhiteSpace(e.SubjectName)))
        {
            var subjectName = evt.SubjectName.Trim();
            if (IsGenericEntityToken(subjectName))
            {
                continue;
            }

            if (!entityByName.TryGetValue(subjectName, out var subjectEntity))
            {
                subjectEntity = await UpsertEntityWithActorContextAsync(
                    subjectName,
                    EntityType.Person,
                    sourceMessage,
                    senderName,
                    ct);
                entityByName[subjectEntity.Name] = subjectEntity;
            }

            eventBuffer.Add(new CommunicationEvent
            {
                MessageId = messageId,
                EntityId = subjectEntity.Id,
                EventType = evt.Type.Trim().ToLowerInvariant(),
                ObjectName = string.IsNullOrWhiteSpace(evt.ObjectName) ? null : evt.ObjectName.Trim(),
                Sentiment = string.IsNullOrWhiteSpace(evt.Sentiment) ? null : evt.Sentiment.Trim().ToLowerInvariant(),
                Summary = string.IsNullOrWhiteSpace(evt.Summary) ? null : evt.Summary.Trim(),
                Confidence = evt.Confidence
            });
        }

        if (eventBuffer.Count > 0)
        {
            await _communicationEventRepository.AddRangeAsync(eventBuffer, ct);
        }
    }

    private async Task<List<string>> GetCurrentFactStringsAsync(ExtractionItem item, CancellationToken ct)
    {
        if (_embeddingSettings.Enabled && !string.IsNullOrWhiteSpace(_embeddingSettings.Model))
        {
            var semantic = await TryGetSemanticFactContextAsync(item, ct);
            if (semantic.Count > 0)
            {
                return semantic;
            }
        }

        var list = new List<string>();
        var maxFacts = Math.Max(10, _settings.ExpensiveContextMaxFacts);
        var maxChars = Math.Max(1000, _settings.ExpensiveContextMaxChars);
        var totalChars = 0;
        foreach (var name in item.Entities.Select(x => x.Name.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var entity = await _entityRepository.FindByNameOrAliasAsync(name, ct);
            if (entity == null)
            {
                continue;
            }

            var facts = await _factRepository.GetCurrentByEntityAsync(entity.Id, ct);
            foreach (var fact in facts
                         .OrderByDescending(f => f.UpdatedAt)
                         .ThenByDescending(f => f.Confidence)
                         .Take(20))
            {
                var line = $"{entity.Name}:{fact.Category}:{fact.Key}={fact.Value}";
                if (line.Length > 400)
                {
                    line = line[..400] + "...";
                }

                if (totalChars + line.Length > maxChars || list.Count >= maxFacts)
                {
                    return list;
                }

                list.Add(line);
                totalChars += line.Length;
            }
        }

        return list;
    }

    private async Task<List<string>> TryGetSemanticFactContextAsync(ExtractionItem item, CancellationToken ct)
    {
        try
        {
            var query = BuildFactQueryText(item);
            if (string.IsNullOrWhiteSpace(query))
            {
                return new List<string>();
            }

            var queryVector = await _embeddingGenerator.GenerateAsync(_embeddingSettings.Model, query, ct);
            if (queryVector.Length == 0)
            {
                return new List<string>();
            }

            var maxFacts = Math.Max(5, _settings.ExpensiveContextMaxFacts);
            var maxChars = Math.Max(1000, _settings.ExpensiveContextMaxChars);
            var nearest = await _embeddingRepository.FindNearestAsync(FactEmbeddingOwnerType, queryVector, maxFacts, ct);
            if (nearest.Count == 0)
            {
                return new List<string>();
            }

            var result = new List<string>(maxFacts);
            var totalChars = 0;
            var seen = new HashSet<Guid>();

            foreach (var emb in nearest)
            {
                if (!Guid.TryParse(emb.OwnerId, out var factId))
                {
                    continue;
                }

                if (!seen.Add(factId))
                {
                    continue;
                }

                var fact = await _factRepository.GetByIdAsync(factId, ct);
                if (fact == null || !fact.IsCurrent)
                {
                    continue;
                }

                var entity = await _entityRepository.GetByIdAsync(fact.EntityId, ct);
                var entityName = entity?.Name ?? fact.EntityId.ToString();
                var line = $"{entityName}:{fact.Category}:{fact.Key}={fact.Value}";
                if (line.Length > 400)
                {
                    line = line[..400] + "...";
                }

                if (totalChars + line.Length > maxChars || result.Count >= maxFacts)
                {
                    break;
                }

                result.Add(line);
                totalChars += line.Length;
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Stage5 semantic fact context fallback to recency mode");
            return new List<string>();
        }
    }

    private static string BuildFactQueryText(ExtractionItem item)
    {
        var parts = new List<string>();
        foreach (var entity in item.Entities.Where(e => !string.IsNullOrWhiteSpace(e.Name)).Take(10))
        {
            parts.Add($"entity:{entity.Name}");
        }

        foreach (var fact in item.Facts.Where(f => !string.IsNullOrWhiteSpace(f.Key)).Take(20))
        {
            parts.Add($"fact:{fact.Category}:{fact.Key}={fact.Value}");
        }

        foreach (var rel in item.Relationships.Where(r => !string.IsNullOrWhiteSpace(r.Type)).Take(10))
        {
            parts.Add($"rel:{rel.FromEntityName}->{rel.Type}->{rel.ToEntityName}");
        }

        foreach (var evt in item.Events.Where(e => !string.IsNullOrWhiteSpace(e.Type)).Take(10))
        {
            parts.Add($"event:{evt.SubjectName}:{evt.Type}:{evt.Summary}");
        }

        return string.Join('\n', parts);
    }

    private static string BuildMessageText(Message m, Message? replyTo)
    {
        var parts = new List<string>();
        parts.Add(
            $"[meta] message_id={m.Id} telegram_message_id={m.TelegramMessageId} chat_id={m.ChatId} sender_id={m.SenderId} sender_name=\"{(m.SenderName ?? string.Empty).Trim()}\" ts={m.Timestamp:O} reply_to={m.ReplyToMessageId?.ToString() ?? "null"}");
        if (replyTo != null)
        {
            var replyText = TruncateForContext(replyTo.Text, 240);
            if (!string.IsNullOrWhiteSpace(replyTo.MediaTranscription))
            {
                replyText = string.IsNullOrWhiteSpace(replyText)
                    ? TruncateForContext(replyTo.MediaTranscription, 240)
                    : $"{replyText} | media: {TruncateForContext(replyTo.MediaTranscription, 120)}";
            }

            if (!string.IsNullOrWhiteSpace(replyText))
            {
                parts.Add(
                    $"[reply_context] from_sender=\"{replyTo.SenderName}\" ts={replyTo.Timestamp:O} text=\"{replyText}\"");
            }
        }

        if (!string.IsNullOrWhiteSpace(m.Text)) parts.Add(m.Text);
        if (!string.IsNullOrWhiteSpace(m.MediaTranscription)) parts.Add($"[media_transcription] {m.MediaTranscription}");
        if (!string.IsNullOrWhiteSpace(m.MediaDescription)) parts.Add($"[media_description] {m.MediaDescription}");
        if (!string.IsNullOrWhiteSpace(m.MediaParalinguisticsJson)) parts.Add($"[voice_paralinguistics] {m.MediaParalinguisticsJson}");
        return string.Join("\n", parts);
    }

    private async Task<Dictionary<long, Message>> LoadReplyContextAsync(List<Message> messages, CancellationToken ct)
    {
        var result = new Dictionary<long, Message>();
        var groups = messages
            .Where(m => m.ReplyToMessageId.HasValue && m.ReplyToMessageId.Value > 0)
            .GroupBy(m => new { m.ChatId, m.Source })
            .ToList();

        foreach (var group in groups)
        {
            var replyIds = group
                .Where(m => m.ReplyToMessageId.HasValue)
                .Select(m => m.ReplyToMessageId!.Value)
                .Distinct()
                .ToList();
            if (replyIds.Count == 0)
            {
                continue;
            }

            var byTelegramId = await _messageRepository.GetByTelegramMessageIdsAsync(
                group.Key.ChatId,
                group.Key.Source,
                replyIds,
                ct);

            foreach (var message in group)
            {
                if (!message.ReplyToMessageId.HasValue)
                {
                    continue;
                }

                if (byTelegramId.TryGetValue(message.ReplyToMessageId.Value, out var reply))
                {
                    result[message.Id] = reply;
                }
            }
        }

        return result;
    }

    private static string TruncateForContext(string? value, int maxLen)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var text = value.Trim();
        if (text.Length <= maxLen)
        {
            return text;
        }

        return text[..maxLen].TrimEnd() + "...";
    }

    private async Task EnsureDefaultPromptsAsync(CancellationToken ct)
    {
        await EnsurePromptAsync(CheapPromptId, "Stage5 Cheap Extraction v2", DefaultCheapPrompt, ct);
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

    private static bool IsGenericEntityToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "sender" => true,
            "author" => true,
            "speaker" => true,
            "user" => true,
            "me" => true,
            "myself" => true,
            "self" => true,
            "i" => true,
            _ => false
        };
    }

    private static bool IsSensitiveCategory(string? category)
    {
        return category?.Trim().ToLowerInvariant() switch
        {
            "health" => true,
            "finance" => true,
            "money" => true,
            "relationship" => true,
            "legal" => true,
            _ => false
        };
    }

    private static FactConflictStrategy GetFactConflictStrategy(string? category)
    {
        return category?.Trim().ToLowerInvariant() switch
        {
            // For sensitive domains keep conflicting versions in parallel for manual review.
            "health" => FactConflictStrategy.Parallel,
            "finance" => FactConflictStrategy.Parallel,
            "money" => FactConflictStrategy.Parallel,
            "relationship" => FactConflictStrategy.Parallel,
            "legal" => FactConflictStrategy.Parallel,
            // Schedule updates are temporal by nature and should not erase prior versions aggressively.
            "schedule" => FactConflictStrategy.Parallel,
            // Career and other stable profile fields are safe to supersede on direct conflict.
            "career" => FactConflictStrategy.Supersede,
            _ => FactConflictStrategy.Supersede
        };
    }

    private enum FactConflictStrategy
    {
        Supersede = 0,
        Parallel = 1,
        Tentative = 2
    }

    private ConfidenceStatus ResolveFactStatus(string category, float confidence)
    {
        if (IsSensitiveCategory(category) || confidence < _settings.CheapConfidenceThreshold)
        {
            return ConfidenceStatus.Tentative;
        }

        if (confidence >= _settings.AutoConfirmFactConfidence)
        {
            return ConfidenceStatus.Confirmed;
        }

        return ConfidenceStatus.Inferred;
    }

    private async Task QueueFactReviewIfNeededAsync(Fact fact, float threshold, CancellationToken ct)
    {
        if (fact.Status != ConfidenceStatus.Tentative)
        {
            return;
        }

        var reason = $"auto_review category={fact.Category} confidence={fact.Confidence:0.00} threshold={threshold:0.00}";
        await _factReviewCommandRepository.EnqueueAsync(fact.Id, "approve", reason, ct);
    }

    private async Task<Entity> UpsertEntityWithActorContextAsync(
        string name,
        EntityType fallbackType,
        Message? sourceMessage,
        string? senderName,
        CancellationToken ct)
    {
        var observedName = name.Trim();
        var isSender = sourceMessage is not null
            && !string.IsNullOrWhiteSpace(senderName)
            && string.Equals(observedName, senderName, StringComparison.OrdinalIgnoreCase);

        if (isSender)
        {
            var senderEntity = await _entityRepository.UpsertAsync(new Entity
            {
                Name = senderName!,
                Type = EntityType.Person,
                ActorKey = BuildActorKey(sourceMessage!.ChatId, sourceMessage.SenderId),
                TelegramUserId = sourceMessage.SenderId > 0 ? sourceMessage.SenderId : null
            }, ct);

            await ObserveAliasAsync(senderEntity, observedName, sourceMessage.Id, ct);
            return senderEntity;
        }

        var entity = await _entityRepository.UpsertAsync(new Entity
        {
            Name = observedName,
            Type = fallbackType
        }, ct);

        var sourceMessageId = sourceMessage?.Id;
        await ObserveAliasAsync(entity, observedName, sourceMessageId, ct);
        return entity;
    }

    private static string BuildActorKey(long chatId, long senderId) => $"{chatId}:{senderId}";

    private async Task ObserveAliasAsync(Entity entity, string observedName, long? sourceMessageId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(observedName))
        {
            return;
        }

        if (string.Equals(entity.Name, observedName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await _entityAliasRepository.UpsertAliasAsync(entity.Id, observedName, sourceMessageId, 0.9f, ct);
    }

    private static ExtractionItem NormalizeExtractionForMessage(ExtractionItem item, Message message)
    {
        var senderName = message.SenderName?.Trim();
        if (string.IsNullOrWhiteSpace(senderName))
        {
            return item;
        }

        foreach (var entity in item.Entities)
        {
            if (IsGenericEntityToken(entity.Name))
            {
                entity.Name = senderName;
                if (string.IsNullOrWhiteSpace(entity.Type))
                {
                    entity.Type = "Person";
                }
            }
        }

        foreach (var fact in item.Facts)
        {
            if (IsGenericEntityToken(fact.EntityName))
            {
                fact.EntityName = senderName;
            }
        }

        foreach (var rel in item.Relationships)
        {
            var fromGeneric = IsGenericEntityToken(rel.FromEntityName);
            var toGeneric = IsGenericEntityToken(rel.ToEntityName);
            if (fromGeneric && toGeneric)
            {
                // Drop ambiguous self/self placeholders; they produce noisy links.
                rel.FromEntityName = string.Empty;
                rel.ToEntityName = string.Empty;
                continue;
            }

            if (fromGeneric)
            {
                rel.FromEntityName = senderName;
            }

            if (toGeneric)
            {
                rel.ToEntityName = senderName;
            }
        }

        return item;
    }

    private static ExtractionItem SanitizeExtraction(ExtractionItem item)
    {
        item.Entities = item.Entities
            .Where(e => !string.IsNullOrWhiteSpace(e.Name))
            .Select(e => new ExtractionEntity
            {
                Name = e.Name.Trim(),
                Type = string.IsNullOrWhiteSpace(e.Type) ? "Person" : e.Type.Trim(),
                Confidence = Clamp01(e.Confidence)
            })
            .ToList();

        item.Facts = item.Facts
            .Where(f => !string.IsNullOrWhiteSpace(f.EntityName) && !string.IsNullOrWhiteSpace(f.Key))
            .Select(f => new ExtractionFact
            {
                EntityName = f.EntityName.Trim(),
                Category = string.IsNullOrWhiteSpace(f.Category) ? "general" : f.Category.Trim(),
                Key = f.Key.Trim(),
                Value = (f.Value ?? string.Empty).Trim(),
                Confidence = Clamp01(f.Confidence)
            })
            .Where(f => f.EntityName.Length > 0 && f.Key.Length > 0)
            .ToList();

        item.Relationships = item.Relationships
            .Where(r => !string.IsNullOrWhiteSpace(r.FromEntityName)
                        && !string.IsNullOrWhiteSpace(r.ToEntityName)
                        && !string.IsNullOrWhiteSpace(r.Type))
            .Select(r => new ExtractionRelationship
            {
                FromEntityName = r.FromEntityName.Trim(),
                ToEntityName = r.ToEntityName.Trim(),
                Type = r.Type.Trim(),
                Confidence = Clamp01(r.Confidence)
            })
            .Where(r => r.FromEntityName.Length > 0 && r.ToEntityName.Length > 0 && r.Type.Length > 0)
            .ToList();

        item.Events = item.Events
            .Where(e => !string.IsNullOrWhiteSpace(e.Type) && !string.IsNullOrWhiteSpace(e.SubjectName))
            .Select(e => new ExtractionEvent
            {
                Type = e.Type.Trim(),
                SubjectName = e.SubjectName.Trim(),
                ObjectName = string.IsNullOrWhiteSpace(e.ObjectName) ? null : e.ObjectName.Trim(),
                Sentiment = string.IsNullOrWhiteSpace(e.Sentiment) ? null : e.Sentiment.Trim().ToLowerInvariant(),
                Summary = string.IsNullOrWhiteSpace(e.Summary) ? null : e.Summary.Trim(),
                Confidence = Clamp01(e.Confidence)
            })
            .Where(e => e.Type.Length > 0 && e.SubjectName.Length > 0)
            .ToList();

        item.ProfileSignals = item.ProfileSignals
            .Where(s => !string.IsNullOrWhiteSpace(s.SubjectName) && !string.IsNullOrWhiteSpace(s.Trait))
            .Select(s => new ExtractionProfileSignal
            {
                SubjectName = s.SubjectName.Trim(),
                Trait = s.Trait.Trim().ToLowerInvariant(),
                Direction = string.IsNullOrWhiteSpace(s.Direction) ? "neutral" : s.Direction.Trim().ToLowerInvariant(),
                Evidence = string.IsNullOrWhiteSpace(s.Evidence) ? null : s.Evidence.Trim(),
                Confidence = Clamp01(s.Confidence)
            })
            .Where(s => s.SubjectName.Length > 0 && s.Trait.Length > 0)
            .ToList();

        return item;
    }

    private static float Clamp01(float value) => Math.Max(0f, Math.Min(1f, value));

    private static bool ValidateExtractionForMessage(ExtractionItem item, Message message, out string? error)
    {
        if (item.MessageId != message.Id)
        {
            error = $"message_id_mismatch:{item.MessageId}!={message.Id}";
            return false;
        }

        return ValidateExtractionRecord(item, out error);
    }

    private async Task<bool> CanRunExpensivePassAsync(CancellationToken ct)
    {
        if (_settings.ExpensiveDailyBudgetUsd <= 0)
        {
            return true;
        }

        var sinceUtc = DateTime.UtcNow.AddDays(-1);
        var spent = await _analysisUsageRepository.GetCostUsdSinceAsync("expensive", sinceUtc, ct);
        if (spent < _settings.ExpensiveDailyBudgetUsd)
        {
            return true;
        }

        _logger.LogWarning(
            "Stage5 expensive daily budget reached. spent_usd={Spent:0.000000}, budget_usd={Budget:0.000000}",
            spent,
            _settings.ExpensiveDailyBudgetUsd);
        return false;
    }

    private static bool ValidateExtractionRecord(ExtractionItem item, out string? error)
    {
        if (item.Entities.Count > 20 ||
            item.Facts.Count > 30 ||
            item.Relationships.Count > 20 ||
            item.Events.Count > 20 ||
            item.ProfileSignals.Count > 20)
        {
            error = "too_many_items";
            return false;
        }

        foreach (var entity in item.Entities)
        {
            if (!IsReasonableText(entity.Name, 120))
            {
                error = "invalid_entity_name";
                return false;
            }
        }

        foreach (var fact in item.Facts)
        {
            if (!IsReasonableText(fact.EntityName, 120) ||
                !IsReasonableText(fact.Category, 64) ||
                !IsReasonableText(fact.Key, 96) ||
                !IsReasonableText(fact.Value, 500))
            {
                error = "invalid_fact_payload";
                return false;
            }
        }

        foreach (var relationship in item.Relationships)
        {
            if (!IsReasonableText(relationship.FromEntityName, 120) ||
                !IsReasonableText(relationship.ToEntityName, 120) ||
                !IsReasonableText(relationship.Type, 64))
            {
                error = "invalid_relationship_payload";
                return false;
            }
        }

        foreach (var evt in item.Events)
        {
            if (!IsReasonableText(evt.Type, 64) ||
                !IsReasonableText(evt.SubjectName, 120) ||
                (evt.ObjectName is not null && !IsReasonableText(evt.ObjectName, 120)) ||
                (evt.Sentiment is not null && !IsReasonableText(evt.Sentiment, 32)) ||
                (evt.Summary is not null && !IsReasonableText(evt.Summary, 500)))
            {
                error = "invalid_event_payload";
                return false;
            }
        }

        foreach (var signal in item.ProfileSignals)
        {
            if (!IsReasonableText(signal.SubjectName, 120) ||
                !IsReasonableText(signal.Trait, 64) ||
                !IsReasonableText(signal.Direction, 32) ||
                (signal.Evidence is not null && !IsReasonableText(signal.Evidence, 500)))
            {
                error = "invalid_profile_signal_payload";
                return false;
            }
        }

        error = null;
        return true;
    }

    private static bool IsReasonableText(string? value, int maxLen)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.Trim();
        if (text.Length == 0 || text.Length > maxLen)
        {
            return false;
        }

        return text.All(ch => !char.IsControl(ch));
    }

    private static bool ShouldEscalateLowSignalExtraction(Message message, ExtractionItem extracted)
    {
        if (!IsLowSignalExtraction(extracted))
        {
            return false;
        }

        var content = BuildSemanticContent(message);
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        if (IsLikelyFillerMessage(content))
        {
            return false;
        }

        return HasSemanticSignal(content);
    }

    private static bool IsLowSignalExtraction(ExtractionItem item)
    {
        return item.Facts.Count == 0
               && item.Events.Count == 0
               && item.Relationships.Count == 0
               && item.ProfileSignals.Count == 0;
    }

    private static string BuildSemanticContent(Message message)
    {
        var parts = new List<string>(3);
        if (!string.IsNullOrWhiteSpace(message.Text))
        {
            parts.Add(message.Text);
        }

        if (!string.IsNullOrWhiteSpace(message.MediaTranscription))
        {
            parts.Add(message.MediaTranscription);
        }

        if (!string.IsNullOrWhiteSpace(message.MediaDescription))
        {
            parts.Add(message.MediaDescription);
        }

        return string.Join(' ', parts).Trim();
    }

    private static bool IsLikelyFillerMessage(string text)
    {
        var normalized = text.Trim().ToLowerInvariant();
        if (normalized.Length == 0)
        {
            return true;
        }

        if (!normalized.Any(char.IsLetterOrDigit))
        {
            return true;
        }

        var tokens = Regex.Matches(normalized, @"[\p{L}\p{N}]+")
            .Select(m => m.Value)
            .ToList();
        if (tokens.Count == 0)
        {
            return true;
        }

        var filler = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ок", "ok", "ага", "угу", "ясно", "пон", "понятно", "спс", "спасибо",
            "thanks", "thank", "лол", "haha", "хаха", "хах", "мм", "эм", "угуу"
        };

        return tokens.All(t => filler.Contains(t));
    }

    private static bool HasSemanticSignal(string text)
    {
        var normalized = text.ToLowerInvariant();
        var tokenCount = Regex.Matches(normalized, @"[\p{L}\p{N}]+").Count;

        if (tokenCount >= 3)
        {
            return true;
        }

        if (Regex.IsMatch(normalized, @"\b\d{1,2}(:\d{2})?\b"))
        {
            return true;
        }

        return normalized.Contains("завтра", StringComparison.Ordinal)
               || normalized.Contains("сегодня", StringComparison.Ordinal)
               || normalized.Contains("послезавтра", StringComparison.Ordinal)
               || normalized.Contains("через ", StringComparison.Ordinal)
               || normalized.Contains("буду ", StringComparison.Ordinal)
               || normalized.Contains("работ", StringComparison.Ordinal)
               || normalized.Contains("встреч", StringComparison.Ordinal)
               || normalized.Contains("освобож", StringComparison.Ordinal)
               || normalized.Contains("позвон", StringComparison.Ordinal)
               || normalized.Contains("отношен", StringComparison.Ordinal)
               || normalized.Contains("любл", StringComparison.Ordinal)
               || normalized.Contains("вместе", StringComparison.Ordinal)
               || normalized.Contains("дома", StringComparison.Ordinal)
               || normalized.Contains("уволи", StringComparison.Ordinal)
               || normalized.Contains("беремен", StringComparison.Ordinal)
               || normalized.Contains("больниц", StringComparison.Ordinal)
               || normalized.Contains("развел", StringComparison.Ordinal)
               || normalized.Contains("купил", StringComparison.Ordinal)
               || normalized.Contains("продал", StringComparison.Ordinal);
    }

    private const string DefaultCheapPrompt = """
You extract personal knowledge from chat messages.
Return ONLY a valid JSON object with field `items`.

Schema per item:
- message_id (number)
- entities: [{name,type,confidence}] where type in [Person, Organization, Place, Pet, Event]
- facts: [{entity_name,category,key,value,confidence}]
- relationships: [{from_entity_name,to_entity_name,type,confidence}]
- events: [{type,subject_name,object_name,sentiment,summary,confidence}]
- profile_signals: [{subject_name,trait,direction,evidence,confidence}]
- requires_expensive (boolean)
- reason (string, optional)

High-precision rules:
- Use exact participant names from sender_name or message text; never use placeholders like sender/me/self/i.
- Extract only stable, actionable facts about people and life context.
- Ignore pure filler/chat noise: greetings-only, emojis-only, "ok", "thanks", "haha", stickers-only chatter.
- Short messages are NOT automatically noise: if a short message contains time/date/intention/availability/relationship signal, extract at least one event or fact.
- Use metadata (`sender_name`, `ts`, `reply_to`) to preserve who said what and when.
- If a message references schedule/time/date, prefer fact category `schedule` with explicit time/date text in `value`.
- Use `events` for dynamic states (conflict, reconciliation, complaint, stress, attitude_change).
- Use `profile_signals` only for evidence-backed behavioral tendencies (Big Five style hints), not diagnosis.
- Do not duplicate the same fact in both `facts` and `events` unless temporal change is explicit.
- If unsure, do not invent; leave arrays empty.
- Set requires_expensive=true only for ambiguity or contradiction.
- Confidence range must be 0.0..1.0.

Few-shot examples:
Input message: "Ok, see you at 19:00"
Output item: {"message_id":123,"entities":[],"facts":[],"relationships":[],"events":[],"profile_signals":[],"requires_expensive":false}

Input message: "From April 1, I work at Yandex as a product manager" (sender_name="Rinat")
Output item: {"message_id":124,"entities":[{"name":"Rinat","type":"Person","confidence":0.95},{"name":"Yandex","type":"Organization","confidence":0.93}],"facts":[{"entity_name":"Rinat","category":"career","key":"employer","value":"Yandex","confidence":0.93},{"entity_name":"Rinat","category":"career","key":"position","value":"product manager","confidence":0.90}],"relationships":[],"events":[],"profile_signals":[{"subject_name":"Rinat","trait":"conscientiousness","direction":"up","evidence":"explicit career planning","confidence":0.68}],"requires_expensive":false}

Input message: "I think Masha and I are together again" (sender_name="Rinat")
Output item: {"message_id":125,"entities":[{"name":"Rinat","type":"Person","confidence":0.90},{"name":"Masha","type":"Person","confidence":0.72}],"facts":[],"relationships":[{"from_entity_name":"Rinat","to_entity_name":"Masha","type":"romantic","confidence":0.68}],"events":[{"type":"reconciliation_hint","subject_name":"Rinat","object_name":"Masha","sentiment":"positive","summary":"possible relationship restoration","confidence":0.66}],"profile_signals":[{"subject_name":"Rinat","trait":"neuroticism","direction":"up","evidence":"uncertainty marker 'I think' in intimate topic","confidence":0.55}],"requires_expensive":true,"reason":"relationship ambiguity"}

Input message: "Опять этот офис, уже ненавижу туда ходить" (sender_name="Insar")
Output item: {"message_id":126,"entities":[{"name":"Insar","type":"Person","confidence":0.94}],"facts":[],"relationships":[],"events":[{"type":"attitude_change","subject_name":"Insar","object_name":"work","sentiment":"negative","summary":"negative shift toward office work","confidence":0.79}],"profile_signals":[{"subject_name":"Insar","trait":"neuroticism","direction":"up","evidence":"strong negative affect about work routine","confidence":0.63}],"requires_expensive":false}

Never include markdown or any extra text.
""";

    private const string DefaultExpensivePrompt = """
You are a high-accuracy resolver.
Input includes one cheap candidate and current known facts.
Return ONLY valid JSON object with field `items` containing exactly one normalized item.
Prioritize consistency, deduplication and conflict-aware fact output.
If uncertainty remains, keep requires_expensive=true.
""";
}
