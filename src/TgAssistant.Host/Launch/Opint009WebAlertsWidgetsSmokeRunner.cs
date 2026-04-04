using System.Text.Json;
using TgAssistant.Core.Models;
using TgAssistant.Host.OperatorApi;
using TgAssistant.Host.OperatorWeb;

namespace TgAssistant.Host.Launch;

public static class Opint009WebAlertsWidgetsSmokeRunner
{
    public static async Task<Opint009WebAlertsWidgetsSmokeReport> RunAsync(
        string? outputPath = null,
        CancellationToken ct = default)
    {
        var resolvedOutputPath = ResolveOutputPath(outputPath);
        var report = new Opint009WebAlertsWidgetsSmokeReport
        {
            GeneratedAtUtc = DateTime.UtcNow,
            OutputPath = resolvedOutputPath
        };

        Exception? fatal = null;
        try
        {
            var projectionBuilder = Opint009WebAlertsSmokeRunner.CreateProjectionBuilder();
            var identity = Opint009WebAlertsSmokeRunner.CreateIdentityContext("opint-009-c2-operator", "OPINT-009-C2 Operator", "opint-009-c2-smoke");
            var session = Opint009WebAlertsSmokeRunner.CreateSessionContext("web:opint009c2");

            var allResult = await projectionBuilder.BuildAsync(
                new OperatorAlertsQueryRequest
                {
                    EscalationBoundary = OperatorAlertsEscalationFilters.All,
                    PersonLimit = 10,
                    AlertsPerPersonLimit = 6
                },
                identity,
                session,
                ct);
            Ensure(allResult.Accepted, "OPINT-009-C2 smoke failed: baseline alerts query should be accepted.");
            Ensure(allResult.Summary.RequiresAcknowledgementCount == 0, "OPINT-009-C2 smoke failed: unscoped baseline should not require acknowledgement.");
            Ensure(allResult.Summary.EnterResolutionCount == 3, "OPINT-009-C2 smoke failed: unscoped baseline should retain three enter-resolution alerts.");
            Ensure(allResult.Summary.TopReasons.Count == 1, "OPINT-009-C2 smoke failed: baseline should collapse to one top reason facet.");
            Ensure(string.Equals(allResult.Summary.TopReasons[0].Key, OperatorAlertRuleIds.CriticalWorkflowBlockerWebOnly, StringComparison.Ordinal), "OPINT-009-C2 smoke failed: wrong baseline top reason facet.");
            Ensure(string.Equals(allResult.Summary.TopReasons[0].AlertsUrl, "/operator/alerts?search=critical_workflow_blocker_web_only", StringComparison.Ordinal), "OPINT-009-C2 smoke failed: baseline reason facet should link back into bounded alerts.");
            Ensure(allResult.Summary.BoundaryBreakdown.Any(facet => string.Equals(facet.Key, OperatorAlertEscalationBoundaries.WebOnly, StringComparison.Ordinal) && facet.Count == 3), "OPINT-009-C2 smoke failed: baseline boundary breakdown should retain three web-only alerts.");

            var trackedPersonResult = await projectionBuilder.BuildAsync(
                new OperatorAlertsQueryRequest
                {
                    TrackedPersonId = Opint009WebAlertsSmokeRunner.PrimaryTrackedPersonId,
                    EscalationBoundary = OperatorAlertsEscalationFilters.All,
                    PersonLimit = 10,
                    AlertsPerPersonLimit = 6
                },
                identity,
                session,
                ct);
            Ensure(trackedPersonResult.Accepted, "OPINT-009-C2 smoke failed: tracked-person widget query should be accepted.");
            Ensure(trackedPersonResult.Summary.RequiresAcknowledgementCount == 1, "OPINT-009-C2 smoke failed: active tracked-person scope should report one acknowledgement-required alert.");
            Ensure(trackedPersonResult.Summary.EnterResolutionCount == 2, "OPINT-009-C2 smoke failed: active tracked-person scope should report two resolution-entry alerts.");
            Ensure(trackedPersonResult.Summary.BoundaryBreakdown.Any(facet => string.Equals(facet.Key, OperatorAlertEscalationBoundaries.TelegramPushAcknowledge, StringComparison.Ordinal) && facet.Count == 1), "OPINT-009-C2 smoke failed: active scope should report one telegram+acknowledge boundary.");
            Ensure(trackedPersonResult.Summary.BoundaryBreakdown.Any(facet => string.Equals(facet.Key, OperatorAlertEscalationBoundaries.WebOnly, StringComparison.Ordinal) && facet.Count == 1), "OPINT-009-C2 smoke failed: active scope should retain one web-only boundary.");
            Ensure(trackedPersonResult.Summary.TopReasons.Any(facet => string.Equals(facet.Key, OperatorAlertRuleIds.CriticalClarificationBlock, StringComparison.Ordinal)), "OPINT-009-C2 smoke failed: active scope should include the clarification-block reason facet.");
            Ensure(trackedPersonResult.Groups.SelectMany(group => group.Alerts).Any(alert => alert.RequiresAcknowledgement && alert.EnterResolutionContext), "OPINT-009-C2 smoke failed: active scope should retain a focus alert suitable for bounded workflow widgets.");

            var allFacetUrls = allResult.Summary.TopReasons
                .Concat(allResult.Summary.BoundaryBreakdown)
                .Concat(trackedPersonResult.Summary.TopReasons)
                .Concat(trackedPersonResult.Summary.BoundaryBreakdown)
                .Select(facet => facet.AlertsUrl)
                .ToList();
            Ensure(allFacetUrls.All(url => url.StartsWith("/operator/alerts", StringComparison.Ordinal)), "OPINT-009-C2 smoke failed: widget analytics facets escaped the bounded alerts page.");

            var allNavigationUrls = trackedPersonResult.Groups
                .SelectMany(group => new[]
                {
                    group.PersonWorkspaceUrl,
                    group.ResolutionQueueUrl
                }.Concat(group.Alerts.SelectMany(alert => new[]
                {
                    alert.ResolutionUrl,
                    alert.PersonWorkspaceUrl
                })))
                .ToList();
            Ensure(allNavigationUrls.All(url =>
                    url.StartsWith("/operator/person-workspace", StringComparison.Ordinal)
                    || url.StartsWith("/operator/resolution", StringComparison.Ordinal)),
                "OPINT-009-C2 smoke failed: widget focus destinations escaped approved operator pages.");

            var shellHtml = OperatorAlertsWebShell.Html;
            Ensure(shellHtml.Contains("Workflow Widgets", StringComparison.Ordinal), "OPINT-009-C2 smoke failed: widget shell section missing.");
            Ensure(shellHtml.Contains("Acknowledgement Queue", StringComparison.Ordinal), "OPINT-009-C2 smoke failed: acknowledgement widget missing.");
            Ensure(shellHtml.Contains("Top Alert Reasons", StringComparison.Ordinal), "OPINT-009-C2 smoke failed: top-reasons widget missing.");
            Ensure(shellHtml.Contains("Boundary Mix", StringComparison.Ordinal), "OPINT-009-C2 smoke failed: boundary widget missing.");
            Ensure(shellHtml.Contains("never expose raw admin or debug controls", StringComparison.Ordinal), "OPINT-009-C2 smoke failed: bounded-widget audit copy missing.");
            Ensure(shellHtml.Contains("Open Focus Alert", StringComparison.Ordinal), "OPINT-009-C2 smoke failed: group focus action missing.");
            Ensure(!shellHtml.Contains("command execution", StringComparison.OrdinalIgnoreCase), "OPINT-009-C2 smoke failed: shell should not advertise free-form command execution.");

            report.AllChecksPassed = true;
            report.BaselineEnterResolutionCount = allResult.Summary.EnterResolutionCount;
            report.ActiveScopeAcknowledgementCount = trackedPersonResult.Summary.RequiresAcknowledgementCount;
            report.BoundedFacetUrlCount = allFacetUrls.Count;
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
                "OPINT-009-C2 web alerts widget smoke failed: bounded analytics/control widgets regressed.",
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

        return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "logs", "opint-009-c2-web-alert-widgets-smoke-report.json"));
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}

public sealed class Opint009WebAlertsWidgetsSmokeReport
{
    public DateTime GeneratedAtUtc { get; set; }
    public string OutputPath { get; set; } = string.Empty;
    public bool AllChecksPassed { get; set; }
    public string? FatalError { get; set; }
    public int BaselineEnterResolutionCount { get; set; }
    public int ActiveScopeAcknowledgementCount { get; set; }
    public int BoundedFacetUrlCount { get; set; }
}
