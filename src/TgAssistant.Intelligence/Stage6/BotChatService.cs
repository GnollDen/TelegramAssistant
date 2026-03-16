using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.OpenRouter;
using TgAssistant.Intelligence.Stage5;

namespace TgAssistant.Intelligence.Stage6;

public class BotChatService : IBotChatService
{
    private const int DefaultFactLimit = 10;
    private const int ReplyMaxTokens = 4000;
    private const int MaxToolCallsPerTurn = 3;
    private const int MaxToolResultChars = 8000;
    private readonly ITextEmbeddingGenerator _embeddingGenerator;
    private readonly IEntityRepository _entityRepository;
    private readonly IFactRepository _factRepository;
    private readonly IRelationshipRepository _relationshipRepository;
    private readonly ICommunicationEventRepository _communicationEventRepository;
    private readonly OpenRouterAnalysisService _analysisService;
    private readonly EmbeddingSettings _embeddingSettings;
    private readonly AnalysisSettings _analysisSettings;
    private readonly ILogger<BotChatService> _logger;

    public BotChatService(
        ITextEmbeddingGenerator embeddingGenerator,
        IEntityRepository entityRepository,
        IFactRepository factRepository,
        IRelationshipRepository relationshipRepository,
        ICommunicationEventRepository communicationEventRepository,
        OpenRouterAnalysisService analysisService,
        IOptions<EmbeddingSettings> embeddingSettings,
        IOptions<AnalysisSettings> analysisSettings,
        ILogger<BotChatService> logger)
    {
        _embeddingGenerator = embeddingGenerator;
        _entityRepository = entityRepository;
        _factRepository = factRepository;
        _relationshipRepository = relationshipRepository;
        _communicationEventRepository = communicationEventRepository;
        _analysisService = analysisService;
        _embeddingSettings = embeddingSettings.Value;
        _analysisSettings = analysisSettings.Value;
        _logger = logger;
    }

    public async Task<string> GenerateReplyAsync(string userMessage)
    {
        var normalizedMessage = (userMessage ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedMessage))
        {
            return "Please provide a message.";
        }

        var embedding = await _embeddingGenerator.GenerateAsync(
            _embeddingSettings.Model,
            normalizedMessage,
            CancellationToken.None);
        if (embedding.Length == 0)
        {
            _logger.LogWarning("Stage6 chat embedding is empty.");
            return "I cannot answer now because context retrieval failed.";
        }

        var facts = await _factRepository.SearchSimilarFactsAsync(
            _embeddingSettings.Model,
            embedding,
            DefaultFactLimit,
            CancellationToken.None);

