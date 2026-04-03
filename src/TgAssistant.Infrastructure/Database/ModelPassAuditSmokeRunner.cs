using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Infrastructure.Database;

public static class ModelPassAuditSmokeRunner
{
    public static async Task RunAsync(CancellationToken ct = default)
    {
        var store = new InMemoryModelPassAuditStore();
        var runtimeDefectRepository = new InMemoryRuntimeDefectRepository();
        var clarificationBranchStateRepository = new InMemoryClarificationBranchStateRepository();
        var service = new ModelPassAuditService(
            new ModelOutputNormalizer(),
            store,
            runtimeDefectRepository,
            clarificationBranchStateRepository);

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
        var openClarificationBranches = await clarificationBranchStateRepository.GetOpenByScopeAsync("person:smoke", ct);
        if (openClarificationBranches.Count != 1
            || !string.Equals(openClarificationBranches[0].BranchFamily, Stage8RecomputeTargetFamilies.Stage6Bootstrap, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Model pass audit smoke failed: clarification outcome did not persist a queryable branch-local block.");
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
        if ((await clarificationBranchStateRepository.GetOpenByScopeAsync("person:smoke", ct)).Count != 1)
        {
            throw new InvalidOperationException("Model pass audit smoke failed: non-clarification blocked outcome should not clear an open clarification branch.");
        }

        var resolvedRecord = await service.NormalizeAndPersistAsync(new ModelNormalizationRequest
        {
            Envelope = BuildEnvelope(Guid.Parse("10000000-0000-0000-0000-000000000008")),
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
        AssertStatus(resolvedRecord, ModelPassResultStatuses.ResultReady, "clarification_resolution");
        if ((await clarificationBranchStateRepository.GetOpenByScopeAsync("person:smoke", ct)).Count != 0)
        {
            throw new InvalidOperationException("Model pass audit smoke failed: ready outcome did not resolve the open clarification branch.");
        }

        if (await store.GetByModelPassRunIdAsync(readyRecord.ModelPassRunId, ct) == null
            || await store.GetByModelPassRunIdAsync(needMoreDataRecord.ModelPassRunId, ct) == null
            || await store.GetByModelPassRunIdAsync(clarificationRecord.ModelPassRunId, ct) == null
            || await store.GetByModelPassRunIdAsync(blockedRecord.ModelPassRunId, ct) == null)
        {
            throw new InvalidOperationException("Model pass audit smoke failed: persisted audit records could not be reloaded.");
        }

        await service.NormalizeAndPersistAsync(new ModelNormalizationRequest
        {
            Envelope = BuildLoopHistoryEnvelope(Guid.Parse("10000000-0000-0000-0000-000000000005")),
            RawModelOutput =
                """
                {
                  "result_status": "need_more_data",
                  "facts": []
                }
                """
        }, ct);
        await service.NormalizeAndPersistAsync(new ModelNormalizationRequest
        {
            Envelope = BuildLoopHistoryEnvelope(Guid.Parse("10000000-0000-0000-0000-000000000006")),
            RawModelOutput =
                """
                {
                  "result_status": "need_more_data",
                  "facts": []
                }
                """
        }, ct);

        var exhaustedRecord = await service.NormalizeAndPersistAsync(new ModelNormalizationRequest
        {
            Envelope = BuildLoopHistoryEnvelope(Guid.Parse("10000000-0000-0000-0000-000000000007")),
            RawModelOutput =
                """
                {
                  "result_status": "need_more_data",
                  "facts": []
                }
                """
        }, ct);
        AssertStatus(exhaustedRecord, ModelPassResultStatuses.NeedOperatorClarification, "loop_budget_exhausted");
        if (!exhaustedRecord.Envelope.Unknowns.Any(x => string.Equals(x.UnknownType, "loop_budget_exhausted", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("Model pass audit smoke failed: loop guard escalation did not persist loop_budget_exhausted unknown.");
        }

        var loopGuardDefects = await runtimeDefectRepository.GetOpenAsync(ct: ct);
        if (!loopGuardDefects.Any(x => x.DedupeKey.Contains("loop_budget_exhausted", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("Model pass audit smoke failed: loop guard escalation did not emit runtime defect signal.");
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

    private static ModelPassEnvelope BuildLoopHistoryEnvelope(Guid runId)
    {
        var envelope = BuildEnvelope(runId);
        envelope.ScopeKey = "person:loop-guard";
        envelope.Scope = new ModelPassScope
        {
            ScopeType = "person_scope",
            ScopeRef = "person:loop-guard",
            AdditionalRefs = ["source_object:loop-guard"]
        };
        envelope.Target = new ModelPassTarget
        {
            TargetType = "person",
            TargetRef = "person:loop-guard"
        };
        envelope.Budget = ModelPassBudgetCatalog.ConsumeOneIteration(
            ModelPassBudgetCatalog.Create("stage6_bootstrap", "graph_init"));
        envelope.ResultStatus = ModelPassResultStatuses.NeedMoreData;
        envelope.Unknowns =
        [
            new ModelPassUnknown
            {
                UnknownType = "coverage_gap",
                Summary = "Additional source evidence is required for this scope.",
                RequiredAction = "collect_more_evidence"
            }
        ];
        return envelope;
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

        public Task<int> GetConsecutiveNeedMoreDataCountAsync(
            string scopeKey,
            string stage,
            string passFamily,
            CancellationToken ct = default)
        {
            var count = _records.Values
                .Where(x => string.Equals(x.Envelope.ScopeKey, scopeKey, StringComparison.Ordinal)
                    && string.Equals(x.Envelope.Stage, stage, StringComparison.Ordinal)
                    && string.Equals(x.Envelope.PassFamily, passFamily, StringComparison.Ordinal))
                .OrderByDescending(x => x.Envelope.StartedAtUtc)
                .ThenByDescending(x => x.Envelope.RunId)
                .Take(32)
                .TakeWhile(x => string.Equals(x.Envelope.ResultStatus, ModelPassResultStatuses.NeedMoreData, StringComparison.Ordinal))
                .Count();
            return Task.FromResult(count);
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

    private sealed class InMemoryRuntimeDefectRepository : IRuntimeDefectRepository
    {
        private readonly List<RuntimeDefectRecord> _records = [];

        public Task<RuntimeDefectRecord> UpsertAsync(RuntimeDefectUpsertRequest request, CancellationToken ct = default)
        {
            var existing = _records.FirstOrDefault(x => string.Equals(x.DedupeKey, request.DedupeKey, StringComparison.Ordinal));
            if (existing == null)
            {
                var escalation = RuntimeDefectEscalationPolicy.Resolve(request.DefectClass, request.Severity, 1);
                existing = new RuntimeDefectRecord
                {
                    Id = Guid.NewGuid(),
                    DefectClass = request.DefectClass,
                    Severity = request.Severity,
                    Status = RuntimeDefectStatuses.Open,
                    ScopeKey = request.ScopeKey,
                    DedupeKey = request.DedupeKey,
                    RunId = request.RunId,
                    ObjectType = request.ObjectType,
                    ObjectRef = request.ObjectRef,
                    Summary = request.Summary,
                    DetailsJson = request.DetailsJson,
                    OccurrenceCount = 1,
                    EscalationAction = escalation.EscalationAction,
                    EscalationReason = escalation.EscalationReason,
                    FirstSeenAtUtc = DateTime.UtcNow,
                    LastSeenAtUtc = DateTime.UtcNow,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow
                };
                _records.Add(existing);
            }
            else
            {
                existing.OccurrenceCount += 1;
                existing.RunId = request.RunId ?? existing.RunId;
                existing.Summary = request.Summary;
                existing.DetailsJson = request.DetailsJson;
                existing.UpdatedAtUtc = DateTime.UtcNow;
                existing.LastSeenAtUtc = DateTime.UtcNow;
                var escalation = RuntimeDefectEscalationPolicy.Resolve(existing.DefectClass, existing.Severity, existing.OccurrenceCount);
                existing.EscalationAction = escalation.EscalationAction;
                existing.EscalationReason = escalation.EscalationReason;
            }

            return Task.FromResult(existing);
        }

        public Task<List<RuntimeDefectRecord>> GetOpenAsync(int limit = 200, CancellationToken ct = default)
            => Task.FromResult(_records.Take(limit).ToList());

        public Task<int> ResolveOpenByDedupeKeyAsync(string dedupeKey, Guid? runId = null, CancellationToken ct = default)
        {
            var affected = 0;
            foreach (var row in _records.Where(x => string.Equals(x.DedupeKey, dedupeKey, StringComparison.Ordinal)
                                                    && string.Equals(x.Status, RuntimeDefectStatuses.Open, StringComparison.Ordinal)))
            {
                row.Status = RuntimeDefectStatuses.Resolved;
                row.ResolvedAtUtc = DateTime.UtcNow;
                row.RunId = runId ?? row.RunId;
                row.UpdatedAtUtc = DateTime.UtcNow;
                affected++;
            }

            return Task.FromResult(affected);
        }
    }

    private sealed class InMemoryClarificationBranchStateRepository : IClarificationBranchStateRepository
    {
        private readonly Dictionary<string, ClarificationBranchStateRecord> _records = [];

        public Task<ClarificationBranchStateRecord?> ApplyOutcomeAsync(
            ModelPassAuditRecord record,
            CancellationToken ct = default)
        {
            var envelope = record.Envelope;
            var branchFamily = string.Equals(envelope.Stage, "stage6_bootstrap", StringComparison.Ordinal)
                ? Stage8RecomputeTargetFamilies.Stage6Bootstrap
                : $"{envelope.Stage}:{envelope.PassFamily}";
            var branchKey = $"{envelope.ScopeKey}|{branchFamily}|{envelope.Target.TargetType}|{envelope.Target.TargetRef}";

            if (string.Equals(envelope.ResultStatus, ModelPassResultStatuses.NeedOperatorClarification, StringComparison.Ordinal))
            {
                if (!_records.TryGetValue(branchKey, out var row))
                {
                    row = new ClarificationBranchStateRecord
                    {
                        Id = Guid.NewGuid(),
                        ScopeKey = envelope.ScopeKey,
                        BranchFamily = branchFamily,
                        BranchKey = branchKey,
                        FirstBlockedAtUtc = DateTime.UtcNow
                    };
                    _records[branchKey] = row;
                }

                row.Stage = envelope.Stage;
                row.PassFamily = envelope.PassFamily;
                row.TargetType = envelope.Target.TargetType;
                row.TargetRef = envelope.Target.TargetRef;
                row.PersonId = envelope.PersonId;
                row.LastModelPassRunId = record.ModelPassRunId;
                row.Status = ClarificationBranchStatuses.Open;
                row.BlockReason = envelope.Unknowns.FirstOrDefault()?.Summary ?? envelope.OutputSummary.Summary;
                row.RequiredAction = envelope.Unknowns.FirstOrDefault()?.RequiredAction;
                row.DetailsJson = "{}";
                row.LastBlockedAtUtc = DateTime.UtcNow;
                row.ResolvedAtUtc = null;
                return Task.FromResult<ClarificationBranchStateRecord?>(row);
            }

            if (_records.TryGetValue(branchKey, out var existing)
                && string.Equals(existing.Status, ClarificationBranchStatuses.Open, StringComparison.Ordinal)
                && string.Equals(envelope.ResultStatus, ModelPassResultStatuses.ResultReady, StringComparison.Ordinal))
            {
                existing.LastModelPassRunId = record.ModelPassRunId;
                existing.Status = ClarificationBranchStatuses.Resolved;
                existing.ResolvedAtUtc = DateTime.UtcNow;
                return Task.FromResult<ClarificationBranchStateRecord?>(existing);
            }

            return Task.FromResult<ClarificationBranchStateRecord?>(null);
        }

        public Task<List<ClarificationBranchStateRecord>> GetOpenByScopeAsync(
            string scopeKey,
            CancellationToken ct = default)
        {
            var rows = _records.Values
                .Where(x => string.Equals(x.ScopeKey, scopeKey, StringComparison.Ordinal)
                    && string.Equals(x.Status, ClarificationBranchStatuses.Open, StringComparison.Ordinal))
                .OrderByDescending(x => x.LastBlockedAtUtc)
                .ToList();
            return Task.FromResult(rows);
        }

        public Task<List<ClarificationBranchStateRecord>> GetOpenByScopeAndFamilyAsync(
            string scopeKey,
            string branchFamily,
            CancellationToken ct = default)
        {
            var rows = _records.Values
                .Where(x => string.Equals(x.ScopeKey, scopeKey, StringComparison.Ordinal)
                    && string.Equals(x.BranchFamily, branchFamily, StringComparison.Ordinal)
                    && string.Equals(x.Status, ClarificationBranchStatuses.Open, StringComparison.Ordinal))
                .OrderByDescending(x => x.LastBlockedAtUtc)
                .ToList();
            return Task.FromResult(rows);
        }
    }
}
