using Microsoft.EntityFrameworkCore;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Infrastructure.Database;

public class ModelPassAuditStore : IModelPassAuditStore
{
    private const string CompletedLifecycleStatus = "completed";

    private readonly IDbContextFactory<TgAssistantDbContext> _dbFactory;

    public ModelPassAuditStore(IDbContextFactory<TgAssistantDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<ModelPassAuditRecord> UpsertAsync(
        ModelPassEnvelope envelope,
        ModelNormalizationResult normalizationResult,
        CancellationToken ct = default)
    {
        ModelPassEnvelopeValidator.Validate(envelope);
        ModelNormalizationResultValidator.Validate(normalizationResult);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;

        var passRow = await db.ModelPassRuns.FirstOrDefaultAsync(x => x.Id == envelope.RunId, ct);
        if (passRow == null)
        {
            passRow = new DbModelPassRun
            {
                Id = envelope.RunId,
                CreatedAt = now
            };
            db.ModelPassRuns.Add(passRow);
        }

        passRow.ScopeKey = envelope.ScopeKey.Trim();
        passRow.Stage = envelope.Stage.Trim();
        passRow.PassFamily = envelope.PassFamily.Trim();
        passRow.RunKind = envelope.RunKind.Trim();
        passRow.Status = CompletedLifecycleStatus;
        passRow.ResultStatus = envelope.ResultStatus;
        passRow.TargetType = envelope.Target.TargetType.Trim();
        passRow.TargetRef = envelope.Target.TargetRef.Trim();
        passRow.PersonId = envelope.PersonId;
        passRow.SourceObjectId = envelope.SourceObjectId;
        passRow.EvidenceItemId = envelope.EvidenceItemId;
        passRow.TriggerKind = NormalizeNullable(envelope.TriggerKind);
        passRow.TriggerRef = NormalizeNullable(envelope.TriggerRef);
        passRow.SchemaVersion = envelope.SchemaVersion;
        passRow.RequestedModel = NormalizeNullable(envelope.RequestedModel);
        passRow.ScopeJson = ModelPassEnvelopeStorageCodec.SerializeScope(envelope.Scope);
        passRow.SourceRefsJson = ModelPassEnvelopeStorageCodec.SerializeSourceRefs(envelope.SourceRefs);
        passRow.TruthSummaryJson = ModelPassEnvelopeStorageCodec.SerializeTruthSummary(envelope.TruthSummary);
        passRow.ConflictsJson = ModelPassEnvelopeStorageCodec.SerializeConflicts(envelope.Conflicts);
        passRow.UnknownsJson = ModelPassEnvelopeStorageCodec.SerializeUnknowns(envelope.Unknowns);
        passRow.InputSummaryJson = ModelPassEnvelopeStorageCodec.SerializeInputSummary(envelope);
        passRow.OutputSummaryJson = ModelPassEnvelopeStorageCodec.SerializeOutputSummary(envelope.OutputSummary);
        passRow.MetricsJson = ModelPassEnvelopeStorageCodec.UpsertBudgetMetrics(passRow.MetricsJson, envelope.Budget);
        passRow.FailureJson = ModelPassEnvelopeStorageCodec.SerializeFailureSummary(envelope);
        passRow.StartedAt = envelope.StartedAtUtc == default ? now : envelope.StartedAtUtc;
        passRow.FinishedAt = envelope.FinishedAtUtc ?? now;

        var normalizationRow = await db.NormalizationRuns.FirstOrDefaultAsync(
            x => x.ModelPassRunId == envelope.RunId,
            ct);
        if (normalizationRow == null)
        {
            normalizationRow = new DbNormalizationRun
            {
                Id = Guid.NewGuid(),
                ModelPassRunId = envelope.RunId,
                CreatedAt = now
            };
            db.NormalizationRuns.Add(normalizationRow);
        }

        normalizationRow.ScopeKey = normalizationResult.ScopeKey.Trim();
        normalizationRow.Status = normalizationResult.Status;
        normalizationRow.TargetType = normalizationResult.TargetType.Trim();
        normalizationRow.TargetRef = normalizationResult.TargetRef.Trim();
        normalizationRow.TruthLayer = normalizationResult.TruthLayer.Trim();
        normalizationRow.PersonId = normalizationResult.PersonId;
        normalizationRow.SourceObjectId = normalizationResult.SourceObjectId;
        normalizationRow.EvidenceItemId = normalizationResult.EvidenceItemId;
        normalizationRow.SchemaVersion = normalizationResult.SchemaVersion;
        normalizationRow.CandidateCountsJson = ModelNormalizationStorageCodec.SerializeCandidateCounts(normalizationResult.CandidateCounts);
        normalizationRow.NormalizedPayloadJson = ModelNormalizationStorageCodec.SerializeNormalizedPayload(normalizationResult.NormalizedPayload);
        normalizationRow.ConflictsJson = ModelNormalizationStorageCodec.SerializeConflictCandidates(normalizationResult.NormalizedPayload.Conflicts);
        normalizationRow.IssuesJson = ModelNormalizationStorageCodec.SerializeIssues(normalizationResult.Issues);
        normalizationRow.BlockedReason = NormalizeNullable(normalizationResult.BlockedReason);
        normalizationRow.FinishedAt = now;

        await db.SaveChangesAsync(ct);

        return new ModelPassAuditRecord
        {
            ModelPassRunId = envelope.RunId,
            NormalizationRunId = normalizationRow.Id,
            Envelope = CloneEnvelope(envelope),
            Normalization = CloneNormalizationResult(normalizationResult)
        };
    }

    public async Task<ModelPassAuditRecord?> GetByModelPassRunIdAsync(Guid runId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var passRow = await db.ModelPassRuns
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == runId, ct);
        if (passRow == null)
        {
            return null;
        }

        var normalizationRow = await db.NormalizationRuns
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.ModelPassRunId == runId, ct);
        if (normalizationRow == null)
        {
            return null;
        }

