using TgAssistant.Core.Models;

namespace TgAssistant.Infrastructure.Database;

public static class ModelNormalizationResultValidator
{
    public static void Validate(ModelNormalizationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(result.CandidateCounts);
        ArgumentNullException.ThrowIfNull(result.NormalizedPayload);
        ArgumentNullException.ThrowIfNull(result.Issues);

        if (result.SchemaVersion <= 0)
        {
            throw new InvalidOperationException("Model normalization schema_version must be positive.");
        }

        if (result.ModelPassRunId == Guid.Empty)
        {
            throw new InvalidOperationException("Model normalization result requires model_pass_run_id.");
        }

        if (string.IsNullOrWhiteSpace(result.ScopeKey)
            || string.IsNullOrWhiteSpace(result.TargetType)
            || string.IsNullOrWhiteSpace(result.TargetRef)
            || string.IsNullOrWhiteSpace(result.TruthLayer))
        {
            throw new InvalidOperationException("Model normalization result requires scope_key, target_type, target_ref, and truth_layer.");
        }

        if (!ModelPassResultStatuses.All.Contains(result.Status, StringComparer.Ordinal))
        {
            throw new InvalidOperationException($"Unsupported normalization status '{result.Status}'.");
        }

        if (string.Equals(result.Status, ModelPassResultStatuses.BlockedInvalidInput, StringComparison.Ordinal)
            && string.IsNullOrWhiteSpace(result.BlockedReason))
        {
            throw new InvalidOperationException("blocked_invalid_input normalization results must provide blocked_reason.");
        }

        if (!string.Equals(result.Status, ModelPassResultStatuses.BlockedInvalidInput, StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(result.BlockedReason))
        {
            throw new InvalidOperationException("Only blocked_invalid_input normalization results may provide blocked_reason.");
        }

        var payload = result.NormalizedPayload;
        if (payload.Facts.Count != result.CandidateCounts.Facts
            || payload.Inferences.Count != result.CandidateCounts.Inferences
            || payload.Hypotheses.Count != result.CandidateCounts.Hypotheses
            || payload.Conflicts.Count != result.CandidateCounts.Conflicts)
        {
            throw new InvalidOperationException("Model normalization candidate_counts must match normalized_payload counts.");
        }

        var totalCandidateCount = payload.Facts.Count + payload.Inferences.Count + payload.Hypotheses.Count + payload.Conflicts.Count;
        if (string.Equals(result.Status, ModelPassResultStatuses.ResultReady, StringComparison.Ordinal)
            && totalCandidateCount == 0)
        {
            throw new InvalidOperationException("result_ready normalization results must contain at least one typed candidate.");
        }
    }
}
