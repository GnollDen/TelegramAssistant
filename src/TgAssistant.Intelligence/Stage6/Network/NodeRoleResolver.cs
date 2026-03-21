using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage6.Network;

public class NodeRoleResolver : INodeRoleResolver
{
    private static readonly HashSet<string> AllowedRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "friend",
        "close_friend",
        "family",
        "ex_partner",
        "new_interest",
        "bridge",
        "conflict_source",
        "advisor",
        "group",
        "place",
        "work_context"
    };

    public NetworkNode ResolveNode(
        Entity entity,
        IReadOnlyCollection<Relationship> relatedRelationships,
        IReadOnlyCollection<Hypothesis> hypotheses,
        IReadOnlyCollection<long> focalSenderIds)
    {
        var roleHints = new List<string>();
        var contexts = new List<NetworkRoleContext>();

        foreach (var relationship in relatedRelationships)
        {
            var role = MapRelationshipRole(relationship.Type);
            if (!string.IsNullOrWhiteSpace(role))
            {
                roleHints.Add(role);
                contexts.Add(new NetworkRoleContext
                {
                    Role = role,
                    ContextSource = $"relationship:{relationship.Id}",
                    Confidence = relationship.Confidence
                });
            }
        }

        foreach (var hypothesis in hypotheses)
        {
            var role = NormalizeRoleLabel(hypothesis.HypothesisType);
            if (string.IsNullOrWhiteSpace(role))
            {
                continue;
            }

            roleHints.Add(role);
            contexts.Add(new NetworkRoleContext
            {
                Role = role,
                PeriodId = hypothesis.PeriodId,
                ContextSource = $"hypothesis:{hypothesis.Id}",
                Confidence = hypothesis.Confidence
            });
        }

        var entityRole = MapEntityTypeRole(entity);
        if (!string.IsNullOrWhiteSpace(entityRole))
        {
            roleHints.Add(entityRole);
        }

        var normalizedRoles = roleHints
            .Select(NormalizeRoleLabel)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var primaryRole = normalizedRoles.FirstOrDefault() ?? "friend";
        var globalRole = primaryRole;
        var additionalRoles = normalizedRoles
            .Where(x => !x.Equals(primaryRole, StringComparison.OrdinalIgnoreCase))
            .Take(4)
            .ToList();

        return new NetworkNode
        {
            NodeId = $"entity:{entity.Id}",
            NodeType = MapNodeType(entity),
            EntityId = entity.Id,
            DisplayName = entity.Name,
            PrimaryRole = primaryRole,
            AdditionalRoles = additionalRoles,
            GlobalRole = globalRole,
            RoleContexts = contexts
                .Where(x => !string.IsNullOrWhiteSpace(x.Role))
                .Take(12)
                .ToList(),
            IsFocalActor = entity.TelegramUserId.HasValue && focalSenderIds.Contains(entity.TelegramUserId.Value),
            Confidence = BuildNodeConfidence(entity, relatedRelationships, hypotheses),
            EvidenceRefs =
            [
                entity.ActorKey ?? $"entity:{entity.Id}",
                ..relatedRelationships.Take(4).Select(x => $"relationship:{x.Id}")
            ]
        };
    }

    private static string MapNodeType(Entity entity)
    {
        return entity.Type switch
        {
            EntityType.Person => "people",
            EntityType.Place => "places",
            EntityType.Organization => GuessOrganizationNodeType(entity.Name),
            _ => "people"
        };
    }

    private static string MapEntityTypeRole(Entity entity)
    {
        return entity.Type switch
        {
            EntityType.Place => "place",
            EntityType.Organization => GuessOrganizationNodeType(entity.Name) switch
            {
                "groups" => "group",
                _ => "work_context"
            },
            _ => string.Empty
        };
    }

    private static string GuessOrganizationNodeType(string name)
    {
        var normalized = name.ToLowerInvariant();
        if (normalized.Contains("group", StringComparison.Ordinal)
            || normalized.Contains("team", StringComparison.Ordinal)
            || normalized.Contains("chat", StringComparison.Ordinal)
            || normalized.Contains("club", StringComparison.Ordinal))
        {
            return "groups";
        }

        return "work_contexts";
    }

    private static string MapRelationshipRole(string relationshipType)
    {
        var normalized = relationshipType.ToLowerInvariant();
        if (normalized.Contains("close_friend", StringComparison.Ordinal) || normalized.Contains("best friend", StringComparison.Ordinal))
        {
            return "close_friend";
        }

        if (normalized.Contains("friend", StringComparison.Ordinal))
        {
            return "friend";
        }

        if (normalized.Contains("family", StringComparison.Ordinal) || normalized.Contains("sibling", StringComparison.Ordinal))
        {
            return "family";
        }

        if (normalized.Contains("ex", StringComparison.Ordinal))
        {
            return "ex_partner";
        }

        if (normalized.Contains("interest", StringComparison.Ordinal) || normalized.Contains("romantic", StringComparison.Ordinal))
        {
            return "new_interest";
        }

        if (normalized.Contains("bridge", StringComparison.Ordinal))
        {
            return "bridge";
        }

        if (normalized.Contains("conflict", StringComparison.Ordinal) || normalized.Contains("toxic", StringComparison.Ordinal))
        {
            return "conflict_source";
        }

        if (normalized.Contains("advisor", StringComparison.Ordinal) || normalized.Contains("mentor", StringComparison.Ordinal))
        {
            return "advisor";
        }

        return string.Empty;
    }

    private static string NormalizeRoleLabel(string? role)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            return string.Empty;
        }

        var normalized = role.Trim().ToLowerInvariant().Replace(' ', '_');
        return AllowedRoles.Contains(normalized) ? normalized : string.Empty;
    }

    private static float BuildNodeConfidence(
        Entity entity,
        IReadOnlyCollection<Relationship> relatedRelationships,
        IReadOnlyCollection<Hypothesis> hypotheses)
    {
        var baseConfidence = entity.IsUserConfirmed ? 0.85f : 0.6f;
        if (relatedRelationships.Count > 0)
        {
            baseConfidence += Math.Clamp(relatedRelationships.Max(x => x.Confidence) * 0.15f, 0f, 0.15f);
        }

        if (hypotheses.Count > 0)
        {
            baseConfidence = Math.Clamp(baseConfidence - 0.05f, 0f, 1f);
        }

        return Math.Clamp(baseConfidence, 0.1f, 1f);
    }
}

