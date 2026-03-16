using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage5;

public partial class AnalysisWorkerService : BackgroundService
{
    private const string WatermarkKey = "stage5:watermark";
    private const string CheapPromptId = "stage5_cheap_extract_v7";
    private const string ExpensivePromptId = "stage5_expensive_reason_v2";
    private const int MaxCheapLlmBatchSize = 10;
    private static readonly Regex ServicePlaceholderRegex = new(@"^\[[A-Z_]{2,32}\]$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly TimeSpan BatchThrottleDelay = TimeSpan.FromMilliseconds(25);
    private static readonly TimeSpan OpenRouterRecoveryProbeTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan OpenRouterRecoveryPollInterval = TimeSpan.FromSeconds(15);

    private readonly AnalysisSettings _settings;
    private readonly IMessageRepository _messageRepository;
    private readonly IMessageExtractionRepository _extractionRepository;
    private readonly IExtractionErrorRepository _extractionErrorRepository;
    private readonly IAnalysisStateRepository _stateRepository;
    private readonly IPromptTemplateRepository _promptRepository;
    private readonly OpenRouterAnalysisService _analysisService;
    private readonly ExpensivePassResolver _expensivePassResolver;
    private readonly ExtractionApplier _extractionApplier;
    private readonly MessageContentBuilder _messageContentBuilder;
    private readonly AnalysisContextBuilder _contextBuilder;
    private readonly ILogger<AnalysisWorkerService> _logger;

    public AnalysisWorkerService(
        IOptions<AnalysisSettings> settings,
        IMessageRepository messageRepository,
        IMessageExtractionRepository extractionRepository,
        IExtractionErrorRepository extractionErrorRepository,
        IAnalysisStateRepository stateRepository,
        IPromptTemplateRepository promptRepository,
        OpenRouterAnalysisService analysisService,
        ExpensivePassResolver expensivePassResolver,
        ExtractionApplier extractionApplier,
        MessageContentBuilder messageContentBuilder,
        AnalysisContextBuilder contextBuilder,
        ILogger<AnalysisWorkerService> logger)
    {
        _settings = settings.Value;
        _messageRepository = messageRepository;
        _extractionRepository = extractionRepository;
        _extractionErrorRepository = extractionErrorRepository;
        _stateRepository = stateRepository;
        _promptRepository = promptRepository;
        _analysisService = analysisService;
        _expensivePassResolver = expensivePassResolver;
        _extractionApplier = extractionApplier;
        _messageContentBuilder = messageContentBuilder;
        _contextBuilder = contextBuilder;
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
        _logger.LogInformation(
            "Stage5 analysis worker started. cheap_model={Cheap}, expensive_model={Expensive}, cheap_parallelism={CheapParallelism}, cheap_batch_workers={CheapBatchWorkers}",
            _settings.CheapModel,
            _settings.ExpensiveModel,
            GetCheapLlmParallelism(),
            GetCheapBatchWorkers());

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var expensiveResolved = await _expensivePassResolver.ProcessExpensiveBacklogAsync(
                    async ct => (await GetPromptAsync(ExpensivePromptId, DefaultExpensivePrompt, ct)).SystemPrompt,
                    stoppingToken);
                if (expensiveResolved > 0)
                {
                    await DelayBetweenBatchesAsync(stoppingToken);
                }

                var reanalysis = await _messageRepository.GetNeedsReanalysisAsync(GetFetchLimit(), stoppingToken);
                if (reanalysis.Count > 0)
                {
                    var reanalysisResult = await ProcessCheapBatchesAsync(reanalysis, stoppingToken);
                    var succeededReanalysis = reanalysis
                        .Select(x => x.Id)
                        .Where(id => !reanalysisResult.FailedMessageIds.Contains(id));
                    await _messageRepository.MarkNeedsReanalysisDoneAsync(succeededReanalysis, stoppingToken);

                    if (reanalysisResult.BalanceIssueDetected)
                    {
                        await WaitForOpenRouterRecoveryAsync(stoppingToken);
                    }

                    _logger.LogInformation("Stage5 reanalysis pass done: processed={Count}", reanalysis.Count);
                    await DelayBetweenBatchesAsync(stoppingToken);
                    continue;
                }

                var watermark = await _stateRepository.GetWatermarkAsync(WatermarkKey, stoppingToken);
                var messages = await _messageRepository.GetProcessedAfterIdAsync(watermark, GetFetchLimit(), stoppingToken);
                if (messages.Count == 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(_settings.PollIntervalSeconds), stoppingToken);
                    continue;
                }

                var cheapResult = await ProcessCheapBatchesAsync(messages, stoppingToken);

                if (cheapResult.BalanceIssueDetected)
                {
                    await WaitForOpenRouterRecoveryAsync(stoppingToken);
                }

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

    private async Task<CheapPassResult> ProcessCheapBatchesAsync(List<Message> messages, CancellationToken ct)
    {
        if (messages.Count == 0)
        {
            return new CheapPassResult(new HashSet<long>(), false);
        }

        var failedMessageIds = new ConcurrentDictionary<long, byte>();
        var balanceIssueDetected = 0;
        var batchSize = Math.Max(1, _settings.BatchSize);
        var batches = messages
            .Chunk(batchSize)
            .Select(chunk => chunk.ToList())
            .ToList();

        if (batches.Count <= 1 || GetCheapBatchWorkers() <= 1)
        {
            foreach (var batch in batches)
            {
                try
                {
                    var batchResult = await ProcessCheapBatchAsync(batch, ct);
                    foreach (var id in batchResult.FailedMessageIds)
                    {
                        failedMessageIds.TryAdd(id, 0);
                    }

                    if (batchResult.BalanceIssueDetected)
                    {
                        Interlocked.Exchange(ref balanceIssueDetected, 1);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Stage5 cheap worker batch failed: batch_size={BatchSize}",
                        batch.Count);

                    foreach (var message in batch)
                    {
                        failedMessageIds.TryAdd(message.Id, 0);
                    }

                    await _messageRepository.MarkNeedsReanalysisAsync(batch.Select(x => x.Id), ct);
                    await _extractionErrorRepository.LogAsync(
                        stage: "stage5_cheap_worker_batch",
                        reason: ex.Message,
                        payload: $"batch_size={batch.Count}",
                        ct: ct);
                }
            }

            return new CheapPassResult(failedMessageIds.Keys.ToHashSet(), balanceIssueDetected == 1);
        }

        await Parallel.ForEachAsync(
            batches,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = GetCheapBatchWorkers(),
                CancellationToken = ct
            },
            async (batch, token) =>
            {
                try
                {
                    var batchResult = await ProcessCheapBatchAsync(batch, token);
                    foreach (var id in batchResult.FailedMessageIds)
                    {
                        failedMessageIds.TryAdd(id, 0);
                    }

                    if (batchResult.BalanceIssueDetected)
                    {
                        Interlocked.Exchange(ref balanceIssueDetected, 1);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Stage5 cheap worker batch failed: batch_size={BatchSize}",
                        batch.Count);

                    foreach (var message in batch)
                    {
                        failedMessageIds.TryAdd(message.Id, 0);
                    }

                    await _messageRepository.MarkNeedsReanalysisAsync(batch.Select(x => x.Id), token);
                    await _extractionErrorRepository.LogAsync(
                        stage: "stage5_cheap_worker_batch",
                        reason: ex.Message,
                        payload: $"batch_size={batch.Count}",
                        ct: token);
                }
            });

        return new CheapPassResult(failedMessageIds.Keys.ToHashSet(), balanceIssueDetected == 1);
    }

