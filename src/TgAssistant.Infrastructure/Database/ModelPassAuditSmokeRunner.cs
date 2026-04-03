using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Infrastructure.Database;

public static class ModelPassAuditSmokeRunner
{
    public static async Task RunAsync(CancellationToken ct = default)
    {
        var store = new InMemoryModelPassAuditStore();
        var service = new ModelPassAuditService(new ModelOutputNormalizer(), store);

        var readyRecord = await service.NormalizeAndPersistAsync(new ModelNormalizationRequest
        {
            Envelope = BuildEnvelope(Guid.Parse("10000000-0000-0000-0000-000000000001")),
            RawModelOutput =
                """
                {
                  "result_status": "result_ready",
                  "facts": [
                    {
                      "category": "identity",
                      "key": "display_name",
                      "value": "Stage5 Smoke Sender",
                      "truth_layer": "canonical_truth",
                      "confidence": 0.98,
                      "evidence_refs": ["evidence:smoke-1"]
                    }
                  ]
                }
                """
        }, ct);
        AssertStatus(readyRecord, ModelPassResultStatuses.ResultReady, "result_ready");

        var needMoreDataEnvelope = BuildEnvelope(Guid.Parse("10000000-0000-0000-0000-000000000002"));
        needMoreDataEnvelope.ResultStatus = ModelPassResultStatuses.NeedMoreData;
        needMoreDataEnvelope.Unknowns =
        [
            new ModelPassUnknown
            {
                UnknownType = "coverage_gap",
                Summary = "More source coverage is required.",
                RequiredAction = "collect_more_evidence"
            }
        ];
        var needMoreDataRecord = await service.NormalizeAndPersistAsync(new ModelNormalizationRequest
        {
            Envelope = needMoreDataEnvelope,
            RawModelOutput =
                """
                {
                  "result_status": "need_more_data",
                  "facts": []
                }
                """
        }, ct);
        AssertStatus(needMoreDataRecord, ModelPassResultStatuses.NeedMoreData, "need_more_data");

        var clarificationRecord = await service.NormalizeAndPersistAsync(new ModelNormalizationRequest
        {
            Envelope = BuildEnvelope(Guid.Parse("10000000-0000-0000-0000-000000000003")),
            RawModelOutput =
                """
                {
                  "result_status": "result_ready",
                  "facts": [
                    {
                      "category": "identity",
                      "key": "display_name",
                      "value": "Stage5 Smoke Sender",
                      "truth_layer": "canonical_truth",
                      "confidence": 0.98
                    },
                    {
                      "category": "identity",
                      "key": "",
                      "value": "bad candidate",
                      "truth_layer": "canonical_truth",
                      "confidence": 0.40
                    }
                  ]
                }
                """
        }, ct);
        AssertStatus(clarificationRecord, ModelPassResultStatuses.NeedOperatorClarification, "need_operator_clarification");
        if (clarificationRecord.Envelope.Unknowns.Count == 0)
        {
            throw new InvalidOperationException("Model pass audit smoke failed: clarification outcome did not persist an unknown for review.");
        }

        var blockedRecord = await service.NormalizeAndPersistAsync(new ModelNormalizationRequest
        {
            Envelope = BuildEnvelope(Guid.Parse("10000000-0000-0000-0000-000000000004")),
            RawModelOutput = "This is free-form prose and must be blocked."
        }, ct);
        AssertStatus(blockedRecord, ModelPassResultStatuses.BlockedInvalidInput, "blocked_invalid_input");
        if (string.IsNullOrWhiteSpace(blockedRecord.Envelope.OutputSummary.BlockedReason))
        {
            throw new InvalidOperationException("Model pass audit smoke failed: blocked outcome did not persist blocked_reason.");
        }

        if (await store.GetByModelPassRunIdAsync(readyRecord.ModelPassRunId, ct) == null
            || await store.GetByModelPassRunIdAsync(needMoreDataRecord.ModelPassRunId, ct) == null
            || await store.GetByModelPassRunIdAsync(clarificationRecord.ModelPassRunId, ct) == null
            || await store.GetByModelPassRunIdAsync(blockedRecord.ModelPassRunId, ct) == null)
        {
            throw new InvalidOperationException("Model pass audit smoke failed: persisted audit records could not be reloaded.");
        }
    }

    private static void AssertStatus(ModelPassAuditRecord record, string expectedStatus, string label)
    {
        if (!string.Equals(record.Envelope.ResultStatus, expectedStatus, StringComparison.Ordinal)
            || !string.Equals(record.Normalization.Status, expectedStatus, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Model pass audit smoke failed: {label} outcome was not persisted consistently.");
        }

        if (record.NormalizationRunId == Guid.Empty)
        {
            throw new InvalidOperationException($"Model pass audit smoke failed: {label} outcome did not assign a normalization_run id.");
        }
    }

    private static ModelPassEnvelope BuildEnvelope(Guid runId)
    {
        return new ModelPassEnvelope
        {
            RunId = runId,
            Stage = "stage6_bootstrap",
            PassFamily = "graph_seed",
            RunKind = "synthetic_audit_smoke",
            ScopeKey = "person:smoke",
            Scope = new ModelPassScope
            {
                ScopeType = "person_scope",
                ScopeRef = "person:smoke",
                AdditionalRefs = ["source_object:smoke-1"]
            },
            Target = new ModelPassTarget
            {
                TargetType = "person",
                TargetRef = "person:smoke"
            },
            RequestedModel = "smoke-model",
            TriggerKind = "manual_smoke",
            TriggerRef = "implement-004-c",
            SourceRefs =
            [
                new ModelPassSourceRef
                {
                    SourceType = "source_object",
                    SourceRef = "source_object:smoke-1"
                }
            ],
            TruthSummary = new ModelPassTruthSummary
            {
                TruthLayer = ModelNormalizationTruthLayers.CanonicalTruth,
                Summary = "Synthetic audit smoke summary.",
                CanonicalRefs = ["evidence:smoke-1"]
            },
            Budget = ModelPassBudgetCatalog.ConsumeOneIteration(
                ModelPassBudgetCatalog.Create("stage6_bootstrap", "graph_seed")),
            ResultStatus = ModelPassResultStatuses.ResultReady,
            OutputSummary = new ModelPassOutputSummary
            {
                Summary = "Synthetic audit smoke envelope."
            }
        };
    }

    private sealed class InMemoryModelPassAuditStore : IModelPassAuditStore
    {
        private readonly Dictionary<Guid, ModelPassAuditRecord> _records = [];

        public Task<ModelPassAuditRecord> UpsertAsync(
            ModelPassEnvelope envelope,
            ModelNormalizationResult normalizationResult,
            CancellationToken ct = default)
        {
            ModelPassEnvelopeValidator.Validate(envelope);
            ModelNormalizationResultValidator.Validate(normalizationResult);

            var record = new ModelPassAuditRecord
            {
                ModelPassRunId = envelope.RunId,
                NormalizationRunId = _records.TryGetValue(envelope.RunId, out var existing)
                    ? existing.NormalizationRunId
                    : Guid.NewGuid(),
                Envelope = CloneEnvelope(envelope),
                Normalization = CloneNormalization(normalizationResult)
            };

            _records[envelope.RunId] = record;
            return Task.FromResult(record);
        }

        public Task<ModelPassAuditRecord?> GetByModelPassRunIdAsync(Guid runId, CancellationToken ct = default)
        {
            _records.TryGetValue(runId, out var record);
            return Task.FromResult(record);
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

        private static ModelNormalizationResult CloneNormalization(ModelNormalizationResult result)
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
    }
}
