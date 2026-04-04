using System.Text.RegularExpressions;

namespace TgAssistant.Telegram.Operator;

public static class OfflineEventClarificationLoopStatuses
{
    public const string Active = "active";
    public const string Stopped = "stopped";
}

public static class OfflineEventClarificationStopReasons
{
    public const string Repetition = "repetition";
    public const string NoNewInformation = "no_new_information";
    public const string UnknownPattern = "unknown_pattern";
    public const string LowGain = "low_gain";
    public const string Exhausted = "question_pool_exhausted";
}

public static class OfflineEventClarificationQuestionStatuses
{
    public const string Queued = "queued";
    public const string Answered = "answered";
}

public sealed class OfflineEventClarificationState
{
    public string SchemaVersion { get; set; } = "opint_offline_clarification_v1";
    public DateTime GeneratedAtUtc { get; set; }
    public string LoopStatus { get; set; } = OfflineEventClarificationLoopStatuses.Active;
    public string? StopReason { get; set; }
    public string? StopDetail { get; set; }
    public DateTime? StoppedAtUtc { get; set; }
    public List<OfflineEventClarificationQuestion> Questions { get; set; } = [];
    public List<OfflineEventClarificationAnswerEntry> History { get; set; } = [];
    public List<string> KnownTokens { get; set; } = [];
    public int UnknownConsecutiveCount { get; set; }
    public int RepetitionConsecutiveCount { get; set; }
    public int NoNewContextConsecutiveCount { get; set; }
    public int LowGainConsecutiveCount { get; set; }
    public float PartialConfidence { get; set; } = 0.45f;
    public string? NextQuestionKey { get; set; }
}

public sealed class OfflineEventClarificationQuestion
{
    public string Key { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public float ExpectedInformationGain { get; set; }
    public int PriorityRank { get; set; }
    public string Status { get; set; } = OfflineEventClarificationQuestionStatuses.Queued;
}

public sealed class OfflineEventClarificationAnswerEntry
{
    public string QuestionKey { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
    public bool UnknownPattern { get; set; }
    public bool RepetitionDetected { get; set; }
    public int NewTokenCount { get; set; }
    public float InformationGain { get; set; }
    public DateTime CapturedAtUtc { get; set; }
}

public sealed class OfflineEventClarificationEvaluationResult
{
    public bool StopTriggered { get; set; }
    public string LoopStatus { get; set; } = OfflineEventClarificationLoopStatuses.Active;
    public string? StopReason { get; set; }
    public string? StopDetail { get; set; }
    public string? NextQuestionKey { get; set; }
    public float PartialConfidence { get; set; }
}

public sealed class OfflineEventClarificationPolicy
{
    private static readonly HashSet<string> UnknownAnswers = new(StringComparer.Ordinal)
    {
        "i don't know",
        "i dont know",
        "don't know",
        "dont know",
        "not sure",
        "i'm not sure",
        "im not sure",
        "don't remember",
        "dont remember",
        "i don't remember",
        "i dont remember",
        "no idea",
        "unknown"
    };

    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "a", "an", "and", "are", "as", "at", "be", "by", "for", "from", "had",
        "has", "have", "he", "her", "his", "i", "in", "is", "it", "its", "me",
        "my", "of", "on", "or", "our", "she", "that", "the", "their", "them",
        "there", "they", "this", "to", "was", "we", "were", "with", "you", "your"
    };

    public OfflineEventClarificationState CreateInitialState(
        string? summary,
        string? recordingReference,
        DateTime nowUtc)
    {
        var normalizedSummary = Normalize(summary);
        var normalizedRecording = Normalize(recordingReference);
        var seedTokens = ExtractTokens($"{normalizedSummary} {normalizedRecording}");

        var questionCandidates = new List<OfflineEventClarificationQuestion>
        {
            new()
            {
                Key = "outcome_next_step",
                Text = "What changed after this event, and what is the next step?",
                ExpectedInformationGain = HasOutcomeCue(normalizedSummary) ? 0.40f : 0.86f
            },
            new()
            {
                Key = "when_happened",
                Text = "When exactly did this happen?",
                ExpectedInformationGain = HasTimeCue(normalizedSummary) ? 0.34f : 0.82f
            },
            new()
            {
                Key = "who_involved",
                Text = "Who was present or directly involved?",
                ExpectedInformationGain = HasParticipantCue(normalizedSummary) ? 0.36f : 0.79f
            },
            new()
            {
                Key = "where_happened",
                Text = "Where did this happen?",
                ExpectedInformationGain = HasLocationCue(normalizedSummary) ? 0.30f : 0.72f
            },
            new()
            {
                Key = "evidence_anchor",
                Text = "Do we have a concrete quote, timestamp, or recording segment to anchor this?",
                ExpectedInformationGain = string.IsNullOrWhiteSpace(normalizedRecording) ? 0.67f : 0.22f
            }
        };

        var ordered = questionCandidates
            .OrderByDescending(x => x.ExpectedInformationGain)
            .ThenBy(x => x.Key, StringComparer.Ordinal)
            .ToList();
        for (var index = 0; index < ordered.Count; index++)
        {
            ordered[index].PriorityRank = index + 1;
        }

        return new OfflineEventClarificationState
        {
            GeneratedAtUtc = nowUtc,
            Questions = ordered,
            KnownTokens = seedTokens.ToList(),
            NextQuestionKey = ordered.FirstOrDefault()?.Key,
            PartialConfidence = 0.45f
        };
    }

