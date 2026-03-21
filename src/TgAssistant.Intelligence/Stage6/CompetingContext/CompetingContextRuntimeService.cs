using System.Text.Json;
using Microsoft.Extensions.Logging;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage6.CompetingContext;

public class CompetingContextRuntimeService : ICompetingContextRuntimeService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IExternalArchiveIngestionRepository _externalArchiveRepository;
    private readonly ICompetingContextInterpretationService _interpretationService;
    private readonly IDomainReviewEventRepository _reviewEventRepository;
    private readonly IInboxConflictRepository _inboxConflictRepository;
    private readonly ILogger<CompetingContextRuntimeService> _logger;

    public CompetingContextRuntimeService(
        IExternalArchiveIngestionRepository externalArchiveRepository,
        ICompetingContextInterpretationService interpretationService,
        IDomainReviewEventRepository reviewEventRepository,
        IInboxConflictRepository inboxConflictRepository,
        ILogger<CompetingContextRuntimeService> logger)
    {
        _externalArchiveRepository = externalArchiveRepository;
        _interpretationService = interpretationService;
        _reviewEventRepository = reviewEventRepository;
        _inboxConflictRepository = inboxConflictRepository;
        _logger = logger;
    }

    public async Task<CompetingContextRuntimeResult> RunAsync(CompetingContextRuntimeRequest request, CancellationToken ct = default)
    {
        var persisted = await _externalArchiveRepository.GetRecentRecordsByCaseSourceAsync(
            request.CaseId,
            ExternalArchiveSourceClasses.CompetingRelationshipArchive,
            request.ChatId,
            request.AsOfUtc,
            limit: 400,
            ct);

        var mappedRecords = persisted
            .Select(x => MapRecord(request, x))
            .Where(x => x != null)
            .Cast<CompetingContextImportRecord>()
            .ToList();

        var interpretation = await _interpretationService.InterpretAsync(new CompetingContextInterpretationRequest
        {
            CaseId = request.CaseId,
            ChatId = request.ChatId,
            AsOfUtc = request.AsOfUtc,
            Actor = request.Actor,
            Records = mappedRecords
        }, ct);

        var runtimeResult = new CompetingContextRuntimeResult
        {
            GeneratedAtUtc = DateTime.UtcNow,
            IsAuthoritative = false,
            RequiresExplicitReview = interpretation.RequiresExplicitReview,
            SourceRecordIds = mappedRecords.Select(x => x.RecordId).Distinct(StringComparer.Ordinal).ToList(),
            Interpretation = interpretation,
            HasAnyEffect = HasAnyEffect(interpretation)
        };

        await PersistReviewTrailAsync(request, runtimeResult, ct);

        _logger.LogInformation(
            "Competing runtime integrated: case_id={CaseId}, chat_id={ChatId}, source_records={SourceRecords}, graph_hints={GraphHints}, timeline_hints={TimelineHints}, strategy_constraints={StrategyConstraints}, blocked={Blocked}, review_alerts={ReviewAlerts}",
            request.CaseId,
            request.ChatId,
            runtimeResult.SourceRecordIds.Count,
            interpretation.GraphHints.Count,
            interpretation.TimelineHints.Count,
            interpretation.StrategyConstraints.Count,
            interpretation.BlockedOverrideAttempts.Count,
            interpretation.ReviewAlerts.Count);

        return runtimeResult;
    }

    private async Task PersistReviewTrailAsync(
        CompetingContextRuntimeRequest request,
        CompetingContextRuntimeResult runtime,
        CancellationToken ct)
    {
        var interpretation = runtime.Interpretation;
        var scopeObjectId = $"{request.CaseId}:{request.ChatId}";
        var existingConflicts = await _inboxConflictRepository.GetConflictRecordsAsync(request.CaseId, null, ct);

        _ = await _reviewEventRepository.AddAsync(new DomainReviewEvent
        {
            ObjectType = "competing_context_runtime",
            ObjectId = scopeObjectId,
            Action = "competing_runtime_interpreted",
            NewValueRef = JsonSerializer.Serialize(new
            {
                is_authoritative = false,
                requires_review = runtime.RequiresExplicitReview,
                source_records = runtime.SourceRecordIds.Count,
                graph_hints = interpretation.GraphHints.Count,
                timeline_hints = interpretation.TimelineHints.Count,
                state_modifier_refs = interpretation.StateModifiers.RationaleRefs.Count,
                strategy_constraints = interpretation.StrategyConstraints.Count,
                blocked_override_attempts = interpretation.BlockedOverrideAttempts.Count
            }, JsonOptions),
            Reason = $"{request.SourceType}:{request.SourceId}",
            Actor = request.Actor,
            CreatedAt = DateTime.UtcNow
        }, ct);

        foreach (var hint in interpretation.GraphHints)
        {
            _ = await _reviewEventRepository.AddAsync(new DomainReviewEvent
            {
                ObjectType = "competing_graph_hint",
                ObjectId = hint.HintId,
                Action = "competing_graph_hint_recorded",
                NewValueRef = JsonSerializer.Serialize(new
                {
                    apply_mode = hint.ApplyMode,
                    confidence = hint.Confidence,
                    is_hypothesis = hint.IsHypothesis,
                    is_authoritative = false,
                    requires_review = true,
                    from_actor_key = hint.FromActorKey,
                    to_actor_key = hint.ToActorKey
                }, JsonOptions),
                Reason = "additive_graph_hint",
                Actor = request.Actor,
                CreatedAt = DateTime.UtcNow
            }, ct);
        }

        foreach (var hint in interpretation.TimelineHints)
        {
            _ = await _reviewEventRepository.AddAsync(new DomainReviewEvent
            {
                ObjectType = "competing_timeline_hint",
                ObjectId = hint.HintId,
                Action = "competing_timeline_annotation_recorded",
                NewValueRef = JsonSerializer.Serialize(new
                {
                    annotation_type = hint.AnnotationType,
                    summary = hint.Summary,
                    confidence = hint.Confidence,
                    requires_review = hint.RequiresReview,
                    allows_boundary_rewrite = hint.AllowsBoundaryRewrite,
                    is_authoritative = false
                }, JsonOptions),
                Reason = "additive_timeline_annotation",
                Actor = request.Actor,
                CreatedAt = DateTime.UtcNow
            }, ct);

            if (hint.RequiresReview)
            {
                await EnsureConflictAsync(
                    existingConflicts,
                    new ConflictRecord
                    {
                        ConflictType = "competing_timeline_review_required",
                        ObjectAType = "competing_timeline_hint",
                        ObjectAId = hint.HintId,
                        ObjectBType = "timeline",
                        ObjectBId = scopeObjectId,
                        Summary = $"Competing timeline annotation requires review: {hint.Summary}",
                        Severity = hint.Confidence >= 0.4f ? "high" : "medium",
                        Status = "open",
                        CaseId = request.CaseId,
                        ChatId = request.ChatId,
                        LastActor = request.Actor,
                        LastReason = "review_required"
                    },
                    ct);
            }
        }

        if (interpretation.StateModifiers.RationaleRefs.Count > 0)
        {
            var stateObjectId = BuildDeterministicObjectId("state", interpretation.StateModifiers.RationaleRefs);
            _ = await _reviewEventRepository.AddAsync(new DomainReviewEvent
            {
                ObjectType = "competing_state_modifier",
                ObjectId = stateObjectId,
                Action = "competing_state_modifier_recorded",
                NewValueRef = JsonSerializer.Serialize(new
                {
                    external_pressure_delta = interpretation.StateModifiers.ExternalPressureDelta,
                    ambiguity_delta = interpretation.StateModifiers.AmbiguityDelta,
                    confidence_cap = interpretation.StateModifiers.ConfidenceCap,
                    additive_only = interpretation.StateModifiers.IsAdditiveOnly,
                    requires_review = true,
                    is_authoritative = false,
                    rationale_refs = interpretation.StateModifiers.RationaleRefs
                }, JsonOptions),
                Reason = "bounded_state_modifier",
                Actor = request.Actor,
                CreatedAt = DateTime.UtcNow
            }, ct);

            if (interpretation.StateModifiers.ConfidenceCap < 0.86f)
            {
                await EnsureConflictAsync(
                    existingConflicts,
                    new ConflictRecord
                    {
                        ConflictType = "competing_state_review_required",
                        ObjectAType = "competing_state_modifier",
                        ObjectAId = stateObjectId,
                        ObjectBType = "state_snapshot",
                        ObjectBId = scopeObjectId,
                        Summary = "Competing state modifier introduced confidence cap and requires explicit review.",
                        Severity = interpretation.StateModifiers.ConfidenceCap < 0.8f ? "high" : "medium",
                        Status = "open",
                        CaseId = request.CaseId,
                        ChatId = request.ChatId,
                        LastActor = request.Actor,
                        LastReason = "review_required"
                    },
                    ct);
            }
        }

        foreach (var constraint in interpretation.StrategyConstraints)
        {
            _ = await _reviewEventRepository.AddAsync(new DomainReviewEvent
            {
                ObjectType = "competing_strategy_constraint",
                ObjectId = constraint.ConstraintId,
                Action = "competing_strategy_constraint_recorded",
                NewValueRef = JsonSerializer.Serialize(new
                {
                    constraint_type = constraint.ConstraintType,
                    severity = constraint.Severity,
                    summary = constraint.Summary,
                    requires_review = constraint.RequiresReview,
                    is_authoritative = false
                }, JsonOptions),
                Reason = "strategy_constraint",
                Actor = request.Actor,
                CreatedAt = DateTime.UtcNow
            }, ct);

            if (constraint.RequiresReview && IsHighImpactSeverity(constraint.Severity))
            {
                await EnsureConflictAsync(
                    existingConflicts,
                    new ConflictRecord
                    {
                        ConflictType = "competing_strategy_review_required",
                        ObjectAType = "competing_strategy_constraint",
                        ObjectAId = constraint.ConstraintId,
                        ObjectBType = "strategy",
                        ObjectBId = scopeObjectId,
                        Summary = $"High-impact competing strategy constraint requires review: {constraint.Summary}",
                        Severity = "high",
                        Status = "open",
                        CaseId = request.CaseId,
                        ChatId = request.ChatId,
                        LastActor = request.Actor,
                        LastReason = "review_required"
                    },
                    ct);
            }
        }

        foreach (var blocked in interpretation.BlockedOverrideAttempts)
        {
            _ = await _reviewEventRepository.AddAsync(new DomainReviewEvent
            {
                ObjectType = "competing_blocked_override",
                ObjectId = blocked.RecordId,
                Action = "competing_override_blocked",
                NewValueRef = JsonSerializer.Serialize(new
                {
                    blocked.AttemptedOperation,
                    blocked.ReasonBlocked,
                    blocked.RequiredReviewPath,
                    non_applied = true,
                    is_authoritative = false,
                    requires_review = true
                }, JsonOptions),
                Reason = "blocked_override_attempt",
                Actor = request.Actor,
                CreatedAt = DateTime.UtcNow
            }, ct);

            await EnsureConflictAsync(
                existingConflicts,
                new ConflictRecord
                {
                    ConflictType = "competing_override_blocked",
                    ObjectAType = "external_archive_record",
                    ObjectAId = blocked.RecordId,
                    ObjectBType = "stage6_runtime",
                    ObjectBId = scopeObjectId,
                    Summary = $"Blocked competing override attempt '{blocked.AttemptedOperation}' was recorded as non-applied artifact.",
                    Severity = "high",
                    Status = "open",
                    CaseId = request.CaseId,
                    ChatId = request.ChatId,
                    LastActor = request.Actor,
                    LastReason = blocked.ReasonBlocked
                },
                ct);
        }

        foreach (var alert in interpretation.ReviewAlerts)
        {
            _ = await _reviewEventRepository.AddAsync(new DomainReviewEvent
            {
                ObjectType = "competing_review_alert",
                ObjectId = alert.AlertId,
                Action = "competing_review_alert_recorded",
                NewValueRef = JsonSerializer.Serialize(new
                {
                    alert.Severity,
                    alert.Message,
                    alert.RelatedRecordIds,
                    requires_review = true,
                    is_authoritative = false
                }, JsonOptions),
                Reason = "review_alert",
                Actor = request.Actor,
                CreatedAt = DateTime.UtcNow
            }, ct);

            if (IsHighImpactSeverity(alert.Severity))
            {
                await EnsureConflictAsync(
                    existingConflicts,
                    new ConflictRecord
                    {
                        ConflictType = "competing_review_alert",
                        ObjectAType = "competing_review_alert",
                        ObjectAId = alert.AlertId,
                        ObjectBType = "stage6_runtime",
                        ObjectBId = scopeObjectId,
                        Summary = alert.Message,
                        Severity = "high",
                        Status = "open",
                        CaseId = request.CaseId,
                        ChatId = request.ChatId,
                        LastActor = request.Actor,
                        LastReason = "review_required"
                    },
                    ct);
            }
        }
    }

    private async Task EnsureConflictAsync(List<ConflictRecord> existing, ConflictRecord candidate, CancellationToken ct)
    {
        var alreadyExists = existing.Any(x =>
            string.Equals(x.ConflictType, candidate.ConflictType, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.ObjectAType, candidate.ObjectAType, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.ObjectAId, candidate.ObjectAId, StringComparison.Ordinal)
            && string.Equals(x.ObjectBType, candidate.ObjectBType, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.ObjectBId, candidate.ObjectBId, StringComparison.Ordinal));

        if (alreadyExists)
        {
            return;
        }

        var created = await _inboxConflictRepository.CreateConflictRecordAsync(candidate, ct);
        existing.Add(created);
    }

    private static bool HasAnyEffect(CompetingContextInterpretationResult interpretation)
    {
        return interpretation.GraphHints.Count > 0
            || interpretation.TimelineHints.Count > 0
            || interpretation.StateModifiers.RationaleRefs.Count > 0
            || interpretation.StrategyConstraints.Count > 0
            || interpretation.BlockedOverrideAttempts.Count > 0;
    }

    private static bool IsHighImpactSeverity(string? severity)
    {
        return string.Equals(severity, "high", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildDeterministicObjectId(string prefix, IReadOnlyCollection<string> refs)
    {
        if (refs.Count == 0)
        {
            return $"{prefix}:none";
        }

        var stable = string.Join('|', refs.OrderBy(x => x, StringComparer.Ordinal));
        var hash = ExternalArchiveHashing.ComputeSha256(stable)[..16];
        return $"{prefix}:{hash}";
    }

    private static CompetingContextImportRecord? MapRecord(CompetingContextRuntimeRequest request, ExternalArchivePersistedRecord record)
    {
        var payload = ParsePayload(record.RawPayloadJson);
        var signalType = GetPayloadString(payload, "signal_type");
        if (string.IsNullOrWhiteSpace(signalType))
        {
            signalType = record.RecordType.Equals(ExternalArchiveRecordTypes.RelationshipSignal, StringComparison.OrdinalIgnoreCase)
                ? "state"
                : "timeline";
        }

        var evidenceRefs = ParseEvidenceRefs(record.EvidenceRefsJson);
        var payloadEvidenceRefs = GetPayloadStringArray(payload, "evidence_refs");
        foreach (var evidenceRef in payloadEvidenceRefs)
        {
            if (!evidenceRefs.Contains(evidenceRef, StringComparer.Ordinal))
            {
                evidenceRefs.Add(evidenceRef);
            }
        }

        var chatId = record.ChatId ?? request.ChatId;
        var metadata = GetPayloadStringMap(payload, "metadata");

        return new CompetingContextImportRecord
        {
            RecordId = record.RecordId,
            CaseId = request.CaseId,
            ChatId = chatId,
            SourceType = GetPayloadString(payload, "source_type") ?? record.SourceClass,
            SourceId = GetPayloadString(payload, "source_id") ?? $"{record.SourceClass}:{record.RecordId}",
            ObservedAtUtc = GetPayloadDateTime(payload, "observed_at_utc") ?? record.OccurredAtUtc,
            SubjectActorKey = GetPayloadString(payload, "subject_actor_key") ?? record.SubjectActorKey ?? $"{chatId}:unknown_subject",
            CompetingActorKey = GetPayloadString(payload, "competing_actor_key") ?? record.TargetActorKey ?? $"{chatId}:unknown_competing",
            SignalType = signalType,
            SignalSubtype = GetPayloadString(payload, "signal_subtype") ?? "context_signal",
            Confidence = GetPayloadSingle(payload, "confidence") ?? record.Confidence,
            EvidenceRefs = evidenceRefs,
            Metadata = metadata
        };
    }

    private static JsonElement? ParsePayload(string rawPayloadJson)
    {
        if (string.IsNullOrWhiteSpace(rawPayloadJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(rawPayloadJson);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static List<string> ParseEvidenceRefs(string rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(rawJson);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return document.RootElement.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => x.GetString()?.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Cast<string>()
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string? GetPayloadString(JsonElement? payload, string propertyName)
    {
        if (payload == null || payload.Value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!payload.Value.TryGetProperty(propertyName, out var node) || node.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var value = node.GetString()?.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static DateTime? GetPayloadDateTime(JsonElement? payload, string propertyName)
    {
        var raw = GetPayloadString(payload, propertyName);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return DateTime.TryParse(raw, out var parsed) ? parsed : null;
    }

    private static float? GetPayloadSingle(JsonElement? payload, string propertyName)
    {
        if (payload == null || payload.Value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!payload.Value.TryGetProperty(propertyName, out var node))
        {
            return null;
        }

        if (node.ValueKind == JsonValueKind.Number && node.TryGetSingle(out var numeric))
        {
            return Math.Clamp(numeric, 0f, 1f);
        }

        if (node.ValueKind == JsonValueKind.String && float.TryParse(node.GetString(), out var textual))
        {
            return Math.Clamp(textual, 0f, 1f);
        }

        return null;
    }

    private static List<string> GetPayloadStringArray(JsonElement? payload, string propertyName)
    {
        if (payload == null || payload.Value.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        if (!payload.Value.TryGetProperty(propertyName, out var node) || node.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return node.EnumerateArray()
            .Where(x => x.ValueKind == JsonValueKind.String)
            .Select(x => x.GetString()?.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static Dictionary<string, string> GetPayloadStringMap(JsonElement? payload, string propertyName)
    {
        if (payload == null || payload.Value.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        if (!payload.Value.TryGetProperty(propertyName, out var node) || node.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var child in node.EnumerateObject())
        {
            if (child.Value.ValueKind == JsonValueKind.String)
            {
                var value = child.Value.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    map[child.Name] = value;
                }
            }
            else
            {
                map[child.Name] = child.Value.ToString();
            }
        }

        return map;
    }
}
