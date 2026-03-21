using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Web.Read;

public class NetworkVerificationService
{
    private readonly IMessageRepository _messageRepository;
    private readonly IEntityRepository _entityRepository;
    private readonly IRelationshipRepository _relationshipRepository;
    private readonly IPeriodRepository _periodRepository;
    private readonly IClarificationRepository _clarificationRepository;
    private readonly IOfflineEventRepository _offlineEventRepository;
    private readonly INetworkGraphService _networkGraphService;
    private readonly IWebRouteRenderer _webRouteRenderer;

    public NetworkVerificationService(
        IMessageRepository messageRepository,
        IEntityRepository entityRepository,
        IRelationshipRepository relationshipRepository,
        IPeriodRepository periodRepository,
        IClarificationRepository clarificationRepository,
        IOfflineEventRepository offlineEventRepository,
        INetworkGraphService networkGraphService,
        IWebRouteRenderer webRouteRenderer)
    {
        _messageRepository = messageRepository;
        _entityRepository = entityRepository;
        _relationshipRepository = relationshipRepository;
        _periodRepository = periodRepository;
        _clarificationRepository = clarificationRepository;
        _offlineEventRepository = offlineEventRepository;
        _networkGraphService = networkGraphService;
        _webRouteRenderer = webRouteRenderer;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        var scope = CaseScopeFactory.CreateSmokeScope("network");
        var now = DateTime.UtcNow;
        var caseId = scope.CaseId;
        var chatId = scope.ChatId;

        await SeedMessagesAsync(chatId, now, ct);

        var self = await _entityRepository.UpsertAsync(new Entity
        {
            Type = EntityType.Person,
            Name = "Self Network",
            TelegramUserId = 1001,
            ActorKey = $"{chatId}:1001",
            IsUserConfirmed = true
        }, ct);
        var other = await _entityRepository.UpsertAsync(new Entity
        {
            Type = EntityType.Person,
            Name = "Other Network",
            TelegramUserId = 2002,
            ActorKey = $"{chatId}:2002",
            IsUserConfirmed = true
        }, ct);
        var friend = await _entityRepository.UpsertAsync(new Entity
        {
            Type = EntityType.Person,
            Name = "Bridge Friend",
            ActorKey = $"{chatId}:3003"
        }, ct);
        var office = await _entityRepository.UpsertAsync(new Entity
        {
            Type = EntityType.Organization,
            Name = "Work Team",
            ActorKey = $"{chatId}:4004"
        }, ct);
        var place = await _entityRepository.UpsertAsync(new Entity
        {
            Type = EntityType.Place,
            Name = "Cafe Aurora",
            ActorKey = $"{chatId}:5005"
        }, ct);

        await _relationshipRepository.UpsertAsync(new Relationship
        {
            FromEntityId = friend.Id,
            ToEntityId = other.Id,
            Type = "bridge_supportive",
            Status = ConfidenceStatus.Confirmed,
            Confidence = 0.76f
        }, ct);
        await _relationshipRepository.UpsertAsync(new Relationship
        {
            FromEntityId = office.Id,
            ToEntityId = other.Id,
            Type = "work_conflict_source",
            Status = ConfidenceStatus.Tentative,
            Confidence = 0.49f
        }, ct);
        await _relationshipRepository.UpsertAsync(new Relationship
        {
            FromEntityId = self.Id,
            ToEntityId = place.Id,
            Type = "favorite_place",
            Status = ConfidenceStatus.Tentative,
            Confidence = 0.52f
        }, ct);

        var period = await _periodRepository.CreatePeriodAsync(new Period
        {
            CaseId = caseId,
            ChatId = chatId,
            Label = "network_seed_period",
            StartAt = now.AddDays(-8),
            EndAt = null,
            IsOpen = true,
            Summary = "network seed period",
            SourceType = "smoke",
            SourceId = "network"
        }, ct);

        await _periodRepository.CreateHypothesisAsync(new Hypothesis
        {
            CaseId = caseId,
            ChatId = chatId,
            PeriodId = period.Id,
            HypothesisType = "network_influence",
            SubjectType = "edge",
            SubjectId = $"entity:{office.Id}->entity:{other.Id}",
            Statement = "destabilizing influence from work context",
            Confidence = 0.41f,
            Status = "open",
            SourceType = "smoke",
            SourceId = "network"
        }, ct);

        await _clarificationRepository.CreateQuestionAsync(new ClarificationQuestion
        {
            CaseId = caseId,
            ChatId = chatId,
            PeriodId = period.Id,
            QuestionText = "Is Bridge Friend reducing pressure in this period?",
            QuestionType = "network_influence",
            Priority = "important",
            Status = "open",
            WhyItMatters = "Changes third-party influence interpretation",
            SourceType = "smoke",
            SourceId = "network"
        }, ct);

        await _offlineEventRepository.CreateOfflineEventAsync(new OfflineEvent
        {
            CaseId = caseId,
            ChatId = chatId,
            PeriodId = period.Id,
            EventType = "meeting",
            Title = "Cafe Aurora meetup with Bridge Friend",
            UserSummary = "Bridge Friend helped de-escalate the conversation",
            TimestampStart = now.AddDays(-3),
            TimestampEnd = now.AddDays(-3).AddHours(1),
            EvidenceRefsJson = "[\"Bridge Friend\",\"Cafe Aurora\",\"de-escalation\"]",
            SourceType = "smoke",
            SourceId = "network"
        }, ct);

        var graph = await _networkGraphService.BuildAsync(new NetworkBuildRequest
        {
            CaseId = caseId,
            ChatId = chatId,
            Actor = "network_smoke",
            MessageLimit = 250
        }, ct);

        if (graph.Nodes.Count < 3)
        {
            throw new InvalidOperationException("Network smoke failed: nodes were not assembled.");
        }

        if (!graph.Nodes.Any(x => !string.IsNullOrWhiteSpace(x.PrimaryRole)))
        {
            throw new InvalidOperationException("Network smoke failed: node roles are not visible.");
        }

        if (graph.InfluenceEdges.Count == 0)
        {
            throw new InvalidOperationException("Network smoke failed: influence edges are missing.");
        }

        if (!graph.InformationFlows.Any())
        {
            throw new InvalidOperationException("Network smoke failed: information flow edges are missing.");
        }

        var request = new WebReadRequest { CaseId = caseId, ChatId = chatId, Actor = "network_smoke" };
        var page = await _webRouteRenderer.RenderAsync("/network", request, ct)
            ?? throw new InvalidOperationException("Network smoke failed: /network route did not resolve.");
        if (string.IsNullOrWhiteSpace(page.Html)
            || !page.Html.Contains("Network", StringComparison.OrdinalIgnoreCase)
            || !page.Html.Contains("Influence", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Network smoke failed: /network page did not render expected content.");
        }

    }

    private async Task SeedMessagesAsync(long chatId, DateTime now, CancellationToken ct)
    {
        var telegramId = 980_000_000_000L + (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() % 100_000_000L);
        var rows = new List<Message>
        {
            new()
            {
                TelegramMessageId = telegramId + 1,
                ChatId = chatId,
                SenderId = 1001,
                SenderName = "Self",
                Timestamp = now.AddDays(-6),
                Text = "How was your week with work pressure?",
                ProcessingStatus = ProcessingStatus.Processed,
                Source = MessageSource.Archive,
                CreatedAt = DateTime.UtcNow
            },
            new()
            {
                TelegramMessageId = telegramId + 2,
                ChatId = chatId,
                SenderId = 2002,
                SenderName = "Other",
                Timestamp = now.AddDays(-6).AddMinutes(2),
                ReplyToMessageId = telegramId + 1,
                Text = "Hard week, but Bridge Friend helped us calm down.",
                ProcessingStatus = ProcessingStatus.Processed,
                Source = MessageSource.Archive,
                CreatedAt = DateTime.UtcNow
            },
            new()
            {
                TelegramMessageId = telegramId + 3,
                ChatId = chatId,
                SenderId = 1001,
                SenderName = "Self",
                Timestamp = now.AddDays(-5),
                ReplyToMessageId = telegramId + 2,
                Text = "Let's meet at Cafe Aurora again.",
                ProcessingStatus = ProcessingStatus.Processed,
                Source = MessageSource.Archive,
                CreatedAt = DateTime.UtcNow
            }
        };

        _ = await _messageRepository.SaveBatchAsync(rows, ct);
    }
}
