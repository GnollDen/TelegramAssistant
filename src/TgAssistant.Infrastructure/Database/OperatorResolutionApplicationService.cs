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
        var pairCard = cards.FirstOrDefault(x => string.Equals(x.Family, Stage7DurableObjectFamilies.PairDynamics, StringComparison.Ordinal));

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
                    Snapshot = BuildBoundedWorkspaceSnapshot(request.OperatorIdentity, session, trackedPerson, pairCard),
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

    public async Task<OperatorPersonWorkspaceProfileQueryResult> QueryPersonWorkspaceProfileAsync(
        OperatorPersonWorkspaceProfileQueryRequest request,
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
            return new OperatorPersonWorkspaceProfileQueryResult
            {
                Accepted = false,
                FailureReason = validationFailure,
                Session = CloneSession(request.Session, nowUtc)
            };
        }

        if (!trackedPersonId.HasValue || trackedPersonId.Value == Guid.Empty)
        {
            return new OperatorPersonWorkspaceProfileQueryResult
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
            return new OperatorPersonWorkspaceProfileQueryResult
            {
                Accepted = false,
                FailureReason = "tracked_person_not_found_or_inactive",
                Session = CloneSession(request.Session, nowUtc)
            };
        }

        await PopulateResolutionSignalsAsync([trackedPerson], ct);

        var profiles = await db.DurableProfiles
            .AsNoTracking()
            .Where(x => x.ScopeKey == trackedPerson.ScopeKey
                && x.PersonId == trackedPerson.TrackedPersonId
                && x.Status == ActiveStatus)
            .OrderByDescending(x => x.UpdatedAt)
            .ToListAsync(ct);

        var metadataIds = profiles
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
                    && x.ObjectFamily == Stage7DurableObjectFamilies.Profile
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

        var signals = profiles
            .SelectMany(profile =>
            {
                metadataById.TryGetValue(profile.DurableObjectMetadataId, out var metadata);
                return BuildProfileSignals(profile, metadata, evidenceCounts.GetValueOrDefault(profile.DurableObjectMetadataId));
            })
            .OrderByDescending(x => x.Confidence)
            .ThenByDescending(x => x.UpdatedAtUtc)
            .ThenBy(x => x.SignalType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.SignalKey, StringComparer.OrdinalIgnoreCase)
            .Take(200)
            .ToList();

        var provenance = profiles
            .Take(24)
            .Select(profile =>
            {
                metadataById.TryGetValue(profile.DurableObjectMetadataId, out var metadata);
                var updatedAtUtc = DateTime.SpecifyKind(profile.UpdatedAt, DateTimeKind.Utc);
                return new OperatorWorkspaceProvenanceItem
                {
                    Family = Stage7DurableObjectFamilies.Profile,
                    ObjectKey = metadata?.ObjectKey ?? profile.Id.ToString("D"),
                    DurableObjectMetadataId = profile.DurableObjectMetadataId,
                    LastModelPassRunId = profile.LastModelPassRunId ?? metadata?.CreatedByModelPassRunId,
                    EvidenceLinkCount = evidenceCounts.GetValueOrDefault(profile.DurableObjectMetadataId),
                    UpdatedAtUtc = updatedAtUtc,
                    Summary = BuildSummarySnippet(profile.SummaryJson)
                };
            })
            .ToList();

        var confidenceValues = metadataById.Values.Select(x => Clamp01(x.Confidence)).ToList();
        var overallTrust = confidenceValues.Count == 0 ? 0f : confidenceValues.Average();
        var inferenceCount = signals.Count(x => string.Equals(x.SignalType, "inference", StringComparison.Ordinal));
        var hypothesisCount = signals.Count(x => string.Equals(x.SignalType, "hypothesis", StringComparison.Ordinal));
        var ambiguityCount = SumProfilePressure(profiles, "ambiguity_count");
        var contradictionCount = SumProfilePressure(profiles, "contradiction_count");

        var session = CloneSession(request.Session, nowUtc);
        session.ActiveTrackedPersonId = trackedPerson.TrackedPersonId;
        session.ActiveScopeItemKey = null;
        session.ActiveMode = OperatorModeTypes.ResolutionQueue;
        session.UnfinishedStep = null;

        return new OperatorPersonWorkspaceProfileQueryResult
        {
            Accepted = true,
            FailureReason = null,
            Session = session,
            Profile = new OperatorPersonWorkspaceProfileSectionView
            {
                GeneratedAtUtc = nowUtc,
                DurableProfileCount = profiles.Count,
                InferenceCount = inferenceCount,
                HypothesisCount = hypothesisCount,
                AmbiguityCount = ambiguityCount,
                ContradictionCount = contradictionCount,
                OverallTrust = overallTrust,
                OverallUncertainty = Clamp01(1f - overallTrust),
                TotalEvidenceLinkCount = evidenceCounts.Values.Sum(),
                Signals = signals,
                Provenance = provenance
            }
        };
    }

    public async Task<OperatorPersonWorkspacePairDynamicsQueryResult> QueryPersonWorkspacePairDynamicsAsync(
        OperatorPersonWorkspacePairDynamicsQueryRequest request,
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
            return new OperatorPersonWorkspacePairDynamicsQueryResult
            {
                Accepted = false,
                FailureReason = validationFailure,
                Session = CloneSession(request.Session, nowUtc)
            };
        }

        if (!trackedPersonId.HasValue || trackedPersonId.Value == Guid.Empty)
        {
            return new OperatorPersonWorkspacePairDynamicsQueryResult
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
            return new OperatorPersonWorkspacePairDynamicsQueryResult
            {
                Accepted = false,
                FailureReason = "tracked_person_not_found_or_inactive",
                Session = CloneSession(request.Session, nowUtc)
            };
        }

        await PopulateResolutionSignalsAsync([trackedPerson], ct);

        var pairs = await db.DurablePairDynamics
            .AsNoTracking()
            .Where(x => x.ScopeKey == trackedPerson.ScopeKey
                && x.Status == ActiveStatus
                && (x.RightPersonId == trackedPerson.TrackedPersonId || x.LeftPersonId == trackedPerson.TrackedPersonId))
            .OrderByDescending(x => x.UpdatedAt)
            .ToListAsync(ct);

        var metadataIds = pairs
            .Select(x => x.DurableObjectMetadataId)
            .Distinct()
            .ToList();
        var metadataById = metadataIds.Count == 0
            ? new Dictionary<Guid, DbDurableObjectMetadata>()
            : await db.DurableObjectMetadata
                .AsNoTracking()
                .Where(x => metadataIds.Contains(x.Id)
                    && x.ScopeKey == trackedPerson.ScopeKey
                    && x.ObjectFamily == Stage7DurableObjectFamilies.PairDynamics
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

        var signals = pairs
            .SelectMany(pair =>
            {
                metadataById.TryGetValue(pair.DurableObjectMetadataId, out var metadata);
                return BuildPairDynamicsSignals(pair, metadata, evidenceCounts.GetValueOrDefault(pair.DurableObjectMetadataId));
            })
            .OrderByDescending(x => x.Confidence)
            .ThenByDescending(x => x.UpdatedAtUtc)
            .ThenBy(x => x.SignalType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.SignalKey, StringComparer.OrdinalIgnoreCase)
            .Take(240)
            .ToList();

        var provenance = pairs
            .Take(24)
            .Select(pair =>
            {
                metadataById.TryGetValue(pair.DurableObjectMetadataId, out var metadata);
                var updatedAtUtc = DateTime.SpecifyKind(pair.UpdatedAt, DateTimeKind.Utc);
                return new OperatorWorkspaceProvenanceItem
                {
                    Family = Stage7DurableObjectFamilies.PairDynamics,
                    ObjectKey = metadata?.ObjectKey ?? pair.Id.ToString("D"),
                    DurableObjectMetadataId = pair.DurableObjectMetadataId,
                    LastModelPassRunId = pair.LastModelPassRunId ?? metadata?.CreatedByModelPassRunId,
                    EvidenceLinkCount = evidenceCounts.GetValueOrDefault(pair.DurableObjectMetadataId),
                    UpdatedAtUtc = updatedAtUtc,
                    Summary = BuildSummarySnippet(pair.SummaryJson)
                };
            })
            .ToList();

        var confidenceValues = metadataById.Values.Select(x => Clamp01(x.Confidence)).ToList();
        var overallTrust = confidenceValues.Count == 0 ? 0f : confidenceValues.Average();
        var dimensionCount = signals.Count(x => string.Equals(x.SignalType, "dimension", StringComparison.Ordinal));
        var inferenceCount = signals.Count(x => string.Equals(x.SignalType, "inference", StringComparison.Ordinal));
        var hypothesisCount = signals.Count(x => string.Equals(x.SignalType, "hypothesis", StringComparison.Ordinal));
        var conflictCount = signals.Count(x => string.Equals(x.SignalType, "conflict", StringComparison.Ordinal));
        var ambiguityCount = SumPairDynamicsPressure(pairs, "ambiguity_count");
        var contradictionCount = SumPairDynamicsPressure(pairs, "contradiction_count");
        var directionOfChange = ResolvePairDynamicsDirection(signals, ambiguityCount, contradictionCount);

        var session = CloneSession(request.Session, nowUtc);
        session.ActiveTrackedPersonId = trackedPerson.TrackedPersonId;
        session.ActiveScopeItemKey = null;
        session.ActiveMode = OperatorModeTypes.ResolutionQueue;
        session.UnfinishedStep = null;

        return new OperatorPersonWorkspacePairDynamicsQueryResult
        {
            Accepted = true,
            FailureReason = null,
            Session = session,
            PairDynamics = new OperatorPersonWorkspacePairDynamicsSectionView
            {
                GeneratedAtUtc = nowUtc,
                DurablePairCount = pairs.Count,
                DimensionCount = dimensionCount,
                InferenceCount = inferenceCount,
                HypothesisCount = hypothesisCount,
                ConflictCount = conflictCount,
                AmbiguityCount = ambiguityCount,
                ContradictionCount = contradictionCount,
                OverallTrust = overallTrust,
                OverallUncertainty = Clamp01(1f - overallTrust),
                DirectionOfChange = directionOfChange,
                TotalEvidenceLinkCount = evidenceCounts.Values.Sum(),
                Signals = signals,
                Provenance = provenance
            }
        };
    }

    public async Task<OperatorPersonWorkspaceTimelineQueryResult> QueryPersonWorkspaceTimelineAsync(
        OperatorPersonWorkspaceTimelineQueryRequest request,
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
            return new OperatorPersonWorkspaceTimelineQueryResult
            {
                Accepted = false,
                FailureReason = validationFailure,
                Session = CloneSession(request.Session, nowUtc)
            };
        }

        if (!trackedPersonId.HasValue || trackedPersonId.Value == Guid.Empty)
        {
            return new OperatorPersonWorkspaceTimelineQueryResult
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
            return new OperatorPersonWorkspaceTimelineQueryResult
            {
                Accepted = false,
                FailureReason = "tracked_person_not_found_or_inactive",
                Session = CloneSession(request.Session, nowUtc)
            };
        }

        await PopulateResolutionSignalsAsync([trackedPerson], ct);

        var episodes = await db.DurableTimelineEpisodes
            .AsNoTracking()
            .Where(x => x.ScopeKey == trackedPerson.ScopeKey
                && x.Status == ActiveStatus
                && (x.PersonId == trackedPerson.TrackedPersonId || x.RelatedPersonId == trackedPerson.TrackedPersonId))
            .OrderByDescending(x => x.UpdatedAt)
            .ToListAsync(ct);

        var arcs = await db.DurableStoryArcs
            .AsNoTracking()
            .Where(x => x.ScopeKey == trackedPerson.ScopeKey
                && x.Status == ActiveStatus
                && (x.PersonId == trackedPerson.TrackedPersonId || x.RelatedPersonId == trackedPerson.TrackedPersonId))
            .OrderByDescending(x => x.UpdatedAt)
            .ToListAsync(ct);

        var metadataIds = episodes
            .Select(x => x.DurableObjectMetadataId)
            .Concat(arcs.Select(x => x.DurableObjectMetadataId))
            .Distinct()
            .ToList();
        var metadataById = metadataIds.Count == 0
            ? new Dictionary<Guid, DbDurableObjectMetadata>()
            : await db.DurableObjectMetadata
                .AsNoTracking()
                .Where(x => metadataIds.Contains(x.Id)
                    && x.ScopeKey == trackedPerson.ScopeKey
                    && (x.ObjectFamily == Stage7DurableObjectFamilies.TimelineEpisode || x.ObjectFamily == Stage7DurableObjectFamilies.StoryArc)
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

        var shifts = BuildTimelineShifts(episodes, arcs, metadataById, evidenceCounts)
            .OrderByDescending(x => x.ShiftEndedAtUtc ?? x.ShiftStartedAtUtc ?? x.UpdatedAtUtc)
            .ThenByDescending(x => x.Confidence)
            .ThenBy(x => x.ShiftType, StringComparer.OrdinalIgnoreCase)
            .Take(240)
            .ToList();

        var provenance = shifts
            .Take(24)
            .Select(shift => new OperatorWorkspaceProvenanceItem
            {
                Family = shift.Family,
                ObjectKey = metadataById.TryGetValue(shift.DurableObjectMetadataId, out var metadata)
                    ? metadata.ObjectKey
                    : shift.DurableObjectId.ToString("D"),
                DurableObjectMetadataId = shift.DurableObjectMetadataId,
                LastModelPassRunId = shift.LastModelPassRunId,
                EvidenceLinkCount = shift.EvidenceRefCount,
                UpdatedAtUtc = shift.UpdatedAtUtc,
                Summary = shift.Summary
            })
            .ToList();

        var confidenceValues = metadataById.Values.Select(x => Clamp01(x.Confidence)).ToList();
        var overallTrust = confidenceValues.Count == 0 ? 0f : confidenceValues.Average();
        var openArcCount = arcs.Count(x => string.Equals(NormalizeOptional(x.ClosureState), Stage7ClosureStates.Open, StringComparison.Ordinal));
        var contradictionCount = metadataById.Values.Sum(x => CountJsonArray(x.ContradictionMarkersJson));

        var session = CloneSession(request.Session, nowUtc);
        session.ActiveTrackedPersonId = trackedPerson.TrackedPersonId;
        session.ActiveScopeItemKey = null;
        session.ActiveMode = OperatorModeTypes.ResolutionQueue;
        session.UnfinishedStep = null;

        return new OperatorPersonWorkspaceTimelineQueryResult
        {
            Accepted = true,
            FailureReason = null,
            Session = session,
            Timeline = new OperatorPersonWorkspaceTimelineSectionView
            {
                GeneratedAtUtc = nowUtc,
                DurableEpisodeCount = episodes.Count,
                DurableStoryArcCount = arcs.Count,
                KeyShiftCount = shifts.Count,
                OpenArcCount = openArcCount,
                ContradictionCount = contradictionCount,
                OverallTrust = overallTrust,
                OverallUncertainty = Clamp01(1f - overallTrust),
                TotalEvidenceLinkCount = evidenceCounts.Values.Sum(),
                Shifts = shifts,
                Provenance = provenance
            }
        };
    }

    public async Task<OperatorPersonWorkspaceEvidenceQueryResult> QueryPersonWorkspaceEvidenceAsync(
        OperatorPersonWorkspaceEvidenceQueryRequest request,
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
            return new OperatorPersonWorkspaceEvidenceQueryResult
            {
                Accepted = false,
                FailureReason = validationFailure,
                Session = CloneSession(request.Session, nowUtc)
            };
        }

        if (!trackedPersonId.HasValue || trackedPersonId.Value == Guid.Empty)
        {
            return new OperatorPersonWorkspaceEvidenceQueryResult
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
            return new OperatorPersonWorkspaceEvidenceQueryResult
            {
                Accepted = false,
                FailureReason = "tracked_person_not_found_or_inactive",
                Session = CloneSession(request.Session, nowUtc)
            };
        }

        await PopulateResolutionSignalsAsync([trackedPerson], ct);

        var metadataRows = await db.DurableObjectMetadata
            .AsNoTracking()
            .Where(x => x.ScopeKey == trackedPerson.ScopeKey
                && x.OwnerPersonId == trackedPerson.TrackedPersonId
                && x.Status == ActiveStatus
                && WorkspaceSummaryFamilies.Contains(x.ObjectFamily))
            .OrderByDescending(x => x.UpdatedAt)
            .Take(300)
            .ToListAsync(ct);

        var metadataById = metadataRows.ToDictionary(x => x.Id, x => x);
        var metadataIds = metadataById.Keys.ToList();
        var links = metadataIds.Count == 0
            ? []
            : await db.DurableObjectEvidenceLinks
                .AsNoTracking()
                .Where(x => x.ScopeKey == trackedPerson.ScopeKey && metadataIds.Contains(x.DurableObjectMetadataId))
                .OrderByDescending(x => x.CreatedAt)
                .Take(600)
                .ToListAsync(ct);

        var evidenceIds = links
            .Select(x => x.EvidenceItemId)
            .Distinct()
            .ToList();
        var evidenceById = evidenceIds.Count == 0
            ? new Dictionary<Guid, DbEvidenceItem>()
            : await db.EvidenceItems
                .AsNoTracking()
                .Where(x => x.ScopeKey == trackedPerson.ScopeKey
                    && evidenceIds.Contains(x.Id)
                    && x.Status == ActiveStatus)
                .ToDictionaryAsync(x => x.Id, x => x, ct);

        var sourceIds = evidenceById.Values
            .Select(x => x.SourceObjectId)
            .Distinct()
            .ToList();
        var sourceById = sourceIds.Count == 0
            ? new Dictionary<Guid, DbSourceObject>()
            : await db.SourceObjects
                .AsNoTracking()
                .Where(x => x.ScopeKey == trackedPerson.ScopeKey
                    && sourceIds.Contains(x.Id)
                    && x.Status == ActiveStatus)
                .ToDictionaryAsync(x => x.Id, x => x, ct);

        var linksByMetadataId = links
            .GroupBy(x => x.DurableObjectMetadataId)
            .ToDictionary(x => x.Key, x => x.Count());

        var linkViews = links
            .Select(link =>
            {
                metadataById.TryGetValue(link.DurableObjectMetadataId, out var metadata);
                evidenceById.TryGetValue(link.EvidenceItemId, out var evidence);
                sourceById.TryGetValue(evidence?.SourceObjectId ?? Guid.Empty, out var source);
                if (metadata == null || evidence == null || source == null)
                {
                    return null;
                }

                return new OperatorWorkspaceEvidenceLinkView
                {
                    DurableObjectMetadataId = metadata.Id,
                    EvidenceItemId = evidence.Id,
                    SourceObjectId = source.Id,
                    LastModelPassRunId = metadata.CreatedByModelPassRunId,
                    DurableObjectFamily = metadata.ObjectFamily,
                    DurableObjectKey = metadata.ObjectKey,
                    DurableTruthLayer = NormalizeOptional(metadata.TruthLayer) ?? "unknown",
                    DurablePromotionState = NormalizeOptional(metadata.PromotionState) ?? "unknown",
                    DurableConfidence = Clamp01(metadata.Confidence),
                    EvidenceKind = NormalizeOptional(evidence.EvidenceKind) ?? "unknown",
                    EvidenceTruthLayer = NormalizeOptional(evidence.TruthLayer) ?? "unknown",
                    EvidenceConfidence = Clamp01(evidence.Confidence),
                    LinkRole = NormalizeOptional(link.LinkRole) ?? "linked",
                    EvidenceSummary = NormalizeOptional(evidence.SummaryText),
                    SourceKind = NormalizeOptional(source.SourceKind) ?? "unknown",
                    SourceRef = NormalizeOptional(source.SourceRef) ?? source.Id.ToString("D"),
                    SourceDisplayLabel = NormalizeOptional(source.DisplayLabel) ?? NormalizeOptional(source.SourceRef) ?? source.Id.ToString("D"),
                    ObservedAtUtc = NormalizeUtc(evidence.ObservedAt),
                    SourceOccurredAtUtc = NormalizeUtc(source.OccurredAt),
                    LinkedAtUtc = DateTime.SpecifyKind(link.CreatedAt, DateTimeKind.Utc)
                };
            })
            .Where(x => x != null)
            .Cast<OperatorWorkspaceEvidenceLinkView>()
            .OrderByDescending(x => x.ObservedAtUtc ?? x.SourceOccurredAtUtc ?? x.LinkedAtUtc)
            .ThenByDescending(x => x.EvidenceConfidence)
            .ThenByDescending(x => x.DurableConfidence)
            .Take(300)
            .ToList();

        var provenance = linkViews
            .Select(link => new OperatorWorkspaceProvenanceItem
            {
                Family = link.DurableObjectFamily,
                ObjectKey = link.DurableObjectKey,
                DurableObjectMetadataId = link.DurableObjectMetadataId,
                LastModelPassRunId = link.LastModelPassRunId,
                EvidenceLinkCount = linksByMetadataId.GetValueOrDefault(link.DurableObjectMetadataId),
                UpdatedAtUtc = link.LinkedAtUtc,
                Summary = link.EvidenceSummary ?? "Evidence-linked durable object."
            })
            .DistinctBy(x => x.DurableObjectMetadataId)
            .Take(48)
            .ToList();

        var averageTrust = linkViews.Count == 0
            ? 0f
            : Clamp01(linkViews.Average(x => (x.EvidenceConfidence + x.DurableConfidence) / 2f));

        var session = CloneSession(request.Session, nowUtc);
        session.ActiveTrackedPersonId = trackedPerson.TrackedPersonId;
        session.ActiveScopeItemKey = null;
        session.ActiveMode = OperatorModeTypes.ResolutionQueue;
        session.UnfinishedStep = null;

        return new OperatorPersonWorkspaceEvidenceQueryResult
        {
            Accepted = true,
            FailureReason = null,
            Session = session,
            Evidence = new OperatorPersonWorkspaceEvidenceSectionView
            {
                GeneratedAtUtc = nowUtc,
                DurableObjectCount = metadataRows.Count,
                EvidenceItemCount = linkViews.Select(x => x.EvidenceItemId).Distinct().Count(),
                SourceObjectCount = linkViews.Select(x => x.SourceObjectId).Distinct().Count(),
                TotalEvidenceLinkCount = links.Count,
                OverallTrust = averageTrust,
                OverallUncertainty = Clamp01(1f - averageTrust),
                Links = linkViews,
                Provenance = provenance
            }
        };
    }

    public async Task<OperatorPersonWorkspaceRevisionsQueryResult> QueryPersonWorkspaceRevisionsAsync(
        OperatorPersonWorkspaceRevisionsQueryRequest request,
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
            return new OperatorPersonWorkspaceRevisionsQueryResult
            {
                Accepted = false,
                FailureReason = validationFailure,
                Session = CloneSession(request.Session, nowUtc)
            };
        }

        if (!trackedPersonId.HasValue || trackedPersonId.Value == Guid.Empty)
        {
            return new OperatorPersonWorkspaceRevisionsQueryResult
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
            return new OperatorPersonWorkspaceRevisionsQueryResult
            {
                Accepted = false,
                FailureReason = "tracked_person_not_found_or_inactive",
                Session = CloneSession(request.Session, nowUtc)
            };
        }

        await PopulateResolutionSignalsAsync([trackedPerson], ct);

        var metadataRows = await db.DurableObjectMetadata
            .AsNoTracking()
            .Where(x => x.ScopeKey == trackedPerson.ScopeKey
                && x.OwnerPersonId == trackedPerson.TrackedPersonId
                && x.Status == ActiveStatus
                && WorkspaceSummaryFamilies.Contains(x.ObjectFamily))
            .OrderByDescending(x => x.UpdatedAt)
            .Take(300)
            .ToListAsync(ct);
        var metadataById = metadataRows.ToDictionary(x => x.Id, x => x);
        var metadataIds = metadataById.Keys.ToList();

        var evidenceCounts = metadataIds.Count == 0
            ? new Dictionary<Guid, int>()
            : await db.DurableObjectEvidenceLinks
                .AsNoTracking()
                .Where(x => metadataIds.Contains(x.DurableObjectMetadataId))
                .GroupBy(x => x.DurableObjectMetadataId)
                .Select(x => new { x.Key, Count = x.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        var dossierById = await db.DurableDossiers
            .AsNoTracking()
            .Where(x => x.ScopeKey == trackedPerson.ScopeKey
                && x.PersonId == trackedPerson.TrackedPersonId
                && x.Status == ActiveStatus
                && metadataIds.Contains(x.DurableObjectMetadataId))
            .ToDictionaryAsync(x => x.Id, x => x, ct);
        var profileById = await db.DurableProfiles
            .AsNoTracking()
            .Where(x => x.ScopeKey == trackedPerson.ScopeKey
                && x.PersonId == trackedPerson.TrackedPersonId
                && x.Status == ActiveStatus
                && metadataIds.Contains(x.DurableObjectMetadataId))
            .ToDictionaryAsync(x => x.Id, x => x, ct);
        var pairById = await db.DurablePairDynamics
            .AsNoTracking()
            .Where(x => x.ScopeKey == trackedPerson.ScopeKey
                && x.Status == ActiveStatus
                && metadataIds.Contains(x.DurableObjectMetadataId)
                && (x.LeftPersonId == trackedPerson.TrackedPersonId || x.RightPersonId == trackedPerson.TrackedPersonId))
            .ToDictionaryAsync(x => x.Id, x => x, ct);
        var eventById = await db.DurableEvents
            .AsNoTracking()
            .Where(x => x.ScopeKey == trackedPerson.ScopeKey
                && x.Status == ActiveStatus
                && metadataIds.Contains(x.DurableObjectMetadataId)
                && (x.PersonId == trackedPerson.TrackedPersonId || x.RelatedPersonId == trackedPerson.TrackedPersonId))
            .ToDictionaryAsync(x => x.Id, x => x, ct);
        var episodeById = await db.DurableTimelineEpisodes
            .AsNoTracking()
            .Where(x => x.ScopeKey == trackedPerson.ScopeKey
                && x.Status == ActiveStatus
                && metadataIds.Contains(x.DurableObjectMetadataId)
                && (x.PersonId == trackedPerson.TrackedPersonId || x.RelatedPersonId == trackedPerson.TrackedPersonId))
            .ToDictionaryAsync(x => x.Id, x => x, ct);
        var arcById = await db.DurableStoryArcs
            .AsNoTracking()
            .Where(x => x.ScopeKey == trackedPerson.ScopeKey
                && x.Status == ActiveStatus
                && metadataIds.Contains(x.DurableObjectMetadataId)
                && (x.PersonId == trackedPerson.TrackedPersonId || x.RelatedPersonId == trackedPerson.TrackedPersonId))
            .ToDictionaryAsync(x => x.Id, x => x, ct);

        var revisions = new List<WorkspaceRevisionSource>(capacity: 512);
        var dossierIds = dossierById.Keys.ToList();
        if (dossierIds.Count != 0)
        {
            var rows = await db.DurableDossierRevisions
                .AsNoTracking()
                .Where(x => dossierIds.Contains(x.DurableDossierId))
                .OrderByDescending(x => x.CreatedAt)
                .Take(240)
                .ToListAsync(ct);
            revisions.AddRange(rows.Select(x => new WorkspaceRevisionSource
            {
                Family = Stage7DurableObjectFamilies.Dossier,
                DurableObjectId = x.DurableDossierId,
                RevisionId = x.Id,
                RevisionNumber = x.RevisionNumber,
                RevisionHash = x.RevisionHash,
                ModelPassRunId = x.ModelPassRunId,
                Confidence = x.Confidence,
                Freshness = x.Freshness,
                Stability = x.Stability,
                ContradictionMarkersJson = x.ContradictionMarkersJson,
                SummaryJson = x.SummaryJson,
                CreatedAtUtc = DateTime.SpecifyKind(x.CreatedAt, DateTimeKind.Utc),
                DurableObjectMetadataId = dossierById[x.DurableDossierId].DurableObjectMetadataId
            }));
        }

        var profileIds = profileById.Keys.ToList();
        if (profileIds.Count != 0)
        {
            var rows = await db.DurableProfileRevisions
                .AsNoTracking()
                .Where(x => profileIds.Contains(x.DurableProfileId))
                .OrderByDescending(x => x.CreatedAt)
                .Take(240)
                .ToListAsync(ct);
            revisions.AddRange(rows.Select(x => new WorkspaceRevisionSource
            {
                Family = Stage7DurableObjectFamilies.Profile,
                DurableObjectId = x.DurableProfileId,
                RevisionId = x.Id,
                RevisionNumber = x.RevisionNumber,
                RevisionHash = x.RevisionHash,
                ModelPassRunId = x.ModelPassRunId,
                Confidence = x.Confidence,
                Freshness = x.Freshness,
                Stability = x.Stability,
                ContradictionMarkersJson = x.ContradictionMarkersJson,
                SummaryJson = x.SummaryJson,
                CreatedAtUtc = DateTime.SpecifyKind(x.CreatedAt, DateTimeKind.Utc),
                DurableObjectMetadataId = profileById[x.DurableProfileId].DurableObjectMetadataId
            }));
        }

        var pairIds = pairById.Keys.ToList();
        if (pairIds.Count != 0)
        {
            var rows = await db.DurablePairDynamicsRevisions
                .AsNoTracking()
                .Where(x => pairIds.Contains(x.DurablePairDynamicsId))
                .OrderByDescending(x => x.CreatedAt)
                .Take(240)
                .ToListAsync(ct);
            revisions.AddRange(rows.Select(x => new WorkspaceRevisionSource
            {
                Family = Stage7DurableObjectFamilies.PairDynamics,
                DurableObjectId = x.DurablePairDynamicsId,
                RevisionId = x.Id,
                RevisionNumber = x.RevisionNumber,
                RevisionHash = x.RevisionHash,
                ModelPassRunId = x.ModelPassRunId,
                Confidence = x.Confidence,
                Freshness = x.Freshness,
                Stability = x.Stability,
                ContradictionMarkersJson = x.ContradictionMarkersJson,
                SummaryJson = x.SummaryJson,
                CreatedAtUtc = DateTime.SpecifyKind(x.CreatedAt, DateTimeKind.Utc),
                DurableObjectMetadataId = pairById[x.DurablePairDynamicsId].DurableObjectMetadataId
            }));
        }

        var eventIds = eventById.Keys.ToList();
        if (eventIds.Count != 0)
        {
            var rows = await db.DurableEventRevisions
                .AsNoTracking()
                .Where(x => eventIds.Contains(x.DurableEventId))
                .OrderByDescending(x => x.CreatedAt)
                .Take(240)
                .ToListAsync(ct);
            revisions.AddRange(rows.Select(x => new WorkspaceRevisionSource
            {
                Family = Stage7DurableObjectFamilies.Event,
                DurableObjectId = x.DurableEventId,
                RevisionId = x.Id,
                RevisionNumber = x.RevisionNumber,
                RevisionHash = x.RevisionHash,
                ModelPassRunId = x.ModelPassRunId,
                Confidence = x.Confidence,
                Freshness = x.Freshness,
                Stability = x.Stability,
                ContradictionMarkersJson = x.ContradictionMarkersJson,
                SummaryJson = x.SummaryJson,
                CreatedAtUtc = DateTime.SpecifyKind(x.CreatedAt, DateTimeKind.Utc),
                DurableObjectMetadataId = eventById[x.DurableEventId].DurableObjectMetadataId
            }));
        }

        var episodeIds = episodeById.Keys.ToList();
        if (episodeIds.Count != 0)
        {
            var rows = await db.DurableTimelineEpisodeRevisions
                .AsNoTracking()
                .Where(x => episodeIds.Contains(x.DurableTimelineEpisodeId))
                .OrderByDescending(x => x.CreatedAt)
                .Take(240)
                .ToListAsync(ct);
            revisions.AddRange(rows.Select(x => new WorkspaceRevisionSource
            {
                Family = Stage7DurableObjectFamilies.TimelineEpisode,
                DurableObjectId = x.DurableTimelineEpisodeId,
                RevisionId = x.Id,
                RevisionNumber = x.RevisionNumber,
                RevisionHash = x.RevisionHash,
                ModelPassRunId = x.ModelPassRunId,
                Confidence = x.Confidence,
                Freshness = x.Freshness,
                Stability = x.Stability,
                ContradictionMarkersJson = x.ContradictionMarkersJson,
                SummaryJson = x.SummaryJson,
                CreatedAtUtc = DateTime.SpecifyKind(x.CreatedAt, DateTimeKind.Utc),
                DurableObjectMetadataId = episodeById[x.DurableTimelineEpisodeId].DurableObjectMetadataId
            }));
        }

        var arcIds = arcById.Keys.ToList();
        if (arcIds.Count != 0)
        {
            var rows = await db.DurableStoryArcRevisions
                .AsNoTracking()
                .Where(x => arcIds.Contains(x.DurableStoryArcId))
                .OrderByDescending(x => x.CreatedAt)
                .Take(240)
                .ToListAsync(ct);
            revisions.AddRange(rows.Select(x => new WorkspaceRevisionSource
            {
                Family = Stage7DurableObjectFamilies.StoryArc,
                DurableObjectId = x.DurableStoryArcId,
                RevisionId = x.Id,
                RevisionNumber = x.RevisionNumber,
                RevisionHash = x.RevisionHash,
                ModelPassRunId = x.ModelPassRunId,
                Confidence = x.Confidence,
                Freshness = x.Freshness,
                Stability = x.Stability,
                ContradictionMarkersJson = x.ContradictionMarkersJson,
                SummaryJson = x.SummaryJson,
                CreatedAtUtc = DateTime.SpecifyKind(x.CreatedAt, DateTimeKind.Utc),
                DurableObjectMetadataId = arcById[x.DurableStoryArcId].DurableObjectMetadataId
            }));
        }

        revisions = revisions
            .Where(x => metadataById.ContainsKey(x.DurableObjectMetadataId))
            .OrderByDescending(x => x.CreatedAtUtc)
            .ThenByDescending(x => x.RevisionNumber)
            .ThenByDescending(x => x.Confidence)
            .Take(360)
            .ToList();

        var modelPassIds = revisions
            .Select(x => x.ModelPassRunId)
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .Distinct()
            .ToList();
        var modelPassById = modelPassIds.Count == 0
            ? new Dictionary<Guid, WorkspaceModelPassTrigger>()
            : await db.ModelPassRuns
                .AsNoTracking()
                .Where(x => modelPassIds.Contains(x.Id))
                .ToDictionaryAsync(
                    x => x.Id,
                    x => new WorkspaceModelPassTrigger
                    {
                        TriggerKind = NormalizeOptional(x.TriggerKind) ?? "none",
                        TriggerRef = NormalizeOptional(x.TriggerRef) ?? "n/a",
                        PassFamily = NormalizeOptional(x.PassFamily) ?? "unknown",
                        RunKind = NormalizeOptional(x.RunKind) ?? "unknown",
                        ResultStatus = NormalizeOptional(x.ResultStatus) ?? "unknown",
                        TargetType = NormalizeOptional(x.TargetType) ?? "unknown",
                        TargetRef = NormalizeOptional(x.TargetRef) ?? "n/a"
                    },
                    ct);

        var revisionViews = revisions
            .Select(revision =>
            {
                metadataById.TryGetValue(revision.DurableObjectMetadataId, out var metadata);
                modelPassById.TryGetValue(revision.ModelPassRunId ?? Guid.Empty, out var trigger);
                return new OperatorWorkspaceRevisionView
                {
                    Family = revision.Family,
                    DurableObjectId = revision.DurableObjectId,
                    DurableObjectMetadataId = revision.DurableObjectMetadataId,
                    ObjectKey = metadata?.ObjectKey ?? revision.DurableObjectId.ToString("D"),
                    ModelPassRunId = revision.ModelPassRunId,
                    RevisionNumber = revision.RevisionNumber,
                    RevisionHash = revision.RevisionHash,
                    Confidence = Clamp01(revision.Confidence),
                    Freshness = Clamp01(revision.Freshness),
                    Stability = Clamp01(revision.Stability),
                    ContradictionCount = CountJsonArray(revision.ContradictionMarkersJson),
                    EvidenceRefCount = evidenceCounts.GetValueOrDefault(revision.DurableObjectMetadataId),
                    TruthLayer = NormalizeOptional(metadata?.TruthLayer) ?? "unknown",
                    PromotionState = NormalizeOptional(metadata?.PromotionState) ?? "unknown",
                    TriggerKind = trigger?.TriggerKind ?? "none",
                    TriggerRef = trigger?.TriggerRef ?? "n/a",
                    PassFamily = trigger?.PassFamily ?? "unknown",
                    RunKind = trigger?.RunKind ?? "unknown",
                    ResultStatus = trigger?.ResultStatus ?? "unknown",
                    TargetType = trigger?.TargetType ?? "unknown",
                    TargetRef = trigger?.TargetRef ?? "n/a",
                    Summary = BuildSummarySnippet(revision.SummaryJson),
                    CreatedAtUtc = revision.CreatedAtUtc
                };
            })
            .ToList();

        var averageTrust = revisionViews.Count == 0
            ? 0f
            : Clamp01(revisionViews.Average(x => x.Confidence));

        var provenance = revisionViews
            .Select(item => new OperatorWorkspaceProvenanceItem
            {
                Family = item.Family,
                ObjectKey = item.ObjectKey,
                DurableObjectMetadataId = item.DurableObjectMetadataId,
                LastModelPassRunId = item.ModelPassRunId,
                EvidenceLinkCount = item.EvidenceRefCount,
                UpdatedAtUtc = item.CreatedAtUtc,
                Summary = item.Summary
            })
            .DistinctBy(x => x.DurableObjectMetadataId)
            .Take(48)
            .ToList();

        var session = CloneSession(request.Session, nowUtc);
        session.ActiveTrackedPersonId = trackedPerson.TrackedPersonId;
        session.ActiveScopeItemKey = null;
        session.ActiveMode = OperatorModeTypes.ResolutionQueue;
        session.UnfinishedStep = null;

        return new OperatorPersonWorkspaceRevisionsQueryResult
        {
            Accepted = true,
            FailureReason = null,
            Session = session,
            Revisions = new OperatorPersonWorkspaceRevisionsSectionView
            {
                GeneratedAtUtc = nowUtc,
                DurableObjectCount = metadataRows.Count,
                RevisionCount = revisionViews.Count,
                TriggeredRevisionCount = revisionViews.Count(x => !string.Equals(x.TriggerKind, "none", StringComparison.Ordinal)),
                ContradictionRevisionCount = revisionViews.Count(x => x.ContradictionCount > 0),
                OverallTrust = averageTrust,
                OverallUncertainty = Clamp01(1f - averageTrust),
                Revisions = revisionViews,
                Provenance = provenance
            }
        };
    }

    public async Task<OperatorPersonWorkspaceResolutionQueryResult> QueryPersonWorkspaceResolutionAsync(
        OperatorPersonWorkspaceResolutionQueryRequest request,
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
            return new OperatorPersonWorkspaceResolutionQueryResult
            {
                Accepted = false,
                FailureReason = validationFailure,
                Session = CloneSession(request.Session, nowUtc)
            };
        }

        if (!trackedPersonId.HasValue || trackedPersonId.Value == Guid.Empty)
        {
            return new OperatorPersonWorkspaceResolutionQueryResult
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
            return new OperatorPersonWorkspaceResolutionQueryResult
            {
                Accepted = false,
                FailureReason = "tracked_person_not_found_or_inactive",
                Session = CloneSession(request.Session, nowUtc)
            };
        }

        await PopulateResolutionSignalsAsync([trackedPerson], ct);

        var queue = await _resolutionReadService.GetQueueAsync(
            new ResolutionQueueRequest
            {
                TrackedPersonId = trackedPerson.TrackedPersonId,
                SortBy = ResolutionQueueSortFields.Priority,
                SortDirection = ResolutionSortDirections.Desc,
                Limit = 100
            },
            ct);

        if (!queue.ScopeBound)
        {
            return new OperatorPersonWorkspaceResolutionQueryResult
            {
                Accepted = false,
                FailureReason = queue.ScopeFailureReason ?? "scope_not_bound",
                Session = CloneSession(request.Session, nowUtc)
            };
        }

        var resolvedActions = await db.OperatorResolutionActions
            .AsNoTracking()
            .Where(x => x.ScopeKey == trackedPerson.ScopeKey
                && x.TrackedPersonId == trackedPerson.TrackedPersonId
                && x.RecomputeStatus == ResolutionRecomputeLifecycleStatuses.Done)
            .OrderByDescending(x => x.RecomputeCompletedAtUtc ?? x.RecomputeStatusUpdatedAtUtc ?? x.CreatedAtUtc)
            .Take(500)
            .ToListAsync(ct);

        var items = queue.Items
            .Select(x => new OperatorWorkspaceResolutionItemView
            {
                ScopeItemKey = x.ScopeItemKey,
                ItemType = x.ItemType,
                Title = x.Title,
                Summary = x.Summary,
                WhyItMatters = x.WhyItMatters,
                AffectedFamily = x.AffectedFamily,
                AffectedObjectRef = x.AffectedObjectRef,
                TrustFactor = Clamp01(x.TrustFactor),
                Status = x.Status,
                EvidenceCount = x.EvidenceCount,
                UpdatedAtUtc = x.UpdatedAtUtc,
                Priority = x.Priority,
                RecommendedNextAction = x.RecommendedNextAction,
                AvailableActions = x.AvailableActions
            })
            .ToList();

        var resolvedDistinctCount = resolvedActions
            .Where(x => !string.IsNullOrWhiteSpace(x.ScopeItemKey))
            .Select(x => x.ScopeItemKey.Trim())
            .Distinct(StringComparer.Ordinal)
            .Count();
        var lastResolvedAtUtc = resolvedActions
            .Select(x => x.RecomputeCompletedAtUtc ?? x.RecomputeStatusUpdatedAtUtc)
            .Where(x => x.HasValue)
            .Select(x => DateTime.SpecifyKind(x!.Value, DateTimeKind.Utc))
            .OrderByDescending(x => x)
            .FirstOrDefault();

        var session = CloneSession(request.Session, nowUtc);
        session.ActiveTrackedPersonId = trackedPerson.TrackedPersonId;
        session.ActiveScopeItemKey = null;
        session.ActiveMode = OperatorModeTypes.ResolutionQueue;
        session.UnfinishedStep = null;

        return new OperatorPersonWorkspaceResolutionQueryResult
        {
            Accepted = true,
            FailureReason = null,
            Session = session,
            Resolution = new OperatorPersonWorkspaceResolutionSectionView
            {
                GeneratedAtUtc = nowUtc,
                UnresolvedCount = Math.Max(0, queue.TotalOpenCount),
                ResolvedCount = resolvedDistinctCount,
                ResolvedActionCount = resolvedActions.Count,
                LastResolvedAtUtc = lastResolvedAtUtc == default ? null : lastResolvedAtUtc,
                StatusCounts = queue.StatusCounts,
                PriorityCounts = queue.PriorityCounts,
                Items = items
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

        var refinementRecord = await _operatorOfflineEventRepository.RefineWithinScopeAsync(
            request.OfflineEventId,
            trackedPerson.ScopeKey,
            trackedPersonId.Value,
            request.Summary,
            request.RecordingReference,
            request.ClearRecordingReference,
            request.RefinementNote,
            request.OperatorIdentity,
            request.Session,
            request.SubmittedAtUtc == default ? nowUtc : request.SubmittedAtUtc,
            ct);
        if (refinementRecord == null)
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

        _logger.LogInformation(
            "Offline-event refinement updated: offline_event_id={OfflineEventId}, tracked_person_id={TrackedPersonId}, operator_id={OperatorId}, audit_event_id={AuditEventId}",
            refinementRecord.OfflineEvent.OfflineEventId,
            trackedPersonId.Value,
            request.OperatorIdentity.OperatorId,
            refinementRecord.AuditEventId);

        return new OperatorOfflineEventRefinementResult
        {
            Accepted = true,
            AuditEventId = refinementRecord.AuditEventId,
            Session = session,
            OfflineEvent = BuildOfflineEventDetailView(refinementRecord.OfflineEvent)
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

        var activePersonIds = db.PersonOperatorLinks
            .AsNoTracking()
            .Where(link => link.Status == ActiveStatus)
            .Select(link => link.PersonId)
            .Distinct();

        var rows = await db.Persons
            .AsNoTracking()
            .Where(person =>
                person.Status == ActiveStatus
                && person.PersonType == "tracked_person"
                && activePersonIds.Contains(person.Id))
            .OrderBy(person => person.DisplayName)
            .ThenBy(person => person.CanonicalName)
            .ThenByDescending(person => person.UpdatedAt)
            .Select(person => new
            {
                person.Id,
                person.ScopeKey,
                person.DisplayName,
                person.CanonicalName,
                person.UpdatedAt
            })
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
            new OperatorWorkspaceSectionState { SectionKey = "profile", Label = "Profile", Status = "ready", Available = true },
            new OperatorWorkspaceSectionState { SectionKey = "pair_dynamics", Label = "Pair Dynamics", Status = "ready", Available = true },
            new OperatorWorkspaceSectionState { SectionKey = "timeline", Label = "Timeline", Status = "ready", Available = true },
            new OperatorWorkspaceSectionState { SectionKey = "evidence", Label = "Evidence", Status = "ready", Available = true },
            new OperatorWorkspaceSectionState { SectionKey = "revisions", Label = "Revisions", Status = "ready", Available = true },
            new OperatorWorkspaceSectionState { SectionKey = "resolution", Label = "Resolution", Status = "ready", Available = true }
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

    private static OperatorWorkspaceBoundedSnapshotView BuildBoundedWorkspaceSnapshot(
        OperatorIdentityContext? operatorIdentity,
        OperatorSessionContext? session,
        OperatorTrackedPersonScopeSummary? trackedPerson,
        OperatorWorkspaceDurableFamilyCard? pairCard)
    {
        return new OperatorWorkspaceBoundedSnapshotView
        {
            Operator = BuildOperatorSnapshot(operatorIdentity, session),
            TrackedPerson = BuildTrackedPersonSnapshot(trackedPerson),
            Pair = BuildPairSnapshot(pairCard)
        };
    }

    private static OperatorWorkspaceOperatorIdentitySnapshotView BuildOperatorSnapshot(
        OperatorIdentityContext? operatorIdentity,
        OperatorSessionContext? session)
    {
        return new OperatorWorkspaceOperatorIdentitySnapshotView
        {
            OperatorId = NormalizeAuditValue(operatorIdentity?.OperatorId),
            OperatorDisplay = NormalizeAuditValue(operatorIdentity?.OperatorDisplay),
            SurfaceSubject = NormalizeAuditValue(operatorIdentity?.SurfaceSubject),
            AuthSource = NormalizeAuditValue(operatorIdentity?.AuthSource),
            AuthTimeUtc = operatorIdentity?.AuthTimeUtc == default ? null : operatorIdentity?.AuthTimeUtc,
            OperatorSessionId = NormalizeAuditValue(session?.OperatorSessionId),
            Surface = NormalizeAuditValue(OperatorSurfaceTypes.Normalize(session?.Surface)),
            ActiveMode = NormalizeAuditValue(OperatorModeTypes.Normalize(session?.ActiveMode)),
            SessionAuthenticatedAtUtc = session?.AuthenticatedAtUtc == default ? null : session?.AuthenticatedAtUtc,
            SessionLastSeenAtUtc = session?.LastSeenAtUtc == default ? null : session?.LastSeenAtUtc,
            SessionExpiresAtUtc = session?.ExpiresAtUtc
        };
    }

    private static OperatorWorkspaceTrackedIdentitySnapshotView BuildTrackedPersonSnapshot(
        OperatorTrackedPersonScopeSummary? trackedPerson)
    {
        if (trackedPerson == null)
        {
            return new OperatorWorkspaceTrackedIdentitySnapshotView();
        }

        return new OperatorWorkspaceTrackedIdentitySnapshotView
        {
            TrackedPersonId = trackedPerson.TrackedPersonId == Guid.Empty ? null : trackedPerson.TrackedPersonId,
            ScopeKey = NormalizeAuditValue(trackedPerson.ScopeKey),
            DisplayName = NormalizeAuditValue(trackedPerson.DisplayName),
            EvidenceCount = trackedPerson.EvidenceCount,
            UnresolvedCount = trackedPerson.UnresolvedCount,
            HasUnresolved = trackedPerson.HasUnresolved,
            RecentUpdateAtUtc = trackedPerson.RecentUpdateAtUtc == default ? null : trackedPerson.RecentUpdateAtUtc,
            LastUnresolvedAtUtc = trackedPerson.LastUnresolvedAtUtc
        };
    }

    private static OperatorWorkspacePairSnapshotView BuildPairSnapshot(
        OperatorWorkspaceDurableFamilyCard? pairCard)
    {
        if (pairCard == null)
        {
            return new OperatorWorkspacePairSnapshotView
            {
                Available = false,
                TruthLayer = "unknown",
                PromotionState = "unknown",
                LatestSummary = null
            };
        }

        return new OperatorWorkspacePairSnapshotView
        {
            Family = NormalizeAuditValue(pairCard.Family, Stage7DurableObjectFamilies.PairDynamics),
            Label = NormalizeAuditValue(pairCard.Label, "Pair Dynamics"),
            Available = true,
            ObjectCount = pairCard.ObjectCount,
            Trust = Clamp01(pairCard.Trust),
            Uncertainty = Clamp01(pairCard.Uncertainty),
            Coverage = Clamp01(pairCard.Coverage),
            Freshness = Clamp01(pairCard.Freshness),
            Stability = Clamp01(pairCard.Stability),
            ContradictionCount = pairCard.ContradictionCount,
            EvidenceLinkCount = pairCard.EvidenceLinkCount,
            LatestUpdatedAtUtc = pairCard.LatestUpdatedAtUtc,
            TruthLayer = NormalizeAuditValue(pairCard.TruthLayer),
            PromotionState = NormalizeAuditValue(pairCard.PromotionState),
            LatestSummary = NormalizeOptional(pairCard.LatestSummary)
        };
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

    private static List<OperatorWorkspaceProfileSignalView> BuildProfileSignals(
        DbDurableProfile profile,
        DbDurableObjectMetadata? metadata,
        int evidenceLinkCount)
    {
        var signals = new List<OperatorWorkspaceProfileSignalView>();
        if (!TryParseJsonObject(profile.PayloadJson, out var payloadRoot))
        {
            return signals;
        }

        AddProfileSignals(
            payloadRoot,
            propertyName: "inferences",
            signalType: "inference",
            keyPropertyName: "inference_type",
            summaryPropertyName: "summary",
            profile,
            metadata,
            evidenceLinkCount,
            signals);

        AddProfileSignals(
            payloadRoot,
            propertyName: "hypotheses",
            signalType: "hypothesis",
            keyPropertyName: "hypothesis_type",
            summaryPropertyName: "statement",
            profile,
            metadata,
            evidenceLinkCount,
            signals);

        return signals;
    }

    private static void AddProfileSignals(
        JsonElement payloadRoot,
        string propertyName,
        string signalType,
        string keyPropertyName,
        string summaryPropertyName,
        DbDurableProfile profile,
        DbDurableObjectMetadata? metadata,
        int evidenceLinkCount,
        List<OperatorWorkspaceProfileSignalView> target)
    {
        if (!payloadRoot.TryGetProperty(propertyName, out var signalsArray)
            || signalsArray.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var signalElement in signalsArray.EnumerateArray())
        {
            if (signalElement.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var signalKey = NormalizeOptional(GetString(signalElement, keyPropertyName)) ?? "unknown";
            var summary = NormalizeOptional(GetString(signalElement, summaryPropertyName)) ?? "n/a";
            var confidence = Clamp01(GetSingle(signalElement, "confidence") ?? metadata?.Confidence ?? 0f);
            var evidenceRefCount = GetArrayLength(signalElement, "evidence_refs");
            if (evidenceRefCount == 0)
            {
                evidenceRefCount = evidenceLinkCount;
            }

            target.Add(new OperatorWorkspaceProfileSignalView
            {
                DurableProfileId = profile.Id,
                DurableObjectMetadataId = profile.DurableObjectMetadataId,
                LastModelPassRunId = profile.LastModelPassRunId ?? metadata?.CreatedByModelPassRunId,
                RevisionNumber = profile.CurrentRevisionNumber,
                ProfileScope = NormalizeOptional(profile.ProfileScope) ?? "unknown",
                SignalType = signalType,
                SignalKey = signalKey,
                Summary = summary,
                Confidence = confidence,
                EvidenceRefCount = evidenceRefCount,
                TruthLayer = NormalizeOptional(metadata?.TruthLayer) ?? "unknown",
                PromotionState = NormalizeOptional(metadata?.PromotionState) ?? "unknown",
                UpdatedAtUtc = DateTime.SpecifyKind(profile.UpdatedAt, DateTimeKind.Utc)
            });
        }
    }

    private static int SumProfilePressure(IEnumerable<DbDurableProfile> profiles, string pressureKey)
    {
        var total = 0;
        foreach (var profile in profiles)
        {
            if (!TryParseJsonObject(profile.PayloadJson, out var payloadRoot)
                || !payloadRoot.TryGetProperty("bootstrap_pressure", out var pressure)
                || pressure.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            total += GetInt32(pressure, pressureKey) ?? 0;
        }

        return Math.Max(0, total);
    }

    private static List<OperatorWorkspacePairDynamicsSignalView> BuildPairDynamicsSignals(
        DbDurablePairDynamics pairDynamics,
        DbDurableObjectMetadata? metadata,
        int evidenceLinkCount)
    {
        var signals = new List<OperatorWorkspacePairDynamicsSignalView>();
        if (!TryParseJsonObject(pairDynamics.PayloadJson, out var payloadRoot))
        {
            return signals;
        }

        AddPairDynamicsSignals(
            payloadRoot,
            propertyName: "dimensions",
            signalType: "dimension",
            keyPropertyName: "key",
            summaryPropertyName: "value",
            valuePropertyName: "value",
            pairDynamics,
            metadata,
            evidenceLinkCount,
            signals);

        AddPairDynamicsSignals(
            payloadRoot,
            propertyName: "inferences",
            signalType: "inference",
            keyPropertyName: "inference_type",
            summaryPropertyName: "summary",
            valuePropertyName: null,
            pairDynamics,
            metadata,
            evidenceLinkCount,
            signals);

        AddPairDynamicsSignals(
            payloadRoot,
            propertyName: "hypotheses",
            signalType: "hypothesis",
            keyPropertyName: "hypothesis_type",
            summaryPropertyName: "statement",
            valuePropertyName: null,
            pairDynamics,
            metadata,
            evidenceLinkCount,
            signals);

        AddPairDynamicsSignals(
            payloadRoot,
            propertyName: "conflicts",
            signalType: "conflict",
            keyPropertyName: "conflict_type",
            summaryPropertyName: "summary",
            valuePropertyName: null,
            pairDynamics,
            metadata,
            evidenceLinkCount,
            signals);

        return signals;
    }

    private static void AddPairDynamicsSignals(
        JsonElement payloadRoot,
        string propertyName,
        string signalType,
        string keyPropertyName,
        string summaryPropertyName,
        string? valuePropertyName,
        DbDurablePairDynamics pairDynamics,
        DbDurableObjectMetadata? metadata,
        int evidenceLinkCount,
        List<OperatorWorkspacePairDynamicsSignalView> target)
    {
        if (!payloadRoot.TryGetProperty(propertyName, out var signalsArray)
            || signalsArray.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var signalElement in signalsArray.EnumerateArray())
        {
            if (signalElement.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var signalKey = NormalizeOptional(GetString(signalElement, keyPropertyName)) ?? "unknown";
            var summary = NormalizeOptional(GetString(signalElement, summaryPropertyName)) ?? "n/a";
            var signalValue = valuePropertyName == null
                ? null
                : NormalizeOptional(RenderJsonValue(signalElement, valuePropertyName));
            var confidence = Clamp01(GetSingle(signalElement, "confidence") ?? metadata?.Confidence ?? 0f);
            var evidenceRefCount = GetArrayLength(signalElement, "evidence_refs");
            if (evidenceRefCount == 0)
            {
                evidenceRefCount = evidenceLinkCount;
            }

            target.Add(new OperatorWorkspacePairDynamicsSignalView
            {
                DurablePairDynamicsId = pairDynamics.Id,
                DurableObjectMetadataId = pairDynamics.DurableObjectMetadataId,
                LastModelPassRunId = pairDynamics.LastModelPassRunId ?? metadata?.CreatedByModelPassRunId,
                RevisionNumber = pairDynamics.CurrentRevisionNumber,
                PairDynamicsType = NormalizeOptional(pairDynamics.PairDynamicsType) ?? "unknown",
                SignalType = signalType,
                SignalKey = signalKey,
                Summary = summary,
                SignalValue = signalValue,
                DirectionOfChange = ResolveSignalDirection(signalKey, signalValue, summary),
                Confidence = confidence,
                EvidenceRefCount = evidenceRefCount,
                TruthLayer = NormalizeOptional(metadata?.TruthLayer) ?? "unknown",
                PromotionState = NormalizeOptional(metadata?.PromotionState) ?? "unknown",
                UpdatedAtUtc = DateTime.SpecifyKind(pairDynamics.UpdatedAt, DateTimeKind.Utc)
            });
        }
    }

    private static int SumPairDynamicsPressure(IEnumerable<DbDurablePairDynamics> pairs, string pressureKey)
    {
        var total = 0;
        foreach (var pair in pairs)
        {
            if (!TryParseJsonObject(pair.PayloadJson, out var payloadRoot)
                || !payloadRoot.TryGetProperty("bootstrap_pressure", out var pressure)
                || pressure.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            total += GetInt32(pressure, pressureKey) ?? 0;
        }

        return Math.Max(0, total);
    }

    private static List<OperatorWorkspaceTimelineShiftView> BuildTimelineShifts(
        IEnumerable<DbDurableTimelineEpisode> episodes,
        IEnumerable<DbDurableStoryArc> arcs,
        IReadOnlyDictionary<Guid, DbDurableObjectMetadata> metadataById,
        IReadOnlyDictionary<Guid, int> evidenceCounts)
    {
        var shifts = new List<OperatorWorkspaceTimelineShiftView>();

        foreach (var episode in episodes)
        {
            metadataById.TryGetValue(episode.DurableObjectMetadataId, out var metadata);
            var evidenceRefCount = CountTimelineEvidenceRefs(
                episode.PayloadJson,
                "inferences",
                "evidence_refs");
            if (evidenceRefCount == 0)
            {
                evidenceRefCount = evidenceCounts.GetValueOrDefault(episode.DurableObjectMetadataId);
            }

            shifts.Add(new OperatorWorkspaceTimelineShiftView
            {
                Family = Stage7DurableObjectFamilies.TimelineEpisode,
                DurableObjectId = episode.Id,
                DurableObjectMetadataId = episode.DurableObjectMetadataId,
                LastModelPassRunId = episode.LastModelPassRunId ?? metadata?.CreatedByModelPassRunId,
                RevisionNumber = episode.CurrentRevisionNumber,
                ShiftType = NormalizeOptional(episode.EpisodeType) ?? "timeline_episode",
                ClosureState = NormalizeOptional(episode.ClosureState) ?? "unknown",
                Summary = BuildSummarySnippet(episode.SummaryJson),
                ShiftStartedAtUtc = NormalizeUtc(episode.StartedAtUtc) ?? GetDateTimeFromJson(episode.PayloadJson, "started_at_utc"),
                ShiftEndedAtUtc = NormalizeUtc(episode.EndedAtUtc) ?? GetDateTimeFromJson(episode.PayloadJson, "ended_at_utc"),
                Confidence = Clamp01(metadata?.Confidence ?? 0f),
                EvidenceRefCount = evidenceRefCount,
                TruthLayer = NormalizeOptional(metadata?.TruthLayer) ?? "unknown",
                PromotionState = NormalizeOptional(metadata?.PromotionState) ?? "unknown",
                UpdatedAtUtc = DateTime.SpecifyKind(episode.UpdatedAt, DateTimeKind.Utc)
            });
        }

        foreach (var arc in arcs)
        {
            metadataById.TryGetValue(arc.DurableObjectMetadataId, out var metadata);
            var evidenceRefCount = CountTimelineEvidenceRefs(
                arc.PayloadJson,
                "hypotheses",
                "evidence_refs",
                "conflicts");
            if (evidenceRefCount == 0)
            {
                evidenceRefCount = evidenceCounts.GetValueOrDefault(arc.DurableObjectMetadataId);
            }

            shifts.Add(new OperatorWorkspaceTimelineShiftView
            {
                Family = Stage7DurableObjectFamilies.StoryArc,
                DurableObjectId = arc.Id,
                DurableObjectMetadataId = arc.DurableObjectMetadataId,
                LastModelPassRunId = arc.LastModelPassRunId ?? metadata?.CreatedByModelPassRunId,
                RevisionNumber = arc.CurrentRevisionNumber,
                ShiftType = NormalizeOptional(arc.ArcType) ?? "story_arc",
                ClosureState = NormalizeOptional(arc.ClosureState) ?? "unknown",
                Summary = BuildSummarySnippet(arc.SummaryJson),
                ShiftStartedAtUtc = NormalizeUtc(arc.OpenedAtUtc) ?? GetDateTimeFromJson(arc.PayloadJson, "opened_at_utc"),
                ShiftEndedAtUtc = NormalizeUtc(arc.ClosedAtUtc) ?? GetDateTimeFromJson(arc.PayloadJson, "closed_at_utc"),
                Confidence = Clamp01(metadata?.Confidence ?? 0f),
                EvidenceRefCount = evidenceRefCount,
                TruthLayer = NormalizeOptional(metadata?.TruthLayer) ?? "unknown",
                PromotionState = NormalizeOptional(metadata?.PromotionState) ?? "unknown",
                UpdatedAtUtc = DateTime.SpecifyKind(arc.UpdatedAt, DateTimeKind.Utc)
            });
        }

        return shifts;
    }

    private static int CountTimelineEvidenceRefs(
        string? payloadJson,
        string primaryArrayProperty,
        string evidenceRefsProperty,
        string? secondaryArrayProperty = null)
    {
        if (!TryParseJsonObject(payloadJson, out var payloadRoot))
        {
            return 0;
        }

        var total = CountEvidenceRefsInArray(payloadRoot, primaryArrayProperty, evidenceRefsProperty);
        if (!string.IsNullOrWhiteSpace(secondaryArrayProperty))
        {
            total += CountEvidenceRefsInArray(payloadRoot, secondaryArrayProperty!, evidenceRefsProperty);
        }

        return total;
    }

    private static int CountEvidenceRefsInArray(
        JsonElement payloadRoot,
        string arrayPropertyName,
        string evidenceRefsProperty)
    {
        if (!payloadRoot.TryGetProperty(arrayPropertyName, out var arrayNode)
            || arrayNode.ValueKind != JsonValueKind.Array)
        {
            return 0;
        }

        var total = 0;
        foreach (var item in arrayNode.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            total += GetArrayLength(item, evidenceRefsProperty);
        }

        return total;
    }

    private static DateTime? GetDateTimeFromJson(string? payloadJson, string propertyName)
    {
        if (!TryParseJsonObject(payloadJson, out var payloadRoot))
        {
            return null;
        }

        return GetDateTime(payloadRoot, propertyName);
    }

    private static DateTime? NormalizeUtc(DateTime? value)
    {
        if (!value.HasValue)
        {
            return null;
        }

        return DateTime.SpecifyKind(value.Value, DateTimeKind.Utc);
    }

    private static string ResolvePairDynamicsDirection(
        IEnumerable<OperatorWorkspacePairDynamicsSignalView> signals,
        int ambiguityCount,
        int contradictionCount)
    {
        var grouped = signals
            .GroupBy(x => NormalizeOptional(x.DirectionOfChange) ?? "steady", StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.Count(), StringComparer.Ordinal);
        var improving = grouped.GetValueOrDefault("improving");
        var concerning = grouped.GetValueOrDefault("concerning");

        if (concerning > improving || contradictionCount > 0)
        {
            return "concerning";
        }

        if (improving > concerning && ambiguityCount == 0)
        {
            return "improving";
        }

        return "steady";
    }

    private static string ResolveSignalDirection(string signalKey, string? signalValue, string summary)
    {
        var text = $"{signalKey} {signalValue} {summary}".ToLowerInvariant();
        var positiveSignals = new[]
        {
            "stable",
            "improving",
            "repair_pressure_low",
            "cautiously_safe",
            "evidence_backed_exchange"
        };
        var negativeSignals = new[]
        {
            "concerning",
            "declining",
            "contradiction",
            "ambiguity",
            "repair_pressure_present",
            "needs_review",
            "low_signal_exchange",
            "open_response"
        };

        var positiveScore = positiveSignals.Count(text.Contains);
        var negativeScore = negativeSignals.Count(text.Contains);
        if (negativeScore > positiveScore)
        {
            return "concerning";
        }

        if (positiveScore > negativeScore)
        {
            return "improving";
        }

        return "steady";
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

    private sealed class WorkspaceRevisionSource
    {
        public string Family { get; init; } = string.Empty;
        public Guid DurableObjectId { get; init; }
        public Guid RevisionId { get; init; }
        public Guid DurableObjectMetadataId { get; init; }
        public Guid? ModelPassRunId { get; init; }
        public int RevisionNumber { get; init; }
        public string RevisionHash { get; init; } = string.Empty;
        public float Confidence { get; init; }
        public float Freshness { get; init; }
        public float Stability { get; init; }
        public string ContradictionMarkersJson { get; init; } = "[]";
        public string SummaryJson { get; init; } = "{}";
        public DateTime CreatedAtUtc { get; init; }
    }

    private sealed class WorkspaceModelPassTrigger
    {
        public string TriggerKind { get; init; } = "none";
        public string TriggerRef { get; init; } = "n/a";
        public string PassFamily { get; init; } = "unknown";
        public string RunKind { get; init; } = "unknown";
        public string ResultStatus { get; init; } = "unknown";
        public string TargetType { get; init; } = "unknown";
        public string TargetRef { get; init; } = "n/a";
    }
}
