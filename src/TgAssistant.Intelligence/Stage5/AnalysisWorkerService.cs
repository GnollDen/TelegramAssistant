using System.Text.Json;
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
    private static readonly TimeSpan BatchThrottleDelay = TimeSpan.FromMilliseconds(25);

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
                var expensiveResolved = await _expensivePassResolver.ProcessExpensiveBacklogAsync(
                    async ct => (await GetPromptAsync(ExpensivePromptId, DefaultExpensivePrompt, ct)).SystemPrompt,
                    stoppingToken);
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

    private async Task ProcessCheapBatchAsync(List<Message> messages, CancellationToken ct)
    {
        var replyContext = await _messageContentBuilder.LoadReplyContextAsync(messages, ct);
        var batch = messages.Select(m => new AnalysisInputMessage
        {
            MessageId = m.Id,
            SenderName = m.SenderName,
            Timestamp = m.Timestamp,
            Text = MessageContentBuilder.BuildMessageText(m, replyContext.GetValueOrDefault(m.Id))
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
                        payload: $"model={modelByMessageId.GetValueOrDefault(message.Id, _settings.CheapModel)};json={JsonSerializer.Serialize(extracted)}",
                        ct: ct);

                    extracted = new ExtractionItem { MessageId = message.Id };
                }

                var needsExpensive = ExtractionRefiner.ShouldRunExpensivePass(message, extracted, _settings);

                await _extractionRepository.UpsertCheapAsync(message.Id, JsonSerializer.Serialize(extracted), needsExpensive, ct);

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
}
