// Legacy Stage6 surface retained for reference and diagnostics only. Not part of the PRD target architecture.

using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Intelligence.Stage5;

namespace TgAssistant.Intelligence.Stage6;

public class BotChatService : IBotChatService
{
    private const int DefaultFactLimit = 10;
    private const int SessionSummaryPromptLimit = 3;
    private const int SessionSummaryCharsLimit = 260;
    private const int PromptLocalWindowLinesLimit = 12;
    private const int PromptFallbackMessagesLimit = 12;
    private const int PromptMessageCharsLimit = 220;
    private const int ReplyMaxTokens = 4000;
    private const int MaxToolCallsPerTurn = 3;
    private const int MaxToolResultChars = 8000;
    private const int DefaultDossierLimit = 40;
    private const int Stage6ScopeCandidatesLimit = 20;
    private const int Stage6ContextTraitsLimit = 3;
    private const int Stage6ContextPeriodsLimit = 3;
    private const int Stage6SectionCharsLimit = 420;
    private const int Stage6ProfileSenderSampleLimit = 1200;
    private const string SessionSummaryCheckpointPrefix = "stage5:summary:session";
    private readonly ITextEmbeddingGenerator _embeddingGenerator;
    private readonly IMessageRepository _messageRepository;
    private readonly IChatSessionRepository _chatSessionRepository;
    private readonly IAnalysisStateRepository _analysisStateRepository;
    private readonly IEntityRepository _entityRepository;
    private readonly IFactRepository _factRepository;
    private readonly IRelationshipRepository _relationshipRepository;
    private readonly ICommunicationEventRepository _communicationEventRepository;
    private readonly IBotCommandService _botCommandService;
    private readonly IStateProfileRepository _stateProfileRepository;
    private readonly IStrategyDraftRepository _strategyDraftRepository;
    private readonly IPeriodRepository _periodRepository;
    private readonly IStage6ArtifactRepository _stage6ArtifactRepository;
    private readonly IStage6ArtifactFreshnessService _stage6ArtifactFreshnessService;
    private readonly IStage6CaseRepository _stage6CaseRepository;
    private readonly ILlmGateway _llmGateway;
    private readonly EmbeddingSettings _embeddingSettings;
    private readonly AnalysisSettings _analysisSettings;
    private readonly BotChatSettings _botChatSettings;
    private readonly TelegramSettings _telegramSettings;
    private readonly ILogger<BotChatService> _logger;

    public BotChatService(
        ITextEmbeddingGenerator embeddingGenerator,
        IMessageRepository messageRepository,
        IChatSessionRepository chatSessionRepository,
        IAnalysisStateRepository analysisStateRepository,
        IEntityRepository entityRepository,
        IFactRepository factRepository,
        IRelationshipRepository relationshipRepository,
        ICommunicationEventRepository communicationEventRepository,
        IBotCommandService botCommandService,
        IStateProfileRepository stateProfileRepository,
        IStrategyDraftRepository strategyDraftRepository,
        IPeriodRepository periodRepository,
        IStage6ArtifactRepository stage6ArtifactRepository,
        IStage6ArtifactFreshnessService stage6ArtifactFreshnessService,
        IStage6CaseRepository stage6CaseRepository,
        ILlmGateway llmGateway,
        IOptions<EmbeddingSettings> embeddingSettings,
        IOptions<AnalysisSettings> analysisSettings,
        IOptions<BotChatSettings> botChatSettings,
        IOptions<TelegramSettings> telegramSettings,
        ILogger<BotChatService> logger)
    {
        _embeddingGenerator = embeddingGenerator;
        _messageRepository = messageRepository;
        _chatSessionRepository = chatSessionRepository;
        _analysisStateRepository = analysisStateRepository;
        _entityRepository = entityRepository;
        _factRepository = factRepository;
        _relationshipRepository = relationshipRepository;
        _communicationEventRepository = communicationEventRepository;
        _botCommandService = botCommandService;
        _stateProfileRepository = stateProfileRepository;
        _strategyDraftRepository = strategyDraftRepository;
        _periodRepository = periodRepository;
        _stage6ArtifactRepository = stage6ArtifactRepository;
        _stage6ArtifactFreshnessService = stage6ArtifactFreshnessService;
        _stage6CaseRepository = stage6CaseRepository;
        _llmGateway = llmGateway;
        _embeddingSettings = embeddingSettings.Value;
        _analysisSettings = analysisSettings.Value;
        _botChatSettings = botChatSettings.Value;
        _telegramSettings = telegramSettings.Value;
        _logger = logger;
    }

    public async Task<string> GenerateReplyAsync(
        string userMessage,
        long? transportChatId = null,
        long? sourceMessageId = null,
        long? senderId = null,
        CancellationToken ct = default)
    {
        var diagnostics = await GenerateReplyWithDiagnosticsAsync(
            userMessage,
            transportChatId,
            sourceMessageId,
            senderId,
            ct);
        return diagnostics.Reply;
    }

