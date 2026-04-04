using System.Text.Json;
using TgAssistant.Telegram.Operator;

namespace TgAssistant.Host.Launch;

public static class Opint007OfflineEventClarificationPolicySmokeRunner
{
    public static async Task<Opint007OfflineEventClarificationPolicySmokeReport> RunAsync(
        OfflineEventClarificationPolicy policy,
        string? outputPath = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(policy);
        var resolvedOutputPath = ResolveOutputPath(outputPath);
        var nowUtc = DateTime.UtcNow;
        var report = new Opint007OfflineEventClarificationPolicySmokeReport
        {
            GeneratedAtUtc = nowUtc,
            OutputPath = resolvedOutputPath
        };

        Exception? fatal = null;
        try
        {
            var rankingState = policy.CreateInitialState(
                summary: "Met after work and discussed a conflict. We agreed to follow up tomorrow.",
                recordingReference: "voice-note://opint-007-b2",
                nowUtc: nowUtc);
            Ensure(rankingState.Questions.Count >= 4, "Clarification pool is unexpectedly small.");
            Ensure(IsSortedByGain(rankingState.Questions), "Clarification questions are not ranked by expected information gain.");
            report.RankingTopQuestionKey = rankingState.Questions[0].Key;
            report.RankingTopExpectedGain = rankingState.Questions[0].ExpectedInformationGain;

            report.RepetitionStop = RunScenario(
                policy,
                summary: "We talked about project pressure and scheduling.",
                answers:
                [
                    "We talked about project pressure.",
                    "We talked about project pressure."
                ],
                expectedStopReason: OfflineEventClarificationStopReasons.Repetition,
                nowUtc);

            report.UnknownStop = RunScenario(
                policy,
                summary: "There was an argument and I only remember parts of it.",
                answers:
                [
                    "I don't know",
                    "I don't remember"
                ],
                expectedStopReason: OfflineEventClarificationStopReasons.UnknownPattern,
                nowUtc);

            report.NoNewInformationStop = RunScenario(
                policy,
                summary: "Discussion happened in office with teammate.",
                answers:
                [
                    "office teammate discussion",
                    "office teammate discussion"
                ],
                expectedStopReason: OfflineEventClarificationStopReasons.NoNewInformation,
                nowUtc);

            report.LowGainStop = RunScenario(
                policy,
                summary: "Initial summary is very limited.",
                answers:
                [
                    "ok",
                    "fine"
                ],
                expectedStopReason: OfflineEventClarificationStopReasons.LowGain,
                nowUtc);

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
                "OPINT-007-B2 clarification policy smoke failed: prioritized question ranking or stop-rule enforcement is incomplete.",
                fatal);
        }

        return report;
    }

    private static Opint007ClarificationScenarioResult RunScenario(
        OfflineEventClarificationPolicy policy,
        string summary,
        IReadOnlyList<string> answers,
        string expectedStopReason,
        DateTime nowUtc)
    {
        var state = policy.CreateInitialState(summary, recordingReference: null, nowUtc);
        OfflineEventClarificationEvaluationResult? last = null;
        foreach (var answer in answers)
        {
            var nextKey = state.NextQuestionKey;
            Ensure(!string.IsNullOrWhiteSpace(nextKey), "No queued clarification question remained before stop-rule test completed.");
            last = policy.ApplyAnswer(state, nextKey!, answer, nowUtc);
            if (last.StopTriggered)
            {
                break;
            }
        }

        Ensure(last != null, "Scenario did not execute any clarification answers.");
        Ensure(last!.StopTriggered, $"Scenario did not trigger stop-rule '{expectedStopReason}'.");
        Ensure(string.Equals(last.StopReason, expectedStopReason, StringComparison.Ordinal),
            $"Scenario stop reason mismatch. expected={expectedStopReason}, actual={last.StopReason ?? "null"}");

        return new Opint007ClarificationScenarioResult
        {
            StopTriggered = last.StopTriggered,
            StopReason = last.StopReason,
            StopDetail = last.StopDetail,
            PartialConfidence = last.PartialConfidence,
            HistoryCount = state.History.Count
        };
    }

    private static bool IsSortedByGain(IReadOnlyList<OfflineEventClarificationQuestion> questions)
    {
        for (var index = 1; index < questions.Count; index++)
        {
            if (questions[index - 1].ExpectedInformationGain < questions[index].ExpectedInformationGain)
            {
                return false;
            }
        }

        return true;
    }

    private static string ResolveOutputPath(string? outputPath)
    {
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            return Path.GetFullPath(outputPath);
        }

        return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "logs", "opint-007-b2-smoke-report.json"));
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}

public sealed class Opint007OfflineEventClarificationPolicySmokeReport
{
    public DateTime GeneratedAtUtc { get; set; }
    public string OutputPath { get; set; } = string.Empty;
    public string? RankingTopQuestionKey { get; set; }
    public float RankingTopExpectedGain { get; set; }
    public Opint007ClarificationScenarioResult RepetitionStop { get; set; } = new();
    public Opint007ClarificationScenarioResult UnknownStop { get; set; } = new();
    public Opint007ClarificationScenarioResult NoNewInformationStop { get; set; } = new();
    public Opint007ClarificationScenarioResult LowGainStop { get; set; } = new();
    public bool AllChecksPassed { get; set; }
    public string? FatalError { get; set; }
}

public sealed class Opint007ClarificationScenarioResult
{
    public bool StopTriggered { get; set; }
    public string? StopReason { get; set; }
    public string? StopDetail { get; set; }
    public float PartialConfidence { get; set; }
    public int HistoryCount { get; set; }
}
