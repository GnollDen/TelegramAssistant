using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Infrastructure.Database;

public sealed class OperatorResolutionApplicationService : IOperatorResolutionApplicationService
{
    private const string ActiveStatus = "active";
    private const string TrackedPersonSwitchSessionEventType = "tracked_person_switch";
    private const string OfflineEventScopeItemPrefix = "offline_event:";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> WorkspaceSummaryFamilies = new(StringComparer.Ordinal)
    {
        Stage7DurableObjectFamilies.Dossier,
        Stage7DurableObjectFamilies.Profile,
        Stage7DurableObjectFamilies.PairDynamics,
        Stage7DurableObjectFamilies.Event,
        Stage7DurableObjectFamilies.TimelineEpisode,
        Stage7DurableObjectFamilies.StoryArc
    };

    private readonly IDbContextFactory<TgAssistantDbContext> _dbFactory;
    private readonly IResolutionReadService _resolutionReadService;
    private readonly IResolutionActionService _resolutionActionService;
    private readonly IOperatorOfflineEventRepository _operatorOfflineEventRepository;
    private readonly ILogger<OperatorResolutionApplicationService> _logger;

    public OperatorResolutionApplicationService(
        IDbContextFactory<TgAssistantDbContext> dbFactory,
        IResolutionReadService resolutionReadService,
        IResolutionActionService resolutionActionService,
        IOperatorOfflineEventRepository operatorOfflineEventRepository,
        ILogger<OperatorResolutionApplicationService> logger)
    {
        _dbFactory = dbFactory;
        _resolutionReadService = resolutionReadService;
        _resolutionActionService = resolutionActionService;
        _operatorOfflineEventRepository = operatorOfflineEventRepository;
        _logger = logger;
    }

    public async Task<OperatorTrackedPersonQueryResult> QueryTrackedPersonsAsync(
        OperatorTrackedPersonQueryRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var nowUtc = DateTime.UtcNow;
        var validationFailure = ValidateOperatorIdentityAndSession(
            request.OperatorIdentity,
            request.Session,
            requireActiveTrackedPerson: false,
            requireSupportedMode: false,
            requireActiveScopeItem: false,
            requireMatchingUnfinishedStep: false,
            expectedTrackedPersonId: null,
            expectedScopeItemKey: null,
            nowUtc);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var trackedPersons = await LoadTrackedPersonsAsync(db, Math.Clamp(request.Limit, 1, 50), ct);

        var response = new OperatorTrackedPersonQueryResult
        {
            Accepted = validationFailure == null,
            FailureReason = validationFailure,
            Session = CloneSession(request.Session, nowUtc),
            TrackedPersons = trackedPersons
        };

        if (validationFailure != null)
        {
            return response;
        }

        if (request.Session.ActiveTrackedPersonId != Guid.Empty)
        {
            var active = trackedPersons.FirstOrDefault(x => x.TrackedPersonId == request.Session.ActiveTrackedPersonId);
            if (active == null)
            {
                response.Accepted = false;
                response.FailureReason = "session_active_tracked_person_not_available";
                response.Session = ClearTrackedPersonSelection(response.Session);
                response.SelectionSource = "stale_session";
                return response;
            }

            response.ActiveTrackedPersonId = active.TrackedPersonId;
            response.ActiveTrackedPerson = active;
            response.SelectionSource = "session";
            response.Session.ActiveTrackedPersonId = active.TrackedPersonId;
            response.Session.ActiveMode = EnsureQueueMode(response.Session.ActiveMode);
            return response;
        }

        if (request.PreferredTrackedPersonId.HasValue)
        {
            var preferred = trackedPersons.FirstOrDefault(x => x.TrackedPersonId == request.PreferredTrackedPersonId.Value);
            if (preferred == null)
            {
                response.Accepted = false;
                response.FailureReason = "preferred_tracked_person_not_available";
                response.SelectionSource = "preferred";
                return response;
            }

            response.ActiveTrackedPersonId = preferred.TrackedPersonId;
            response.ActiveTrackedPerson = preferred;
            response.SelectionSource = "preferred";
            response.Session.ActiveTrackedPersonId = preferred.TrackedPersonId;
            response.Session.ActiveMode = EnsureQueueMode(response.Session.ActiveMode);
            return response;
        }

        if (trackedPersons.Count == 1)
        {
            response.AutoSelected = true;
            response.SelectionSource = "auto_single";
            response.ActiveTrackedPersonId = trackedPersons[0].TrackedPersonId;
            response.ActiveTrackedPerson = trackedPersons[0];
            response.Session.ActiveTrackedPersonId = trackedPersons[0].TrackedPersonId;
            response.Session.ActiveMode = EnsureQueueMode(response.Session.ActiveMode);
        }

        return response;
    }

    public async Task<OperatorTrackedPersonSelectionResult> SelectTrackedPersonAsync(
        OperatorTrackedPersonSelectionRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var eventTimeUtc = request.RequestedAtUtc == default ? DateTime.UtcNow : request.RequestedAtUtc;
        var selectionRequestId = BuildSessionRequestId(request.Session?.OperatorSessionId, TrackedPersonSwitchSessionEventType, eventTimeUtc);
        var normalizedSession = CloneSession(request.Session, eventTimeUtc);
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var validationFailure = ValidateOperatorIdentityAndSession(
            request.OperatorIdentity,
            request.Session,
            requireActiveTrackedPerson: false,
            requireSupportedMode: false,
            requireActiveScopeItem: false,
            requireMatchingUnfinishedStep: false,
            expectedTrackedPersonId: null,
            expectedScopeItemKey: null,
            eventTimeUtc);

        if (request.TrackedPersonId == Guid.Empty && validationFailure == null)
        {
            validationFailure = "tracked_person_id_required";
        }

        var trackedPersons = await LoadTrackedPersonsAsync(db, 50, ct);
        var target = request.TrackedPersonId == Guid.Empty
            ? null
            : trackedPersons.FirstOrDefault(x => x.TrackedPersonId == request.TrackedPersonId);
        if (target == null && validationFailure == null)
        {
            validationFailure = "tracked_person_not_found_or_inactive";
        }

        if (validationFailure != null)
        {
            var deniedAuditId = await PersistTrackedPersonSelectionAuditAsync(
                db,
                request,
                selectionRequestId,
                activeTrackedPerson: null,
                updatedSession: normalizedSession,
                scopeChanged: false,
                accepted: false,
                failureReason: validationFailure,
                eventTimeUtc,
                ct);

            return new OperatorTrackedPersonSelectionResult
            {
                Accepted = false,
                FailureReason = validationFailure,
                AuditEventId = deniedAuditId,
                Session = normalizedSession
            };
        }

        var scopeChanged = normalizedSession.ActiveTrackedPersonId != target!.TrackedPersonId;
        normalizedSession.ActiveTrackedPersonId = target.TrackedPersonId;
        normalizedSession.ActiveScopeItemKey = null;
        normalizedSession.ActiveMode = OperatorModeTypes.ResolutionQueue;
        normalizedSession.UnfinishedStep = null;

        var auditEventId = await PersistTrackedPersonSelectionAuditAsync(
            db,
            request,
            selectionRequestId,
            target,
            normalizedSession,
            scopeChanged,
            accepted: true,
            failureReason: null,
            eventTimeUtc,
            ct);

        _logger.LogInformation(
            "Tracked person selection accepted: audit_event_id={AuditEventId}, tracked_person_id={TrackedPersonId}, operator_id={OperatorId}, session_id={SessionId}, scope_changed={ScopeChanged}",
            auditEventId,
            target.TrackedPersonId,
            request.OperatorIdentity.OperatorId,
            normalizedSession.OperatorSessionId,
            scopeChanged);

        return new OperatorTrackedPersonSelectionResult
        {
            Accepted = true,
            ScopeChanged = scopeChanged,
            AuditEventId = auditEventId,
            ActiveTrackedPerson = target,
            Session = normalizedSession
        };
    }

