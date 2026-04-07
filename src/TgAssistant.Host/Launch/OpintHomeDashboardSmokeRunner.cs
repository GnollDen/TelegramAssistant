using System.Reflection;
using System.Text.Json;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Models;
using TgAssistant.Host.OperatorApi;
using TgAssistant.Host.OperatorWeb;

namespace TgAssistant.Host.Launch;

public static class OpintHomeDashboardSmokeRunner
{
    private static readonly IReadOnlyList<string> RequiredTopLevelFields =
    [
        "navigationCounts",
        "systemStatus",
        "criticalUnresolvedCount",
        "activeTrackedPersonCount",
        "recentUpdates",
        "degradedSources"
    ];

    private static readonly IReadOnlyList<string> RequiredHomeMarkers =
    [
        "home-system-status",
        "home-critical-unresolved",
        "home-active-persons",
        "home-recent-updates"
    ];

    private static readonly IReadOnlyList<string> RequiredCountBadgeIds =
    [
        "home-nav-count-resolution",
        "home-nav-count-persons",
        "home-nav-count-alerts",
        "home-nav-count-offline-events"
    ];

    public static async Task<OpintHomeDashboardSmokeReport> RunAsync(
        string? outputPath = null,
        CancellationToken ct = default)
    {
        var resolvedOutputPath = ResolveOutputPath(outputPath);
        var report = new OpintHomeDashboardSmokeReport
        {
            GeneratedAtUtc = DateTime.UtcNow,
            OutputPath = resolvedOutputPath,
            RequiredTopLevelFields = RequiredTopLevelFields.ToList(),
            ExpectedDegradedSourcesOrder = OperatorHomeSummaryDegradedSources.FullOrder.ToList(),
            ExpectedHomeMarkers = RequiredHomeMarkers.ToList(),
            PartialFailureStatus = "not_exercised"
        };

        Exception? fatal = null;
        try
        {
            VerifyApiContractShape(report);
            VerifyForcedDegradedContract(report);
            VerifyDegradedSourcesOrder(report);
            VerifyTargetUrlAllowList(report);
            VerifyHomeWebContract(report);
            VerifyPartialFailureStatus(report);

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
                "OPINT home dashboard smoke failed: API/web/degraded contract regressed.",
                fatal);
        }