    public OfflineEventClarificationEvaluationResult ApplyAnswer(
        OfflineEventClarificationState state,
        string questionKey,
        string? answer,
        DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (string.IsNullOrWhiteSpace(questionKey))
        {
            throw new ArgumentException("Question key is required.", nameof(questionKey));
        }

        var question = state.Questions.FirstOrDefault(x => string.Equals(x.Key, questionKey.Trim(), StringComparison.Ordinal));
        if (question == null)
        {
            throw new InvalidOperationException("Unknown clarification question key.");
        }

        var normalizedAnswer = Normalize(answer);
        if (string.IsNullOrWhiteSpace(normalizedAnswer))
        {
            normalizedAnswer = "no_answer";
        }

        var unknownPattern = IsUnknownPattern(normalizedAnswer);
        var repetitionDetected = IsRepetition(state, normalizedAnswer);
        var answerTokens = ExtractTokens(normalizedAnswer);
        var novelTokenCount = answerTokens.Count(token => !state.KnownTokens.Contains(token, StringComparer.Ordinal));
        var informationGain = CalculateInformationGain(normalizedAnswer, novelTokenCount, unknownPattern);

        question.Status = OfflineEventClarificationQuestionStatuses.Answered;
        state.History.Add(
            new OfflineEventClarificationAnswerEntry
            {
                QuestionKey = question.Key,
                Answer = normalizedAnswer,
                UnknownPattern = unknownPattern,
                RepetitionDetected = repetitionDetected,
                NewTokenCount = novelTokenCount,
                InformationGain = informationGain,
                CapturedAtUtc = nowUtc
            });

        foreach (var token in answerTokens)
        {
            if (!state.KnownTokens.Contains(token, StringComparer.Ordinal))
            {
                state.KnownTokens.Add(token);
            }
        }

        state.UnknownConsecutiveCount = unknownPattern ? state.UnknownConsecutiveCount + 1 : 0;
        state.RepetitionConsecutiveCount = repetitionDetected ? state.RepetitionConsecutiveCount + 1 : 0;
        state.NoNewContextConsecutiveCount = novelTokenCount == 0 ? state.NoNewContextConsecutiveCount + 1 : 0;
        state.LowGainConsecutiveCount = informationGain <= 0.18f ? state.LowGainConsecutiveCount + 1 : 0;

        state.PartialConfidence = Math.Clamp(
            state.PartialConfidence + (informationGain >= 0.55f ? 0.08f : -0.03f),
            0.20f,
            0.90f);

        var stopReason = DetermineStopReason(state);
        if (stopReason != null)
        {
            state.LoopStatus = OfflineEventClarificationLoopStatuses.Stopped;
            state.StopReason = stopReason;
            state.StopDetail = BuildStopDetail(stopReason);
            state.StoppedAtUtc = nowUtc;
            state.NextQuestionKey = null;
            state.PartialConfidence = Math.Min(state.PartialConfidence, 0.65f);
        }
        else
        {
            var nextQuestion = state.Questions
                .Where(x => string.Equals(x.Status, OfflineEventClarificationQuestionStatuses.Queued, StringComparison.Ordinal))
                .OrderBy(x => x.PriorityRank)
                .FirstOrDefault();
            state.NextQuestionKey = nextQuestion?.Key;
            state.LoopStatus = nextQuestion == null
                ? OfflineEventClarificationLoopStatuses.Stopped
                : OfflineEventClarificationLoopStatuses.Active;
            if (nextQuestion == null)
            {
                state.StopReason = OfflineEventClarificationStopReasons.Exhausted;
                state.StopDetail = BuildStopDetail(OfflineEventClarificationStopReasons.Exhausted);
                state.StoppedAtUtc = nowUtc;
            }
        }

        return new OfflineEventClarificationEvaluationResult
        {
            StopTriggered = string.Equals(state.LoopStatus, OfflineEventClarificationLoopStatuses.Stopped, StringComparison.Ordinal),
            LoopStatus = state.LoopStatus,
            StopReason = state.StopReason,
            StopDetail = state.StopDetail,
            NextQuestionKey = state.NextQuestionKey,
            PartialConfidence = state.PartialConfidence
        };
    }