    public async Task<OperatorPersonWorkspaceListQueryResult> QueryPersonWorkspaceListAsync(
        OperatorPersonWorkspaceListQueryRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var nowUtc = DateTime.UtcNow;
        var validationFailure = ValidateOperatorIdentityAndSession(
            request.OperatorIdentity,
            request.Session,
            requireActiveTrackedPerson: false,
            requireSupportedMode: false,
            requireActiveScopeItem: false,
            requireMatchingUnfinishedStep: false,
            expectedTrackedPersonId: null,
            expectedScopeItemKey: null,
            nowUtc);

        var response = new OperatorPersonWorkspaceListQueryResult
        {
            Accepted = validationFailure == null,
            FailureReason = validationFailure,
            Session = CloneSession(request.Session, nowUtc)
        };
        if (validationFailure != null)
        {
            return response;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var trackedPersons = await LoadTrackedPersonsAsync(db, 200, ct);
        await PopulateResolutionSignalsAsync(trackedPersons, ct);

        var normalizedSearch = NormalizeOptional(request.Search);
        IEnumerable<OperatorTrackedPersonScopeSummary> filtered = trackedPersons;
        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            filtered = trackedPersons.Where(x =>
                x.DisplayName.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase)
                || x.ScopeKey.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase));
        }

        var boundedLimit = Math.Clamp(request.Limit, 1, 100);
        var ordered = filtered
            .OrderByDescending(x => x.HasUnresolved)
            .ThenByDescending(x => x.RecentUpdateAtUtc)
            .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Take(boundedLimit)
            .ToList();

        var session = CloneSession(request.Session, nowUtc);
        if (string.IsNullOrWhiteSpace(session.ActiveMode))
        {
            session.ActiveMode = OperatorModeTypes.ResolutionQueue;
        }

        response.Accepted = true;
        response.FailureReason = null;
        response.Session = session;
        response.ActiveTrackedPersonId = session.ActiveTrackedPersonId == Guid.Empty
            ? null
            : session.ActiveTrackedPersonId;
        response.TotalCount = trackedPersons.Count;
        response.FilteredCount = filtered.Count();
        response.Persons = ordered;
        return response;
    }

    public async Task<OperatorPersonWorkspaceSummaryQueryResult> QueryPersonWorkspaceSummaryAsync(
        OperatorPersonWorkspaceSummaryQueryRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var nowUtc = DateTime.UtcNow;
        var trackedPersonId = ResolveTrackedPersonId(request.TrackedPersonId, request.Session);
        var validationFailure = ValidateOperatorIdentityAndSession(
            request.OperatorIdentity,
            request.Session,
            requireActiveTrackedPerson: false,
            requireSupportedMode: false,
            requireActiveScopeItem: false,
            requireMatchingUnfinishedStep: false,
            expectedTrackedPersonId: null,
            expectedScopeItemKey: null,
            nowUtc);
        if (validationFailure != null)
        {
            return new OperatorPersonWorkspaceSummaryQueryResult
            {
                Accepted = false,
                FailureReason = validationFailure,
                Session = CloneSession(request.Session, nowUtc)
            };
        }

        if (!trackedPersonId.HasValue || trackedPersonId.Value == Guid.Empty)
        {
            return new OperatorPersonWorkspaceSummaryQueryResult
            {
                Accepted = false,
                FailureReason = "tracked_person_id_required",
                Session = CloneSession(request.Session, nowUtc)
            };
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var trackedPerson = await LoadTrackedPersonByIdAsync(db, trackedPersonId.Value, ct);
        if (trackedPerson == null)
        {
            return new OperatorPersonWorkspaceSummaryQueryResult
            {
                Accepted = false,
                FailureReason = "tracked_person_not_found_or_inactive",
                Session = CloneSession(request.Session, nowUtc)
            };
        }

        await PopulateResolutionSignalsAsync([trackedPerson], ct);

        var durableSources = await LoadWorkspaceDurableSourcesAsync(
            db,
            trackedPerson.ScopeKey,
            trackedPerson.TrackedPersonId,
            ct);
        var metadataIds = durableSources.Select(x => x.Metadata.Id).Distinct().ToList();
        var evidenceCounts = metadataIds.Count == 0
            ? new Dictionary<Guid, int>()
            : await db.DurableObjectEvidenceLinks
                .AsNoTracking()
                .Where(x => metadataIds.Contains(x.DurableObjectMetadataId))
                .GroupBy(x => x.DurableObjectMetadataId)
                .Select(x => new { x.Key, Count = x.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        var cards = durableSources
            .GroupBy(x => x.Metadata.ObjectFamily, StringComparer.Ordinal)
            .Select(group =>
            {
                var familySources = group.ToList();
                var confidence = Clamp01(familySources.Average(x => x.Metadata.Confidence));
                var coverage = Clamp01(familySources.Average(x => x.Metadata.Coverage));
                var freshness = Clamp01(familySources.Average(x => x.Metadata.Freshness));
                var stability = Clamp01(familySources.Average(x => x.Metadata.Stability));
                var contradictionCount = familySources.Sum(x => CountJsonArray(x.Metadata.ContradictionMarkersJson));
                var evidenceLinkCount = familySources.Sum(x => evidenceCounts.GetValueOrDefault(x.Metadata.Id));
                var latest = familySources
                    .OrderByDescending(x => x.UpdatedAtUtc)
                    .FirstOrDefault();

                return new OperatorWorkspaceDurableFamilyCard
                {
                    Family = group.Key,
                    Label = ToFamilyLabel(group.Key),
                    ObjectCount = familySources.Count,
                    Trust = confidence,
                    Uncertainty = Clamp01(1f - confidence),
                    Confidence = confidence,
                    Coverage = coverage,
                    Freshness = freshness,
                    Stability = stability,
                    ContradictionCount = contradictionCount,
                    EvidenceLinkCount = evidenceLinkCount,
                    LatestUpdatedAtUtc = latest?.UpdatedAtUtc,
                    TruthLayer = MostCommon(familySources.Select(x => x.Metadata.TruthLayer)),
                    PromotionState = MostCommon(familySources.Select(x => x.Metadata.PromotionState)),
                    LatestSummary = latest?.SummarySnippet
                };
            })
            .OrderByDescending(x => x.ObjectCount)
            .ThenBy(x => x.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var truthLayerCounts = durableSources
            .GroupBy(x => NormalizeOptional(x.Metadata.TruthLayer) ?? "unknown", StringComparer.Ordinal)
            .OrderByDescending(x => x.Count())
            .ThenBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => new ResolutionFacetCount
            {
                Key = x.Key,
                Count = x.Count()
            })
            .ToList();

        var promotionStateCounts = durableSources
            .GroupBy(x => NormalizeOptional(x.Metadata.PromotionState) ?? "unknown", StringComparer.Ordinal)
            .OrderByDescending(x => x.Count())
            .ThenBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => new ResolutionFacetCount
            {
                Key = x.Key,
                Count = x.Count()
            })
            .ToList();

        var provenance = durableSources
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Take(16)
            .Select(x => new OperatorWorkspaceProvenanceItem
            {
                Family = x.Metadata.ObjectFamily,
                ObjectKey = x.Metadata.ObjectKey,
                DurableObjectMetadataId = x.Metadata.Id,
                LastModelPassRunId = x.Metadata.CreatedByModelPassRunId,
                EvidenceLinkCount = evidenceCounts.GetValueOrDefault(x.Metadata.Id),
                UpdatedAtUtc = x.UpdatedAtUtc,
                Summary = x.SummarySnippet
            })
            .ToList();

        var overallTrust = durableSources.Count == 0
            ? 0f
            : Clamp01(durableSources.Average(x => x.Metadata.Confidence));

        var session = CloneSession(request.Session, nowUtc);
        session.ActiveTrackedPersonId = trackedPerson.TrackedPersonId;
        session.ActiveScopeItemKey = null;
        session.ActiveMode = OperatorModeTypes.ResolutionQueue;
        session.UnfinishedStep = null;

        return new OperatorPersonWorkspaceSummaryQueryResult
        {
            Accepted = true,
            FailureReason = null,
            Session = session,
            Workspace = new OperatorPersonWorkspaceView
            {
                TrackedPerson = trackedPerson,
                Sections = BuildWorkspaceSections(),
                Summary = new OperatorPersonWorkspaceSummarySectionView
                {
                    GeneratedAtUtc = nowUtc,
                    DurableObjectCount = durableSources.Count,
                    UnresolvedCount = trackedPerson.UnresolvedCount,
                    OverallTrust = overallTrust,
                    OverallUncertainty = Clamp01(1f - overallTrust),
                    TruthLayerCounts = truthLayerCounts,
                    PromotionStateCounts = promotionStateCounts,
                    Families = cards,
                    Provenance = provenance
                }
            }
        };
    }

    public async Task<OperatorPersonWorkspaceDossierQueryResult> QueryPersonWorkspaceDossierAsync(
        OperatorPersonWorkspaceDossierQueryRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var nowUtc = DateTime.UtcNow;
        var trackedPersonId = ResolveTrackedPersonId(request.TrackedPersonId, request.Session);
        var validationFailure = ValidateOperatorIdentityAndSession(
            request.OperatorIdentity,
            request.Session,
            requireActiveTrackedPerson: false,
            requireSupportedMode: false,
            requireActiveScopeItem: false,
            requireMatchingUnfinishedStep: false,
            expectedTrackedPersonId: null,
            expectedScopeItemKey: null,
            nowUtc);
        if (validationFailure != null)
        {
            return new OperatorPersonWorkspaceDossierQueryResult
            {
                Accepted = false,
                FailureReason = validationFailure,
                Session = CloneSession(request.Session, nowUtc)
            };
        }

        if (!trackedPersonId.HasValue || trackedPersonId.Value == Guid.Empty)
        {
            return new OperatorPersonWorkspaceDossierQueryResult
            {
                Accepted = false,
                FailureReason = "tracked_person_id_required",
                Session = CloneSession(request.Session, nowUtc)
            };
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var trackedPerson = await LoadTrackedPersonByIdAsync(db, trackedPersonId.Value, ct);
        if (trackedPerson == null)
        {
            return new OperatorPersonWorkspaceDossierQueryResult
            {
                Accepted = false,
                FailureReason = "tracked_person_not_found_or_inactive",
                Session = CloneSession(request.Session, nowUtc)
            };
        }

        await PopulateResolutionSignalsAsync([trackedPerson], ct);

        var dossiers = await db.DurableDossiers
            .AsNoTracking()
            .Where(x => x.ScopeKey == trackedPerson.ScopeKey
                && x.PersonId == trackedPerson.TrackedPersonId
                && x.Status == ActiveStatus)
            .OrderByDescending(x => x.UpdatedAt)
            .ToListAsync(ct);

        var metadataIds = dossiers
            .Select(x => x.DurableObjectMetadataId)
            .Distinct()
            .ToList();
        var metadataById = metadataIds.Count == 0
            ? new Dictionary<Guid, DbDurableObjectMetadata>()
            : await db.DurableObjectMetadata
                .AsNoTracking()
                .Where(x => metadataIds.Contains(x.Id)
                    && x.ScopeKey == trackedPerson.ScopeKey
                    && x.OwnerPersonId == trackedPerson.TrackedPersonId
                    && x.ObjectFamily == Stage7DurableObjectFamilies.Dossier
                    && x.Status == ActiveStatus)
                .ToDictionaryAsync(x => x.Id, x => x, ct);

        var metadataIdList = metadataById.Keys.ToList();
        var evidenceCounts = metadataById.Count == 0
            ? new Dictionary<Guid, int>()
            : await db.DurableObjectEvidenceLinks
                .AsNoTracking()
                .Where(x => metadataIdList.Contains(x.DurableObjectMetadataId))
                .GroupBy(x => x.DurableObjectMetadataId)
                .Select(x => new { x.Key, Count = x.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        var facts = dossiers
            .SelectMany(dossier =>
            {
                metadataById.TryGetValue(dossier.DurableObjectMetadataId, out var metadata);
                return BuildDossierFacts(dossier, metadata, evidenceCounts.GetValueOrDefault(dossier.DurableObjectMetadataId));
            })
            .OrderByDescending(x => x.Confidence)
            .ThenByDescending(x => x.UpdatedAtUtc)
            .ThenBy(x => x.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Take(200)
            .ToList();

        var provenance = dossiers
            .Take(24)
            .Select(dossier =>
            {
                metadataById.TryGetValue(dossier.DurableObjectMetadataId, out var metadata);
                var updatedAtUtc = DateTime.SpecifyKind(dossier.UpdatedAt, DateTimeKind.Utc);
                return new OperatorWorkspaceProvenanceItem
                {
                    Family = Stage7DurableObjectFamilies.Dossier,
                    ObjectKey = metadata?.ObjectKey ?? dossier.Id.ToString("D"),
                    DurableObjectMetadataId = dossier.DurableObjectMetadataId,
                    LastModelPassRunId = dossier.LastModelPassRunId ?? metadata?.CreatedByModelPassRunId,
                    EvidenceLinkCount = evidenceCounts.GetValueOrDefault(dossier.DurableObjectMetadataId),
                    UpdatedAtUtc = updatedAtUtc,
                    Summary = BuildSummarySnippet(dossier.SummaryJson)
                };
            })
            .ToList();

        var confidenceValues = metadataById.Values.Select(x => Clamp01(x.Confidence)).ToList();
        var overallTrust = confidenceValues.Count == 0 ? 0f : confidenceValues.Average();
        var durableFieldCount = facts.Count(x => !string.Equals(x.ApprovalState, DossierFieldApprovalStates.ProposalOnly, StringComparison.OrdinalIgnoreCase));
        var proposalOnlyFieldCount = facts.Count(x => string.Equals(x.ApprovalState, DossierFieldApprovalStates.ProposalOnly, StringComparison.OrdinalIgnoreCase));
        var session = CloneSession(request.Session, nowUtc);
        session.ActiveTrackedPersonId = trackedPerson.TrackedPersonId;
        session.ActiveScopeItemKey = null;
        session.ActiveMode = OperatorModeTypes.ResolutionQueue;
        session.UnfinishedStep = null;

        return new OperatorPersonWorkspaceDossierQueryResult
        {
            Accepted = true,
            FailureReason = null,
            Session = session,
            Dossier = new OperatorPersonWorkspaceDossierSectionView
            {
                GeneratedAtUtc = nowUtc,
                DurableDossierCount = dossiers.Count,
                DurableFieldCount = durableFieldCount,
                ProposalOnlyFieldCount = proposalOnlyFieldCount,
                OverallTrust = overallTrust,
                OverallUncertainty = Clamp01(1f - overallTrust),
                TotalEvidenceLinkCount = evidenceCounts.Values.Sum(),
                Facts = facts,
                Provenance = provenance
            }
        };
    }

    public async Task<OperatorResolutionQueueQueryResult> GetResolutionQueueAsync(
        OperatorResolutionQueueQueryRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var nowUtc = DateTime.UtcNow;
        var trackedPersonId = ResolveTrackedPersonId(request.TrackedPersonId, request.Session);
        var validationFailure = ValidateOperatorIdentityAndSession(
            request.OperatorIdentity,
            request.Session,
            requireActiveTrackedPerson: true,
            requireSupportedMode: true,
            requireActiveScopeItem: false,
            requireMatchingUnfinishedStep: false,
            expectedTrackedPersonId: trackedPersonId,
            expectedScopeItemKey: null,
            nowUtc)
            ?? ValidateQueueFilters(request);

        if (validationFailure != null)
        {
            return new OperatorResolutionQueueQueryResult
            {
                Accepted = false,
                FailureReason = validationFailure,
                Session = CloneSession(request.Session, nowUtc)
            };
        }

        var queue = await _resolutionReadService.GetQueueAsync(
            new ResolutionQueueRequest
            {
                TrackedPersonId = trackedPersonId!.Value,
                ItemTypes = request.ItemTypes,
                Statuses = request.Statuses,
                Priorities = request.Priorities,
                RecommendedActions = request.RecommendedActions,
                SortBy = ResolutionQueueSortFields.Normalize(request.SortBy),
                SortDirection = ResolutionSortDirections.Normalize(request.SortDirection),
                Limit = request.Limit
            },
            ct);

        var session = CloneSession(request.Session, nowUtc);
        session.ActiveTrackedPersonId = trackedPersonId.Value;
        session.ActiveScopeItemKey = null;
        session.ActiveMode = OperatorModeTypes.ResolutionQueue;
        session.UnfinishedStep = null;

        return new OperatorResolutionQueueQueryResult
        {
            Accepted = queue.ScopeBound,
            FailureReason = queue.ScopeBound ? null : queue.ScopeFailureReason,
            Session = session,
            Queue = queue
        };
    }

    public async Task<OperatorResolutionDetailQueryResult> GetResolutionDetailAsync(
        OperatorResolutionDetailQueryRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var nowUtc = DateTime.UtcNow;
        var trackedPersonId = ResolveTrackedPersonId(request.TrackedPersonId, request.Session);
        var expectedScopeItemKey = NormalizeOptional(request.ScopeItemKey);
        var validationFailure = ValidateOperatorIdentityAndSession(
            request.OperatorIdentity,
            request.Session,
            requireActiveTrackedPerson: true,
            requireSupportedMode: true,
            requireActiveScopeItem: false,
            requireMatchingUnfinishedStep: true,
            expectedTrackedPersonId: trackedPersonId,
            expectedScopeItemKey: expectedScopeItemKey,
            nowUtc)
            ?? ValidateResolutionDetailRequest(request);

        if (validationFailure != null)
        {
            return new OperatorResolutionDetailQueryResult
            {
                Accepted = false,
                FailureReason = validationFailure,
                Session = CloneSession(request.Session, nowUtc)
            };
        }

        var detail = await _resolutionReadService.GetDetailAsync(
            new ResolutionDetailRequest
            {
                TrackedPersonId = trackedPersonId!.Value,
                ScopeItemKey = expectedScopeItemKey!,
                EvidenceLimit = request.EvidenceLimit,
                EvidenceSortBy = ResolutionEvidenceSortFields.Normalize(request.EvidenceSortBy),
                EvidenceSortDirection = ResolutionSortDirections.Normalize(request.EvidenceSortDirection)
            },
            ct);

        var session = CloneSession(request.Session, nowUtc);
        session.ActiveTrackedPersonId = trackedPersonId.Value;
        session.ActiveScopeItemKey = expectedScopeItemKey;
        session.ActiveMode = OperatorModeTypes.ResolutionDetail;
        if (session.UnfinishedStep != null
            && (!string.Equals(session.UnfinishedStep.BoundScopeItemKey, expectedScopeItemKey, StringComparison.Ordinal)
                || session.UnfinishedStep.BoundTrackedPersonId != trackedPersonId.Value))
        {
            session.UnfinishedStep = null;
        }

        return new OperatorResolutionDetailQueryResult
        {
            Accepted = detail.ScopeBound && detail.ItemFound,
            FailureReason = detail.ScopeBound
                ? detail.ItemFound ? null : "scope_item_not_found"
                : detail.ScopeFailureReason,
            Session = session,
            Detail = detail
        };
    }

    public async Task<OperatorResolutionActionResultEnvelope> SubmitResolutionActionAsync(
        ResolutionActionRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var nowUtc = DateTime.UtcNow;
        var normalizedRequest = CloneActionRequest(request, nowUtc);
        if (normalizedRequest.TrackedPersonId == Guid.Empty && normalizedRequest.Session.ActiveTrackedPersonId != Guid.Empty)
        {
            normalizedRequest.TrackedPersonId = normalizedRequest.Session.ActiveTrackedPersonId;
        }

        if (string.IsNullOrWhiteSpace(normalizedRequest.ScopeItemKey)
            && !string.IsNullOrWhiteSpace(normalizedRequest.Session.ActiveScopeItemKey))
        {
            normalizedRequest.ScopeItemKey = normalizedRequest.Session.ActiveScopeItemKey!.Trim();
        }

        var actionResult = await _resolutionActionService.SubmitAsync(normalizedRequest, ct);
        return new OperatorResolutionActionResultEnvelope
        {
            Accepted = actionResult.Accepted,
            FailureReason = actionResult.Accepted ? null : actionResult.FailureReason,
            Session = CloneSession(normalizedRequest.Session, nowUtc),
            Action = actionResult
        };
    }

    public async Task<OperatorOfflineEventQueryApiResult> QueryOfflineEventsAsync(
        OperatorOfflineEventQueryApiRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var nowUtc = DateTime.UtcNow;
        var trackedPersonId = ResolveTrackedPersonId(request.TrackedPersonId, request.Session);
        var validationFailure = ValidateOperatorIdentityAndSession(
            request.OperatorIdentity,
            request.Session,
            requireActiveTrackedPerson: true,
            requireSupportedMode: true,
            requireActiveScopeItem: false,
            requireMatchingUnfinishedStep: false,
            expectedTrackedPersonId: trackedPersonId,
            expectedScopeItemKey: null,
            nowUtc)
            ?? ValidateOfflineEventQueryRequest(request);
        if (validationFailure != null)
        {
            return new OperatorOfflineEventQueryApiResult
            {
                Accepted = false,
                FailureReason = validationFailure,
                Session = CloneSession(request.Session, nowUtc)
            };
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var trackedPerson = await LoadTrackedPersonByIdAsync(db, trackedPersonId!.Value, ct);
        if (trackedPerson == null)
        {
            return new OperatorOfflineEventQueryApiResult
            {
                Accepted = false,
                FailureReason = "tracked_person_not_found_or_inactive",
                Session = CloneSession(request.Session, nowUtc)
            };
        }

        var offlineEvents = await _operatorOfflineEventRepository.QueryAsync(
            new OperatorOfflineEventQueryRequest
            {
                TrackedPersonId = trackedPersonId.Value,
                ScopeKey = trackedPerson.ScopeKey,
                Statuses = request.Statuses,
                SortBy = OperatorOfflineEventSortFields.Normalize(request.SortBy),
                SortDirection = ResolutionSortDirections.Normalize(request.SortDirection),
                Limit = request.Limit
            },
            ct);

        var session = CloneSession(request.Session, nowUtc);
        session.ActiveTrackedPersonId = trackedPersonId.Value;
        session.ActiveScopeItemKey = null;
        session.ActiveMode = OperatorModeTypes.OfflineEvent;
        session.UnfinishedStep = null;

        return new OperatorOfflineEventQueryApiResult
        {
            Accepted = offlineEvents.ScopeBound,
            FailureReason = offlineEvents.ScopeBound ? null : offlineEvents.ScopeFailureReason,
            Session = session,
            OfflineEvents = offlineEvents
        };
    }

    public async Task<OperatorOfflineEventDetailQueryResultEnvelope> GetOfflineEventDetailAsync(
        OperatorOfflineEventDetailQueryRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var nowUtc = DateTime.UtcNow;
        var trackedPersonId = ResolveTrackedPersonId(request.TrackedPersonId, request.Session);
        var expectedScopeItemKey = BuildOfflineEventScopeItemKey(request.OfflineEventId);
        var validationFailure = ValidateOperatorIdentityAndSession(
            request.OperatorIdentity,
            request.Session,
            requireActiveTrackedPerson: true,
            requireSupportedMode: true,
            requireActiveScopeItem: false,
            requireMatchingUnfinishedStep: false,
            expectedTrackedPersonId: trackedPersonId,
            expectedScopeItemKey: expectedScopeItemKey,
            nowUtc)
            ?? ValidateOfflineEventDetailRequest(request);
        if (validationFailure != null)
        {
            return new OperatorOfflineEventDetailQueryResultEnvelope
            {
                Accepted = false,
                FailureReason = validationFailure,
                Session = CloneSession(request.Session, nowUtc),
                OfflineEvent = new OperatorOfflineEventDetailView
                {
                    ScopeBound = false,
                    Found = false,
                    ScopeFailureReason = validationFailure
                }
            };
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var trackedPerson = await LoadTrackedPersonByIdAsync(db, trackedPersonId!.Value, ct);
        if (trackedPerson == null)
        {
            return new OperatorOfflineEventDetailQueryResultEnvelope
            {
                Accepted = false,
                FailureReason = "tracked_person_not_found_or_inactive",
                Session = CloneSession(request.Session, nowUtc),
                OfflineEvent = new OperatorOfflineEventDetailView
                {
                    ScopeBound = false,
                    Found = false,
                    ScopeFailureReason = "tracked_person_not_found_or_inactive"
                }
            };
        }

        var record = await _operatorOfflineEventRepository.GetByIdWithinScopeAsync(
            request.OfflineEventId,
            trackedPerson.ScopeKey,
            trackedPersonId.Value,
            ct);
        var detail = record == null
            ? new OperatorOfflineEventDetailView
            {
                ScopeBound = true,
                Found = false,
                ScopeFailureReason = "offline_event_not_found"
            }
            : BuildOfflineEventDetailView(record);

        var session = CloneSession(request.Session, nowUtc);
        session.ActiveTrackedPersonId = trackedPersonId.Value;
        session.ActiveScopeItemKey = record == null ? null : expectedScopeItemKey;
        session.ActiveMode = OperatorModeTypes.OfflineEvent;
        session.UnfinishedStep = null;

        return new OperatorOfflineEventDetailQueryResultEnvelope
        {
            Accepted = detail.ScopeBound && detail.Found,
            FailureReason = detail.ScopeBound
                ? detail.Found ? null : "offline_event_not_found"
                : detail.ScopeFailureReason,
            Session = session,
            OfflineEvent = detail
        };
    }

    public async Task<OperatorOfflineEventRefinementResult> SubmitOfflineEventRefinementAsync(
        OperatorOfflineEventRefinementRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var nowUtc = DateTime.UtcNow;
        var trackedPersonId = ResolveTrackedPersonId(request.TrackedPersonId, request.Session);
        var expectedScopeItemKey = BuildOfflineEventScopeItemKey(request.OfflineEventId);
        var validationFailure = ValidateOperatorIdentityAndSession(
            request.OperatorIdentity,
            request.Session,
            requireActiveTrackedPerson: true,
            requireSupportedMode: true,
            requireActiveScopeItem: true,
            requireMatchingUnfinishedStep: false,
            expectedTrackedPersonId: trackedPersonId,
            expectedScopeItemKey: expectedScopeItemKey,
            nowUtc)
            ?? ValidateOfflineEventRefinementRequest(request);
        if (validationFailure != null)
        {
            return new OperatorOfflineEventRefinementResult
            {
                Accepted = false,
                FailureReason = validationFailure,
                Session = CloneSession(request.Session, nowUtc),
                OfflineEvent = new OperatorOfflineEventDetailView
                {
                    ScopeBound = false,
                    Found = false,
                    ScopeFailureReason = validationFailure
                }
            };
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var trackedPerson = await LoadTrackedPersonByIdAsync(db, trackedPersonId!.Value, ct);
        if (trackedPerson == null)
        {
            return new OperatorOfflineEventRefinementResult
            {
                Accepted = false,
                FailureReason = "tracked_person_not_found_or_inactive",
                Session = CloneSession(request.Session, nowUtc),
                OfflineEvent = new OperatorOfflineEventDetailView
                {
                    ScopeBound = false,
                    Found = false,
                    ScopeFailureReason = "tracked_person_not_found_or_inactive"
                }
            };
        }

        var record = await _operatorOfflineEventRepository.RefineWithinScopeAsync(
            request.OfflineEventId,
            trackedPerson.ScopeKey,
            trackedPersonId.Value,
            request.Summary,
            request.RecordingReference,
            request.ClearRecordingReference,
            request.OperatorIdentity,
            request.Session,
            request.SubmittedAtUtc == default ? nowUtc : request.SubmittedAtUtc,
            ct);
        if (record == null)
        {
            return new OperatorOfflineEventRefinementResult
            {
                Accepted = false,
                FailureReason = "offline_event_not_found",
                Session = CloneSession(request.Session, nowUtc),
                OfflineEvent = new OperatorOfflineEventDetailView
                {
                    ScopeBound = true,
                    Found = false,
                    ScopeFailureReason = "offline_event_not_found"
                }
            };
        }

        var session = CloneSession(request.Session, nowUtc);
        session.ActiveTrackedPersonId = trackedPersonId.Value;
        session.ActiveScopeItemKey = expectedScopeItemKey;
        session.ActiveMode = OperatorModeTypes.OfflineEvent;
        session.UnfinishedStep = null;

        return new OperatorOfflineEventRefinementResult
        {
            Accepted = true,
            Session = session,
            OfflineEvent = BuildOfflineEventDetailView(record)
        };
    }

    public async Task<OperatorOfflineEventTimelineLinkageUpdateResult> SubmitOfflineEventTimelineLinkageUpdateAsync(
        OperatorOfflineEventTimelineLinkageUpdateRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var nowUtc = DateTime.UtcNow;
        var trackedPersonId = ResolveTrackedPersonId(request.TrackedPersonId, request.Session);
        var expectedScopeItemKey = BuildOfflineEventScopeItemKey(request.OfflineEventId);
        var validationFailure = ValidateOperatorIdentityAndSession(
            request.OperatorIdentity,
            request.Session,
            requireActiveTrackedPerson: true,
            requireSupportedMode: true,
            requireActiveScopeItem: true,
            requireMatchingUnfinishedStep: false,
            expectedTrackedPersonId: trackedPersonId,
            expectedScopeItemKey: expectedScopeItemKey,
            nowUtc)
            ?? ValidateOfflineEventTimelineLinkageUpdateRequest(request);
        if (validationFailure != null)
        {
            return new OperatorOfflineEventTimelineLinkageUpdateResult
            {
                Accepted = false,
                FailureReason = validationFailure,
                Session = CloneSession(request.Session, nowUtc),
                OfflineEvent = new OperatorOfflineEventDetailView
                {
                    ScopeBound = false,
                    Found = false,
                    ScopeFailureReason = validationFailure
                }
            };
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var trackedPerson = await LoadTrackedPersonByIdAsync(db, trackedPersonId!.Value, ct);
        if (trackedPerson == null)
        {
            return new OperatorOfflineEventTimelineLinkageUpdateResult
            {
                Accepted = false,
                FailureReason = "tracked_person_not_found_or_inactive",
                Session = CloneSession(request.Session, nowUtc),
                OfflineEvent = new OperatorOfflineEventDetailView
                {
                    ScopeBound = false,
                    Found = false,
                    ScopeFailureReason = "tracked_person_not_found_or_inactive"
                }
            };
        }

        var updateRecord = await _operatorOfflineEventRepository.UpdateTimelineLinkageWithinScopeAsync(
            request.OfflineEventId,
            trackedPerson.ScopeKey,
            trackedPersonId.Value,
            request.LinkageStatus,
            request.TargetFamily,
            request.TargetRef,
            request.LinkageNote,
            request.OperatorIdentity,
            request.Session,
            request.SubmittedAtUtc == default ? nowUtc : request.SubmittedAtUtc,
            ct);
        if (updateRecord == null)
        {
            return new OperatorOfflineEventTimelineLinkageUpdateResult
            {
                Accepted = false,
                FailureReason = "offline_event_not_found",
                Session = CloneSession(request.Session, nowUtc),
                OfflineEvent = new OperatorOfflineEventDetailView
                {
                    ScopeBound = true,
                    Found = false,
                    ScopeFailureReason = "offline_event_not_found"
                }
            };
        }

        var session = CloneSession(request.Session, nowUtc);
        session.ActiveTrackedPersonId = trackedPersonId.Value;
        session.ActiveScopeItemKey = expectedScopeItemKey;
        session.ActiveMode = OperatorModeTypes.OfflineEvent;
        session.UnfinishedStep = null;

        _logger.LogInformation(
            "Offline-event timeline linkage updated: offline_event_id={OfflineEventId}, tracked_person_id={TrackedPersonId}, operator_id={OperatorId}, audit_event_id={AuditEventId}, linkage_status={LinkageStatus}",
            updateRecord.OfflineEvent.OfflineEventId,
            trackedPersonId.Value,
            request.OperatorIdentity.OperatorId,
            updateRecord.AuditEventId,
            ParseTimelineLinkage(updateRecord.OfflineEvent.TimelineLinkageJson).LinkageStatus);

        return new OperatorOfflineEventTimelineLinkageUpdateResult
        {
            Accepted = true,
            AuditEventId = updateRecord.AuditEventId,
            Session = session,
            OfflineEvent = BuildOfflineEventDetailView(updateRecord.OfflineEvent)
        };
    }

    private static async Task<List<OperatorTrackedPersonScopeSummary>> LoadTrackedPersonsAsync(
        TgAssistantDbContext db,
        int limit,
        CancellationToken ct)
    {
        var evidenceCounts = await db.EvidenceItemPersonLinks
            .AsNoTracking()
            .GroupBy(x => x.PersonId)
            .Select(x => new
            {
                PersonId = x.Key,
                EvidenceCount = x.Select(y => y.EvidenceItemId).Distinct().Count()
            })
            .ToListAsync(ct);
        var evidenceLookup = evidenceCounts.ToDictionary(x => x.PersonId, x => x.EvidenceCount);

        var rows = await (from person in db.Persons.AsNoTracking()
                          join link in db.PersonOperatorLinks.AsNoTracking()
                              on person.Id equals link.PersonId
                          where person.Status == ActiveStatus
                              && person.PersonType == "tracked_person"
                              && link.Status == ActiveStatus
                          orderby person.DisplayName, person.CanonicalName, person.UpdatedAt descending
                          select new
                          {
                              person.Id,
                              person.ScopeKey,
                              person.DisplayName,
                              person.CanonicalName,
                              person.UpdatedAt
                          })
            .Distinct()
            .Take(limit)
            .ToListAsync(ct);

        return rows
            .Select(x => new OperatorTrackedPersonScopeSummary
            {
                TrackedPersonId = x.Id,
                ScopeKey = x.ScopeKey,
                DisplayName = string.IsNullOrWhiteSpace(x.DisplayName) ? x.CanonicalName : x.DisplayName,
                EvidenceCount = evidenceLookup.GetValueOrDefault(x.Id),
                UnresolvedCount = 0,
                HasUnresolved = false,
                RecentUpdateAtUtc = DateTime.SpecifyKind(x.UpdatedAt, DateTimeKind.Utc),
                LastUnresolvedAtUtc = null,
                UpdatedAtUtc = DateTime.SpecifyKind(x.UpdatedAt, DateTimeKind.Utc)
            })
            .ToList();
    }

    private async Task PopulateResolutionSignalsAsync(
        List<OperatorTrackedPersonScopeSummary> trackedPersons,
        CancellationToken ct)
    {
        foreach (var trackedPerson in trackedPersons)
        {
            var queue = await _resolutionReadService.GetQueueAsync(
                new ResolutionQueueRequest
                {
                    TrackedPersonId = trackedPerson.TrackedPersonId,
                    SortBy = ResolutionQueueSortFields.UpdatedAt,
                    SortDirection = ResolutionSortDirections.Desc,
                    Limit = 1
                },
                ct);

            if (!queue.ScopeBound)
            {
                trackedPerson.UnresolvedCount = 0;
                trackedPerson.HasUnresolved = false;
                trackedPerson.LastUnresolvedAtUtc = null;
                trackedPerson.RecentUpdateAtUtc = trackedPerson.UpdatedAtUtc;
                continue;
            }

            trackedPerson.UnresolvedCount = Math.Max(0, queue.TotalOpenCount);
            trackedPerson.HasUnresolved = trackedPerson.UnresolvedCount > 0;
            trackedPerson.LastUnresolvedAtUtc = queue.Items.FirstOrDefault()?.UpdatedAtUtc;
            trackedPerson.RecentUpdateAtUtc = trackedPerson.LastUnresolvedAtUtc.HasValue
                ? trackedPerson.LastUnresolvedAtUtc.Value > trackedPerson.UpdatedAtUtc
                    ? trackedPerson.LastUnresolvedAtUtc.Value
                    : trackedPerson.UpdatedAtUtc
                : trackedPerson.UpdatedAtUtc;
        }
    }

    private async Task<Guid> PersistTrackedPersonSelectionAuditAsync(
        TgAssistantDbContext db,
        OperatorTrackedPersonSelectionRequest request,
        string requestId,
        OperatorTrackedPersonScopeSummary? activeTrackedPerson,
        OperatorSessionContext updatedSession,
        bool scopeChanged,
        bool accepted,
        string? failureReason,
        DateTime eventTimeUtc,
        CancellationToken ct)
    {
        var auditEventId = Guid.NewGuid();
        var row = new DbOperatorAuditEvent
        {
            AuditEventId = auditEventId,
            RequestId = requestId,
            ScopeKey = activeTrackedPerson?.ScopeKey,
            TrackedPersonId = request.TrackedPersonId == Guid.Empty ? null : request.TrackedPersonId,
            ScopeItemKey = null,
            ItemType = null,
            OperatorId = NormalizeAuditValue(request.OperatorIdentity?.OperatorId),
            OperatorDisplay = NormalizeAuditValue(request.OperatorIdentity?.OperatorDisplay),
            OperatorSessionId = NormalizeAuditValue(updatedSession.OperatorSessionId),
            Surface = NormalizeAuditValue(OperatorSurfaceTypes.Normalize(updatedSession.Surface)),
            SurfaceSubject = NormalizeAuditValue(request.OperatorIdentity?.SurfaceSubject),
            AuthSource = NormalizeAuditValue(request.OperatorIdentity?.AuthSource),
            ActiveMode = NormalizeAuditValue(updatedSession.ActiveMode),
            UnfinishedStepKind = NormalizeOptional(updatedSession.UnfinishedStep?.StepKind),
            ActionType = null,
            SessionEventType = TrackedPersonSwitchSessionEventType,
            DecisionOutcome = accepted
                ? OperatorAuditDecisionOutcomes.Accepted
                : OperatorAuditDecisionOutcomes.Denied,
            FailureReason = failureReason,
            DetailsJson = JsonSerializer.Serialize(
                new
                {
                    requested_tracked_person_id = request.TrackedPersonId == Guid.Empty ? (Guid?)null : request.TrackedPersonId,
                    previous_tracked_person_id = request.Session?.ActiveTrackedPersonId == Guid.Empty ? (Guid?)null : request.Session?.ActiveTrackedPersonId,
                    resulting_tracked_person_id = updatedSession.ActiveTrackedPersonId == Guid.Empty ? (Guid?)null : updatedSession.ActiveTrackedPersonId,
                    scope_changed = scopeChanged,
                    resulting_active_mode = updatedSession.ActiveMode,
                    resulting_scope_item_key = updatedSession.ActiveScopeItemKey,
                    requested_at_utc = eventTimeUtc,
                    auth_time_utc = request.OperatorIdentity?.AuthTimeUtc == default ? null : request.OperatorIdentity?.AuthTimeUtc,
                    session_authenticated_at_utc = updatedSession.AuthenticatedAtUtc == default ? (DateTime?)null : updatedSession.AuthenticatedAtUtc,
                    session_last_seen_at_utc = updatedSession.LastSeenAtUtc == default ? (DateTime?)null : updatedSession.LastSeenAtUtc,
                    failure_reason = failureReason
                },
                JsonOptions),
            EventTimeUtc = eventTimeUtc
        };

        db.OperatorAuditEvents.Add(row);
        await db.SaveChangesAsync(ct);
        return auditEventId;
    }

    private static string? ValidateQueueFilters(OperatorResolutionQueueQueryRequest request)
    {
        if (!ResolutionQueueSortFields.IsSupported(request.SortBy))
        {
            return "unsupported_queue_sort";
        }

        if (!ResolutionSortDirections.IsSupported(request.SortDirection))
        {
            return "unsupported_sort_direction";
        }

        if (request.ItemTypes.Any(x => !ResolutionItemTypes.All.Contains(x.Trim(), StringComparer.Ordinal)))
        {
            return "unsupported_item_type_filter";
        }

        if (request.Statuses.Any(x => !ResolutionItemStatuses.All.Contains(x.Trim(), StringComparer.Ordinal)))
        {
            return "unsupported_status_filter";
        }

        if (request.Priorities.Any(x => !ResolutionItemPriorities.All.Contains(x.Trim(), StringComparer.Ordinal)))
        {
            return "unsupported_priority_filter";
        }

        if (request.RecommendedActions.Any(x => !ResolutionActionTypes.All.Contains(x.Trim(), StringComparer.Ordinal)))
        {
            return "unsupported_recommended_action_filter";
        }

        return null;
    }

    private static string? ValidateOfflineEventQueryRequest(OperatorOfflineEventQueryApiRequest request)
    {
        if (!OperatorOfflineEventSortFields.IsSupported(request.SortBy))
        {
            return "unsupported_offline_event_sort";
        }

        if (!ResolutionSortDirections.IsSupported(request.SortDirection))
        {
            return "unsupported_sort_direction";
        }

        if ((request.Statuses ?? []).Any(x => !OperatorOfflineEventStatuses.IsSupported(x)))
        {
            return "unsupported_offline_event_status_filter";
        }

        return null;
    }

    private static string? ValidateOfflineEventDetailRequest(OperatorOfflineEventDetailQueryRequest request)
    {
        return request.OfflineEventId == Guid.Empty
            ? "offline_event_id_required"
            : null;
    }

    private static string? ValidateOfflineEventRefinementRequest(OperatorOfflineEventRefinementRequest request)
    {
        if (request.OfflineEventId == Guid.Empty)
        {
            return "offline_event_id_required";
        }

        var summary = NormalizeOptional(request.Summary);
        var recording = NormalizeOptional(request.RecordingReference);
        if (string.IsNullOrWhiteSpace(summary)
            && string.IsNullOrWhiteSpace(recording)
            && !request.ClearRecordingReference)
        {
            return "offline_event_refinement_no_changes";
        }

        return null;
    }

    private static string? ValidateOfflineEventTimelineLinkageUpdateRequest(OperatorOfflineEventTimelineLinkageUpdateRequest request)
    {
        if (request.OfflineEventId == Guid.Empty)
        {
            return "offline_event_id_required";
        }

        if (!OperatorOfflineEventTimelineLinkageStatuses.IsSupported(request.LinkageStatus))
        {
            return "unsupported_offline_event_timeline_linkage_status";
        }

        var normalizedLinkageStatus = OperatorOfflineEventTimelineLinkageStatuses.Normalize(request.LinkageStatus);
        var targetFamily = NormalizeOptional(request.TargetFamily);
        var targetRef = NormalizeOptional(request.TargetRef);
        if (string.Equals(normalizedLinkageStatus, OperatorOfflineEventTimelineLinkageStatuses.Linked, StringComparison.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(targetFamily))
            {
                return "offline_event_timeline_linkage_target_family_required";
            }

            if (string.IsNullOrWhiteSpace(targetRef))
            {
                return "offline_event_timeline_linkage_target_ref_required";
            }
        }

        return null;
    }

    private static string? ValidateResolutionDetailRequest(OperatorResolutionDetailQueryRequest request)
    {
        if (string.IsNullOrWhiteSpace(NormalizeOptional(request.ScopeItemKey)))
        {
            return "scope_item_key_required";
        }

        if (!ResolutionEvidenceSortFields.IsSupported(request.EvidenceSortBy))
        {
            return "unsupported_evidence_sort";
        }

        if (!ResolutionSortDirections.IsSupported(request.EvidenceSortDirection))
        {
            return "unsupported_sort_direction";
        }

        return null;
    }

    private static OperatorOfflineEventDetailView BuildOfflineEventDetailView(OperatorOfflineEventRecord record)
    {
        var clarification = ParseClarificationState(record.ClarificationStateJson);
        var extractedInterpretation = ParseExtractedInterpretation(record.CapturePayloadJson);
        return new OperatorOfflineEventDetailView
        {
            ScopeBound = true,
            Found = true,
            OfflineEventId = record.OfflineEventId,
            TrackedPersonId = record.TrackedPersonId,
            ScopeKey = record.ScopeKey,
            Summary = record.Summary,
            RecordingReference = record.RecordingReference,
            Status = OperatorOfflineEventStatuses.Normalize(record.Status),
            Confidence = record.Confidence,
            CapturedAtUtc = record.CapturedAtUtc,
            SavedAtUtc = record.SavedAtUtc,
            UpdatedAtUtc = record.UpdatedAtUtc,
            ExtractedInterpretation = extractedInterpretation,
            CapturePayloadJson = string.IsNullOrWhiteSpace(record.CapturePayloadJson)
                ? "{}"
                : record.CapturePayloadJson,
            TimelineLinkage = ParseTimelineLinkage(record.TimelineLinkageJson),
            Clarification = clarification
        };
    }

    private static OperatorOfflineEventTimelineLinkageMetadata ParseTimelineLinkage(string? timelineLinkageJson)
    {
        var rawJson = string.IsNullOrWhiteSpace(timelineLinkageJson) ? "{}" : timelineLinkageJson.Trim();
        var metadata = new OperatorOfflineEventTimelineLinkageMetadata
        {
            RawJson = rawJson
        };
        if (!TryParseJsonObject(rawJson, out var root))
        {
            return metadata;
        }

        metadata.HasLinkage = root.EnumerateObject().Any();
        metadata.LinkageStatus = NormalizeOptional(
            GetString(root, "linkage_status")
            ?? GetString(root, "status")
            ?? GetString(root, "timeline_status")
            ?? GetNestedString(root, "target", "linkage_status")
            ?? GetNestedString(root, "target", "status"))
            ?? (metadata.HasLinkage ? "linked" : "unlinked");
        metadata.TargetFamily = NormalizeOptional(
            GetString(root, "target_family")
            ?? GetString(root, "object_family")
            ?? GetString(root, "timeline_family")
            ?? GetNestedString(root, "target", "family")
            ?? GetNestedString(root, "target", "target_family"));
        metadata.TargetRef = NormalizeOptional(
            GetString(root, "target_ref")
            ?? GetString(root, "object_ref")
            ?? GetString(root, "timeline_ref")
            ?? GetNestedString(root, "target", "ref")
            ?? GetNestedString(root, "target", "target_ref"));
        metadata.LinkedAtUtc = GetDateTime(root, "linked_at_utc")
            ?? GetDateTime(root, "updated_at_utc")
            ?? GetDateTime(root, "resolved_at_utc")
            ?? GetNestedDateTime(root, "target", "linked_at_utc");
        return metadata;
    }

    private static OperatorOfflineEventClarificationView ParseClarificationState(string? clarificationStateJson)
    {
        var view = new OperatorOfflineEventClarificationView();
        if (!TryParseJsonObject(clarificationStateJson, out var root))
        {
            return view;
        }

        view.LoopStatus = NormalizeOptional(GetString(root, "loopStatus") ?? GetString(root, "LoopStatus")) ?? "unknown";
        view.StopReason = NormalizeOptional(GetString(root, "stopReason") ?? GetString(root, "StopReason")) ?? "none";
        view.StopDetail = NormalizeOptional(GetString(root, "stopDetail") ?? GetString(root, "StopDetail"));
        view.StoppedAtUtc = GetDateTime(root, "stoppedAtUtc") ?? GetDateTime(root, "StoppedAtUtc");
        view.PartialConfidence = GetSingle(root, "partialConfidence") ?? GetSingle(root, "PartialConfidence");
        view.NextQuestionKey = NormalizeOptional(GetString(root, "nextQuestionKey") ?? GetString(root, "NextQuestionKey"));

        if ((root.TryGetProperty("questions", out var questionsElement) || root.TryGetProperty("Questions", out questionsElement))
            && questionsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var question in questionsElement.EnumerateArray())
            {
                if (question.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                view.Questions.Add(new OperatorOfflineEventClarificationQuestionSummary
                {
                    Key = NormalizeOptional(GetString(question, "key") ?? GetString(question, "Key")) ?? string.Empty,
                    Text = NormalizeOptional(GetString(question, "text") ?? GetString(question, "Text")) ?? string.Empty,
                    ExpectedInformationGain = GetSingle(question, "expectedInformationGain") ?? GetSingle(question, "ExpectedInformationGain") ?? 0f,
                    PriorityRank = GetInt32(question, "priorityRank") ?? GetInt32(question, "PriorityRank") ?? 0,
                    Status = NormalizeOptional(GetString(question, "status") ?? GetString(question, "Status")) ?? string.Empty
                });
            }
        }

        if ((root.TryGetProperty("history", out var historyElement) || root.TryGetProperty("History", out historyElement))
            && historyElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var history in historyElement.EnumerateArray())
            {
                if (history.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                view.History.Add(new OperatorOfflineEventClarificationHistoryEntry
                {
                    QuestionKey = NormalizeOptional(GetString(history, "questionKey") ?? GetString(history, "QuestionKey")) ?? string.Empty,
                    Answer = NormalizeOptional(GetString(history, "answer") ?? GetString(history, "Answer")) ?? string.Empty,
                    UnknownPattern = GetBoolean(history, "unknownPattern") ?? GetBoolean(history, "UnknownPattern") ?? false,
                    RepetitionDetected = GetBoolean(history, "repetitionDetected") ?? GetBoolean(history, "RepetitionDetected") ?? false,
                    NewTokenCount = GetInt32(history, "newTokenCount") ?? GetInt32(history, "NewTokenCount") ?? 0,
                    InformationGain = GetSingle(history, "informationGain") ?? GetSingle(history, "InformationGain") ?? 0f,
                    CapturedAtUtc = GetDateTime(history, "capturedAtUtc") ?? GetDateTime(history, "CapturedAtUtc") ?? default
                });
            }
        }

        view.QuestionCount = view.Questions.Count;
        view.AnsweredCount = view.Questions.Count(x => string.Equals(x.Status, "answered", StringComparison.OrdinalIgnoreCase));
        view.HistoryCount = view.History.Count;
        view.LastAnsweredAtUtc = view.History
            .Where(x => x.CapturedAtUtc != default)
            .OrderByDescending(x => x.CapturedAtUtc)
            .Select(x => x.CapturedAtUtc)
            .FirstOrDefault();
        if (view.LastAnsweredAtUtc == default)
        {
            view.LastAnsweredAtUtc = null;
        }

        return view;
    }

    private static string ParseExtractedInterpretation(string? capturePayloadJson)
    {
        if (!TryParseJsonObject(capturePayloadJson, out var root))
        {
            return "No extracted interpretation available.";
        }

        var interpretation = NormalizeOptional(
            GetString(root, "extracted_interpretation")
            ?? GetString(root, "interpretation")
            ?? GetString(root, "analysis")
            ?? GetString(root, "model_interpretation")
            ?? GetString(root, "summary"));
        return interpretation ?? "No extracted interpretation available.";
    }

    private static string? ValidateOperatorIdentityAndSession(
        OperatorIdentityContext? identity,
        OperatorSessionContext? session,
        bool requireActiveTrackedPerson,
        bool requireSupportedMode,
        bool requireActiveScopeItem,
        bool requireMatchingUnfinishedStep,
        Guid? expectedTrackedPersonId,
        string? expectedScopeItemKey,
        DateTime nowUtc)
    {
        if (identity == null)
        {
            return "operator_identity_required";
        }

        if (string.IsNullOrWhiteSpace(NormalizeOptional(identity.OperatorId)))
        {
            return "operator_id_required";
        }

        if (string.IsNullOrWhiteSpace(NormalizeOptional(identity.OperatorDisplay)))
        {
            return "operator_display_required";
        }

        if (string.IsNullOrWhiteSpace(NormalizeOptional(identity.SurfaceSubject)))
        {
            return "surface_subject_required";
        }

        if (string.IsNullOrWhiteSpace(NormalizeOptional(identity.AuthSource)))
        {
            return "auth_source_required";
        }

        if (identity.AuthTimeUtc == default)
        {
            return "auth_time_utc_required";
        }

        if (session == null)
        {
            return "operator_session_required";
        }

        if (string.IsNullOrWhiteSpace(NormalizeOptional(session.OperatorSessionId)))
        {
            return "operator_session_id_required";
        }

        if (!OperatorSurfaceTypes.IsSupported(session.Surface))
        {
            return string.IsNullOrWhiteSpace(session.Surface)
                ? "surface_required"
                : "unsupported_surface";
        }

        if (session.AuthenticatedAtUtc == default)
        {
            return "session_authenticated_at_utc_required";
        }

        if (session.LastSeenAtUtc == default)
        {
            return "session_last_seen_at_utc_required";
        }

        if (session.ExpiresAtUtc.HasValue && session.ExpiresAtUtc.Value <= nowUtc)
        {
            return "session_expired";
        }

        if (requireSupportedMode)
        {
            if (string.IsNullOrWhiteSpace(NormalizeOptional(session.ActiveMode)))
            {
                return "active_mode_required";
            }

            if (!OperatorModeTypes.IsSupported(session.ActiveMode))
            {
                return "invalid_active_mode";
            }
        }

        if (!requireActiveTrackedPerson)
        {
            return null;
        }

        if (session.ActiveTrackedPersonId == Guid.Empty)
        {
            return "session_active_tracked_person_required";
        }

        if (expectedTrackedPersonId.HasValue && session.ActiveTrackedPersonId != expectedTrackedPersonId.Value)
        {
            return "session_active_tracked_person_mismatch";
        }

        var normalizedExpectedScopeItemKey = NormalizeOptional(expectedScopeItemKey);
        var normalizedActiveScopeItemKey = NormalizeOptional(session.ActiveScopeItemKey);
        if (requireActiveScopeItem && string.IsNullOrWhiteSpace(normalizedActiveScopeItemKey))
        {
            return "session_active_scope_item_required";
        }

        if (!string.IsNullOrWhiteSpace(normalizedExpectedScopeItemKey)
            && !string.IsNullOrWhiteSpace(normalizedActiveScopeItemKey)
            && !string.Equals(normalizedExpectedScopeItemKey, normalizedActiveScopeItemKey, StringComparison.Ordinal))
        {
            return "session_scope_item_mismatch";
        }

        if (!requireMatchingUnfinishedStep || session.UnfinishedStep == null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(NormalizeOptional(session.UnfinishedStep.StepKind)))
        {
            return "unfinished_step_kind_required";
        }

        if (session.UnfinishedStep.StartedAtUtc == default)
        {
            return "unfinished_step_started_at_utc_required";
        }

        if (session.UnfinishedStep.BoundTrackedPersonId == Guid.Empty)
        {
            return "unfinished_step_tracked_person_required";
        }

        if (expectedTrackedPersonId.HasValue && session.UnfinishedStep.BoundTrackedPersonId != expectedTrackedPersonId.Value)
        {
            return "unfinished_step_tracked_person_mismatch";
        }

        if (string.IsNullOrWhiteSpace(NormalizeOptional(session.UnfinishedStep.BoundScopeItemKey)))
        {
            return "unfinished_step_scope_item_required";
        }

        if (!string.IsNullOrWhiteSpace(normalizedExpectedScopeItemKey)
            && !string.Equals(
                session.UnfinishedStep.BoundScopeItemKey.Trim(),
                normalizedExpectedScopeItemKey,
                StringComparison.Ordinal))
        {
            return "unfinished_step_scope_item_mismatch";
        }

        return null;
    }

    private static ResolutionActionRequest CloneActionRequest(ResolutionActionRequest request, DateTime nowUtc)
    {
        return new ResolutionActionRequest
        {
            RequestId = request.RequestId,
            TrackedPersonId = request.TrackedPersonId,
            ScopeItemKey = request.ScopeItemKey,
            ActionType = request.ActionType,
            Explanation = request.Explanation,
            ClarificationPayload = request.ClarificationPayload,
            OperatorIdentity = request.OperatorIdentity ?? new OperatorIdentityContext(),
            Session = CloneSession(request.Session, nowUtc),
            SubmittedAtUtc = request.SubmittedAtUtc == default ? nowUtc : request.SubmittedAtUtc
        };
    }

    private static OperatorSessionContext CloneSession(OperatorSessionContext? session, DateTime nowUtc)
    {
        if (session == null)
        {
            return new OperatorSessionContext
            {
                LastSeenAtUtc = nowUtc
            };
        }

        return new OperatorSessionContext
        {
            OperatorSessionId = NormalizeOptional(session.OperatorSessionId) ?? string.Empty,
            Surface = OperatorSurfaceTypes.Normalize(session.Surface),
            AuthenticatedAtUtc = session.AuthenticatedAtUtc,
            LastSeenAtUtc = nowUtc,
            ExpiresAtUtc = session.ExpiresAtUtc,
            ActiveTrackedPersonId = session.ActiveTrackedPersonId,
            ActiveScopeItemKey = NormalizeOptional(session.ActiveScopeItemKey),
            ActiveMode = NormalizeOptional(session.ActiveMode) ?? string.Empty,
            UnfinishedStep = session.UnfinishedStep == null
                ? null
                : new OperatorWorkflowStepContext
                {
                    StepKind = NormalizeOptional(session.UnfinishedStep.StepKind) ?? string.Empty,
                    StepState = NormalizeOptional(session.UnfinishedStep.StepState) ?? string.Empty,
                    StartedAtUtc = session.UnfinishedStep.StartedAtUtc,
                    BoundTrackedPersonId = session.UnfinishedStep.BoundTrackedPersonId,
                    BoundScopeItemKey = NormalizeOptional(session.UnfinishedStep.BoundScopeItemKey) ?? string.Empty
                }
        };
    }

    private static OperatorSessionContext ClearTrackedPersonSelection(OperatorSessionContext session)
    {
        session.ActiveTrackedPersonId = Guid.Empty;
        session.ActiveScopeItemKey = null;
        session.ActiveMode = EnsureQueueMode(session.ActiveMode);
        session.UnfinishedStep = null;
        return session;
    }

    private static string EnsureQueueMode(string? activeMode)
    {
        return OperatorModeTypes.IsSupported(activeMode)
            ? OperatorModeTypes.Normalize(activeMode)
            : OperatorModeTypes.ResolutionQueue;
    }

    private static string BuildOfflineEventScopeItemKey(Guid offlineEventId)
        => offlineEventId == Guid.Empty
            ? string.Empty
            : $"{OfflineEventScopeItemPrefix}{offlineEventId:D}";

    private static Guid? ResolveTrackedPersonId(Guid? requestTrackedPersonId, OperatorSessionContext? session)
    {
        if (requestTrackedPersonId.HasValue && requestTrackedPersonId.Value != Guid.Empty)
        {
            return requestTrackedPersonId.Value;
        }

        return session?.ActiveTrackedPersonId == Guid.Empty
            ? null
            : session?.ActiveTrackedPersonId;
    }

    private static string BuildSessionRequestId(string? sessionId, string sessionEventType, DateTime eventTimeUtc)
    {
        var normalizedSessionId = NormalizeAuditValue(sessionId);
        return $"{sessionEventType}:{normalizedSessionId}:{eventTimeUtc:yyyyMMddHHmmssfff}";
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string NormalizeAuditValue(string? value, string fallback = "unknown")
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static async Task<OperatorTrackedPersonScopeSummary?> LoadTrackedPersonByIdAsync(
        TgAssistantDbContext db,
        Guid trackedPersonId,
        CancellationToken ct)
    {
        return (await LoadTrackedPersonsAsync(db, 200, ct))
            .FirstOrDefault(x => x.TrackedPersonId == trackedPersonId);
    }

    private static List<OperatorWorkspaceSectionState> BuildWorkspaceSections()
    {
        return
        [
            new OperatorWorkspaceSectionState { SectionKey = "summary", Label = "Summary", Status = "ready", Available = true },
            new OperatorWorkspaceSectionState { SectionKey = "dossier", Label = "Dossier", Status = "ready", Available = true },
            new OperatorWorkspaceSectionState { SectionKey = "profile", Label = "Profile", Status = "pending", Available = false },
            new OperatorWorkspaceSectionState { SectionKey = "pair_dynamics", Label = "Pair Dynamics", Status = "pending", Available = false },
            new OperatorWorkspaceSectionState { SectionKey = "timeline", Label = "Timeline", Status = "pending", Available = false },
            new OperatorWorkspaceSectionState { SectionKey = "evidence", Label = "Evidence", Status = "pending", Available = false },
            new OperatorWorkspaceSectionState { SectionKey = "revisions", Label = "Revisions", Status = "pending", Available = false },
            new OperatorWorkspaceSectionState { SectionKey = "resolution", Label = "Resolution", Status = "pending", Available = false }
        ];
    }

    private static async Task<List<WorkspaceDurableSource>> LoadWorkspaceDurableSourcesAsync(
        TgAssistantDbContext db,
        string scopeKey,
        Guid trackedPersonId,
        CancellationToken ct)
    {
        var metadataRows = await db.DurableObjectMetadata
            .AsNoTracking()
            .Where(x => x.ScopeKey == scopeKey
                && x.OwnerPersonId == trackedPersonId
                && x.Status == ActiveStatus
                && WorkspaceSummaryFamilies.Contains(x.ObjectFamily))
            .ToListAsync(ct);
        if (metadataRows.Count == 0)
        {
            return [];
        }

        var metadataIds = metadataRows.Select(x => x.Id).Distinct().ToList();
        var dossiers = await db.DurableDossiers
            .AsNoTracking()
            .Where(x => metadataIds.Contains(x.DurableObjectMetadataId))
            .Select(x => new WorkspaceDurablePayloadRow
            {
                DurableObjectMetadataId = x.DurableObjectMetadataId,
                SummaryJson = x.SummaryJson,
                UpdatedAtUtc = DateTime.SpecifyKind(x.UpdatedAt, DateTimeKind.Utc)
            })
            .ToDictionaryAsync(x => x.DurableObjectMetadataId, x => x, ct);
        var profiles = await db.DurableProfiles
            .AsNoTracking()
            .Where(x => metadataIds.Contains(x.DurableObjectMetadataId))
            .Select(x => new WorkspaceDurablePayloadRow
            {
                DurableObjectMetadataId = x.DurableObjectMetadataId,
                SummaryJson = x.SummaryJson,
                UpdatedAtUtc = DateTime.SpecifyKind(x.UpdatedAt, DateTimeKind.Utc)
            })
            .ToDictionaryAsync(x => x.DurableObjectMetadataId, x => x, ct);
        var pairDynamics = await db.DurablePairDynamics
            .AsNoTracking()
            .Where(x => metadataIds.Contains(x.DurableObjectMetadataId))
            .Select(x => new WorkspaceDurablePayloadRow
            {
                DurableObjectMetadataId = x.DurableObjectMetadataId,
                SummaryJson = x.SummaryJson,
                UpdatedAtUtc = DateTime.SpecifyKind(x.UpdatedAt, DateTimeKind.Utc)
            })
            .ToDictionaryAsync(x => x.DurableObjectMetadataId, x => x, ct);
        var events = await db.DurableEvents
            .AsNoTracking()
            .Where(x => metadataIds.Contains(x.DurableObjectMetadataId))
            .Select(x => new WorkspaceDurablePayloadRow
            {
                DurableObjectMetadataId = x.DurableObjectMetadataId,
                SummaryJson = x.SummaryJson,
                UpdatedAtUtc = DateTime.SpecifyKind(x.UpdatedAt, DateTimeKind.Utc)
            })
            .ToDictionaryAsync(x => x.DurableObjectMetadataId, x => x, ct);
        var episodes = await db.DurableTimelineEpisodes
            .AsNoTracking()
            .Where(x => metadataIds.Contains(x.DurableObjectMetadataId))
            .Select(x => new WorkspaceDurablePayloadRow
            {
                DurableObjectMetadataId = x.DurableObjectMetadataId,
                SummaryJson = x.SummaryJson,
                UpdatedAtUtc = DateTime.SpecifyKind(x.UpdatedAt, DateTimeKind.Utc)
            })
            .ToDictionaryAsync(x => x.DurableObjectMetadataId, x => x, ct);
        var arcs = await db.DurableStoryArcs
            .AsNoTracking()
            .Where(x => metadataIds.Contains(x.DurableObjectMetadataId))
            .Select(x => new WorkspaceDurablePayloadRow
            {
                DurableObjectMetadataId = x.DurableObjectMetadataId,
                SummaryJson = x.SummaryJson,
                UpdatedAtUtc = DateTime.SpecifyKind(x.UpdatedAt, DateTimeKind.Utc)
            })
            .ToDictionaryAsync(x => x.DurableObjectMetadataId, x => x, ct);

        return metadataRows
            .Select(metadata =>
            {
                var payload = ResolvePayloadRow(metadata.ObjectFamily, metadata.Id, dossiers, profiles, pairDynamics, events, episodes, arcs);
                var updatedAt = payload?.UpdatedAtUtc ?? DateTime.SpecifyKind(metadata.UpdatedAt, DateTimeKind.Utc);
                var summaryJson = payload?.SummaryJson;
                return new WorkspaceDurableSource
                {
                    Metadata = metadata,
                    SummaryJson = summaryJson,
                    SummarySnippet = BuildSummarySnippet(summaryJson),
                    UpdatedAtUtc = updatedAt
                };
            })
            .ToList();
    }

    private static WorkspaceDurablePayloadRow? ResolvePayloadRow(
        string family,
        Guid metadataId,
        IReadOnlyDictionary<Guid, WorkspaceDurablePayloadRow> dossiers,
        IReadOnlyDictionary<Guid, WorkspaceDurablePayloadRow> profiles,
        IReadOnlyDictionary<Guid, WorkspaceDurablePayloadRow> pairDynamics,
        IReadOnlyDictionary<Guid, WorkspaceDurablePayloadRow> events,
        IReadOnlyDictionary<Guid, WorkspaceDurablePayloadRow> episodes,
        IReadOnlyDictionary<Guid, WorkspaceDurablePayloadRow> arcs)
    {
        return family switch
        {
            Stage7DurableObjectFamilies.Dossier => dossiers.GetValueOrDefault(metadataId),
            Stage7DurableObjectFamilies.Profile => profiles.GetValueOrDefault(metadataId),
            Stage7DurableObjectFamilies.PairDynamics => pairDynamics.GetValueOrDefault(metadataId),
            Stage7DurableObjectFamilies.Event => events.GetValueOrDefault(metadataId),
            Stage7DurableObjectFamilies.TimelineEpisode => episodes.GetValueOrDefault(metadataId),
            Stage7DurableObjectFamilies.StoryArc => arcs.GetValueOrDefault(metadataId),
            _ => null
        };
    }

    private static string BuildSummarySnippet(string? summaryJson)
    {
        if (!TryParseJsonObject(summaryJson, out var root))
        {
            return "Summary not available.";
        }

        var preferredKeys = new[]
        {
            "tracked_person",
            "operator_root",
            "fact_count",
            "inference_count",
            "hypothesis_count",
            "contradiction_count",
            "closure_state",
            "durable_field_count"
        };
        var parts = new List<string>();
        foreach (var key in preferredKeys)
        {
            if (!root.TryGetProperty(key, out var value))
            {
                continue;
            }

            var renderedValue = value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => null
            };
            var normalizedValue = NormalizeOptional(renderedValue);
            if (string.IsNullOrWhiteSpace(normalizedValue))
            {
                continue;
            }

            parts.Add($"{key.Replace('_', ' ')}={normalizedValue}");
            if (parts.Count == 3)
            {
                break;
            }
        }

        return parts.Count == 0
            ? "Summary available."
            : string.Join(" | ", parts);
    }

    private static List<OperatorWorkspaceDossierFactView> BuildDossierFacts(
        DbDurableDossier dossier,
        DbDurableObjectMetadata? metadata,
        int evidenceLinkCount)
    {
        var facts = new List<OperatorWorkspaceDossierFactView>();
        if (!TryParseJsonObject(dossier.PayloadJson, out var payloadRoot))
        {
            return facts;
        }

        AddDossierFacts(
            payloadRoot,
            "fields",
            defaultApprovalState: DossierFieldApprovalStates.Approved,
            dossier,
            metadata,
            evidenceLinkCount,
            facts);

        AddDossierFacts(
            payloadRoot,
            "proposal_fields",
            defaultApprovalState: DossierFieldApprovalStates.ProposalOnly,
            dossier,
            metadata,
            evidenceLinkCount,
            facts);

        return facts;
    }

    private static void AddDossierFacts(
        JsonElement payloadRoot,
        string propertyName,
        string defaultApprovalState,
        DbDurableDossier dossier,
        DbDurableObjectMetadata? metadata,
        int evidenceLinkCount,
        List<OperatorWorkspaceDossierFactView> target)
    {
        if (!payloadRoot.TryGetProperty(propertyName, out var factsArray)
            || factsArray.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var factElement in factsArray.EnumerateArray())
        {
            if (factElement.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var category = NormalizeOptional(GetString(factElement, "category"))
                ?? NormalizeOptional(GetString(factElement, "canonical_category"))
                ?? "unknown";
            var key = NormalizeOptional(GetString(factElement, "key"))
                ?? NormalizeOptional(GetString(factElement, "canonical_key"))
                ?? "unknown";
            var value = RenderJsonValue(factElement, "value");
            var confidence = Clamp01(GetSingle(factElement, "confidence") ?? metadata?.Confidence ?? 0f);
            var approvalState = NormalizeOptional(GetString(factElement, "approval_state")) ?? defaultApprovalState;
            var evidenceRefCount = GetArrayLength(factElement, "evidence_refs");
            if (evidenceRefCount == 0)
            {
                evidenceRefCount = evidenceLinkCount;
            }

            target.Add(new OperatorWorkspaceDossierFactView
            {
                DurableDossierId = dossier.Id,
                DurableObjectMetadataId = dossier.DurableObjectMetadataId,
                LastModelPassRunId = dossier.LastModelPassRunId ?? metadata?.CreatedByModelPassRunId,
                RevisionNumber = dossier.CurrentRevisionNumber,
                DossierType = NormalizeOptional(dossier.DossierType) ?? "unknown",
                Category = category,
                Key = key,
                Value = value,
                ApprovalState = approvalState,
                Confidence = confidence,
                EvidenceRefCount = evidenceRefCount,
                TruthLayer = NormalizeOptional(metadata?.TruthLayer) ?? "unknown",
                PromotionState = NormalizeOptional(metadata?.PromotionState) ?? "unknown",
                UpdatedAtUtc = DateTime.SpecifyKind(dossier.UpdatedAt, DateTimeKind.Utc)
            });
        }
    }

    private static string RenderJsonValue(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return "n/a";
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => NormalizeOptional(property.GetString()) ?? "n/a",
            JsonValueKind.Number => property.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => "n/a",
            _ => property.GetRawText()
        };
    }

    private static int GetArrayLength(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.Array
                ? property.GetArrayLength()
                : 0;
    }

    private static int CountJsonArray(string? json)
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

    private static string MostCommon(IEnumerable<string?> values)
    {
        return values
            .Select(value => NormalizeOptional(value) ?? "unknown")
            .GroupBy(value => value, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => group.Key)
            .FirstOrDefault() ?? "unknown";
    }

    private static float Clamp01(float value)
        => Math.Clamp(value, 0f, 1f);

    private static string ToFamilyLabel(string family)
    {
        return family switch
        {
            Stage7DurableObjectFamilies.Dossier => "Dossier",
            Stage7DurableObjectFamilies.Profile => "Profile",
            Stage7DurableObjectFamilies.PairDynamics => "Pair Dynamics",
            Stage7DurableObjectFamilies.Event => "Events",
            Stage7DurableObjectFamilies.TimelineEpisode => "Timeline Episodes",
            Stage7DurableObjectFamilies.StoryArc => "Story Arcs",
            _ => family.Replace("_", " ", StringComparison.Ordinal)
        };
    }

    private static bool TryParseJsonObject(string? json, out JsonElement root)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            root = default;
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                root = document.RootElement.Clone();
                return true;
            }
        }
        catch (JsonException)
        {
        }

        root = default;
        return false;
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;
    }

    private static string? GetNestedString(JsonElement element, string objectPropertyName, string nestedPropertyName)
    {
        if (!element.TryGetProperty(objectPropertyName, out var nestedObject)
            || nestedObject.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return GetString(nestedObject, nestedPropertyName);
    }

    private static DateTime? GetDateTime(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.String
            && DateTime.TryParse(property.GetString(), out var parsed))
        {
            return parsed.Kind == DateTimeKind.Utc
                ? parsed
                : parsed.ToUniversalTime();
        }

        return null;
    }

    private static DateTime? GetNestedDateTime(JsonElement element, string objectPropertyName, string nestedPropertyName)
    {
        if (!element.TryGetProperty(objectPropertyName, out var nestedObject)
            || nestedObject.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return GetDateTime(nestedObject, nestedPropertyName);
    }

    private static float? GetSingle(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetSingle(out var value))
        {
            return value;
        }

        if (property.ValueKind == JsonValueKind.String
            && float.TryParse(property.GetString(), out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static int? GetInt32(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var value))
        {
            return value;
        }

        if (property.ValueKind == JsonValueKind.String
            && int.TryParse(property.GetString(), out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static bool? GetBoolean(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.True)
        {
            return true;
        }

        if (property.ValueKind == JsonValueKind.False)
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.String
            && bool.TryParse(property.GetString(), out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private sealed class WorkspaceDurablePayloadRow
    {
        public Guid DurableObjectMetadataId { get; init; }
        public string SummaryJson { get; init; } = "{}";
        public DateTime UpdatedAtUtc { get; init; }
    }

    private sealed class WorkspaceDurableSource
    {
        public DbDurableObjectMetadata Metadata { get; init; } = new();
        public string? SummaryJson { get; init; }
        public string SummarySnippet { get; init; } = string.Empty;
        public DateTime UpdatedAtUtc { get; init; }
    }
}
