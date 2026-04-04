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
                UpdatedAtUtc = DateTime.SpecifyKind(x.UpdatedAt, DateTimeKind.Utc)
            })
            .ToList();
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
}