    private static string? DetermineStopReason(OfflineEventClarificationState state)
    {
        if (state.RepetitionConsecutiveCount >= 1)
        {
            return OfflineEventClarificationStopReasons.Repetition;
        }

        if (state.UnknownConsecutiveCount >= 2)
        {
            return OfflineEventClarificationStopReasons.UnknownPattern;
        }

        if (state.NoNewContextConsecutiveCount >= 2)
        {
            return OfflineEventClarificationStopReasons.NoNewInformation;
        }

        if (state.LowGainConsecutiveCount >= 2)
        {
            return OfflineEventClarificationStopReasons.LowGain;
        }

        return null;
    }

    private static string BuildStopDetail(string stopReason)
    {
        return stopReason switch
        {
            OfflineEventClarificationStopReasons.Repetition => "operator_repeated_previous_context",
            OfflineEventClarificationStopReasons.NoNewInformation => "answers_added_no_novel_context",
            OfflineEventClarificationStopReasons.UnknownPattern => "answers_converged_to_unknown_pattern",
            OfflineEventClarificationStopReasons.LowGain => "information_gain_below_threshold",
            OfflineEventClarificationStopReasons.Exhausted => "clarification_question_pool_exhausted",
            _ => "clarification_loop_stopped"
        };
    }

    private static bool IsRepetition(OfflineEventClarificationState state, string normalizedAnswer)
    {
        if (state.History.Count == 0)
        {
            return false;
        }

        var lastAnswer = Normalize(state.History[^1].Answer);
        if (string.Equals(lastAnswer, normalizedAnswer, StringComparison.Ordinal))
        {
            return true;
        }

        return state.History.Any(x => string.Equals(Normalize(x.Answer), normalizedAnswer, StringComparison.Ordinal));
    }

    private static float CalculateInformationGain(string normalizedAnswer, int novelTokenCount, bool unknownPattern)
    {
        var gain = 0.08f + Math.Min(0.70f, novelTokenCount * 0.12f);
        if (normalizedAnswer.Length >= 24)
        {
            gain += 0.10f;
        }

        if (unknownPattern)
        {
            gain -= 0.35f;
        }

        return Math.Clamp(gain, 0f, 1f);
    }

    private static bool IsUnknownPattern(string value)
    {
        if (UnknownAnswers.Contains(value))
        {
            return true;
        }

        return value.StartsWith("i don't know", StringComparison.Ordinal)
            || value.StartsWith("i dont know", StringComparison.Ordinal)
            || value.StartsWith("don't remember", StringComparison.Ordinal)
            || value.StartsWith("dont remember", StringComparison.Ordinal);
    }

    private static bool HasTimeCue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return Regex.IsMatch(
            value,
            @"\b(today|yesterday|tomorrow|morning|afternoon|evening|night|monday|tuesday|wednesday|thursday|friday|saturday|sunday|\d{1,2}:\d{2}|am|pm)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool HasLocationCue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return Regex.IsMatch(
            value,
            @"\b(at|in|near|office|home|cafe|restaurant|school|park|station|airport|online)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool HasParticipantCue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return Regex.IsMatch(
            value,
            @"\b(with|we|they|friend|manager|client|partner|family|team)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool HasOutcomeCue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return Regex.IsMatch(
            value,
            @"\b(agreed|decided|next step|follow up|follow-up|plan|outcome|resolved|escalate|de-escalate|action)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalizedWhitespace = Regex.Replace(value.Trim().ToLowerInvariant(), @"\s+", " ");
        return normalizedWhitespace;
    }

    private static HashSet<string> ExtractTokens(string value)
    {
        return Regex.Matches(value ?? string.Empty, "[a-z0-9']+")
            .Select(match => match.Value.Trim('\''))
            .Where(token => token.Length >= 3 && !StopWords.Contains(token))
            .ToHashSet(StringComparer.Ordinal);
    }
}
