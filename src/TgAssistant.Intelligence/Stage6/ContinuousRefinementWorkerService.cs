using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Intelligence.Stage5;

namespace TgAssistant.Intelligence.Stage6;

/// <summary>
/// Slowly reprocesses previously extracted messages to improve quality with the latest cheap prompt.
/// </summary>
[Obsolete("Deprecated by Stage 5 v10 Hybrid manifesto. Keep disabled and do not register in DI.")]
public class ContinuousRefinementWorkerService : BackgroundService
{
    private const string CheapPromptId = "stage5_cheap_extract_v7";
    private const string CursorKey = "stage6:continuous_refinement:cursor";
    private const string FallbackCheapPrompt = """
Return only valid JSON object {"items":[...]} and produce one extraction item per input message_id.
Use grounded facts only and keep empty arrays for low-value chatter.
""";

    private readonly ContinuousRefinementSettings _settings;
    private readonly AnalysisSettings _analysisSettings;
    private readonly IMessageExtractionRepository _messageExtractionRepository;
    private readonly IPromptTemplateRepository _promptTemplateRepository;
    private readonly IAnalysisStateRepository _stateRepository;
    private readonly OpenRouterAnalysisService _analysisService;
    private readonly MessageContentBuilder _messageContentBuilder;
    private readonly AnalysisContextBuilder _contextBuilder;
    private readonly ExtractionApplier _extractionApplier;
    private readonly IExtractionErrorRepository _extractionErrorRepository;
    private readonly ILogger<ContinuousRefinementWorkerService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ContinuousRefinementWorkerService"/> class.
    /// </summary>
    public ContinuousRefinementWorkerService(
        IOptions<ContinuousRefinementSettings> settings,
        IOptions<AnalysisSettings> analysisSettings,
        IMessageExtractionRepository messageExtractionRepository,
        IPromptTemplateRepository promptTemplateRepository,
        IAnalysisStateRepository stateRepository,
        OpenRouterAnalysisService analysisService,
        MessageContentBuilder messageContentBuilder,
        AnalysisContextBuilder contextBuilder,
        ExtractionApplier extractionApplier,
        IExtractionErrorRepository extractionErrorRepository,
        ILogger<ContinuousRefinementWorkerService> logger)
    {
        _settings = settings.Value;
        _analysisSettings = analysisSettings.Value;
        _messageExtractionRepository = messageExtractionRepository;
        _promptTemplateRepository = promptTemplateRepository;
        _stateRepository = stateRepository;
        _analysisService = analysisService;
        _messageContentBuilder = messageContentBuilder;
        _contextBuilder = contextBuilder;
        _extractionApplier = extractionApplier;
        _extractionErrorRepository = extractionErrorRepository;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled)
        {
            _logger.LogInformation("Continuous refinement worker is disabled");
            return;
        }

        var minDelaySeconds = Math.Max(45, _settings.MinDelaySeconds);
        var maxDelaySeconds = Math.Max(minDelaySeconds, _settings.MaxDelaySeconds);
        _logger.LogInformation(
            "Continuous refinement worker started. batch_size={BatchSize}, delay_range_s={MinDelaySeconds}-{MaxDelaySeconds}, min_message_length={MinLength}, stale_after_h={StaleAfterHours}",
            Math.Max(1, _settings.BatchSize),
            minDelaySeconds,
            maxDelaySeconds,
            Math.Max(1, _settings.MinMessageLength),
            Math.Max(1, _settings.StaleAfterHours));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var cheapPrompt = await GetCheapPromptAsync(stoppingToken);
                var cursor = await _stateRepository.GetWatermarkAsync(CursorKey, stoppingToken);
                var candidates = await _messageExtractionRepository.GetRefinementCandidatesAsync(
                    afterExtractionId: cursor,
                    limit: Math.Max(1, _settings.BatchSize),
                    minMessageLength: Math.Max(1, _settings.MinMessageLength),
                    staleAfterHours: Math.Max(1, _settings.StaleAfterHours),
                    cheapPromptUpdatedAtUtc: cheapPrompt.IsFallback
                        ? DateTime.MinValue
                        : (cheapPrompt.UpdatedAt == default ? DateTime.UtcNow : cheapPrompt.UpdatedAt),
                    lowConfidenceThreshold: _settings.LowConfidenceThreshold,
                    ct: stoppingToken);
                if (candidates.Count == 0)
                {
                    if (cursor > 0)
                    {
                        await _stateRepository.ResetWatermarksIfExistAsync([CursorKey], stoppingToken);
                        continue;
                    }

                    await Task.Delay(TimeSpan.FromSeconds(Math.Max(30, _settings.PollIntervalSeconds)), stoppingToken);
                    continue;
                }

