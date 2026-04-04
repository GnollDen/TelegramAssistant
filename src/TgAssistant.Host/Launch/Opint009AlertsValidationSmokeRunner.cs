using System.Text.Json;
using TgAssistant.Infrastructure.Database;

namespace TgAssistant.Host.Launch;

public static class Opint009AlertsValidationSmokeRunner
{
    public static async Task<Opint009AlertsValidationSmokeReport> RunAsync(
        string? outputPath = null,
        CancellationToken ct = default)
    {
        var resolvedOutputPath = ResolveOutputPath(outputPath);
        var report = new Opint009AlertsValidationSmokeReport
        {
            GeneratedAtUtc = DateTime.UtcNow,
            OutputPath = resolvedOutputPath
        };

        Exception? fatal = null;
        try
        {
            var policyReport = await Opint009AlertPolicySmokeRunner.RunAsync(new OperatorAlertPolicyService(), null, ct);
            var telegramReport = await Opint009TelegramAlertsSmokeRunner.RunAsync(null, ct);
            var webAlertsReport = await Opint009WebAlertsSmokeRunner.RunAsync(null, ct);
            var widgetsReport = await Opint009WebAlertsWidgetsSmokeRunner.RunAsync(null, ct);

            Ensure(policyReport.AllChecksPassed, "OPINT-009-D smoke failed: policy smoke did not pass.");
            Ensure(telegramReport.AllChecksPassed, "OPINT-009-D smoke failed: Telegram smoke did not pass.");
            Ensure(webAlertsReport.AllChecksPassed, "OPINT-009-D smoke failed: web alerts smoke did not pass.");
            Ensure(widgetsReport.AllChecksPassed, "OPINT-009-D smoke failed: web widgets smoke did not pass.");

            Ensure(telegramReport.NonCriticalAlertSuppressed, "OPINT-009-D smoke failed: Telegram suppression contract was not retained.");
            Ensure(telegramReport.AcknowledgementRetainedContext, "OPINT-009-D smoke failed: acknowledgement context contract was not retained.");
            Ensure(widgetsReport.ActiveScopeAcknowledgementCount > 0, "OPINT-009-D smoke failed: acknowledgement-required alerts were not visible in web widgets.");

            Ensure(!string.IsNullOrWhiteSpace(telegramReport.OpenInWebUrl), "OPINT-009-D smoke failed: Telegram handoff URL was empty.");
            var handoffUri = new Uri(telegramReport.OpenInWebUrl, UriKind.Absolute);
            var query = ParseQuery(handoffUri.Query);
            Ensure(string.Equals(handoffUri.AbsolutePath, "/operator/resolution", StringComparison.Ordinal), "OPINT-009-D smoke failed: handoff did not target /operator/resolution.");
            Ensure(query.ContainsKey("handoff_token"), "OPINT-009-D smoke failed: handoff token query parameter is missing.");
            Ensure(string.Equals(query.GetValueOrDefault("operator_session_id"), telegramReport.OperatorSessionId, StringComparison.Ordinal), "OPINT-009-D smoke failed: handoff session id mismatch.");
            Ensure(string.Equals(query.GetValueOrDefault("scope_item_key"), telegramReport.CriticalAlertScopeItemKey, StringComparison.Ordinal), "OPINT-009-D smoke failed: handoff scope item mismatch.");
            Ensure(string.Equals(query.GetValueOrDefault("target_api"), "/api/operator/resolution/detail/query", StringComparison.Ordinal), "OPINT-009-D smoke failed: handoff target API mismatch.");

            report.AllChecksPassed = true;
            report.DeliverySuppressionValidated = policyReport.AllChecksPassed
                                                   && telegramReport.NonCriticalAlertSuppressed
                                                   && webAlertsReport.WebOnlyAlerts > 0;
            report.AcknowledgementValidated = telegramReport.AcknowledgementRetainedContext
                                              && widgetsReport.ActiveScopeAcknowledgementCount > 0;
            report.DeepLinkRecoveryValidated = true;
            report.WebBoundedSurfaceValidated = webAlertsReport.AllChecksPassed
                                                && widgetsReport.BoundedFacetUrlCount > 0;
            report.PolicyReportPath = policyReport.OutputPath;
            report.TelegramReportPath = telegramReport.OutputPath;
            report.WebAlertsReportPath = webAlertsReport.OutputPath;
            report.WebWidgetsReportPath = widgetsReport.OutputPath;
            report.TelegramOpenInWebUrl = telegramReport.OpenInWebUrl;
            report.ActiveTrackedPersonId = telegramReport.ActiveTrackedPersonId;
            report.OperatorSessionId = telegramReport.OperatorSessionId;
            report.TotalWebAlerts = webAlertsReport.TotalAlerts;
            report.ActiveScopeAcknowledgementCount = widgetsReport.ActiveScopeAcknowledgementCount;
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
                "OPINT-009-D alerts validation smoke failed: delivery/suppression/acknowledgement/deep-link contracts regressed.",
                fatal);
        }

        return report;
    }

    private static Dictionary<string, string> ParseQuery(string queryString)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in queryString.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            var key = Uri.UnescapeDataString(parts[0]);
            var value = parts.Length == 2 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
            result[key] = value;
        }

        return result;
    }

    private static string ResolveOutputPath(string? outputPath)
    {
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            return Path.GetFullPath(outputPath);
        }

        return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "logs", "opint-009-d-alerts-validation-smoke-report.json"));
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}

public sealed class Opint009AlertsValidationSmokeReport
{
    public DateTime GeneratedAtUtc { get; set; }
    public string OutputPath { get; set; } = string.Empty;
    public bool AllChecksPassed { get; set; }
    public string? FatalError { get; set; }
    public bool DeliverySuppressionValidated { get; set; }
    public bool AcknowledgementValidated { get; set; }
    public bool DeepLinkRecoveryValidated { get; set; }
    public bool WebBoundedSurfaceValidated { get; set; }
    public string PolicyReportPath { get; set; } = string.Empty;
    public string TelegramReportPath { get; set; } = string.Empty;
    public string WebAlertsReportPath { get; set; } = string.Empty;
    public string WebWidgetsReportPath { get; set; } = string.Empty;
    public string TelegramOpenInWebUrl { get; set; } = string.Empty;
    public Guid ActiveTrackedPersonId { get; set; }
    public string OperatorSessionId { get; set; } = string.Empty;
    public int TotalWebAlerts { get; set; }
    public int ActiveScopeAcknowledgementCount { get; set; }
}
