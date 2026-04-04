using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database;

namespace TgAssistant.Host.Launch;

public static class Opint006AssistantContextAssemblySmokeRunner
{
    public static async Task RunAsync(CancellationToken ct = default)
    {
        var trackedPersonId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var service = new OperatorAssistantContextAssemblyService(
            new StubResolutionReadService(trackedPersonId),
            new OperatorAssistantResponseGenerationService());

        var session = new OperatorSessionContext
        {
            OperatorSessionId = "telegram:opint-006-b2-smoke",
            Surface = OperatorSurfaceTypes.Telegram,
            ActiveTrackedPersonId = trackedPersonId,
            ActiveScopeItemKey = "resolution:clarification:opint-006-b2",
            ActiveMode = OperatorModeTypes.ResolutionDetail,
            AuthenticatedAtUtc = DateTime.UtcNow,
            LastSeenAtUtc = DateTime.UtcNow
        };

        var response = await service.BuildBoundedResponseAsync(
            new OperatorAssistantContextAssemblyRequest
            {
                TrackedPersonId = trackedPersonId,
                ScopeKey = "chat:opint-006-b2-smoke",
                Question = "What is the bounded status?",
                Session = session,
                ScopeItemKey = session.ActiveScopeItemKey
            },
            generatedAtUtc: DateTime.Parse("2026-04-04T00:00:00Z").ToUniversalTime(),
            ct);

        if (!response.Guardrails.ScopeBounded || response.Guardrails.McpDependent || !response.Guardrails.ReadModelBounded)
        {
            throw new InvalidOperationException("OPINT-006-B2 smoke failed: expected bounded non-MCP guardrails.");
        }

        if (response.Guardrails.ReadModelAudit.Count < 2
            || response.Guardrails.ReadModelAudit.Any(x => !x.Bounded)
            || response.Guardrails.ReadModelAudit.Any(x => x.TrackedPersonId != trackedPersonId)
            || response.Guardrails.ReadModelAudit.Any(x => !string.Equals(x.ScopeKey, "chat:opint-006-b2-smoke", StringComparison.Ordinal))
            || response.Guardrails.ReadModelAudit.Any(x => !string.Equals(x.OperatorSessionId, session.OperatorSessionId, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("OPINT-006-B2 smoke failed: read-model audit entries are missing or not scope-bounded.");
        }

        var rendered = new OperatorAssistantResponseGenerationService().RenderTelegram(response);
        if (!rendered.Contains("Trust: ", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("OPINT-006-B2 smoke failed: rendered output missing terminal trust line.");
        }

        try
        {
            await service.BuildBoundedResponseAsync(
                new OperatorAssistantContextAssemblyRequest
                {
                    TrackedPersonId = trackedPersonId,
                    ScopeKey = "chat:cross-scope",
                    Question = "Cross scope should fail",
                    Session = session,
                    ScopeItemKey = session.ActiveScopeItemKey
                },
                generatedAtUtc: DateTime.Parse("2026-04-04T00:00:00Z").ToUniversalTime(),
                ct);
            throw new InvalidOperationException("OPINT-006-B2 smoke failed: cross-scope request was not rejected.");
        }
        catch (InvalidOperationException ex) when (string.Equals(ex.Message, OperatorAssistantFailureReasons.ReadModelScopeMismatch, StringComparison.Ordinal))
        {
            // Expected bounded-guardrail rejection.
        }
    }

    private sealed class StubResolutionReadService : IResolutionReadService
    {
        private readonly Guid _trackedPersonId;

        public StubResolutionReadService(Guid trackedPersonId)
        {
            _trackedPersonId = trackedPersonId;
        }

        public Task<ResolutionQueueResult> GetQueueAsync(ResolutionQueueRequest request, CancellationToken ct = default)
        {
            var scopeBound = request.TrackedPersonId == _trackedPersonId;
            var scopeKey = string.Equals(request.TrackedPersonId.ToString("D"), _trackedPersonId.ToString("D"), StringComparison.Ordinal)
                ? "chat:opint-006-b2-smoke"
                : "chat:cross-scope";

            return Task.FromResult(new ResolutionQueueResult
            {
                ScopeBound = scopeBound,
                ScopeFailureReason = scopeBound ? null : "tracked_person_scope_mismatch",
                TrackedPersonId = request.TrackedPersonId,
                ScopeKey = scopeKey,
                TrackedPersonDisplayName = "Tracked Person",
                TotalOpenCount = 1,
                FilteredCount = 1,
                Items =
                [
                    new ResolutionItemSummary
                    {
                        ScopeItemKey = "resolution:clarification:opint-006-b2",
                        ItemType = ResolutionItemTypes.Clarification,
                        Title = "Clarification required for profile branch",
                        Summary = "A clarification branch is waiting for operator input.",
                        WhyItMatters = "Progress is blocked until clarification is captured.",
                        AffectedFamily = Stage8RecomputeTargetFamilies.DossierProfile,
                        AffectedObjectRef = "scope:chat:opint-006-b2-smoke",
                        TrustFactor = 0.77f,
                        Status = ResolutionItemStatuses.Open,
                        EvidenceCount = 2,
                        UpdatedAtUtc = DateTime.Parse("2026-04-04T00:00:00Z").ToUniversalTime(),
                        Priority = ResolutionItemPriorities.High,
                        RecommendedNextAction = ResolutionActionTypes.Clarify,
                        AvailableActions = [ResolutionActionTypes.Clarify, ResolutionActionTypes.OpenWeb]
                    }
                ]
            });
        }

        public Task<ResolutionDetailResult> GetDetailAsync(ResolutionDetailRequest request, CancellationToken ct = default)
        {
            if (!string.Equals(request.ScopeItemKey, "resolution:clarification:opint-006-b2", StringComparison.Ordinal))
            {
                return Task.FromResult(new ResolutionDetailResult
                {
                    ScopeBound = true,
                    ItemFound = false
                });
            }

            return Task.FromResult(new ResolutionDetailResult
            {
                ScopeBound = true,
                ItemFound = true,
                Item = new ResolutionItemDetail
                {
                    ScopeItemKey = request.ScopeItemKey,
                    ItemType = ResolutionItemTypes.Clarification,
                    Title = "Clarification required for profile branch",
                    Summary = "A clarification branch is waiting for operator input.",
                    WhyItMatters = "Progress is blocked until clarification is captured.",
                    AffectedFamily = Stage8RecomputeTargetFamilies.DossierProfile,
                    AffectedObjectRef = "scope:chat:opint-006-b2-smoke",
                    TrustFactor = 0.77f,
                    Status = ResolutionItemStatuses.Open,
                    EvidenceCount = 2,
                    UpdatedAtUtc = DateTime.Parse("2026-04-04T00:00:00Z").ToUniversalTime(),
                    Priority = ResolutionItemPriorities.High,
                    RecommendedNextAction = ResolutionActionTypes.Clarify,
                    AvailableActions = [ResolutionActionTypes.Clarify, ResolutionActionTypes.OpenWeb],
                    Evidence =
                    [
                        new ResolutionEvidenceSummary
                        {
                            EvidenceItemId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                            Summary = "Recent messages show delayed replies after unresolved scheduling ambiguity.",
                            TrustFactor = 0.84f,
                            ObservedAtUtc = DateTime.Parse("2026-04-03T12:00:00Z").ToUniversalTime(),
                            SourceRef = "message:1",
                            SourceLabel = "message"
                        }
                    ]
                }
            });
        }
    }
}
