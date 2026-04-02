using TgAssistant.Core.Models;

namespace TgAssistant.Infrastructure.Database;

public static class ModelPassEnvelopeValidator
{
    public static void Validate(ModelPassEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(envelope.Scope);
        ArgumentNullException.ThrowIfNull(envelope.Target);
        ArgumentNullException.ThrowIfNull(envelope.TruthSummary);
        ArgumentNullException.ThrowIfNull(envelope.OutputSummary);

        if (envelope.SchemaVersion <= 0)
        {
            throw new InvalidOperationException("Model pass envelope schema_version must be positive.");
        }

        if (string.IsNullOrWhiteSpace(envelope.Stage)
            || string.IsNullOrWhiteSpace(envelope.PassFamily)
            || string.IsNullOrWhiteSpace(envelope.RunKind)
            || string.IsNullOrWhiteSpace(envelope.ScopeKey))
        {
            throw new InvalidOperationException("Model pass envelope requires non-empty stage, pass_family, run_kind, and scope_key.");
        }

        if (string.IsNullOrWhiteSpace(envelope.Scope.ScopeType)
            || string.IsNullOrWhiteSpace(envelope.Scope.ScopeRef))
        {
            throw new InvalidOperationException("Model pass envelope requires explicit scope_type and scope_ref.");
        }

        if (string.IsNullOrWhiteSpace(envelope.Target.TargetType)
            || string.IsNullOrWhiteSpace(envelope.Target.TargetRef))
        {
            throw new InvalidOperationException("Model pass envelope requires explicit target_type and target_ref.");
        }

        if (!ModelPassResultStatuses.All.Contains(envelope.ResultStatus, StringComparer.Ordinal))
        {
            throw new InvalidOperationException($"Unsupported model pass result_status '{envelope.ResultStatus}'.");
        }

        foreach (var sourceRef in envelope.SourceRefs)
        {
            if (sourceRef == null
                || string.IsNullOrWhiteSpace(sourceRef.SourceType)
                || string.IsNullOrWhiteSpace(sourceRef.SourceRef))
            {
                throw new InvalidOperationException("Model pass envelope source_refs must contain non-empty source_type and source_ref.");
            }
        }

        foreach (var conflict in envelope.Conflicts)
        {
            if (conflict == null
                || string.IsNullOrWhiteSpace(conflict.ConflictType)
                || string.IsNullOrWhiteSpace(conflict.Summary))
            {
                throw new InvalidOperationException("Model pass envelope conflicts must contain non-empty conflict_type and summary.");
            }
        }

        foreach (var unknown in envelope.Unknowns)
        {
            if (unknown == null
                || string.IsNullOrWhiteSpace(unknown.UnknownType)
                || string.IsNullOrWhiteSpace(unknown.Summary))
            {
                throw new InvalidOperationException("Model pass envelope unknowns must contain non-empty unknown_type and summary.");
            }
        }

        if (string.IsNullOrWhiteSpace(envelope.TruthSummary.TruthLayer))
        {
            throw new InvalidOperationException("Model pass envelope truth_summary requires truth_layer.");
        }

        if (string.Equals(envelope.ResultStatus, ModelPassResultStatuses.BlockedInvalidInput, StringComparison.Ordinal)
            && string.IsNullOrWhiteSpace(envelope.OutputSummary.BlockedReason))
        {
            throw new InvalidOperationException("blocked_invalid_input envelopes must provide output_summary.blocked_reason.");
        }

        if ((string.Equals(envelope.ResultStatus, ModelPassResultStatuses.NeedMoreData, StringComparison.Ordinal)
                || string.Equals(envelope.ResultStatus, ModelPassResultStatuses.NeedOperatorClarification, StringComparison.Ordinal))
            && envelope.Unknowns.Count == 0)
        {
            throw new InvalidOperationException("need_more_data and need_operator_clarification envelopes must provide at least one unknown.");
        }

        if (string.Equals(envelope.ResultStatus, ModelPassResultStatuses.ResultReady, StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(envelope.OutputSummary.BlockedReason))
        {
            throw new InvalidOperationException("result_ready envelopes cannot include blocked_reason.");
        }
    }
}
