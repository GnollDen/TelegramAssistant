using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage5;

public class ExpensivePassResolver
{
    private const string FactEmbeddingOwnerType = "fact";
    private const string ExpensiveBackoffStateLegacyKey = "stage5:expensive_backoff_until";
    private const string ExpensiveBackoffStateKeyPrefix = "stage5:expensive_backoff_until:model:";
    private readonly AnalysisSettings _settings;
    private readonly EmbeddingSettings _embeddingSettings;
    private readonly OpenRouterAnalysisService _analysisService;
    private readonly IMessageExtractionRepository _extractionRepository;
    private readonly IExtractionErrorRepository _extractionErrorRepository;
    private readonly IAnalysisUsageRepository _analysisUsageRepository;
    private readonly IAnalysisStateRepository _analysisStateRepository;
    private readonly IMessageRepository _messageRepository;
    private readonly IEntityRepository _entityRepository;
    private readonly IFactRepository _factRepository;
    private readonly IEmbeddingRepository _embeddingRepository;
    private readonly ITextEmbeddingGenerator _embeddingGenerator;
    private readonly ExtractionApplier _extractionApplier;
    private readonly MessageContentBuilder _messageContentBuilder;
    private readonly AnalysisContextBuilder _contextBuilder;
    private readonly ILogger<ExpensivePassResolver> _logger;
    private readonly Dictionary<string, DateTimeOffset> _expensiveBlockedUntilByModel = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _expensiveFailureStreakByModel = new(StringComparer.OrdinalIgnoreCase);
    private bool _expensiveBackoffStateLoaded;
    private bool _expensiveDisabledLogged;

    public ExpensivePassResolver(
        IOptions<AnalysisSettings> settings,
        IOptions<EmbeddingSettings> embeddingSettings,
        OpenRouterAnalysisService analysisService,
        IMessageExtractionRepository extractionRepository,
        IExtractionErrorRepository extractionErrorRepository,
        IAnalysisUsageRepository analysisUsageRepository,
        IAnalysisStateRepository analysisStateRepository,
        IMessageRepository messageRepository,
        IEntityRepository entityRepository,
        IFactRepository factRepository,
        IEmbeddingRepository embeddingRepository,
        ITextEmbeddingGenerator embeddingGenerator,
        ExtractionApplier extractionApplier,
        MessageContentBuilder messageContentBuilder,
        AnalysisContextBuilder contextBuilder,
        ILogger<ExpensivePassResolver> logger)
    {
        _settings = settings.Value;
        _embeddingSettings = embeddingSettings.Value;
        _analysisService = analysisService;
        _extractionRepository = extractionRepository;
        _extractionErrorRepository = extractionErrorRepository;
        _analysisUsageRepository = analysisUsageRepository;
        _analysisStateRepository = analysisStateRepository;
        _messageRepository = messageRepository;
        _entityRepository = entityRepository;
        _factRepository = factRepository;
        _embeddingRepository = embeddingRepository;
        _embeddingGenerator = embeddingGenerator;
        _extractionApplier = extractionApplier;
        _messageContentBuilder = messageContentBuilder;
        _contextBuilder = contextBuilder;
        _logger = logger;
    }

