using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Host.Launch;

public static class WorkspaceSummarySnapshotValidationRunner
{
    private const string ScopeKey = "chat:885574984";
    private const long ChatId = 885574984;
    private const string OperatorId = "workspace-summary-snapshot-validator";
    private const string OperatorDisplay = "Workspace Summary Snapshot Validator";
    private const string SurfaceSubject = "workspace_summary_snapshot_validation";
    private const string AuthSource = "local_runtime_validation";

    public static async Task<WorkspaceSummarySnapshotValidationReport> RunAsync(
        IServiceProvider services,
        string? outputPath = null,
        CancellationToken ct = default)
    {
        var resolvedOutputPath = ResolveOutputPath(outputPath);
        var applicationService = services.GetRequiredService<IOperatorResolutionApplicationService>();

        var report = new WorkspaceSummarySnapshotValidationReport
        {
            GeneratedAtUtc = DateTime.UtcNow,
            OutputPath = resolvedOutputPath,
            ScopeKey = ScopeKey,
            ChatId = ChatId
        };

        Exception? fatal = null;
        try
        {
            var authTimeUtc = DateTime.UtcNow;
            var identity = BuildIdentity(authTimeUtc);
            var session = BuildSession($"workspace-summary-snapshot-{Guid.NewGuid():N}", authTimeUtc, authTimeUtc.AddMinutes(30));

            var trackedPersonQuery = await applicationService.QueryTrackedPersonsAsync(
                new OperatorTrackedPersonQueryRequest
                {
                    OperatorIdentity = identity,
                    Session = session,
                    Limit = 50
                },
                ct);
            Ensure(trackedPersonQuery.Accepted, $"Tracked-person query failed: {trackedPersonQuery.FailureReason ?? "unknown"}");

            var trackedPerson = trackedPersonQuery.TrackedPersons
                .FirstOrDefault(x => string.Equals(x.ScopeKey, ScopeKey, StringComparison.Ordinal))
                ?? throw new InvalidOperationException($"Tracked-person query did not return bounded scope '{ScopeKey}'.");

            report.TrackedPerson = new WorkspaceSummarySnapshotTrackedPersonReport
            {
                TrackedPersonId = trackedPerson.TrackedPersonId,
                DisplayName = trackedPerson.DisplayName,
                ScopeKey = trackedPerson.ScopeKey,
                EvidenceCount = trackedPerson.EvidenceCount,
                UnresolvedCount = trackedPerson.UnresolvedCount
            };

            var trackedPersonSelection = await applicationService.SelectTrackedPersonAsync(
                new OperatorTrackedPersonSelectionRequest
                {
                    OperatorIdentity = identity,
                    Session = trackedPersonQuery.Session,
                    TrackedPersonId = trackedPerson.TrackedPersonId,
                    RequestedAtUtc = authTimeUtc
                },
                ct);
            Ensure(trackedPersonSelection.Accepted, $"Tracked-person selection failed: {trackedPersonSelection.FailureReason ?? "unknown"}");

            var summaryResult = await applicationService.QueryPersonWorkspaceSummaryAsync(
                new OperatorPersonWorkspaceSummaryQueryRequest
                {
                    OperatorIdentity = identity,
                    Session = trackedPersonSelection.Session,
                    TrackedPersonId = trackedPerson.TrackedPersonId
                },
                ct);
            Ensure(summaryResult.Accepted, $"Workspace summary query failed: {summaryResult.FailureReason ?? "unknown"}");

            var summary = summaryResult.Workspace.Summary;
            var snapshot = summary.Snapshot ?? throw new InvalidOperationException("Workspace summary snapshot was null.");
            Ensure(summaryResult.Session.ActiveTrackedPersonId == trackedPerson.TrackedPersonId, "Workspace summary did not retain active tracked-person session context.");
            Ensure(string.IsNullOrWhiteSpace(summaryResult.Session.ActiveScopeItemKey), "Workspace summary should clear active scope item context.");
            Ensure(
                string.Equals(summaryResult.Session.ActiveMode, OperatorModeTypes.ResolutionQueue, StringComparison.Ordinal),
                $"Workspace summary should normalize active mode to '{OperatorModeTypes.ResolutionQueue}'.");

            Ensure(snapshot.TrackedPerson.TrackedPersonId == trackedPerson.TrackedPersonId, "Snapshot tracked-person id mismatch.");
            Ensure(string.Equals(snapshot.TrackedPerson.ScopeKey, ScopeKey, StringComparison.Ordinal), "Snapshot tracked-person scope mismatch.");
            Ensure(HasBoundedValue(snapshot.TrackedPerson.DisplayName), "Snapshot tracked-person display name degraded to placeholder.");
            Ensure(HasBoundedValue(snapshot.Operator.OperatorSessionId), "Snapshot operator session id degraded to placeholder.");
            Ensure(
                string.Equals(snapshot.Operator.Surface, OperatorSurfaceTypes.Web, StringComparison.Ordinal),
                $"Snapshot operator surface should remain '{OperatorSurfaceTypes.Web}'.");
            Ensure(
                string.Equals(snapshot.Operator.ActiveMode, OperatorModeTypes.ResolutionQueue, StringComparison.Ordinal),
                $"Snapshot operator active mode should remain '{OperatorModeTypes.ResolutionQueue}'.");

            if (snapshot.Pair.Available)
            {
                Ensure(snapshot.Pair.ObjectCount > 0, "Available pair snapshot must expose at least one durable object.");
                Ensure(HasBoundedValue(snapshot.Pair.Family), "Available pair snapshot family degraded to placeholder.");
                Ensure(HasBoundedValue(snapshot.Pair.Label), "Available pair snapshot label degraded to placeholder.");
            }
            else
            {
                Ensure(snapshot.Pair.ObjectCount == 0, "Unavailable pair snapshot should not report durable objects.");
                Ensure(string.IsNullOrWhiteSpace(snapshot.Pair.LatestSummary), "Unavailable pair snapshot should not report a summary.");
            }

            report.SuccessPath = new WorkspaceSummarySnapshotSuccessPathReport
            {
                TrackedPersonQueryAccepted = trackedPersonQuery.Accepted,
                TrackedPersonSelectionAccepted = trackedPersonSelection.Accepted,
                SummaryAccepted = summaryResult.Accepted,
                DurableObjectCount = summary.DurableObjectCount,
                SummaryUnresolvedCount = summary.UnresolvedCount,
                PairAvailable = snapshot.Pair.Available,
                OperatorSurface = snapshot.Operator.Surface,
                ActiveMode = snapshot.Operator.ActiveMode
            };

            var expiredResult = await applicationService.QueryPersonWorkspaceSummaryAsync(
                new OperatorPersonWorkspaceSummaryQueryRequest
                {
                    OperatorIdentity = identity,
                    Session = BuildExpiredSession(summaryResult.Session, DateTime.UtcNow),
                    TrackedPersonId = trackedPerson.TrackedPersonId
                },
                ct);
            Ensure(!expiredResult.Accepted, "Expired-session workspace summary query should be rejected.");
            Ensure(
                string.Equals(expiredResult.FailureReason, "session_expired", StringComparison.Ordinal),
                $"Expired-session workspace summary query returned unexpected failure: {expiredResult.FailureReason ?? "unknown"}");

            report.FailurePath = new WorkspaceSummarySnapshotFailurePathReport
            {
                ExpiredSessionRejected = !expiredResult.Accepted,
                FailureReason = expiredResult.FailureReason
            };

            report.AllChecksPassed = true;
        }
        catch (Exception ex)
        {
            fatal = ex;
            report.AllChecksPassed = false;
            report.FatalError = ex.Message;
        }
        finally
        {
            Directory.CreateDirectory(Path.GetDirectoryName(resolvedOutputPath)!);
            var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(resolvedOutputPath, json, ct);
        }

        if (!report.AllChecksPassed)
        {
            throw new InvalidOperationException(
                "Workspace summary snapshot validation failed: bounded workspace summary snapshot evidence is incomplete.",
                fatal);
        }

        return report;
    }