        return report;
    }

    private static void VerifyApiContractShape(OpintHomeDashboardSmokeReport report)
    {
        var payload = new OperatorHomeSummaryApiResponse
        {
            NavigationCounts = new OperatorHomeSummaryNavigationCounts
            {
                Resolution = 3,
                Persons = 2,
                Alerts = 1,
                OfflineEvents = 4
            },
            SystemStatus = OperatorHomeSummarySystemStatuses.Normal,
            CriticalUnresolvedCount = 1,
            ActiveTrackedPersonCount = 2,
            RecentUpdates =
            [
                new OperatorHomeSummaryRecentUpdate
                {
                    Id = "alert:sample",
                    OccurredAtUtc = DateTime.UnixEpoch,
                    Summary = "Sample update",
                    TargetUrl = "/operator"
                }
            ],
            DegradedSources = []
        };

        using var document = SerializeAndParse(payload);
        var root = document.RootElement;
        Ensure(root.ValueKind == JsonValueKind.Object, "OPINT home dashboard smoke failed: summary payload did not serialize as JSON object.");

        var actualTopLevelFields = root.EnumerateObject().Select(x => x.Name).ToList();
        report.ActualTopLevelFields = actualTopLevelFields;

        Ensure(actualTopLevelFields.Count == RequiredTopLevelFields.Count,
            $"OPINT home dashboard smoke failed: expected exactly {RequiredTopLevelFields.Count} top-level fields but saw {actualTopLevelFields.Count}.");
        Ensure(RequiredTopLevelFields.All(actualTopLevelFields.Contains),
            "OPINT home dashboard smoke failed: summary payload is missing one or more required top-level fields.");

        var navigationCounts = root.GetProperty("navigationCounts");
        Ensure(navigationCounts.ValueKind == JsonValueKind.Object,
            "OPINT home dashboard smoke failed: navigationCounts should be a bounded object in happy-path shape validation.");
        Ensure(navigationCounts.TryGetProperty("resolution", out _), "OPINT home dashboard smoke failed: navigationCounts.resolution is missing.");
        Ensure(navigationCounts.TryGetProperty("persons", out _), "OPINT home dashboard smoke failed: navigationCounts.persons is missing.");
        Ensure(navigationCounts.TryGetProperty("alerts", out _), "OPINT home dashboard smoke failed: navigationCounts.alerts is missing.");
        Ensure(navigationCounts.TryGetProperty("offlineEvents", out _), "OPINT home dashboard smoke failed: navigationCounts.offlineEvents is missing.");

        var recentUpdate = root.GetProperty("recentUpdates")[0];
        Ensure(recentUpdate.TryGetProperty("id", out _), "OPINT home dashboard smoke failed: recentUpdates[*].id is missing.");
        Ensure(recentUpdate.TryGetProperty("occurredAtUtc", out _), "OPINT home dashboard smoke failed: recentUpdates[*].occurredAtUtc is missing.");
        Ensure(recentUpdate.TryGetProperty("summary", out _), "OPINT home dashboard smoke failed: recentUpdates[*].summary is missing.");
        Ensure(recentUpdate.TryGetProperty("targetUrl", out _), "OPINT home dashboard smoke failed: recentUpdates[*].targetUrl is missing.");

        report.ApiShapeValidated = true;
    }

    private static void VerifyForcedDegradedContract(OpintHomeDashboardSmokeReport report)
    {
        var forcedDegradedPayload = new OperatorHomeSummaryApiResponse
        {
            NavigationCounts = null,
            SystemStatus = null,
            CriticalUnresolvedCount = null,
            ActiveTrackedPersonCount = null,
            RecentUpdates = null,
            DegradedSources = OperatorHomeSummaryDegradedSources.FullOrder.ToList()
        };

        using var document = SerializeAndParse(forcedDegradedPayload);
        var root = document.RootElement;
        Ensure(root.ValueKind == JsonValueKind.Object,
            "OPINT home dashboard smoke failed: forced degraded payload did not serialize as JSON object.");

        Ensure(root.GetProperty("navigationCounts").ValueKind == JsonValueKind.Null,
            "OPINT home dashboard smoke failed: forced degraded contract expects navigationCounts=null.");
        Ensure(root.GetProperty("systemStatus").ValueKind == JsonValueKind.Null,
            "OPINT home dashboard smoke failed: forced degraded contract expects systemStatus=null.");
        Ensure(root.GetProperty("criticalUnresolvedCount").ValueKind == JsonValueKind.Null,
            "OPINT home dashboard smoke failed: forced degraded contract expects criticalUnresolvedCount=null.");
        Ensure(root.GetProperty("activeTrackedPersonCount").ValueKind == JsonValueKind.Null,
            "OPINT home dashboard smoke failed: forced degraded contract expects activeTrackedPersonCount=null.");
        Ensure(root.GetProperty("recentUpdates").ValueKind == JsonValueKind.Null,
            "OPINT home dashboard smoke failed: forced degraded contract expects recentUpdates=null.");

        var degradedSources = root.GetProperty("degradedSources")
            .EnumerateArray()
            .Select(x => x.GetString())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Cast<string>()
            .ToList();

        report.ForcedDegradedActualSources = degradedSources;
        Ensure(degradedSources.SequenceEqual(OperatorHomeSummaryDegradedSources.FullOrder),
            "OPINT home dashboard smoke failed: forced degraded contract requires exact ordered degradedSources full list.");

        report.ForcedDegradedContractValidated = true;
    }

    private static void VerifyDegradedSourcesOrder(OpintHomeDashboardSmokeReport report)
    {
        var expected = new[]
        {
            OperatorHomeSummaryDegradedSources.NavigationCounts,
            OperatorHomeSummaryDegradedSources.SystemStatus,
            OperatorHomeSummaryDegradedSources.CriticalUnresolvedCount,
            OperatorHomeSummaryDegradedSources.ActiveTrackedPersonCount,
            OperatorHomeSummaryDegradedSources.RecentUpdates
        };

        Ensure(OperatorHomeSummaryDegradedSources.FullOrder.SequenceEqual(expected),
            "OPINT home dashboard smoke failed: degradedSources full-order contract mismatch.");

        report.DegradedSourcesOrderValidated = true;
    }

    private static void VerifyTargetUrlAllowList(OpintHomeDashboardSmokeReport report)
    {
        var allowListMethod = typeof(OperatorApiEndpointExtensions)
            .GetMethod("IsAllowedHomeTargetUrl", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("OPINT home dashboard smoke failed: target-url allow-list validator method not found.");

        var trackedPersonId = "99999999-aaaa-bbbb-cccc-111111111111";
        var acceptedCases = new[]
        {
            "/operator",
            "/operator/resolution",
            "/operator/persons",
            "/operator/alerts",
            "/operator/offline-events",
            $"/operator/person-workspace?trackedPersonId={trackedPersonId}",
            $"/operator/resolution?trackedPersonId={trackedPersonId}&scopeItemKey=scope:abc&activeMode=resolution_queue",
            $"/operator/resolution?trackedPersonId={trackedPersonId}&scopeItemKey=scope:abc&activeMode=resolution_detail",
            $"/operator/resolution?trackedPersonId={trackedPersonId}&scopeItemKey=scope:abc&activeMode=assistant"
        };

        var rejectedCases = new[]
        {
            "",
            "https://example.com/operator",
            "/operator?unexpected=1",
            "/operator/offline-events?unexpected=1",
            "/operator/person-workspace",
            "/operator/person-workspace?trackedPersonId=not-a-guid",
            $"/operator/person-workspace?trackedPersonId={trackedPersonId}&unexpected=1",
            $"/operator/resolution?trackedPersonId={trackedPersonId}",
            $"/operator/resolution?trackedPersonId={trackedPersonId}&scopeItemKey=scope:abc&activeMode=invalid",
            $"/operator/resolution?trackedPersonId={trackedPersonId}&scopeItemKey=&activeMode=assistant",
            "/operator/resolution?trackedPersonId=not-a-guid&scopeItemKey=scope:abc&activeMode=assistant",
            "/operator/unknown"
        };

        foreach (var value in acceptedCases)
        {
            var allowed = InvokeAllowListMethod(allowListMethod, value);
            report.TargetUrlAllowedChecks.Add(new OpintHomeDashboardTargetUrlCheck { Url = value, ExpectedAllowed = true, ActualAllowed = allowed });
            Ensure(allowed, $"OPINT home dashboard smoke failed: approved target URL was rejected: {value}");
        }

        foreach (var value in rejectedCases)
        {
            var allowed = InvokeAllowListMethod(allowListMethod, value);
            report.TargetUrlRejectedChecks.Add(new OpintHomeDashboardTargetUrlCheck { Url = value, ExpectedAllowed = false, ActualAllowed = allowed });
            Ensure(!allowed, $"OPINT home dashboard smoke failed: unapproved target URL was accepted: {value}");
        }

        report.TargetUrlAllowListValidated = true;
    }

    private static void VerifyHomeWebContract(OpintHomeDashboardSmokeReport report)
    {
        var html = typeof(OperatorWebEndpointExtensions)
            .GetField("OperatorHomeHtml", BindingFlags.NonPublic | BindingFlags.Static)
            ?.GetRawConstantValue() as string;

        if (string.IsNullOrWhiteSpace(html))
        {
            throw new InvalidOperationException(
                "OPINT home dashboard smoke failed: operator home shell HTML constant not found.");
        }

        foreach (var marker in RequiredHomeMarkers)
        {
            Ensure(html.Contains($"id=\"{marker}\"", StringComparison.Ordinal),
                $"OPINT home dashboard smoke failed: required home marker missing: {marker}");
        }

        Ensure(html.Contains("id=\"home-nav-assistant\"", StringComparison.Ordinal),
            "OPINT home dashboard smoke failed: assistant navigation button missing.");

        foreach (var badgeId in RequiredCountBadgeIds)
        {
            Ensure(html.Contains($"id=\"{badgeId}\"", StringComparison.Ordinal),
                $"OPINT home dashboard smoke failed: required navigation count badge missing: {badgeId}");
        }

        Ensure(!html.Contains("id=\"home-nav-count-assistant\"", StringComparison.Ordinal),
            "OPINT home dashboard smoke failed: assistant navigation must not require its own count badge.");

        Ensure(html.Contains("fetch(\"/api/operator/home/summary\"", StringComparison.Ordinal),
            "OPINT home dashboard smoke failed: operator home shell no longer fetches /api/operator/home/summary.");

        Ensure(html.Contains("setContainerState(isDegraded ? \"degraded\" : \"live\")", StringComparison.Ordinal),
            "OPINT home dashboard smoke failed: happy/degraded container state transition contract missing.");

        Ensure(html.Contains("setContainerState(degradedSet.size > 0 ? \"degraded\" : \"live\")", StringComparison.Ordinal),
            "OPINT home dashboard smoke failed: degraded fallback container-state contract missing.");

        Ensure(html.Contains("applyDegradedFallback(true, degradedOrder)", StringComparison.Ordinal),
            "OPINT home dashboard smoke failed: forced degraded fallback invocation missing for fetch failure path.");

        Ensure(html.Contains("Summary is partially degraded. Navigation is still available.", StringComparison.Ordinal),
            "OPINT home dashboard smoke failed: degraded-summary status copy contract missing.");

        Ensure(html.Contains("Summary fetch failed. Navigation is still available.", StringComparison.Ordinal),
            "OPINT home dashboard smoke failed: fetch-failure degraded status copy contract missing.");

        report.HomeWebContractValidated = true;
    }

    private static void VerifyPartialFailureStatus(OpintHomeDashboardSmokeReport report)
    {
        report.PartialFailureDeterministicInjectionAvailable = false;
        report.PartialFailureStatus = "not_exercised";
        report.PartialFailureStatusNote =
            $"No deterministic partial-failure injection setting exists under {OperatorHomeSummarySettings.Section}; status recorded as not_exercised per DTP-015B preflight.";
        report.PartialFailureContractStatusValidated = true;
    }

    private static JsonDocument SerializeAndParse<T>(T payload)
    {
        var serialized = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        return JsonDocument.Parse(serialized);
    }

    private static bool InvokeAllowListMethod(MethodInfo method, string? targetUrl)
        => (bool)(method.Invoke(null, [targetUrl]) ?? false);

    private static string ResolveOutputPath(string? outputPath)
    {
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            return Path.GetFullPath(outputPath);
        }

        return Path.GetFullPath(Path.Combine(
            Directory.GetCurrentDirectory(),
            "artifacts",
            "operator-home",
            "opint-home-dashboard-smoke.json"));
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}

