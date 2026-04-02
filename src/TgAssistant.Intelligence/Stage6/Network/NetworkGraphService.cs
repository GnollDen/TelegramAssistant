// Legacy Stage6 surface retained for reference and diagnostics only. Not part of the PRD target architecture.

using System.Text.Json;
using Microsoft.Extensions.Logging;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage6.Network;

public class NetworkGraphService : INetworkGraphService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IMessageRepository _messageRepository;
    private readonly IEntityRepository _entityRepository;
    private readonly IRelationshipRepository _relationshipRepository;
    private readonly IPeriodRepository _periodRepository;
    private readonly IClarificationRepository _clarificationRepository;
    private readonly IOfflineEventRepository _offlineEventRepository;
    private readonly INodeRoleResolver _nodeRoleResolver;
    private readonly IInfluenceEdgeBuilder _influenceEdgeBuilder;
    private readonly IInformationFlowBuilder _informationFlowBuilder;
    private readonly INetworkScoringService _networkScoringService;
    private readonly ILogger<NetworkGraphService> _logger;

    public NetworkGraphService(
        IMessageRepository messageRepository,
        IEntityRepository entityRepository,
        IRelationshipRepository relationshipRepository,
        IPeriodRepository periodRepository,
        IClarificationRepository clarificationRepository,
        IOfflineEventRepository offlineEventRepository,
        INodeRoleResolver nodeRoleResolver,
        IInfluenceEdgeBuilder influenceEdgeBuilder,
        IInformationFlowBuilder informationFlowBuilder,
        INetworkScoringService networkScoringService,
        ILogger<NetworkGraphService> logger)
    {
        _messageRepository = messageRepository;
        _entityRepository = entityRepository;
        _relationshipRepository = relationshipRepository;
        _periodRepository = periodRepository;
        _clarificationRepository = clarificationRepository;
        _offlineEventRepository = offlineEventRepository;
        _nodeRoleResolver = nodeRoleResolver;
        _influenceEdgeBuilder = influenceEdgeBuilder;
        _informationFlowBuilder = informationFlowBuilder;
        _networkScoringService = networkScoringService;
        _logger = logger;
    }

    public async Task<NetworkGraphResult> BuildAsync(NetworkBuildRequest request, CancellationToken ct = default)
    {
        var asOf = request.AsOfUtc ?? DateTime.UtcNow;
        var messages = await _messageRepository.GetProcessedByChatAsync(request.ChatId, Math.Max(120, request.MessageLimit), ct);
        messages = messages.OrderByDescending(x => x.Timestamp).Take(request.MessageLimit).OrderBy(x => x.Timestamp).ToList();

        var focalSenderIds = messages
            .GroupBy(x => x.SenderId)
            .OrderByDescending(x => x.Count())
            .Take(2)
            .Select(x => x.Key)
            .ToHashSet();

        var entities = await _entityRepository.GetAllAsync(ct);
        var chatActorPrefix = request.ChatId.ToString() + ":";
        var senderIds = messages.Select(x => x.SenderId).Distinct().ToHashSet();

        var scopedEntities = entities
            .Where(x => (x.TelegramUserId.HasValue && senderIds.Contains(x.TelegramUserId.Value))
                        || (!string.IsNullOrWhiteSpace(x.ActorKey)
                            && x.ActorKey.StartsWith(chatActorPrefix, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        var relationships = new List<Relationship>();
        foreach (var entity in scopedEntities)
        {
            relationships.AddRange(await _relationshipRepository.GetByEntityAsync(entity.Id, ct));
        }

        relationships = relationships
            .GroupBy(x => x.Id)
            .Select(x => x.First())
            .ToList();

        var hypotheses = (await _periodRepository.GetHypothesesByCaseAsync(request.CaseId, null, ct))
            .Where(x => x.ChatId == null || x.ChatId == request.ChatId)
            .Where(x => x.Status.Equals("open", StringComparison.OrdinalIgnoreCase)
                || x.Status.Equals("in_review", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var nodes = new List<NetworkNode>();
        foreach (var entity in scopedEntities)
        {
            var related = relationships
                .Where(x => x.FromEntityId == entity.Id || x.ToEntityId == entity.Id)
                .ToList();
            var relatedHypotheses = hypotheses
                .Where(x => x.SubjectId.Equals($"entity:{entity.Id}", StringComparison.OrdinalIgnoreCase)
                    || x.SubjectId.Equals(entity.Id.ToString(), StringComparison.OrdinalIgnoreCase))
                .ToList();
            var node = _nodeRoleResolver.ResolveNode(entity, related, relatedHypotheses, focalSenderIds);
            nodes.Add(node);
        }

        // Canonical substrate may contain messages before entity merge completes; keep fallback sender nodes.
        var senderNodeMap = new Dictionary<long, string>();
        foreach (var node in nodes.Where(x => x.EntityId.HasValue))
        {
            var entityId = node.EntityId;
            if (!entityId.HasValue)
            {
                continue;
            }

            var entity = scopedEntities.FirstOrDefault(x => x.Id == entityId.Value);
            if (entity?.TelegramUserId is long senderId)
            {
                senderNodeMap[senderId] = node.NodeId;
            }
        }

        foreach (var senderId in senderIds)
        {
            if (senderNodeMap.ContainsKey(senderId))
            {
                continue;
            }

            var syntheticNode = new NetworkNode
            {
                NodeId = $"sender:{senderId}",
                NodeType = "people",
                DisplayName = $"Sender {senderId}",
                PrimaryRole = "friend",
                GlobalRole = "friend",
                IsFocalActor = focalSenderIds.Contains(senderId),
                Confidence = 0.4f,
                EvidenceRefs = [$"sender:{senderId}"]
            };
            nodes.Add(syntheticNode);
            senderNodeMap[senderId] = syntheticNode.NodeId;
        }

        var nodesByEntityId = nodes
            .Where(x => x.EntityId.HasValue)
            .ToDictionary(x => x.EntityId!.Value, x => x);

        var influenceEdges = _influenceEdgeBuilder.BuildInfluenceEdges(nodesByEntityId, relationships, hypotheses);
        var informationFlows = _informationFlowBuilder.BuildInformationFlows(senderNodeMap, messages);

        await PopulateContextLinksAsync(request, asOf, nodes, messages, ct);
        _networkScoringService.ApplyImportanceScores(nodes, influenceEdges, informationFlows);

        var result = new NetworkGraphResult
        {
            GeneratedAtUtc = DateTime.UtcNow,
            Nodes = nodes
                .OrderByDescending(x => x.ImportanceScore)
                .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            InfluenceEdges = influenceEdges
                .OrderByDescending(x => x.Confidence)
                .ThenBy(x => x.EdgeId, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            InformationFlows = informationFlows
                .OrderByDescending(x => x.Confidence)
                .ThenBy(x => x.EdgeId, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };

        _logger.LogInformation(
            "Network graph built: case_id={CaseId}, chat_id={ChatId}, nodes={NodeCount}, influence_edges={InfluenceCount}, information_flows={FlowCount}",
            request.CaseId,
            request.ChatId,
            result.Nodes.Count,
            result.InfluenceEdges.Count,
            result.InformationFlows.Count);

        return result;
    }

    private async Task PopulateContextLinksAsync(
        NetworkBuildRequest request,
        DateTime asOf,
        IReadOnlyCollection<NetworkNode> nodes,
        IReadOnlyCollection<Message> messages,
        CancellationToken ct)
    {
        var periods = (await _periodRepository.GetPeriodsByCaseAsync(request.CaseId, ct))
            .Where(x => x.ChatId == null || x.ChatId == request.ChatId)
            .OrderBy(x => x.StartAt)
            .ToList();
        var offlineEvents = (await _offlineEventRepository.GetOfflineEventsByCaseAsync(request.CaseId, ct))
            .Where(x => x.ChatId == null || x.ChatId == request.ChatId)
            .Where(x => x.TimestampStart <= asOf)
            .ToList();
        var clarifications = (await _clarificationRepository.GetQuestionsAsync(request.CaseId, null, null, ct))
            .Where(x => x.ChatId == null || x.ChatId == request.ChatId)
            .Take(250)
            .ToList();

        var messagePeriodsBySender = messages
            .GroupBy(x => x.SenderId)
            .ToDictionary(
                x => x.Key,
                x => x
                    .Select(m => ResolvePeriodId(periods, m.Timestamp))
                    .Where(id => id.HasValue)
                    .Select(id => id!.Value)
                    .Distinct()
                    .ToHashSet());

        foreach (var node in nodes)
        {
            if (node.NodeId.StartsWith("sender:", StringComparison.OrdinalIgnoreCase)
                && long.TryParse(node.NodeId["sender:".Length..], out var senderId)
                && messagePeriodsBySender.TryGetValue(senderId, out var periodIds))
            {
                node.LinkedPeriodIds = periodIds.Take(8).ToList();
            }

            foreach (var clarification in clarifications)
            {
                if (clarification.PeriodId.HasValue
                    && node.LinkedPeriodIds.Contains(clarification.PeriodId.Value))
                {
                    node.LinkedClarificationIds.Add(clarification.Id);
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(node.DisplayName)
                    && clarification.QuestionText.Contains(node.DisplayName, StringComparison.OrdinalIgnoreCase))
                {
                    node.LinkedClarificationIds.Add(clarification.Id);
                    if (clarification.PeriodId.HasValue)
                    {
                        node.LinkedPeriodIds.Add(clarification.PeriodId.Value);
                    }
                }
            }

            foreach (var evt in offlineEvents)
            {
                if (evt.PeriodId.HasValue && node.LinkedPeriodIds.Contains(evt.PeriodId.Value))
                {
                    node.LinkedOfflineEventIds.Add(evt.Id);
                    continue;
                }

                if (IsEventLinkedToNode(evt, node.DisplayName))
                {
                    node.LinkedOfflineEventIds.Add(evt.Id);
                    if (evt.PeriodId.HasValue)
                    {
                        node.LinkedPeriodIds.Add(evt.PeriodId.Value);
                    }
                }
            }

            node.LinkedPeriodIds = node.LinkedPeriodIds.Distinct().Take(10).ToList();
            node.LinkedOfflineEventIds = node.LinkedOfflineEventIds.Distinct().Take(10).ToList();
            node.LinkedClarificationIds = node.LinkedClarificationIds.Distinct().Take(10).ToList();
        }
    }

    private static Guid? ResolvePeriodId(IReadOnlyCollection<Period> periods, DateTime timestamp)
    {
        return periods
            .Where(x => x.StartAt <= timestamp && (x.EndAt == null || x.EndAt >= timestamp))
            .OrderByDescending(x => x.StartAt)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefault();
    }

    private static bool IsEventLinkedToNode(OfflineEvent evt, string nodeName)
    {
        if (string.IsNullOrWhiteSpace(nodeName))
        {
            return false;
        }

        if (evt.Title.Contains(nodeName, StringComparison.OrdinalIgnoreCase)
            || evt.UserSummary.Contains(nodeName, StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrWhiteSpace(evt.AutoSummary)
                && evt.AutoSummary.Contains(nodeName, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var evidenceRefs = ParseJsonStringList(evt.EvidenceRefsJson);
        return evidenceRefs.Any(x => x.Contains(nodeName, StringComparison.OrdinalIgnoreCase));
    }

    private static List<string> ParseJsonStringList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<List<string>>(json, JsonOptions);
            return parsed?
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .ToList() ?? [];
        }
        catch
        {
            return [];
        }
    }
}