    public async Task<BotChatTurnDiagnostics> GenerateReplyWithDiagnosticsAsync(
        string userMessage,
        long? transportChatId = null,
        long? sourceMessageId = null,
        long? senderId = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var normalizedMessage = (userMessage ?? string.Empty).Trim();
        var diagnostics = new BotChatTurnDiagnostics();
        if (string.IsNullOrWhiteSpace(normalizedMessage))
        {
            diagnostics.Reply = "Введите сообщение или команду. Подсказка: /help";
            return diagnostics;
        }

        var commandResult = await _botCommandService.TryHandleAsync(
            normalizedMessage,
            transportChatId,
            sourceMessageId,
            senderId,
            ct);
        if (commandResult.Handled)
        {
            diagnostics.Reply = commandResult.Reply;
            return diagnostics;
        }

        List<Fact> facts = [];
        try
        {
            var embedding = await _embeddingGenerator.GenerateAsync(
                _embeddingSettings.Model,
                normalizedMessage,
                ct);
            diagnostics.EmbeddingCalls = 1;
            if (embedding.Length == 0)
            {
                _logger.LogWarning("Stage6 chat embedding is empty. Continuing without similar-facts memory context.");
            }
            else
            {
                facts = await _factRepository.SearchSimilarFactsAsync(
                    _embeddingSettings.Model,
                    embedding,
                    DefaultFactLimit,
                    ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to resolve similar-facts memory context for Stage6 bot chat. Continuing in degraded mode.");
        }

        var localContext = await BuildLocalChatContextAsync(transportChatId, sourceMessageId, ct);
        var stage6Context = await BuildStage6ContextAsync(transportChatId, ct);
        var systemPrompt = BotChatPromptBuilder.BuildSystemPrompt(localContext, facts, stage6Context);
        var messages = new List<LlmGatewayMessage>
        {
            LlmGatewayMessage.FromText(LlmMessageRole.System, systemPrompt),
            LlmGatewayMessage.FromText(LlmMessageRole.User, normalizedMessage)
        };
        var tools = BuildTools();
        var firstResponse = await ExecuteGatewayTurnAsync(
            modality: LlmModality.Tools,
            taskKey: "legacy_stage6_bot_chat_tools",
            messages,
            tools,
            transportChatId,
            sourceMessageId,
            responseMode: LlmResponseMode.ToolCalls,
            ct);
        diagnostics.ResolvedModel = firstResponse.Model;
        diagnostics.ChatCompletionCalls++;
        var firstText = NormalizeResponseText(firstResponse.Output.Text);
        var rawToolCalls = firstResponse.Output.ToolCalls ?? [];
        var droppedToolCalls = Math.Max(0, rawToolCalls.Count - MaxToolCallsPerTurn);
        diagnostics.DroppedToolCalls = droppedToolCalls;
        if (droppedToolCalls > 0)
        {
            _logger.LogWarning(
                "Tool call count exceeded limit. Dropping extra calls. total={Total}, dropped={Dropped}",
                rawToolCalls.Count,
                droppedToolCalls);
        }

        var toolCalls = rawToolCalls.Take(MaxToolCallsPerTurn).ToList();
        if (toolCalls.Count == 0)
        {
            if (string.IsNullOrWhiteSpace(firstText))
            {
                diagnostics.Reply = "Сейчас недостаточно данных для уверенного ответа.";
                return diagnostics;
            }

            diagnostics.Reply = firstText.Trim();
            return diagnostics;
        }

        messages.Add(new LlmGatewayMessage
        {
            Role = LlmMessageRole.Assistant,
            ContentParts = string.IsNullOrWhiteSpace(firstText)
                ? []
                : [LlmMessageContentPart.FromText(firstText)],
            ToolCalls = toolCalls
        });

        foreach (var toolCall in toolCalls)
        {
            ct.ThrowIfCancellationRequested();
            string toolResult;
            var toolName = toolCall.Name;
            var toolArgs = toolCall.ArgumentsJson ?? "{}";
            diagnostics.ToolCallsExecuted.Add(toolName);
            _logger.LogInformation(
                "Executing tool {ToolName} with args: {Args}",
                toolName,
                toolArgs);
            try
            {
                toolResult = await ExecuteToolCallAsync(toolCall, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Tool execution failed. Tool={ToolName}",
                    toolName);
                toolResult = BuildToolErrorResult(
                    "tool_execution_failed",
                    "Tool execution failed.",
                    toolName);
            }

            _logger.LogInformation(
                "Tool {ToolName} returned {Length} characters of JSON data",
                toolName,
                toolResult.Length);
            toolResult = EnsureToolResultSize(toolResult);
            _logger.LogInformation(
                "Tool {ToolName} finished. args: {Args}",
                toolName,
                toolArgs);
            messages.Add(new LlmGatewayMessage
            {
                Role = LlmMessageRole.Tool,
                Name = toolName,
                ToolCallId = toolCall.Id,
                ContentParts = [LlmMessageContentPart.FromText(toolResult)]
            });
        }

        messages.Add(
            LlmGatewayMessage.FromText(
                LlmMessageRole.System,
                BotChatPromptBuilder.BuildPostToolInstruction()));

        var secondResponse = await ExecuteGatewayTurnAsync(
            modality: LlmModality.TextChat,
            taskKey: "legacy_stage6_bot_chat_reply",
            messages,
            [],
            transportChatId,
            sourceMessageId,
            responseMode: LlmResponseMode.Text,
            ct);
        diagnostics.ChatCompletionCalls++;
        var reply = NormalizeResponseText(secondResponse.Output.Text);

        if (string.IsNullOrWhiteSpace(reply))
        {
            diagnostics.Reply = string.IsNullOrWhiteSpace(firstText)
                ? "Сейчас недостаточно данных для уверенного ответа."
                : firstText.Trim();
            return diagnostics;
        }

        diagnostics.Reply = reply.Trim();
        return diagnostics;
    }

    public async Task<string> TriggerSessionResummaryAsync(long chatId, int sessionIndex, CancellationToken ct)
    {
        if (chatId == 0 || sessionIndex < 0)
        {
            return "Неверные параметры. Использование: /resummary <chat_id> <session_index>";
        }

        var sessionsByChat = await _chatSessionRepository.GetByChatsAsync([chatId], ct);
        var session = sessionsByChat.GetValueOrDefault(chatId)?
            .FirstOrDefault(x => x.SessionIndex == sessionIndex);
        if (session == null)
        {
            return $"Сессия не найдена: chat_id={chatId}, session_index={sessionIndex}.";
        }

        var checkpointKey = BuildSessionSummaryCheckpointKey(chatId, sessionIndex);
        await _analysisStateRepository.ResetWatermarksIfExistAsync([checkpointKey], ct);
        _logger.LogInformation(
            "Manual session re-summary requested: chat_id={ChatId}, session_index={SessionIndex}, checkpoint_key={CheckpointKey}",
            chatId,
            sessionIndex,
            checkpointKey);
        return $"Пересборка summary запрошена: chat_id={chatId}, session_index={sessionIndex}.";
    }

    private static string BuildSessionSummaryCheckpointKey(long chatId, int sessionIndex)
    {
        return $"{SessionSummaryCheckpointPrefix}:{chatId}:{sessionIndex}";
    }

    private async Task<LlmGatewayResponse> ExecuteGatewayTurnAsync(
        LlmModality modality,
        string taskKey,
        List<LlmGatewayMessage> messages,
        List<LlmToolDefinition> tools,
        long? transportChatId,
        long? sourceMessageId,
        LlmResponseMode responseMode,
        CancellationToken ct)
    {
        var request = new LlmGatewayRequest
        {
            Modality = modality,
            TaskKey = taskKey,
            Messages = messages,
            ToolDefinitions = tools,
            ResponseMode = responseMode,
            Limits = new LlmExecutionLimits
            {
                MaxTokens = ReplyMaxTokens,
                Temperature = 0.2f,
                TimeoutMs = Math.Max(1, _analysisSettings.HttpTimeoutSeconds) * 1000
            },
            Trace = new LlmTraceContext
            {
                PathKey = "legacy_stage6_bot_chat",
                RequestId = BuildGatewayRequestId(taskKey, transportChatId, sourceMessageId),
                IsImportScope = false,
                IsOptionalPath = true,
                ScopeTags =
                [
                    "stage6",
                    "legacy",
                    "bot_chat"
                ]
            }
        };

        return await _llmGateway.ExecuteAsync(request, ct);
    }

    private static string BuildGatewayRequestId(string taskKey, long? transportChatId, long? sourceMessageId)
    {
        return $"{taskKey}:{transportChatId ?? 0}:{sourceMessageId ?? 0}:{Guid.NewGuid():N}";
    }

    private static List<LlmToolDefinition> BuildTools()
    {
        return
        [
            new LlmToolDefinition
            {
                Name = "get_entity_dossier",
                Description = "Get full dossier facts for an entity by name.",
                ParametersJsonSchema = JsonSerializer.Serialize(
                    new Dictionary<string, object?>
                    {
                        ["type"] = "object",
                        ["properties"] = new Dictionary<string, object?>
                        {
                            ["entity_name"] = new Dictionary<string, object?>
                            {
                                ["type"] = "string",
                                ["description"] = "Entity name to search for."
                            },
                            ["limit"] = new Dictionary<string, object?>
                            {
                                ["type"] = "integer",
                                ["description"] = "Max number of facts to return.",
                                ["minimum"] = 5,
                                ["maximum"] = 200
                            },
                            ["category_filter"] = new Dictionary<string, object?>
                            {
                                ["type"] = "string",
                                ["description"] = "Optional category (e.g., Work, Health) to filter facts."
                            }
                        },
                        ["required"] = new[] { "entity_name" },
                        ["additionalProperties"] = false
                    },
                    JsonOptions)
            },
            new LlmToolDefinition
            {
                Name = "get_relationships",
                Description = "Get known relationships for an entity by name.",
                ParametersJsonSchema = JsonSerializer.Serialize(
                    new Dictionary<string, object?>
                    {
                        ["type"] = "object",
                        ["properties"] = new Dictionary<string, object?>
                        {
                            ["entity_name"] = new Dictionary<string, object?>
                            {
                                ["type"] = "string",
                                ["description"] = "Entity name to search for."
                            }
                        },
                        ["required"] = new[] { "entity_name" },
                        ["additionalProperties"] = false
                    },
                    JsonOptions)
            }
        ];
    }

    private async Task<string> ExecuteToolCallAsync(LlmToolCall toolCall, CancellationToken ct)
    {
        var toolName = toolCall.Name?.Trim();
        var dossierArgs = ParseDossierToolArgs(toolCall);
        if (string.IsNullOrWhiteSpace(dossierArgs.EntityName))
        {
            return JsonSerializer.Serialize(new
            {
                ok = false,
                error = "missing_entity_name",
                message = "Entity name was not provided in tool arguments."
            }, JsonOptions);
        }

        return toolName?.ToLowerInvariant() switch
        {
            "get_entity_dossier" => await BuildEntityDossierResultAsync(dossierArgs, ct),
            "get_relationships" => await BuildRelationshipsResultAsync(dossierArgs.EntityName, ct),
            _ => JsonSerializer.Serialize(new
            {
                ok = false,
                error = "unknown_tool",
                tool = toolName,
                message = "Tool is not supported."
            }, JsonOptions)
        };
    }

    private async Task<string> BuildEntityDossierResultAsync(DossierToolArgs args, CancellationToken ct)
    {
        var entity = await _entityRepository.FindByNameOrAliasAsync(args.EntityName, ct);
        if (entity == null)
        {
            var yoToYeName = NormalizeYoToYe(args.EntityName);
            if (!string.Equals(args.EntityName, yoToYeName, StringComparison.Ordinal))
            {
                entity = await _entityRepository.FindByNameOrAliasAsync(yoToYeName, ct);
            }
        }

        entity ??= await _entityRepository.FindBestByNameAsync(args.EntityName, ct);
        if (entity == null)
        {
            return JsonSerializer.Serialize(new
            {
                ok = true,
                found = false,
                entity_name = args.EntityName,
                message = "Entity not found. Ask the user for a different spelling."
            }, JsonOptions);
        }

        var factsPage = await _factRepository.GetDossierFactsPageAsync(entity.Id, args.Limit, args.CategoryFilter, ct);
        var facts = factsPage.Facts;
        var relationships = await _relationshipRepository.GetByEntityWithNamesAsync(entity.Id, ct);
        var events = await _communicationEventRepository.GetByEntityAsync(
            entity.Id,
            DateTime.UnixEpoch,
            DateTime.UtcNow.AddDays(1),
            ct);
        var sourceContextByMessageId = await BuildContextSummaryBySourceMessageIdAsync(
            facts.Where(x => x.SourceMessageId.HasValue).Select(x => x.SourceMessageId!.Value)
                .Concat(relationships.Where(x => x.SourceMessageId.HasValue).Select(x => x.SourceMessageId!.Value))
                .Concat(events.Select(x => x.MessageId)),
            ct);
        var payload = new
        {
            ok = true,
            found = true,
            meta = new
            {
                total_facts_in_db = factsPage.TotalCount,
                returned_facts = facts.Count,
                has_more = factsPage.TotalCount > facts.Count
            },
            entity = new
            {
                id = entity.Id,
                name = entity.Name,
                type = entity.Type.ToString(),
                aliases = entity.Aliases,
                actor_key = entity.ActorKey,
                telegram_user_id = entity.TelegramUserId,
                telegram_username = entity.TelegramUsername
            },
            facts = facts
                .OrderByDescending(x => x.IsCurrent)
                .ThenByDescending(x => x.Confidence)
                .ThenByDescending(x => x.UpdatedAt)
                .Select(x => new
                {
                    category = x.Category,
                    key = x.Key,
                    value = x.Value,
                    is_current = x.IsCurrent,
                    confidence = x.Confidence,
                    status = x.Status.ToString(),
                    source_message_id = x.SourceMessageId,
                    source_context = x.SourceMessageId.HasValue
                        ? sourceContextByMessageId.GetValueOrDefault(x.SourceMessageId.Value)
                        : null,
                    valid_from = x.ValidFrom,
                    valid_until = x.ValidUntil,
                    updated_at = x.UpdatedAt
                })
                .ToList(),
            relationships = relationships.Select(x =>
            {
                var outgoing = x.FromEntityId == entity.Id;
                return new
                {
                    relationship_id = x.RelationshipId,
                    type = x.Type,
                    direction = outgoing ? "outgoing" : "incoming",
                    related_entity_id = outgoing ? x.ToEntityId : x.FromEntityId,
                    related_entity_name = outgoing ? x.ToEntityName : x.FromEntityName,
                    confidence = x.Confidence,
                    status = x.Status.ToString(),
                    context_text = x.ContextText,
                    source_message_id = x.SourceMessageId,
                    source_context = x.SourceMessageId.HasValue
                        ? sourceContextByMessageId.GetValueOrDefault(x.SourceMessageId.Value)
                        : null,
                    updated_at = x.UpdatedAt
                };
            })
                .ToList(),
            events = events
                .OrderByDescending(x => x.CreatedAt)
                .Select(x => new
                {
                    event_type = x.EventType,
                    object_name = x.ObjectName,
                    sentiment = x.Sentiment,
                    summary = x.Summary,
                    confidence = x.Confidence,
                    source_message_id = x.MessageId,
                    source_context = sourceContextByMessageId.GetValueOrDefault(x.MessageId),
                    created_at = x.CreatedAt
                })
                .ToList()
        };

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private static string NormalizeYoToYe(string value)
    {
        return value
            .Replace('ё', 'е')
            .Replace('Ё', 'Е');
    }

    private async Task<string> BuildRelationshipsResultAsync(string entityName, CancellationToken ct)
    {
        var entity = await _entityRepository.FindBestByNameAsync(entityName, ct);
        if (entity == null)
        {
            return JsonSerializer.Serialize(new
            {
                ok = true,
                found = false,
                entity_name = entityName,
                message = "Entity not found."
            }, JsonOptions);
        }

        var relationships = await _relationshipRepository.GetByEntityWithNamesAsync(entity.Id, ct);
        var sourceContextByMessageId = await BuildContextSummaryBySourceMessageIdAsync(
            relationships.Where(x => x.SourceMessageId.HasValue).Select(x => x.SourceMessageId!.Value),
            ct);
        var payload = new
        {
            ok = true,
            found = true,
            entity = new
            {
                id = entity.Id,
                name = entity.Name
            },
            relationships = relationships.Select(x =>
            {
                var outgoing = x.FromEntityId == entity.Id;
                return new
                {
                    relationship_id = x.RelationshipId,
                    type = x.Type,
                    direction = outgoing ? "outgoing" : "incoming",
                    related_entity_id = outgoing ? x.ToEntityId : x.FromEntityId,
                    related_entity_name = outgoing ? x.ToEntityName : x.FromEntityName,
                    confidence = x.Confidence,
                    status = x.Status.ToString(),
                    source_message_id = x.SourceMessageId,
                    source_context = x.SourceMessageId.HasValue
                        ? sourceContextByMessageId.GetValueOrDefault(x.SourceMessageId.Value)
                        : null,
                    updated_at = x.UpdatedAt
                };
            }).ToList()
        };

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private static string ExtractEntityName(string? arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
        {
            return string.Empty;
        }

        try
        {
            using var doc = JsonDocument.Parse(arguments);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return string.Empty;
            }

            if (TryReadStringArg(doc.RootElement, "entity_name", out var entityName))
            {
                return entityName;
            }

            if (TryReadStringArg(doc.RootElement, "name", out var fallbackName))
            {
                return fallbackName;
            }

            return string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static DossierToolArgs ParseDossierToolArgs(LlmToolCall toolCall)
    {
        var entityName = ExtractEntityName(toolCall.ArgumentsJson);
        var limit = DefaultDossierLimit;
        string? categoryFilter = null;

        try
        {
            using var doc = JsonDocument.Parse(toolCall.ArgumentsJson ?? "{}");
            if (doc.RootElement.TryGetProperty("limit", out var limitNode) &&
                limitNode.ValueKind == JsonValueKind.Number &&
                limitNode.TryGetInt32(out var parsedLimit))
            {
                limit = Math.Clamp(parsedLimit, 5, 200);
            }

            if (doc.RootElement.TryGetProperty("category_filter", out var categoryNode) &&
                categoryNode.ValueKind == JsonValueKind.String)
            {
                categoryFilter = categoryNode.GetString();
            }
        }
        catch
        {
            // ignore malformed arguments
        }

        return new DossierToolArgs(
            entityName,
            limit,
            string.IsNullOrWhiteSpace(categoryFilter) ? null : categoryFilter.Trim());
    }

    private record DossierToolArgs(string EntityName, int Limit, string? CategoryFilter);

    private async Task<Dictionary<long, string>> BuildContextSummaryBySourceMessageIdAsync(
        IEnumerable<long> sourceMessageIds,
        CancellationToken ct)
    {
        var result = new Dictionary<long, string>();
        var distinctIds = sourceMessageIds
            .Where(x => x > 0)
            .Distinct()
            .Take(48)
            .ToArray();
        if (distinctIds.Length == 0)
        {
            return result;
        }

        foreach (var sourceMessageId in distinctIds)
        {
            var source = await _messageRepository.GetByIdAsync(sourceMessageId, ct);
            if (source == null)
            {
                continue;
            }

            var window = await _messageRepository.GetChatWindowAroundAsync(source.ChatId, sourceMessageId, 5, 5, ct);
            if (window.Count == 0)
            {
                continue;
            }

            var lines = window
                .OrderBy(x => x.Timestamp)
                .ThenBy(x => x.Id)
                .Select(message =>
                {
                    var sender = string.IsNullOrWhiteSpace(message.SenderName)
                        ? $"user:{message.SenderId}"
                        : message.SenderName.Trim();
                    var text = MessageContentBuilder.TruncateForContext(
                        MessageContentBuilder.BuildSemanticContent(message),
                        120);
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        return null;
                    }

                    var marker = message.Id == sourceMessageId ? "*" : "-";
                    return $"{marker}[{message.Timestamp:MM-dd HH:mm}] {sender}: {text}";
                })
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Take(11)
                .ToList();
            if (lines.Count == 0)
            {
                continue;
            }

            result[sourceMessageId] = MessageContentBuilder.TruncateForContext(
                string.Join(" | ", lines),
                1200);
        }

        return result;
    }

    private static bool TryReadStringArg(JsonElement root, string field, out string value)
    {
        value = string.Empty;
        if (!root.TryGetProperty(field, out var node) || node.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = (node.GetString() ?? string.Empty).Trim();
        return value.Length > 0;
    }

    private static string NormalizeResponseText(object? content)
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
            return NormalizeJsonElementText(element);
        }

        return content.ToString() ?? string.Empty;
    }

    private static string NormalizeJsonElementText(JsonElement element)
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

        return string.Empty;
    }

    private static string EnsureToolResultSize(string toolResult)
    {
        if (toolResult.Length <= MaxToolResultChars)
        {
            return toolResult;
        }

        return JsonSerializer.Serialize(new
        {
            ok = false,
            error = "tool_result_truncated",
            message = $"Tool result exceeded {MaxToolResultChars} characters.",
            excerpt = toolResult[..MaxToolResultChars]
        }, JsonOptions);
    }

    private static string BuildToolErrorResult(string errorCode, string message, string? toolName)
    {
        return JsonSerializer.Serialize(new
        {
            ok = false,
            error = errorCode,
            tool = toolName,
            message
        }, JsonOptions);
    }

    private static readonly JsonSerializerOptions JsonOptions = ExtractionSerializationOptions.SnakeCase;

    private async Task<BotChatLocalContext> BuildLocalChatContextAsync(
        long? transportChatId,
        long? sourceMessageId,
        CancellationToken ct)
    {
        if (!transportChatId.HasValue || transportChatId.Value <= 0)
        {
            return BotChatLocalContext.Empty;
        }

        var chatId = transportChatId.Value;
        var recentSessionSummaries = await BuildRecentSessionSummariesAsync(chatId, ct);
        var localMessages = await BuildPromptLocalMessagesAsync(chatId, sourceMessageId, ct);

        return new BotChatLocalContext(
            chatId,
            recentSessionSummaries,
            localMessages.Messages,
            localMessages.SourceLabel);
    }

    private async Task<List<string>> BuildRecentSessionSummariesAsync(long chatId, CancellationToken ct)
    {
        var sessionsByChat = await _chatSessionRepository.GetByChatsAsync([chatId], ct);
        var sessions = sessionsByChat.GetValueOrDefault(chatId) ?? [];
        if (sessions.Count == 0)
        {
            return [];
        }

        return sessions
            .Where(x => !string.IsNullOrWhiteSpace(x.Summary))
            .OrderByDescending(x => x.EndDate)
            .Take(SessionSummaryPromptLimit)
            .OrderBy(x => x.EndDate)
            .ThenBy(x => x.SessionIndex)
            .Select(x =>
            {
                var summary = MessageContentBuilder.TruncateForContext(
                    MessageContentBuilder.CollapseWhitespace(x.Summary),
                    SessionSummaryCharsLimit);
                return $"[session {x.SessionIndex}, {x.StartDate:yyyy-MM-dd}..{x.EndDate:yyyy-MM-dd}] {summary}";
            })
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();
    }

    private async Task<(List<string> Messages, string SourceLabel)> BuildPromptLocalMessagesAsync(
        long chatId,
        long? sourceMessageId,
        CancellationToken ct)
    {
        if (sourceMessageId.HasValue && sourceMessageId.Value > 0)
        {
            var source = await _messageRepository.GetByIdAsync(sourceMessageId.Value, ct);
            if (source != null && source.ChatId == chatId)
            {
                var sourceWindow = await _messageRepository.GetChatWindowAroundAsync(
                    chatId,
                    sourceMessageId.Value,
                    PromptLocalWindowLinesLimit / 2,
                    PromptLocalWindowLinesLimit / 2,
                    ct);
                var sourceWindowLines = FormatPromptMessageLines(sourceWindow, sourceMessageId.Value);
                if (sourceWindowLines.Count > 0)
                {
                    return (sourceWindowLines, $"source_window(message_id={sourceMessageId.Value})");
                }
            }
        }

        var recentMessages = await _messageRepository.GetProcessedByChatAsync(chatId, PromptFallbackMessagesLimit, ct);
        var recentLines = FormatPromptMessageLines(recentMessages, null);
        return (recentLines, "recent_processed_fallback");
    }

    private static List<string> FormatPromptMessageLines(IReadOnlyCollection<Message> messages, long? sourceMessageId)
    {
        if (messages.Count == 0)
        {
            return [];
        }

        return messages
            .OrderBy(x => x.Timestamp)
            .ThenBy(x => x.Id)
            .Select(message =>
            {
                var sender = string.IsNullOrWhiteSpace(message.SenderName)
                    ? $"user:{message.SenderId}"
                    : message.SenderName.Trim();
                var semantic = MessageContentBuilder.TruncateForContext(
                    MessageContentBuilder.CollapseWhitespace(MessageContentBuilder.BuildSemanticContent(message)),
                    PromptMessageCharsLimit);
                if (string.IsNullOrWhiteSpace(semantic))
                {
                    return null;
                }

                var marker = sourceMessageId.HasValue && message.Id == sourceMessageId.Value ? "*" : "-";
                return $"{marker}[{message.Timestamp:MM-dd HH:mm}] {sender}: {semantic}";
            })
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Take(PromptLocalWindowLinesLimit)
            .ToList()!;
    }

    private async Task<BotChatStage6Context> BuildStage6ContextAsync(long? transportChatId, CancellationToken ct)
    {
        var scope = await ResolveStage6ScopeAsync(transportChatId, ct);
        if (scope == null)
        {
            return BotChatStage6Context.Unavailable("scope_unresolved");
        }

        var stateBlockTask = BuildStage6StateBlockAsync(scope, ct);
        var strategyBlockTask = BuildStage6StrategyBlockAsync(scope, ct);
        var profilesBlockTask = BuildStage6ProfilesBlockAsync(scope, ct);
        var timelineBlockTask = BuildStage6TimelineBlockAsync(scope, ct);
        await Task.WhenAll(stateBlockTask, strategyBlockTask, profilesBlockTask, timelineBlockTask);

        var scopeLine = $"case_id={scope.CaseId}; chat_id={scope.ChatId}; source={scope.Source}";
        return new BotChatStage6Context(
            scopeLine,
            CapStage6Section(stateBlockTask.Result),
            CapStage6Section(strategyBlockTask.Result),
            CapStage6Section(profilesBlockTask.Result),
            CapStage6Section(timelineBlockTask.Result));
    }

    private async Task<BotChatStage6Scope?> ResolveStage6ScopeAsync(long? transportChatId, CancellationToken ct)
    {
        var chatId = ResolveStage6ChatId(transportChatId);
        if (chatId <= 0 || ScopeVisibilityPolicy.IsSyntheticChatId(chatId))
        {
            return null;
        }

        var caseId = _botChatSettings.DefaultCaseId;
        var source = "bot_default";
        if (caseId <= 0)
        {
            var candidate = (await _stage6CaseRepository.GetScopeCandidatesAsync(Stage6ScopeCandidatesLimit, ct))
                .Where(x => x.ChatId == chatId)
                .OrderByDescending(x => x.ActiveCaseCount)
                .ThenByDescending(x => x.TotalCaseCount)
                .ThenByDescending(x => x.LastCaseUpdatedAtUtc)
                .FirstOrDefault();
            if (candidate != null)
            {
                caseId = candidate.ScopeCaseId;
                source = "scope_candidates";
            }
        }

        return caseId > 0
            ? new BotChatStage6Scope(caseId, chatId, source)
            : null;
    }

    private long ResolveStage6ChatId(long? transportChatId)
    {
        if (transportChatId.HasValue && transportChatId.Value > 0)
        {
            return transportChatId.Value;
        }

        if (_botChatSettings.DefaultChatId > 0)
        {
            return _botChatSettings.DefaultChatId;
        }

        return _telegramSettings.MonitoredChatIds.FirstOrDefault(x => x > 0);
    }

    private async Task<string> BuildStage6StateBlockAsync(BotChatStage6Scope scope, CancellationToken ct)
    {
        try
        {
            var snapshot = await TryGetReusableCurrentStateSnapshotAsync(scope, ct)
                ?? (await _stateProfileRepository.GetStateSnapshotsByCaseAsync(scope.CaseId, 30, ct))
                .Where(x => x.ChatId == null || x.ChatId == scope.ChatId)
                .OrderByDescending(x => x.AsOf)
                .FirstOrDefault();
            if (snapshot == null)
            {
                return "unavailable: no state snapshot";
            }

            var signals = ParseJsonList(snapshot.KeySignalRefsJson, 3);
            var risks = ParseJsonList(snapshot.RiskRefsJson, 3);
            var status = string.IsNullOrWhiteSpace(snapshot.AlternativeStatus)
                ? snapshot.RelationshipStatus
                : $"{snapshot.RelationshipStatus} (alt {snapshot.AlternativeStatus})";
            return string.Join('\n',
                $"dynamic={ShortText(snapshot.DynamicLabel, 120, "unknown")}",
                $"status={ShortText(status, 120, "unknown")}",
                $"confidence={snapshot.Confidence:0.00}; ambiguity={snapshot.AmbiguityScore:0.00}",
                $"signals={(signals.Count == 0 ? "limited_evidence" : string.Join("; ", signals))}",
                $"risks={(risks.Count == 0 ? "not_stable" : string.Join("; ", risks))}",
                $"as_of={snapshot.AsOf:yyyy-MM-dd HH:mm} UTC");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to build Stage6 state context. case_id={CaseId}, chat_id={ChatId}", scope.CaseId, scope.ChatId);
            return "unavailable: read_error";
        }
    }

    private async Task<string> BuildStage6StrategyBlockAsync(BotChatStage6Scope scope, CancellationToken ct)
    {
        try
        {
            var reusable = await TryGetReusableStrategyAsync(scope, ct);
            var strategy = reusable.Record;
            var options = reusable.Options;
            if (strategy == null)
            {
                strategy = (await _strategyDraftRepository.GetStrategyRecordsByCaseAsync(scope.CaseId, ct))
                    .Where(x => x.ChatId == null || x.ChatId == scope.ChatId)
                    .OrderByDescending(x => x.CreatedAt)
                    .FirstOrDefault();
                options = strategy == null
                    ? []
                    : await _strategyDraftRepository.GetStrategyOptionsByRecordIdAsync(strategy.Id, ct);
            }

            if (strategy == null)
            {
                return "unavailable: no strategy record";
            }

            var primary = options.FirstOrDefault(x => x.IsPrimary) ?? options.FirstOrDefault();
            var alternatives = options
                .Where(x => primary == null || x.Id != primary.Id)
                .Take(2)
                .Select(x => $"{x.ActionType}: {ShortText(x.Summary, 90, x.ActionType)}")
                .ToList();

            var nextStep = ShortText(strategy.MicroStep, 140, primary?.Summary ?? "clarify missing context");
            return string.Join('\n',
                $"goal={ShortText(strategy.RecommendedGoal, 110, "stabilize and clarify")}",
                $"primary={(primary == null ? "none" : $"{primary.ActionType}: {ShortText(primary.Summary, 120, primary.ActionType)}")}",
                $"micro_step={nextStep}",
                $"alternatives={(alternatives.Count == 0 ? "none" : string.Join(" | ", alternatives))}",
                $"confidence={strategy.StrategyConfidence:0.00}",
                $"as_of={strategy.CreatedAt:yyyy-MM-dd HH:mm} UTC");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to build Stage6 strategy context. case_id={CaseId}, chat_id={ChatId}", scope.CaseId, scope.ChatId);
            return "unavailable: read_error";
        }
    }

    private async Task<string> BuildStage6ProfilesBlockAsync(BotChatStage6Scope scope, CancellationToken ct)
    {
        try
        {
            var subjects = await ResolveProfileSubjectsAsync(scope.ChatId, ct);
            if (subjects == null)
            {
                return "unavailable: need at least two active participants";
            }

            var profileEvidence = await _stage6ArtifactFreshnessService.BuildEvidenceStampAsync(
                scope.CaseId,
                scope.ChatId,
                "profile",
                ct);
            var profileTtl = _stage6ArtifactFreshnessService.ResolveTtl("profile");
            var now = DateTime.UtcNow;

            var self = await GetLatestProfileBundleAsync(scope, "self", subjects.SelfSenderId.ToString(), profileEvidence.LatestEvidenceAtUtc, profileTtl, now, ct);
            var other = await GetLatestProfileBundleAsync(scope, "other", subjects.OtherSenderId.ToString(), profileEvidence.LatestEvidenceAtUtc, profileTtl, now, ct);
            var pair = await GetLatestProfileBundleAsync(scope, "pair", subjects.PairId, profileEvidence.LatestEvidenceAtUtc, profileTtl, now, ct);
            if (pair == null)
            {
                return "unavailable: no fresh pair profile";
            }

            var works = ResolveProfileSignal(pair, "what_works");
            var fails = ResolveProfileSignal(pair, "what_fails");
            return string.Join('\n',
                $"pair_summary={ShortText(pair.Snapshot.Summary, 150, "pattern not stabilized")}",
                $"pair_traits={FormatProfileTraits(pair, Stage6ContextTraitsLimit)}",
                $"self_traits={(self == null ? "limited_evidence" : FormatProfileTraits(self, 2))}",
                $"other_traits={(other == null ? "limited_evidence" : FormatProfileTraits(other, 2))}",
                $"works={works}",
                $"watch={fails}",
                $"confidence={pair.Snapshot.Confidence:0.00}; stability={pair.Snapshot.Stability:0.00}; as_of={pair.Snapshot.CreatedAt:yyyy-MM-dd HH:mm} UTC");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to build Stage6 profile context. case_id={CaseId}, chat_id={ChatId}", scope.CaseId, scope.ChatId);
            return "unavailable: read_error";
        }
    }

    private async Task<string> BuildStage6TimelineBlockAsync(BotChatStage6Scope scope, CancellationToken ct)
    {
        try
        {
            var periods = (await _periodRepository.GetPeriodsByCaseAsync(scope.CaseId, ct))
                .Where(x => x.ChatId == null || x.ChatId == scope.ChatId)
                .OrderByDescending(x => x.StartAt)
                .ToList();
            if (periods.Count == 0)
            {
                return "unavailable: no periods";
            }

            var current = periods.FirstOrDefault(x => x.IsOpen) ?? periods[0];
            var prior = periods.Where(x => x.Id != current.Id).Take(Stage6ContextPeriodsLimit).ToList();
            var lines = new List<string> { $"current={FormatPeriodLine(current)}" };
            lines.AddRange(prior.Select(x => $"prior={FormatPeriodLine(x)}"));
            var transitions = await _periodRepository.GetTransitionsByPeriodAsync(current.Id, ct);
            lines.Add($"unresolved_transitions={transitions.Count(IsActionableUnresolvedTransition)}");
            return string.Join('\n', lines);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to build Stage6 timeline context. case_id={CaseId}, chat_id={ChatId}", scope.CaseId, scope.ChatId);
            return "unavailable: read_error";
        }
    }

    private async Task<StateSnapshot?> TryGetReusableCurrentStateSnapshotAsync(BotChatStage6Scope scope, CancellationToken ct)
    {
        var artifact = await _stage6ArtifactRepository.GetCurrentAsync(
            scope.CaseId,
            scope.ChatId,
            Stage6ArtifactTypes.CurrentState,
            Stage6ArtifactTypes.ChatScope(scope.ChatId),
            ct);
        if (artifact == null)
        {
            return null;
        }

        var evidence = await _stage6ArtifactFreshnessService.BuildEvidenceStampAsync(scope.CaseId, scope.ChatId, Stage6ArtifactTypes.CurrentState, ct);
        var freshness = Stage6ArtifactFreshness.Evaluate(artifact, DateTime.UtcNow, evidence.LatestEvidenceAtUtc);
        if (freshness.IsStale || !Guid.TryParse(artifact.PayloadObjectId, out var snapshotId))
        {
            _ = await _stage6ArtifactRepository.MarkStaleAsync(artifact.Id, freshness.Reason ?? "stale", DateTime.UtcNow, ct);
            return null;
        }

        var snapshot = await _stateProfileRepository.GetStateSnapshotByIdAsync(snapshotId, ct);
        if (snapshot == null)
        {
            _ = await _stage6ArtifactRepository.MarkStaleAsync(artifact.Id, "missing_payload_object", DateTime.UtcNow, ct);
            return null;
        }

        _ = await _stage6ArtifactRepository.TouchReusedAsync(artifact.Id, DateTime.UtcNow, ct);
        return snapshot;
    }

    private async Task<(StrategyRecord? Record, List<StrategyOption> Options)> TryGetReusableStrategyAsync(
        BotChatStage6Scope scope,
        CancellationToken ct)
    {
        var artifact = await _stage6ArtifactRepository.GetCurrentAsync(
            scope.CaseId,
            scope.ChatId,
            Stage6ArtifactTypes.Strategy,
            Stage6ArtifactTypes.ChatScope(scope.ChatId),
            ct);
        if (artifact == null)
        {
            return (null, []);
        }

        var evidence = await _stage6ArtifactFreshnessService.BuildEvidenceStampAsync(scope.CaseId, scope.ChatId, Stage6ArtifactTypes.Strategy, ct);
        var freshness = Stage6ArtifactFreshness.Evaluate(artifact, DateTime.UtcNow, evidence.LatestEvidenceAtUtc);
        if (freshness.IsStale || !Guid.TryParse(artifact.PayloadObjectId, out var strategyId))
        {
            _ = await _stage6ArtifactRepository.MarkStaleAsync(artifact.Id, freshness.Reason ?? "stale", DateTime.UtcNow, ct);
            return (null, []);
        }

        var record = await _strategyDraftRepository.GetStrategyRecordByIdAsync(strategyId, ct);
        if (record == null)
        {
            _ = await _stage6ArtifactRepository.MarkStaleAsync(artifact.Id, "missing_payload_object", DateTime.UtcNow, ct);
            return (null, []);
        }

        var options = await _strategyDraftRepository.GetStrategyOptionsByRecordIdAsync(record.Id, ct);
        _ = await _stage6ArtifactRepository.TouchReusedAsync(artifact.Id, DateTime.UtcNow, ct);
        return (record, options);
    }

    private async Task<BotChatProfileBundle?> GetLatestProfileBundleAsync(
        BotChatStage6Scope scope,
        string subjectType,
        string subjectId,
        DateTime? latestEvidenceAtUtc,
        TimeSpan ttl,
        DateTime nowUtc,
        CancellationToken ct)
    {
        var snapshot = (await _stateProfileRepository.GetProfileSnapshotsByCaseAsync(scope.CaseId, subjectType, subjectId, ct))
            .Where(x => x.ChatId == null || x.ChatId == scope.ChatId)
            .OrderBy(x => x.PeriodId.HasValue ? 1 : 0)
            .ThenByDescending(x => x.CreatedAt)
            .FirstOrDefault();
        if (snapshot == null || IsProfileSnapshotStale(snapshot, latestEvidenceAtUtc, ttl, nowUtc))
        {
            return null;
        }

        var traits = await _stateProfileRepository.GetProfileTraitsBySnapshotIdAsync(snapshot.Id, ct);
        return new BotChatProfileBundle(snapshot, traits);
    }

    private async Task<BotChatProfileSubjects?> ResolveProfileSubjectsAsync(long chatId, CancellationToken ct)
    {
        var senderCounts = (await _messageRepository.GetProcessedByChatAsync(chatId, Stage6ProfileSenderSampleLimit, ct))
            .Where(x => x.SenderId > 0)
            .GroupBy(x => x.SenderId)
            .Select(g => new { SenderId = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .ToList();
        if (senderCounts.Count < 2)
        {
            return null;
        }

        var selfSenderId = senderCounts[0].SenderId;
        var otherSenderId = senderCounts.First(x => x.SenderId != selfSenderId).SenderId;
        return new BotChatProfileSubjects(selfSenderId, otherSenderId);
    }

    private static bool IsProfileSnapshotStale(ProfileSnapshot snapshot, DateTime? latestEvidenceAtUtc, TimeSpan ttl, DateTime nowUtc)
    {
        if (latestEvidenceAtUtc.HasValue && latestEvidenceAtUtc.Value > snapshot.CreatedAt)
        {
            return true;
        }

        return snapshot.CreatedAt.Add(ttl) <= nowUtc;
    }

    private static string ResolveProfileSignal(BotChatProfileBundle bundle, string traitKey)
    {
        var value = bundle.Traits
            .Where(x => x.TraitKey.Equals(traitKey, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.Confidence)
            .Select(x => x.ValueLabel)
            .FirstOrDefault();

        return ShortText(value, 120, traitKey == "what_works" ? "no stable positive pattern yet" : "no stable anti-pattern yet");
    }

    private static string FormatProfileTraits(BotChatProfileBundle bundle, int limit)
    {
        var traits = bundle.Traits
            .Where(x => !x.IsSensitive)
            .Where(x => !ProfileSummaryTraitKeys.Contains(x.TraitKey))
            .OrderByDescending(x => x.Confidence)
            .ThenByDescending(x => x.Stability)
            .Take(Math.Max(1, limit))
            .Select(x => $"{x.TraitKey}={ShortText(x.ValueLabel, 52, x.ValueLabel)}")
            .ToList();

        return traits.Count == 0
            ? ShortText(bundle.Snapshot.Summary, 96, "limited evidence")
            : string.Join("; ", traits);
    }

    private static bool IsActionableUnresolvedTransition(PeriodTransition transition)
    {
        if (transition.IsResolved)
        {
            return false;
        }

        var summary = transition.Summary ?? string.Empty;
        var isGenericUnresolved =
            summary.Contains("No clear transition cause found", StringComparison.OrdinalIgnoreCase) ||
            summary.Contains("transition cause remains unclear", StringComparison.OrdinalIgnoreCase) ||
            summary.Contains("unresolved gap created", StringComparison.OrdinalIgnoreCase);

        return !(transition.TransitionType.Equals("unresolved_gap", StringComparison.OrdinalIgnoreCase)
                 && isGenericUnresolved
                 && transition.Confidence <= 0.55f);
    }

    private static string FormatPeriodLine(Period period)
    {
        var end = period.EndAt?.ToString("yyyy-MM-dd") ?? "now";
        var summary = ShortText(period.Summary, 100, "summary not available");
        return $"[{period.StartAt:yyyy-MM-dd}..{end}] {period.Label}; open_q={period.OpenQuestionsCount}; conf={period.InterpretationConfidence:0.00}; {summary}";
    }

    private static string ShortText(string? value, int maxLength, string fallback)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            return fallback;
        }

        if (normalized.Length <= maxLength)
        {
            return normalized;
        }

        return normalized[..Math.Max(1, maxLength - 3)].TrimEnd() + "...";
    }

    private static List<string> ParseJsonList(string? json, int limit)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return doc.RootElement.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => x.GetString()?.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Take(limit)
                .Cast<string>()
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string CapStage6Section(string value)
    {
        var normalized = (value ?? string.Empty).Replace("\r", string.Empty).Trim();
        if (normalized.Length <= Stage6SectionCharsLimit)
        {
            return normalized;
        }

        return $"{normalized[..Math.Max(1, Stage6SectionCharsLimit - 3)].TrimEnd()}...";
    }

    private sealed record BotChatLocalContext(
        long ChatId,
        IReadOnlyList<string> SessionSummaries,
        IReadOnlyList<string> LocalMessages,
        string LocalMessagesSource)
    {
        public static BotChatLocalContext Empty { get; } = new(0, [], [], string.Empty);

        public bool HasChatContext => ChatId > 0;
    }

    private sealed record BotChatStage6Scope(long CaseId, long ChatId, string Source);

    private sealed record BotChatProfileSubjects(long SelfSenderId, long OtherSenderId)
    {
        public string PairId => $"{SelfSenderId}:{OtherSenderId}";
    }

    private sealed record BotChatProfileBundle(ProfileSnapshot Snapshot, List<ProfileTrait> Traits);

    private sealed record BotChatStage6Context(
        string Scope,
        string CurrentState,
        string Strategy,
        string Profiles,
        string Timeline)
    {
        public static BotChatStage6Context Unavailable(string reason)
        {
            var missing = $"unavailable: {reason}";
            return new BotChatStage6Context("unavailable", missing, missing, missing, missing);
        }
    }

    private static readonly HashSet<string> ProfileSummaryTraitKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "what_works",
        "what_fails",
        "participant_patterns",
        "pair_dynamics",
        "repeated_interaction_modes",
        "changes_over_time"
    };

    private static class BotChatPromptBuilder
    {
        public static string BuildSystemPrompt(
            BotChatLocalContext localContext,
            IReadOnlyCollection<Fact> facts,
            BotChatStage6Context stage6Context)
        {
            var localContextBlock = BuildLocalContextBlock(localContext);
            var factsBlock = BuildFactsBlock(facts);
            var stage6ContextBlock = BuildStage6ContextBlock(stage6Context);
            return $$"""
                You are an AI personal assistant for Rinat.
                Use the provided chat context and memory context to answer accurately and chat-aware.
                Chat context is already included below. Do not claim you cannot see the chat or ask the user to send chat history again.
                Do not answer with phrases like "не вижу чат", "пришлите чат", "нет данных", "I can't see the chat", or "this is the first message in chat" when chat context block is present.
                Stage6 context is structured support (state/strategy/profiles/timeline). Use it to improve reasoning, but do not dump raw blocks to the user.
                Keep local chat recency and user question intent as primary. Use Stage6 to resolve ambiguity and avoid contradictory advice.
                If context is partial, answer in this format:
                - in available context, we can see: X
                - not visible in current context: Y
                - to improve accuracy, clarify: Z
                If the user asks for a dossier, profile, or full information about a person, you MUST call the get_entity_dossier tool.
                Do not fabricate missing facts. Keep the answer concise and direct.

                [chat_context]
                {{localContextBlock}}
                [/chat_context]

                [supplementary_memory_similar_facts]
                {{factsBlock}}
                [/supplementary_memory_similar_facts]

                [stage6_reasoning_context]
                {{stage6ContextBlock}}
                [/stage6_reasoning_context]
                """;
        }

        public static string BuildPostToolInstruction()
        {
            return """
                You are in post-tool synthesis mode.
                Tool messages contain raw JSON from get_entity_dossier/get_relationships and are the only source of truth.

                Your task: produce a clean human-readable final answer based on tool data.
                Do not output raw JSON. Do not copy/paste tool payload blocks, keys, braces, or arrays.
                Do not say "tool output", "json", or "database dump" in the final answer.

                Required output structure (always use all sections):
                1) Confirmed facts
                2) Relationships
                3) Notable events
                4) Uncertainties and data limits

                Synthesis rules:
                - Keep high recall: include all distinct material facts from tool results, but merge duplicates and near-duplicates.
                - Group facts by meaning (for example: identity/profile, work/finance, health/lifestyle, preferences, plans/timeline).
                - Within each group, prioritize current and higher-confidence items first. Mark outdated or low-confidence items explicitly.
                - For relationships, group by type and direction; include related entity and confidence/status when available.
                - For events, present concise timeline-style bullets (newest first when timestamps exist).
                - If no data for a section, state that explicitly in one short line.

                Truncation and completeness rules:
                - If tool payload indicates meta.has_more=true, say that the returned slice is partial and suggest narrowing by category or timeframe.
                - If tool payload indicates tool_result_truncated, clearly mark the response as partial and list what is still reliable from the visible slice.
                - If tool error/missing entity appears, report that clearly and avoid fabricated details.

                Safety rules:
                - Do not hallucinate details outside tool messages and already provided conversation context.
                - Prefer concise synthesized bullets over verbatim extraction.
                """;
        }

        private static string BuildFactsBlock(IReadOnlyCollection<Fact> facts)
        {
            if (facts.Count == 0)
            {
                return "- no facts available";
            }

            var sb = new StringBuilder();
            var index = 1;
            foreach (var fact in facts)
            {
                sb.Append(index++)
                    .Append(". ")
                    .Append(fact.Category)
                    .Append('.')
                    .Append(fact.Key)
                    .Append('=')
                    .Append(fact.Value)
                    .Append(" (confidence=")
                    .Append(fact.Confidence.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture))
                    .AppendLine(")");
            }

            return sb.ToString().TrimEnd();
        }

        private static string BuildStage6ContextBlock(BotChatStage6Context stage6Context)
        {
            return $$"""
                scope: {{stage6Context.Scope}}
                current_state:
                {{stage6Context.CurrentState}}

                strategy:
                {{stage6Context.Strategy}}

                profiles:
                {{stage6Context.Profiles}}

                timeline:
                {{stage6Context.Timeline}}
                """;
        }

        private static string BuildLocalContextBlock(BotChatLocalContext localContext)
        {
            if (!localContext.HasChatContext)
            {
                return "chat_id=unknown\nsession_summaries: unavailable\nlocal_messages: unavailable";
            }

            var sb = new StringBuilder();
            sb.Append("chat_id=").AppendLine(localContext.ChatId.ToString());
            sb.AppendLine("session_summaries:");
            if (localContext.SessionSummaries.Count == 0)
            {
                sb.AppendLine("- none");
            }
            else
            {
                foreach (var summary in localContext.SessionSummaries)
                {
                    sb.Append("- ").AppendLine(summary);
                }
            }

            sb.Append("local_messages_source=").AppendLine(
                string.IsNullOrWhiteSpace(localContext.LocalMessagesSource)
                    ? "unknown"
                    : localContext.LocalMessagesSource);
            sb.AppendLine("local_messages:");
            if (localContext.LocalMessages.Count == 0)
            {
                sb.AppendLine("- none");
            }
            else
            {
                foreach (var line in localContext.LocalMessages)
                {
                    sb.AppendLine(line);
                }
            }

            return sb.ToString().TrimEnd();
        }
    }
}
