using System.Data.Common;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Host.Launch;

public static class Opint003LoopValidationRunner
{
    private const string ScopeKey = "chat:885574984";
    private const long ChatId = 885574984;
    private const string OperatorId = "opint-003-d-validator";
    private const string OperatorDisplay = "OPINT-003-D Validator";
    private const string SurfaceSubject = "validation";
    private const string AuthSource = "local_runtime_validation";
    private const string SeedTrackedPersonDisplayName = "000 OPINT-003-D Validation Subject";
    private const string SeedTrackedPersonCanonicalName = "OPINT-003-D Validation Subject";

    public static async Task<Opint003LoopValidationReport> RunAsync(
        IServiceProvider services,
        string? outputPath = null,
        CancellationToken ct = default)
    {
        var resolvedOutputPath = ResolveOutputPath(outputPath);
        var dbFactory = services.GetRequiredService<IDbContextFactory<TgAssistantDbContext>>();
        var applicationService = services.GetRequiredService<IOperatorResolutionApplicationService>();
        var queueRepository = services.GetRequiredService<IStage8RecomputeQueueRepository>();
        var relatedConflictRepository = services.GetRequiredService<IStage8RelatedConflictRepository>();

        var report = new Opint003LoopValidationReport
        {
            GeneratedAtUtc = DateTime.UtcNow,
            OutputPath = resolvedOutputPath,
            ScopeKey = ScopeKey,
            ChatId = ChatId
        };

        ValidationSeedState? seedState = null;
        Exception? fatal = null;

        try
        {
            report.SchemaCheck = await LoadSchemaCheckAsync(dbFactory, ct);
            seedState = await SeedValidationStateAsync(dbFactory, ct);
            report.TrackedPerson = new Opint003TrackedPersonReport
            {
                TrackedPersonId = seedState.TrackedPersonId,
                DisplayName = seedState.TrackedPersonDisplayName
            };

            var authTimeUtc = DateTime.UtcNow;
            var sessionId = $"opint-003-d-session-{Guid.NewGuid():N}";
            var baseIdentity = BuildIdentity(authTimeUtc);
            var baseSession = BuildSession(sessionId, authTimeUtc, expiresAtUtc: authTimeUtc.AddMinutes(30));

            var trackedPersonQuery = await applicationService.QueryTrackedPersonsAsync(
                new OperatorTrackedPersonQueryRequest
                {
                    OperatorIdentity = baseIdentity,
                    Session = baseSession,
                    PreferredTrackedPersonId = seedState.TrackedPersonId,
                    Limit = 50
                },
                ct);
            Ensure(trackedPersonQuery.Accepted, $"Tracked-person query failed: {trackedPersonQuery.FailureReason ?? "unknown"}");
            Ensure(
                trackedPersonQuery.TrackedPersons.Any(x => x.TrackedPersonId == seedState.TrackedPersonId),
                "Tracked-person query did not return the validation tracked person.");

            var trackedPersonSelection = await applicationService.SelectTrackedPersonAsync(
                new OperatorTrackedPersonSelectionRequest
                {
                    OperatorIdentity = baseIdentity,
                    Session = trackedPersonQuery.Session,
                    TrackedPersonId = seedState.TrackedPersonId,
                    RequestedAtUtc = authTimeUtc
                },
                ct);
            Ensure(trackedPersonSelection.Accepted, $"Tracked-person selection failed: {trackedPersonSelection.FailureReason ?? "unknown"}");

            var queue = await applicationService.GetResolutionQueueAsync(
                new OperatorResolutionQueueQueryRequest
                {
                    OperatorIdentity = baseIdentity,
                    Session = trackedPersonSelection.Session,
                    TrackedPersonId = seedState.TrackedPersonId,
                    ItemTypes = [ResolutionItemTypes.MissingData],
                    SortBy = ResolutionQueueSortFields.UpdatedAt,
                    SortDirection = ResolutionSortDirections.Desc,
                    Limit = 20
                },
                ct);
            Ensure(queue.Accepted, $"Resolution queue query failed: {queue.FailureReason ?? "unknown"}");
            Ensure(
                queue.Queue.Items.Any(x => string.Equals(x.ScopeItemKey, seedState.NormalSourceItemKey, StringComparison.Ordinal)),
                "Resolution queue did not expose the normal-path seeded source item.");
            Ensure(
                queue.Queue.Items.Any(x => string.Equals(x.ScopeItemKey, seedState.ClarificationSourceItemKey, StringComparison.Ordinal)),
                "Resolution queue did not expose the clarification-path seeded source item.");

            report.ContractChecks = new Opint003ContractChecks
            {
                TrackedPersonQueryAccepted = trackedPersonQuery.Accepted,
                TrackedPersonSelectionAccepted = trackedPersonSelection.Accepted,
                ResolutionQueueAccepted = queue.Accepted,
                QueueVisibleItemCount = queue.Queue.Items.Count
            };

            report.NormalScenario = await ExecuteScenarioAsync(
                applicationService,
                dbFactory,
                queueRepository,
                relatedConflictRepository,
                baseIdentity,
                trackedPersonSelection.Session,
                seedState,
                seedState.NormalSourceItemKey,
                scenarioName: "normal_result_ready",
                requestId: $"opint-003-d-normal-{Guid.NewGuid():N}",
                completionResultStatus: ModelPassResultStatuses.ResultReady,
                ct);

            report.ClarificationScenario = await ExecuteScenarioAsync(
                applicationService,
                dbFactory,
                queueRepository,
                relatedConflictRepository,
                baseIdentity,
                trackedPersonSelection.Session,
                seedState,
                seedState.ClarificationSourceItemKey,
                scenarioName: "degraded_clarification_blocked",
                requestId: $"opint-003-d-clarification-{Guid.NewGuid():N}",
                completionResultStatus: ModelPassResultStatuses.NeedOperatorClarification,
                ct);

            report.KeyMetrics = new Opint003KeyMetrics
            {
                ScenariosPassed = CountPassed(report.NormalScenario, report.ClarificationScenario),
                ScenariosTotal = 2,
                RelatedConflictCreated = report.NormalScenario.RelatedConflictCreatedCount,
                RelatedConflictResolved = report.NormalScenario.RelatedConflictResolvedCount,
                RelatedConflictDomainReviewEvents = report.NormalScenario.RelatedConflictDomainReviewEventCount
            };

            report.AllChecksPassed =
                report.SchemaCheck.Migrations0052And0053Present
                && report.ContractChecks.TrackedPersonQueryAccepted
                && report.ContractChecks.TrackedPersonSelectionAccepted
                && report.ContractChecks.ResolutionQueueAccepted
                && report.NormalScenario.Passed
                && report.ClarificationScenario.Passed;
        }
        catch (Exception ex)
        {
            fatal = ex;
            report.AllChecksPassed = false;
            report.FatalError = ex.Message;
        }
        finally
        {
            if (seedState != null)
            {
                report.Cleanup = await CleanupValidationStateAsync(dbFactory, seedState, ct);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(resolvedOutputPath)!);
            var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(resolvedOutputPath, json, ct);
        }

        if (!report.AllChecksPassed)
        {
            throw new InvalidOperationException(
                "OPINT-003-D bounded validation failed: lifecycle loop evidence is incomplete.",
                fatal);
        }

        return report;
    }