    private static string ResolveOutputPath(string? outputPath)
    {
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            return Path.GetFullPath(outputPath);
        }

        return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "logs", "workspace-summary-snapshot-validation-report.json"));
    }

    private static OperatorIdentityContext BuildIdentity(DateTime authTimeUtc)
        => new()
        {
            OperatorId = OperatorId,
            OperatorDisplay = OperatorDisplay,
            SurfaceSubject = SurfaceSubject,
            AuthSource = AuthSource,
            AuthTimeUtc = authTimeUtc
        };

    private static OperatorSessionContext BuildSession(string sessionId, DateTime authTimeUtc, DateTime? expiresAtUtc)
        => new()
        {
            OperatorSessionId = sessionId,
            Surface = OperatorSurfaceTypes.Web,
            AuthenticatedAtUtc = authTimeUtc,
            LastSeenAtUtc = authTimeUtc,
            ExpiresAtUtc = expiresAtUtc,
            ActiveMode = OperatorModeTypes.ResolutionQueue
        };

    private static OperatorSessionContext BuildExpiredSession(OperatorSessionContext session, DateTime nowUtc)
        => new()
        {
            OperatorSessionId = session.OperatorSessionId,
            Surface = session.Surface,
            AuthenticatedAtUtc = session.AuthenticatedAtUtc,
            LastSeenAtUtc = nowUtc,
            ExpiresAtUtc = nowUtc.AddSeconds(-1),
            ActiveTrackedPersonId = session.ActiveTrackedPersonId,
            ActiveScopeItemKey = session.ActiveScopeItemKey,
            ActiveMode = session.ActiveMode,
            UnfinishedStep = session.UnfinishedStep == null
                ? null
                : new OperatorWorkflowStepContext
                {
                    StepKind = session.UnfinishedStep.StepKind,
                    StepState = session.UnfinishedStep.StepState,
                    StartedAtUtc = session.UnfinishedStep.StartedAtUtc,
                    BoundTrackedPersonId = session.UnfinishedStep.BoundTrackedPersonId,
                    BoundScopeItemKey = session.UnfinishedStep.BoundScopeItemKey
                }
        };

    private static bool HasBoundedValue(string? value)
        => !string.IsNullOrWhiteSpace(value)
           && !string.Equals(value.Trim(), "unknown", StringComparison.OrdinalIgnoreCase);

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}

