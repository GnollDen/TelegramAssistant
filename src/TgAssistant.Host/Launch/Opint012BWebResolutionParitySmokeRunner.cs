using System.Text.Json;
using TgAssistant.Host.OperatorWeb;

namespace TgAssistant.Host.Launch;

public static class Opint012BWebResolutionParitySmokeRunner
{
    public static async Task<Opint012BWebResolutionParitySmokeReport> RunAsync(
        string? outputPath = null,
        CancellationToken ct = default)
    {
        var resolvedOutputPath = ResolveOutputPath(outputPath);
        var report = new Opint012BWebResolutionParitySmokeReport
        {
            GeneratedAtUtc = DateTime.UtcNow,
            OutputPath = resolvedOutputPath
        };

        Exception? fatal = null;
        try
        {
            var shellHtml = OperatorWebEndpointExtensions.OperatorResolutionShellHtml;

            Ensure(shellHtml.Contains("/api/operator/resolution/detail/query", StringComparison.Ordinal), "OPINT-012-B smoke failed: resolution web shell no longer targets bounded detail query API.");
            Ensure(shellHtml.Contains("function buildDisplayLabelTrustPrefix(displayLabel, trustPercent)", StringComparison.Ordinal), "OPINT-012-B smoke failed: web shell missing display_label/trust_percent formatter.");
            Ensure(shellHtml.Contains("function renderInterpretationPreview(item)", StringComparison.Ordinal), "OPINT-012-B smoke failed: web shell missing interpretation preview renderer.");
            Ensure(shellHtml.Contains("claim.display_label || claim.displayLabel", StringComparison.Ordinal), "OPINT-012-B smoke failed: claim rendering is not consuming upstream display_label.");
            Ensure(shellHtml.Contains("claim.trust_percent ?? claim.trustPercent", StringComparison.Ordinal), "OPINT-012-B smoke failed: claim rendering is not consuming upstream trust_percent.");
            Ensure(shellHtml.Contains("recommendation.display_label || recommendation.displayLabel", StringComparison.Ordinal), "OPINT-012-B smoke failed: recommendation rendering is not consuming upstream display_label.");
            Ensure(shellHtml.Contains("recommendation.trust_percent ?? recommendation.trustPercent", StringComparison.Ordinal), "OPINT-012-B smoke failed: recommendation rendering is not consuming upstream trust_percent.");
            Ensure(shellHtml.Contains("Math.round(Number(trustPercent))", StringComparison.Ordinal), "OPINT-012-B smoke failed: trust formatting no longer rounds bounded trust_percent.");
            Ensure(shellHtml.Contains("%]\";", StringComparison.Ordinal), "OPINT-012-B smoke failed: trust formatting no longer emits percent token.");
            Ensure(!shellHtml.Contains("claim.confidence", StringComparison.Ordinal), "OPINT-012-B smoke failed: web rendering path still references raw claim confidence.");
            Ensure(!shellHtml.Contains("recommendation.confidence", StringComparison.Ordinal), "OPINT-012-B smoke failed: web rendering path still references raw recommendation confidence.");

            var inferredPrefix = BuildDisplayLabelTrustPrefix("Inference", 74);
            var hypothesisPrefix = BuildDisplayLabelTrustPrefix("Hypothesis", null);
            var recommendationPrefix = BuildDisplayLabelTrustPrefix("Recommendation", 63);
            Ensure(string.Equals(inferredPrefix, "[Inference] [74%]", StringComparison.Ordinal), "OPINT-012-B smoke failed: inferred claim prefix format mismatch.");
            Ensure(string.Equals(hypothesisPrefix, "[Hypothesis]", StringComparison.Ordinal), "OPINT-012-B smoke failed: null-trust claim should omit percent token.");
            Ensure(string.Equals(recommendationPrefix, "[Recommendation] [63%]", StringComparison.Ordinal), "OPINT-012-B smoke failed: recommendation prefix format mismatch.");

            report.AllChecksPassed = true;
            report.RendererUsesDisplayLabelTrustPercent = true;
            report.NullTrustOmitsPercent = true;
            report.RawConfidenceSuppressed = true;
        }
        catch (Exception ex)
        {
            fatal = ex;
            report.AllChecksPassed = false;
            report.FatalError = ex.Message;
            report.RendererUsesDisplayLabelTrustPercent = false;
            report.NullTrustOmitsPercent = false;
            report.RawConfidenceSuppressed = false;
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
                "OPINT-012-B web resolution parity smoke failed: display_label/trust_percent render contract regressed.",
                fatal);
        }

        return report;
    }

    private static string BuildDisplayLabelTrustPrefix(string? displayLabel, int? trustPercent)
    {
        var normalizedLabel = (displayLabel ?? string.Empty).Trim();
        var hasLabel = !string.IsNullOrWhiteSpace(normalizedLabel);
        if (!trustPercent.HasValue && !hasLabel)
        {
            return string.Empty;
        }

        if (!trustPercent.HasValue)
        {
            return $"[{normalizedLabel}]";
        }

        var trustToken = $"[{Math.Clamp(trustPercent.Value, 0, 100)}%]";
        if (!hasLabel)
        {
            return trustToken;
        }

        return $"[{normalizedLabel}] {trustToken}";
    }

    private static string ResolveOutputPath(string? outputPath)
    {
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            return Path.GetFullPath(outputPath);
        }

        return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "logs", "opint-012-b-smoke-report.json"));
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}

public sealed class Opint012BWebResolutionParitySmokeReport
{
    public DateTime GeneratedAtUtc { get; set; }
    public string OutputPath { get; set; } = string.Empty;
    public bool AllChecksPassed { get; set; }
    public string? FatalError { get; set; }
    public bool RendererUsesDisplayLabelTrustPercent { get; set; }
    public bool NullTrustOmitsPercent { get; set; }
    public bool RawConfidenceSuppressed { get; set; }
}