    private static async Task<Opint003ScenarioReport> ExecuteScenarioAsync(
        IOperatorResolutionApplicationService applicationService,
        IDbContextFactory<TgAssistantDbContext> dbFactory,
        IStage8RecomputeQueueRepository queueRepository,
        IStage8RelatedConflictRepository relatedConflictRepository,
        OperatorIdentityContext operatorIdentity,
        OperatorSessionContext selectedSession,
        ValidationSeedState seedState,
        string scopeItemKey,
        string scenarioName,
        string requestId,
        string completionResultStatus,
        CancellationToken ct)
    {
        var detail = await applicationService.GetResolutionDetailAsync(
            new OperatorResolutionDetailQueryRequest
            {
                OperatorIdentity = operatorIdentity,
                Session = selectedSession,
                TrackedPersonId = seedState.TrackedPersonId,
                ScopeItemKey = scopeItemKey,
                EvidenceLimit = 3,
                EvidenceSortBy = ResolutionEvidenceSortFields.ObservedAt,
                EvidenceSortDirection = ResolutionSortDirections.Desc
            },
            ct);
        Ensure(detail.Accepted, $"Resolution detail query failed for '{scenarioName}': {detail.FailureReason ?? "unknown"}");
        Ensure(detail.Detail.ItemFound && detail.Detail.Item != null, $"Resolution detail did not return an item for '{scenarioName}'.");

        var actionRequest = new ResolutionActionRequest
        {
            RequestId = requestId,
            TrackedPersonId = seedState.TrackedPersonId,
            ScopeItemKey = scopeItemKey,
            ActionType = ResolutionActionTypes.Approve,
            OperatorIdentity = operatorIdentity,
            Session = detail.Session,
            SubmittedAtUtc = DateTime.UtcNow
        };

        var action = await applicationService.SubmitResolutionActionAsync(actionRequest, ct);
        Ensure(action.Accepted, $"Resolution action failed for '{scenarioName}': {action.FailureReason ?? "unknown"}");
        Ensure(action.Action.Accepted, $"Resolution action did not return an accepted action result for '{scenarioName}'.");
        var recompute = action.Action.Recompute ?? throw new InvalidOperationException($"Resolution action did not return recompute contract for '{scenarioName}'.");
        Ensure(
            string.Equals(recompute.LifecycleStatus, ResolutionRecomputeLifecycleStatuses.Running, StringComparison.Ordinal),
            $"Expected initial recompute lifecycle 'running' for '{scenarioName}', got '{recompute.LifecycleStatus}'.");
        Ensure(recompute.Targets.Count == 1, $"Expected exactly one recompute target for '{scenarioName}'.");
        var target = recompute.Targets[0];
        Ensure(target.QueueItemId.HasValue, $"Recompute target queue item id missing for '{scenarioName}'.");
        var targetQueueItemId = target.QueueItemId!.Value;
        seedState.ActionRequestIds.Add(requestId);
        if (action.Action.ActionId.HasValue)
        {
            seedState.ActionIds.Add(action.Action.ActionId.Value);
        }

        seedState.ActionQueueItemIds.Add(targetQueueItemId);

        var leased = await LeaseQueueItemAsync(dbFactory, targetQueueItemId, ct);
        Stage8RelatedConflictReevaluationResult relatedConflictResult;
        Guid? modelPassRunId = null;
        if (string.Equals(completionResultStatus, ModelPassResultStatuses.ResultReady, StringComparison.Ordinal))
        {
            relatedConflictResult = await relatedConflictRepository.ReevaluateAsync(
                new Stage8RelatedConflictReevaluationRequest
                {
                    QueueItemId = leased.Id,
                    ScopeKey = leased.ScopeKey,
                    PersonId = leased.PersonId,
                    TargetFamily = leased.TargetFamily,
                    TargetRef = leased.TargetRef,
                    ResultStatus = completionResultStatus,
                    TriggerKind = leased.TriggerKind,
                    TriggerRef = leased.TriggerRef,
                    ModelPassRunId = modelPassRunId
                },
                ct);
            seedState.CreatedConflictIds.AddRange(relatedConflictResult.ActiveConflictIds);
            seedState.ResolvedConflictIds.AddRange(relatedConflictResult.ResolvedConflictIds);
        }
        else
        {
            relatedConflictResult = await relatedConflictRepository.ReevaluateAsync(
                new Stage8RelatedConflictReevaluationRequest
                {
                    QueueItemId = leased.Id,
                    ScopeKey = leased.ScopeKey,
                    PersonId = leased.PersonId,
                    TargetFamily = leased.TargetFamily,
                    TargetRef = leased.TargetRef,
                    ResultStatus = completionResultStatus,
                    TriggerKind = leased.TriggerKind,
                    TriggerRef = leased.TriggerRef,
                    ModelPassRunId = modelPassRunId
                },
                ct);
        }

        await queueRepository.CompleteAsync(leased.Id, leased.LeaseToken!.Value, completionResultStatus, modelPassRunId, ct);

        var actionRow = await LoadActionRowAsync(dbFactory, action.Action.ActionId!.Value, ct);
        var replay = await applicationService.SubmitResolutionActionAsync(actionRequest, ct);
        Ensure(replay.Accepted && replay.Action.Accepted, $"Replay action failed for '{scenarioName}'.");
        var replayRecompute = replay.Action.Recompute ?? throw new InvalidOperationException($"Replay did not return recompute contract for '{scenarioName}'.");

        var domainReviewEventCount = await CountDomainReviewEventsAsync(
            dbFactory,
            relatedConflictResult.ActiveConflictIds.Concat(relatedConflictResult.ResolvedConflictIds).ToList(),
            ct);

        var expectedLifecycle = string.Equals(completionResultStatus, ModelPassResultStatuses.ResultReady, StringComparison.Ordinal)
            ? ResolutionRecomputeLifecycleStatuses.Done
            : string.Equals(completionResultStatus, ModelPassResultStatuses.NeedOperatorClarification, StringComparison.Ordinal)
                ? ResolutionRecomputeLifecycleStatuses.ClarificationBlocked
                : ResolutionRecomputeLifecycleStatuses.Failed;
        var expectedRelatedConflictApplied = string.Equals(completionResultStatus, ModelPassResultStatuses.ResultReady, StringComparison.Ordinal);

        var report = new Opint003ScenarioReport
        {
            Scenario = scenarioName,
            SourceItemKey = scopeItemKey,
            DetailAccepted = detail.Accepted,
            ActionAccepted = action.Accepted && action.Action.Accepted,
            ReplayAccepted = replay.Accepted && replay.Action.Accepted,
            ActionId = action.Action.ActionId,
            QueueItemId = target.QueueItemId,
            InitialLifecycleStatus = recompute.LifecycleStatus,
            InitialTargetLifecycleStatus = target.LifecycleStatus,
            ActionRowLifecycleStatus = actionRow.RecomputeStatus,
            ActionRowLastResultStatus = actionRow.RecomputeLastResultStatus,
            ActionRowCompletedAtUtc = actionRow.RecomputeCompletedAtUtc,
            ReplayLifecycleStatus = replayRecompute.LifecycleStatus,
            ReplayTargetLifecycleStatus = replayRecompute.Targets[0].LifecycleStatus,
            ReplayLastResultStatus = replayRecompute.LastResultStatus,
            ReplayFailureReason = replayRecompute.FailureReason,
            RelatedConflictApplied = relatedConflictResult.Applied,
            RelatedConflictSkipReason = relatedConflictResult.SkipReason,
            RelatedConflictCreatedCount = relatedConflictResult.CreatedCount,
            RelatedConflictResolvedCount = relatedConflictResult.ResolvedCount,
            RelatedConflictRefreshedCount = relatedConflictResult.RefreshedCount,
            RelatedConflictUnchangedCount = relatedConflictResult.UnchangedCount,
            RelatedConflictActiveConflictIds = relatedConflictResult.ActiveConflictIds.Select(x => x.ToString("D")).ToList(),
            RelatedConflictResolvedConflictIds = relatedConflictResult.ResolvedConflictIds.Select(x => x.ToString("D")).ToList(),
            RelatedConflictDomainReviewEventCount = domainReviewEventCount
        };

        var lifecyclePassed =
            string.Equals(actionRow.RecomputeStatus, expectedLifecycle, StringComparison.Ordinal)
            && string.Equals(replayRecompute.LifecycleStatus, expectedLifecycle, StringComparison.Ordinal)
            && string.Equals(replayRecompute.Targets[0].LifecycleStatus, expectedLifecycle, StringComparison.Ordinal)
            && string.Equals(actionRow.RecomputeLastResultStatus, completionResultStatus, StringComparison.Ordinal)
            && string.Equals(replayRecompute.LastResultStatus, completionResultStatus, StringComparison.Ordinal);
        var relatedConflictPassed = expectedRelatedConflictApplied
            ? relatedConflictResult.Applied
              && relatedConflictResult.CreatedCount == 1
              && relatedConflictResult.ResolvedCount == 1
              && domainReviewEventCount == 2
            : !relatedConflictResult.Applied
              && string.Equals(relatedConflictResult.SkipReason, "result_status_not_ready", StringComparison.Ordinal);

        report.Passed =
            report.DetailAccepted
            && report.ActionAccepted
            && report.ReplayAccepted
            && string.Equals(report.InitialLifecycleStatus, ResolutionRecomputeLifecycleStatuses.Running, StringComparison.Ordinal)
            && string.Equals(report.InitialTargetLifecycleStatus, ResolutionRecomputeLifecycleStatuses.Running, StringComparison.Ordinal)
            && lifecyclePassed
            && relatedConflictPassed;

        return report;
    }

