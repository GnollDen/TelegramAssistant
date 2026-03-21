using TgAssistant.Core.Models;

namespace TgAssistant.Core.Interfaces;

public interface INetworkGraphService
{
    Task<NetworkGraphResult> BuildAsync(NetworkBuildRequest request, CancellationToken ct = default);
}

public interface INodeRoleResolver
{
    NetworkNode ResolveNode(
        Entity entity,
        IReadOnlyCollection<Relationship> relatedRelationships,
        IReadOnlyCollection<Hypothesis> hypotheses,
        IReadOnlyCollection<long> focalSenderIds);
}

public interface IInfluenceEdgeBuilder
{
    IReadOnlyList<NetworkInfluenceEdge> BuildInfluenceEdges(
        IReadOnlyDictionary<Guid, NetworkNode> nodesByEntityId,
        IReadOnlyCollection<Relationship> relationships,
        IReadOnlyCollection<Hypothesis> hypotheses);
}

public interface IInformationFlowBuilder
{
    IReadOnlyList<NetworkInformationFlowEdge> BuildInformationFlows(
        IReadOnlyDictionary<long, string> senderNodeMap,
        IReadOnlyCollection<Message> messages);
}

public interface INetworkScoringService
{
    void ApplyImportanceScores(
        IList<NetworkNode> nodes,
        IReadOnlyCollection<NetworkInfluenceEdge> influenceEdges,
        IReadOnlyCollection<NetworkInformationFlowEdge> informationFlows);
}

