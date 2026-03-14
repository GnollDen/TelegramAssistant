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
    private const string CheapPromptId = "stage5_cheap_extract_v7";
    private const string ExpensivePromptId = "stage5_expensive_reason_v2";
    private const int MaxCheapLlmBatchSize = 10;
    private const string FactEmbeddingOwnerType = "fact";
    private static readonly TimeSpan BatchThrottleDelay = TimeSpan.FromMilliseconds(25);
    private static readonly Regex WordTokenRegex = new(@"[\p{L}\p{N}]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex HouseNumberTailRegex = new(
        @"^[\p{L}\-]+\s+[\p{L}\-]+(?:\s+[\p{L}\-]+){0,2}\s+\d+(?:/\d+)?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex AnyDigitRegex = new(@"\d", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex TimeTokenRegex = new(@"\b\d{1,2}(:\d{2})?\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex NumberTokenRegex = new(@"\b\d+\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
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
    private readonly IIntelligenceRepository _intelligenceRepository;
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
        IIntelligenceRepository intelligenceRepository,
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
        _intelligenceRepository = intelligenceRepository;
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
                var expensiveResolved = await ProcessExpensiveBacklogAsync(stoppingToken);
                if (expensiveResolved > 0)
                {
                    await DelayBetweenBatchesAsync(stoppingToken);
                }

                var reanalysis = await _messageRepository.GetNeedsReanalysisAsync(_settings.BatchSize, stoppingToken);
                if (reanalysis.Count > 0)
                {
                    await ProcessCheapBatchAsync(reanalysis, stoppingToken);
                    await _messageRepository.MarkNeedsReanalysisDoneAsync(reanalysis.Select(x => x.Id), stoppingToken);
                    _logger.LogInformation("Stage5 reanalysis pass done: processed={Count}", reanalysis.Count);
                    await DelayBetweenBatchesAsync(stoppingToken);
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
                await DelayBetweenBatchesAsync(stoppingToken);
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

    private async Task<int> ProcessExpensiveBacklogAsync(CancellationToken ct)
    {
        if (_settings.MaxExpensivePerBatch <= 0)
        {
            return 0;
        }

        if (!await CanRunExpensivePassAsync(ct))
        {
            return 0;
        }

        if (AreAllExpensiveModelsBlocked())
        {
            return 0;
        }

        var backlog = await _extractionRepository.GetExpensiveBacklogAsync(_settings.MaxExpensivePerBatch, ct);
        if (backlog.Count == 0)
        {
            return 0;
        }

        var expensivePrompt = await GetPromptAsync(ExpensivePromptId, DefaultExpensivePrompt, ct);
        var resolvedCount = 0;

        foreach (var row in backlog)
        {
            if (!await CanRunExpensivePassAsync(ct))
            {
                break;
            }

            var sourceMessage = await _messageRepository.GetByIdAsync(row.MessageId, ct);
            var candidate = JsonSerializer.Deserialize<ExtractionItem>(row.CheapJson) ?? new ExtractionItem { MessageId = row.MessageId };
            var finalizedCandidate = FinalizeResolvedExtraction(candidate);
            if (!ShouldRunExpensivePass(sourceMessage, candidate))
            {
                await ApplyExtractionAsync(row.MessageId, finalizedCandidate, sourceMessage, ct);
                await _extractionRepository.ResolveExpensiveAsync(row.Id, JsonSerializer.Serialize(finalizedCandidate), ct);
                continue;
            }

            var currentFacts = await GetCurrentFactStringsAsync(candidate, ct);
            var replyMessage = await LoadReplyMessageAsync(sourceMessage, ct);
            var messageText = sourceMessage == null
                ? string.Empty
                : BuildMessageText(sourceMessage, replyMessage);

            try
            {
                var resolved = await ResolveWithFallbackAsync(candidate, currentFacts, messageText, expensivePrompt.SystemPrompt, ct);
                var effective = FinalizeResolvedExtraction(resolved ?? candidate);
                effective = RefineExtractionForMessage(effective, sourceMessage);
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

                    effective = FinalizeResolvedExtraction(new ExtractionItem { MessageId = row.MessageId });
                }
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
                var sanitized = finalizedCandidate;
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
                    var sanitized = finalizedCandidate;
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
        return resolvedCount;
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
            foreach (var chunk in modelBatch.Chunk(MaxCheapLlmBatchSize))
            {
                var cheapChunk = chunk.ToList();
                try
                {
                    var cheapResult = await _analysisService.ExtractCheapAsync(model, cheapPrompt.SystemPrompt, cheapChunk, ct);
                    foreach (var item in cheapResult.Items.Where(x => x.MessageId > 0))
                    {
                        byId[item.MessageId] = item;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Stage5 cheap batch failed for model={Model}, count={Count}", model, cheapChunk.Count);
                    await _extractionErrorRepository.LogAsync(
                        stage: "stage5_cheap_batch_model",
                        reason: ex.Message,
                        payload: $"model={model};count={cheapChunk.Count}",
                        ct: ct);
                }
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
                extracted = RefineExtractionForMessage(extracted, message);
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

                var needsExpensive = ShouldRunExpensivePass(message, extracted);

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
        string messageText,
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
                    messageText,
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
        var currentFactsByEntityId = new Dictionary<Guid, List<Fact>>();
        var eventBuffer = new List<CommunicationEvent>();
        var senderName = sourceMessage?.SenderName?.Trim();

        foreach (var entity in extraction.Entities.Where(e => !string.IsNullOrWhiteSpace(e.Name)))
        {
            if (IsGenericEntityToken(entity.Name))
            {
                continue;
            }

            await GetOrCreateCachedEntityAsync(
                entityByName,
                entity.Name.Trim(),
                ParseEntityType(entity.Type),
                sourceMessage,
                senderName,
                ct);
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

            var entity = await GetOrCreateCachedEntityAsync(
                entityByName,
                fact.EntityName.Trim(),
                EntityType.Person,
                sourceMessage,
                senderName,
                ct);

            var current = await GetCurrentFactsCachedAsync(currentFactsByEntityId, entity.Id, ct);
            var sameKey = current.FirstOrDefault(x => x.Category.Equals(normalizedCategory, StringComparison.OrdinalIgnoreCase)
                                                 && x.Key.Equals(fact.Key.Trim(), StringComparison.OrdinalIgnoreCase));
            var strategy = GetFactConflictStrategy(normalizedCategory, sameKey);
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
                        currentFactsByEntityId.Remove(entity.Id);
                        await QueueFactReviewIfNeededAsync(newFact, factThreshold, ct);
                        break;
                    case FactConflictStrategy.Parallel:
                    case FactConflictStrategy.Tentative:
                        var parallelSaved = await _factRepository.UpsertAsync(newFact, ct);
                        currentFactsByEntityId.Remove(entity.Id);
                        await QueueFactReviewIfNeededAsync(parallelSaved, factThreshold, ct);
                        break;
                }
            }
            else
            {
                var saved = await _factRepository.UpsertAsync(newFact, ct);
                currentFactsByEntityId.Remove(entity.Id);
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

            var from = await GetOrCreateCachedEntityAsync(entityByName, rel.FromEntityName.Trim(), EntityType.Person, sourceMessage, senderName, ct);
            var to = await GetOrCreateCachedEntityAsync(entityByName, rel.ToEntityName.Trim(), EntityType.Person, sourceMessage, senderName, ct);

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

            var subjectEntity = await GetOrCreateCachedEntityAsync(
                entityByName,
                subjectName,
                EntityType.Person,
                sourceMessage,
                senderName,
                ct);

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

        await PersistIntelligenceAsync(messageId, extraction, sourceMessage, senderName, entityByName, ct);

        if (eventBuffer.Count > 0)
        {
            await _communicationEventRepository.AddRangeAsync(eventBuffer, ct);
        }
    }

    private async Task PersistIntelligenceAsync(
        long messageId,
        ExtractionItem extraction,
        Message? sourceMessage,
        string? senderName,
        Dictionary<string, Entity> entityByName,
        CancellationToken ct)
    {
        var observations = SelectIntelligenceObservations(extraction);
        var claims = SelectIntelligenceClaims(extraction);

        var observationRows = new List<IntelligenceObservation>(observations.Count);
        foreach (var observation in observations)
        {
            var subjectName = observation.SubjectName.Trim();
            if (subjectName.Length == 0 || IsGenericEntityToken(subjectName))
            {
                continue;
            }

            var subjectEntity = await GetOrCreateCachedEntityAsync(
                entityByName,
                subjectName,
                EntityType.Person,
                sourceMessage,
                senderName,
                ct);
            observationRows.Add(new IntelligenceObservation
            {
                MessageId = messageId,
                EntityId = subjectEntity?.Id,
                SubjectName = subjectName,
                ObservationType = observation.Type.Trim().ToLowerInvariant(),
                ObjectName = string.IsNullOrWhiteSpace(observation.ObjectName) ? null : observation.ObjectName.Trim(),
                Value = string.IsNullOrWhiteSpace(observation.Value) ? null : observation.Value.Trim(),
                Evidence = string.IsNullOrWhiteSpace(observation.Evidence) ? null : observation.Evidence.Trim(),
                Confidence = observation.Confidence,
                CreatedAt = DateTime.UtcNow
            });
        }

        var claimRows = new List<IntelligenceClaim>(claims.Count);
        foreach (var claim in claims)
        {
            var entityName = claim.EntityName.Trim();
            if (entityName.Length == 0 || IsGenericEntityToken(entityName))
            {
                continue;
            }

            var entity = await GetOrCreateCachedEntityAsync(
                entityByName,
                entityName,
                EntityType.Person,
                sourceMessage,
                senderName,
                ct);
            var normalizedCategory = string.IsNullOrWhiteSpace(claim.Category) ? "general" : claim.Category.Trim().ToLowerInvariant();
            claimRows.Add(new IntelligenceClaim
            {
                MessageId = messageId,
                EntityId = entity?.Id,
                EntityName = entityName,
                ClaimType = string.IsNullOrWhiteSpace(claim.ClaimType) ? "fact" : claim.ClaimType.Trim().ToLowerInvariant(),
                Category = normalizedCategory,
                Key = claim.Key.Trim(),
                Value = claim.Value.Trim(),
                Evidence = string.IsNullOrWhiteSpace(claim.Evidence) ? null : claim.Evidence.Trim(),
                Status = ResolveFactStatus(normalizedCategory, claim.Confidence),
                Confidence = claim.Confidence,
                CreatedAt = DateTime.UtcNow
            });
        }

        await _intelligenceRepository.ReplaceMessageIntelligenceAsync(messageId, observationRows, claimRows, ct);
    }

    private async Task<Entity> GetOrCreateCachedEntityAsync(
        Dictionary<string, Entity> entityByName,
        string entityName,
        EntityType fallbackType,
        Message? sourceMessage,
        string? senderName,
        CancellationToken ct)
    {
        var normalized = entityName.Trim();
        if (entityByName.TryGetValue(normalized, out var cached))
        {
            return cached;
        }

        var entity = await UpsertEntityWithActorContextAsync(normalized, fallbackType, sourceMessage, senderName, ct);
        entityByName[normalized] = entity;
        entityByName[entity.Name] = entity;
        return entity;
    }

    private async Task<List<Fact>> GetCurrentFactsCachedAsync(
        Dictionary<Guid, List<Fact>> currentFactsByEntityId,
        Guid entityId,
        CancellationToken ct)
    {
        if (currentFactsByEntityId.TryGetValue(entityId, out var cached))
        {
            return cached;
        }

        var facts = await _factRepository.GetCurrentByEntityAsync(entityId, ct);
        currentFactsByEntityId[entityId] = facts;
        return facts;
    }

    private static List<ExtractionObservation> SelectIntelligenceObservations(ExtractionItem extraction)
    {
        if (extraction.Observations.Count > 0)
        {
            return extraction.Observations;
        }

        return extraction.Events
            .Where(e => !string.IsNullOrWhiteSpace(e.Type) && !string.IsNullOrWhiteSpace(e.SubjectName))
            .Select(e => new ExtractionObservation
            {
                SubjectName = e.SubjectName,
                Type = e.Type,
                ObjectName = e.ObjectName,
                Value = e.Summary,
                Evidence = e.Summary,
                Confidence = e.Confidence
            })
            .ToList();
    }

    private static List<ExtractionClaim> SelectIntelligenceClaims(ExtractionItem extraction)
    {
        if (extraction.Claims.Count > 0)
        {
            return extraction.Claims;
        }

        var claims = new List<ExtractionClaim>();
        claims.AddRange(extraction.Facts.Select(f => new ExtractionClaim
        {
            EntityName = f.EntityName,
            ClaimType = "fact",
            Category = f.Category,
            Key = f.Key,
            Value = f.Value,
            Evidence = f.Value,
            Confidence = f.Confidence
        }));
        claims.AddRange(extraction.Relationships.Select(r => new ExtractionClaim
        {
            EntityName = r.FromEntityName,
            ClaimType = "relationship",
            Category = "relationship",
            Key = r.Type,
            Value = r.ToEntityName,
            Evidence = $"{r.FromEntityName} -> {r.Type} -> {r.ToEntityName}",
            Confidence = r.Confidence
        }));
        claims.AddRange(extraction.ProfileSignals.Select(s => new ExtractionClaim
        {
            EntityName = s.SubjectName,
            ClaimType = "profile_signal",
            Category = "profile",
            Key = s.Trait,
            Value = s.Direction,
            Evidence = s.Evidence,
            Confidence = s.Confidence
        }));
        return claims;
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

        foreach (var name in item.Claims
                     .Select(x => x.EntityName.Trim())
                     .Concat(item.Facts.Select(x => x.EntityName.Trim()))
                     .Where(x => x.Length > 0)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (list.Count >= maxFacts || totalChars >= maxChars)
            {
                break;
            }

            var entity = await _entityRepository.FindByNameOrAliasAsync(name, ct);
            if (entity == null)
            {
                continue;
            }

            var facts = await _factRepository.GetCurrentByEntityAsync(entity.Id, ct);
            foreach (var fact in facts.OrderByDescending(f => f.UpdatedAt).Take(10))
            {
                var line = $"{entity.Name}:{fact.Category}:{fact.Key}={fact.Value}";
                if (line.Length > 400)
                {
                    line = line[..400] + "...";
                }

                if (list.Contains(line, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
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

        foreach (var claim in item.Claims.Where(c => !string.IsNullOrWhiteSpace(c.Key)).Take(20))
        {
            parts.Add($"claim:{claim.EntityName}:{claim.Category}:{claim.Key}={claim.Value}");
        }

        foreach (var rel in item.Relationships.Where(r => !string.IsNullOrWhiteSpace(r.Type)).Take(10))
        {
            parts.Add($"rel:{rel.FromEntityName}->{rel.Type}->{rel.ToEntityName}");
        }

        foreach (var evt in item.Events.Where(e => !string.IsNullOrWhiteSpace(e.Type)).Take(10))
        {
            parts.Add($"event:{evt.SubjectName}:{evt.Type}:{evt.Summary}");
        }

        foreach (var observation in item.Observations.Where(o => !string.IsNullOrWhiteSpace(o.Type)).Take(10))
        {
            parts.Add($"obs:{observation.SubjectName}:{observation.Type}:{observation.Value ?? observation.Evidence}");
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

    private async Task<Message?> LoadReplyMessageAsync(Message? message, CancellationToken ct)
    {
        if (message?.ReplyToMessageId is null || message.ReplyToMessageId.Value <= 0)
        {
            return null;
        }

        var byTelegramId = await _messageRepository.GetByTelegramMessageIdsAsync(
            message.ChatId,
            message.Source,
            [message.ReplyToMessageId.Value],
            ct);

        return byTelegramId.GetValueOrDefault(message.ReplyToMessageId.Value);
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
        await EnsurePromptAsync(CheapPromptId, "Stage5 Cheap Extraction v6", DefaultCheapPrompt, ct);
        await EnsurePromptAsync(ExpensivePromptId, "Stage5 Expensive Reasoning v2", DefaultExpensivePrompt, ct);
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

    private FactConflictStrategy GetFactConflictStrategy(string? category, Fact? sameKey)
    {
        return category?.Trim().ToLowerInvariant() switch
        {
            // For sensitive domains keep conflicting versions in parallel for manual review.
            "health" => FactConflictStrategy.Parallel,
            "finance" => FactConflictStrategy.Parallel,
            "money" => FactConflictStrategy.Parallel,
            "relationship" => FactConflictStrategy.Parallel,
            "legal" => FactConflictStrategy.Parallel,
            // Availability/schedule updates are temporal: supersede stale values, keep recent versions in parallel.
            "availability" => sameKey != null
                              && (DateTime.UtcNow - sameKey.UpdatedAt).TotalHours > _settings.TemporalFactSupersedeTtlHours
                ? FactConflictStrategy.Supersede
                : FactConflictStrategy.Parallel,
            "schedule" => sameKey != null
                          && (DateTime.UtcNow - sameKey.UpdatedAt).TotalHours > _settings.TemporalFactSupersedeTtlHours
                ? FactConflictStrategy.Supersede
                : FactConflictStrategy.Parallel,
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

        foreach (var claim in item.Claims)
        {
            if (IsGenericEntityToken(claim.EntityName))
            {
                claim.EntityName = senderName;
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

        foreach (var observation in item.Observations)
        {
            if (IsGenericEntityToken(observation.SubjectName))
            {
                observation.SubjectName = senderName;
            }

            if (!string.IsNullOrWhiteSpace(observation.ObjectName) && IsGenericEntityToken(observation.ObjectName))
            {
                observation.ObjectName = senderName;
            }
        }

        return item;
    }

    private static ExtractionItem SanitizeExtraction(ExtractionItem item)
    {
        item.Reason = string.IsNullOrWhiteSpace(item.Reason) ? null : item.Reason.Trim();

        item.Entities = item.Entities
            .Where(e => !string.IsNullOrWhiteSpace(e.Name))
            .Select(e => new ExtractionEntity
            {
                Name = e.Name.Trim(),
                Type = string.IsNullOrWhiteSpace(e.Type) ? "Person" : e.Type.Trim(),
                Confidence = Clamp01(e.Confidence)
            })
            .ToList();

        item.Observations = item.Observations
            .Where(o => !string.IsNullOrWhiteSpace(o.SubjectName) && !string.IsNullOrWhiteSpace(o.Type))
            .Select(o => new ExtractionObservation
            {
                SubjectName = o.SubjectName.Trim(),
                Type = o.Type.Trim(),
                ObjectName = string.IsNullOrWhiteSpace(o.ObjectName) ? null : o.ObjectName.Trim(),
                Value = string.IsNullOrWhiteSpace(o.Value) ? null : o.Value.Trim(),
                Evidence = string.IsNullOrWhiteSpace(o.Evidence) ? null : o.Evidence.Trim(),
                Confidence = Clamp01(o.Confidence)
            })
            .Where(o => o.SubjectName.Length > 0 && o.Type.Length > 0)
            .ToList();

        item.Claims = item.Claims
            .Where(c => !string.IsNullOrWhiteSpace(c.EntityName) && !string.IsNullOrWhiteSpace(c.Key))
            .Select(c => new ExtractionClaim
            {
                EntityName = c.EntityName.Trim(),
                ClaimType = string.IsNullOrWhiteSpace(c.ClaimType) ? "fact" : c.ClaimType.Trim(),
                Category = string.IsNullOrWhiteSpace(c.Category) ? "general" : c.Category.Trim(),
                Key = c.Key.Trim(),
                Value = NormalizeClaimValue(c),
                Evidence = string.IsNullOrWhiteSpace(c.Evidence) ? null : c.Evidence.Trim(),
                Confidence = Clamp01(c.Confidence)
            })
            .Where(c => c.EntityName.Length > 0 && c.Key.Length > 0 && c.Value.Length > 0)
            .ToList();

        item.Facts = item.Facts
            .Where(f => !string.IsNullOrWhiteSpace(f.EntityName) && !string.IsNullOrWhiteSpace(f.Key))
            .Select(f => new ExtractionFact
            {
                EntityName = f.EntityName.Trim(),
                Category = string.IsNullOrWhiteSpace(f.Category) ? "general" : f.Category.Trim(),
                Key = f.Key.Trim(),
                Value = NormalizePlainValue(f.Value),
                Confidence = Clamp01(f.Confidence)
            })
            .Where(f => f.EntityName.Length > 0 && f.Key.Length > 0 && f.Value.Length > 0)
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

    private ExtractionItem RefineExtractionForMessage(ExtractionItem item, Message? message)
    {
        if (message == null)
        {
            return item;
        }

        var rawContent = BuildSemanticContent(message);
        if (string.IsNullOrWhiteSpace(rawContent))
        {
            item.Entities = TrimUnreferencedEntities(item);
            return item;
        }

        var content = CollapseWhitespace(rawContent);
        var normalized = content.ToLowerInvariant();
        PromoteLocationFallback(item, message, rawContent, normalized);
        PromoteContactFallback(item, message, rawContent);
        PromoteWorkAssessmentFallback(item, message, content, normalized);
        PruneLowValueSignals(item, normalized);
        item.Entities = TrimUnreferencedEntities(item);
        return item;
    }

    private static void PromoteLocationFallback(ExtractionItem item, Message message, string content, string normalizedContent)
    {
        if (HasLocationSignal(item))
        {
            return;
        }

        if (!TryExtractAddressLikeText(content, normalizedContent, out var address))
        {
            return;
        }

        var senderName = message.SenderName?.Trim();
        if (string.IsNullOrWhiteSpace(senderName))
        {
            return;
        }

        var evidence = TruncateForContext(address, 200);
        AddEntityIfMissing(item.Entities, address, "Place", 0.9f);
        AddObservationIfMissing(item.Observations, new ExtractionObservation
        {
            SubjectName = senderName,
            Type = "location_update",
            ObjectName = address,
            Value = address,
            Evidence = evidence,
            Confidence = 0.84f
        });
        AddClaimIfMissing(item.Claims, new ExtractionClaim
        {
            EntityName = senderName,
            ClaimType = "fact",
            Category = "location",
            Key = "shared_location",
            Value = address,
            Evidence = evidence,
            Confidence = 0.84f
        });
        AddFactIfMissing(item.Facts, new ExtractionFact
        {
            EntityName = senderName,
            Category = "location",
            Key = "shared_location",
            Value = address,
            Confidence = 0.84f
        });
    }

    private static void PromoteContactFallback(ExtractionItem item, Message message, string content)
    {
        if (HasContactSignal(item))
        {
            return;
        }

        if (!TryExtractContactShare(content, out var contactName, out var handle, out var evidence))
        {
            return;
        }

        var senderName = message.SenderName?.Trim();
        if (string.IsNullOrWhiteSpace(senderName))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(contactName))
        {
            AddEntityIfMissing(item.Entities, contactName, "Person", 0.9f);
        }

        var contactValue = string.IsNullOrWhiteSpace(contactName)
            ? handle
            : $"{contactName} {handle}";

        AddObservationIfMissing(item.Observations, new ExtractionObservation
        {
            SubjectName = senderName,
            Type = "contact_share",
            ObjectName = string.IsNullOrWhiteSpace(contactName) ? handle : contactName,
            Value = handle,
            Evidence = evidence,
            Confidence = 0.84f
        });
        AddClaimIfMissing(item.Claims, new ExtractionClaim
        {
            EntityName = senderName,
            ClaimType = "fact",
            Category = "contact",
            Key = "shared_contact",
            Value = contactValue,
            Evidence = evidence,
            Confidence = 0.84f
        });
        AddFactIfMissing(item.Facts, new ExtractionFact
        {
            EntityName = senderName,
            Category = "contact",
            Key = "shared_contact",
            Value = contactValue,
            Confidence = 0.84f
        });
    }

    private static void PromoteWorkAssessmentFallback(ExtractionItem item, Message message, string content, string normalizedContent)
    {
        if (item.Claims.Any(c => string.Equals(c.Category, "work", StringComparison.OrdinalIgnoreCase)) ||
            item.Facts.Any(f => string.Equals(f.Category, "work", StringComparison.OrdinalIgnoreCase)) ||
            item.Observations.Any(o => string.Equals(o.Type, "work_assessment", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        if (!LooksLikeWorkAssessmentMessage(normalizedContent))
        {
            return;
        }

        var senderName = message.SenderName?.Trim();
        if (string.IsNullOrWhiteSpace(senderName))
        {
            return;
        }

        var evidence = TruncateForContext(content, 220);
        AddObservationIfMissing(item.Observations, new ExtractionObservation
        {
            SubjectName = senderName,
            Type = "work_assessment",
            ObjectName = "team",
            Value = evidence,
            Evidence = evidence,
            Confidence = 0.8f
        });
        AddClaimIfMissing(item.Claims, new ExtractionClaim
        {
            EntityName = senderName,
            ClaimType = "state",
            Category = "work",
            Key = "team_assessment",
            Value = evidence,
            Evidence = evidence,
            Confidence = 0.8f
        });
    }

    private void PruneLowValueSignals(ExtractionItem item, string normalizedContent)
    {
        var hasHighValueCategory = item.Facts.Any(f => IsHighValueCategory(f.Category))
                                   || item.Claims.Any(c => IsHighValueCategory(c.Category));
        if (hasHighValueCategory)
        {
            return;
        }

        var hasConfidentSignal = item.Facts.Any(f => f.Confidence >= _settings.MinFactConfidence)
                                 || item.Claims.Any(c => c.Confidence >= _settings.MinFactConfidence);
        if (hasConfidentSignal)
        {
            return;
        }

        if (HasHighValueStructuredSignal(item))
        {
            return;
        }

        var keepOperational = HasConcreteActionableAnchor(normalizedContent) && HasOperationalSignal(item);
        var keepContextual = HasStrongDossierSignal(normalizedContent) && HasNonTrivialStructuredSignal(item);

        if (!keepOperational && !keepContextual)
        {
            item.Observations.Clear();
            item.Claims.Clear();
            item.Facts.Clear();
            item.Relationships.Clear();
            item.Events.Clear();
            item.ProfileSignals.Clear();
            return;
        }

        item.Observations = item.Observations
            .Where(o => ShouldKeepOperationalObservation(o, normalizedContent))
            .ToList();

        item.Claims = item.Claims
            .Where(c => ShouldKeepOperationalClaim(c, normalizedContent))
            .ToList();

        item.Facts = item.Facts
            .Where(f => ShouldKeepOperationalFact(f))
            .ToList();
    }

    private static bool HasOperationalSignal(ExtractionItem item)
    {
        return item.Observations.Any(o => IsOperationalObservationType(o.Type))
               || item.Claims.Any(c => IsOperationalClaim(c))
               || item.Facts.Any(ShouldKeepOperationalFact);
    }

    private static bool HasNonTrivialStructuredSignal(ExtractionItem item)
    {
        return item.Observations.Count > 0
               || item.Claims.Count > 0
               || item.Facts.Count > 0
               || item.Relationships.Count > 0
               || item.Events.Count > 0
               || item.ProfileSignals.Count > 0;
    }

    private static bool ShouldKeepOperationalObservation(ExtractionObservation observation, string normalizedContent)
    {
        if (IsHighValueObservationType(observation.Type))
        {
            return true;
        }

        var type = observation.Type.Trim().ToLowerInvariant();
        return type switch
        {
            "request" or "question" or "intent" => HasConcreteActionableAnchor(normalizedContent),
            "status_update" => HasStrongDossierSignal(normalizedContent),
            _ => false
        };
    }

    private static bool ShouldKeepOperationalClaim(ExtractionClaim claim, string normalizedContent)
    {
        if (IsHighValueCategory(claim.Category) || IsLocationOrContactKey(claim.Key))
        {
            return true;
        }

        var claimType = string.IsNullOrWhiteSpace(claim.ClaimType)
            ? string.Empty
            : claim.ClaimType.Trim().ToLowerInvariant();

        return claimType is "intent" or "need"
               && HasConcreteActionableAnchor(normalizedContent);
    }

    private static bool ShouldKeepOperationalFact(ExtractionFact fact)
    {
        return IsHighValueCategory(fact.Category) || IsLocationOrContactKey(fact.Key);
    }

    private static bool IsOperationalObservationType(string? type)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            return false;
        }

        return type.Trim().ToLowerInvariant() is
            "request" or "question" or "intent" or "status_update"
            or "availability_update" or "schedule_update" or "movement"
            or "location_update" or "contact_share" or "work_assessment";
    }

    private static bool IsOperationalClaim(ExtractionClaim claim)
    {
        return IsHighValueCategory(claim.Category)
               || IsLocationOrContactKey(claim.Key)
               || string.Equals(claim.ClaimType, "intent", StringComparison.OrdinalIgnoreCase)
               || string.Equals(claim.ClaimType, "need", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasLocationSignal(ExtractionItem item)
    {
        return item.Facts.Any(f => string.Equals(f.Category, "location", StringComparison.OrdinalIgnoreCase))
               || item.Claims.Any(c => string.Equals(c.Category, "location", StringComparison.OrdinalIgnoreCase))
               || item.Observations.Any(o => string.Equals(o.Type, "location_update", StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasContactSignal(ExtractionItem item)
    {
        return item.Facts.Any(f => string.Equals(f.Category, "contact", StringComparison.OrdinalIgnoreCase) || IsLocationOrContactKey(f.Key))
               || item.Claims.Any(c => string.Equals(c.Category, "contact", StringComparison.OrdinalIgnoreCase) || IsLocationOrContactKey(c.Key))
               || item.Observations.Any(o => string.Equals(o.Type, "contact_share", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsLocationOrContactKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        return key.Trim().ToLowerInvariant() is "shared_location" or "shared_address" or "shared_contact";
    }

    private static bool TryExtractAddressLikeText(string content, string normalizedContent, out string address)
    {
        address = string.Empty;
        var lines = SplitMeaningfulLines(content).ToList();
        var hasMapLink = normalizedContent.Contains("yandex.ru/maps", StringComparison.Ordinal)
                         || normalizedContent.Contains("google.com/maps", StringComparison.Ordinal)
                         || normalizedContent.Contains("2gis.ru", StringComparison.Ordinal);

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.Contains("http://", StringComparison.OrdinalIgnoreCase) || trimmed.Contains("https://", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var normalizedLine = trimmed.ToLowerInvariant();
            var hasAddressLexeme = ContainsAny(normalizedLine,
                "ул", "улиц", "просп", "шоссе", "переул", "бул", "дом", "д.", "кв", "корп", "подъезд", "этаж", "адрес");

            var looksLikeHouseNumberTail = HouseNumberTailRegex.IsMatch(trimmed);
            if ((hasAddressLexeme && AnyDigitRegex.IsMatch(trimmed)) || (hasMapLink && looksLikeHouseNumberTail) || looksLikeHouseNumberTail)
            {
                address = trimmed;
                return true;
            }
        }

        return false;
    }

    private static bool TryExtractContactShare(string content, out string contactName, out string handle, out string evidence)
    {
        contactName = string.Empty;
        handle = string.Empty;
        evidence = string.Empty;

        var match = Regex.Match(
            content,
            @"(?im)^(?<name>[\p{L}][\p{L}\-]+(?:\s+[\p{L}][\p{L}\-]+){0,2})\s+@(?<handle>[A-Za-z0-9_]{3,32})\s*$");

        if (!match.Success)
        {
            return false;
        }

        contactName = match.Groups["name"].Value.Trim();
        handle = "@" + match.Groups["handle"].Value.Trim();
        evidence = TruncateForContext(match.Value.Trim(), 160);
        return true;
    }

    private static bool LooksLikeWorkAssessmentMessage(string normalizedContent)
    {
        if (!ContainsAny(normalizedContent, "команд", "трайб", "алерт", "дефект", "тойл", "работ", "релиз", "проект"))
        {
            return false;
        }

        return ContainsAny(normalizedContent,
            "не сравним", "хуже", "лучше", "все плохо", "всё плохо", "пиздец", "амеб", "амёб",
            "шикар", "горди", "прибавилось", "мастер класс", "мастер-класс", "хорошо", "плохо");
    }

    private static void AddEntityIfMissing(List<ExtractionEntity> entities, string name, string type, float confidence)
    {
        if (string.IsNullOrWhiteSpace(name) || entities.Any(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        entities.Add(new ExtractionEntity
        {
            Name = name,
            Type = type,
            Confidence = confidence
        });
    }

    private static void AddObservationIfMissing(List<ExtractionObservation> observations, ExtractionObservation observation)
    {
        if (observations.Any(existing =>
                string.Equals(existing.SubjectName, observation.SubjectName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(existing.Type, observation.Type, StringComparison.OrdinalIgnoreCase)
                && string.Equals(existing.Value, observation.Value, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        observations.Add(observation);
    }

    private static void AddClaimIfMissing(List<ExtractionClaim> claims, ExtractionClaim claim)
    {
        if (claims.Any(existing =>
                string.Equals(existing.EntityName, claim.EntityName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(existing.Category, claim.Category, StringComparison.OrdinalIgnoreCase)
                && string.Equals(existing.Key, claim.Key, StringComparison.OrdinalIgnoreCase)
                && string.Equals(existing.Value, claim.Value, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        claims.Add(claim);
    }

    private static void AddFactIfMissing(List<ExtractionFact> facts, ExtractionFact fact)
    {
        if (facts.Any(existing =>
                string.Equals(existing.EntityName, fact.EntityName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(existing.Category, fact.Category, StringComparison.OrdinalIgnoreCase)
                && string.Equals(existing.Key, fact.Key, StringComparison.OrdinalIgnoreCase)
                && string.Equals(existing.Value, fact.Value, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        facts.Add(fact);
    }

    private static List<ExtractionEntity> TrimUnreferencedEntities(ExtractionItem item)
    {
        var referencedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var fact in item.Facts)
        {
            if (!string.IsNullOrWhiteSpace(fact.EntityName))
            {
                referencedNames.Add(fact.EntityName);
            }
        }

        foreach (var claim in item.Claims)
        {
            if (!string.IsNullOrWhiteSpace(claim.EntityName))
            {
                referencedNames.Add(claim.EntityName);
            }
        }

        foreach (var relation in item.Relationships)
        {
            if (!string.IsNullOrWhiteSpace(relation.FromEntityName))
            {
                referencedNames.Add(relation.FromEntityName);
            }

            if (!string.IsNullOrWhiteSpace(relation.ToEntityName))
            {
                referencedNames.Add(relation.ToEntityName);
            }
        }

        foreach (var observation in item.Observations)
        {
            if (!string.IsNullOrWhiteSpace(observation.SubjectName))
            {
                referencedNames.Add(observation.SubjectName);
            }

            if (!string.IsNullOrWhiteSpace(observation.ObjectName))
            {
                referencedNames.Add(observation.ObjectName);
            }
        }

        foreach (var evt in item.Events)
        {
            if (!string.IsNullOrWhiteSpace(evt.SubjectName))
            {
                referencedNames.Add(evt.SubjectName);
            }

            if (!string.IsNullOrWhiteSpace(evt.ObjectName))
            {
                referencedNames.Add(evt.ObjectName);
            }
        }

        foreach (var signal in item.ProfileSignals)
        {
            if (!string.IsNullOrWhiteSpace(signal.SubjectName))
            {
                referencedNames.Add(signal.SubjectName);
            }
        }

        return item.Entities
            .Where(e => !string.IsNullOrWhiteSpace(e.Name) && referencedNames.Contains(e.Name))
            .ToList();
    }

    private static IEnumerable<string> SplitMeaningfulLines(string content)
    {
        return content
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !string.IsNullOrWhiteSpace(line));
    }

    private static string CollapseWhitespace(string value)
    {
        return Regex.Replace(value, @"\s+", " ").Trim();
    }

    private static ExtractionItem FinalizeResolvedExtraction(ExtractionItem item)
    {
        var effective = SanitizeExtraction(item);
        effective.RequiresExpensive = false;

        if (IsLowSignalExtraction(effective) && IsLowValueAmbiguityReason(effective.Reason))
        {
            effective.Entities = new List<ExtractionEntity>();
        }

        return effective;
    }

    private static float Clamp01(float value) => Math.Max(0f, Math.Min(1f, value));

    private static string NormalizeClaimValue(ExtractionClaim claim)
    {
        var direct = NormalizePlainValue(claim.Value);
        if (direct.Length > 0)
        {
            return direct;
        }

        var evidence = NormalizePlainValue(claim.Evidence);
        if (evidence.Length > 0)
        {
            return evidence;
        }

        return HumanizeKey(claim.Key);
    }

    private static string NormalizePlainValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    private static string HumanizeKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        return key.Trim().Replace('_', ' ').Replace('-', ' ');
    }

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
            item.Observations.Count > 30 ||
            item.Claims.Count > 40 ||
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

        foreach (var observation in item.Observations)
        {
            if (!IsReasonableText(observation.SubjectName, 120) ||
                !IsReasonableText(observation.Type, 64) ||
                (observation.ObjectName is not null && !IsReasonableText(observation.ObjectName, 120)) ||
                (observation.Value is not null && !IsReasonableText(observation.Value, 500)) ||
                (observation.Evidence is not null && !IsReasonableText(observation.Evidence, 500)))
            {
                error = "invalid_observation_payload";
                return false;
            }
        }

        foreach (var claim in item.Claims)
        {
            if (!IsReasonableText(claim.EntityName, 120) ||
                !IsReasonableText(claim.ClaimType, 64) ||
                !IsReasonableText(claim.Category, 64) ||
                !IsReasonableText(claim.Key, 96) ||
                !IsReasonableText(claim.Value, 500) ||
                (claim.Evidence is not null && !IsReasonableText(claim.Evidence, 500)))
            {
                error = "invalid_claim_payload";
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

    private bool ShouldRunExpensivePass(Message? message, ExtractionItem extracted)
    {
        if (message == null)
        {
            return false;
        }

        var content = BuildSemanticContent(message);
        if (string.IsNullOrWhiteSpace(content) || IsLikelyFillerMessage(content))
        {
            return false;
        }

        var lowSignalEscalation = ShouldEscalateLowSignalExtraction(message, extracted);
        if (lowSignalEscalation)
        {
            return true;
        }

        var hasLowConfidenceStructuredSignal =
            extracted.Facts.Any(f => f.Confidence < _settings.CheapConfidenceThreshold && IsHighValueCategory(f.Category)) ||
            extracted.Relationships.Any(r => r.Confidence < _settings.CheapConfidenceThreshold) ||
            extracted.Claims.Any(c => c.Confidence < _settings.CheapConfidenceThreshold && IsHighValueCategory(c.Category)) ||
            extracted.Events.Any(e => e.Confidence < _settings.CheapConfidenceThreshold) ||
            extracted.Observations.Any(o => o.Confidence < _settings.CheapConfidenceThreshold && IsHighValueObservationType(o.Type));

        if (!extracted.RequiresExpensive && !hasLowConfidenceStructuredSignal)
        {
            return false;
        }

        return IsHighValueEscalationCandidate(content, extracted);
    }

    private static bool IsLowSignalExtraction(ExtractionItem item)
    {
        return item.Claims.Count == 0
               && item.Observations.Count == 0
               && item.Facts.Count == 0
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

        var tokens = WordTokenRegex.Matches(normalized)
            .Select(m => m.Value)
            .ToList();
        if (tokens.Count == 0)
        {
            return true;
        }

        var filler = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "\u043E\u043A", "ok", "\u0430\u0433\u0430", "\u0443\u0433\u0443", "\u044F\u0441\u043D\u043E",
            "\u043F\u043E\u043D", "\u043F\u043E\u043D\u044F\u0442\u043D\u043E", "\u0441\u043F\u0441",
            "\u0441\u043F\u0430\u0441\u0438\u0431\u043E", "thanks", "thank", "\u043B\u043E\u043B", "haha",
            "\u0445\u0430\u0445\u0430", "\u0445\u0430\u0445", "\u043C\u043C", "\u044D\u043C", "\u0443\u0433\u0443\u0443"
        };

        return tokens.All(t => filler.Contains(t));
    }

    private static bool HasSemanticSignal(string text)
    {
        var normalized = text.ToLowerInvariant();
        var tokenCount = WordTokenRegex.Matches(normalized).Count;

        if (tokenCount >= 6)
        {
            return true;
        }

        if (HasStrongDossierSignal(normalized))
        {
            return true;
        }

        return HasConcreteActionableAnchor(normalized) && tokenCount >= 3;
    }

    private static bool IsHighValueEscalationCandidate(string text, ExtractionItem extracted)
    {
        var normalized = text.ToLowerInvariant();
        var tokenCount = WordTokenRegex.Matches(normalized).Count;

        if (HasHighValueStructuredSignal(extracted))
        {
            return true;
        }

        if (HasStrongDossierSignal(normalized))
        {
            return true;
        }

        return tokenCount >= 8 && HasConcreteActionableAnchor(normalized);
    }

    private static bool HasHighValueStructuredSignal(ExtractionItem extracted)
    {
        return extracted.Relationships.Count > 0
               || extracted.Events.Count > 0
               || extracted.Facts.Any(f => IsHighValueCategory(f.Category))
               || extracted.Claims.Any(c => IsHighValueCategory(c.Category))
               || extracted.Observations.Any(o => IsHighValueObservationType(o.Type));
    }

    private static bool IsHighValueCategory(string? category)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return false;
        }

        return category.Trim().ToLowerInvariant() switch
        {
            "availability" => true,
            "schedule" => true,
            "travel" => true,
            "transportation" => true,
            "work" => true,
            "finance" => true,
            "health" => true,
            "relationship" => true,
            "location" => true,
            "contact" => true,
            "purchase" => true,
            "family" => true,
            "career" => true,
            "education" => true,
            "project" => true,
            _ => false
        };
    }

    private static bool IsHighValueObservationType(string? type)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            return false;
        }

        return type.Trim().ToLowerInvariant() switch
        {
            "availability_update" => true,
            "movement" => true,
            "travel_plan" => true,
            "schedule_update" => true,
            "work_update" => true,
            "work_status" => true,
            "work_assessment" => true,
            "health_update" => true,
            "health_report" => true,
            "location_update" => true,
            "contact_share" => true,
            "relationship_signal" => true,
            _ => false
        };
    }

    private static bool HasStrongDossierSignal(string normalized)
    {
        return ContainsAny(normalized,
            "работ", "офис", "уволи", "зарплат", "доход", "кредит", "ипотек", "долг", "лимит", "команд", "трайб", "алерт", "дефект", "тойл", "проект",
            "боль", "врач", "больниц", "беремен", "травм", "температур", "диагноз", "лекар", "антибиот", "леч",
            "встреч", "развел", "расст", "муж", "жена", "парень", "девуш", "любл",
            "купил", "продал", "заказ", "стоим", "цена", "руб", "тыс", "деньг",
            "домой", "дома", "приед", "уед", "поед", "вылет", "летим", "поезд", "такси", "забрать", "выхожу", "еду",
            "сегодня", "завтра", "послезавтра", "через ", "буду ", "свобод", "занят",
            "адрес", "улица", "ул.", "просп", "шоссе", "подъезд", "этаж", "@");
    }

    private static bool HasConcreteActionableAnchor(string normalized)
    {
        return TimeTokenRegex.IsMatch(normalized)
               || NumberTokenRegex.IsMatch(normalized)
               || ContainsAny(normalized,
                   "в ", "на ", "к ", "до ", "после ", "через ",
                   "сегодня", "завтра", "послезавтра", "утром", "днем", "днём", "вечером",
                   "минут", "час", "домой", "дом", "офис", "работ", "встреч", "звон", "позвон", "куп", "прод", "верн",
                   "адрес", "улица", "ул.", "просп", "подъезд", "этаж", "забрать", "такси", "@", "maps");
    }

    private static bool IsLowValueAmbiguityReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return false;
        }

        var normalized = reason.Trim().ToLowerInvariant();
        return normalized.Contains("ambiguous", StringComparison.Ordinal)
               || normalized.Contains("incomplete", StringComparison.Ordinal)
               || normalized.Contains("too little context", StringComparison.Ordinal)
               || normalized.Contains("missing context", StringComparison.Ordinal)
               || normalized.Contains("unclear", StringComparison.Ordinal);
    }

    private static bool ContainsAny(string text, params string[] patterns)
    {
        foreach (var pattern in patterns)
        {
            if (text.Contains(pattern, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static Task DelayBetweenBatchesAsync(CancellationToken ct)
    {
        return Task.Delay(BatchThrottleDelay, ct);
    }

    private const string DefaultCheapPrompt = """
You extract intelligence signals from chat logs.
Return ONLY a valid JSON object with field `items`.
For each input `<message id="...">` return exactly one item with the same `message_id`.

Goal:
- maximize grounded recall for dossier-useful or operationally useful signals
- keep empty for low-value chatter, filler, or generic chat summarization
- keep labels reusable and concise

Schema per item:
- message_id (number)
- entities: [{name,type,confidence}] where type in [Person, Organization, Place, Pet, Event]
- observations: [{subject_name,type,object_name,value,evidence,confidence}]
- claims: [{entity_name,claim_type,category,key,value,evidence,confidence}]
- facts: [{entity_name,category,key,value,confidence}]
- relationships: [{from_entity_name,to_entity_name,type,confidence}]
- events: [{type,subject_name,object_name,sentiment,summary,confidence}]
- profile_signals: [{subject_name,trait,direction,evidence,confidence}]
- requires_expensive (boolean)
- reason (string, optional)

Definitions:
- observations: message-local grounded signals
- claims: atomic dossier-ready statements grounded in one message
- facts/relationships/events/profile_signals remain for backward compatibility

Type guidance:
- observation.type should be short snake_case and reusable
- prefer stable labels like availability_update, movement, request, question, intent, schedule_update, work_update, work_assessment, health_update, location_update, contact_share, relationship_signal, communication, other
- claim.claim_type should usually be one of: fact, intent, preference, relationship, state, need
- category should be broad and reusable: availability, schedule, travel, transportation, work, finance, health, relationship, communication, activity, purchase, location, contact, education, family, project, other
- do not create near-duplicate labels just because wording differs

Rules:
- use real participant names from sender_name/text/reply_context; never use placeholders like sender, author, me, self, i
- prioritize signals with durable or actionable value: availability, schedule, travel, movement, pickup/dropoff, work/team/project state, finance, health, relationship, address/location, shared contacts
- keep empty for pure noise and low-value chat filler: emoji-only, sticker-only, laughter-only, generic agreements, vague acknowledgements, rhetorical filler, low-value reactions, generic tech gripes with no lasting relevance
- a question/request/agreement is only worth extracting when it is actionable: time, place, movement, pickup, call, meeting, health, work, travel, money, address, contact
- if a third party is explicit in the message or reply_context, attribute the signal to that third party instead of automatically using the sender
- if the subject is unresolved and the signal is low-value, return empty arrays
- when a Russian person or place is in oblique case and the canonical form is obvious, normalize to the canonical form; otherwise keep the observed form
- extract shared addresses, map links, @handles, pickup/dropoff logistics, and destination options as location/contact/travel signals
- prefer grounded claims/facts only when the current message supports them; do not invent hidden context
- evidence should be a short grounded snippet or tight paraphrase from the same message
- if there is a useful fact or relationship, also try to emit at least one supporting claim
- if there is a useful event-like signal, also try to emit at least one observation
- set requires_expensive=true only when the message is materially useful for a dossier but grounded extraction is blocked by ambiguity or missing context
- do NOT set requires_expensive=true for vague short coordination, filler planning, incomplete chatter, or low-value snippets
- confidence must be 0.0..1.0

Examples:
Input: <message id="101">[meta] sender_name="Rinat" ... I will be free in 20 minutes</message>
Output item: {"message_id":101,"entities":[{"name":"Rinat","type":"Person","confidence":0.98}],"observations":[{"subject_name":"Rinat","type":"availability_update","object_name":null,"value":"in 20 minutes","evidence":"will be free in 20 minutes","confidence":0.88}],"claims":[{"entity_name":"Rinat","claim_type":"fact","category":"availability","key":"free_time","value":"in 20 minutes","evidence":"will be free in 20 minutes","confidence":0.88}],"facts":[{"entity_name":"Rinat","category":"availability","key":"free_time","value":"in 20 minutes","confidence":0.88}],"relationships":[],"events":[{"type":"availability_update","subject_name":"Rinat","object_name":null,"sentiment":"neutral","summary":"reported when he will be free","confidence":0.82}],"profile_signals":[],"requires_expensive":false}

Input: <message id="102">[meta] sender_name="Rinat" ... улица Шавалеева, 1 ... https://yandex.ru/maps/...</message>
Output item: {"message_id":102,"entities":[{"name":"Rinat","type":"Person","confidence":0.98},{"name":"улица Шавалеева, 1","type":"Place","confidence":0.92}],"observations":[{"subject_name":"Rinat","type":"location_update","object_name":"улица Шавалеева, 1","value":"улица Шавалеева, 1","evidence":"улица Шавалеева, 1","confidence":0.86}],"claims":[{"entity_name":"Rinat","claim_type":"fact","category":"location","key":"shared_location","value":"улица Шавалеева, 1","evidence":"улица Шавалеева, 1","confidence":0.86}],"facts":[{"entity_name":"Rinat","category":"location","key":"shared_location","value":"улица Шавалеева, 1","confidence":0.86}],"relationships":[],"events":[],"profile_signals":[],"requires_expensive":false}

Input: <message id="103">[meta] sender_name="Alena" ... Катя @Kotyonoksok</message>
Output item: {"message_id":103,"entities":[{"name":"Alena","type":"Person","confidence":0.98},{"name":"Катя","type":"Person","confidence":0.9}],"observations":[{"subject_name":"Alena","type":"contact_share","object_name":"Катя","value":"@Kotyonoksok","evidence":"Катя @Kotyonoksok","confidence":0.84}],"claims":[{"entity_name":"Alena","claim_type":"fact","category":"contact","key":"shared_contact","value":"Катя @Kotyonoksok","evidence":"Катя @Kotyonoksok","confidence":0.84}],"facts":[{"entity_name":"Alena","category":"contact","key":"shared_contact","value":"Катя @Kotyonoksok","confidence":0.84}],"relationships":[],"events":[],"profile_signals":[],"requires_expensive":false}

Input: <message id="104">[reply_context] from_sender="Alena" text="Катя уже неделю болеет" [meta] sender_name="Rinat" ... Она все еще на антибиотиках</message>
Output item: {"message_id":104,"entities":[{"name":"Катя","type":"Person","confidence":0.9}],"observations":[{"subject_name":"Катя","type":"health_update","object_name":"antibiotics","value":"still on antibiotics","evidence":"Она все еще на антибиотиках","confidence":0.86}],"claims":[{"entity_name":"Катя","claim_type":"state","category":"health","key":"antibiotics_course","value":"still on antibiotics","evidence":"Она все еще на антибиотиках","confidence":0.86}],"facts":[],"relationships":[],"events":[],"profile_signals":[],"requires_expensive":false}

Input: <message id="105">[meta] sender_name="Alena" ... ну да</message>
Output item: {"message_id":105,"entities":[],"observations":[],"claims":[],"facts":[],"relationships":[],"events":[],"profile_signals":[],"requires_expensive":false}

Never include markdown or extra text.
""";
    private const string DefaultExpensivePrompt = """
You are a high-accuracy resolver for dossier extraction.
Input includes:
- the original message text with metadata
- one cheap candidate extraction
- current known facts for the same entity set

Return ONLY a valid JSON object with field `items` containing exactly one item.
The item schema is the same as cheap extraction:
- message_id
- entities
- observations
- claims
- facts
- relationships
- events
- profile_signals
- requires_expensive
- reason

Rules:
- use the original message text as the primary evidence source
- improve the cheap candidate only when the current message contains grounded, useful information
- keep labels reusable and normalized
- do not hallucinate missing context
- if the message is vague, low-value, or too context-dependent to extract safely, return empty arrays and requires_expensive=false
- if the message is clearly important but still ambiguous after careful reading, keep requires_expensive=true and set reason
- prefer grounded claims and observations over speculative interpretation
- preserve durable facts only when directly supported by the current message

Examples:
Input message: [meta] sender_name="Alena" ... My income is stable around 5000 per month
Output item: {"message_id":102,"entities":[{"name":"Alena","type":"Person","confidence":0.98}],"observations":[{"subject_name":"Alena","type":"status_update","object_name":"income","value":"~5000 per month","evidence":"income is stable around 5000 per month","confidence":0.9}],"claims":[{"entity_name":"Alena","claim_type":"fact","category":"finance","key":"monthly_income","value":"~5000 per month","evidence":"income is stable around 5000 per month","confidence":0.9}],"facts":[{"entity_name":"Alena","category":"finance","key":"monthly_income","value":"~5000 per month","confidence":0.9}],"relationships":[],"events":[],"profile_signals":[],"requires_expensive":false}

Input message: [meta] sender_name="Alena" ... and then I'll go
Output item: {"message_id":104,"entities":[],"observations":[],"claims":[],"facts":[],"relationships":[],"events":[],"profile_signals":[],"requires_expensive":false}
""";
}