    private static async Task<ValidationSeedState> SeedValidationStateAsync(
        IDbContextFactory<TgAssistantDbContext> dbFactory,
        CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var nowUtc = DateTime.UtcNow;
        var operatorPersonId = await db.PersonOperatorLinks
            .AsNoTracking()
            .Where(x => x.ScopeKey == ScopeKey && x.Status == "active")
            .OrderByDescending(x => x.UpdatedAt)
            .Select(x => (Guid?)x.OperatorPersonId)
            .FirstOrDefaultAsync(ct);
        if (!operatorPersonId.HasValue)
        {
            throw new InvalidOperationException($"No active operator person link is available for scope '{ScopeKey}'.");
        }

        var trackedPersonId = Guid.NewGuid();
        var metadataId = Guid.NewGuid();
        var obsoleteMetadataId = Guid.NewGuid();
        var existingConflictId = Guid.NewGuid();
        var normalSourceQueueId = Guid.NewGuid();
        var clarificationSourceQueueId = Guid.NewGuid();
        var targetRef = $"person:{trackedPersonId:D}";
        var profileObjectKey = $"{targetRef}:profile:validation";
        var obsoleteProfileObjectKey = $"{targetRef}:profile:obsolete";

        db.Persons.Add(new DbPerson
        {
            Id = trackedPersonId,
            ScopeKey = ScopeKey,
            PersonType = "tracked_person",
            DisplayName = SeedTrackedPersonDisplayName,
            CanonicalName = SeedTrackedPersonCanonicalName,
            Status = "active",
            MetadataJson = "{}",
            CreatedAt = nowUtc,
            UpdatedAt = nowUtc
        });
        await db.SaveChangesAsync(ct);

        db.PersonOperatorLinks.Add(new DbPersonOperatorLink
        {
            ScopeKey = ScopeKey,
            OperatorPersonId = operatorPersonId.Value,
            PersonId = trackedPersonId,
            LinkType = "validation_seed",
            Status = "active",
            SourceBindingType = "validation",
            SourceBindingValue = "opint-003-d",
            SourceBindingNormalized = "opint-003-d",
            CreatedAt = nowUtc,
            UpdatedAt = nowUtc
        });
        await db.SaveChangesAsync(ct);

        db.DurableObjectMetadata.Add(new DbDurableObjectMetadata
        {
            Id = metadataId,
            ScopeKey = ScopeKey,
            ObjectFamily = Stage7DurableObjectFamilies.Profile,
            ObjectKey = profileObjectKey,
            Status = "active",
            TruthLayer = ModelNormalizationTruthLayers.DerivedButDurable,
            PromotionState = Stage8PromotionStates.PromotionBlocked,
            OwnerPersonId = trackedPersonId,
            Confidence = 0.82f,
            Coverage = 0.75f,
            Freshness = 0.81f,
            Stability = 0.69f,
            ContradictionMarkersJson = """
                [{"kind":"validation_conflict_a"},{"kind":"validation_conflict_b"}]
                """,
            MetadataJson = """{"summary":"OPINT-003-D validation profile metadata"}""",
            CreatedAt = nowUtc,
            UpdatedAt = nowUtc
        });
        db.ConflictRecords.Add(new DbConflictRecord
        {
            Id = existingConflictId,
            ConflictType = Stage8RelatedConflictTypes.RecomputedContradiction,
            ObjectAType = "durable_object_metadata",
            ObjectAId = obsoleteMetadataId.ToString("D"),
            ObjectBType = Stage7DurableObjectFamilies.Profile,
            ObjectBId = obsoleteProfileObjectKey,
            Summary = "obsolete validation conflict",
            Severity = "high",
            Status = "open",
            CaseId = ChatId,
            ChatId = ChatId,
            LastActor = "validation_seed",
            LastReason = "seeded_for_opint_003_d_validation",
            CreatedAt = nowUtc,
            UpdatedAt = nowUtc
        });
        db.Stage8RecomputeQueueItems.AddRange(
            new DbStage8RecomputeQueueItem
            {
                Id = normalSourceQueueId,
                ScopeKey = ScopeKey,
                PersonId = trackedPersonId,
                TargetFamily = Stage8RecomputeTargetFamilies.DossierProfile,
                TargetRef = targetRef,
                DedupeKey = $"validation_source:{normalSourceQueueId:D}",
                ActiveDedupeKey = null,
                TriggerKind = "validation_seed",
                TriggerRef = "opint-003-d-normal-source",
                Status = Stage8RecomputeQueueStatuses.Completed,
                Priority = 20,
                AttemptCount = 1,
                MaxAttempts = 5,
                AvailableAtUtc = nowUtc,
                LastResultStatus = ModelPassResultStatuses.NeedMoreData,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc,
                CompletedAtUtc = nowUtc
            },
            new DbStage8RecomputeQueueItem
            {
                Id = clarificationSourceQueueId,
                ScopeKey = ScopeKey,
                PersonId = trackedPersonId,
                TargetFamily = Stage8RecomputeTargetFamilies.TimelineObjects,
                TargetRef = targetRef,
                DedupeKey = $"validation_source:{clarificationSourceQueueId:D}",
                ActiveDedupeKey = null,
                TriggerKind = "validation_seed",
                TriggerRef = "opint-003-d-clarification-source",
                Status = Stage8RecomputeQueueStatuses.Completed,
                Priority = 20,
                AttemptCount = 1,
                MaxAttempts = 5,
                AvailableAtUtc = nowUtc,
                LastResultStatus = ModelPassResultStatuses.NeedMoreData,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc,
                CompletedAtUtc = nowUtc
            });
        await db.SaveChangesAsync(ct);

        return new ValidationSeedState
        {
            TrackedPersonId = trackedPersonId,
            TrackedPersonDisplayName = SeedTrackedPersonDisplayName,
            MetadataId = metadataId,
            ExistingConflictId = existingConflictId,
            NormalSourceQueueId = normalSourceQueueId,
            ClarificationSourceQueueId = clarificationSourceQueueId,
            NormalSourceItemKey = BuildSourceItemKey(normalSourceQueueId),
            ClarificationSourceItemKey = BuildSourceItemKey(clarificationSourceQueueId)
        };
    }