public sealed class WorkspaceSummarySnapshotValidationReport
{
    public DateTime GeneratedAtUtc { get; set; }
    public string OutputPath { get; set; } = string.Empty;
    public string ScopeKey { get; set; } = string.Empty;
    public long ChatId { get; set; }
    public bool AllChecksPassed { get; set; }
    public string? FatalError { get; set; }
    public WorkspaceSummarySnapshotTrackedPersonReport TrackedPerson { get; set; } = new();
    public WorkspaceSummarySnapshotSuccessPathReport SuccessPath { get; set; } = new();
    public WorkspaceSummarySnapshotFailurePathReport FailurePath { get; set; } = new();
}

public sealed class WorkspaceSummarySnapshotTrackedPersonReport
{
    public Guid TrackedPersonId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string ScopeKey { get; set; } = string.Empty;
    public int EvidenceCount { get; set; }
    public int UnresolvedCount { get; set; }
}

public sealed class WorkspaceSummarySnapshotSuccessPathReport
{
    public bool TrackedPersonQueryAccepted { get; set; }
    public bool TrackedPersonSelectionAccepted { get; set; }
    public bool SummaryAccepted { get; set; }
    public int DurableObjectCount { get; set; }
    public int SummaryUnresolvedCount { get; set; }
    public bool PairAvailable { get; set; }
    public string OperatorSurface { get; set; } = string.Empty;
    public string ActiveMode { get; set; } = string.Empty;
}

public sealed class WorkspaceSummarySnapshotFailurePathReport
{
    public bool ExpiredSessionRejected { get; set; }
    public string? FailureReason { get; set; }
}
