using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Infrastructure.Database;

public sealed class ResolutionReadProjectionService : IResolutionReadService
{
    private const string ActiveStatus = "active";

    private static readonly IReadOnlyDictionary<string, string[]> TargetFamilyToObjectFamilies =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [Stage8RecomputeTargetFamilies.DossierProfile] =
            [
                Stage7DurableObjectFamilies.Dossier,
                Stage7DurableObjectFamilies.Profile
            ],
            [Stage8RecomputeTargetFamilies.PairDynamics] = [Stage7DurableObjectFamilies.PairDynamics],
            [Stage8RecomputeTargetFamilies.TimelineObjects] =
            [
                Stage7DurableObjectFamilies.Event,
                Stage7DurableObjectFamilies.TimelineEpisode,
                Stage7DurableObjectFamilies.StoryArc
            ]
        };

    private readonly IDbContextFactory<TgAssistantDbContext> _dbFactory;

    public ResolutionReadProjectionService(IDbContextFactory<TgAssistantDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<ResolutionQueueResult> GetQueueAsync(
        ResolutionQueueRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.TrackedPersonId == Guid.Empty)
        {
            return new ResolutionQueueResult
            {
                ScopeBound = false,
                ScopeFailureReason = "tracked_person_id_required"
            };
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var trackedPerson = await LoadTrackedPersonScopeAsync(db, request.TrackedPersonId, ct);
        if (trackedPerson == null)
        {
            return new ResolutionQueueResult
            {
                ScopeBound = false,
                ScopeFailureReason = "tracked_person_not_found_or_inactive"
            };
        }

        var runtimeState = await LoadRuntimeStateAsync(db, ct);
        var durableContexts = await LoadDurableContextsAsync(db, trackedPerson, ct);
        var clarificationBranches = await db.ClarificationBranchStates
            .AsNoTracking()
            .Where(x => x.ScopeKey == trackedPerson.ScopeKey
                && x.Status == ClarificationBranchStatuses.Open
                && (x.PersonId == null || x.PersonId == trackedPerson.PersonId))
            .OrderByDescending(x => x.LastBlockedAtUtc)
            .ToListAsync(ct);
        var queueItems = await db.Stage8RecomputeQueueItems
            .AsNoTracking()
            .Where(x => x.ScopeKey == trackedPerson.ScopeKey
                && (x.PersonId == null || x.PersonId == trackedPerson.PersonId))
            .Where(x =>
                x.Status != Stage8RecomputeQueueStatuses.Completed
                || x.Status == Stage8RecomputeQueueStatuses.Failed
                || x.LastError != null
                || x.LastResultStatus == ModelPassResultStatuses.NeedOperatorClarification
                || x.LastResultStatus == ModelPassResultStatuses.NeedMoreData
                || x.LastResultStatus == ModelPassResultStatuses.BlockedInvalidInput)
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Take(200)
            .ToListAsync(ct);
        var runtimeDefects = await db.RuntimeDefects
            .AsNoTracking()
            .Where(x => x.ScopeKey == trackedPerson.ScopeKey && x.Status == RuntimeDefectStatuses.Open)
            .OrderByDescending(x => x.UpdatedAtUtc)
            .ToListAsync(ct);

        var projected = BuildProjectedItems(
            trackedPerson,
            runtimeState,
            durableContexts,
            clarificationBranches,
            queueItems,
            runtimeDefects);

        var filtered = string.IsNullOrWhiteSpace(request.ItemTypeFilter)
            ? projected
            : projected
                .Where(x => string.Equals(x.Summary.ItemType, request.ItemTypeFilter.Trim(), StringComparison.Ordinal))
                .ToList();

        var ordered = OrderItems(filtered);
        var boundedLimit = Math.Clamp(request.Limit, 1, 200);

        return new ResolutionQueueResult
        {
            ScopeBound = true,
            TrackedPersonId = trackedPerson.PersonId,
            ScopeKey = trackedPerson.ScopeKey,
            TrackedPersonDisplayName = trackedPerson.DisplayName,
            RuntimeState = runtimeState,
            TotalOpenCount = ordered.Count,
            Items = ordered
                .Take(boundedLimit)
                .Select(x => x.Summary)
                .ToList()
        };
    }

    public async Task<ResolutionDetailResult> GetDetailAsync(
        ResolutionDetailRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.TrackedPersonId == Guid.Empty)
        {
            return new ResolutionDetailResult
            {
                ScopeBound = false,
                ScopeFailureReason = "tracked_person_id_required"
            };
        }

        if (string.IsNullOrWhiteSpace(request.ScopeItemKey))
        {
            return new ResolutionDetailResult
            {
                ScopeBound = false,
                ScopeFailureReason = "scope_item_key_required"
            };
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var trackedPerson = await LoadTrackedPersonScopeAsync(db, request.TrackedPersonId, ct);
        if (trackedPerson == null)
        {
            return new ResolutionDetailResult
            {
                ScopeBound = false,
                ScopeFailureReason = "tracked_person_not_found_or_inactive"
            };
        }

        var runtimeState = await LoadRuntimeStateAsync(db, ct);
        var durableContexts = await LoadDurableContextsAsync(db, trackedPerson, ct);
        var clarificationBranches = await db.ClarificationBranchStates
            .AsNoTracking()
            .Where(x => x.ScopeKey == trackedPerson.ScopeKey
                && x.Status == ClarificationBranchStatuses.Open
                && (x.PersonId == null || x.PersonId == trackedPerson.PersonId))
            .OrderByDescending(x => x.LastBlockedAtUtc)
            .ToListAsync(ct);
        var queueItems = await db.Stage8RecomputeQueueItems
            .AsNoTracking()
            .Where(x => x.ScopeKey == trackedPerson.ScopeKey
                && (x.PersonId == null || x.PersonId == trackedPerson.PersonId))
            .Where(x =>
                x.Status != Stage8RecomputeQueueStatuses.Completed
                || x.Status == Stage8RecomputeQueueStatuses.Failed
                || x.LastError != null
                || x.LastResultStatus == ModelPassResultStatuses.NeedOperatorClarification
                || x.LastResultStatus == ModelPassResultStatuses.NeedMoreData
                || x.LastResultStatus == ModelPassResultStatuses.BlockedInvalidInput)
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Take(200)
            .ToListAsync(ct);
        var runtimeDefects = await db.RuntimeDefects
            .AsNoTracking()
            .Where(x => x.ScopeKey == trackedPerson.ScopeKey && x.Status == RuntimeDefectStatuses.Open)
            .OrderByDescending(x => x.UpdatedAtUtc)
            .ToListAsync(ct);

        var projected = BuildProjectedItems(
            trackedPerson,
            runtimeState,
            durableContexts,
            clarificationBranches,
            queueItems,
            runtimeDefects);
        var match = projected.FirstOrDefault(x =>
            string.Equals(x.Summary.ScopeItemKey, request.ScopeItemKey.Trim(), StringComparison.Ordinal));

        if (match == null)
        {
            return new ResolutionDetailResult
            {
                ScopeBound = true,
                ItemFound = false
            };
        }

        var evidence = await LoadEvidenceAsync(
            db,
            trackedPerson,
            match.DurableMetadataIds,
            match.UseTrackedPersonEvidence,
            request.EvidenceLimit,
            ct);

        return new ResolutionDetailResult
        {
            ScopeBound = true,
            ItemFound = true,
            Item = new ResolutionItemDetail
            {
                ScopeItemKey = match.Summary.ScopeItemKey,
                ItemType = match.Summary.ItemType,
                Title = match.Summary.Title,
                Summary = match.Summary.Summary,
                WhyItMatters = match.Summary.WhyItMatters,
                AffectedFamily = match.Summary.AffectedFamily,
                AffectedObjectRef = match.Summary.AffectedObjectRef,
                TrustFactor = match.Summary.TrustFactor,
                Status = match.Summary.Status,
                EvidenceCount = match.Summary.EvidenceCount,
                UpdatedAtUtc = match.Summary.UpdatedAtUtc,
                Priority = match.Summary.Priority,
                RecommendedNextAction = match.Summary.RecommendedNextAction,
                SourceKind = match.SourceKind,
                SourceRef = match.SourceRef,
                RequiredAction = match.RequiredAction,
                Notes = match.Notes,
                Evidence = evidence
            }
        };
    }

    private static List<ProjectedResolutionItem> BuildProjectedItems(
        TrackedPersonScope trackedPerson,
        ResolutionRuntimeStateSummary? runtimeState,
        IReadOnlyList<DurableContext> durableContexts,
        IReadOnlyList<DbClarificationBranchState> clarificationBranches,
        IReadOnlyList<DbStage8RecomputeQueueItem> queueItems,
        IReadOnlyList<DbRuntimeDefect> runtimeDefects)
    {
        var items = new List<ProjectedResolutionItem>();
        var branchesByFamily = clarificationBranches
            .GroupBy(x => x.BranchFamily, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.OrderByDescending(y => y.LastBlockedAtUtc).ToList(), StringComparer.Ordinal);

        items.AddRange(BuildClarificationItems(trackedPerson, durableContexts, clarificationBranches));
        items.AddRange(BuildBlockedBranchItems(trackedPerson, durableContexts, queueItems, branchesByFamily));
        items.AddRange(BuildMissingDataItems(trackedPerson, durableContexts, queueItems, runtimeDefects));
        items.AddRange(BuildContradictionItems(trackedPerson, durableContexts));
        items.AddRange(BuildPromotionReviewItems(trackedPerson, durableContexts));
        items.AddRange(BuildRuntimeReviewItems(trackedPerson, runtimeState, durableContexts, runtimeDefects));

        return items;
    }

    private static List<ProjectedResolutionItem> BuildClarificationItems(
        TrackedPersonScope trackedPerson,
        IReadOnlyList<DurableContext> durableContexts,
        IReadOnlyList<DbClarificationBranchState> clarificationBranches)
    {
        var items = new List<ProjectedResolutionItem>();
        foreach (var branch in clarificationBranches)
        {
            var targetContexts = GetTargetContexts(branch.BranchFamily, durableContexts);
            var title = $"Clarification needed for {DescribeFamily(branch.BranchFamily)}";
            var summary = string.IsNullOrWhiteSpace(branch.BlockReason)
                ? "Operator clarification is required before this branch can continue."
                : branch.BlockReason.Trim();
            var whyItMatters = branch.RequiredAction != null
                ? $"Stage8 recompute is waiting for the operator to '{branch.RequiredAction}'."
                : $"Stage8 recompute for {DescribeFamily(branch.BranchFamily)} cannot continue until this ambiguity is resolved.";

            var notes = new List<ResolutionDetailNote>
            {
                new()
                {
                    Kind = "branch",
                    Text = $"Blocked branch family: {branch.BranchFamily}."
                }
            };
            if (!string.IsNullOrWhiteSpace(branch.RequiredAction))
            {
                notes.Add(new ResolutionDetailNote
                {
                    Kind = "required_action",
                    Text = branch.RequiredAction.Trim()
                });
            }

            notes.AddRange(ParseClarificationDetails(branch.DetailsJson));

            items.Add(new ProjectedResolutionItem
            {
                Summary = new ResolutionItemSummary
                {
                    ScopeItemKey = BuildItemKey(ResolutionItemTypes.Clarification, "clarification_branch", branch.Id.ToString("D")),
                    ItemType = ResolutionItemTypes.Clarification,
                    Title = title,
                    Summary = summary,
                    WhyItMatters = whyItMatters,
                    AffectedFamily = branch.BranchFamily,
                    AffectedObjectRef = branch.TargetRef,
                    TrustFactor = targetContexts.Count == 0 ? 0.5f : AverageTrust(targetContexts),
                    Status = ResolutionItemStatuses.Open,
                    EvidenceCount = ResolveEvidenceCount(trackedPerson, targetContexts),
                    UpdatedAtUtc = branch.LastBlockedAtUtc,
                    Priority = string.Equals(branch.BranchFamily, Stage8RecomputeTargetFamilies.Stage6Bootstrap, StringComparison.Ordinal)
                        ? ResolutionItemPriorities.Critical
                        : ResolutionItemPriorities.High,
                    RecommendedNextAction = "clarify"
                },
                SourceKind = "clarification_branch",
                SourceRef = branch.Id.ToString("D"),
                RequiredAction = branch.RequiredAction,
                Notes = notes,
                DurableMetadataIds = targetContexts.Select(x => x.MetadataId).ToList(),
                UseTrackedPersonEvidence = targetContexts.Count == 0
            });
        }

        return items;
    }

    private static List<ProjectedResolutionItem> BuildBlockedBranchItems(
        TrackedPersonScope trackedPerson,
        IReadOnlyList<DurableContext> durableContexts,
        IReadOnlyList<DbStage8RecomputeQueueItem> queueItems,
        IReadOnlyDictionary<string, List<DbClarificationBranchState>> branchesByFamily)
    {
        var items = new List<ProjectedResolutionItem>();
        var candidates = queueItems
            .Where(x =>
                x.Status == Stage8RecomputeQueueStatuses.Pending
                || x.Status == Stage8RecomputeQueueStatuses.Leased
                || x.LastResultStatus == ModelPassResultStatuses.NeedOperatorClarification)
            .GroupBy(x => $"{x.TargetFamily}|{x.TargetRef}", StringComparer.Ordinal)
            .Select(x => x.OrderByDescending(y => y.UpdatedAtUtc).First())
            .ToList();

        foreach (var queueItem in candidates)
        {
            if (!branchesByFamily.TryGetValue(queueItem.TargetFamily, out var matchingBranches)
                || matchingBranches.Count == 0)
            {
                continue;
            }

            var branch = matchingBranches[0];
            var targetContexts = GetTargetContexts(queueItem.TargetFamily, durableContexts);
            var notes = new List<ResolutionDetailNote>
            {
                new()
                {
                    Kind = "queue",
                    Text = $"Queue status: {queueItem.Status}; attempts {queueItem.AttemptCount}/{queueItem.MaxAttempts}."
                }
            };
            if (!string.IsNullOrWhiteSpace(queueItem.TriggerKind))
            {
                notes.Add(new ResolutionDetailNote
                {
                    Kind = "trigger",
                    Text = string.IsNullOrWhiteSpace(queueItem.TriggerRef)
                        ? queueItem.TriggerKind
                        : $"{queueItem.TriggerKind} ({queueItem.TriggerRef})"
                });
            }
            if (!string.IsNullOrWhiteSpace(branch.BlockReason))
            {
                notes.Add(new ResolutionDetailNote
                {
                    Kind = "branch_reason",
                    Text = branch.BlockReason.Trim()
                });
            }

            items.Add(new ProjectedResolutionItem
            {
                Summary = new ResolutionItemSummary
                {
                    ScopeItemKey = BuildItemKey(ResolutionItemTypes.BlockedBranch, "stage8_queue", queueItem.Id.ToString("D")),
                    ItemType = ResolutionItemTypes.BlockedBranch,
                    Title = $"{DescribeFamily(queueItem.TargetFamily)} recompute blocked",
                    Summary = string.IsNullOrWhiteSpace(branch.BlockReason)
                        ? "A queued recompute branch is blocked by unresolved clarification."
                        : branch.BlockReason.Trim(),
                    WhyItMatters = $"The queued Stage8 recompute for {DescribeFamily(queueItem.TargetFamily)} cannot complete until the clarification branch is resolved.",
                    AffectedFamily = queueItem.TargetFamily,
                    AffectedObjectRef = queueItem.TargetRef,
                    TrustFactor = targetContexts.Count == 0 ? 0.45f : AverageTrust(targetContexts),
                    Status = queueItem.Status == Stage8RecomputeQueueStatuses.Leased
                        ? ResolutionItemStatuses.Running
                        : ResolutionItemStatuses.Blocked,
                    EvidenceCount = ResolveEvidenceCount(trackedPerson, targetContexts),
                    UpdatedAtUtc = Max(queueItem.UpdatedAtUtc, branch.LastBlockedAtUtc),
                    Priority = string.Equals(queueItem.TargetFamily, Stage8RecomputeTargetFamilies.Stage6Bootstrap, StringComparison.Ordinal)
                        ? ResolutionItemPriorities.Critical
                        : ResolutionItemPriorities.High,
                    RecommendedNextAction = "clarify"
                },
                SourceKind = "stage8_queue",
                SourceRef = queueItem.Id.ToString("D"),
                RequiredAction = branch.RequiredAction,
                Notes = notes,
                DurableMetadataIds = targetContexts.Select(x => x.MetadataId).ToList(),
                UseTrackedPersonEvidence = targetContexts.Count == 0
            });
        }

        return items;
    }

    private static List<ProjectedResolutionItem> BuildMissingDataItems(
        TrackedPersonScope trackedPerson,
        IReadOnlyList<DurableContext> durableContexts,
        IReadOnlyList<DbStage8RecomputeQueueItem> queueItems,
        IReadOnlyList<DbRuntimeDefect> runtimeDefects)
    {
        var items = new List<ProjectedResolutionItem>();
        var queueCandidates = queueItems
            .Where(x => x.LastResultStatus == ModelPassResultStatuses.NeedMoreData)
            .GroupBy(x => $"{x.TargetFamily}|{x.TargetRef}", StringComparer.Ordinal)
            .Select(x => x.OrderByDescending(y => y.UpdatedAtUtc).First())
            .ToList();

        foreach (var queueItem in queueCandidates)
        {
            var targetContexts = GetTargetContexts(queueItem.TargetFamily, durableContexts);
            var notes = new List<ResolutionDetailNote>
            {
                new()
                {
                    Kind = "queue",
                    Text = $"Latest result status: {queueItem.LastResultStatus}; attempts {queueItem.AttemptCount}/{queueItem.MaxAttempts}."
                }
            };
            if (!string.IsNullOrWhiteSpace(queueItem.TriggerKind))
            {
                notes.Add(new ResolutionDetailNote
                {
                    Kind = "trigger",
                    Text = string.IsNullOrWhiteSpace(queueItem.TriggerRef)
                        ? queueItem.TriggerKind
                        : $"{queueItem.TriggerKind} ({queueItem.TriggerRef})"
                });
            }

            items.Add(new ProjectedResolutionItem
            {
                Summary = new ResolutionItemSummary
                {
                    ScopeItemKey = BuildItemKey(ResolutionItemTypes.MissingData, "stage8_queue", queueItem.Id.ToString("D")),
                    ItemType = ResolutionItemTypes.MissingData,
                    Title = $"{DescribeFamily(queueItem.TargetFamily)} needs more evidence",
                    Summary = "The latest bounded recompute could not form a durable result from the available evidence.",
                    WhyItMatters = $"This {DescribeFamily(queueItem.TargetFamily)} branch will remain incomplete until more evidence or operator-provided clarification arrives.",
                    AffectedFamily = queueItem.TargetFamily,
                    AffectedObjectRef = queueItem.TargetRef,
                    TrustFactor = targetContexts.Count == 0 ? 0.35f : Math.Min(AverageTrust(targetContexts), 0.55f),
                    Status = ResolutionItemStatuses.Open,
                    EvidenceCount = ResolveEvidenceCount(trackedPerson, targetContexts),
                    UpdatedAtUtc = queueItem.UpdatedAtUtc,
                    Priority = queueItem.AttemptCount >= 3
                        ? ResolutionItemPriorities.High
                        : ResolutionItemPriorities.Medium,
                    RecommendedNextAction = "evidence"
                },
                SourceKind = "stage8_queue",
                SourceRef = queueItem.Id.ToString("D"),
                Notes = notes,
                DurableMetadataIds = targetContexts.Select(x => x.MetadataId).ToList(),
                UseTrackedPersonEvidence = targetContexts.Count == 0
            });
        }

        var queueFamilies = queueCandidates
            .Select(x => $"{x.TargetFamily}|{x.TargetRef}")
            .ToHashSet(StringComparer.Ordinal);
        var defectCandidates = runtimeDefects
            .Where(x => string.Equals(x.DefectClass, RuntimeDefectClasses.Ingestion, StringComparison.Ordinal)
                || string.Equals(x.DefectClass, RuntimeDefectClasses.Data, StringComparison.Ordinal))
            .Where(x => !string.IsNullOrWhiteSpace(x.ObjectRef))
            .ToList();

        foreach (var defect in defectCandidates)
        {
            var familyKey = defect.ObjectType != null && defect.ObjectRef != null
                ? $"{defect.ObjectType}|{defect.ObjectRef}"
                : string.Empty;
            if (!string.IsNullOrWhiteSpace(familyKey) && queueFamilies.Contains(familyKey))
            {
                continue;
            }

            var targetContexts = GetTargetContexts(defect.ObjectType, durableContexts);
            items.Add(new ProjectedResolutionItem
            {
                Summary = new ResolutionItemSummary
                {
                    ScopeItemKey = BuildItemKey(ResolutionItemTypes.MissingData, "runtime_defect", defect.Id.ToString("D")),
                    ItemType = ResolutionItemTypes.MissingData,
                    Title = $"{DescribeFamily(defect.ObjectType)} data gap",
                    Summary = defect.Summary,
                    WhyItMatters = "The active bounded scope is missing enough substrate or normalized input to keep this branch moving.",
                    AffectedFamily = defect.ObjectType ?? "scope",
                    AffectedObjectRef = defect.ObjectRef ?? $"scope:{trackedPerson.ScopeKey}",
                    TrustFactor = 0.8f,
                    Status = ResolutionItemStatuses.AttentionRequired,
                    EvidenceCount = ResolveEvidenceCount(trackedPerson, targetContexts),
                    UpdatedAtUtc = defect.UpdatedAtUtc,
                    Priority = MapSeverityToPriority(defect.Severity),
                    RecommendedNextAction = "evidence"
                },
                SourceKind = "runtime_defect",
                SourceRef = defect.Id.ToString("D"),
                Notes =
                [
                    new ResolutionDetailNote
                    {
                        Kind = "defect",
                        Text = $"{defect.DefectClass} / {defect.Severity}; escalation {defect.EscalationAction}."
                    }
                ],
                DurableMetadataIds = targetContexts.Select(x => x.MetadataId).ToList(),
                UseTrackedPersonEvidence = targetContexts.Count == 0
            });
        }

        return items;
    }

    private static List<ProjectedResolutionItem> BuildContradictionItems(
        TrackedPersonScope trackedPerson,
        IReadOnlyList<DurableContext> durableContexts)
    {
        var items = new List<ProjectedResolutionItem>();
        foreach (var context in durableContexts.Where(x => x.ContradictionCount > 0))
        {
            var notes = new List<ResolutionDetailNote>
            {
                new()
                {
                    Kind = "contradiction_count",
                    Text = $"{context.ContradictionCount} contradiction marker(s) remain attached to this durable object."
                }
            };
            notes.AddRange(ParseContradictionNotes(context.ContradictionMarkersJson));

            items.Add(new ProjectedResolutionItem
            {
                Summary = new ResolutionItemSummary
                {
                    ScopeItemKey = BuildItemKey(ResolutionItemTypes.Contradiction, "durable_object_metadata", context.MetadataId.ToString("D")),
                    ItemType = ResolutionItemTypes.Contradiction,
                    Title = $"{DescribeFamily(context.ObjectFamily)} contradiction",
                    Summary = $"{DescribeFamily(context.ObjectFamily)} for {trackedPerson.DisplayName} still carries contradiction markers.",
                    WhyItMatters = string.Equals(context.PromotionState, Stage8PromotionStates.PromotionBlocked, StringComparison.Ordinal)
                        ? "Contradictions are blocking promotion into canonical truth."
                        : "Contradictions lower trust and make downstream resolution decisions less stable.",
                    AffectedFamily = context.ObjectFamily,
                    AffectedObjectRef = context.ObjectKey,
                    TrustFactor = context.TrustFactor,
                    Status = string.Equals(context.PromotionState, Stage8PromotionStates.PromotionBlocked, StringComparison.Ordinal)
                        ? ResolutionItemStatuses.Blocked
                        : ResolutionItemStatuses.Open,
                    EvidenceCount = context.EvidenceCount,
                    UpdatedAtUtc = context.UpdatedAtUtc,
                    Priority = context.ContradictionCount > 1 || string.Equals(context.PromotionState, Stage8PromotionStates.PromotionBlocked, StringComparison.Ordinal)
                        ? ResolutionItemPriorities.High
                        : ResolutionItemPriorities.Medium,
                    RecommendedNextAction = "evidence"
                },
                SourceKind = "durable_object_metadata",
                SourceRef = context.MetadataId.ToString("D"),
                Notes = notes,
                DurableMetadataIds = [context.MetadataId]
            });
        }

        return items;
    }

    private static List<ProjectedResolutionItem> BuildPromotionReviewItems(
        TrackedPersonScope trackedPerson,
        IReadOnlyList<DurableContext> durableContexts)
    {
        var items = new List<ProjectedResolutionItem>();
        foreach (var context in durableContexts.Where(x =>
                     string.Equals(x.PromotionState, Stage8PromotionStates.PromotionBlocked, StringComparison.Ordinal)))
        {
            var notes = new List<ResolutionDetailNote>
            {
                new()
                {
                    Kind = "promotion_state",
                    Text = $"Promotion state: {context.PromotionState}; truth layer: {context.TruthLayer}."
                }
            };
            if (!string.IsNullOrWhiteSpace(context.MetadataSummary))
            {
                notes.Add(new ResolutionDetailNote
                {
                    Kind = "summary",
                    Text = context.MetadataSummary
                });
            }

            items.Add(new ProjectedResolutionItem
            {
                Summary = new ResolutionItemSummary
                {
                    ScopeItemKey = BuildItemKey(ResolutionItemTypes.Review, "durable_object_metadata", context.MetadataId.ToString("D")),
                    ItemType = ResolutionItemTypes.Review,
                    Title = $"{DescribeFamily(context.ObjectFamily)} promotion blocked",
                    Summary = $"{DescribeFamily(context.ObjectFamily)} for {trackedPerson.DisplayName} is not eligible for canonical promotion.",
                    WhyItMatters = context.ContradictionCount > 0
                        ? "Contradiction pressure is blocking promotion and needs operator review."
                        : "This durable object is held below canonical truth and needs operator review before downstream surfaces rely on it.",
                    AffectedFamily = context.ObjectFamily,
                    AffectedObjectRef = context.ObjectKey,
                    TrustFactor = context.TrustFactor,
                    Status = ResolutionItemStatuses.AttentionRequired,
                    EvidenceCount = context.EvidenceCount,
                    UpdatedAtUtc = context.UpdatedAtUtc,
                    Priority = context.ContradictionCount > 0
                        ? ResolutionItemPriorities.High
                        : ResolutionItemPriorities.Medium,
                    RecommendedNextAction = "open-web"
                },
                SourceKind = "durable_object_metadata",
                SourceRef = context.MetadataId.ToString("D"),
                Notes = notes,
                DurableMetadataIds = [context.MetadataId]
            });
        }

        return items;
    }

    private static List<ProjectedResolutionItem> BuildRuntimeReviewItems(
        TrackedPersonScope trackedPerson,
        ResolutionRuntimeStateSummary? runtimeState,
        IReadOnlyList<DurableContext> durableContexts,
        IReadOnlyList<DbRuntimeDefect> runtimeDefects)
    {
        var items = new List<ProjectedResolutionItem>();

        if (runtimeState != null && !string.Equals(runtimeState.State, RuntimeControlStates.Normal, StringComparison.Ordinal))
        {
            items.Add(new ProjectedResolutionItem
            {
                Summary = new ResolutionItemSummary
                {
                    ScopeItemKey = BuildItemKey(ResolutionItemTypes.Review, "runtime_control_state", runtimeState.State),
                    ItemType = ResolutionItemTypes.Review,
                    Title = $"Runtime operating in {runtimeState.State}",
                    Summary = runtimeState.Reason,
                    WhyItMatters = "Runtime control state is directly affecting which Stage8 work can run or promote inside this tracked-person scope.",
                    AffectedFamily = "runtime_control",
                    AffectedObjectRef = $"scope:{trackedPerson.ScopeKey}",
                    TrustFactor = 1f,
                    Status = runtimeState.State == RuntimeControlStates.Degraded
                        ? ResolutionItemStatuses.Degraded
                        : ResolutionItemStatuses.AttentionRequired,
                    EvidenceCount = runtimeDefects.Count,
                    UpdatedAtUtc = runtimeState.ActivatedAtUtc,
                    Priority = MapRuntimeStateToPriority(runtimeState.State),
                    RecommendedNextAction = "open-web"
                },
                SourceKind = "runtime_control_state",
                SourceRef = runtimeState.State,
                Notes =
                [
                    new ResolutionDetailNote
                    {
                        Kind = "runtime_state",
                        Text = $"{runtimeState.State} from {runtimeState.Source}."
                    }
                ],
                UseTrackedPersonEvidence = true
            });
        }

        foreach (var defect in runtimeDefects)
        {
            if (ShouldSkipRuntimeDefect(defect))
            {
                continue;
            }

            var targetContexts = GetTargetContexts(defect.ObjectType, durableContexts);
            items.Add(new ProjectedResolutionItem
            {
                Summary = new ResolutionItemSummary
                {
                    ScopeItemKey = BuildItemKey(ResolutionItemTypes.Review, "runtime_defect", defect.Id.ToString("D")),
                    ItemType = ResolutionItemTypes.Review,
                    Title = $"{NormalizeDefectTitle(defect)}",
                    Summary = defect.Summary,
                    WhyItMatters = "Operator review is required because runtime defects are affecting data integrity, promotion safety, or bounded execution reliability.",
                    AffectedFamily = defect.ObjectType ?? defect.DefectClass,
                    AffectedObjectRef = defect.ObjectRef ?? $"scope:{trackedPerson.ScopeKey}",
                    TrustFactor = 1f,
                    Status = defect.EscalationAction == RuntimeDefectEscalationActions.Observe
                        ? ResolutionItemStatuses.Open
                        : ResolutionItemStatuses.AttentionRequired,
                    EvidenceCount = ResolveEvidenceCount(trackedPerson, targetContexts),
                    UpdatedAtUtc = defect.UpdatedAtUtc,
                    Priority = MapSeverityToPriority(defect.Severity),
                    RecommendedNextAction = "open-web"
                },
                SourceKind = "runtime_defect",
                SourceRef = defect.Id.ToString("D"),
                Notes =
                [
                    new ResolutionDetailNote
                    {
                        Kind = "defect",
                        Text = $"{defect.DefectClass} / {defect.Severity}; occurrences {defect.OccurrenceCount}."
                    },
                    new ResolutionDetailNote
                    {
                        Kind = "escalation",
                        Text = $"{defect.EscalationAction}: {defect.EscalationReason}"
                    }
                ],
                DurableMetadataIds = targetContexts.Select(x => x.MetadataId).ToList(),
                UseTrackedPersonEvidence = targetContexts.Count == 0
            });
        }

        return items;
    }

    private static bool ShouldSkipRuntimeDefect(DbRuntimeDefect defect)
    {
        if (string.Equals(defect.DefectClass, RuntimeDefectClasses.Ingestion, StringComparison.Ordinal)
            || string.Equals(defect.DefectClass, RuntimeDefectClasses.Data, StringComparison.Ordinal))
        {
            return true;
        }

        if (defect.DedupeKey.EndsWith("|clarification_gate", StringComparison.Ordinal)
            || defect.DedupeKey.EndsWith("|need_more_data", StringComparison.Ordinal)
            || defect.DedupeKey.EndsWith("|promotion_blocked", StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }

    private static List<ProjectedResolutionItem> OrderItems(List<ProjectedResolutionItem> items)
    {
        return items
            .OrderByDescending(x => PriorityRank(x.Summary.Priority))
            .ThenByDescending(x => x.Summary.UpdatedAtUtc)
            .ToList();
    }

    private static int PriorityRank(string priority)
    {
        return priority switch
        {
            ResolutionItemPriorities.Critical => 4,
            ResolutionItemPriorities.High => 3,
            ResolutionItemPriorities.Medium => 2,
            ResolutionItemPriorities.Low => 1,
            _ => 0
        };
    }

    private static string MapSeverityToPriority(string severity)
    {
        return severity switch
        {
            RuntimeDefectSeverities.Critical => ResolutionItemPriorities.Critical,
            RuntimeDefectSeverities.High => ResolutionItemPriorities.High,
            RuntimeDefectSeverities.Medium => ResolutionItemPriorities.Medium,
            _ => ResolutionItemPriorities.Low
        };
    }

    private static string MapRuntimeStateToPriority(string state)
    {
        return state switch
        {
            RuntimeControlStates.SafeMode => ResolutionItemPriorities.Critical,
            RuntimeControlStates.ReviewOnly => ResolutionItemPriorities.High,
            RuntimeControlStates.BudgetProtected => ResolutionItemPriorities.High,
            RuntimeControlStates.PromotionBlocked => ResolutionItemPriorities.High,
            RuntimeControlStates.Degraded => ResolutionItemPriorities.Medium,
            _ => ResolutionItemPriorities.Low
        };
    }

    private static float AverageTrust(IReadOnlyList<DurableContext> contexts)
    {
        if (contexts.Count == 0)
        {
            return 0.5f;
        }

        var trust = contexts.Average(x => x.TrustFactor);
        return Clamp01((float)Math.Round(trust, 2, MidpointRounding.AwayFromZero));
    }

    private static int ResolveEvidenceCount(TrackedPersonScope trackedPerson, IReadOnlyList<DurableContext> contexts)
        => contexts.Count == 0
            ? trackedPerson.EvidenceCount
            : contexts.Sum(x => x.EvidenceCount);

    private static List<DurableContext> GetTargetContexts(
        string? targetFamily,
        IReadOnlyList<DurableContext> durableContexts)
    {
        if (string.IsNullOrWhiteSpace(targetFamily))
        {
            return [];
        }

        if (TargetFamilyToObjectFamilies.TryGetValue(targetFamily.Trim(), out var families))
        {
            return durableContexts
                .Where(x => families.Contains(x.ObjectFamily, StringComparer.Ordinal))
                .ToList();
        }

        return durableContexts
            .Where(x => string.Equals(x.ObjectFamily, targetFamily.Trim(), StringComparison.Ordinal))
            .ToList();
    }

    private static string BuildItemKey(string itemType, string sourceKind, string sourceId)
        => $"{itemType}:{sourceKind}:{sourceId}";

    private static DateTime Max(DateTime left, DateTime right)
        => left >= right ? left : right;

    private static string NormalizeDefectTitle(DbRuntimeDefect defect)
    {
        if (!string.IsNullOrWhiteSpace(defect.ObjectType))
        {
            return $"{DescribeFamily(defect.ObjectType)} runtime review";
        }

        return $"{defect.DefectClass} runtime review";
    }

    private static string DescribeFamily(string? family)
    {
        return family switch
        {
            Stage8RecomputeTargetFamilies.Stage6Bootstrap => "bootstrap scope",
            Stage8RecomputeTargetFamilies.DossierProfile => "dossier/profile",
            Stage8RecomputeTargetFamilies.PairDynamics => "pair dynamics",
            Stage8RecomputeTargetFamilies.TimelineObjects => "timeline",
            Stage7DurableObjectFamilies.Dossier => "dossier",
            Stage7DurableObjectFamilies.Profile => "profile",
            Stage7DurableObjectFamilies.Event => "event",
            Stage7DurableObjectFamilies.TimelineEpisode => "timeline episode",
            Stage7DurableObjectFamilies.StoryArc => "story arc",
            "runtime_control" => "runtime control",
            _ => string.IsNullOrWhiteSpace(family) ? "scope" : family.Trim()
        };
    }

    private async Task<TrackedPersonScope?> LoadTrackedPersonScopeAsync(
        TgAssistantDbContext db,
        Guid trackedPersonId,
        CancellationToken ct)
    {
        var row = await db.Persons
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.Id == trackedPersonId
                && x.Status == ActiveStatus
                && x.PersonType == "tracked_person",
                ct);
        if (row == null)
        {
            return null;
        }

        var evidenceCount = await db.EvidenceItemPersonLinks
            .AsNoTracking()
            .Where(x => x.ScopeKey == row.ScopeKey && x.PersonId == row.Id)
            .Select(x => x.EvidenceItemId)
            .Distinct()
            .CountAsync(ct);

        return new TrackedPersonScope
        {
            PersonId = row.Id,
            ScopeKey = row.ScopeKey,
            DisplayName = string.IsNullOrWhiteSpace(row.DisplayName) ? row.CanonicalName : row.DisplayName,
            EvidenceCount = evidenceCount
        };
    }

    private static async Task<ResolutionRuntimeStateSummary?> LoadRuntimeStateAsync(
        TgAssistantDbContext db,
        CancellationToken ct)
    {
        var row = await db.RuntimeControlStates
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.IsActive, ct);
        if (row == null)
        {
            return null;
        }

        return new ResolutionRuntimeStateSummary
        {
            State = row.State,
            Reason = row.Reason,
            Source = row.Source,
            ActivatedAtUtc = row.ActivatedAtUtc
        };
    }

    private static async Task<List<DurableContext>> LoadDurableContextsAsync(
        TgAssistantDbContext db,
        TrackedPersonScope trackedPerson,
        CancellationToken ct)
    {
        var rows = await db.DurableObjectMetadata
            .AsNoTracking()
            .Where(x => x.ScopeKey == trackedPerson.ScopeKey
                && (x.OwnerPersonId == trackedPerson.PersonId || x.RelatedPersonId == trackedPerson.PersonId))
            .ToListAsync(ct);
        if (rows.Count == 0)
        {
            return [];
        }

        var metadataIds = rows.Select(x => x.Id).ToList();
        var evidenceCounts = await db.DurableObjectEvidenceLinks
            .AsNoTracking()
            .Where(x => metadataIds.Contains(x.DurableObjectMetadataId))
            .GroupBy(x => x.DurableObjectMetadataId)
            .Select(x => new
            {
                MetadataId = x.Key,
                EvidenceCount = x.Select(y => y.EvidenceItemId).Distinct().Count()
            })
            .ToListAsync(ct);
        var evidenceCountLookup = evidenceCounts.ToDictionary(x => x.MetadataId, x => x.EvidenceCount);

        var contexts = new List<DurableContext>(rows.Count);
        foreach (var row in rows)
        {
            var evidenceCount = evidenceCountLookup.GetValueOrDefault(row.Id);
            if (evidenceCount == 0)
            {
                evidenceCount = TryReadInt(row.MetadataJson, "evidence_count");
            }

            contexts.Add(new DurableContext
            {
                MetadataId = row.Id,
                ObjectFamily = row.ObjectFamily,
                ObjectKey = row.ObjectKey,
                PromotionState = row.PromotionState,
                TruthLayer = row.TruthLayer,
                Confidence = row.Confidence,
                Freshness = row.Freshness,
                Stability = row.Stability,
                ContradictionMarkersJson = row.ContradictionMarkersJson,
                ContradictionCount = CountJsonArrayItems(row.ContradictionMarkersJson),
                EvidenceCount = evidenceCount,
                UpdatedAtUtc = row.UpdatedAt,
                TrustFactor = Clamp01((float)Math.Round((row.Confidence + row.Freshness + row.Stability) / 3f, 2, MidpointRounding.AwayFromZero)),
                MetadataSummary = BuildDurableSummary(trackedPerson.DisplayName, row.ObjectFamily, row.MetadataJson, evidenceCount)
            });
        }

        return contexts;
    }

    private static string BuildDurableSummary(
        string trackedPersonDisplayName,
        string objectFamily,
        string metadataJson,
        int evidenceCount)
    {
        var contradictionCount = TryReadInt(metadataJson, "contradiction_count");
        var ambiguityCount = TryReadInt(metadataJson, "ambiguity_count");
        var linkedPersonCount = TryReadInt(metadataJson, "linked_person_count");

        return $"{DescribeFamily(objectFamily)} for {trackedPersonDisplayName}: {evidenceCount} evidence, {contradictionCount} contradictions, {ambiguityCount} ambiguities, {linkedPersonCount} linked people.";
    }

    private static List<ResolutionDetailNote> ParseClarificationDetails(string? detailsJson)
    {
        if (string.IsNullOrWhiteSpace(detailsJson))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(detailsJson);
            var notes = new List<ResolutionDetailNote>();
            if (document.RootElement.TryGetProperty("unknowns", out var unknowns)
                && unknowns.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in unknowns.EnumerateArray().Take(5))
                {
                    if (!TryReadString(item, "summary", out var summary))
                    {
                        continue;
                    }

                    notes.Add(new ResolutionDetailNote
                    {
                        Kind = "unknown",
                        Text = summary
                    });
                }
            }

            if (document.RootElement.TryGetProperty("normalization_issues", out var issues)
                && issues.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in issues.EnumerateArray().Take(3))
                {
                    if (!TryReadString(item, "summary", out var summary))
                    {
                        continue;
                    }

                    notes.Add(new ResolutionDetailNote
                    {
                        Kind = "normalization_issue",
                        Text = summary
                    });
                }
            }

            return notes;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static List<ResolutionDetailNote> ParseContradictionNotes(string? contradictionMarkersJson)
    {
        if (string.IsNullOrWhiteSpace(contradictionMarkersJson))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(contradictionMarkersJson);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var notes = new List<ResolutionDetailNote>();
            foreach (var item in document.RootElement.EnumerateArray().Take(3))
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    notes.Add(new ResolutionDetailNote
                    {
                        Kind = "contradiction_marker",
                        Text = item.GetString() ?? string.Empty
                    });
                    continue;
                }

                if (TryReadString(item, "summary", out var summary))
                {
                    notes.Add(new ResolutionDetailNote
                    {
                        Kind = "contradiction_marker",
                        Text = summary
                    });
                }
            }

            return notes;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static async Task<List<ResolutionEvidenceSummary>> LoadEvidenceAsync(
        TgAssistantDbContext db,
        TrackedPersonScope trackedPerson,
        IReadOnlyList<Guid> durableMetadataIds,
        bool useTrackedPersonEvidence,
        int evidenceLimit,
        CancellationToken ct)
    {
        var boundedLimit = Math.Clamp(evidenceLimit, 1, 20);

        if (durableMetadataIds.Count > 0)
        {
            var rows = await (from link in db.DurableObjectEvidenceLinks.AsNoTracking()
                              join evidence in db.EvidenceItems.AsNoTracking()
                                  on link.EvidenceItemId equals evidence.Id
                              join sourceObject in db.SourceObjects.AsNoTracking()
                                  on evidence.SourceObjectId equals sourceObject.Id into sourceObjects
                              from sourceObject in sourceObjects.DefaultIfEmpty()
                              where durableMetadataIds.Contains(link.DurableObjectMetadataId)
                              orderby evidence.ObservedAt descending, evidence.CreatedAt descending
                              select new
                              {
                                  evidence.Id,
                                  evidence.SummaryText,
                                  evidence.Confidence,
                                  evidence.ObservedAt,
                                  SourceRef = sourceObject != null ? sourceObject.SourceRef : null,
                                  SourceLabel = sourceObject != null ? sourceObject.DisplayLabel : null
                              })
                .Take(boundedLimit)
                .ToListAsync(ct);

            return rows.Select(x => new ResolutionEvidenceSummary
            {
                EvidenceItemId = x.Id,
                Summary = string.IsNullOrWhiteSpace(x.SummaryText) ? x.Id.ToString("D") : x.SummaryText.Trim(),
                TrustFactor = Clamp01(x.Confidence),
                ObservedAtUtc = x.ObservedAt?.ToUniversalTime(),
                SourceRef = x.SourceRef,
                SourceLabel = x.SourceLabel
            }).ToList();
        }

        if (!useTrackedPersonEvidence)
        {
            return [];
        }

        var personRows = await (from personLink in db.EvidenceItemPersonLinks.AsNoTracking()
                                join evidence in db.EvidenceItems.AsNoTracking()
                                    on personLink.EvidenceItemId equals evidence.Id
                                join sourceObject in db.SourceObjects.AsNoTracking()
                                    on evidence.SourceObjectId equals sourceObject.Id into sourceObjects
                                from sourceObject in sourceObjects.DefaultIfEmpty()
                                where personLink.ScopeKey == trackedPerson.ScopeKey
                                    && personLink.PersonId == trackedPerson.PersonId
                                orderby evidence.ObservedAt descending, evidence.CreatedAt descending
                                select new
                                {
                                    evidence.Id,
                                    evidence.SummaryText,
                                    evidence.Confidence,
                                    evidence.ObservedAt,
                                    SourceRef = sourceObject != null ? sourceObject.SourceRef : null,
                                    SourceLabel = sourceObject != null ? sourceObject.DisplayLabel : null
                                })
            .Take(boundedLimit)
            .ToListAsync(ct);

        return personRows.Select(x => new ResolutionEvidenceSummary
        {
            EvidenceItemId = x.Id,
            Summary = string.IsNullOrWhiteSpace(x.SummaryText) ? x.Id.ToString("D") : x.SummaryText.Trim(),
            TrustFactor = Clamp01(x.Confidence),
            ObservedAtUtc = x.ObservedAt?.ToUniversalTime(),
            SourceRef = x.SourceRef,
            SourceLabel = x.SourceLabel
        }).ToList();
    }

    private static int CountJsonArrayItems(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return 0;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Array
                ? document.RootElement.GetArrayLength()
                : 0;
        }
        catch (JsonException)
        {
            return 0;
        }
    }

    private static int TryReadInt(string? json, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return 0;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty(propertyName, out var property)
                   && property.TryGetInt32(out var value)
                ? value
                : 0;
        }
        catch (JsonException)
        {
            return 0;
        }
    }

    private static bool TryReadString(JsonElement element, string propertyName, out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var raw = property.GetString();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        value = raw.Trim();
        return true;
    }

    private static float Clamp01(float value)
        => Math.Clamp(value, 0f, 1f);

    private sealed class TrackedPersonScope
    {
        public Guid PersonId { get; init; }
        public string ScopeKey { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public int EvidenceCount { get; init; }
    }

    private sealed class DurableContext
    {
        public Guid MetadataId { get; init; }
        public string ObjectFamily { get; init; } = string.Empty;
        public string ObjectKey { get; init; } = string.Empty;
        public string PromotionState { get; init; } = string.Empty;
        public string TruthLayer { get; init; } = string.Empty;
        public float Confidence { get; init; }
        public float Freshness { get; init; }
        public float Stability { get; init; }
        public float TrustFactor { get; init; }
        public int ContradictionCount { get; init; }
        public string ContradictionMarkersJson { get; init; } = "[]";
        public int EvidenceCount { get; init; }
        public DateTime UpdatedAtUtc { get; init; }
        public string MetadataSummary { get; init; } = string.Empty;
    }

    private sealed class ProjectedResolutionItem
    {
        public ResolutionItemSummary Summary { get; init; } = new();
        public string SourceKind { get; init; } = string.Empty;
        public string SourceRef { get; init; } = string.Empty;
        public string? RequiredAction { get; init; }
        public List<ResolutionDetailNote> Notes { get; init; } = [];
        public List<Guid> DurableMetadataIds { get; init; } = [];
        public bool UseTrackedPersonEvidence { get; init; }
    }
}