    private static async Task<Opint003CleanupReport> CleanupValidationStateAsync(
        IDbContextFactory<TgAssistantDbContext> dbFactory,
        ValidationSeedState seedState,
        CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var conflictIds = seedState.CreatedConflictIds
            .Concat(seedState.ResolvedConflictIds)
            .Append(seedState.ExistingConflictId)
            .Distinct()
            .ToList();
        var conflictIdTexts = conflictIds.Select(id => id.ToString("D")).ToList();
        var queueIds = seedState.ActionQueueItemIds
            .Append(seedState.NormalSourceQueueId)
            .Append(seedState.ClarificationSourceQueueId)
            .Distinct()
            .ToList();
        var actionIds = seedState.ActionIds.Distinct().ToList();

        if (conflictIds.Count > 0)
        {
            await db.DomainReviewEvents
                .Where(x => x.ObjectType == "conflict_record" && conflictIdTexts.Contains(x.ObjectId))
                .ExecuteDeleteAsync(ct);
            await db.ConflictRecords
                .Where(x => conflictIds.Contains(x.Id))
                .ExecuteDeleteAsync(ct);
        }

        if (queueIds.Count > 0)
        {
            await db.Stage8RecomputeQueueItems
                .Where(x => queueIds.Contains(x.Id))
                .ExecuteDeleteAsync(ct);
        }

        if (actionIds.Count > 0)
        {
            await db.OperatorResolutionActions
                .Where(x => actionIds.Contains(x.Id))
                .ExecuteDeleteAsync(ct);
        }

        if (seedState.ActionRequestIds.Count > 0)
        {
            await db.OperatorAuditEvents
                .Where(x => seedState.ActionRequestIds.Contains(x.RequestId))
                .ExecuteDeleteAsync(ct);
        }

        await db.DurableObjectMetadata
            .Where(x => x.Id == seedState.MetadataId)
            .ExecuteDeleteAsync(ct);
        await db.PersonOperatorLinks
            .Where(x => x.PersonId == seedState.TrackedPersonId && x.ScopeKey == ScopeKey)
            .ExecuteDeleteAsync(ct);
        await db.Persons
            .Where(x => x.Id == seedState.TrackedPersonId)
            .ExecuteDeleteAsync(ct);

        var residualActions = actionIds.Count == 0
            ? 0
            : await db.OperatorResolutionActions.CountAsync(x => actionIds.Contains(x.Id), ct);
        var residualAudits = seedState.ActionRequestIds.Count == 0
            ? 0
            : await db.OperatorAuditEvents.CountAsync(x => seedState.ActionRequestIds.Contains(x.RequestId), ct);
        var residualQueues = queueIds.Count == 0
            ? 0
            : await db.Stage8RecomputeQueueItems.CountAsync(x => queueIds.Contains(x.Id), ct);
        var residualConflicts = conflictIds.Count == 0
            ? 0
            : await db.ConflictRecords.CountAsync(x => conflictIds.Contains(x.Id), ct);
        var residualEvents = conflictIds.Count == 0
            ? 0
            : await db.DomainReviewEvents.CountAsync(x => x.ObjectType == "conflict_record" && conflictIdTexts.Contains(x.ObjectId), ct);
        var residualMetadata = await db.DurableObjectMetadata.CountAsync(x => x.Id == seedState.MetadataId, ct);
        var residualTrackedPerson = await db.Persons.CountAsync(x => x.Id == seedState.TrackedPersonId, ct);

        return new Opint003CleanupReport
        {
            Completed = residualActions == 0
                        && residualAudits == 0
                        && residualQueues == 0
                        && residualConflicts == 0
                        && residualEvents == 0
                        && residualMetadata == 0
                        && residualTrackedPerson == 0,
            ResidualActionRows = residualActions,
            ResidualAuditRows = residualAudits,
            ResidualQueueRows = residualQueues,
            ResidualConflictRows = residualConflicts,
            ResidualDomainReviewEvents = residualEvents,
            ResidualMetadataRows = residualMetadata,
            ResidualTrackedPersonRows = residualTrackedPerson
        };
    }

