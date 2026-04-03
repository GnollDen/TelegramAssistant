using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Infrastructure.Database;

public class ModelPassAuditService : IModelPassAuditService
{
    private const string LoopBudgetIssueCode = "loop_budget_exhausted";

    private readonly IModelOutputNormalizer _normalizer;
    private readonly IModelPassAuditStore _auditStore;
    private readonly IRuntimeDefectRepository? _runtimeDefectRepository;
    private readonly IClarificationBranchStateRepository? _clarificationBranchStateRepository;

    public ModelPassAuditService(
        IModelOutputNormalizer normalizer,
        IModelPassAuditStore auditStore,
        IRuntimeDefectRepository? runtimeDefectRepository = null,
        IClarificationBranchStateRepository? clarificationBranchStateRepository = null)
    {
        _normalizer = normalizer;
        _auditStore = auditStore;
        _runtimeDefectRepository = runtimeDefectRepository;
        _clarificationBranchStateRepository = clarificationBranchStateRepository;
    }

    public async Task<ModelPassAuditRecord> NormalizeAndPersistAsync(ModelNormalizationRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Envelope);

        var consecutiveNeedMoreDataCount = await _auditStore.GetConsecutiveNeedMoreDataCountAsync(
            request.Envelope.ScopeKey,
            request.Envelope.Stage,
            request.Envelope.PassFamily,
            ct);
        var normalization = _normalizer.Normalize(request);
        var finalizedNormalization = ApplyLoopGuard(
            request.Envelope,
            normalization,
            consecutiveNeedMoreDataCount,
            out var loopGuardTriggered);
        var envelope = BuildAuditedEnvelope(request.Envelope, finalizedNormalization, consecutiveNeedMoreDataCount);
        var record = await _auditStore.UpsertAsync(envelope, finalizedNormalization, ct);

        if (loopGuardTriggered)
        {
            await PersistLoopGuardDefectAsync(record, consecutiveNeedMoreDataCount, ct);
        }

        if (_clarificationBranchStateRepository != null)
        {
            await _clarificationBranchStateRepository.ApplyOutcomeAsync(record, ct);
        }