    /// <summary>
    /// Processes expensive-stage backlog with model fallback and backoff handling.
    /// </summary>
    public async Task<int> ProcessExpensiveBacklogAsync(Func<CancellationToken, Task<string>> getPromptAsync, CancellationToken ct)
    {
        await EnsureExpensiveBackoffStateLoadedAsync(ct);

        if (!_settings.ExpensivePassEnabled)
        {
            if (!_expensiveDisabledLogged)
            {
                _logger.LogInformation(
                    "Stage5 expensive pass is disabled by policy. Set Analysis:ExpensivePassEnabled=true and Analysis:MaxExpensivePerBatch>0 to activate.");
                _expensiveDisabledLogged = true;
            }

            return 0;
        }

        if (_settings.MaxExpensivePerBatch <= 0)
        {
            if (!_expensiveDisabledLogged)
            {
                _logger.LogInformation(
                    "Stage5 expensive pass is configured with zero batch limit. Set Analysis:MaxExpensivePerBatch>0 to activate.");
                _expensiveDisabledLogged = true;
            }

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

        var expensivePrompt = await getPromptAsync(ct);
        var resolvedCount = 0;

        foreach (var row in backlog)
        {
            if (!await CanRunExpensivePassAsync(ct))
            {
                break;
            }

            var sourceMessage = await _messageRepository.GetByIdAsync(row.MessageId, ct);
            var candidate = JsonSerializer.Deserialize<ExtractionItem>(row.CheapJson) ?? new ExtractionItem { MessageId = row.MessageId };
            var finalizedCandidate = ExtractionRefiner.FinalizeResolvedExtraction(candidate);
            if (!ExtractionRefiner.ShouldRunExpensivePass(sourceMessage, candidate, _settings))
            {
                await _extractionApplier.ApplyExtractionAsync(row.MessageId, finalizedCandidate, sourceMessage, ct);
                await _extractionRepository.ResolveExpensiveAsync(row.Id, JsonSerializer.Serialize(finalizedCandidate), ct);
                continue;
            }

            var currentFacts = await GetCurrentFactStringsAsync(candidate, ct);
            var replyMessage = await _messageContentBuilder.LoadReplyMessageAsync(sourceMessage, ct);
            AnalysisMessageContext? context = null;
            if (sourceMessage != null)
            {
                var built = await _contextBuilder.BuildBatchContextsAsync([sourceMessage], ct);
                context = built.GetValueOrDefault(sourceMessage.Id);
            }

            var messageText = sourceMessage == null
                ? string.Empty
                : MessageContentBuilder.BuildMessageText(sourceMessage, replyMessage, context);

            try
            {
                var resolved = await ResolveWithFallbackAsync(candidate, currentFacts, messageText, context, expensivePrompt, ct);
                var effective = ExtractionRefiner.FinalizeResolvedExtraction(resolved ?? candidate);
                effective = ExtractionRefiner.RefineExtractionForMessage(effective, sourceMessage, _settings);
                effective.MessageId = row.MessageId;
                if (!ExtractionValidator.ValidateExtractionRecord(effective, out var validationError))
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

                    effective = ExtractionRefiner.FinalizeResolvedExtraction(new ExtractionItem { MessageId = row.MessageId });
                }

                await _extractionApplier.ApplyExtractionAsync(row.MessageId, effective, sourceMessage, ct);
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
                await _extractionApplier.ApplyExtractionAsync(row.MessageId, sanitized, sourceMessage, ct);
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
                    await _extractionApplier.ApplyExtractionAsync(row.MessageId, sanitized, sourceMessage, ct);
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

    private async Task<ExtractionItem?> ResolveWithFallbackAsync(
        ExtractionItem candidate,
        List<string> currentFacts,
        string messageText,
        AnalysisMessageContext? context,
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
                    context,
                    ct);
                await RegisterModelSuccessAsync(model, ct);
                return resolved;
            }
            catch (Exception ex) when (ShouldFallback(ex) || IsProviderDenied(ex))
            {
                var denied = IsProviderDenied(ex);
                ex.Data["expensive_model"] = model;
                await RegisterModelFailureAsync(model, denied, ct);
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
                   || msg.Contains(" 429", StringComparison.OrdinalIgnoreCase)
                   || msg.Contains("\"code\":429", StringComparison.OrdinalIgnoreCase)
                   || msg.Contains("forbidden", StringComparison.OrdinalIgnoreCase)
                   || msg.Contains("rate limit", StringComparison.OrdinalIgnoreCase)
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

    private async Task RegisterModelFailureAsync(string model, bool denied, CancellationToken ct)
    {
        var key = NormalizeModelKey(model);
        var current = _expensiveFailureStreakByModel.TryGetValue(key, out var streak) ? streak : 0;
        var next = Math.Max(1, current + 1);
        _expensiveFailureStreakByModel[key] = next;

        var backoff = ComputeExpensiveBackoff(next);
        var deniedCooldown = TimeSpan.FromMinutes(Math.Max(1, _settings.ExpensiveCooldownMinutes));
        var effective = denied ? Max(backoff, deniedCooldown) : backoff;
        _expensiveBlockedUntilByModel[key] = DateTimeOffset.UtcNow.Add(effective);
        await PersistExpensiveBackoffStateAsync(ct);
    }

    private async Task RegisterModelSuccessAsync(string model, CancellationToken ct)
    {
        var key = NormalizeModelKey(model);
        _expensiveFailureStreakByModel.Remove(key);
        _expensiveBlockedUntilByModel.Remove(key);
        await PersistExpensiveBackoffStateAsync(ct);
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

    private static string BuildExpensiveBackoffStateKey(string model)
    {
        var normalized = NormalizeModelKey(model);
        return $"{ExpensiveBackoffStateKeyPrefix}{normalized}";
    }

    private async Task EnsureExpensiveBackoffStateLoadedAsync(CancellationToken ct)
    {
        if (_expensiveBackoffStateLoaded)
        {
            return;
        }

        _expensiveBackoffStateLoaded = true;
        var now = DateTimeOffset.UtcNow;
        var loadedModels = new List<string>();
        foreach (var model in GetDistinctExpensiveModels())
        {
            var stateKey = BuildExpensiveBackoffStateKey(model);
            var unixSeconds = await _analysisStateRepository.GetWatermarkAsync(stateKey, ct);
            if (unixSeconds <= 0)
            {
                continue;
            }

            var blockedUntil = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
            if (blockedUntil <= now)
            {
                continue;
            }

            _expensiveBlockedUntilByModel[NormalizeModelKey(model)] = blockedUntil;
            loadedModels.Add(model);
        }

        if (loadedModels.Count > 0)
        {
            _logger.LogInformation(
                "Loaded persisted Stage5 expensive model backoff: models={Models}",
                string.Join(", ", loadedModels));
            return;
        }

        var legacyUnixSeconds = await _analysisStateRepository.GetWatermarkAsync(ExpensiveBackoffStateLegacyKey, ct);
        if (legacyUnixSeconds <= 0)
        {
            return;
        }

        var legacyBlockedUntil = DateTimeOffset.FromUnixTimeSeconds(legacyUnixSeconds);
        if (legacyBlockedUntil <= now)
        {
            return;
        }

        foreach (var model in GetDistinctExpensiveModels())
        {
            _expensiveBlockedUntilByModel[NormalizeModelKey(model)] = legacyBlockedUntil;
        }

        _logger.LogInformation(
            "Loaded legacy Stage5 expensive backoff and applied to configured models. blocked_until={BlockedUntil}",
            legacyBlockedUntil);
    }

    private async Task PersistExpensiveBackoffStateAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var model in GetDistinctExpensiveModels())
        {
            var modelKey = NormalizeModelKey(model);
            var stateKey = BuildExpensiveBackoffStateKey(model);
            var value = _expensiveBlockedUntilByModel.TryGetValue(modelKey, out var blockedUntil) && blockedUntil > now
                ? blockedUntil.ToUnixTimeSeconds()
                : 0;
            await _analysisStateRepository.SetWatermarkAsync(stateKey, value, ct);
        }

        await _analysisStateRepository.SetWatermarkAsync(ExpensiveBackoffStateLegacyKey, 0, ct);
    }

    private static string BuildExpensiveErrorPayload(Exception ex)
    {
        var model = ex.Data.Contains("expensive_model")
            ? ex.Data["expensive_model"]?.ToString()
            : null;
        var modelPart = string.IsNullOrWhiteSpace(model) ? "unknown" : model.Trim();
        return $"model={modelPart};exception={ex.GetType().Name}";
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
            var nearest = await _embeddingRepository.FindNearestAsync(FactEmbeddingOwnerType, _embeddingSettings.Model, queryVector, maxFacts, ct);
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
}