    private static async Task<DbStage8RecomputeQueueItem> LeaseQueueItemAsync(
        IDbContextFactory<TgAssistantDbContext> dbFactory,
        Guid queueItemId,
        CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var nowUtc = DateTime.UtcNow;
        var leaseToken = Guid.NewGuid();
        var affected = await db.Database.ExecuteSqlInterpolatedAsync($"""
            update stage8_recompute_queue_items
            set status = {Stage8RecomputeQueueStatuses.Leased},
                attempt_count = attempt_count + 1,
                leased_until_utc = {nowUtc.AddMinutes(2)},
                lease_token = {leaseToken},
                updated_at_utc = {nowUtc}
            where id = {queueItemId}
              and status = {Stage8RecomputeQueueStatuses.Pending}
            """, ct);
        Ensure(affected == 1, $"Failed to lease validation queue item '{queueItemId:D}'.");

        return await db.Stage8RecomputeQueueItems
            .AsNoTracking()
            .FirstAsync(x => x.Id == queueItemId, ct);
    }

    private static async Task<DbOperatorResolutionAction> LoadActionRowAsync(
        IDbContextFactory<TgAssistantDbContext> dbFactory,
        Guid actionId,
        CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.OperatorResolutionActions
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == actionId, ct);
        return row ?? throw new InvalidOperationException($"Expected action row '{actionId:D}' was not persisted.");
    }

    private static async Task<int> CountDomainReviewEventsAsync(
        IDbContextFactory<TgAssistantDbContext> dbFactory,
        IReadOnlyCollection<Guid> conflictIds,
        CancellationToken ct)
    {
        if (conflictIds.Count == 0)
        {
            return 0;
        }

        var idTexts = conflictIds.Select(x => x.ToString("D")).ToList();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.DomainReviewEvents.CountAsync(
            x => x.ObjectType == "conflict_record" && idTexts.Contains(x.ObjectId),
            ct);
    }

    private static async Task<Opint003SchemaCheck> LoadSchemaCheckAsync(
        IDbContextFactory<TgAssistantDbContext> dbFactory,
        CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.Database.OpenConnectionAsync(ct);

        var migration0052 = await ExecuteScalarAsync(
            db.Database.GetDbConnection(),
            "select exists(select 1 from schema_migrations where id = '0052_operator_resolution_actions.sql');",
            ct);
        var migration0053 = await ExecuteScalarAsync(
            db.Database.GetDbConnection(),
            "select exists(select 1 from schema_migrations where id = '0053_operator_resolution_recompute_status.sql');",
            ct);

        return new Opint003SchemaCheck
        {
            Migration0052Present = IsTrueScalar(migration0052),
            Migration0053Present = IsTrueScalar(migration0053)
        };
    }

    private static async Task<string> ExecuteScalarAsync(DbConnection connection, string sql, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var result = await command.ExecuteScalarAsync(ct);
        return Convert.ToString(result) ?? string.Empty;
    }

    private static bool IsTrueScalar(string value)
        => string.Equals(value, "t", StringComparison.OrdinalIgnoreCase)
           || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    private static OperatorIdentityContext BuildIdentity(DateTime authTimeUtc)
    {
        return new OperatorIdentityContext
        {
            OperatorId = OperatorId,
            OperatorDisplay = OperatorDisplay,
            SurfaceSubject = SurfaceSubject,
            AuthSource = AuthSource,
            AuthTimeUtc = authTimeUtc
        };
    }

    private static OperatorSessionContext BuildSession(string sessionId, DateTime authTimeUtc, DateTime expiresAtUtc)
    {
        return new OperatorSessionContext
        {
            OperatorSessionId = sessionId,
            Surface = OperatorSurfaceTypes.Web,
            AuthenticatedAtUtc = authTimeUtc,
            LastSeenAtUtc = authTimeUtc,
            ExpiresAtUtc = expiresAtUtc,
            ActiveMode = OperatorModeTypes.ResolutionQueue
        };
    }

    private static int CountPassed(params Opint003ScenarioReport[] scenarios)
        => scenarios.Count(x => x.Passed);

    private static string BuildSourceItemKey(Guid queueItemId)
        => $"{ResolutionItemTypes.MissingData}:stage8_queue:{queueItemId:D}";

    private static string ResolveOutputPath(string? outputPath)
    {
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            return Path.GetFullPath(outputPath.Trim());
        }

        return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "logs", "opint-003-d-validation-report.json"));
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class ValidationSeedState
    {
        public Guid TrackedPersonId { get; init; }
        public string TrackedPersonDisplayName { get; init; } = string.Empty;
        public Guid MetadataId { get; init; }
        public Guid ExistingConflictId { get; init; }
        public Guid NormalSourceQueueId { get; init; }
        public Guid ClarificationSourceQueueId { get; init; }
        public string NormalSourceItemKey { get; init; } = string.Empty;
        public string ClarificationSourceItemKey { get; init; } = string.Empty;
        public List<Guid> ActionIds { get; } = [];
        public List<Guid> ActionQueueItemIds { get; } = [];
        public List<string> ActionRequestIds { get; } = [];
        public List<Guid> CreatedConflictIds { get; } = [];
        public List<Guid> ResolvedConflictIds { get; } = [];
    }
}

