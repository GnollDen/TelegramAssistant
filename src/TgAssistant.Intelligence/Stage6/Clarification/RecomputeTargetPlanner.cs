using System.Text.Json;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage6.Clarification;

public class RecomputeTargetPlanner : IRecomputeTargetPlanner
{
    private readonly IDependencyLinkRepository _dependencyLinkRepository;

    public RecomputeTargetPlanner(IDependencyLinkRepository dependencyLinkRepository)
    {
        _dependencyLinkRepository = dependencyLinkRepository;
    }

    public async Task<RecomputeTargetPlan> BuildPlanAsync(
        ClarificationQuestion question,
        ClarificationAnswer answer,
        IReadOnlyCollection<ClarificationDependencyUpdate> dependencyUpdates,
        IReadOnlyCollection<ConflictRecord> conflicts,
        CancellationToken ct = default)
    {
        var targets = new Dictionary<string, RecomputeTarget>(StringComparer.OrdinalIgnoreCase);

        AddImpactTargets(question, targets);
        AddJsonTargets(question.AffectedOutputsJson, "question_affected_output", targets);
        AddJsonTargets(answer.AffectedObjectsJson, "answer_affected_object", targets);

        if (question.PeriodId.HasValue)
        {
            AddTarget(targets, "periods", "period", question.PeriodId.Value.ToString(), "question_period_scope");
        }

        foreach (var update in dependencyUpdates)
        {
            AddTarget(targets, "state", "clarification_question", update.QuestionId.ToString(), "dependency_update");
        }

        foreach (var conflict in conflicts)
        {
            AddTarget(targets, "state", "conflict_record", conflict.Id.ToString(), "contradiction_conflict");
        }

        var dependentLinks = await _dependencyLinkRepository.GetByUpstreamAsync("clarification_question", question.Id.ToString(), ct);
        foreach (var link in dependentLinks)
        {
            if (string.Equals(link.DownstreamType, "clarification_question", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            AddMappedTarget(targets, link.DownstreamType, link.DownstreamId, "dependency_link");
        }

        EnsureMinimumLayers(targets, question);

        return new RecomputeTargetPlan
        {
            QuestionId = question.Id,
            AnswerId = answer.Id,
            Targets = targets.Values.OrderBy(x => x.Layer).ThenBy(x => x.TargetType).ThenBy(x => x.TargetId).ToList()
        };
    }

    private static void EnsureMinimumLayers(Dictionary<string, RecomputeTarget> targets, ClarificationQuestion question)
    {
        if (!targets.Values.Any(x => x.Layer == "periods"))
        {
            AddTarget(targets, "periods", "clarification_question", question.Id.ToString(), "default_local_recompute");
        }

        if (!targets.Values.Any(x => x.Layer == "state"))
        {
            AddTarget(targets, "state", "clarification_question", question.Id.ToString(), "default_local_recompute");
        }

        if (!targets.Values.Any(x => x.Layer == "profiles"))
        {
            AddTarget(targets, "profiles", "clarification_question", question.Id.ToString(), "default_local_recompute");
        }

        if (!targets.Values.Any(x => x.Layer == "strategy_artifacts"))
        {
            AddTarget(targets, "strategy_artifacts", "clarification_question", question.Id.ToString(), "default_local_recompute");
        }
    }

    private static void AddImpactTargets(ClarificationQuestion question, Dictionary<string, RecomputeTarget> targets)
    {
        var questionType = question.QuestionType.Trim();
        if (question.PeriodId.HasValue || questionType.Contains("period", StringComparison.OrdinalIgnoreCase) || questionType.Contains("timeline", StringComparison.OrdinalIgnoreCase))
        {
            AddTarget(targets, "periods", "clarification_question", question.Id.ToString(), "question_timeline_impact");
        }

        if (questionType.Contains("state", StringComparison.OrdinalIgnoreCase) || questionType.Contains("status", StringComparison.OrdinalIgnoreCase))
        {
            AddTarget(targets, "state", "clarification_question", question.Id.ToString(), "question_state_impact");
        }

        if (questionType.Contains("profile", StringComparison.OrdinalIgnoreCase) || questionType.Contains("trait", StringComparison.OrdinalIgnoreCase))
        {
            AddTarget(targets, "profiles", "clarification_question", question.Id.ToString(), "question_profile_impact");
        }

        if (questionType.Contains("strategy", StringComparison.OrdinalIgnoreCase) || questionType.Contains("draft", StringComparison.OrdinalIgnoreCase) || questionType.Contains("action", StringComparison.OrdinalIgnoreCase))
        {
            AddTarget(targets, "strategy_artifacts", "clarification_question", question.Id.ToString(), "question_strategy_impact");
        }
    }

    private static void AddJsonTargets(string json, string reason, Dictionary<string, RecomputeTarget> targets)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var token = item.GetString();
                    if (string.IsNullOrWhiteSpace(token))
                    {
                        continue;
                    }

                    AddMappedTarget(targets, token.Trim(), string.Empty, reason);
                    continue;
                }

                if (item.ValueKind == JsonValueKind.Object)
                {
                    var type = item.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;
                    var id = item.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
                    if (string.IsNullOrWhiteSpace(type))
                    {
                        continue;
                    }

                    AddMappedTarget(targets, type.Trim(), id?.Trim() ?? string.Empty, reason);
                }
            }
        }
        catch (JsonException)
        {
            // intentionally ignored; malformed auxiliary JSON should not break answer apply flow
        }
    }

    private static void AddMappedTarget(Dictionary<string, RecomputeTarget> targets, string token, string id, string reason)
    {
        var normalized = token.Trim().ToLowerInvariant();
        if (normalized.Contains("period") || normalized.Contains("timeline") || normalized.Contains("transition") || normalized.Contains("hypothesis"))
        {
            AddTarget(targets, "periods", normalized, NormalizeTargetId(id), reason);
            return;
        }

        if (normalized.Contains("state"))
        {
            AddTarget(targets, "state", normalized, NormalizeTargetId(id), reason);
            return;
        }

        if (normalized.Contains("profile") || normalized.Contains("trait"))
        {
            AddTarget(targets, "profiles", normalized, NormalizeTargetId(id), reason);
            return;
        }

        if (normalized.Contains("strategy") || normalized.Contains("draft"))
        {
            AddTarget(targets, "strategy_artifacts", normalized, NormalizeTargetId(id), reason);
            return;
        }

        if (normalized.Contains("clarification") || normalized.Contains("conflict") || normalized.Contains("inbox"))
        {
            AddTarget(targets, "state", normalized, NormalizeTargetId(id), reason);
        }
    }

    private static void AddTarget(Dictionary<string, RecomputeTarget> targets, string layer, string type, string id, string reason)
    {
        var normalizedId = NormalizeTargetId(id);
        var key = $"{layer}|{type}|{normalizedId}";
        if (targets.ContainsKey(key))
        {
            return;
        }

        targets[key] = new RecomputeTarget
        {
            Layer = layer,
            TargetType = type,
            TargetId = normalizedId,
            Reason = reason
        };
    }

    private static string NormalizeTargetId(string id)
    {
        return string.IsNullOrWhiteSpace(id) ? "*" : id.Trim();
    }
}
