using System.Text.Json;
using System.Text.Json.Serialization;
using TgAssistant.Core.Models;

namespace TgAssistant.Host.Launch;

public static class StageSemanticContractProofRunner
{
    public static async Task<StageSemanticContractProofReport> RunAsync(
        string? outputPath = null,
        CancellationToken ct = default)
    {
        var resolvedOutputPath = ResolveOutputPath(outputPath);
        var report = new StageSemanticContractProofReport
        {
            GeneratedAtUtc = DateTime.UtcNow,
            OutputPath = resolvedOutputPath
        };

        Exception? fatal = null;
        try
        {
            report.Cases =
            [
                ValidateStage6ToStage7Case(
                    caseId: "s6_to_s7_valid",
                    stage6OwnedOutputFamily: StageSemanticOwnedOutputFamilies.Stage6BootstrapGraph,
                    stage7AcceptedInputFamily: StageSemanticAcceptedInputFamilies.Stage6BootstrapGraph,
                    handoffReason: StageSemanticHandoffReasons.BootstrapComplete,
                    expectedDecision: "allow",
                    expectedStatus: "accepted"),
                ValidateStage7ToStage8Case(
                    caseId: "s7_to_s8_valid",
                    stage7OwnedOutputFamily: StageSemanticOwnedOutputFamilies.Stage7DurableProfile,
                    stage8AcceptedInputFamily: StageSemanticAcceptedInputFamilies.Stage7DurableProfile,
                    handoffReason: StageSemanticHandoffReasons.DurableReady,
                    expectedDecision: "allow",
                    expectedStatus: "accepted"),
                ValidateStage7ToStage8Case(
                    caseId: "s8_input_family_invalid",
                    stage7OwnedOutputFamily: StageSemanticOwnedOutputFamilies.Stage7DurableProfile,
                    stage8AcceptedInputFamily: StageSemanticAcceptedInputFamilies.Stage6DiscoveryPool,
                    handoffReason: StageSemanticHandoffReasons.DurableReady,
                    expectedDecision: "reject",
                    expectedStatus: "blocked"),
                ValidateStage7ToStage8Case(
                    caseId: "s8_invalid_input_sets_blocked_status",
                    stage7OwnedOutputFamily: StageSemanticOwnedOutputFamilies.Stage7DurableProfile,
                    stage8AcceptedInputFamily: StageSemanticAcceptedInputFamilies.Stage7PairDynamics,
                    handoffReason: StageSemanticHandoffReasons.DurableReady,
                    expectedDecision: "reject",
                    expectedStatus: "blocked")
            ];

            report.Passed = report.Cases.All(x => x.Passed);
        }
        catch (Exception ex)
        {
            fatal = ex;
            report.Passed = false;
            report.FatalError = ex.Message;
        }
        finally
        {
            Directory.CreateDirectory(Path.GetDirectoryName(resolvedOutputPath)!);
            var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(resolvedOutputPath, json, ct);
        }

        if (!report.Passed)
        {
            throw new InvalidOperationException(
                "Stage semantic contract proof failed.",
                fatal);
        }

        return report;
    }

    private static StageSemanticContractProofCaseRow ValidateStage6ToStage7Case(
        string caseId,
        string stage6OwnedOutputFamily,
        string stage7AcceptedInputFamily,
        string handoffReason,
        string expectedDecision,
        string expectedStatus)
    {
        var validation = StageSemanticContract.ValidateStage6ToStage7Handoff(
            stage6OwnedOutputFamily,
            stage7AcceptedInputFamily,
            handoffReason);

        return BuildCaseRow(
            caseId,
            stage7AcceptedInputFamily,
            expectedDecision,
            expectedStatus,
            validation);
    }

    private static StageSemanticContractProofCaseRow ValidateStage7ToStage8Case(
        string caseId,
        string stage7OwnedOutputFamily,
        string stage8AcceptedInputFamily,
        string handoffReason,
        string expectedDecision,
        string expectedStatus)
    {
        var validation = StageSemanticContract.ValidateStage7ToStage8Handoff(
            stage7OwnedOutputFamily,
            stage8AcceptedInputFamily,
            handoffReason);

        return BuildCaseRow(
            caseId,
            stage8AcceptedInputFamily,
            expectedDecision,
            expectedStatus,
            validation);
    }

    private static StageSemanticContractProofCaseRow BuildCaseRow(
        string caseId,
        string inputFamily,
        string expectedDecision,
        string expectedStatus,
        StageSemanticHandoffValidationResult validation)
    {
        var actualDecision = validation.IsValid ? "allow" : "reject";
        var actualStatus = validation.IsValid ? "accepted" : "blocked";
        var reason = validation.IsValid ? "valid" : validation.Reason ?? StageSemanticHandoffReasons.StageContractViolation;

        return new StageSemanticContractProofCaseRow
        {
            CaseId = caseId,
            InputFamily = inputFamily,
            ExpectedDecision = expectedDecision,
            ActualDecision = actualDecision,
            ExpectedStatus = expectedStatus,
            ActualStatus = actualStatus,
            Reason = reason,
            Passed = string.Equals(expectedDecision, actualDecision, StringComparison.Ordinal)
                && string.Equals(expectedStatus, actualStatus, StringComparison.Ordinal)
        };
    }

    private static string ResolveOutputPath(string? outputPath)
    {
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            return Path.GetFullPath(outputPath);
        }

        var cwd = Directory.GetCurrentDirectory();
        var hostArtifactsRoot = string.Equals(Path.GetFileName(cwd), "TgAssistant.Host", StringComparison.Ordinal)
            ? Path.Combine(cwd, "artifacts")
            : Path.Combine(cwd, "src", "TgAssistant.Host", "artifacts");

        return Path.GetFullPath(Path.Combine(
            hostArtifactsRoot,
            "phase-b",
            "stage-semantic-contract-proof.json"));
    }
}

public sealed class StageSemanticContractProofReport
{
    [JsonPropertyName("generated_at_utc")]
    public DateTime GeneratedAtUtc { get; set; }

    [JsonPropertyName("output_path")]
    public string OutputPath { get; set; } = string.Empty;

    [JsonPropertyName("passed")]
    public bool Passed { get; set; }

    [JsonPropertyName("fatal_error")]
    public string? FatalError { get; set; }

    [JsonPropertyName("cases")]
    public List<StageSemanticContractProofCaseRow> Cases { get; set; } = [];
}

public sealed class StageSemanticContractProofCaseRow
{
    [JsonPropertyName("case_id")]
    public string CaseId { get; set; } = string.Empty;

    [JsonPropertyName("input_family")]
    public string InputFamily { get; set; } = string.Empty;

    [JsonPropertyName("expected_decision")]
    public string ExpectedDecision { get; set; } = string.Empty;

    [JsonPropertyName("actual_decision")]
    public string ActualDecision { get; set; } = string.Empty;

    [JsonPropertyName("expected_status")]
    public string ExpectedStatus { get; set; } = string.Empty;

    [JsonPropertyName("actual_status")]
    public string ActualStatus { get; set; } = string.Empty;

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;

    [JsonPropertyName("passed")]
    public bool Passed { get; set; }
}