public sealed class Opint003LoopValidationReport
{
    public DateTime GeneratedAtUtc { get; set; }
    public string OutputPath { get; set; } = string.Empty;
    public string ScopeKey { get; set; } = string.Empty;
    public long ChatId { get; set; }
    public bool AllChecksPassed { get; set; }
    public string? FatalError { get; set; }
    public Opint003SchemaCheck SchemaCheck { get; set; } = new();
    public Opint003TrackedPersonReport TrackedPerson { get; set; } = new();
    public Opint003ContractChecks ContractChecks { get; set; } = new();
    public Opint003ScenarioReport NormalScenario { get; set; } = new();
    public Opint003ScenarioReport ClarificationScenario { get; set; } = new();
    public Opint003KeyMetrics KeyMetrics { get; set; } = new();
    public Opint003CleanupReport Cleanup { get; set; } = new();
}

public sealed class Opint003SchemaCheck
{
    public bool Migration0052Present { get; set; }
    public bool Migration0053Present { get; set; }
    public bool Migrations0052And0053Present => Migration0052Present && Migration0053Present;
}

public sealed class Opint003TrackedPersonReport
{
    public Guid TrackedPersonId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
}

public sealed class Opint003ContractChecks
{
    public bool TrackedPersonQueryAccepted { get; set; }
    public bool TrackedPersonSelectionAccepted { get; set; }
    public bool ResolutionQueueAccepted { get; set; }
    public int QueueVisibleItemCount { get; set; }
}

