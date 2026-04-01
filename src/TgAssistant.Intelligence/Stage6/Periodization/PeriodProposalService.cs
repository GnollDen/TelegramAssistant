using System.Text.Json;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage6.Periodization;

public class PeriodProposalService : IPeriodProposalService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IInboxConflictRepository _inboxConflictRepository;
    private readonly IDomainReviewEventRepository _domainReviewEventRepository;

    public PeriodProposalService(
        IInboxConflictRepository inboxConflictRepository,
        IDomainReviewEventRepository domainReviewEventRepository)
    {
        _inboxConflictRepository = inboxConflictRepository;
        _domainReviewEventRepository = domainReviewEventRepository;
    }

    public async Task<IReadOnlyList<PeriodProposalRecord>> BuildAndPersistProposalsAsync(
        PeriodizationRunRequest request,
        IReadOnlyList<Period> periods,
        IReadOnlyList<PeriodTransition> transitions,
        IReadOnlyList<ConflictRecord> conflicts,
        CancellationToken ct = default)
    {
        var proposals = new List<PeriodProposalRecord>();

        for (var i = 0; i < periods.Count - 1; i++)
        {
            var left = periods[i];
            var right = periods[i + 1];
            var transition = transitions.FirstOrDefault(x => x.FromPeriodId == left.Id && x.ToPeriodId == right.Id);
            if (transition == null)
            {
                continue;
            }

            var leftHours = ((left.EndAt ?? DateTime.UtcNow) - left.StartAt).TotalHours;
            var rightHours = ((right.EndAt ?? DateTime.UtcNow) - right.StartAt).TotalHours;
            var unresolvedOrWeak = !transition.IsResolved || transition.Confidence < 0.55f;
            var shouldMerge = (leftHours <= 24 || rightHours <= 24 || unresolvedOrWeak)
                              && (unresolvedOrWeak || (left.InterpretationConfidence + right.InterpretationConfidence) / 2f < 0.55f);
            if (!shouldMerge)
            {
                continue;
            }

            var leftSignal = ReadPeriodSignal(left);
            var rightSignal = ReadPeriodSignal(right);
            var weakTransition = !transition.IsResolved || transition.Confidence < 0.55f;
            if (weakTransition && leftSignal.IsEmpty && rightSignal.IsEmpty)
            {
                continue;
            }

            var proposal = new PeriodProposalRecord
            {
                ProposalType = "merge",
                PeriodIds = [left.Id, right.Id],
                Summary = "Likely merge: adjacent short/low-confidence periods separated by weak or unresolved transition.",
                ReviewPriority = 4
            };

            var inbox = await PersistProposalInboxItemAsync(request, proposal, ct);
            proposal.InboxItemId = inbox.Id;
            proposals.Add(proposal);
        }

        foreach (var period in periods)
        {
            if (period.IsOpen)
            {
                continue;
            }

            var durationDays = ((period.EndAt ?? DateTime.UtcNow) - period.StartAt).TotalDays;
            var messageCount = ReadMessageCount(period.KeySignalsJson);
            var hasVolatility = period.KeySignalsJson.Contains("volatility_high", StringComparison.OrdinalIgnoreCase);
            if (durationDays < 14 || messageCount < 140 || !hasVolatility)
            {
                continue;
            }

            var localConflictCount = conflicts.Count(x => x.PeriodId == period.Id && x.Status.Equals("open", StringComparison.OrdinalIgnoreCase));
            var proposal = new PeriodProposalRecord
            {
                ProposalType = "split",
                PeriodIds = [period.Id],
                Summary = localConflictCount > 0
                    ? "Likely split: long high-volatility period with open conflicts."
                    : "Likely split: long high-volatility period indicates potential hidden boundary.",
                ReviewPriority = (short)Math.Clamp(3 + localConflictCount, 3, 5)
            };

            var inbox = await PersistProposalInboxItemAsync(request, proposal, ct);
            proposal.InboxItemId = inbox.Id;
            proposals.Add(proposal);
        }

        return proposals;
    }

    private async Task<InboxItem> PersistProposalInboxItemAsync(PeriodizationRunRequest request, PeriodProposalRecord proposal, CancellationToken ct)
    {
        var sourceObjectType = proposal.ProposalType == "merge" ? "period_merge_proposal" : "period_split_proposal";
        var sourceObjectId = JsonSerializer.Serialize(proposal.PeriodIds, JsonOptions);
        var inbox = await _inboxConflictRepository.CreateInboxItemAsync(new InboxItem
        {
            Id = Guid.NewGuid(),
            ItemType = sourceObjectType,
            SourceObjectType = sourceObjectType,
            SourceObjectId = sourceObjectId,
            Priority = proposal.ReviewPriority >= 4 ? "blocking" : "important",
            IsBlocking = proposal.ReviewPriority >= 4,
            Title = proposal.ProposalType == "merge" ? "Review merge proposal" : "Review split proposal",
            Summary = proposal.Summary,
            CaseId = request.CaseId,
            ChatId = request.ChatId,
            Status = "open",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            LastActor = request.Actor,
            LastReason = "periodization_mvp"
        }, ct);

        await _domainReviewEventRepository.AddAsync(new DomainReviewEvent
        {
            ObjectType = "inbox_item",
            ObjectId = inbox.Id.ToString(),
            Action = "period_proposal_created",
            NewValueRef = JsonSerializer.Serialize(new
            {
                proposal.ProposalType,
                proposal.ReviewPriority,
                proposal.PeriodIds
            }, JsonOptions),
            Reason = "periodization_mvp",
            Actor = request.Actor,
            CreatedAt = DateTime.UtcNow
        }, ct);

        return inbox;
    }

    private static int ReadMessageCount(string keySignalsJson)
    {
        return ReadCountSignal(keySignalsJson, "message_count");
    }

    private static PeriodSignal ReadPeriodSignal(Period period)
    {
        var messageCount = ReadCountSignal(period.KeySignalsJson, "message_count");
        var eventCount = ReadCountSignal(period.KeySignalsJson, "offline_event_count");
        return new PeriodSignal(messageCount, eventCount, period.OpenQuestionsCount);
    }

    private static int ReadCountSignal(string keySignalsJson, string key)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(keySignalsJson) ? "[]" : keySignalsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return 0;
            }

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var token = item.GetString();
                if (string.IsNullOrWhiteSpace(token))
                {
                    continue;
                }

                var prefix = $"{key}:";
                if (!token.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (int.TryParse(token[prefix.Length..], out var parsed))
                {
                    return parsed;
                }
            }

            return 0;
        }
        catch (JsonException)
        {
            return 0;
        }
    }

    private readonly record struct PeriodSignal(int MessageCount, int EventCount, int OpenQuestionsCount)
    {
        public bool IsEmpty => MessageCount <= 0 && EventCount <= 0 && OpenQuestionsCount <= 0;
    }
}
