using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Infrastructure.Database;

public sealed class ResolutionReadProjectionService : IResolutionReadService
{
    private const string ActiveStatus = "active";
    private static readonly Regex GuidRegex = new(
        @"\b[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex InternalKeyValueRegex = new(
        @"\b(?:tracked_person_id|scope_item_key|scope_key|operator_session_id|source_ref|target_ref|object_ref)\s*[:=]\s*\S+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ScopeTokenRegex = new(
        @"\bscope:[^\s,;]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex WhitespaceRegex = new(
        @"\s{2,}",
        RegexOptions.Compiled);

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

        var filtered = ApplyQueueFilters(projected, request);
        var ordered = OrderItems(filtered, request.SortBy, request.SortDirection);
        var boundedLimit = Math.Clamp(request.Limit, 1, 200);
        var summaries = projected.Select(x => x.Summary).ToList();

        return new ResolutionQueueResult
        {
            ScopeBound = true,
            TrackedPersonId = trackedPerson.PersonId,
            ScopeKey = trackedPerson.ScopeKey,
            TrackedPersonDisplayName = trackedPerson.DisplayName,
            RuntimeState = runtimeState,
            TotalOpenCount = projected.Count,
            FilteredCount = ordered.Count,
            ItemTypeCounts = BuildFacetCounts(
                summaries,
                x => x.ItemType,
                ResolutionItemTypes.All),
            StatusCounts = BuildFacetCounts(
                summaries,
                x => x.Status,
                ResolutionItemStatuses.All),
            PriorityCounts = BuildFacetCounts(
                summaries,
                x => x.Priority,
                ResolutionItemPriorities.All),
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
            request.EvidenceSortBy,
            request.EvidenceSortDirection,
            request.EvidenceLimit,
            ct);
        ApplyEvidenceRelevanceHints(match, evidence);
        var evidenceRationaleSummary = BuildEvidenceRationaleSummary(match, evidence);
        var autoResolutionGap = BuildAutoResolutionGap(match);
        var operatorDecisionFocus = BuildOperatorDecisionFocus(match);

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
                HumanShortTitle = match.Summary.HumanShortTitle,
                WhatHappened = match.Summary.WhatHappened,
                WhyOperatorAnswerNeeded = match.Summary.WhyOperatorAnswerNeeded,
                WhatToDoPrompt = match.Summary.WhatToDoPrompt,
                EvidenceHint = match.Summary.EvidenceHint,
                SecondaryText = match.Summary.SecondaryText,
                AffectedFamily = match.Summary.AffectedFamily,
                AffectedObjectRef = match.Summary.AffectedObjectRef,
                TrustFactor = match.Summary.TrustFactor,
                Status = match.Summary.Status,
                EvidenceCount = match.Summary.EvidenceCount,
                UpdatedAtUtc = match.Summary.UpdatedAtUtc,
                Priority = match.Summary.Priority,
                RecommendedNextAction = match.Summary.RecommendedNextAction,
                AvailableActions = [.. match.Summary.AvailableActions],
                SourceKind = match.SourceKind,
                SourceRef = match.SourceRef,
                RequiredAction = match.RequiredAction,
                EvidenceRationaleSummary = evidenceRationaleSummary,
                AutoResolutionGap = autoResolutionGap,
                OperatorDecisionFocus = operatorDecisionFocus,
                RationaleIsHeuristic = true,
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
            var evidenceCount = ResolveEvidenceCount(trackedPerson, targetContexts);
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
                    HumanShortTitle = $"Нужно уточнение: {DescribeFamilyRu(branch.BranchFamily)}",
                    WhatHappened = SanitizeVisibleCardTextOrDefault(
                        string.IsNullOrWhiteSpace(branch.BlockReason)
                            ? "Ветка обработки остановилась: системе не хватает однозначного ответа."
                            : branch.BlockReason,
                        "Ветка обработки остановилась и требует уточнения от оператора."),
                    WhyOperatorAnswerNeeded = $"Без ответа оператора обработка по теме «{DescribeFamilyRu(branch.BranchFamily)}» не продолжится.",
                    WhatToDoPrompt = BuildClarificationPrompt(branch.RequiredAction),
                    EvidenceHint = BuildEvidenceHint(
                        evidenceCount,
                        GetFirstVisibleNote(notes, "unknown", "normalization_issue")),
                    AffectedFamily = branch.BranchFamily,
                    AffectedObjectRef = branch.TargetRef,
                    TrustFactor = targetContexts.Count == 0 ? 0.5f : AverageTrust(targetContexts),
                    Status = ResolutionItemStatuses.Open,
                    EvidenceCount = evidenceCount,
                    UpdatedAtUtc = branch.LastBlockedAtUtc,
                    SecondaryText = BuildSecondaryText(
                        targetContexts.Count == 0 ? 0.5f : AverageTrust(targetContexts),
                        ResolutionItemStatuses.Open,
                        branch.LastBlockedAtUtc),
                    Priority = string.Equals(branch.BranchFamily, Stage8RecomputeTargetFamilies.Stage6Bootstrap, StringComparison.Ordinal)
                        ? ResolutionItemPriorities.Critical
                        : ResolutionItemPriorities.High,
                    RecommendedNextAction = "clarify",
                    AvailableActions = BuildAvailableActions()
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
            var trustFactor = targetContexts.Count == 0 ? 0.45f : AverageTrust(targetContexts);
            var status = queueItem.Status == Stage8RecomputeQueueStatuses.Leased
                ? ResolutionItemStatuses.Running
                : ResolutionItemStatuses.Blocked;
            var evidenceCount = ResolveEvidenceCount(trackedPerson, targetContexts);
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
                    HumanShortTitle = $"Блокер в очереди: {DescribeFamilyRu(queueItem.TargetFamily)}",
                    WhatHappened = SanitizeVisibleCardTextOrDefault(
                        string.IsNullOrWhiteSpace(branch.BlockReason)
                            ? "Очередь пересчета уперлась в незакрытое уточнение."
                            : branch.BlockReason,
                        "Очередь пересчета остановилась и ожидает решения оператора."),
                    WhyOperatorAnswerNeeded = "Пока оператор не снимет блокировку, эта ветка не сможет завершить пересчет.",
                    WhatToDoPrompt = BuildClarificationPrompt(branch.RequiredAction),
                    EvidenceHint = BuildEvidenceHint(
                        evidenceCount,
                        "Очередь ждет закрытия уточнения."),
                    AffectedFamily = queueItem.TargetFamily,
                    AffectedObjectRef = queueItem.TargetRef,
                    TrustFactor = trustFactor,
                    Status = status,
                    EvidenceCount = evidenceCount,
                    UpdatedAtUtc = Max(queueItem.UpdatedAtUtc, branch.LastBlockedAtUtc),
                    SecondaryText = BuildSecondaryText(
                        trustFactor,
                        status,
                        Max(queueItem.UpdatedAtUtc, branch.LastBlockedAtUtc)),
                    Priority = string.Equals(queueItem.TargetFamily, Stage8RecomputeTargetFamilies.Stage6Bootstrap, StringComparison.Ordinal)
                        ? ResolutionItemPriorities.Critical
                        : ResolutionItemPriorities.High,
                    RecommendedNextAction = "clarify",
                    AvailableActions = BuildAvailableActions()
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
            var trustFactor = targetContexts.Count == 0 ? 0.35f : Math.Min(AverageTrust(targetContexts), 0.55f);
            var evidenceCount = ResolveEvidenceCount(trackedPerson, targetContexts);
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
                    HumanShortTitle = $"Не хватает данных: {DescribeFamilyRu(queueItem.TargetFamily)}",
                    WhatHappened = "Последний пересчет не смог собрать устойчивый результат из доступных фактов.",
                    WhyOperatorAnswerNeeded = "Без решения оператора ветка останется неполной и не сможет двигаться дальше.",
                    WhatToDoPrompt = "Нажмите «Факты», проверьте опору и решите: добавить данные, дать уточнение или отложить решение.",
                    EvidenceHint = BuildEvidenceHint(evidenceCount),
                    AffectedFamily = queueItem.TargetFamily,
                    AffectedObjectRef = queueItem.TargetRef,
                    TrustFactor = trustFactor,
                    Status = ResolutionItemStatuses.Open,
                    EvidenceCount = evidenceCount,
                    UpdatedAtUtc = queueItem.UpdatedAtUtc,
                    SecondaryText = BuildSecondaryText(
                        trustFactor,
                        ResolutionItemStatuses.Open,
                        queueItem.UpdatedAtUtc),
                    Priority = queueItem.AttemptCount >= 3
                        ? ResolutionItemPriorities.High
                        : ResolutionItemPriorities.Medium,
                    RecommendedNextAction = "evidence",
                    AvailableActions = BuildAvailableActions()
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
            var evidenceCount = ResolveEvidenceCount(trackedPerson, targetContexts);
            var priority = MapSeverityToPriority(defect.Severity);
            items.Add(new ProjectedResolutionItem
            {
                Summary = new ResolutionItemSummary
                {
                    ScopeItemKey = BuildItemKey(ResolutionItemTypes.MissingData, "runtime_defect", defect.Id.ToString("D")),
                    ItemType = ResolutionItemTypes.MissingData,
                    Title = $"{DescribeFamily(defect.ObjectType)} data gap",
                    Summary = defect.Summary,
                    WhyItMatters = "The active bounded scope is missing enough substrate or normalized input to keep this branch moving.",
                    HumanShortTitle = $"Пробел в данных: {DescribeFamilyRu(defect.ObjectType)}",
                    WhatHappened = SanitizeVisibleCardTextOrDefault(
                        defect.Summary,
                        "Для этой ветки не хватает данных, чтобы продолжить обработку."),
                    WhyOperatorAnswerNeeded = "Без решения оператора текущий контекст останется неполным и часть обработки не продвинется дальше.",
                    WhatToDoPrompt = "Нажмите «Факты» и проверьте, каких сигналов не хватает; затем решите, нужно ли уточнение или пауза.",
                    EvidenceHint = BuildEvidenceHint(evidenceCount),
                    AffectedFamily = defect.ObjectType ?? "scope",
                    AffectedObjectRef = defect.ObjectRef ?? $"scope:{trackedPerson.ScopeKey}",
                    TrustFactor = 0.8f,
                    Status = ResolutionItemStatuses.AttentionRequired,
                    EvidenceCount = evidenceCount,
                    UpdatedAtUtc = defect.UpdatedAtUtc,
                    SecondaryText = BuildSecondaryText(
                        0.8f,
                        ResolutionItemStatuses.AttentionRequired,
                        defect.UpdatedAtUtc),
                    Priority = priority,
                    RecommendedNextAction = "evidence",
                    AvailableActions = BuildAvailableActions()
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
                    HumanShortTitle = $"Противоречие: {DescribeFamilyRu(context.ObjectFamily)}",
                    WhatHappened = "По теме остались несовместимые сигналы и объект не выглядит согласованным.",
                    WhyOperatorAnswerNeeded = string.Equals(context.PromotionState, Stage8PromotionStates.PromotionBlocked, StringComparison.Ordinal)
                        ? "Пока конфликт не снят, объект нельзя продвинуть дальше."
                        : "Пока конфликт не снят, следующие решения будут менее надежными.",
                    WhatToDoPrompt = "Нажмите «Факты», сопоставьте конфликтующие сигналы и решите: подтвердить, отклонить или отложить.",
                    EvidenceHint = BuildContradictionEvidenceHint(context.ContradictionCount, context.EvidenceCount),
                    AffectedFamily = context.ObjectFamily,
                    AffectedObjectRef = context.ObjectKey,
                    TrustFactor = context.TrustFactor,
                    Status = string.Equals(context.PromotionState, Stage8PromotionStates.PromotionBlocked, StringComparison.Ordinal)
                        ? ResolutionItemStatuses.Blocked
                        : ResolutionItemStatuses.Open,
                    EvidenceCount = context.EvidenceCount,
                    UpdatedAtUtc = context.UpdatedAtUtc,
                    SecondaryText = BuildSecondaryText(
                        context.TrustFactor,
                        string.Equals(context.PromotionState, Stage8PromotionStates.PromotionBlocked, StringComparison.Ordinal)
                            ? ResolutionItemStatuses.Blocked
                            : ResolutionItemStatuses.Open,
                        context.UpdatedAtUtc),
                    Priority = context.ContradictionCount > 1 || string.Equals(context.PromotionState, Stage8PromotionStates.PromotionBlocked, StringComparison.Ordinal)
                        ? ResolutionItemPriorities.High
                        : ResolutionItemPriorities.Medium,
                    RecommendedNextAction = "evidence",
                    AvailableActions = BuildAvailableActions()
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
                    HumanShortTitle = $"Нужна проверка: {DescribeFamilyRu(context.ObjectFamily)}",
                    WhatHappened = "Объект не прошел дальше в канонический слой и остался на ручной проверке.",
                    WhyOperatorAnswerNeeded = context.ContradictionCount > 0
                        ? "Есть конфликтующие сигналы, поэтому автоматического продвижения нет."
                        : "Автоматика оставила объект на ручной проверке, прежде чем на него смогут опираться другие поверхности.",
                    WhatToDoPrompt = "Нажмите «В веб» и решите, можно ли продвигать объект дальше или его нужно оставить на проверке.",
                    EvidenceHint = BuildEvidenceHint(context.EvidenceCount),
                    AffectedFamily = context.ObjectFamily,
                    AffectedObjectRef = context.ObjectKey,
                    TrustFactor = context.TrustFactor,
                    Status = ResolutionItemStatuses.AttentionRequired,
                    EvidenceCount = context.EvidenceCount,
                    UpdatedAtUtc = context.UpdatedAtUtc,
                    SecondaryText = BuildSecondaryText(
                        context.TrustFactor,
                        ResolutionItemStatuses.AttentionRequired,
                        context.UpdatedAtUtc),
                    Priority = context.ContradictionCount > 0
                        ? ResolutionItemPriorities.High
                        : ResolutionItemPriorities.Medium,
                    RecommendedNextAction = "open-web",
                    AvailableActions = BuildAvailableActions()
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
            var runtimeStatus = runtimeState.State == RuntimeControlStates.Degraded
                ? ResolutionItemStatuses.Degraded
                : ResolutionItemStatuses.AttentionRequired;
            var priority = MapRuntimeStateToPriority(runtimeState.State);
            items.Add(new ProjectedResolutionItem
            {
                Summary = new ResolutionItemSummary
                {
                    ScopeItemKey = BuildItemKey(ResolutionItemTypes.Review, "runtime_control_state", runtimeState.State),
                    ItemType = ResolutionItemTypes.Review,
                    Title = $"Runtime operating in {runtimeState.State}",
                    Summary = runtimeState.Reason,
                    WhyItMatters = "Runtime control state is directly affecting which Stage8 work can run or promote inside this tracked-person scope.",
                    HumanShortTitle = $"Рантайм: {DescribeRuntimeStateRu(runtimeState.State)}",
                    WhatHappened = SanitizeVisibleCardTextOrDefault(
                        string.IsNullOrWhiteSpace(runtimeState.Reason)
                            ? "Контур исполнения работает с ограничениями."
                            : runtimeState.Reason,
                        "Контур исполнения работает с ограничениями."),
                    WhyOperatorAnswerNeeded = "Это влияет на то, какие ветки могут выполняться и продвигаться в текущем контексте.",
                    WhatToDoPrompt = "Нажмите «В веб» и проверьте, нужно ли снять ограничение или оставить его до стабилизации.",
                    EvidenceHint = runtimeDefects.Count > 0
                        ? $"Есть {FormatCountRu(runtimeDefects.Count, "открытый сбой", "открытых сбоя", "открытых сбоев")} рантайма в этом контексте."
                        : "Связанных открытых сбоев рантайма в этом контексте пока не видно.",
                    AffectedFamily = "runtime_control",
                    AffectedObjectRef = $"scope:{trackedPerson.ScopeKey}",
                    TrustFactor = 1f,
                    Status = runtimeStatus,
                    EvidenceCount = runtimeDefects.Count,
                    UpdatedAtUtc = runtimeState.ActivatedAtUtc,
                    SecondaryText = BuildSecondaryText(
                        1f,
                        runtimeStatus,
                        runtimeState.ActivatedAtUtc),
                    Priority = priority,
                    RecommendedNextAction = "open-web",
                    AvailableActions = BuildAvailableActions()
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
            var status = defect.EscalationAction == RuntimeDefectEscalationActions.Observe
                ? ResolutionItemStatuses.Open
                : ResolutionItemStatuses.AttentionRequired;
            var evidenceCount = ResolveEvidenceCount(trackedPerson, targetContexts);
            var priority = MapSeverityToPriority(defect.Severity);
            items.Add(new ProjectedResolutionItem
            {
                Summary = new ResolutionItemSummary
                {
                    ScopeItemKey = BuildItemKey(ResolutionItemTypes.Review, "runtime_defect", defect.Id.ToString("D")),
                    ItemType = ResolutionItemTypes.Review,
                    Title = $"{NormalizeDefectTitle(defect)}",
                    Summary = defect.Summary,
                    WhyItMatters = "Operator review is required because runtime defects are affecting data integrity, promotion safety, or bounded execution reliability.",
                    HumanShortTitle = $"Разбор рантайма: {DescribeRuntimeTargetRu(defect)}",
                    WhatHappened = SanitizeVisibleCardTextOrDefault(
                        defect.Summary,
                        "Обнаружен сбой в рантайме, который требует операторского решения."),
                    WhyOperatorAnswerNeeded = "Сбой влияет на целостность данных, безопасность продвижения или стабильность текущего контура обработки.",
                    WhatToDoPrompt = "Нажмите «В веб» для детального разбора и решите, нужен ли фикс, ручное подтверждение или наблюдение.",
                    EvidenceHint = BuildEvidenceHint(evidenceCount),
                    AffectedFamily = defect.ObjectType ?? defect.DefectClass,
                    AffectedObjectRef = defect.ObjectRef ?? $"scope:{trackedPerson.ScopeKey}",
                    TrustFactor = 1f,
                    Status = status,
                    EvidenceCount = evidenceCount,
                    UpdatedAtUtc = defect.UpdatedAtUtc,
                    SecondaryText = BuildSecondaryText(
                        1f,
                        status,
                        defect.UpdatedAtUtc),
                    Priority = priority,
                    RecommendedNextAction = "open-web",
                    AvailableActions = BuildAvailableActions()
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

    private static List<ProjectedResolutionItem> ApplyQueueFilters(
        List<ProjectedResolutionItem> items,
        ResolutionQueueRequest request)
    {
        var itemTypes = NormalizeFilters(request.ItemTypes);
        var statuses = NormalizeFilters(request.Statuses);
        var priorities = NormalizeFilters(request.Priorities);
        var recommendedActions = NormalizeFilters(request.RecommendedActions);

        return items
            .Where(x => itemTypes.Count == 0 || itemTypes.Contains(x.Summary.ItemType))
            .Where(x => statuses.Count == 0 || statuses.Contains(x.Summary.Status))
            .Where(x => priorities.Count == 0 || priorities.Contains(x.Summary.Priority))
            .Where(x => recommendedActions.Count == 0 || recommendedActions.Contains(x.Summary.RecommendedNextAction ?? string.Empty))
            .ToList();
    }

    private static List<ProjectedResolutionItem> OrderItems(
        List<ProjectedResolutionItem> items,
        string? sortBy,
        string? sortDirection)
    {
        var normalizedSortBy = ResolutionQueueSortFields.Normalize(sortBy);
        var normalizedDirection = ResolutionSortDirections.Normalize(sortDirection);
        var descending = string.Equals(normalizedDirection, ResolutionSortDirections.Desc, StringComparison.Ordinal);

        return normalizedSortBy switch
        {
            ResolutionQueueSortFields.UpdatedAt => descending
                ? items.OrderByDescending(x => x.Summary.UpdatedAtUtc)
                    .ThenByDescending(x => PriorityRank(x.Summary.Priority))
                    .ToList()
                : items.OrderBy(x => x.Summary.UpdatedAtUtc)
                    .ThenBy(x => PriorityRank(x.Summary.Priority))
                    .ToList(),
            _ => descending
                ? items.OrderByDescending(x => PriorityRank(x.Summary.Priority))
                    .ThenByDescending(x => x.Summary.UpdatedAtUtc)
                    .ToList()
                : items.OrderBy(x => PriorityRank(x.Summary.Priority))
                    .ThenBy(x => x.Summary.UpdatedAtUtc)
                    .ToList()
        };
    }

    private static List<ResolutionFacetCount> BuildFacetCounts(
        IReadOnlyList<ResolutionItemSummary> items,
        Func<ResolutionItemSummary, string> selector,
        IReadOnlyList<string> orderedKeys)
    {
        var counts = items
            .GroupBy(selector, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.Count(), StringComparer.Ordinal);

        var result = new List<ResolutionFacetCount>();
        foreach (var key in orderedKeys)
        {
            if (counts.TryGetValue(key, out var count))
            {
                result.Add(new ResolutionFacetCount
                {
                    Key = key,
                    Count = count
                });
            }
        }

        foreach (var extra in counts.Keys.Except(orderedKeys, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal))
        {
            result.Add(new ResolutionFacetCount
            {
                Key = extra,
                Count = counts[extra]
            });
        }

        return result;
    }

    private static HashSet<string> NormalizeFilters(IEnumerable<string>? values)
    {
        return values == null
            ? []
            : values
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .ToHashSet(StringComparer.Ordinal);
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

    private static List<string> BuildAvailableActions()
        => [.. ResolutionActionTypes.All];

    private static string BuildItemKey(string itemType, string sourceKind, string sourceId)
        => $"{itemType}:{sourceKind}:{sourceId}";

    private static DateTime Max(DateTime left, DateTime right)
        => left >= right ? left : right;

    private static string BuildClarificationPrompt(string? requiredAction)
    {
        if (!string.IsNullOrWhiteSpace(requiredAction))
        {
            var actionText = EnsureSentence(requiredAction);
            if (!string.IsNullOrWhiteSpace(actionText))
            {
                return $"Нажмите «Уточнить» и зафиксируйте решение: {actionText}";
            }
        }

        return "Нажмите «Уточнить» и дайте короткий ответ, который снимает неоднозначность.";
    }

    private static string BuildEvidenceHint(int evidenceCount, string? customHint = null)
    {
        var countHint = evidenceCount > 0
            ? $"Связано {FormatCountRu(evidenceCount, "факт", "факта", "фактов")}."
            : "Связанных фактов пока мало или они не выделены.";

        if (string.IsNullOrWhiteSpace(customHint))
        {
            return countHint;
        }

        return $"{EnsureSentence(customHint)} {countHint}";
    }

    private static string BuildContradictionEvidenceHint(int contradictionCount, int evidenceCount)
        => $"Есть {FormatCountRu(contradictionCount, "маркер противоречия", "маркера противоречия", "маркеров противоречия")} и {FormatCountRu(evidenceCount, "связанный факт", "связанных факта", "связанных фактов")}.";

    private static void ApplyEvidenceRelevanceHints(
        ProjectedResolutionItem item,
        List<ResolutionEvidenceSummary> evidence)
    {
        for (var index = 0; index < evidence.Count; index++)
        {
            var entry = evidence[index];
            entry.RelevanceHint = BuildEvidenceRelevanceHint(item, entry);
            entry.RelevanceHintIsHeuristic = true;
            entry.DecisionLinkage = BuildEvidenceDecisionLinkage(item, entry);
        }
    }

    private static ResolutionEvidenceDecisionLinkage BuildEvidenceDecisionLinkage(
        ProjectedResolutionItem item,
        ResolutionEvidenceSummary evidence)
    {
        if (item.Summary.ItemType == ResolutionItemTypes.Review)
        {
            return BuildReviewEvidenceDecisionLinkage(item, evidence);
        }

        var criterion = ResolveDecisionCriterion(item);
        var reviewQuestion = BuildOperatorDecisionFocus(item);
        var stance = ResolveDecisionStance(item, evidence);
        var heuristicCalibration = ResolveLinkageHeuristicCalibration(item, evidence, stance);
        var stanceText = stance switch
        {
            ResolutionDecisionStances.Supports => "вероятно поддерживает",
            ResolutionDecisionStances.Challenges => "скорее оспаривает",
            _ => "оставляет неопределенность по"
        };
        var summary = $"{stanceText} критерию: {criterion}.";
        if (!string.IsNullOrWhiteSpace(reviewQuestion))
        {
            summary += $" Связан с вопросом: {reviewQuestion}";
        }

        return new ResolutionEvidenceDecisionLinkage
        {
            LinkType = ResolutionDecisionLinkTypes.Criterion,
            LinkTarget = criterion,
            ReviewQuestion = reviewQuestion,
            Stance = stance,
            Summary = summary,
            IsHeuristic = true,
            HeuristicCalibration = heuristicCalibration
        };
    }

    private static ResolutionEvidenceDecisionLinkage BuildReviewEvidenceDecisionLinkage(
        ProjectedResolutionItem item,
        ResolutionEvidenceSummary evidence)
    {
        var reviewQuestion = BuildOperatorDecisionFocus(item);
        var decisionUnit = ResolveReviewDecisionUnit(item);
        var hasDirectDurableLink = item.DurableMetadataIds.Count > 0;
        var hasEvidenceProvenance = !string.IsNullOrWhiteSpace(evidence.SourceRef)
            || !string.IsNullOrWhiteSpace(evidence.SourceLabel);
        var reviewSignal = ResolveReviewEvidenceSignalHeuristic(evidence);

        string stance;
        string summary;
        string calibration;

        if (string.Equals(item.SourceKind, "runtime_defect", StringComparison.Ordinal))
        {
            if (hasDirectDurableLink && hasEvidenceProvenance)
            {
                stance = reviewSignal.Stance;
                calibration = reviewSignal.Calibration;
                summary = reviewSignal.Role switch
                {
                    "contextual" => $"Сообщение напрямую связано с evidence объекта «{DescribeFamilyRu(item.Summary.AffectedFamily)}», но остается в основном контекстным сигналом: {reviewSignal.Reason}; само по себе оно не подтверждает и не опровергает рантайм-сбой.",
                    "weak_support" => $"Сообщение напрямую связано с evidence объекта «{DescribeFamilyRu(item.Summary.AffectedFamily)}» и дает лишь слабую поддержку ручному разбору review item: {reviewSignal.Reason}; само по себе оно не доказывает рантайм-сбой.",
                    _ => $"Сообщение напрямую входит в evidence затронутого объекта «{DescribeFamilyRu(item.Summary.AffectedFamily)}» и скорее поддерживает ручной разбор этого review item: {reviewSignal.Reason}; само по себе оно не доказывает рантайм-сбой."
                };
            }
            else
            {
                stance = ResolutionDecisionStances.Ambiguous;
                calibration = ResolutionDecisionHeuristicCalibrations.Low;
                summary = "Сообщение помогает читать контекст вокруг runtime review item, но без прямой привязки к затронутому объекту не подтверждает и не опровергает сбой.";
            }
        }
        else if (string.Equals(item.SourceKind, "durable_object_metadata", StringComparison.Ordinal))
        {
            if (hasDirectDurableLink && hasEvidenceProvenance)
            {
                stance = reviewSignal.Stance;
                calibration = reviewSignal.Calibration;
                summary = reviewSignal.Role switch
                {
                    "contextual" => $"Сообщение входит в доказательную базу объекта «{DescribeFamilyRu(item.Summary.AffectedFamily)}», но по форме остается скорее контекстным сигналом: {reviewSignal.Reason}; этого недостаточно для снятия ручной проверки.",
                    "weak_support" => $"Сообщение входит в доказательную базу объекта «{DescribeFamilyRu(item.Summary.AffectedFamily)}» и дает лишь слабую поддержку решению оставить его на ручной проверке: {reviewSignal.Reason}.",
                    _ => $"Сообщение входит в доказательную базу объекта «{DescribeFamilyRu(item.Summary.AffectedFamily)}» и скорее поддерживает решение держать его на ручной проверке: {reviewSignal.Reason}."
                };
            }
            else
            {
                stance = ResolutionDecisionStances.Ambiguous;
                calibration = ResolutionDecisionHeuristicCalibrations.Low;
                summary = "Сообщение остается фоновым сигналом для review item и не дает достаточной опоры, чтобы снять ручную проверку автоматически.";
            }
        }
        else if (string.Equals(item.SourceKind, "runtime_control_state", StringComparison.Ordinal))
        {
            stance = ResolutionDecisionStances.Ambiguous;
            calibration = ResolutionDecisionHeuristicCalibrations.Low;
            summary = "Сообщение остается bounded-контекстом этого scope и не подтверждает причину режима рантайма само по себе.";
        }
        else if (hasDirectDurableLink && hasEvidenceProvenance)
        {
            stance = reviewSignal.Stance;
            calibration = reviewSignal.Calibration;
            summary = reviewSignal.Role switch
            {
                "contextual" => $"Сообщение связано с объектом на ручной проверке, но остается скорее контекстным сигналом: {reviewSignal.Reason}.",
                "weak_support" => $"Сообщение связано с объектом на ручной проверке и дает лишь слабую поддержку решению разобрать его вручную: {reviewSignal.Reason}.",
                _ => $"Сообщение связано с объектом на ручной проверке и скорее поддерживает решение разобрать его вручную перед автоматическим продолжением: {reviewSignal.Reason}."
            };
        }
        else
        {
            stance = ResolutionDecisionStances.Ambiguous;
            calibration = ResolutionDecisionHeuristicCalibrations.Low;
            summary = "Сообщение остается контекстным сигналом review item и не дает достаточной опоры для более сильного вывода.";
        }

        if (!string.IsNullOrWhiteSpace(reviewQuestion))
        {
            summary += $" Вопрос оператора: {reviewQuestion}";
        }

        return new ResolutionEvidenceDecisionLinkage
        {
            LinkType = ResolutionDecisionLinkTypes.DecisionUnit,
            LinkTarget = decisionUnit,
            ReviewQuestion = reviewQuestion,
            Stance = stance,
            Summary = summary,
            IsHeuristic = true,
            HeuristicCalibration = calibration
        };
    }

    private static string BuildEvidenceRationaleSummary(
        ProjectedResolutionItem item,
        IReadOnlyList<ResolutionEvidenceSummary> evidence)
    {
        var familyRu = DescribeFamilyRu(item.Summary.AffectedFamily);
        var signalSummary = item.Summary.ItemType switch
        {
            ResolutionItemTypes.Contradiction => "система вероятно видит конфликтующие сигналы",
            ResolutionItemTypes.Clarification or ResolutionItemTypes.BlockedBranch => "система вероятно видит неоднозначность и зависимость от уточнения",
            ResolutionItemTypes.MissingData => "система вероятно видит недостаточную или неполную опору",
            _ => "система вероятно видит риск, который нельзя безопасно закрыть автоматически"
        };
        var countSummary = BuildEvidenceCountSummary(item, evidence);
        return $"По теме «{familyRu}» {signalSummary}. {countSummary} Это эвристическое объяснение выбора evidence.";
    }

    private static string BuildEvidenceCountSummary(
        ProjectedResolutionItem item,
        IReadOnlyList<ResolutionEvidenceSummary> evidence)
    {
        if (evidence.Count == 0)
        {
            return "Опорные сообщения в текущей выдаче не выделились.";
        }

        if (item.Summary.ItemType != ResolutionItemTypes.Review)
        {
            return $"Показано {FormatCountRu(evidence.Count, "сообщение", "сообщения", "сообщений")} как опорные сигналы.";
        }

        var supportsCount = evidence.Count(x => x.DecisionLinkage?.Stance == ResolutionDecisionStances.Supports);
        var ambiguousCount = evidence.Count(x => x.DecisionLinkage?.Stance == ResolutionDecisionStances.Ambiguous);

        if (supportsCount > 0 && ambiguousCount > 0)
        {
            return $"Показано {FormatCountRu(evidence.Count, "сообщение", "сообщения", "сообщений")}: {supportsCount} скорее поддерживают ручное решение, а {ambiguousCount} остаются контекстными или неоднозначными.";
        }

        if (ambiguousCount == evidence.Count)
        {
            return $"Показано {FormatCountRu(evidence.Count, "сообщение", "сообщения", "сообщений")}, но они остаются в основном контекстными или неоднозначными.";
        }

        return $"Показано {FormatCountRu(evidence.Count, "сообщение", "сообщения", "сообщений")} как эвристические опорные сигналы для ручного решения.";
    }

    private static string BuildAutoResolutionGap(ProjectedResolutionItem item)
    {
        return item.Summary.ItemType switch
        {
            ResolutionItemTypes.Clarification or ResolutionItemTypes.BlockedBranch
                => "Авторазрешение остановлено: не хватает однозначного ответа для продолжения ветки.",
            ResolutionItemTypes.Contradiction
                => "Авторазрешение остановлено: конфликтующие сигналы не удалось согласовать безопасно.",
            ResolutionItemTypes.MissingData
                => "Авторазрешение остановлено: опоры недостаточно для устойчивого вывода.",
            _ => "Авторазрешение остановлено: риск ошибки выше допустимого без ручной проверки."
        };
    }

    private static string BuildOperatorDecisionFocus(ProjectedResolutionItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.Summary.WhatToDoPrompt))
        {
            return EnsureSentence(item.Summary.WhatToDoPrompt);
        }

        if (string.Equals(item.RequiredAction, "clarify", StringComparison.OrdinalIgnoreCase))
        {
            return "Дайте короткое уточнение, чтобы снять блокировку ветки.";
        }

        return item.Summary.RecommendedNextAction switch
        {
            "clarify" => "Дайте bounded-уточнение и зафиксируйте, какой вариант считать корректным.",
            "evidence" => "Проверьте, достаточно ли опоры для решения: подтвердить, отклонить или отложить.",
            "open-web" => "Проверьте влияние на текущий контур и выберите безопасное ручное решение.",
            _ => "Примите bounded-решение по текущему review item и зафиксируйте объяснение."
        };
    }

    private static string ResolveReviewDecisionUnit(ProjectedResolutionItem item)
    {
        if (string.Equals(item.SourceKind, "runtime_defect", StringComparison.Ordinal))
        {
            return $"нужна ли ручная проверка «{DescribeFamilyRu(item.Summary.AffectedFamily)}» перед снятием runtime review";
        }

        if (string.Equals(item.SourceKind, "runtime_control_state", StringComparison.Ordinal))
        {
            return $"оставлять ли контур в режиме «{DescribeRuntimeStateRu(item.SourceRef)}» для этого bounded scope";
        }

        if (string.Equals(item.SourceKind, "durable_object_metadata", StringComparison.Ordinal))
        {
            return $"можно ли продвигать «{DescribeFamilyRu(item.Summary.AffectedFamily)}» без дополнительной ручной блокировки";
        }

        return $"нужно ли ручное решение по item «{SanitizeVisibleCardText(item.Summary.HumanShortTitle ?? item.Summary.Title)}»";
    }

    private static string BuildEvidenceRelevanceHint(ProjectedResolutionItem item, ResolutionEvidenceSummary evidence)
    {
        if (string.IsNullOrWhiteSpace(evidence.SenderDisplay))
        {
            return "Индикатор нехватки контекста: не удалось надежно привязать отправителя.";
        }

        if (item.Summary.ItemType == ResolutionItemTypes.Contradiction)
        {
            return "Может поддерживать конфликт: версия из этого сообщения расходится с другими сигналами.";
        }

        if (HasNoteKind(item, "normalization_issue"))
        {
            return "Фраза чувствительна к нормализации: трактовка может меняться после уточнения формулировки.";
        }

        if (item.Summary.ItemType == ResolutionItemTypes.Clarification
            || item.Summary.ItemType == ResolutionItemTypes.BlockedBranch)
        {
            return "Индикатор неоднозначности: без дополнительного контекста смысл остается неустойчивым.";
        }

        if (item.Summary.ItemType == ResolutionItemTypes.MissingData)
        {
            return evidence.TrustFactor <= 0.45f
                ? "Слабый поддерживающий сигнал: одного сообщения недостаточно для автоматического вывода."
                : "Индикатор неполного контекста: сигнал есть, но опоры для авто-решения недостаточно.";
        }

        if (ContainsEmotionalCue(evidence.Summary))
        {
            return "Эмоциональный сигнал без стабильного контекста: нужен ручной разбор перед решением.";
        }

        if (evidence.TrustFactor <= 0.45f)
        {
            return "Слабый поддерживающий сигнал: уверенность ограничена, поэтому решение оставлено оператору.";
        }

        return "Опорный сигнал для текущего review item; используется как часть эвристической проверки.";
    }

    private static string ResolveDecisionCriterion(ProjectedResolutionItem item)
    {
        return item.Summary.ItemType switch
        {
            ResolutionItemTypes.Contradiction => "сигнал противоречий требует ручного согласования",
            ResolutionItemTypes.Clarification or ResolutionItemTypes.BlockedBranch => "без уточнения ветка не может быть безопасно продолжена",
            ResolutionItemTypes.MissingData => "опоры недостаточно для устойчивого авто-решения",
            _ => "риск ошибки остается выше порога для auto-resolution"
        };
    }

    private static string ResolveDecisionStance(ProjectedResolutionItem item, ResolutionEvidenceSummary evidence)
    {
        if (string.IsNullOrWhiteSpace(evidence.SenderDisplay))
        {
            return ResolutionDecisionStances.Ambiguous;
        }

        if (item.Summary.ItemType == ResolutionItemTypes.Contradiction)
        {
            return ContainsContradictionCue(evidence.Summary)
                ? ResolutionDecisionStances.Supports
                : ResolutionDecisionStances.Ambiguous;
        }

        if (item.Summary.ItemType == ResolutionItemTypes.MissingData)
        {
            if (evidence.TrustFactor <= 0.45f)
            {
                return ResolutionDecisionStances.Supports;
            }

            return evidence.TrustFactor >= 0.78f
                ? ResolutionDecisionStances.Challenges
                : ResolutionDecisionStances.Ambiguous;
        }

        if (item.Summary.ItemType == ResolutionItemTypes.Clarification
            || item.Summary.ItemType == ResolutionItemTypes.BlockedBranch)
        {
            return ContainsUncertaintyCue(evidence.Summary)
                ? ResolutionDecisionStances.Supports
                : ResolutionDecisionStances.Ambiguous;
        }

        if (ContainsStabilityCue(evidence.Summary) && evidence.TrustFactor >= 0.78f)
        {
            return ResolutionDecisionStances.Challenges;
        }

        return evidence.TrustFactor <= 0.45f
            ? ResolutionDecisionStances.Supports
            : ResolutionDecisionStances.Ambiguous;
    }

    private static string ResolveLinkageHeuristicCalibration(
        ProjectedResolutionItem item,
        ResolutionEvidenceSummary evidence,
        string stance)
    {
        if (string.IsNullOrWhiteSpace(evidence.SenderDisplay))
        {
            return ResolutionDecisionHeuristicCalibrations.Low;
        }

        if (stance == ResolutionDecisionStances.Ambiguous)
        {
            return ResolutionDecisionHeuristicCalibrations.Low;
        }

        if (item.Summary.ItemType == ResolutionItemTypes.Contradiction
            || item.Summary.ItemType == ResolutionItemTypes.MissingData
            || item.Summary.ItemType == ResolutionItemTypes.Clarification
            || item.Summary.ItemType == ResolutionItemTypes.BlockedBranch)
        {
            return ResolutionDecisionHeuristicCalibrations.Medium;
        }

        return evidence.TrustFactor <= 0.45f
            ? ResolutionDecisionHeuristicCalibrations.Low
            : ResolutionDecisionHeuristicCalibrations.Medium;
    }

    private static bool HasNoteKind(ProjectedResolutionItem item, string kind)
        => item.Notes.Any(x => string.Equals(x.Kind, kind, StringComparison.OrdinalIgnoreCase));

    private static (string Stance, string Calibration, string Role, string Reason) ResolveReviewEvidenceSignalHeuristic(
        ResolutionEvidenceSummary evidence)
    {
        var summary = evidence.Summary?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(summary))
        {
            return (
                ResolutionDecisionStances.Ambiguous,
                ResolutionDecisionHeuristicCalibrations.Low,
                "contextual",
                "текст слишком пустой для более сильного вывода");
        }

        var letterCount = summary.Count(char.IsLetter);
        var digitCount = summary.Count(char.IsDigit);
        var tokenCount = summary
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Length;
        var symbolCount = summary.Count(x => !char.IsWhiteSpace(x) && !char.IsLetterOrDigit(x));
        var mostlySymbolic = letterCount == 0 && symbolCount > 0;
        var timingFragment = letterCount == 0 && digitCount > 0;
        if (mostlySymbolic || timingFragment)
        {
            return (
                ResolutionDecisionStances.Ambiguous,
                ResolutionDecisionHeuristicCalibrations.Low,
                "contextual",
                "сообщение выглядит как реакция, эмодзи или короткий временной фрагмент");
        }

        if (ContainsUncertaintyCue(summary))
        {
            return (
                ResolutionDecisionStances.Ambiguous,
                ResolutionDecisionHeuristicCalibrations.Low,
                "contextual",
                "в самой формулировке остается заметная неопределенность");
        }

        var weakLexicalSignal = tokenCount <= 5 || letterCount < 18 || summary.Length < 28;
        if (weakLexicalSignal || ContainsEmotionalCue(summary))
        {
            return (
                ResolutionDecisionStances.Supports,
                ResolutionDecisionHeuristicCalibrations.Low,
                "weak_support",
                "это короткий или локальный фрагмент контекста, поэтому опора на него ограничена");
        }

        return (
            ResolutionDecisionStances.Supports,
            ResolutionDecisionHeuristicCalibrations.Medium,
            "support",
            "сигнал достаточно содержательный, чтобы быть одной из bounded-опор ручного решения");
    }

    private static bool ContainsContradictionCue(string? summary)
    {
        if (string.IsNullOrWhiteSpace(summary))
        {
            return false;
        }

        var text = summary.Trim();
        return text.Contains("но", StringComparison.OrdinalIgnoreCase)
               || text.Contains("однако", StringComparison.OrdinalIgnoreCase)
               || text.Contains("противореч", StringComparison.OrdinalIgnoreCase)
               || text.Contains("не совп", StringComparison.OrdinalIgnoreCase)
               || text.Contains("расход", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsUncertaintyCue(string? summary)
    {
        if (string.IsNullOrWhiteSpace(summary))
        {
            return false;
        }

        var text = summary.Trim();
        return text.Contains("неяс", StringComparison.OrdinalIgnoreCase)
               || text.Contains("непонят", StringComparison.OrdinalIgnoreCase)
               || text.Contains("возможно", StringComparison.OrdinalIgnoreCase)
               || text.Contains("кажется", StringComparison.OrdinalIgnoreCase)
               || text.Contains("сомнен", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsStabilityCue(string? summary)
    {
        if (string.IsNullOrWhiteSpace(summary))
        {
            return false;
        }

        var text = summary.Trim();
        return text.Contains("стабиль", StringComparison.OrdinalIgnoreCase)
               || text.Contains("подтвержден", StringComparison.OrdinalIgnoreCase)
               || text.Contains("однознач", StringComparison.OrdinalIgnoreCase)
               || text.Contains("регуляр", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsEmotionalCue(string? summary)
    {
        if (string.IsNullOrWhiteSpace(summary))
        {
            return false;
        }

        var text = summary.Trim();
        return text.Contains("любл", StringComparison.OrdinalIgnoreCase)
               || text.Contains("обид", StringComparison.OrdinalIgnoreCase)
               || text.Contains("зл", StringComparison.OrdinalIgnoreCase)
               || text.Contains("страш", StringComparison.OrdinalIgnoreCase)
               || text.Contains("рад", StringComparison.OrdinalIgnoreCase)
               || text.Contains("трев", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildSecondaryText(float trustFactor, string status, DateTime updatedAtUtc)
        => $"Доверие {FormatTrustPercentRu(trustFactor)} · {DescribeStatusRu(status)} · обновлено {updatedAtUtc:yyyy-MM-dd HH:mm} UTC";

    private static string FormatTrustPercentRu(float trustFactor)
        => $"{Math.Round(Clamp01(trustFactor) * 100, 0, MidpointRounding.AwayFromZero):0}%";

    private static string GetFirstVisibleNote(
        IEnumerable<ResolutionDetailNote> notes,
        params string[] preferredKinds)
    {
        foreach (var kind in preferredKinds)
        {
            var match = notes.FirstOrDefault(x =>
                string.Equals(x.Kind, kind, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(x.Text));
            if (match != null)
            {
                return SanitizeVisibleCardText(match.Text);
            }
        }

        return string.Empty;
    }

    private static string EnsureSentence(string? value)
    {
        var sanitized = SanitizeVisibleCardText(value);
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            return string.Empty;
        }

        return sanitized.EndsWith('.')
               || sanitized.EndsWith('!')
               || sanitized.EndsWith('?')
            ? sanitized
            : $"{sanitized}.";
    }

    private static string SanitizeVisibleCardText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var sanitized = value.Trim();
        sanitized = GuidRegex.Replace(sanitized, "[id]");
        sanitized = InternalKeyValueRegex.Replace(sanitized, string.Empty);
        sanitized = ScopeTokenRegex.Replace(sanitized, "текущий контекст");
        sanitized = ReplaceKnownInternalTokens(sanitized);
        sanitized = WhitespaceRegex.Replace(sanitized, " ").Trim(' ', ',', ';', ':');
        return sanitized;
    }

    private static string SanitizeVisibleCardTextOrDefault(string? value, string fallback)
    {
        var sanitized = SanitizeVisibleCardText(value);
        if (!string.IsNullOrWhiteSpace(sanitized))
        {
            return sanitized;
        }

        return SanitizeVisibleCardText(fallback);
    }

    private static string ReplaceKnownInternalTokens(string value)
    {
        return value
            .Replace("safe_mode", "безопасный режим", StringComparison.OrdinalIgnoreCase)
            .Replace("review_only", "режим только проверки", StringComparison.OrdinalIgnoreCase)
            .Replace("budget_protected", "режим защиты бюджета", StringComparison.OrdinalIgnoreCase)
            .Replace("promotion_blocked", "режим блокировки продвижения", StringComparison.OrdinalIgnoreCase)
            .Replace("runtime_control", "контур рантайма", StringComparison.OrdinalIgnoreCase)
            .Replace("clarification_branch", "ветка уточнения", StringComparison.OrdinalIgnoreCase)
            .Replace("stage8_queue", "очередь пересчета", StringComparison.OrdinalIgnoreCase)
            .Replace("durable_object_metadata", "объект сводки", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatCountRu(int count, string one, string few, string many)
    {
        var remainder100 = count % 100;
        var remainder10 = count % 10;
        var noun = remainder100 is >= 11 and <= 14
            ? many
            : remainder10 switch
            {
                1 => one,
                2 or 3 or 4 => few,
                _ => many
            };

        return $"{count} {noun}";
    }

    private static string DescribeStatusRu(string status)
    {
        return status switch
        {
            ResolutionItemStatuses.Open => "открыто",
            ResolutionItemStatuses.Blocked => "заблокировано",
            ResolutionItemStatuses.Queued => "в очереди",
            ResolutionItemStatuses.Running => "в работе",
            ResolutionItemStatuses.AttentionRequired => "требует внимания",
            ResolutionItemStatuses.Degraded => "деградировано",
            _ => string.IsNullOrWhiteSpace(status) ? "неизвестно" : SanitizeVisibleCardText(status)
        };
    }

    private static string DescribeRuntimeStateRu(string state)
    {
        return state switch
        {
            RuntimeControlStates.Normal => "нормальный режим",
            RuntimeControlStates.SafeMode => "безопасный режим",
            RuntimeControlStates.ReviewOnly => "режим только проверки",
            RuntimeControlStates.BudgetProtected => "режим защиты бюджета",
            RuntimeControlStates.PromotionBlocked => "режим блокировки продвижения",
            RuntimeControlStates.Degraded => "деградированный режим",
            _ => SanitizeVisibleCardText(state)
        };
    }

    private static string DescribeRuntimeTargetRu(DbRuntimeDefect defect)
    {
        if (!string.IsNullOrWhiteSpace(defect.ObjectType))
        {
            return DescribeFamilyRu(defect.ObjectType);
        }

        if (!string.IsNullOrWhiteSpace(defect.DefectClass))
        {
            return defect.DefectClass switch
            {
                RuntimeDefectClasses.ControlPlane => "контур рантайма",
                RuntimeDefectClasses.Ingestion => "контур загрузки данных",
                RuntimeDefectClasses.Data => "данные",
                _ => SanitizeVisibleCardText(defect.DefectClass)
            };
        }

        return "рантайм";
    }

    private static string NormalizeDefectTitle(DbRuntimeDefect defect)
    {
        if (!string.IsNullOrWhiteSpace(defect.ObjectType))
        {
            return $"{DescribeFamily(defect.ObjectType)} runtime review";
        }

        return $"{defect.DefectClass} runtime review";
    }

    private static string DescribeFamilyRu(string? family)
    {
        return family switch
        {
            Stage8RecomputeTargetFamilies.Stage6Bootstrap => "стартовый контекст",
            Stage8RecomputeTargetFamilies.DossierProfile => "досье и профиль",
            Stage8RecomputeTargetFamilies.PairDynamics => "динамика пары",
            Stage8RecomputeTargetFamilies.TimelineObjects => "таймлайн",
            Stage7DurableObjectFamilies.Dossier => "досье",
            Stage7DurableObjectFamilies.Profile => "профиль",
            Stage7DurableObjectFamilies.Event => "событие",
            Stage7DurableObjectFamilies.TimelineEpisode => "эпизод таймлайна",
            Stage7DurableObjectFamilies.StoryArc => "сюжетная арка",
            "runtime_control" => "контур рантайма",
            _ => string.IsNullOrWhiteSpace(family) ? "текущий контекст" : SanitizeVisibleCardText(family)
        };
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
        string? evidenceSortBy,
        string? evidenceSortDirection,
        int evidenceLimit,
        CancellationToken ct)
    {
        var boundedLimit = Math.Clamp(evidenceLimit, 1, 20);
        var normalizedSortBy = ResolutionEvidenceSortFields.Normalize(evidenceSortBy);
        var normalizedSortDirection = ResolutionSortDirections.Normalize(evidenceSortDirection);

        if (durableMetadataIds.Count > 0)
        {
            var query = from link in db.DurableObjectEvidenceLinks.AsNoTracking()
                        join evidence in db.EvidenceItems.AsNoTracking()
                            on link.EvidenceItemId equals evidence.Id
                        join sourceObject in db.SourceObjects.AsNoTracking()
                            on evidence.SourceObjectId equals sourceObject.Id into sourceObjects
                        from sourceObject in sourceObjects.DefaultIfEmpty()
                        join message in db.Messages.AsNoTracking()
                            on sourceObject != null ? sourceObject.SourceMessageId : null equals (long?)message.Id into messages
                        from message in messages.DefaultIfEmpty()
                        where durableMetadataIds.Contains(link.DurableObjectMetadataId)
                        select new EvidenceProjectionRow
                        {
                            Id = evidence.Id,
                            SummaryText = evidence.SummaryText,
                            Confidence = evidence.Confidence,
                            ObservedAt = evidence.ObservedAt,
                            CreatedAt = evidence.CreatedAt,
                            SenderDisplay = message != null ? message.SenderName : null,
                            SourceRef = sourceObject != null ? sourceObject.SourceRef : null,
                            SourceLabel = sourceObject != null ? sourceObject.DisplayLabel : null
                        };

            var rows = await ApplyEvidenceOrdering(query, normalizedSortBy, normalizedSortDirection)
                .Take(boundedLimit)
                .ToListAsync(ct);

            return rows.Select(x => new ResolutionEvidenceSummary
            {
                EvidenceItemId = x.Id,
                Summary = string.IsNullOrWhiteSpace(x.SummaryText) ? x.Id.ToString("D") : x.SummaryText.Trim(),
                TrustFactor = Clamp01(x.Confidence),
                ObservedAtUtc = x.ObservedAt?.ToUniversalTime(),
                SenderDisplay = NormalizeSenderDisplay(x.SenderDisplay),
                SourceRef = x.SourceRef,
                SourceLabel = x.SourceLabel
            }).ToList();
        }

        if (!useTrackedPersonEvidence)
        {
            return [];
        }

        var personQuery = from personLink in db.EvidenceItemPersonLinks.AsNoTracking()
                          join evidence in db.EvidenceItems.AsNoTracking()
                              on personLink.EvidenceItemId equals evidence.Id
                          join sourceObject in db.SourceObjects.AsNoTracking()
                              on evidence.SourceObjectId equals sourceObject.Id into sourceObjects
                          from sourceObject in sourceObjects.DefaultIfEmpty()
                          join message in db.Messages.AsNoTracking()
                              on sourceObject != null ? sourceObject.SourceMessageId : null equals (long?)message.Id into messages
                          from message in messages.DefaultIfEmpty()
                          where personLink.ScopeKey == trackedPerson.ScopeKey
                              && personLink.PersonId == trackedPerson.PersonId
                          select new EvidenceProjectionRow
                          {
                              Id = evidence.Id,
                              SummaryText = evidence.SummaryText,
                              Confidence = evidence.Confidence,
                              ObservedAt = evidence.ObservedAt,
                              CreatedAt = evidence.CreatedAt,
                              SenderDisplay = message != null ? message.SenderName : null,
                              SourceRef = sourceObject != null ? sourceObject.SourceRef : null,
                              SourceLabel = sourceObject != null ? sourceObject.DisplayLabel : null
                          };

        var personRows = await ApplyEvidenceOrdering(personQuery, normalizedSortBy, normalizedSortDirection)
            .Take(boundedLimit)
            .ToListAsync(ct);

        return personRows.Select(x => new ResolutionEvidenceSummary
        {
            EvidenceItemId = x.Id,
            Summary = string.IsNullOrWhiteSpace(x.SummaryText) ? x.Id.ToString("D") : x.SummaryText.Trim(),
            TrustFactor = Clamp01(x.Confidence),
            ObservedAtUtc = x.ObservedAt?.ToUniversalTime(),
            SenderDisplay = NormalizeSenderDisplay(x.SenderDisplay),
            SourceRef = x.SourceRef,
            SourceLabel = x.SourceLabel
        }).ToList();
    }

    private static string? NormalizeSenderDisplay(string? senderDisplay)
    {
        return string.IsNullOrWhiteSpace(senderDisplay)
            ? null
            : senderDisplay.Trim();
    }

    private static IQueryable<EvidenceProjectionRow> ApplyEvidenceOrdering(
        IQueryable<EvidenceProjectionRow> query,
        string sortBy,
        string sortDirection)
    {
        var descending = string.Equals(sortDirection, ResolutionSortDirections.Desc, StringComparison.Ordinal);
        return sortBy switch
        {
            ResolutionEvidenceSortFields.TrustFactor => descending
                ? query.OrderByDescending(x => x.Confidence)
                    .ThenByDescending(x => x.ObservedAt)
                    .ThenByDescending(x => x.CreatedAt)
                : query.OrderBy(x => x.Confidence)
                    .ThenBy(x => x.ObservedAt)
                    .ThenBy(x => x.CreatedAt),
            _ => descending
                ? query.OrderByDescending(x => x.ObservedAt)
                    .ThenByDescending(x => x.CreatedAt)
                    .ThenByDescending(x => x.Confidence)
                : query.OrderBy(x => x.ObservedAt)
                    .ThenBy(x => x.CreatedAt)
                    .ThenBy(x => x.Confidence)
        };
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

    private sealed class EvidenceProjectionRow
    {
        public Guid Id { get; set; }
        public string? SummaryText { get; set; }
        public float Confidence { get; set; }
        public DateTime? ObservedAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public string? SenderDisplay { get; set; }
        public string? SourceRef { get; set; }
        public string? SourceLabel { get; set; }
    }
}