public sealed class Opint003ScenarioReport
{
    public string Scenario { get; set; } = string.Empty;
    public bool Passed { get; set; }
    public string SourceItemKey { get; set; } = string.Empty;
    public bool DetailAccepted { get; set; }
    public bool ActionAccepted { get; set; }
    public bool ReplayAccepted { get; set; }
    public Guid? ActionId { get; set; }
    public Guid? QueueItemId { get; set; }
    public string? InitialLifecycleStatus { get; set; }
    public string? InitialTargetLifecycleStatus { get; set; }
    public string? ActionRowLifecycleStatus { get; set; }
    public string? ActionRowLastResultStatus { get; set; }
    public DateTime? ActionRowCompletedAtUtc { get; set; }
    public string? ReplayLifecycleStatus { get; set; }
    public string? ReplayTargetLifecycleStatus { get; set; }
    public string? ReplayLastResultStatus { get; set; }
    public string? ReplayFailureReason { get; set; }
    public bool RelatedConflictApplied { get; set; }
    public string? RelatedConflictSkipReason { get; set; }
    public int RelatedConflictCreatedCount { get; set; }
    public int RelatedConflictResolvedCount { get; set; }
    public int RelatedConflictRefreshedCount { get; set; }
    public int RelatedConflictUnchangedCount { get; set; }
    public int RelatedConflictDomainReviewEventCount { get; set; }
    public List<string> RelatedConflictActiveConflictIds { get; set; } = [];
    public List<string> RelatedConflictResolvedConflictIds { get; set; } = [];
}

public sealed class Opint003KeyMetrics
{
    public int ScenariosPassed { get; set; }
    public int ScenariosTotal { get; set; }
    public int RelatedConflictCreated { get; set; }
    public int RelatedConflictResolved { get; set; }
    public int RelatedConflictDomainReviewEvents { get; set; }
}

public sealed class Opint003CleanupReport
{
    public bool Completed { get; set; }
    public int ResidualActionRows { get; set; }
    public int ResidualAuditRows { get; set; }
    public int ResidualQueueRows { get; set; }
    public int ResidualConflictRows { get; set; }
    public int ResidualDomainReviewEvents { get; set; }
    public int ResidualMetadataRows { get; set; }
    public int ResidualTrackedPersonRows { get; set; }
}
