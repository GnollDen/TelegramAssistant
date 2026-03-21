using System.Text.RegularExpressions;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage6.Outcome;

public class DraftActionMatcher : IDraftActionMatcher
{
    private static readonly Regex WsRegex = new(@"\s+", RegexOptions.Compiled);
    private readonly IMessageRepository _messageRepository;

    public DraftActionMatcher(IMessageRepository messageRepository)
    {
        _messageRepository = messageRepository;
    }

    public async Task<DraftActionMatchResult> MatchAsync(
        DraftRecord draft,
        long chatId,
        long? explicitMessageId,
        string? explicitActionText,
        CancellationToken ct = default)
    {
        if (explicitMessageId.HasValue)
        {
            var explicitMessage = await _messageRepository.GetByIdAsync(explicitMessageId.Value, ct);
            if (explicitMessage != null && explicitMessage.ChatId == chatId)
            {
                var score = ComputeScore(draft, explicitMessage.Text);
                return new DraftActionMatchResult
                {
                    MatchedMessageId = explicitMessage.Id,
                    MatchScore = score,
                    IsPartialMatch = score < 0.95f,
                    MatchMethod = "explicit_message_id"
                };
            }
        }

        if (!string.IsNullOrWhiteSpace(explicitActionText))
        {
            var score = ComputeScore(draft, explicitActionText);
            return new DraftActionMatchResult
            {
                MatchedMessageId = null,
                MatchScore = score,
                IsPartialMatch = score < 0.95f,
                MatchMethod = "explicit_action_text"
            };
        }

        var fromUtc = draft.CreatedAt.AddDays(-2);
        var toUtc = draft.CreatedAt.AddDays(10);
        var candidates = await _messageRepository.GetByChatAndPeriodAsync(chatId, fromUtc, toUtc, 400, ct);
        var best = candidates
            .Select(x => new { x.Id, Score = ComputeScore(draft, x.Text) })
            .OrderByDescending(x => x.Score)
            .FirstOrDefault();

        if (best == null || best.Score < 0.2f)
        {
            return new DraftActionMatchResult
            {
                MatchScore = 0f,
                IsPartialMatch = false,
                MatchMethod = "none"
            };
        }

        return new DraftActionMatchResult
        {
            MatchedMessageId = best.Id,
            MatchScore = best.Score,
            IsPartialMatch = best.Score < 0.95f,
            MatchMethod = best.Score >= 0.95f ? "exact_like_text" : "partial_similarity"
        };
    }

    private static float ComputeScore(DraftRecord draft, string? actualText)
    {
        var normalizedActual = Normalize(actualText);
        if (string.IsNullOrWhiteSpace(normalizedActual))
        {
            return 0f;
        }

        var variants = new[]
        {
            Normalize(draft.MainDraft),
            Normalize(draft.AltDraft1),
            Normalize(draft.AltDraft2)
        }.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();

        if (variants.Count == 0)
        {
            return 0f;
        }

        var exact = variants.Any(x => string.Equals(x, normalizedActual, StringComparison.Ordinal));
        if (exact)
        {
            return 1f;
        }

        var best = 0f;
        foreach (var variant in variants)
        {
            var overlap = TokenOverlap(variant, normalizedActual);
            var lenPenalty = LengthPenalty(variant, normalizedActual);
            var score = (overlap * 0.85f) + (lenPenalty * 0.15f);
            if (score > best)
            {
                best = score;
            }
        }

        return Math.Clamp(best, 0f, 1f);
    }

    private static float TokenOverlap(string left, string right)
    {
        var leftTokens = left.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.Ordinal);
        var rightTokens = right.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.Ordinal);
        if (leftTokens.Count == 0 || rightTokens.Count == 0)
        {
            return 0f;
        }

        var intersection = leftTokens.Intersect(rightTokens, StringComparer.Ordinal).Count();
        var union = leftTokens.Union(rightTokens, StringComparer.Ordinal).Count();
        return union == 0 ? 0f : (float)intersection / union;
    }

    private static float LengthPenalty(string left, string right)
    {
        var max = Math.Max(left.Length, right.Length);
        if (max == 0)
        {
            return 1f;
        }

        var delta = Math.Abs(left.Length - right.Length);
        return Math.Clamp(1f - ((float)delta / max), 0f, 1f);
    }

    private static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var lower = text.Trim().ToLowerInvariant();
        return WsRegex.Replace(lower, " ");
    }
}