        return new ModelPassAuditRecord
        {
            ModelPassRunId = runId,
            NormalizationRunId = normalizationRow.Id,
            Envelope = MapEnvelope(passRow),
            Normalization = MapNormalizationResult(normalizationRow)
        };
    }

    public async Task<int> GetConsecutiveNeedMoreDataCountAsync(
        string scopeKey,
        string stage,
        string passFamily,
        CancellationToken ct = default)
    {
        var normalizedScopeKey = string.IsNullOrWhiteSpace(scopeKey) ? string.Empty : scopeKey.Trim();
        var normalizedStage = string.IsNullOrWhiteSpace(stage) ? string.Empty : stage.Trim();
        var normalizedPassFamily = string.IsNullOrWhiteSpace(passFamily) ? string.Empty : passFamily.Trim();
        if (string.IsNullOrWhiteSpace(normalizedScopeKey)
            || string.IsNullOrWhiteSpace(normalizedStage)
            || string.IsNullOrWhiteSpace(normalizedPassFamily))
        {
            return 0;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var resultStatuses = await db.ModelPassRuns
            .AsNoTracking()
            .Where(x => x.ScopeKey == normalizedScopeKey
                && x.Stage == normalizedStage
                && x.PassFamily == normalizedPassFamily)
            .OrderByDescending(x => x.StartedAt)
            .ThenByDescending(x => x.CreatedAt)
            .Select(x => x.ResultStatus)
            .Take(32)
            .ToListAsync(ct);

        var count = 0;
        foreach (var resultStatus in resultStatuses)
        {
            if (!string.Equals(resultStatus, ModelPassResultStatuses.NeedMoreData, StringComparison.Ordinal))
            {
                break;
            }

            count += 1;
        }

        return count;
    }

    private static ModelPassEnvelope MapEnvelope(DbModelPassRun row)
    {
        return new ModelPassEnvelope
        {
            RunId = row.Id,
            SchemaVersion = row.SchemaVersion,
            Stage = row.Stage,
            PassFamily = row.PassFamily,
            RunKind = row.RunKind,
            ScopeKey = row.ScopeKey,
            Scope = ModelPassEnvelopeStorageCodec.DeserializeScope(row.ScopeJson),
            Target = new ModelPassTarget
            {
                TargetType = row.TargetType,
                TargetRef = row.TargetRef
            },
            PersonId = row.PersonId,
            SourceObjectId = row.SourceObjectId,
            EvidenceItemId = row.EvidenceItemId,
            RequestedModel = row.RequestedModel,
            TriggerKind = row.TriggerKind,
            TriggerRef = row.TriggerRef,
            SourceRefs = ModelPassEnvelopeStorageCodec.DeserializeSourceRefs(row.SourceRefsJson),
            TruthSummary = ModelPassEnvelopeStorageCodec.DeserializeTruthSummary(row.TruthSummaryJson),
            Conflicts = ModelPassEnvelopeStorageCodec.DeserializeConflicts(row.ConflictsJson),
            Unknowns = ModelPassEnvelopeStorageCodec.DeserializeUnknowns(row.UnknownsJson),
            Budget = ModelPassEnvelopeStorageCodec.DeserializeBudget(row.MetricsJson),
            ResultStatus = row.ResultStatus,
            OutputSummary = ModelPassEnvelopeStorageCodec.DeserializeOutputSummary(row.OutputSummaryJson),
            StartedAtUtc = row.StartedAt,
            FinishedAtUtc = row.FinishedAt
        };
    }

    private static ModelNormalizationResult MapNormalizationResult(DbNormalizationRun row)
    {
        return new ModelNormalizationResult
        {
            ModelPassRunId = row.ModelPassRunId,
            SchemaVersion = row.SchemaVersion,
            ScopeKey = row.ScopeKey,
            TargetType = row.TargetType,
            TargetRef = row.TargetRef,
            TruthLayer = row.TruthLayer,
            PersonId = row.PersonId,
            SourceObjectId = row.SourceObjectId,
            EvidenceItemId = row.EvidenceItemId,
            Status = row.Status,
            BlockedReason = row.BlockedReason,
            CandidateCounts = ModelNormalizationStorageCodec.DeserializeCandidateCounts(row.CandidateCountsJson),
            NormalizedPayload = ModelNormalizationStorageCodec.DeserializeNormalizedPayload(row.NormalizedPayloadJson),
            Issues = ModelNormalizationStorageCodec.DeserializeIssues(row.IssuesJson)
        };
    }

    private static ModelPassEnvelope CloneEnvelope(ModelPassEnvelope envelope)
    {
        return new ModelPassEnvelope
        {
            RunId = envelope.RunId,
            SchemaVersion = envelope.SchemaVersion,
            Stage = envelope.Stage,
            PassFamily = envelope.PassFamily,
            RunKind = envelope.RunKind,
            ScopeKey = envelope.ScopeKey,
            Scope = new ModelPassScope
            {
                ScopeType = envelope.Scope.ScopeType,
                ScopeRef = envelope.Scope.ScopeRef,
                AdditionalRefs = [.. envelope.Scope.AdditionalRefs]
            },
            Target = new ModelPassTarget
            {
                TargetType = envelope.Target.TargetType,
                TargetRef = envelope.Target.TargetRef
            },
            PersonId = envelope.PersonId,
            SourceObjectId = envelope.SourceObjectId,
            EvidenceItemId = envelope.EvidenceItemId,
            RequestedModel = envelope.RequestedModel,
            TriggerKind = envelope.TriggerKind,
            TriggerRef = envelope.TriggerRef,
            SourceRefs =
            [
                .. envelope.SourceRefs.Select(x => new ModelPassSourceRef
                {
                    SourceType = x.SourceType,
                    SourceRef = x.SourceRef,
                    SourceObjectId = x.SourceObjectId,
                    EvidenceItemId = x.EvidenceItemId
                })
            ],
            TruthSummary = new ModelPassTruthSummary
            {
                TruthLayer = envelope.TruthSummary.TruthLayer,
                Summary = envelope.TruthSummary.Summary,
                CanonicalRefs = [.. envelope.TruthSummary.CanonicalRefs]
            },
            Conflicts =
            [
                .. envelope.Conflicts.Select(x => new ModelPassConflict
                {
                    ConflictType = x.ConflictType,
                    Summary = x.Summary,
                    RelatedObjectRef = x.RelatedObjectRef
                })
            ],
            Unknowns =
            [
                .. envelope.Unknowns.Select(x => new ModelPassUnknown
                {
                    UnknownType = x.UnknownType,
                    Summary = x.Summary,
                    RequiredAction = x.RequiredAction
                })
            ],
            Budget = new ModelPassBudgetEnvelope
            {
                BudgetProfileKey = envelope.Budget.BudgetProfileKey,
                MaxIterations = envelope.Budget.MaxIterations,
                IterationsConsumed = envelope.Budget.IterationsConsumed,
                MaxInputTokens = envelope.Budget.MaxInputTokens,
                InputTokensConsumed = envelope.Budget.InputTokensConsumed,
                MaxOutputTokens = envelope.Budget.MaxOutputTokens,
                OutputTokensConsumed = envelope.Budget.OutputTokensConsumed,
                MaxTotalTokens = envelope.Budget.MaxTotalTokens,
                TotalTokensConsumed = envelope.Budget.TotalTokensConsumed,
                MaxCostUsd = envelope.Budget.MaxCostUsd,
                CostUsdConsumed = envelope.Budget.CostUsdConsumed
            },
            ResultStatus = envelope.ResultStatus,
            OutputSummary = new ModelPassOutputSummary
            {
                Summary = envelope.OutputSummary.Summary,
                BlockedReason = envelope.OutputSummary.BlockedReason
            },
            StartedAtUtc = envelope.StartedAtUtc,
            FinishedAtUtc = envelope.FinishedAtUtc
        };
    }

    private static ModelNormalizationResult CloneNormalizationResult(ModelNormalizationResult result)
    {
        return new ModelNormalizationResult
        {
            ModelPassRunId = result.ModelPassRunId,
            SchemaVersion = result.SchemaVersion,
            ScopeKey = result.ScopeKey,
            TargetType = result.TargetType,
            TargetRef = result.TargetRef,
            TruthLayer = result.TruthLayer,
            PersonId = result.PersonId,
            SourceObjectId = result.SourceObjectId,
            EvidenceItemId = result.EvidenceItemId,
            Status = result.Status,
            BlockedReason = result.BlockedReason,
            CandidateCounts = new ModelNormalizationCandidateCounts
            {
                Facts = result.CandidateCounts.Facts,
                Inferences = result.CandidateCounts.Inferences,
                Hypotheses = result.CandidateCounts.Hypotheses,
                Conflicts = result.CandidateCounts.Conflicts
            },
            NormalizedPayload = new ModelNormalizationPayload
            {
                Facts =
                [
                    .. result.NormalizedPayload.Facts.Select(x => new NormalizedFactCandidate
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
                    .. result.NormalizedPayload.Inferences.Select(x => new NormalizedInferenceCandidate
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
                    .. result.NormalizedPayload.Hypotheses.Select(x => new NormalizedHypothesisCandidate
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
                    .. result.NormalizedPayload.Conflicts.Select(x => new NormalizedConflictCandidate
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
                .. result.Issues.Select(x => new ModelNormalizationIssue
                {
                    Severity = x.Severity,
                    Code = x.Code,
                    Summary = x.Summary,
                    Path = x.Path
                })
            ]
        };
    }

    private static string? NormalizeNullable(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
