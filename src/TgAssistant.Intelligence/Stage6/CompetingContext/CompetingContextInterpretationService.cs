// Legacy Stage6 surface retained for reference and diagnostics only. Not part of the PRD target architecture.

using Microsoft.Extensions.Logging;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage6.CompetingContext;

public class CompetingContextInterpretationService : ICompetingContextInterpretationService
{
    private static readonly HashSet<string> SupportedSignalTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "graph",
        "timeline",
        "state",
        "strategy"
    };

    private static readonly HashSet<string> BlockedOperationHints = new(StringComparer.OrdinalIgnoreCase)
    {
        "status_override",
        "period_rewrite",
        "edge_delete",
        "strategy_force_primary"
    };

    private readonly ILogger<CompetingContextInterpretationService> _logger;

    public CompetingContextInterpretationService(ILogger<CompetingContextInterpretationService> logger)
    {
        _logger = logger;
    }

    public CompetingContextImportValidationResult Validate(CompetingContextInterpretationRequest request)
    {
        var result = new CompetingContextImportValidationResult();

        foreach (var record in request.Records)
        {
            if (record.CaseId != request.CaseId)
            {
                Reject(result, record, "case_id_mismatch");
                continue;
            }

            if (record.ChatId.HasValue && record.ChatId.Value != request.ChatId)
            {
                Reject(result, record, "chat_id_mismatch");
                continue;
            }

            if (string.IsNullOrWhiteSpace(record.RecordId))
            {
                Reject(result, record, "missing_record_id");
                continue;
            }

            if (!SupportedSignalTypes.Contains(record.SignalType))
            {
                Reject(result, record, "unsupported_signal_type");
                continue;
            }

            if (string.IsNullOrWhiteSpace(record.SourceId) && record.EvidenceRefs.Count == 0)
            {
                Reject(result, record, "missing_provenance_anchor");
                continue;
            }

            if (record.Confidence <= 0f)
            {
                Reject(result, record, "non_positive_confidence");
                continue;
            }

            result.Accepted.Add(record);
        }

        return result;
    }

    public Task<CompetingContextInterpretationResult> InterpretAsync(CompetingContextInterpretationRequest request, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var validation = Validate(request);
        var interpreted = new CompetingContextInterpretationResult
        {
            GeneratedAtUtc = DateTime.UtcNow,
            IsAuthoritative = false,
            RequiresExplicitReview = true
        };

        foreach (var accepted in validation.Accepted)
        {
            ct.ThrowIfCancellationRequested();

            var effectiveConfidence = ClampEffectiveConfidence(accepted.Confidence);

            if (TryBlockOverrideAttempt(accepted, interpreted))
            {
                continue;
            }

            if (accepted.SignalType.Equals("graph", StringComparison.OrdinalIgnoreCase))
            {
                interpreted.GraphHints.Add(BuildGraphHint(accepted, effectiveConfidence));
                continue;
            }

            if (accepted.SignalType.Equals("timeline", StringComparison.OrdinalIgnoreCase))
            {
                interpreted.TimelineHints.Add(BuildTimelineHint(accepted, effectiveConfidence));
                continue;
            }

            if (accepted.SignalType.Equals("state", StringComparison.OrdinalIgnoreCase))
            {
                ApplyStateModifier(interpreted.StateModifiers, accepted, effectiveConfidence);
                continue;
            }

            if (accepted.SignalType.Equals("strategy", StringComparison.OrdinalIgnoreCase))
            {
                interpreted.StrategyConstraints.Add(BuildStrategyConstraint(accepted, effectiveConfidence));
            }
        }

        foreach (var rejected in validation.Rejected)
        {
            interpreted.ReviewAlerts.Add(new CompetingReviewAlert
            {
                AlertId = $"reject:{rejected.Record.RecordId}",
                Severity = "medium",
                Message = $"Competing-context import rejected: {rejected.Reason}",
                RelatedRecordIds = [rejected.Record.RecordId]
            });
        }

        if (interpreted.GraphHints.Count == 0
            && interpreted.TimelineHints.Count == 0
            && interpreted.StateModifiers.RationaleRefs.Count == 0
            && interpreted.StrategyConstraints.Count == 0)
        {
            interpreted.ReviewAlerts.Add(new CompetingReviewAlert
            {
                AlertId = "competing:no_effect",
                Severity = "low",
                Message = "No additive competing-context impact was produced.",
                RelatedRecordIds = []
            });
        }

        _logger.LogInformation(
            "Competing context interpreted: case_id={CaseId}, chat_id={ChatId}, accepted={AcceptedCount}, rejected={RejectedCount}, graph_hints={GraphHints}, timeline_hints={TimelineHints}, strategy_constraints={StrategyConstraints}, blocked={BlockedCount}",
            request.CaseId,
            request.ChatId,
            validation.Accepted.Count,
            validation.Rejected.Count,
            interpreted.GraphHints.Count,
            interpreted.TimelineHints.Count,
            interpreted.StrategyConstraints.Count,
            interpreted.BlockedOverrideAttempts.Count);

        return Task.FromResult(interpreted);
    }

    private static void Reject(CompetingContextImportValidationResult result, CompetingContextImportRecord record, string reason)
    {
        result.Rejected.Add(new CompetingImportRejection
        {
            Record = record,
            Reason = reason
        });
    }

    private static float ClampEffectiveConfidence(float sourceConfidence)
    {
        var bounded = Math.Clamp(sourceConfidence, 0f, 1f);
        return Math.Min(0.49f, bounded);
    }

    private static bool TryBlockOverrideAttempt(CompetingContextImportRecord record, CompetingContextInterpretationResult result)
    {
        if (!BlockedOperationHints.Contains(record.SignalSubtype))
        {
            return false;
        }

        result.BlockedOverrideAttempts.Add(new CompetingBlockedOverride
        {
            RecordId = record.RecordId,
            AttemptedOperation = record.SignalSubtype,
            ReasonBlocked = "non_authoritative_competing_context",
            RequiredReviewPath = "manual_review"
        });

        result.ReviewAlerts.Add(new CompetingReviewAlert
        {
            AlertId = $"blocked:{record.RecordId}",
            Severity = "high",
            Message = $"Blocked competing-context override attempt: {record.SignalSubtype}",
            RelatedRecordIds = [record.RecordId]
        });

        return true;
    }

    private static CompetingGraphHint BuildGraphHint(CompetingContextImportRecord record, float effectiveConfidence)
    {
        return new CompetingGraphHint
        {
            HintId = $"graph:{record.RecordId}",
            FromActorKey = record.SubjectActorKey,
            ToActorKey = record.CompetingActorKey,
            InfluenceType = string.IsNullOrWhiteSpace(record.SignalSubtype) ? "complicating" : record.SignalSubtype,
            Confidence = effectiveConfidence,
            IsHypothesis = true,
            ApplyMode = "additive_hint",
            EvidenceRefs = record.EvidenceRefs.ToList()
        };
    }

    private static CompetingTimelineHint BuildTimelineHint(CompetingContextImportRecord record, float effectiveConfidence)
    {
        return new CompetingTimelineHint
        {
            HintId = $"timeline:{record.RecordId}",
            EffectiveAtUtc = record.ObservedAtUtc,
            AnnotationType = "competing_context",
            Summary = BuildTimelineSummary(record),
            Confidence = effectiveConfidence,
            RequiresReview = true,
            AllowsBoundaryRewrite = false,
            EvidenceRefs = record.EvidenceRefs.ToList()
        };
    }

    private static string BuildTimelineSummary(CompetingContextImportRecord record)
    {
        var subtype = string.IsNullOrWhiteSpace(record.SignalSubtype) ? "signal" : record.SignalSubtype;
        return $"Competing-context annotation ({subtype}) for {record.SubjectActorKey}.";
    }

    private static void ApplyStateModifier(CompetingStateModifiers state, CompetingContextImportRecord record, float effectiveConfidence)
    {
        var pressureDelta = effectiveConfidence * 0.3f;
        var ambiguityDelta = effectiveConfidence * 0.2f;

        state.ExternalPressureDelta = Math.Clamp(state.ExternalPressureDelta + pressureDelta, 0f, 0.3f);
        state.AmbiguityDelta = Math.Clamp(state.AmbiguityDelta + ambiguityDelta, 0f, 0.25f);

        var confidenceCap = 0.9f - (effectiveConfidence * 0.25f);
        state.ConfidenceCap = Math.Clamp(Math.Min(state.ConfidenceCap, confidenceCap), 0.65f, 1f);
        state.IsAdditiveOnly = true;

        if (!string.IsNullOrWhiteSpace(record.RecordId))
        {
            state.RationaleRefs.Add(record.RecordId);
        }
    }

    private static CompetingStrategyConstraint BuildStrategyConstraint(CompetingContextImportRecord record, float effectiveConfidence)
    {
        var constraintType = effectiveConfidence >= 0.35f
            ? "defer_escalation"
            : "pace_guard";

        return new CompetingStrategyConstraint
        {
            ConstraintId = $"strategy:{record.RecordId}",
            ConstraintType = constraintType,
            Severity = effectiveConfidence >= 0.35f ? "high" : "medium",
            Summary = "Competing-context pressure detected; keep safer alternatives and avoid forced escalation.",
            RequiresReview = true,
            EvidenceRefs = record.EvidenceRefs.ToList()
        };
    }
}