public sealed class OpintHomeDashboardSmokeReport
{
    public DateTime GeneratedAtUtc { get; set; }
    public string OutputPath { get; set; } = string.Empty;
    public bool AllChecksPassed { get; set; }
    public string? FatalError { get; set; }
    public bool ApiShapeValidated { get; set; }
    public bool ForcedDegradedContractValidated { get; set; }
    public bool DegradedSourcesOrderValidated { get; set; }
    public bool TargetUrlAllowListValidated { get; set; }
    public bool HomeWebContractValidated { get; set; }
    public bool PartialFailureContractStatusValidated { get; set; }
    public bool PartialFailureDeterministicInjectionAvailable { get; set; }
    public string PartialFailureStatus { get; set; } = "not_exercised";
    public string? PartialFailureStatusNote { get; set; }
    public List<string> RequiredTopLevelFields { get; set; } = [];
    public List<string> ActualTopLevelFields { get; set; } = [];
    public List<string> ExpectedDegradedSourcesOrder { get; set; } = [];
    public List<string> ForcedDegradedActualSources { get; set; } = [];
    public List<string> ExpectedHomeMarkers { get; set; } = [];
    public List<OpintHomeDashboardTargetUrlCheck> TargetUrlAllowedChecks { get; set; } = [];
    public List<OpintHomeDashboardTargetUrlCheck> TargetUrlRejectedChecks { get; set; } = [];
}

public sealed class OpintHomeDashboardTargetUrlCheck
{
    public string? Url { get; set; }
    public bool ExpectedAllowed { get; set; }
    public bool ActualAllowed { get; set; }
}
