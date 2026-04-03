using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.OpenRouter;

namespace TgAssistant.Intelligence.Stage5;

public class OpenRouterAnalysisService
{
    private const int CheapTransientRetryAttempts = 2;
    private const int CheapTransientRetryBaseDelayMs = 600;
    private const int ErrorBodySnippetLimit = 260;
    private const int SchemaFailureSnippetLimit = 420;

    private readonly AnalysisSettings _analysis;
    private readonly ExtractionSchemaValidator _schemaValidator;
    private readonly IAnalysisUsageRepository _usageRepository;
    private readonly IBudgetGuardrailService _budgetGuardrailService;
    private readonly ILlmContractNormalizer _contractNormalizer;
    private readonly ILlmGateway _gateway;
    private readonly ILogger<OpenRouterAnalysisService> _logger;

    public OpenRouterAnalysisService(
        HttpClient http,
        IOptions<AnalysisSettings> analysis,
        ExtractionSchemaValidator schemaValidator,
        IAnalysisUsageRepository usageRepository,
        IBudgetGuardrailService budgetGuardrailService,
        ILlmContractNormalizer contractNormalizer,
        ILlmGateway gateway,
        ILogger<OpenRouterAnalysisService> logger)
    {
        _ = http;
        _analysis = analysis.Value;
        _schemaValidator = schemaValidator;
        _usageRepository = usageRepository;
        _budgetGuardrailService = budgetGuardrailService;
        _contractNormalizer = contractNormalizer;
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<ExtractionBatchResult> ExtractCheapAsync(
        string model,
        string systemPrompt,
        List<AnalysisInputMessage> batch,
        CancellationToken ct,
        string? chunkSummaryPrev = null,
        string? replySliceContext = null,
        string? ragContext = null)
    {
        var user = MessageContentBuilder.BuildCheapBatchPrompt(batch, chunkSummaryPrev, replySliceContext, ragContext);
        var req = BuildRequest(
            model,
            systemPrompt,
            user,
            NormalizeMaxTokens(_analysis.CheapMaxTokens, 300, 8000),
            0.0f,
            phase: "cheap");
        var json = await SendAndExtractJsonAsync(req, "cheap", ct);
        return ParseBatch(json);
    }

    public async Task<bool> ProbeCheapAvailabilityAsync(CancellationToken ct)
    {
        var model = string.IsNullOrWhiteSpace(_analysis.CheapModel)
            ? "openai/gpt-4o-mini"
            : _analysis.CheapModel.Trim();
        var req = BuildRequest(
            model,
            "Return only valid JSON object: {\"items\":[]}",
            "ping",
            32,
            0.0f,
            phase: "cheap");

        try
        {
            var json = await SendAndExtractJsonAsync(req, "cheap_probe", ct);
            return !string.IsNullOrWhiteSpace(json);
        }
        catch (OpenRouterBalanceException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException)
        {
            return false;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    public async Task<ExtractionItem?> ResolveExpensiveAsync(
        string model,
        string systemPrompt,
        ExtractionItem candidate,
        List<string> currentFacts,
        string messageText,
        AnalysisMessageContext? context,
        CancellationToken ct)
    {
        var user = JsonSerializer.Serialize(
            new
            {
                message_text = messageText,
                context = new
                {
                    local_burst = context?.LocalBurst ?? [],
                    session_start = context?.SessionStart ?? [],
                    historical = context?.HistoricalSummaries ?? []
                },
                candidate,
                current_facts = currentFacts
            },
            JsonOptions);
        var req = BuildRequest(
            model,
            systemPrompt,
            user,
            NormalizeMaxTokens(_analysis.ExpensiveMaxTokens, 500, 12000),
            0.0f,
            phase: "expensive");
        var json = await SendAndExtractJsonAsync(req, "expensive", ct);
        var parsed = ParseBatch(json);
        return parsed.Items.FirstOrDefault();
    }

    public async Task<string> SummarizeDialogAsync(
        string model,
        string systemPrompt,
        long chatId,
        string scope,
        DateTime periodStart,
        DateTime periodEnd,
        List<Message> messages,
        IReadOnlyCollection<SummaryHistoricalHint>? historicalHints,
        IReadOnlyDictionary<long, string>? cheapJsonByMessageId,
        CancellationToken ct)
    {
        var user = MessageContentBuilder.BuildSummaryPrompt(
            chatId,
            scope,
            periodStart,
            periodEnd,
            messages,
            historicalHints,
            cheapJsonByMessageId);
        var req = BuildRequest(
            model,
            systemPrompt,
            user,
            NormalizeMaxTokens(_analysis.SummaryMaxTokens, 256, 8000),
            0.0f,
            phase: "summary");
        var rawSummary = await SendAndExtractJsonAsync(req, "summary", ct);
        if (!_analysis.SummaryContractNormalizationEnabled)
        {
            return rawSummary;
        }

        try
        {
            var normalization = await _contractNormalizer.NormalizeAsync(
                new LlmContractNormalizationRequest
                {
                    ContractKind = LlmContractKind.SessionSummaryV1,
                    RawReasoningPayload = rawSummary,
                    TaskKey = "stage5_summary_contract_normalization",
                    Trace = new LlmTraceContext
                    {
                        PathKey = "stage5:summary:contract_normalization",
                        ScopeTags = ["stage5", "summary", "contract_normalization"]
                    },
                    Limits = new LlmExecutionLimits
                    {
                        MaxTokens = Math.Clamp(_analysis.SummaryMaxTokens, 128, 1200),
                        Temperature = 0f,
                        TimeoutMs = Math.Max(1000, _analysis.HttpTimeoutSeconds * 1000)
                    }
                },
                ct);

            if (normalization.Status == LlmContractNormalizationStatus.Success
                && !string.IsNullOrWhiteSpace(normalization.NormalizedPayloadJson))
            {
                _logger.LogInformation(
                    "Stage5 summary contract normalization applied. status={Status}, schema_ref={SchemaRef}, provider={Provider}, model={Model}, fallback_attempt={FallbackAttempt}",
                    normalization.Status,
                    normalization.SchemaRef,
                    normalization.ProviderMetadata?.Provider ?? "n/a",
                    normalization.ProviderMetadata?.Model ?? "n/a",
                    normalization.ProviderMetadata?.FallbackAttempt ?? false);
                return normalization.NormalizedPayloadJson;
            }

            _logger.LogWarning(
                "Stage5 summary contract normalization fallback to raw summary. status={Status}, failure_reason={FailureReason}, validation_errors={ValidationErrors}",
                normalization.Status,
                normalization.Diagnostics.FailureReason ?? "n/a",
                string.Join(" | ", normalization.Diagnostics.ValidationErrors));
            return rawSummary;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Stage5 summary contract normalization failed unexpectedly; fallback to raw summary.");
            return rawSummary;
        }
    }

    public async Task<string> CompleteTextAsync(
        string model,
        string systemPrompt,
        string userPrompt,
        int maxTokens,
        string phase,
        CancellationToken ct)
    {
        var req = new OpenRouterRequest
        {
            Model = model,
            Messages =
            [
                new OpenRouterMessage { Role = "system", Content = systemPrompt },
                new OpenRouterMessage { Role = "user", Content = userPrompt }
            ],
            MaxTokens = NormalizeMaxTokens(maxTokens, 64, 4000),
            Temperature = 0.2f
        };

        return await SendAndExtractTextAsync(req, phase, ct);
    }

    public async Task<OpenRouterResponseMessage> CompleteChatWithToolsAsync(
        string model,
        List<OpenRouterMessage> messages,
        List<OpenRouterTool>? tools,
        int maxTokens,
        CancellationToken ct)
    {
        var req = new OpenRouterRequest
        {
            Model = model,
            Messages = messages,
            Tools = tools?.Count > 0 ? tools : null,
            ToolChoice = tools?.Count > 0 ? "auto" : null,
            MaxTokens = NormalizeMaxTokens(maxTokens, 64, 4000),
            Temperature = 0.2f
        };

        var response = await SendAsync(req, "chat", ct);
        return response?.Choices?.FirstOrDefault()?.Message ?? new OpenRouterResponseMessage();
    }

    private OpenRouterRequest BuildRequest(string model, string systemPrompt, string userPrompt, int maxTokens, float temperature, string phase)
    {
        return new OpenRouterRequest
        {
            Model = model,
            Messages =
            [
                new OpenRouterMessage { Role = "system", Content = systemPrompt },
                new OpenRouterMessage { Role = "user", Content = userPrompt }
            ],
            ResponseFormat = new OpenRouterResponseFormat { Type = "json_object" },
            Provider = BuildProviderPreferences(phase),
            MaxTokens = maxTokens,
            Temperature = temperature
        };
    }

    private OpenRouterProviderPreferences? BuildProviderPreferences(string phase)
    {
        if (!string.Equals(phase, "cheap", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(_analysis.CheapProviderOrder))
        {
            return null;
        }

        var order = _analysis.CheapProviderOrder
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (order.Count == 0)
        {
            return null;
        }

        return new OpenRouterProviderPreferences
        {
            Order = order,
            AllowFallbacks = _analysis.CheapProviderAllowFallbacks
        };
    }

    private async Task<string> SendAndExtractJsonAsync(OpenRouterRequest request, string phase, CancellationToken ct)
    {
        var parsed = await SendAsync(request, phase, ct);
        var content = ExtractMessageContent(parsed?.Choices?.FirstOrDefault()?.Message?.Content);
        if (string.IsNullOrWhiteSpace(content))
        {
            return phase == "summary" ? "{\"summary\":\"\"}" : "{\"items\":[]}";
        }

        _logger.LogDebug("Analysis model response ({Model}) len={Len}", request.Model, content.Length);
        return content;
    }

    private async Task<string> SendAndExtractTextAsync(OpenRouterRequest request, string phase, CancellationToken ct)
    {
        var parsed = await SendAsync(request, phase, ct);
        var content = ExtractMessageContent(parsed?.Choices?.FirstOrDefault()?.Message?.Content);
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        _logger.LogDebug("Text completion response ({Model}) len={Len}", request.Model, content.Length);
        return content.Trim();
    }

    private async Task<OpenRouterResponse?> SendAsync(OpenRouterRequest request, string phase, CancellationToken ct)
    {
        var budgetDecision = await _budgetGuardrailService.EvaluatePathAsync(
            BuildBudgetPathRequest(phase),
            ct);
        if (budgetDecision.ShouldPausePath || budgetDecision.ShouldDegradeOptionalPath)
        {
            throw new InvalidOperationException(
                $"Budget guardrail blocked phase '{phase}' with state '{budgetDecision.State}' ({budgetDecision.Reason}).");
        }

        var maxAttempts = string.Equals(phase, "cheap", StringComparison.OrdinalIgnoreCase)
            ? 1 + CheapTransientRetryAttempts
            : 1;

        Exception? lastError = null;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var gatewayResponse = await _gateway.ExecuteAsync(BuildGatewayRequest(request, phase), ct);
                await LogUsageAsync(phase, gatewayResponse.Model, gatewayResponse.Usage, gatewayResponse.LatencyMs, ct);
                return BuildOpenRouterResponse(gatewayResponse);
            }
            catch (LlmGatewayException ex)
            {
                var mapped = await MapGatewayExceptionAsync(ex, phase, request.Model, ct);
                if (attempt < maxAttempts && IsTransientCheapException(phase, mapped) && !ct.IsCancellationRequested)
                {
                    lastError = mapped;
                    await DelayBeforeRetryAsync(phase, request.Model, attempt, maxAttempts, mapped.GetType().Name, ct);
                    continue;
                }

                throw mapped;
            }
            catch (Exception ex) when (attempt < maxAttempts && IsTransientCheapException(phase, ex) && !ct.IsCancellationRequested)
            {
                lastError = ex;
                await DelayBeforeRetryAsync(phase, request.Model, attempt, maxAttempts, ex.GetType().Name, ct);
            }
        }

        if (lastError != null)
        {
            throw lastError;
        }

        return null;
    }

    private static bool IsTransientCheapException(string phase, Exception ex)
    {
        if (!string.Equals(phase, "cheap", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (ex is OpenRouterBalanceException)
        {
            return false;
        }

        return ex is HttpRequestException or TaskCanceledException or TimeoutException;
    }

    private async Task DelayBeforeRetryAsync(string phase, string model, int attempt, int maxAttempts, string reason, CancellationToken ct)
    {
        var jitterMs = Random.Shared.Next(0, 250);
        var delayMs = (int)(CheapTransientRetryBaseDelayMs * Math.Pow(2, attempt - 1)) + jitterMs;
        _logger.LogWarning(
            "OpenRouter transient failure; retrying phase={Phase}, model={Model}, attempt={Attempt}/{MaxAttempts}, reason={Reason}, delay_ms={DelayMs}",
            phase,
            model,
            attempt,
            maxAttempts,
            reason,
            delayMs);
        await Task.Delay(TimeSpan.FromMilliseconds(delayMs), ct);
    }

    private LlmGatewayRequest BuildGatewayRequest(OpenRouterRequest request, string phase)
    {
        var responseMode = request.Tools?.Count > 0
            ? LlmResponseMode.ToolCalls
            : string.Equals(request.ResponseFormat?.Type, "json_object", StringComparison.OrdinalIgnoreCase)
                ? LlmResponseMode.JsonObject
                : LlmResponseMode.Text;

        return new LlmGatewayRequest
        {
            Modality = request.Tools?.Count > 0 ? LlmModality.Tools : LlmModality.TextChat,
            TaskKey = string.IsNullOrWhiteSpace(phase) ? "stage5_text" : phase.Trim(),
            ResponseMode = responseMode,
            Limits = new LlmExecutionLimits
            {
                MaxTokens = request.MaxTokens,
                Temperature = request.Temperature,
                TimeoutMs = Math.Max(1, _analysis.HttpTimeoutSeconds) * 1000
            },
            Trace = new LlmTraceContext
            {
                PathKey = ResolveBudgetPathKey(phase),
                IsImportScope = phase.StartsWith("import_", StringComparison.OrdinalIgnoreCase),
                IsOptionalPath = IsBudgetOptionalPath(phase),
                ScopeTags =
                [
                    "stage5",
                    string.IsNullOrWhiteSpace(phase) ? "text" : phase.Trim()
                ]
            },
            Messages = request.Messages.Select(ConvertMessage).ToList(),
            ToolDefinitions = request.Tools?.Select(ConvertToolDefinition).ToList() ?? new List<LlmToolDefinition>()
        };
    }

    private static LlmGatewayMessage ConvertMessage(OpenRouterMessage message)
    {
        var converted = new LlmGatewayMessage
        {
            Role = message.Role.Trim().ToLowerInvariant() switch
            {
                "system" => LlmMessageRole.System,
                "assistant" => LlmMessageRole.Assistant,
                "tool" => LlmMessageRole.Tool,
                _ => LlmMessageRole.User
            },
            Name = message.Name,
            ToolCallId = message.ToolCallId,
            ToolCalls = message.ToolCalls?.Select(ConvertToolCall).ToList() ?? new List<LlmToolCall>()
        };

        var text = ExtractMessageContent(message.Content);
        if (!string.IsNullOrWhiteSpace(text))
        {
            converted.ContentParts.Add(LlmMessageContentPart.FromText(text));
        }

        return converted;
    }

    private static LlmToolDefinition ConvertToolDefinition(OpenRouterTool tool)
    {
        return new LlmToolDefinition
        {
            Name = tool.Function.Name,
            Description = tool.Function.Description,
            ParametersJsonSchema = SerializeLooseJson(tool.Function.Parameters)
        };
    }

    private static LlmToolCall ConvertToolCall(OpenRouterToolCall toolCall)
    {
        return new LlmToolCall
        {
            Id = toolCall.Id,
            Name = toolCall.Function.Name,
            ArgumentsJson = string.IsNullOrWhiteSpace(toolCall.Function.Arguments)
                ? "{}"
                : toolCall.Function.Arguments
        };
    }

    private static OpenRouterResponse BuildOpenRouterResponse(LlmGatewayResponse response)
    {
        return new OpenRouterResponse
        {
            Choices =
            [
                new OpenRouterChoice
                {
                    Message = new OpenRouterResponseMessage
                    {
                        Role = "assistant",
                        Content = response.Output.StructuredPayloadJson ?? response.Output.Text ?? string.Empty,
                        ToolCalls = response.Output.ToolCalls.Select(ConvertOpenRouterToolCall).ToList()
                    }
                }
            ],
            Usage = new OpenRouterUsage
            {
                PromptTokens = response.Usage.PromptTokens,
                CompletionTokens = response.Usage.CompletionTokens,
                TotalTokens = response.Usage.TotalTokens,
                Cost = response.Usage.CostUsd
            }
        };
    }

    private static OpenRouterToolCall ConvertOpenRouterToolCall(LlmToolCall toolCall)
    {
        return new OpenRouterToolCall
        {
            Id = toolCall.Id,
            Function = new OpenRouterToolCallFunction
            {
                Name = toolCall.Name,
                Arguments = toolCall.ArgumentsJson
            }
        };
    }

    private async Task<Exception> MapGatewayExceptionAsync(
        LlmGatewayException ex,
        string phase,
        string model,
        CancellationToken ct)
    {
        if (ex.Category == LlmGatewayErrorCategory.Quota)
        {
            await _budgetGuardrailService.RegisterQuotaBlockedAsync(
                pathKey: ResolveBudgetPathKey(phase),
                modality: ResolveBudgetModality(phase),
                reason: "quota_like_provider_failure",
                isImportScope: phase.StartsWith("import_", StringComparison.OrdinalIgnoreCase),
                isOptionalPath: IsBudgetOptionalPath(phase),
                ct: ct);

            var statusCode = ex.HttpStatus ?? HttpStatusCode.PaymentRequired;
            _logger.LogWarning(
                "Gateway quota issue detected. phase={Phase}, model={Model}, status={StatusCode}, provider={Provider}",
                phase,
                model,
                (int)statusCode,
                ex.Provider ?? "n/a");
            return new OpenRouterBalanceException(
                $"Gateway quota issue status={(int)statusCode}; provider={ex.Provider ?? "unknown"}",
                statusCode);
        }

        var status = ex.HttpStatus is null ? "n/a" : ((int)ex.HttpStatus.Value).ToString();
        _logger.LogWarning(
            ex,
            "Gateway text request failed. phase={Phase}, model={Model}, provider={Provider}, category={Category}, retryable={Retryable}, status={StatusCode}",
            phase,
            model,
            ex.Provider ?? "n/a",
            ex.Category,
            ex.Retryable,
            status);

        return ex.Category switch
        {
            LlmGatewayErrorCategory.Timeout => new TaskCanceledException(
                $"Gateway timeout provider={ex.Provider ?? "unknown"} status={status}",
                ex),
            LlmGatewayErrorCategory.SchemaMismatch => new InvalidDataException(
                $"gateway_schema_validation_failed:provider={ex.Provider ?? "unknown"};status={status}",
                ex),
            _ => new HttpRequestException(
                $"Gateway provider error provider={ex.Provider ?? "unknown"} category={ex.Category} status={status}; {ex.Message}",
                ex,
                ex.HttpStatus)
        };
    }

    private static string SerializeLooseJson(object? value)
    {
        if (value is null)
        {
            return "{}";
        }

        return value switch
        {
            string text when !string.IsNullOrWhiteSpace(text) => text,
            JsonElement element => element.GetRawText(),
            _ => JsonSerializer.Serialize(value, JsonOptions)
        };
    }

    private static string ExtractMessageContent(object? content)
    {
        if (content == null)
        {
            return string.Empty;
        }

        if (content is string text)
        {
            return text;
        }

        if (content is JsonElement element)
        {
            return ExtractMessageContentFromJsonElement(element);
        }

        return content.ToString() ?? string.Empty;
    }

    private static string ExtractMessageContentFromJsonElement(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            return element.GetString() ?? string.Empty;
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            var parts = element.EnumerateArray()
                .Select(part =>
                {
                    if (part.ValueKind == JsonValueKind.String)
                    {
                        return part.GetString() ?? string.Empty;
                    }

                    if (part.ValueKind == JsonValueKind.Object &&
                        part.TryGetProperty("text", out var textNode) &&
                        textNode.ValueKind == JsonValueKind.String)
                    {
                        return textNode.GetString() ?? string.Empty;
                    }

                    return string.Empty;
                })
                .Where(part => !string.IsNullOrWhiteSpace(part));

            return string.Join('\n', parts);
        }

        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty("text", out var textProperty) &&
            textProperty.ValueKind == JsonValueKind.String)
        {
            return textProperty.GetString() ?? string.Empty;
        }

        return element.ToString();
    }

    private async Task LogUsageAsync(string phase, string model, LlmUsageInfo? usage, int? latencyMs, CancellationToken ct)
    {
        if (usage == null)
        {
            return;
        }

        await _usageRepository.LogAsync(new AnalysisUsageEvent
        {
            Phase = phase,
            Model = model,
            PromptTokens = usage.PromptTokens ?? 0,
            CompletionTokens = usage.CompletionTokens ?? 0,
            TotalTokens = usage.TotalTokens ?? 0,
            CostUsd = usage.CostUsd ?? 0m,
            LatencyMs = latencyMs.HasValue ? Math.Max(0, latencyMs.Value) : null,
            CreatedAt = DateTime.UtcNow
        }, ct);
    }

    private static string TruncateForLog(string? value, int maxLen)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = MessageContentBuilder.CollapseWhitespace(value);
        return normalized.Length <= maxLen
            ? normalized
            : normalized[..maxLen] + "...";
    }

    private ExtractionBatchResult ParseBatch(string json)
    {
        if (!_schemaValidator.TryParseBatch(json, out var parsed, out var error))
        {
            var reason = error ?? "invalid_schema";
            var snippet = TruncateForLog(json, SchemaFailureSnippetLimit);
            _logger.LogWarning(
                "Stage5 schema validation failed: reason={Reason}, response_snippet={Snippet}",
                reason,
                snippet);
            throw new InvalidDataException(
                $"stage5_schema_validation_failed:{reason};response_snippet={snippet}");
        }

        return parsed;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static int NormalizeMaxTokens(int requested, int min, int max)
    {
        return Math.Max(min, Math.Min(max, requested));
    }

    private static BudgetPathCheckRequest BuildBudgetPathRequest(string phase)
    {
        return new BudgetPathCheckRequest
        {
            PathKey = ResolveBudgetPathKey(phase),
            Modality = ResolveBudgetModality(phase),
            IsImportScope = phase.StartsWith("import_", StringComparison.OrdinalIgnoreCase),
            IsOptionalPath = IsBudgetOptionalPath(phase)
        };
    }

    private static string ResolveBudgetPathKey(string phase)
    {
        return phase switch
        {
            "cheap" => "stage5_cheap",
            "cheap_probe" => "stage5_cheap_probe",
            "expensive" => "stage5_expensive",
            "summary" => "stage5_summary",
            "edit_diff" => "stage5_edit_diff",
            "daily_crystallization" => "stage5_daily_crystallization",
            _ => $"stage5_{phase}"
        };
    }

    private static string ResolveBudgetModality(string phase)
    {
        return phase switch
        {
            "embedding" => BudgetModalities.Embeddings,
            "vision" or "import_vision" => BudgetModalities.Vision,
            "audio_transcription" or "audio_paralinguistics" or "import_audio_transcription" or "import_audio_paralinguistics" => BudgetModalities.Audio,
            _ => BudgetModalities.TextAnalysis
        };
    }

    private static bool IsBudgetOptionalPath(string phase)
    {
        return phase is "expensive" or "summary" or "edit_diff" or "daily_crystallization";
    }
}
