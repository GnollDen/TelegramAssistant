using TgAssistant.Core.Models;

namespace TgAssistant.Infrastructure.Database;

public static class PassEnvelopeContractSmokeRunner
{
    public static void Run()
    {
        var readyEnvelope = BuildReadyEnvelope();
        ModelPassEnvelopeValidator.Validate(readyEnvelope);

        var scopeJson = ModelPassEnvelopeStorageCodec.SerializeScope(readyEnvelope.Scope);
        var sourceRefsJson = ModelPassEnvelopeStorageCodec.SerializeSourceRefs(readyEnvelope.SourceRefs);
        var truthSummaryJson = ModelPassEnvelopeStorageCodec.SerializeTruthSummary(readyEnvelope.TruthSummary);
        var conflictsJson = ModelPassEnvelopeStorageCodec.SerializeConflicts(readyEnvelope.Conflicts);
        var unknownsJson = ModelPassEnvelopeStorageCodec.SerializeUnknowns(readyEnvelope.Unknowns);
        var outputSummaryJson = ModelPassEnvelopeStorageCodec.SerializeOutputSummary(readyEnvelope.OutputSummary);

        var roundTripScope = ModelPassEnvelopeStorageCodec.DeserializeScope(scopeJson);
        var roundTripSourceRefs = ModelPassEnvelopeStorageCodec.DeserializeSourceRefs(sourceRefsJson);
        var roundTripTruthSummary = ModelPassEnvelopeStorageCodec.DeserializeTruthSummary(truthSummaryJson);
        var roundTripConflicts = ModelPassEnvelopeStorageCodec.DeserializeConflicts(conflictsJson);
        var roundTripUnknowns = ModelPassEnvelopeStorageCodec.DeserializeUnknowns(unknownsJson);
        var roundTripOutputSummary = ModelPassEnvelopeStorageCodec.DeserializeOutputSummary(outputSummaryJson);

        if (!string.Equals(roundTripScope.ScopeRef, readyEnvelope.Scope.ScopeRef, StringComparison.Ordinal)
            || roundTripSourceRefs.Count != readyEnvelope.SourceRefs.Count
            || !string.Equals(roundTripTruthSummary.TruthLayer, readyEnvelope.TruthSummary.TruthLayer, StringComparison.Ordinal)
            || roundTripConflicts.Count != readyEnvelope.Conflicts.Count
            || roundTripUnknowns.Count != readyEnvelope.Unknowns.Count
            || !string.Equals(roundTripOutputSummary.Summary, readyEnvelope.OutputSummary.Summary, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Pass envelope contract smoke failed: valid envelope did not round-trip through storage codec.");
        }

        var blockedEnvelope = BuildBlockedEnvelopeWithoutReason();
        var blockedRejected = false;
        try
        {
            ModelPassEnvelopeValidator.Validate(blockedEnvelope);
        }
        catch (InvalidOperationException)
        {
            blockedRejected = true;
        }

        if (!blockedRejected)
        {
            throw new InvalidOperationException("Pass envelope contract smoke failed: blocked_invalid_input envelope was accepted without blocked_reason.");
        }
    }

    private static ModelPassEnvelope BuildReadyEnvelope()
    {
        return new ModelPassEnvelope
        {
            Stage = "stage6_bootstrap",
            PassFamily = "graph_seed",
            RunKind = "synthetic_smoke",
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
            TriggerRef = "implement-004-a",
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
                TruthLayer = "canonical_truth",
                Summary = "Synthetic pass envelope smoke truth summary.",
                CanonicalRefs = ["evidence:smoke-1"]
            },
            Conflicts =
            [
                new ModelPassConflict
                {
                    ConflictType = "none_pending",
                    Summary = "No blocking conflicts for synthetic smoke."
                }
            ],
            Unknowns =
            [
                new ModelPassUnknown
                {
                    UnknownType = "coverage_gap",
                    Summary = "Known gap retained for contract round-trip.",
                    RequiredAction = "later_normalization"
                }
            ],
            ResultStatus = ModelPassResultStatuses.ResultReady,
            OutputSummary = new ModelPassOutputSummary
            {
                Summary = "Synthetic pass envelope completed successfully."
            }
        };
    }

    private static ModelPassEnvelope BuildBlockedEnvelopeWithoutReason()
    {
        var envelope = BuildReadyEnvelope();
        envelope.ResultStatus = ModelPassResultStatuses.BlockedInvalidInput;
        envelope.OutputSummary = new ModelPassOutputSummary
        {
            Summary = "Synthetic blocked output without explicit reason."
        };
        envelope.Unknowns = [];
        envelope.Conflicts = [];
        return envelope;
    }
}
