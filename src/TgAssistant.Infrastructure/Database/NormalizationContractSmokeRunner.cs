using TgAssistant.Core.Models;

namespace TgAssistant.Infrastructure.Database;

public static class NormalizationContractSmokeRunner
{
    public static void Run()
    {
        var normalizer = new ModelOutputNormalizer();

        var readyResult = normalizer.Normalize(new ModelNormalizationRequest
        {
            Envelope = BuildEnvelope(),
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
                  ],
                  "inferences": [
                    {
                      "inference_type": "response_rhythm",
                      "subject_type": "person",
                      "subject_ref": "person:smoke",
                      "summary": "Replies are steady in the sampled scope.",
                      "truth_layer": "derived_but_durable",
                      "confidence": 0.76,
                      "evidence_refs": ["evidence:smoke-1"]
                    }
                  ],
                  "hypotheses": [
                    {
                      "hypothesis_type": "availability_pattern",
                      "subject_type": "person",
                      "subject_ref": "person:smoke",
                      "statement": "Availability may improve on weekdays.",
                      "truth_layer": "proposal_layer",
                      "confidence": 0.52,
                      "evidence_refs": ["evidence:smoke-2"]
                    }
                  ],
                  "conflicts": [
                    {
                      "conflict_type": "timeline_gap",
                      "summary": "A short interval still lacks supporting evidence.",
                      "truth_layer": "conflicted_or_obsolete",
                      "related_object_ref": "episode:smoke-gap",
                      "confidence": 0.41,
                      "evidence_refs": ["evidence:smoke-3"]
                    }
                  ]
                }
                """
        });

        if (!string.Equals(readyResult.Status, ModelPassResultStatuses.ResultReady, StringComparison.Ordinal)
            || readyResult.CandidateCounts.Facts != 1
            || readyResult.CandidateCounts.Inferences != 1
            || readyResult.CandidateCounts.Hypotheses != 1
            || readyResult.CandidateCounts.Conflicts != 1)
        {
            throw new InvalidOperationException("Normalization contract smoke failed: valid typed JSON did not normalize to result_ready candidates.");
        }

        var downgradedResult = normalizer.Normalize(new ModelNormalizationRequest
        {
            Envelope = BuildEnvelope(),
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
        });

        if (!string.Equals(downgradedResult.Status, ModelPassResultStatuses.NeedOperatorClarification, StringComparison.Ordinal)
            || downgradedResult.CandidateCounts.Facts != 1
            || downgradedResult.Issues.Count == 0)
        {
            throw new InvalidOperationException("Normalization contract smoke failed: malformed candidate did not downgrade result_ready output.");
        }

        var blockedResult = normalizer.Normalize(new ModelNormalizationRequest
        {
            Envelope = BuildEnvelope(),
            RawModelOutput = "This is free-form model prose and must never enter durable state directly."
        });

        if (!string.Equals(blockedResult.Status, ModelPassResultStatuses.BlockedInvalidInput, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(blockedResult.BlockedReason))
        {
            throw new InvalidOperationException("Normalization contract smoke failed: raw model prose was not blocked.");
        }
    }

    private static ModelPassEnvelope BuildEnvelope()
    {
        return new ModelPassEnvelope
        {
            Stage = "stage6_bootstrap",
            PassFamily = "graph_seed",
            RunKind = "synthetic_normalization_smoke",
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
            TriggerRef = "implement-004-b",
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
                Summary = "Synthetic normalization smoke summary.",
                CanonicalRefs = ["evidence:smoke-1"]
            },
            Budget = ModelPassBudgetCatalog.ConsumeOneIteration(
                ModelPassBudgetCatalog.Create("stage6_bootstrap", "graph_seed")),
            Conflicts = [],
            Unknowns =
            [
                new ModelPassUnknown
                {
                    UnknownType = "coverage_gap",
                    Summary = "Synthetic gap retained for smoke.",
                    RequiredAction = "manual_review"
                }
            ],
            ResultStatus = ModelPassResultStatuses.ResultReady,
            OutputSummary = new ModelPassOutputSummary
            {
                Summary = "Synthetic normalization smoke envelope."
            }
        };
    }
}