    private async Task<CheapPassResult> ProcessCheapBatchAsync(List<Message> messages, CancellationToken ct)
    {
        var analyzableMessages = new List<Message>(messages.Count);
        var skippedByReason = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var message in messages)
        {
            if (TryGetCheapSkipReason(message, out var reason))
            {
                skippedByReason[reason] = skippedByReason.GetValueOrDefault(reason) + 1;
                continue;
            }

            analyzableMessages.Add(message);
        }

        if (skippedByReason.Count > 0)
        {
            _logger.LogInformation(
                "Stage5 cheap prefilter skipped={Skipped} of {Total}: {Reasons}",
                messages.Count - analyzableMessages.Count,
                messages.Count,
                string.Join(", ", skippedByReason.Select(x => $"{x.Key}={x.Value}")));
        }

        var modelByMessageId = BuildCheapModelMap(messages);
        var byId = new ConcurrentDictionary<long, ExtractionItem>();
        var failedChunkMessageIds = new ConcurrentDictionary<long, byte>();
        var balanceIssueDetected = 0;
        if (analyzableMessages.Count > 0)
        {
            var replyContext = await _messageContentBuilder.LoadReplyContextAsync(analyzableMessages, ct);
            var contexts = await _contextBuilder.BuildBatchContextsAsync(analyzableMessages, ct);
            var batch = analyzableMessages.Select(m => new AnalysisInputMessage
            {
                MessageId = m.Id,
                SenderName = m.SenderName,
                Timestamp = m.Timestamp,
                Text = MessageContentBuilder.BuildMessageText(
                    m,
                    replyContext.GetValueOrDefault(m.Id),
                    contexts.GetValueOrDefault(m.Id))
            }).ToList();

            var cheapPrompt = await GetPromptAsync(CheapPromptId, DefaultCheapPrompt, ct);
            var cheapChunks = batch
                .GroupBy(x => modelByMessageId.GetValueOrDefault(x.MessageId, _settings.CheapModel))
                .SelectMany(group => group
                    .Chunk(MaxCheapLlmBatchSize)
                    .Select(chunk => new CheapChunkRequest(group.Key, chunk.ToList())))
                .ToList();

            await Parallel.ForEachAsync(
                cheapChunks,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = GetCheapLlmParallelism(),
                    CancellationToken = ct
                },
                async (request, token) =>
                {
                    try
                    {
                        var cheapResult = await _analysisService.ExtractCheapAsync(
                            request.Model,
                            cheapPrompt.SystemPrompt,
                            request.Messages,
                            token);

                        foreach (var item in cheapResult.Items.Where(x => x.MessageId > 0))
                        {
                            byId[item.MessageId] = item;
                        }
                    }
                    catch (Exception ex)
                    {
                        if (ex is OpenRouterBalanceException)
                        {
                            Interlocked.Exchange(ref balanceIssueDetected, 1);
                        }

                        _logger.LogWarning(
                            ex,
                            "Stage5 cheap batch failed for model={Model}, count={Count}",
                            request.Model,
                            request.Messages.Count);

                        await _extractionErrorRepository.LogAsync(
                            stage: "stage5_cheap_batch_model",
                            reason: ex.Message,
                            payload: $"model={request.Model};count={request.Messages.Count}",
                            ct: token);

                        if (ex is not OpenRouterBalanceException)
                        {
                            var fallbackModel = ResolveSingleFallbackModel(request.Model, ex);
                            var singleFallbackFailed = await TryProcessChunkOneByOneAsync(
                                request,
                                cheapPrompt.SystemPrompt,
                                fallbackModel,
                                byId,
                                token);

                            foreach (var failedId in singleFallbackFailed)
                            {
                                failedChunkMessageIds.TryAdd(failedId, 0);
                            }
                        }
                        else
                        {
                            foreach (var failed in request.Messages)
                            {
                                failedChunkMessageIds.TryAdd(failed.MessageId, 0);
                            }
                        }
                    }
                });
        }

        if (!failedChunkMessageIds.IsEmpty)
        {
            await _messageRepository.MarkNeedsReanalysisAsync(failedChunkMessageIds.Keys, ct);
            _logger.LogWarning(
                "Stage5 tagged failed cheap chunk messages for reanalysis: count={Count}, balance_issue={BalanceIssue}",
                failedChunkMessageIds.Count,
                balanceIssueDetected == 1);
        }

        foreach (var message in messages)
        {
            if (failedChunkMessageIds.ContainsKey(message.Id))
            {
                continue;
            }

            try
            {
                byId.TryGetValue(message.Id, out var extracted);
                extracted ??= new ExtractionItem { MessageId = message.Id };
                extracted = ExtractionRefiner.NormalizeExtractionForMessage(extracted, message);
                extracted = ExtractionRefiner.SanitizeExtraction(extracted);
                extracted = ExtractionRefiner.RefineExtractionForMessage(extracted, message, _settings);
                extracted.MessageId = message.Id;

                if (!ExtractionValidator.ValidateExtractionForMessage(extracted, message, out var validationError))
                {
                    _logger.LogWarning(
                        "Stage5 extraction validation rejected message_id={MessageId}: {Reason}",
                        message.Id,
                        validationError);

                    await _extractionErrorRepository.LogAsync(
                        stage: "stage5_validation",
                        reason: validationError ?? "invalid_extraction",
                        messageId: message.Id,
                        payload: $"model={modelByMessageId.GetValueOrDefault(message.Id, _settings.CheapModel)};json={JsonSerializer.Serialize(extracted, ExtractionSerializationOptions.SnakeCase)}",
                        ct: ct);

                    continue;
                }

                var needsExpensive = IsExpensivePassEnabled() &&
                                     ExtractionRefiner.ShouldRunExpensivePass(message, extracted, _settings);

                await _extractionRepository.UpsertCheapAsync(message.Id, JsonSerializer.Serialize(extracted, ExtractionSerializationOptions.SnakeCase), needsExpensive, ct);

                if (!needsExpensive)
                {
                    await _extractionApplier.ApplyExtractionAsync(message.Id, extracted, message, ct);
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

        return new CheapPassResult(failedChunkMessageIds.Keys.ToHashSet(), balanceIssueDetected == 1);
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

    private static Task DelayBetweenBatchesAsync(CancellationToken ct)
    {
        return Task.Delay(BatchThrottleDelay, ct);
    }

    private async Task WaitForOpenRouterRecoveryAsync(CancellationToken ct)
    {
        _logger.LogWarning(
            "Stage5 paused due to OpenRouter balance/quota issue. Poll interval={PollSeconds}s, probe timeout={ProbeSeconds}s",
            OpenRouterRecoveryPollInterval.TotalSeconds,
            OpenRouterRecoveryProbeTimeout.TotalSeconds);

        var attempts = 0;
        while (!ct.IsCancellationRequested)
        {
            attempts++;
            try
            {
                using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                probeCts.CancelAfter(OpenRouterRecoveryProbeTimeout);
                var recovered = await _analysisService.ProbeCheapAvailabilityAsync(probeCts.Token);
                if (recovered)
                {
                    _logger.LogInformation("OpenRouter recovery probe succeeded after attempts={Attempts}. Resuming Stage5.", attempts);
                    return;
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Probe timeout; keep waiting.
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "OpenRouter recovery probe failed on attempt={Attempt}", attempts);
            }

            await Task.Delay(OpenRouterRecoveryPollInterval, ct);
        }
    }

    private int GetCheapLlmParallelism()
    {
        return Math.Clamp(_settings.CheapLlmParallelism, 1, 16);
    }

    private async Task<HashSet<long>> TryProcessChunkOneByOneAsync(
        CheapChunkRequest request,
        string cheapPrompt,
        string model,
        ConcurrentDictionary<long, ExtractionItem> byId,
        CancellationToken ct)
    {
        var failed = new HashSet<long>();
        var perMessageTimeoutSeconds = Math.Clamp(_settings.HttpTimeoutSeconds / 3, 20, 60);
        if (!string.Equals(model, request.Model, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation(
                "Stage5 cheap single fallback model override: from={SourceModel}, to={FallbackModel}, count={Count}",
                request.Model,
                model,
                request.Messages.Count);
        }

        foreach (var message in request.Messages)
        {
            try
            {
                using var messageCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                messageCts.CancelAfter(TimeSpan.FromSeconds(perMessageTimeoutSeconds));
                var single = await _analysisService.ExtractCheapAsync(
                    model,
                    cheapPrompt,
                    [message],
                    messageCts.Token);

                var item = single.Items.FirstOrDefault(x => x.MessageId == message.MessageId);
                if (item != null)
                {
                    byId[message.MessageId] = item;
                }
            }
            catch (Exception ex)
            {
                failed.Add(message.MessageId);
                _logger.LogWarning(
                    ex,
                    "Stage5 cheap single fallback failed for model={Model}, message_id={MessageId}, timeout_s={TimeoutSeconds}",
                    model,
                    message.MessageId,
                    perMessageTimeoutSeconds);

                await _extractionErrorRepository.LogAsync(
                    stage: "stage5_cheap_single_fallback",
                    reason: ex.Message,
                    messageId: message.MessageId,
                    payload: $"model={model};timeout_s={perMessageTimeoutSeconds}",
                    ct: ct);
            }
        }

        if (failed.Count < request.Messages.Count)
        {
            _logger.LogInformation(
                "Stage5 cheap single fallback recovered={Recovered} of {Total} for model={Model}",
                request.Messages.Count - failed.Count,
                request.Messages.Count,
                model);
        }

        return failed;
    }

    private string ResolveSingleFallbackModel(string sourceModel, Exception ex)
    {
        if (ex is TaskCanceledException or TimeoutException)
        {
            var baseline = _settings.CheapBaselineModel?.Trim();
            if (!string.IsNullOrWhiteSpace(baseline) &&
                !string.Equals(sourceModel, baseline, StringComparison.OrdinalIgnoreCase))
            {
                return baseline;
            }
        }

        return sourceModel;
    }

    private int GetCheapBatchWorkers()
    {
        return Math.Clamp(_settings.CheapBatchWorkers, 1, 12);
    }

    private bool IsExpensivePassEnabled()
    {
        return _settings.MaxExpensivePerBatch > 0;
    }

    private int GetFetchLimit()
    {
        var batchSize = Math.Max(1, _settings.BatchSize);
        return batchSize * GetCheapBatchWorkers();
    }

    private static bool TryGetCheapSkipReason(Message message, out string reason)
    {
        var semanticContent = MessageContentBuilder.BuildSemanticContent(message);
        if (string.IsNullOrWhiteSpace(semanticContent))
        {
            reason = "empty_semantic_content";
            return true;
        }

        var text = message.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(text) &&
            (text.Equals("[DELETED]", StringComparison.OrdinalIgnoreCase)
             || ServicePlaceholderRegex.IsMatch(text)))
        {
            reason = "service_placeholder";
            return true;
        }

        reason = string.Empty;
        return false;
    }

    private sealed record CheapPassResult(HashSet<long> FailedMessageIds, bool BalanceIssueDetected);
    private sealed record CheapChunkRequest(string Model, List<AnalysisInputMessage> Messages);
}
