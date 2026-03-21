namespace TgAssistant.Core.Models;

public class NetworkBuildRequest
{
    public long CaseId { get; set; }
    public long ChatId { get; set; }
    public string Actor { get; set; } = "system";
    public DateTime? AsOfUtc { get; set; }
    public int MessageLimit { get; set; } = 500;
}

public class NetworkGraphResult
{
    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
    public List<NetworkNode> Nodes { get; set; } = [];
    public List<NetworkInfluenceEdge> InfluenceEdges { get; set; } = [];
    public List<NetworkInformationFlowEdge> InformationFlows { get; set; } = [];
}

public class NetworkNode
{
    public string NodeId { get; set; } = string.Empty;
    public string NodeType { get; set; } = string.Empty;
    public Guid? EntityId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string PrimaryRole { get; set; } = string.Empty;
    public List<string> AdditionalRoles { get; set; } = [];
    public string GlobalRole { get; set; } = string.Empty;
    public List<NetworkRoleContext> RoleContexts { get; set; } = [];
    public bool IsFocalActor { get; set; }
    public float ImportanceScore { get; set; }
    public float Confidence { get; set; }
    public List<Guid> LinkedPeriodIds { get; set; } = [];
    public List<Guid> LinkedOfflineEventIds { get; set; } = [];
    public List<Guid> LinkedClarificationIds { get; set; } = [];
    public List<string> EvidenceRefs { get; set; } = [];
}

public class NetworkRoleContext
{
    public string Role { get; set; } = string.Empty;
    public Guid? PeriodId { get; set; }
    public string ContextSource { get; set; } = string.Empty;
    public float Confidence { get; set; }
}

public class NetworkInfluenceEdge
{
    public string EdgeId { get; set; } = string.Empty;
    public string FromNodeId { get; set; } = string.Empty;
    public string ToNodeId { get; set; } = string.Empty;
    public string InfluenceType { get; set; } = string.Empty;
    public float Confidence { get; set; }
    public bool IsHypothesis { get; set; }
    public Guid? LinkedPeriodId { get; set; }
    public List<string> EvidenceRefs { get; set; } = [];
}

public class NetworkInformationFlowEdge
{
    public string EdgeId { get; set; } = string.Empty;
    public string FromNodeId { get; set; } = string.Empty;
    public string ToNodeId { get; set; } = string.Empty;
    public string FlowType { get; set; } = "informational";
    public string Direction { get; set; } = "direct";
    public float Confidence { get; set; }
    public Guid? LinkedPeriodId { get; set; }
    public List<string> EvidenceRefs { get; set; } = [];
}

