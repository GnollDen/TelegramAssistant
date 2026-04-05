using System.Text.Json;
using Microsoft.Extensions.Logging;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Infrastructure.Database;

public sealed class LlmResolutionInterpretationModel : IResolutionInterpretationModel
{
    private const string TaskKey = "resolution_interpretation_loop_v1";
    private const string SchemaName = "resolution_interpretation_loop_v1";
    private const string SchemaJson = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "context_sufficient": { "type": "boolean" },
            "requested_context_type": {
              "type": "string",
              "enum": ["none", "additional_evidence", "durable_context"]
            },
            "interpretation_summary": { "type": "string" },
            "key_claims": {
              "type": "array",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "properties": {
                  "claim_type": {
                    "type": "string",
                    "enum": ["fact", "inference", "hypothesis"]
                  },
                  "summary": { "type": "string" },
                  "evidence_refs": {
                    "type": "array",
                    "items": { "type": "string" }
                  }
                },
                "required": ["claim_type", "summary", "evidence_refs"]
              }
            },
            "explicit_uncertainties": {
              "type": "array",
              "items": { "type": "string" }
            },
            "review_recommendation": {
              "type": "object",
              "additionalProperties": false,
              "properties": {
                "decision": {
                  "type": "string",
                  "enum": ["review", "no_review"]
                },
                "reason": { "type": "string" }
              },
              "required": ["decision", "reason"]
            },
            "evidence_refs_used": {
              "type": "array",
              "items": { "type": "string" }
            }
          },
          "required": [
            "context_sufficient",
            "requested_context_type",
            "interpretation_summary",
            "key_claims",
            "explicit_uncertainties",
            "review_recommendation",
            "evidence_refs_used"
          ]
        }
        """;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private readonly ILlmGateway _gateway;
    private readonly ILogger<LlmResolutionInterpretationModel> _logger;

    public LlmResolutionInterpretationModel(
        ILlmGateway gateway,
        ILogger<LlmResolutionInterpretationModel> logger)
    {
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<ResolutionInterpretationModelResponse> InterpretAsync(
        ResolutionInterpretationModelRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var response = await _gateway.ExecuteAsync(BuildGatewayRequest(request), ct);
        var payload = response.Output.StructuredPayloadJson ?? response.Output.Text;
        if (string.IsNullOrWhiteSpace(payload))
        {
            throw new InvalidOperationException("Resolution interpretation model returned an empty payload.");
        }

        var interpretation = JsonSerializer.Deserialize<ResolutionInterpretationLoopResult>(payload, SerializerOptions)
            ?? throw new InvalidOperationException("Resolution interpretation model payload could not be deserialized.");

        _logger.LogInformation(
            "Resolution interpretation loop model call completed: scope={ScopeKey}, scope_item_key={ScopeItemKey}, retrieval_round={RetrievalRound}, provider={Provider}, model={Model}, latency_ms={LatencyMs}, total_tokens={TotalTokens}",
            request.ScopeKey,
            request.ScopeItemKey,
            request.RetrievalRound,
            response.Provider,
            response.Model,
            response.LatencyMs,
            response.Usage.TotalTokens);

        return new ResolutionInterpretationModelResponse
        {
            Interpretation = interpretation,
            Provider = response.Provider,
            Model = response.Model,
            RequestId = response.RequestId,
            LatencyMs = response.LatencyMs,
            TotalTokens = response.Usage.TotalTokens ?? 0
        };
    }

    private static LlmGatewayRequest BuildGatewayRequest(ResolutionInterpretationModelRequest request)
    {
        var payload = new
        {
            scope_key = request.ScopeKey,
            scope_item_key = request.ScopeItemKey,
            retrieval_round = request.RetrievalRound,
            allowed_context_types = request.AllowedContextTypes,
            item = new
            {
                scope_item_key = request.Context.Item.ScopeItemKey,
                item_type = request.Context.Item.ItemType,
                title = request.Context.Item.Title,
                summary = request.Context.Item.Summary,
                why_it_matters = request.Context.Item.WhyItMatters,
                status = request.Context.Item.Status,
                priority = request.Context.Item.Priority,
                recommended_next_action = request.Context.Item.RecommendedNextAction,
                affected_family = request.Context.Item.AffectedFamily,
                affected_object_ref = request.Context.Item.AffectedObjectRef,
                trust_factor = request.Context.Item.TrustFactor
            },
            source = new
            {
                kind = request.Context.SourceKind,
                reference = request.Context.SourceRef,
                required_action = request.Context.RequiredAction
            },
            notes = request.Context.Notes.Select(x => new
            {
                kind = x.Kind,
                text = x.Text
            }),
            evidence = request.Context.Evidence.Select(BuildEvidencePayload),
            durable_context_summaries = request.Context.DurableContextSummaries,
            additional_context = new
            {
                evidence = request.AdditionalContext.Evidence.Select(BuildEvidencePayload),
                durable_context_summaries = request.AdditionalContext.DurableContextSummaries
            }
        };

        return new LlmGatewayRequest
        {
            Modality = LlmModality.TextChat,
            TaskKey = TaskKey,
            ResponseMode = LlmResponseMode.JsonObject,
            StructuredOutputSchema = new LlmStructuredOutputSchema
            {
                Name = SchemaName,
                SchemaJson = SchemaJson,
                Strict = true
            },
            Limits = new LlmExecutionLimits
            {
                MaxTokens = 800,
                Temperature = 0.1f,
                TimeoutMs = 15000
            },
            Trace = new LlmTraceContext
            {
                PathKey = TaskKey,
                ScopeTags = [request.ScopeKey, request.ScopeItemKey],
                IsOptionalPath = true
            },
            Messages =
            [
                LlmGatewayMessage.FromText(
                    LlmMessageRole.System,
                    """
                    You are ResolutionInterpretationLoopV1.
                    Stay strictly within the provided bounded scope and evidence.
                    If the context is not sufficient, set context_sufficient=false and requested_context_type to one of: none, additional_evidence, durable_context.
                    Never invent evidence refs. Every key claim must cite evidence_refs or be omitted and reflected in explicit_uncertainties.
                    review_recommendation.decision must be either review or no_review.
                    """),
                LlmGatewayMessage.FromText(
                    LlmMessageRole.User,
                    JsonSerializer.Serialize(payload, SerializerOptions))
            ]
        };
    }

    private static object BuildEvidencePayload(ResolutionEvidenceSummary evidence)
    {
        return new
        {
            evidence_ref = string.IsNullOrWhiteSpace(evidence.SourceRef)
                ? $"evidence:{evidence.EvidenceItemId:D}"
                : evidence.SourceRef.Trim(),
            summary = evidence.Summary,
            trust_factor = evidence.TrustFactor,
            observed_at_utc = evidence.ObservedAtUtc,
            sender_display = evidence.SenderDisplay,
            source_label = evidence.SourceLabel,
            relevance_hint = evidence.RelevanceHint
        };
    }
}
