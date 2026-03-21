using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage6.Network;

public class InfluenceEdgeBuilder : IInfluenceEdgeBuilder
{
    public IReadOnlyList<NetworkInfluenceEdge> BuildInfluenceEdges(
        IReadOnlyDictionary<Guid, NetworkNode> nodesByEntityId,
        IReadOnlyCollection<Relationship> relationships,
        IReadOnlyCollection<Hypothesis> hypotheses)
    {
        var edges = new List<NetworkInfluenceEdge>();

        foreach (var relationship in relationships)
        {
            if (!nodesByEntityId.TryGetValue(relationship.FromEntityId, out var fromNode)
                || !nodesByEntityId.TryGetValue(relationship.ToEntityId, out var toNode))
            {
                continue;
            }

            var influenceType = MapInfluenceType(relationship.Type);
            var isHypothesis = relationship.Status == ConfidenceStatus.Tentative || relationship.Confidence < 0.55f;
            edges.Add(new NetworkInfluenceEdge
            {
                EdgeId = $"rel:{relationship.Id}",
                FromNodeId = fromNode.NodeId,
                ToNodeId = toNode.NodeId,
                InfluenceType = influenceType,
                Confidence = relationship.Confidence,
                IsHypothesis = isHypothesis,
                EvidenceRefs =
                [
                    $"relationship:{relationship.Id}",
                    relationship.SourceMessageId.HasValue ? $"message:{relationship.SourceMessageId}" : "relationship:no_message"
                ]
            });
        }

        foreach (var hypothesis in hypotheses.Where(x => x.HypothesisType.Equals("network_influence", StringComparison.OrdinalIgnoreCase)))
        {
            var parsed = ParseHypothesisEdge(hypothesis.SubjectId);
            if (parsed == null)
            {
                continue;
            }

            edges.Add(new NetworkInfluenceEdge
            {
                EdgeId = $"hyp:{hypothesis.Id}",
                FromNodeId = parsed.Value.FromNodeId,
                ToNodeId = parsed.Value.ToNodeId,
                InfluenceType = MapInfluenceType(hypothesis.Statement),
                Confidence = Math.Clamp(hypothesis.Confidence, 0.2f, 0.75f),
                IsHypothesis = true,
                LinkedPeriodId = hypothesis.PeriodId,
                EvidenceRefs = [$"hypothesis:{hypothesis.Id}"]
            });
        }

        return edges
            .GroupBy(x => new { x.FromNodeId, x.ToNodeId, x.InfluenceType, x.EdgeId })
            .Select(x => x.First())
            .ToList();
    }

    private static (string FromNodeId, string ToNodeId)? ParseHypothesisEdge(string subjectId)
    {
        if (string.IsNullOrWhiteSpace(subjectId))
        {
            return null;
        }

        var tokens = subjectId.Split("->", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length != 2)
        {
            return null;
        }

        return (tokens[0], tokens[1]);
    }

    private static string MapInfluenceType(string source)
    {
        var normalized = source.ToLowerInvariant();
        if (normalized.Contains("support", StringComparison.Ordinal)
            || normalized.Contains("friend", StringComparison.Ordinal)
            || normalized.Contains("advisor", StringComparison.Ordinal))
        {
            return "supportive";
        }

        if (normalized.Contains("complicat", StringComparison.Ordinal)
            || normalized.Contains("conflict", StringComparison.Ordinal))
        {
            return "complicating";
        }

        if (normalized.Contains("mediat", StringComparison.Ordinal)
            || normalized.Contains("bridge", StringComparison.Ordinal))
        {
            return "mediating";
        }

        if (normalized.Contains("inform", StringComparison.Ordinal))
        {
            return "informational";
        }

        if (normalized.Contains("stabil", StringComparison.Ordinal))
        {
            return "stabilizing";
        }

        if (normalized.Contains("destabil", StringComparison.Ordinal)
            || normalized.Contains("ex_", StringComparison.Ordinal)
            || normalized.Contains("ex ", StringComparison.Ordinal))
        {
            return "destabilizing";
        }

        return "informational";
    }
}

