using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage6.Network;

public class InformationFlowBuilder : IInformationFlowBuilder
{
    public IReadOnlyList<NetworkInformationFlowEdge> BuildInformationFlows(
        IReadOnlyDictionary<long, string> senderNodeMap,
        IReadOnlyCollection<Message> messages)
    {
        var byTelegramId = messages
            .Where(x => x.TelegramMessageId > 0)
            .GroupBy(x => x.TelegramMessageId)
            .ToDictionary(x => x.Key, x => x.OrderByDescending(m => m.Timestamp).First());

        var edges = new List<NetworkInformationFlowEdge>();
        foreach (var message in messages.Where(x => x.ReplyToMessageId.HasValue))
        {
            if (!byTelegramId.TryGetValue(message.ReplyToMessageId!.Value, out var referenced))
            {
                continue;
            }

            if (!senderNodeMap.TryGetValue(message.SenderId, out var fromNodeId)
                || !senderNodeMap.TryGetValue(referenced.SenderId, out var toNodeId))
            {
                continue;
            }

            edges.Add(new NetworkInformationFlowEdge
            {
                EdgeId = $"reply:{message.Id}:{referenced.Id}",
                FromNodeId = fromNodeId,
                ToNodeId = toNodeId,
                FlowType = "informational",
                Direction = "direct_reply",
                Confidence = 0.68f,
                EvidenceRefs = [$"message:{message.Id}", $"message:{referenced.Id}"]
            });
        }

        if (edges.Count == 0)
        {
            // Fallback flow edge so seeded chats without explicit reply pointers still show directional flow.
            var ordered = messages.OrderBy(x => x.Timestamp).Take(200).ToList();
            for (var i = 1; i < ordered.Count; i++)
            {
                if (ordered[i].SenderId == ordered[i - 1].SenderId)
                {
                    continue;
                }

                if (!senderNodeMap.TryGetValue(ordered[i].SenderId, out var fromNodeId)
                    || !senderNodeMap.TryGetValue(ordered[i - 1].SenderId, out var toNodeId))
                {
                    continue;
                }

                edges.Add(new NetworkInformationFlowEdge
                {
                    EdgeId = $"seq:{ordered[i - 1].Id}:{ordered[i].Id}",
                    FromNodeId = fromNodeId,
                    ToNodeId = toNodeId,
                    FlowType = "informational",
                    Direction = "turn_taking",
                    Confidence = 0.44f,
                    EvidenceRefs = [$"message:{ordered[i - 1].Id}", $"message:{ordered[i].Id}"]
                });
                break;
            }
        }

        return edges;
    }
}