        return record;
    }

    private static ModelPassEnvelope BuildAuditedEnvelope(
        ModelPassEnvelope inputEnvelope,
        ModelNormalizationResult normalization,
        int consecutiveNeedMoreDataCount)
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

        if (normalization.Issues.Any(x => string.Equals(x.Code, LoopBudgetIssueCode, StringComparison.Ordinal))
            && !unknowns.Any(x => string.Equals(x.UnknownType, "loop_budget_exhausted", StringComparison.Ordinal)))
        {
            unknowns.Add(new ModelPassUnknown
            {
                UnknownType = "loop_budget_exhausted",
                Summary = normalization.Issues.First(x => string.Equals(x.Code, LoopBudgetIssueCode, StringComparison.Ordinal)).Summary,
                RequiredAction = "operator_review"
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
            Budget = ModelPassBudgetCatalog.WithIterationsConsumed(
                inputEnvelope.Budget,
                consecutiveNeedMoreDataCount + 1),
            ResultStatus = normalization.Status,
            OutputSummary = outputSummary,
            StartedAtUtc = inputEnvelope.StartedAtUtc == default ? DateTime.UtcNow : inputEnvelope.StartedAtUtc,
            FinishedAtUtc = DateTime.UtcNow
        };
    }

    private static string BuildOutputSummary(ModelPassEnvelope inputEnvelope, ModelNormalizationResult normalization)
    {
        var loopBudgetIssue = normalization.Issues.FirstOrDefault(x => string.Equals(x.Code, LoopBudgetIssueCode, StringComparison.Ordinal));
        if (loopBudgetIssue != null)
        {
            return loopBudgetIssue.Summary;
        }

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

    private static ModelNormalizationResult ApplyLoopGuard(
        ModelPassEnvelope inputEnvelope,
        ModelNormalizationResult normalization,
        int consecutiveNeedMoreDataCount,
        out bool loopGuardTriggered)
    {
        loopGuardTriggered = false;

        if (!string.Equals(normalization.Status, ModelPassResultStatuses.NeedMoreData, StringComparison.Ordinal))
        {
            return CloneNormalization(normalization);
        }

        var exhaustedIterations = consecutiveNeedMoreDataCount + 1 >= inputEnvelope.Budget.MaxIterations;
        if (!exhaustedIterations)
        {
            return CloneNormalization(normalization);
        }

        loopGuardTriggered = true;
        var escalated = CloneNormalization(normalization);
        escalated.Status = ModelPassResultStatuses.NeedOperatorClarification;
        escalated.BlockedReason = null;
        escalated.Issues.Add(new ModelNormalizationIssue
        {
            Severity = RuntimeDefectSeverities.High,
            Code = LoopBudgetIssueCode,
            Summary = $"Loop guard exhausted pass budget '{inputEnvelope.Budget.BudgetProfileKey}' after {Math.Min(consecutiveNeedMoreDataCount + 1, inputEnvelope.Budget.MaxIterations)} iterations; operator review is required before another pass.",
            Path = "budget.max_iterations"
        });
        return escalated;
    }

    private async Task PersistLoopGuardDefectAsync(
        ModelPassAuditRecord record,
        int consecutiveNeedMoreDataCount,
        CancellationToken ct)
    {
        if (_runtimeDefectRepository == null)
        {
            return;
        }

        var envelope = record.Envelope;
        var issue = record.Normalization.Issues.FirstOrDefault(x => string.Equals(x.Code, LoopBudgetIssueCode, StringComparison.Ordinal));
        var detailsJson = $$"""
            {"stage":"{{EscapeJson(envelope.Stage)}}","pass_family":"{{EscapeJson(envelope.PassFamily)}}","result_status":"{{EscapeJson(envelope.ResultStatus)}}","budget_profile_key":"{{EscapeJson(envelope.Budget.BudgetProfileKey)}}","iterations_consumed":{{envelope.Budget.IterationsConsumed}},"max_iterations":{{envelope.Budget.MaxIterations}},"prior_need_more_data_count":{{consecutiveNeedMoreDataCount}},"issue_code":"{{EscapeJson(issue?.Code ?? LoopBudgetIssueCode)}}"}
            """;

        await _runtimeDefectRepository.UpsertAsync(new RuntimeDefectUpsertRequest
        {
            DefectClass = RuntimeDefectClasses.Normalization,
            Severity = RuntimeDefectSeverities.High,
            ScopeKey = envelope.ScopeKey,
            DedupeKey = $"{envelope.ScopeKey}|{envelope.Stage}|{envelope.PassFamily}|loop_budget_exhausted",
            RunId = record.ModelPassRunId,
            ObjectType = envelope.Target.TargetType,
            ObjectRef = envelope.Target.TargetRef,
            Summary = issue?.Summary ?? "Loop guard escalated repeated need_more_data outcomes to operator clarification.",
            DetailsJson = detailsJson
        }, ct);
    }

    private static ModelNormalizationResult CloneNormalization(ModelNormalizationResult normalization)
    {
        return new ModelNormalizationResult
        {
            ModelPassRunId = normalization.ModelPassRunId,
            SchemaVersion = normalization.SchemaVersion,
            ScopeKey = normalization.ScopeKey,
            TargetType = normalization.TargetType,
            TargetRef = normalization.TargetRef,
            TruthLayer = normalization.TruthLayer,
            PersonId = normalization.PersonId,
            SourceObjectId = normalization.SourceObjectId,
            EvidenceItemId = normalization.EvidenceItemId,
            Status = normalization.Status,
            BlockedReason = normalization.BlockedReason,
            CandidateCounts = new ModelNormalizationCandidateCounts
            {
                Facts = normalization.CandidateCounts.Facts,
                Inferences = normalization.CandidateCounts.Inferences,
                Hypotheses = normalization.CandidateCounts.Hypotheses,
                Conflicts = normalization.CandidateCounts.Conflicts
            },
            NormalizedPayload = new ModelNormalizationPayload
            {
                Facts =
                [
                    .. normalization.NormalizedPayload.Facts.Select(x => new NormalizedFactCandidate
                    {
                        Category = x.Category,
                        Key = x.Key,
                        Value = x.Value,
                        TruthLayer = x.TruthLayer,
                        Confidence = x.Confidence,
                        EvidenceRefs = [.. x.EvidenceRefs]
                    })
                ],
                Inferences =
                [
                    .. normalization.NormalizedPayload.Inferences.Select(x => new NormalizedInferenceCandidate
                    {
                        InferenceType = x.InferenceType,
                        SubjectType = x.SubjectType,
                        SubjectRef = x.SubjectRef,
                        Summary = x.Summary,
                        TruthLayer = x.TruthLayer,
                        Confidence = x.Confidence,
                        EvidenceRefs = [.. x.EvidenceRefs]
                    })
                ],
                Hypotheses =
                [
                    .. normalization.NormalizedPayload.Hypotheses.Select(x => new NormalizedHypothesisCandidate
                    {
                        HypothesisType = x.HypothesisType,
                        SubjectType = x.SubjectType,
                        SubjectRef = x.SubjectRef,
                        Statement = x.Statement,
                        TruthLayer = x.TruthLayer,
                        Confidence = x.Confidence,
                        EvidenceRefs = [.. x.EvidenceRefs]
                    })
                ],
                Conflicts =
                [
                    .. normalization.NormalizedPayload.Conflicts.Select(x => new NormalizedConflictCandidate
                    {
                        ConflictType = x.ConflictType,
                        Summary = x.Summary,
                        TruthLayer = x.TruthLayer,
                        RelatedObjectRef = x.RelatedObjectRef,
                        Confidence = x.Confidence,
                        EvidenceRefs = [.. x.EvidenceRefs]
                    })
                ]
            },
            Issues =
            [
                .. normalization.Issues.Select(x => new ModelNormalizationIssue
                {
                    Severity = x.Severity,
                    Code = x.Code,
                    Summary = x.Summary,
                    Path = x.Path
                })
            ]
        };
    }

    private static string EscapeJson(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
}
