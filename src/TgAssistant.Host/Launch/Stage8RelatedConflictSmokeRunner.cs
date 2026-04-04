using TgAssistant.Core.Legacy.Models;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database;

namespace TgAssistant.Host.Launch;

public static class Stage8RelatedConflictSmokeRunner
{
    public static void Run()
    {
        var queueItemId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var metadataCreate = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var metadataRefresh = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var metadataUnchanged = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var metadataResolve = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var request = new Stage8RelatedConflictReevaluationRequest
        {
            QueueItemId = queueItemId,
            ScopeKey = "chat:12345",
            PersonId = Guid.Parse("55555555-5555-5555-5555-555555555555"),
            TargetFamily = Stage8RecomputeTargetFamilies.DossierProfile,
            TargetRef = "person:55555555-5555-5555-5555-555555555555",
            ResultStatus = ModelPassResultStatuses.ResultReady,
            ModelPassRunId = Guid.Parse("66666666-6666-6666-6666-666666666666")
        };

        var unchangedSummary = "Profile 'person:55555555-5555-5555-5555-555555555555:profile:global' still carries 1 contradiction marker after recompute.";
        var existingConflicts = new List<ConflictRecord>
        {
            new()
            {
                Id = Guid.Parse("77777777-7777-7777-7777-777777777777"),
                ConflictType = Stage8RelatedConflictTypes.RecomputedContradiction,
                ObjectAType = "durable_object_metadata",
                ObjectAId = metadataRefresh.ToString("D"),
                ObjectBType = Stage7DurableObjectFamilies.Dossier,
                ObjectBId = "person:55555555-5555-5555-5555-555555555555:dossier:person_dossier",
                Summary = "stale",
                Severity = "medium",
                Status = "resolved"
            },
            new()
            {
                Id = Guid.Parse("88888888-8888-8888-8888-888888888888"),
                ConflictType = Stage8RelatedConflictTypes.RecomputedContradiction,
                ObjectAType = "durable_object_metadata",
                ObjectAId = metadataUnchanged.ToString("D"),
                ObjectBType = Stage7DurableObjectFamilies.Profile,
                ObjectBId = "person:55555555-5555-5555-5555-555555555555:profile:global",
                Summary = unchangedSummary,
                Severity = "medium",
                Status = "open"
            },
            new()
            {
                Id = Guid.Parse("99999999-9999-9999-9999-999999999999"),
                ConflictType = Stage8RelatedConflictTypes.RecomputedContradiction,
                ObjectAType = "durable_object_metadata",
                ObjectAId = metadataResolve.ToString("D"),
                ObjectBType = Stage7DurableObjectFamilies.Dossier,
                ObjectBId = "person:55555555-5555-5555-5555-555555555555:dossier:obsolete",
                Summary = "obsolete",
                Severity = "high",
                Status = "open"
            }
        };
        var snapshots = new List<Stage8RelatedConflictSnapshot>
        {
            new()
            {
                MetadataId = metadataCreate,
                ObjectFamily = Stage7DurableObjectFamilies.Dossier,
                ObjectKey = "person:55555555-5555-5555-5555-555555555555:dossier:person_dossier",
                PromotionState = Stage8PromotionStates.PromotionBlocked,
                ContradictionCount = 2
            },
            new()
            {
                MetadataId = metadataRefresh,
                ObjectFamily = Stage7DurableObjectFamilies.Dossier,
                ObjectKey = "person:55555555-5555-5555-5555-555555555555:dossier:person_dossier",
                PromotionState = Stage8PromotionStates.PromotionBlocked,
                ContradictionCount = 1
            },
            new()
            {
                MetadataId = metadataUnchanged,
                ObjectFamily = Stage7DurableObjectFamilies.Profile,
                ObjectKey = "person:55555555-5555-5555-5555-555555555555:profile:global",
                PromotionState = Stage8PromotionStates.Pending,
                ContradictionCount = 1
            }
        };

        var operations = Stage8RelatedConflictReevaluationPlanner.Plan(snapshots, existingConflicts, request);
        AssertCount(operations, Stage8RelatedConflictOperationKinds.Create, 1);
        AssertCount(operations, Stage8RelatedConflictOperationKinds.Refresh, 1);
        AssertCount(operations, Stage8RelatedConflictOperationKinds.Resolve, 1);
        AssertCount(operations, Stage8RelatedConflictOperationKinds.Unchanged, 1);

        var created = operations.Single(x => string.Equals(x.Kind, Stage8RelatedConflictOperationKinds.Create, StringComparison.Ordinal));
        if (!string.Equals(created.Severity, "high", StringComparison.Ordinal)
            || !created.Reason.Contains(queueItemId.ToString("D"), StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Stage8 related conflict smoke failed: create operation did not preserve severity or traceable reason.");
        }

        var resolved = operations.Single(x => string.Equals(x.Kind, Stage8RelatedConflictOperationKinds.Resolve, StringComparison.Ordinal));
        if (!resolved.Reason.Contains("auto_closed_after_recompute_no_remaining_contradictions", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Stage8 related conflict smoke failed: resolve operation did not record the expected auto-close reason.");
        }
    }

    private static void AssertCount(
        IReadOnlyCollection<Stage8RelatedConflictOperation> operations,
        string kind,
        int expected)
    {
        var actual = operations.Count(x => string.Equals(x.Kind, kind, StringComparison.Ordinal));
        if (actual != expected)
        {
            throw new InvalidOperationException($"Stage8 related conflict smoke failed: expected {expected} '{kind}' operations but got {actual}.");
        }
    }
}
