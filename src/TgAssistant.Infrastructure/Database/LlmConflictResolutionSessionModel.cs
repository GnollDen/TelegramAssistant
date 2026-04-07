using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Infrastructure.Database;

public sealed class LlmConflictResolutionSessionModel : IConflictResolutionSessionModel
{
    private const string DefaultTaskKey = "ai_conflict_resolution_session_v1";
    private const string SchemaName = "ai_conflict_resolution_session_v1";
    private const string SchemaJson = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "ask_follow_up_question": { "type": "boolean" },
            "follow_up_question": {
              "anyOf": [
                { "type": "null" },
                {
                  "type": "object",
                  "additionalProperties": false,
                  "properties": {
                    "question_key": { "type": "string" },
                    "question_text": { "type": "string" },
                    "answer_kind": { "type": "string" },
                    "notes": { "type": ["string", "null"] }
                  },
                  "required": ["question_key", "question_text", "answer_kind", "notes"]
                }
              ]
            },
            "tool_requests": {
              "type": "array",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "properties": {
                  "tool_name": { "type": "string" },
                  "request_scope": { "type": "string" },
                  "request_items": { "type": "array", "items": { "type": "string" } }
                },
                "required": ["tool_name", "request_scope", "request_items"]
              }
            },
            "resolution_verdict": {
              "type": "string",
              "enum": ["ready_for_commit", "needs_web_review", "insufficient_context"]
            },
            "resolved_claims": {
              "type": "array",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "properties": {
                  "claim_type": { "type": "string", "enum": ["fact", "inference", "hypothesis"] },
                  "summary": { "type": "string" },
                  "evidence_refs": { "type": "array", "items": { "type": "string" } },
                  "operator_input_refs": { "type": "array", "items": { "type": "string" } }
                },
                "required": ["claim_type", "summary", "evidence_refs", "operator_input_refs"]
              }
            },
            "rejected_claims": {
              "type": "array",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "properties": {
                  "summary": { "type": "string" },
                  "reason": { "type": "string" }
                },
                "required": ["summary", "reason"]
              }
            },
            "evidence_refs_used": { "type": "array", "items": { "type": "string" } },
            "operator_inputs_used": { "type": "array", "items": { "type": "string" } },
            "remaining_uncertainties": { "type": "array", "items": { "type": "string" } },
            "normalization_proposal": {
              "type": "object",
              "additionalProperties": false,
              "properties": {
                "recommended_action": { "type": "string", "enum": ["approve", "reject", "defer", "clarify"] },
                "explanation": { "type": "string" },
                "clarification_payload": { "type": ["object", "null"] }
              },
              "required": ["recommended_action", "explanation", "clarification_payload"]
            },
            "confidence_calibration": {
              "type": "object",
              "additionalProperties": false,
              "properties": {
                "confidence_score": { "type": "number" },
                "rationale": { "type": "string" }
              },
              "required": ["confidence_score", "rationale"]
            }
          },
          "required": [
            "ask_follow_up_question",
            "follow_up_question",
            "tool_requests",
            "resolution_verdict",
            "resolved_claims",
            "rejected_claims",
            "evidence_refs_used",
            "operator_inputs_used",
            "remaining_uncertainties",
            "normalization_proposal",
            "confidence_calibration"
          ]
        }
        """;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private readonly ILlmGateway _gateway;
    private readonly ILogger<LlmConflictResolutionSessionModel> _logger;
    private readonly string _taskKey;
    private readonly int _maxOutputTokens;
    private readonly int _requestTimeoutMs;

    public LlmConflictResolutionSessionModel(
        ILlmGateway gateway,
        IOptions<ConflictResolutionSessionSettings> settings,
        ILogger<LlmConflictResolutionSessionModel> logger)
    {
        _gateway = gateway;
        _logger = logger;
        var resolvedSettings = settings.Value ?? new ConflictResolutionSessionSettings();
        _taskKey = string.IsNullOrWhiteSpace(resolvedSettings.ModelTaskKey)
            ? DefaultTaskKey
            : resolvedSettings.ModelTaskKey.Trim();
        _maxOutputTokens = Math.Max(1, resolvedSettings.MaxOutputTokens);
        _requestTimeoutMs = Math.Max(1000, resolvedSettings.ModelTimeoutMs);
    }

    public async Task<ResolutionConflictSessionModelResult> ResolveAsync(
        ResolutionConflictSessionModelRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var response = await _gateway.ExecuteAsync(BuildGatewayRequest(request), ct);
        var payload = response.Output.StructuredPayloadJson ?? response.Output.Text;
        if (string.IsNullOrWhiteSpace(payload))
        {
            throw new InvalidOperationException("AI conflict resolution session model returned an empty payload.");
        }

        var parsed = JsonSerializer.Deserialize<ModelPayload>(payload, SerializerOptions)
            ?? throw new InvalidOperationException("AI conflict resolution session payload could not be deserialized.");

        var result = new ResolutionConflictSessionModelResult
        {
            AskFollowUpQuestion = parsed.AskFollowUpQuestion,
            FollowUpQuestion = parsed.FollowUpQuestion == null
                ? null
                : new ResolutionConflictSessionQuestion
                {
                    QuestionKey = parsed.FollowUpQuestion.QuestionKey ?? "q1",
                    QuestionText = parsed.FollowUpQuestion.QuestionText ?? string.Empty,
                    AnswerKind = string.IsNullOrWhiteSpace(parsed.FollowUpQuestion.AnswerKind) ? "free_text" : parsed.FollowUpQuestion.AnswerKind.Trim(),
                    Notes = parsed.FollowUpQuestion.Notes
                },
            ToolRequests = (parsed.ToolRequests ?? [])
                .Select(request => new ResolutionConflictSessionToolRequest
                {
                    ToolName = request.ToolName ?? string.Empty,
                    RequestScope = request.RequestScope ?? string.Empty,
                    RequestItems = request.RequestItems ?? []
                })
                .ToList(),
            FinalVerdict = BuildVerdict(parsed),
            Provider = response.Provider,
            Model = response.Model,
            ModelVersion = null,
            RequestId = response.RequestId,
            LatencyMs = response.LatencyMs,
            PromptTokens = response.Usage.PromptTokens,
            CompletionTokens = response.Usage.CompletionTokens,
            TotalTokens = response.Usage.TotalTokens,
            CostUsd = response.Usage.CostUsd
        };

        _logger.LogInformation(
            "AI conflict resolution session model call completed: stage={Stage}, scope={ScopeKey}, scope_item_key={ScopeItemKey}, provider={Provider}, model={Model}, latency_ms={LatencyMs}, total_tokens={TotalTokens}",
            request.Stage,
            request.ScopeKey,
            request.ScopeItemKey,
            result.Provider,
            result.Model,
            result.LatencyMs,
            result.TotalTokens ?? 0);

        return result;
    }

    private static ResolutionConflictSessionVerdict BuildVerdict(ModelPayload payload)
    {
        var normalizedVerdict = payload.ResolutionVerdict switch
        {
            "ready_for_commit" => ResolutionConflictSessionStates.ReadyForCommit,
            "insufficient_context" => ResolutionConflictSessionStates.Fallback,
            _ => ResolutionConflictSessionStates.NeedsWebReview
        };

        return new ResolutionConflictSessionVerdict
        {
            ResolutionVerdict = normalizedVerdict,
            ResolvedClaims = payload.ResolvedClaims
                .Select(x => new ResolutionConflictSessionClaim
                {
                    ClaimType = ResolutionInterpretationClaimTypes.Normalize(x.ClaimType),
                    Summary = x.Summary ?? string.Empty,
                    EvidenceRefs = x.EvidenceRefs ?? [],
                    OperatorInputRefs = x.OperatorInputRefs ?? []
                })
                .ToList(),
            RejectedClaims = payload.RejectedClaims
                .Select(x => new ResolutionConflictSessionRejectedClaim
                {
                    Summary = x.Summary ?? string.Empty,
                    Reason = x.Reason ?? string.Empty
                })
                .ToList(),
            EvidenceRefsUsed = payload.EvidenceRefsUsed ?? [],
            OperatorInputsUsed = payload.OperatorInputsUsed ?? [],
            RemainingUncertainties = payload.RemainingUncertainties ?? [],
            NormalizationProposal = new ResolutionConflictNormalizationProposal
            {
                RecommendedAction = string.IsNullOrWhiteSpace(payload.NormalizationProposal?.RecommendedAction)
                    ? ResolutionActionTypes.Clarify
                    : payload.NormalizationProposal.RecommendedAction.Trim().ToLowerInvariant(),
                Explanation = payload.NormalizationProposal?.Explanation ?? string.Empty,
                ClarificationPayload = payload.NormalizationProposal?.ClarificationPayload
            },
            ConfidenceCalibration = new ResolutionConflictConfidenceCalibration
            {
                ConfidenceScore = payload.ConfidenceCalibration?.ConfidenceScore ?? 0f,
                Rationale = payload.ConfidenceCalibration?.Rationale ?? string.Empty
            }
        };
    }

    private LlmGatewayRequest BuildGatewayRequest(ResolutionConflictSessionModelRequest request)
    {
        var payload = new
        {
            stage = request.Stage,
            scope_key = request.ScopeKey,
            tracked_person_id = request.TrackedPersonId,
            scope_item_key = request.ScopeItemKey,
            case_packet = request.CasePacket,
            operator_question = request.OperatorQuestion,
            operator_input = request.OperatorInput,
            tool_responses = request.ToolResponses,
            allowed_tools = ConflictResolutionSessionToolNames.Allowed
        };

        return new LlmGatewayRequest
        {
            Modality = LlmModality.TextChat,
            TaskKey = _taskKey,
            ResponseMode = LlmResponseMode.JsonObject,
            StructuredOutputSchema = new LlmStructuredOutputSchema
            {
                Name = SchemaName,
                SchemaJson = SchemaJson,
                Strict = true
            },
            Limits = new LlmExecutionLimits
            {
                MaxTokens = _maxOutputTokens,
                Temperature = 0.1f,
                TimeoutMs = _requestTimeoutMs
            },
            Trace = new LlmTraceContext
            {
                PathKey = _taskKey,
                ScopeTags = [request.ScopeKey, request.ScopeItemKey, request.Stage],
                IsOptionalPath = true
            },
            Messages =
            [
                LlmGatewayMessage.FromText(
                    LlmMessageRole.System,
                    """
                    You are AI Conflict Resolution Session V1.
                    Work only with provided bounded case data.
                    Ask at most one follow-up question.
                    If stage is final, do not ask follow-up question.
                    If more context is needed, emit tool_requests only from allowed_tools.
                    Every tool request must use request_scope exactly equal to scope_item_key.
                    If evidence remains insufficient, return needs_web_review or insufficient_context with explicit uncertainties.
                    Never propose actions outside approve/reject/defer/clarify.
                    """),
                LlmGatewayMessage.FromText(
                    LlmMessageRole.User,
                    JsonSerializer.Serialize(payload, SerializerOptions))
            ]
        };
    }

    private sealed class ModelPayload
    {
        public bool AskFollowUpQuestion { get; set; }
        public ModelQuestion? FollowUpQuestion { get; set; }
        public List<ModelToolRequest>? ToolRequests { get; set; }
        public string ResolutionVerdict { get; set; } = "needs_web_review";
        public List<ModelClaim> ResolvedClaims { get; set; } = [];
        public List<ModelRejectedClaim> RejectedClaims { get; set; } = [];
        public List<string>? EvidenceRefsUsed { get; set; }
        public List<string>? OperatorInputsUsed { get; set; }
        public List<string>? RemainingUncertainties { get; set; }
        public ModelNormalizationProposal? NormalizationProposal { get; set; }
        public ModelConfidenceCalibration? ConfidenceCalibration { get; set; }
    }

    private sealed class ModelQuestion
    {
        public string? QuestionKey { get; set; }
        public string? QuestionText { get; set; }
        public string? AnswerKind { get; set; }
        public string? Notes { get; set; }
    }

    private sealed class ModelToolRequest
    {
        public string? ToolName { get; set; }
        public string? RequestScope { get; set; }
        public List<string>? RequestItems { get; set; }
    }

    private sealed class ModelClaim
    {
        public string? ClaimType { get; set; }
        public string? Summary { get; set; }
        public List<string>? EvidenceRefs { get; set; }
        public List<string>? OperatorInputRefs { get; set; }
    }

    private sealed class ModelRejectedClaim
    {
        public string? Summary { get; set; }
        public string? Reason { get; set; }
    }

    private sealed class ModelNormalizationProposal
    {
        public string? RecommendedAction { get; set; }
        public string? Explanation { get; set; }
        public ResolutionClarificationPayload? ClarificationPayload { get; set; }
    }

    private sealed class ModelConfidenceCalibration
    {
        public float ConfidenceScore { get; set; }
        public string? Rationale { get; set; }
    }
}