                foreach (var candidate in candidates)
                {
                    var startedAt = DateTime.UtcNow;
                    var processedSuccessfully = false;
                    try
                    {
                        processedSuccessfully = await ProcessCandidateAsync(candidate, cheapPrompt.SystemPrompt, stoppingToken);
                        if (processedSuccessfully)
                        {
                            await _stateRepository.SetWatermarkAsync(CursorKey, candidate.ExtractionId, stoppingToken);
                        }
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(
                            ex,
                            "Continuous refinement failed for message_id={MessageId}",
                            candidate.MessageId);
                        await _extractionErrorRepository.LogAsync(
                            stage: "stage6_continuous_refine_item",
                            reason: ex.Message,
                            messageId: candidate.MessageId,
                            payload: ex.GetType().Name,
                            ct: stoppingToken);
                    }
                    finally
                    {
                        await DelayBetweenItemsAsync(startedAt, minDelaySeconds, maxDelaySeconds, stoppingToken);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Continuous refinement loop failed");
                await _extractionErrorRepository.LogAsync(
                    stage: "stage6_continuous_refine_loop",
                    reason: ex.Message,
                    payload: ex.GetType().Name,
                    ct: stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(30, _settings.PollIntervalSeconds)), stoppingToken);
            }
        }
    }

    private async Task<bool> ProcessCandidateAsync(
        RefinementCandidate candidate,
        string cheapPrompt,
        CancellationToken ct)
    {
        var sourceMessage = candidate.Message;
        var replyContext = await _messageContentBuilder.LoadReplyMessageAsync(sourceMessage, ct);
        var contexts = await _contextBuilder.BuildBatchContextsAsync([sourceMessage], ct);
        var context = contexts.GetValueOrDefault(sourceMessage.Id);

        var requestMessage = new AnalysisInputMessage
        {
            MessageId = sourceMessage.Id,
            SenderName = sourceMessage.SenderName,
            Timestamp = sourceMessage.Timestamp,
            Text = MessageContentBuilder.BuildMessageText(sourceMessage, replyContext, context)
        };
        var model = ResolveCheapModel();
        var extractedBatch = await _analysisService.ExtractCheapAsync(
            model,
            cheapPrompt,
            [requestMessage],
            ct);

        var extracted = extractedBatch.Items.FirstOrDefault(x => x.MessageId == sourceMessage.Id)
                        ?? new ExtractionItem { MessageId = sourceMessage.Id };
        extracted = ExtractionRefiner.NormalizeExtractionForMessage(extracted, sourceMessage);
        extracted = ExtractionRefiner.SanitizeExtraction(extracted);
        extracted = ExtractionRefiner.RefineExtractionForMessage(extracted, sourceMessage, _analysisSettings);
        extracted.MessageId = sourceMessage.Id;

        if (!ExtractionValidator.ValidateExtractionForMessage(extracted, sourceMessage, out var validationError))
        {
            _logger.LogWarning(
                "Continuous refinement validation failed for message_id={MessageId}: {Reason}",
                sourceMessage.Id,
                validationError ?? "invalid_extraction");
            await _extractionErrorRepository.LogAsync(
                stage: "stage6_continuous_refine_validation",
                reason: validationError ?? "invalid_extraction",
                messageId: sourceMessage.Id,
                payload: JsonSerializer.Serialize(extracted, ExtractionSerializationOptions.SnakeCase),
                ct: ct);
            return false;
        }

        await _messageExtractionRepository.UpsertCheapAsync(
            sourceMessage.Id,
            JsonSerializer.Serialize(extracted, ExtractionSerializationOptions.SnakeCase),
            needsExpensive: false,
            ct);

        await _extractionApplier.ApplyIntelligenceOnlyAsync(sourceMessage.Id, extracted, sourceMessage, ct);
        _logger.LogInformation(
            "Continuous refinement updated message_id={MessageId}, claims={ClaimsCount}",
            sourceMessage.Id,
            extracted.Claims.Count);

        return true;
    }

    private async Task<ResolvedPrompt> GetCheapPromptAsync(CancellationToken ct)
    {
        var prompt = await _promptTemplateRepository.GetByIdAsync(CheapPromptId, ct);
        if (prompt == null || string.IsNullOrWhiteSpace(prompt.SystemPrompt))
        {
            return new ResolvedPrompt
            {
                SystemPrompt = FallbackCheapPrompt,
                UpdatedAt = DateTime.UtcNow,
                IsFallback = true
            };
        }

        return new ResolvedPrompt
        {
            SystemPrompt = prompt.SystemPrompt,
            UpdatedAt = prompt.UpdatedAt,
            IsFallback = false
        };
    }

    private static async Task DelayBetweenItemsAsync(
        DateTime startedAt,
        int minDelaySeconds,
        int maxDelaySeconds,
        CancellationToken ct)
    {
        var targetSeconds = Random.Shared.Next(minDelaySeconds, maxDelaySeconds + 1);
        var targetDelay = TimeSpan.FromSeconds(targetSeconds);
        var elapsed = DateTime.UtcNow - startedAt;
        var remaining = targetDelay - elapsed;
        if (remaining > TimeSpan.Zero)
        {
            await Task.Delay(remaining, ct);
        }
    }

    private string ResolveCheapModel()
    {
        if (!string.IsNullOrWhiteSpace(_analysisSettings.CheapModel))
        {
            return _analysisSettings.CheapModel.Trim();
        }

        if (!string.IsNullOrWhiteSpace(_analysisSettings.CheapBaselineModel))
        {
            return _analysisSettings.CheapBaselineModel.Trim();
        }

        return "openai/gpt-4o-mini";
    }

    private sealed class ResolvedPrompt
    {
        public string SystemPrompt { get; set; } = string.Empty;
        public DateTime UpdatedAt { get; set; }
        public bool IsFallback { get; set; }
    }
}
