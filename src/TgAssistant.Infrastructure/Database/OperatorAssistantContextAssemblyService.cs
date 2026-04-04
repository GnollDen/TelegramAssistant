using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Infrastructure.Database;

public sealed class OperatorAssistantContextAssemblyService : IOperatorAssistantContextAssemblyService
{
    private readonly IResolutionReadService _resolutionReadService;
    private readonly IOperatorAssistantResponseGenerationService _responseGenerationService;

    public OperatorAssistantContextAssemblyService(
        IResolutionReadService resolutionReadService,
        IOperatorAssistantResponseGenerationService responseGenerationService)
    {
        _resolutionReadService = resolutionReadService;
        _responseGenerationService = responseGenerationService;
    }

    public async Task<OperatorAssistantResponseEnvelope> BuildBoundedResponseAsync(
        OperatorAssistantContextAssemblyRequest request,
        DateTime? generatedAtUtc = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var session = request.Session ?? new OperatorSessionContext();
        if (request.TrackedPersonId == Guid.Empty)
        {
            throw new InvalidOperationException(OperatorAssistantFailureReasons.MissingActiveTrackedPerson);
        }

        if (session.ActiveTrackedPersonId != Guid.Empty && session.ActiveTrackedPersonId != request.TrackedPersonId)
        {
            throw new InvalidOperationException(OperatorAssistantFailureReasons.TrackedPersonScopeMismatch);
        }

        var scopeKey = NormalizeRequired(request.ScopeKey, "scope_missing");
        var requestedScopeItemKey = NormalizeOptional(request.ScopeItemKey);
        var sessionScopeItemKey = NormalizeOptional(session.ActiveScopeItemKey);

        if (!string.IsNullOrWhiteSpace(sessionScopeItemKey)
            && !string.IsNullOrWhiteSpace(requestedScopeItemKey)
            && !string.Equals(sessionScopeItemKey, requestedScopeItemKey, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(OperatorAssistantFailureReasons.SessionScopeItemMismatch);
        }

        var activeScopeItemKey = requestedScopeItemKey ?? sessionScopeItemKey;
        var observedAtUtc = (generatedAtUtc ?? DateTime.UtcNow).ToUniversalTime();
        var queueResult = await _resolutionReadService.GetQueueAsync(
            new ResolutionQueueRequest
            {
                TrackedPersonId = request.TrackedPersonId,
                SortBy = ResolutionQueueSortFields.Priority,
                SortDirection = ResolutionSortDirections.Desc,
                Limit = Math.Clamp(request.QueueLimit, 1, 50)
            },
            ct);
        var auditEntries = new List<OperatorAssistantReadModelAuditEntry>
        {
            new()
            {
                ReadModel = "resolution_queue",
                Bounded = queueResult.ScopeBound,
                TrackedPersonId = request.TrackedPersonId,
                ScopeKey = NormalizeOptional(queueResult.ScopeKey) ?? scopeKey,
                ScopeItemKey = string.Empty,
                RecordCount = queueResult.Items.Count,
                OperatorSessionId = NormalizeOptional(session.OperatorSessionId) ?? string.Empty,
                ObservedAtUtc = observedAtUtc,
                Notes = queueResult.ScopeFailureReason
            }
        };

        if (!queueResult.ScopeBound)
        {
            throw new InvalidOperationException($"{OperatorAssistantFailureReasons.ReadModelScopeUnbounded}:{queueResult.ScopeFailureReason ?? "queue_scope_unbounded"}");
        }

        if (queueResult.TrackedPersonId != request.TrackedPersonId
            || !string.Equals(NormalizeOptional(queueResult.ScopeKey), scopeKey, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(OperatorAssistantFailureReasons.ReadModelScopeMismatch);
        }

        ResolutionDetailResult? detailResult = null;
        if (!string.IsNullOrWhiteSpace(activeScopeItemKey))
        {
            detailResult = await _resolutionReadService.GetDetailAsync(
                new ResolutionDetailRequest
                {
                    TrackedPersonId = request.TrackedPersonId,
                    ScopeItemKey = activeScopeItemKey,
                    EvidenceLimit = Math.Clamp(request.EvidenceLimit, 1, 10),
                    EvidenceSortBy = ResolutionEvidenceSortFields.ObservedAt,
                    EvidenceSortDirection = ResolutionSortDirections.Desc
                },
                ct);

            auditEntries.Add(new OperatorAssistantReadModelAuditEntry
            {
                ReadModel = "resolution_detail",
                Bounded = detailResult.ScopeBound && detailResult.ItemFound,
                TrackedPersonId = request.TrackedPersonId,
                ScopeKey = scopeKey,
                ScopeItemKey = activeScopeItemKey,
                RecordCount = detailResult.Item?.Evidence.Count ?? 0,
                OperatorSessionId = NormalizeOptional(session.OperatorSessionId) ?? string.Empty,
                ObservedAtUtc = observedAtUtc,
                Notes = detailResult.ScopeBound ? (detailResult.ItemFound ? null : OperatorAssistantFailureReasons.ReadModelScopeItemNotFound) : detailResult.ScopeFailureReason
            });

            if (!detailResult.ScopeBound)
            {
                throw new InvalidOperationException($"{OperatorAssistantFailureReasons.ReadModelScopeUnbounded}:{detailResult.ScopeFailureReason ?? "detail_scope_unbounded"}");
            }

            if (!detailResult.ItemFound)
            {
                throw new InvalidOperationException(OperatorAssistantFailureReasons.ReadModelScopeItemNotFound);
            }
        }

        var queueTop = queueResult.Items.FirstOrDefault();
        var detailItem = detailResult?.Item;
        var recommendationAction = detailItem?.RecommendedNextAction ?? queueTop?.RecommendedNextAction ?? ResolutionActionTypes.OpenWeb;
        var trustPercent = ResolveTrustPercent(detailItem?.TrustFactor ?? queueTop?.TrustFactor ?? 0.6f);

        var known = BuildKnownStatements(queueResult, detailItem);
        var means = BuildInferenceStatements(queueResult, detailItem);
        var recommendationText = BuildRecommendationText(recommendationAction, detailItem?.Title, queueResult.TotalOpenCount);

        var response = _responseGenerationService.BuildResponse(
            new OperatorAssistantResponseGenerationRequest
            {
                OperatorIdentity = request.OperatorIdentity,
                Session = request.Session ?? session,
                TrackedPersonId = request.TrackedPersonId,
                ScopeKey = scopeKey,
                Question = NormalizeRequired(request.Question, "question_missing"),
                ShortAnswer = new OperatorAssistantStatementInput
                {
                    Text = BuildShortAnswer(queueResult, detailItem),
                    TruthLabel = OperatorAssistantTruthLabels.Inference,
                    TrustPercent = trustPercent
                },
                WhatIsKnown = known,
                WhatItMeans = means,
                Recommendation = new OperatorAssistantStatementInput
                {
                    Text = recommendationText,
                    TruthLabel = OperatorAssistantTruthLabels.Recommendation,
                    TrustPercent = trustPercent,
                    EvidenceRefs = detailItem?.Evidence.Select(x => $"evidence:{x.EvidenceItemId:D}").ToList() ?? []
                },
                TrustPercent = trustPercent,
                OpenInWebEnabled = request.OpenInWebEnabled,
                OpenInWebTargetApi = request.OpenInWebTargetApi,
                OpenInWebScopeItemKey = activeScopeItemKey ?? string.Empty,
                OpenInWebActiveMode = request.OpenInWebActiveMode
            },
            generatedAtUtc: observedAtUtc);

        response.Guardrails.ReadModelBounded = true;
        response.Guardrails.ReadModelAudit = auditEntries;

        return response;
    }

    private static string BuildShortAnswer(ResolutionQueueResult queue, ResolutionItemDetail? detail)
    {
        if (detail != null)
        {
            return $"{detail.Title} is currently {detail.Status} for the active tracked-person scope.";
        }

        return queue.TotalOpenCount == 0
            ? "No open resolution blockers are projected for the active tracked-person scope."
            : $"{queue.TotalOpenCount} open resolution item(s) are projected for the active tracked-person scope.";
    }

    private static List<OperatorAssistantStatementInput> BuildKnownStatements(ResolutionQueueResult queue, ResolutionItemDetail? detail)
    {
        var statements = new List<OperatorAssistantStatementInput>
        {
            new()
            {
                Text = $"Active bounded scope `{queue.ScopeKey}` currently has {queue.TotalOpenCount} open resolution item(s).",
                TruthLabel = OperatorAssistantTruthLabels.Fact,
                TrustPercent = 100
            }
        };

        if (detail != null)
        {
            statements.Add(new OperatorAssistantStatementInput
            {
                Text = $"Selected scope item `{detail.ScopeItemKey}` is `{detail.ItemType}` with status `{detail.Status}` and {detail.EvidenceCount} evidence link(s).",
                TruthLabel = OperatorAssistantTruthLabels.Fact,
                TrustPercent = ResolveTrustPercent(detail.TrustFactor)
            });

            foreach (var evidence in detail.Evidence.Take(2))
            {
                statements.Add(new OperatorAssistantStatementInput
                {
                    Text = evidence.Summary,
                    TruthLabel = OperatorAssistantTruthLabels.Fact,
                    TrustPercent = ResolveTrustPercent(evidence.TrustFactor),
                    EvidenceRefs = [$"evidence:{evidence.EvidenceItemId:D}"]
                });
            }
        }

        return statements;
    }

    private static List<OperatorAssistantStatementInput> BuildInferenceStatements(ResolutionQueueResult queue, ResolutionItemDetail? detail)
    {
        var highOrCriticalCount = queue.Items.Count(x =>
            string.Equals(x.Priority, ResolutionItemPriorities.High, StringComparison.Ordinal)
            || string.Equals(x.Priority, ResolutionItemPriorities.Critical, StringComparison.Ordinal));
        var inferenceText = highOrCriticalCount > 0
            ? $"{highOrCriticalCount} high-priority branch(es) suggest near-term operator follow-up is likely required."
            : "Current projected branches suggest stable bounded flow with no immediate high-priority escalations.";

        var statements = new List<OperatorAssistantStatementInput>
        {
            new()
            {
                Text = inferenceText,
                TruthLabel = OperatorAssistantTruthLabels.Inference,
                TrustPercent = highOrCriticalCount > 0 ? 74 : 63
            }
        };

        if (detail != null)
        {
            statements.Add(new OperatorAssistantStatementInput
            {
                Text = $"Resolving `{detail.ScopeItemKey}` should reduce unresolved pressure in `{detail.AffectedFamily}`.",
                TruthLabel = OperatorAssistantTruthLabels.Hypothesis,
                TrustPercent = 58
            });
        }

        return statements;
    }

    private static string BuildRecommendationText(string action, string? title, int totalOpenCount)
    {
        var normalizedAction = NormalizeOptional(action) ?? ResolutionActionTypes.OpenWeb;
        return normalizedAction switch
        {
            ResolutionActionTypes.Clarify => $"Capture clarification for {title ?? "the active scope item"} before re-running bounded recompute.",
            ResolutionActionTypes.Approve => $"Approve {title ?? "the active scope item"} if supporting evidence remains consistent in this bounded scope.",
            ResolutionActionTypes.Reject => $"Reject {title ?? "the active scope item"} with an explicit bounded explanation and trigger recompute.",
            ResolutionActionTypes.Defer => $"Defer {title ?? "the active scope item"} only with an explanation and a concrete follow-up checkpoint.",
            ResolutionActionTypes.Evidence => $"Open evidence for {title ?? "the active scope item"} to close remaining uncertainty before deciding.",
            _ => totalOpenCount > 0
                ? "Use Open in Web for deeper analysis of the top unresolved bounded item before taking action."
                : "Keep monitoring this tracked-person scope and reopen assistant mode after new evidence arrives."
        };
    }

    private static int ResolveTrustPercent(float trustFactor)
        => Math.Clamp((int)Math.Round(Math.Clamp(trustFactor, 0f, 1f) * 100f), 0, 100);

    private static string NormalizeRequired(string? value, string fallback)
        => NormalizeOptional(value) ?? fallback;

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