        var systemPrompt = BotChatPromptBuilder.BuildSystemPrompt(facts);
        var model = ResolveReplyModel();
        var messages = new List<OpenRouterMessage>
        {
            new() { Role = "system", Content = systemPrompt },
            new() { Role = "user", Content = normalizedMessage }
        };
        var tools = BuildTools();
        var firstResponse = await _analysisService.CompleteChatWithToolsAsync(
            model,
            messages,
            tools,
            ReplyMaxTokens,
            CancellationToken.None);
        var firstText = NormalizeResponseText(firstResponse.Content);
        var rawToolCalls = firstResponse.ToolCalls ?? [];
        var droppedToolCalls = Math.Max(0, rawToolCalls.Count - MaxToolCallsPerTurn);
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
                return "I cannot answer based on the available context facts.";
            }

            return firstText.Trim();
        }

        messages.Add(new OpenRouterMessage
        {
            Role = "assistant",
            Content = string.IsNullOrWhiteSpace(firstText) ? string.Empty : firstText,
            ToolCalls = toolCalls
        });

        foreach (var toolCall in toolCalls)
        {
            string toolResult;
            var toolName = toolCall.Function.Name;
            var toolArgs = toolCall.Function.Arguments ?? "{}";
            _logger.LogInformation(
                "Executing tool {ToolName} with args: {Args}",
                toolName,
                toolArgs);
            try
            {
                toolResult = await ExecuteToolCallAsync(toolCall, CancellationToken.None);
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
            messages.Add(new OpenRouterMessage
            {
                Role = "tool",
                Name = toolName,
                ToolCallId = toolCall.Id,
                Content = toolResult
            });
        }

        messages.Add(new OpenRouterMessage
        {
            Role = "system",
            Content = BotChatPromptBuilder.BuildPostToolInstruction()
        });

        var secondResponse = await _analysisService.CompleteChatWithToolsAsync(
            model,
            messages,
            null,
            ReplyMaxTokens,
            CancellationToken.None);
        var reply = NormalizeResponseText(secondResponse.Content);

        if (string.IsNullOrWhiteSpace(reply))
        {
            return string.IsNullOrWhiteSpace(firstText)
                ? "I cannot answer based on the available context facts."
                : firstText.Trim();
        }

        return reply.Trim();
    }

    private string ResolveReplyModel()
    {
        if (!string.IsNullOrWhiteSpace(_analysisSettings.CheapBaselineModel))
        {
            return _analysisSettings.CheapBaselineModel.Trim();
        }

        if (!string.IsNullOrWhiteSpace(_analysisSettings.CheapModel))
        {
            return _analysisSettings.CheapModel.Trim();
        }

        return "openai/gpt-4o-mini";
    }

    private static List<OpenRouterTool> BuildTools()
    {
        return
        [
            new OpenRouterTool
            {
                Type = "function",
                Function = new OpenRouterFunctionDefinition
                {
                    Name = "get_entity_dossier",
                    Description = "Get full dossier facts for an entity by name.",
                    Parameters = new Dictionary<string, object?>
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
                    }
                }
            },
            new OpenRouterTool
            {
                Type = "function",
                Function = new OpenRouterFunctionDefinition
                {
                    Name = "get_relationships",
                    Description = "Get known relationships for an entity by name.",
                    Parameters = new Dictionary<string, object?>
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
                    }
                }
            }
        ];
    }

    private async Task<string> ExecuteToolCallAsync(OpenRouterToolCall toolCall, CancellationToken ct)
    {
        var toolName = toolCall.Function.Name?.Trim();
        var entityName = ExtractEntityName(toolCall.Function.Arguments);
        if (string.IsNullOrWhiteSpace(entityName))
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
            "get_entity_dossier" => await BuildEntityDossierResultAsync(entityName, ct),
            "get_relationships" => await BuildRelationshipsResultAsync(entityName, ct),
            _ => JsonSerializer.Serialize(new
            {
                ok = false,
                error = "unknown_tool",
                tool = toolName,
                message = "Tool is not supported."
            }, JsonOptions)
        };
    }

    private async Task<string> BuildEntityDossierResultAsync(string entityName, CancellationToken ct)
    {
        var entity = await _entityRepository.FindByNameOrAliasAsync(entityName, ct);
        if (entity == null)
        {
            var yoToYeName = NormalizeYoToYe(entityName);
            if (!string.Equals(entityName, yoToYeName, StringComparison.Ordinal))
            {
                entity = await _entityRepository.FindByNameOrAliasAsync(yoToYeName, ct);
            }
        }

        entity ??= await _entityRepository.FindBestByNameAsync(entityName, ct);
        if (entity == null)
        {
            return JsonSerializer.Serialize(new
            {
                ok = true,
                found = false,
                entity_name = entityName,
                message = "Entity not found. Ask the user for a different spelling."
            }, JsonOptions);
        }

        var facts = await _factRepository.GetAllByEntityAsync(entity.Id, ct);
        var relationships = await _relationshipRepository.GetByEntityWithNamesAsync(entity.Id, ct);
        var events = await _communicationEventRepository.GetByEntityAsync(
            entity.Id,
            DateTime.UnixEpoch,
            DateTime.UtcNow.AddDays(1),
            ct);
        var payload = new
        {
            ok = true,
            found = true,
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

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private static class BotChatPromptBuilder
    {
        public static string BuildSystemPrompt(IReadOnlyCollection<Fact> facts)
        {
            var factsBlock = BuildFactsBlock(facts);
            return $"You are an AI personal assistant for Rinat. Use provided context facts and tool results to answer accurately. If the user asks for a dossier, profile, or full information about a person, you MUST call the get_entity_dossier tool. Do not try to answer from memory or context alone. If a requested entity is not found, say that clearly and suggest the closest known context. Be concise and direct. Context:\n{factsBlock}";
        }

        public static string BuildPostToolInstruction()
        {
            return "You just received the raw dossier from the database tool. You MUST list ALL facts, events, and relationships found in the tool result. Do not omit, truncate, or overly summarize them. Present a complete and detailed profile.";
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
    }
}
