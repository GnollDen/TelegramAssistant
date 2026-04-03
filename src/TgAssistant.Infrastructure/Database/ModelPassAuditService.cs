using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Infrastructure.Database;

public class ModelPassAuditService : IModelPassAuditService
{
    private readonly IModelOutputNormalizer _normalizer;
    private readonly IModelPassAuditStore _auditStore;

    public ModelPassAuditService(
        IModelOutputNormalizer normalizer,
        IModelPassAuditStore auditStore)
    {
        _normalizer = normalizer;
        _auditStore = auditStore;
    }

    public Task<ModelPassAuditRecord> NormalizeAndPersistAsync(ModelNormalizationRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Envelope);

        var normalization = _normalizer.Normalize(request);
        var envelope = BuildAuditedEnvelope(request.Envelope, normalization);
        return _auditStore.UpsertAsync(envelope, normalization, ct);
    }

    private static ModelPassEnvelope BuildAuditedEnvelope(
        ModelPassEnvelope inputEnvelope,
        ModelNormalizationResult normalization)
    {
        var unknowns = inputEnvelope.Unknowns
            .Select(x => new ModelPassUnknown
            {
                UnknownType = x.UnknownType,
                Summary = x.Summary,
                RequiredAction = x.RequiredAction
            })
            .ToList();

        if ((string.Equals(normalization.Status, ModelPassResultStatuses.NeedMoreData, StringComparison.Ordinal)
                || string.Equals(normalization.Status, ModelPassResultStatuses.NeedOperatorClarification, StringComparison.Ordinal))
            && unknowns.Count == 0)
        {
            unknowns.Add(new ModelPassUnknown
            {
                UnknownType = string.Equals(normalization.Status, ModelPassResultStatuses.NeedMoreData, StringComparison.Ordinal)
                    ? "missing_context"
                    : "normalization_review_required",
                Summary = normalization.Issues.FirstOrDefault()?.Summary
                    ?? "Normalization outcome requires operator review before promotion.",
                RequiredAction = string.Equals(normalization.Status, ModelPassResultStatuses.NeedMoreData, StringComparison.Ordinal)
                    ? "provide_more_evidence"
                    : "review_normalization_issues"
            });
        }

        var outputSummary = new ModelPassOutputSummary
        {
            Summary = BuildOutputSummary(inputEnvelope, normalization),
            BlockedReason = string.Equals(normalization.Status, ModelPassResultStatuses.BlockedInvalidInput, StringComparison.Ordinal)
                ? normalization.BlockedReason
                : null
        };

        return new ModelPassEnvelope
        {
            RunId = inputEnvelope.RunId,
            SchemaVersion = inputEnvelope.SchemaVersion,
            Stage = inputEnvelope.Stage,
            PassFamily = inputEnvelope.PassFamily,
            RunKind = inputEnvelope.RunKind,
            ScopeKey = inputEnvelope.ScopeKey,
            Scope = new ModelPassScope
            {
                ScopeType = inputEnvelope.Scope.ScopeType,
                ScopeRef = inputEnvelope.Scope.ScopeRef,
                AdditionalRefs = [.. inputEnvelope.Scope.AdditionalRefs]
            },
            Target = new ModelPassTarget
            {
                TargetType = inputEnvelope.Target.TargetType,
                TargetRef = inputEnvelope.Target.TargetRef
            },
            PersonId = inputEnvelope.PersonId,
            SourceObjectId = inputEnvelope.SourceObjectId,
            EvidenceItemId = inputEnvelope.EvidenceItemId,
            RequestedModel = inputEnvelope.RequestedModel,
            TriggerKind = inputEnvelope.TriggerKind,
            TriggerRef = inputEnvelope.TriggerRef,
            SourceRefs =
            [
                .. inputEnvelope.SourceRefs.Select(x => new ModelPassSourceRef
                {
                    SourceType = x.SourceType,
                    SourceRef = x.SourceRef,
                    SourceObjectId = x.SourceObjectId,
                    EvidenceItemId = x.EvidenceItemId
                })
            ],
            TruthSummary = new ModelPassTruthSummary
            {
                TruthLayer = inputEnvelope.TruthSummary.TruthLayer,
                Summary = inputEnvelope.TruthSummary.Summary,
                CanonicalRefs = [.. inputEnvelope.TruthSummary.CanonicalRefs]
            },
            Conflicts =
            [
                .. inputEnvelope.Conflicts.Select(x => new ModelPassConflict
                {
                    ConflictType = x.ConflictType,
                    Summary = x.Summary,
                    RelatedObjectRef = x.RelatedObjectRef
                })
            ],
            Unknowns = unknowns,
            Budget = new ModelPassBudgetEnvelope
            {
                BudgetProfileKey = inputEnvelope.Budget.BudgetProfileKey,
                MaxIterations = inputEnvelope.Budget.MaxIterations,
                IterationsConsumed = inputEnvelope.Budget.IterationsConsumed,
                MaxInputTokens = inputEnvelope.Budget.MaxInputTokens,
                InputTokensConsumed = inputEnvelope.Budget.InputTokensConsumed,
                MaxOutputTokens = inputEnvelope.Budget.MaxOutputTokens,
                OutputTokensConsumed = inputEnvelope.Budget.OutputTokensConsumed,
                MaxTotalTokens = inputEnvelope.Budget.MaxTotalTokens,
                TotalTokensConsumed = inputEnvelope.Budget.TotalTokensConsumed,
                MaxCostUsd = inputEnvelope.Budget.MaxCostUsd,
                CostUsdConsumed = inputEnvelope.Budget.CostUsdConsumed
            },
            ResultStatus = normalization.Status,
            OutputSummary = outputSummary,
            StartedAtUtc = inputEnvelope.StartedAtUtc == default ? DateTime.UtcNow : inputEnvelope.StartedAtUtc,
            FinishedAtUtc = DateTime.UtcNow
        };
    }

    private static string BuildOutputSummary(ModelPassEnvelope inputEnvelope, ModelNormalizationResult normalization)
    {
        if (string.Equals(normalization.Status, ModelPassResultStatuses.BlockedInvalidInput, StringComparison.Ordinal))
        {
            return normalization.BlockedReason ?? "Normalization blocked invalid input.";
        }

        if (string.Equals(normalization.Status, ModelPassResultStatuses.ResultReady, StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(inputEnvelope.OutputSummary.Summary))
        {
            return inputEnvelope.OutputSummary.Summary;
        }

        return normalization.Status switch
        {
            ModelPassResultStatuses.ResultReady => "Normalization completed with typed output candidates.",
            ModelPassResultStatuses.NeedMoreData => "Normalization completed but needs more data before promotion.",
            ModelPassResultStatuses.NeedOperatorClarification => "Normalization completed with issues that require operator clarification.",
            _ => "Normalization completed."
        };
    }
}
