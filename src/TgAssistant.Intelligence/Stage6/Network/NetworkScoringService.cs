// Legacy Stage6 surface retained for reference and diagnostics only. Not part of the PRD target architecture.

using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage6.Network;

public class NetworkScoringService : INetworkScoringService
{
    public void ApplyImportanceScores(
        IList<NetworkNode> nodes,
        IReadOnlyCollection<NetworkInfluenceEdge> influenceEdges,
        IReadOnlyCollection<NetworkInformationFlowEdge> informationFlows)
    {
        if (nodes.Count == 0)
        {
            return;
        }

        var nodeById = nodes.ToDictionary(x => x.NodeId, StringComparer.OrdinalIgnoreCase);
        var rawScores = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

        foreach (var node in nodes)
        {
            rawScores[node.NodeId] = node.IsFocalActor ? 0.55f : 0.25f;
        }

        foreach (var edge in influenceEdges)
        {
            if (rawScores.ContainsKey(edge.FromNodeId))
            {
                rawScores[edge.FromNodeId] += Math.Clamp(edge.Confidence * 0.3f, 0.05f, 0.3f);
            }

            if (rawScores.ContainsKey(edge.ToNodeId))
            {
                rawScores[edge.ToNodeId] += Math.Clamp(edge.Confidence * 0.4f, 0.05f, 0.35f);
            }
        }

        foreach (var edge in informationFlows)
        {
            if (rawScores.ContainsKey(edge.FromNodeId))
            {
                rawScores[edge.FromNodeId] += Math.Clamp(edge.Confidence * 0.15f, 0.03f, 0.16f);
            }

            if (rawScores.ContainsKey(edge.ToNodeId))
            {
                rawScores[edge.ToNodeId] += Math.Clamp(edge.Confidence * 0.2f, 0.04f, 0.18f);
            }
        }

        foreach (var node in nodes)
        {
            var linkageWeight = (node.LinkedPeriodIds.Count * 0.04f)
                + (node.LinkedOfflineEventIds.Count * 0.03f)
                + (node.LinkedClarificationIds.Count * 0.03f);
            rawScores[node.NodeId] += Math.Clamp(linkageWeight, 0f, 0.22f);
        }

        var max = rawScores.Values.DefaultIfEmpty(1f).Max();
        var min = rawScores.Values.DefaultIfEmpty(0f).Min();
        var spread = Math.Max(0.0001f, max - min);

        foreach (var kv in rawScores)
        {
            if (!nodeById.TryGetValue(kv.Key, out var node))
            {
                continue;
            }

            node.ImportanceScore = Math.Clamp((kv.Value - min) / spread, 0f, 1f);
        }
    }
}

