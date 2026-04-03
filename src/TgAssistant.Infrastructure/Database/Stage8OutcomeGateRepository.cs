using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Infrastructure.Database;

public class Stage8OutcomeGateRepository : IStage8OutcomeGateRepository
{
    private static readonly IReadOnlyDictionary<string, string[]> TargetFamilyToObjectFamilies =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [Stage8RecomputeTargetFamilies.DossierProfile] =
            [
                Stage7DurableObjectFamilies.Dossier,
                Stage7DurableObjectFamilies.Profile
            ],
            [Stage8RecomputeTargetFamilies.PairDynamics] = [Stage7DurableObjectFamilies.PairDynamics],
            [Stage8RecomputeTargetFamilies.TimelineObjects] =
            [
                Stage7DurableObjectFamilies.Event,
                Stage7DurableObjectFamilies.TimelineEpisode,
                Stage7DurableObjectFamilies.StoryArc
            ]
        };

    private readonly IDbContextFactory<TgAssistantDbContext> _dbFactory;

    public Stage8OutcomeGateRepository(IDbContextFactory<TgAssistantDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<Stage8OutcomeGateResult> ApplyOutcomeGateAsync(
        Stage8OutcomeGateRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var scopeKey = request.ScopeKey.Trim();
        if (string.IsNullOrWhiteSpace(scopeKey))
        {
            return BuildResult(request, 0, 0, 0, 0);
        }

        if (!TargetFamilyToObjectFamilies.TryGetValue(request.TargetFamily, out var objectFamilies)
            || objectFamilies.Length == 0)
        {
            return BuildResult(request, 0, 0, 0, 0);
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var rows = await db.DurableObjectMetadata
            .Where(x => x.ScopeKey == scopeKey && objectFamilies.Contains(x.ObjectFamily))
            .ToListAsync(ct);

        if (rows.Count == 0)
        {
            return BuildResult(request, 0, 0, 0, 0);
        }

        var now = DateTime.UtcNow;
        var promotedCount = 0;
        var promotionBlockedCount = 0;
        var clarificationBlockedCount = 0;

        foreach (var row in rows)
        {
            var hasContradictions = HasNonEmptyJsonArray(row.ContradictionMarkersJson);
            var decision = ResolveDecision(request.ResultStatus, hasContradictions, request.ForcePromotionBlocked);
            row.PromotionState = decision.PromotionState;
            row.TruthLayer = decision.TruthLayer;
            row.LastPromotionRunId = request.ModelPassRunId;
            row.MetadataJson = MergeGateMetadataJson(
                row.MetadataJson,
                decision.PromotionState,
                request.ResultStatus,
                request.TriggerKind,
                request.TriggerRef,
                request.RuntimeControlState,
                request.ForcePromotionBlocked,
                hasContradictions,
                now);
            row.UpdatedAt = now;

            switch (decision.PromotionState)
            {
                case Stage8PromotionStates.Promoted:
                    promotedCount += 1;
                    break;
                case Stage8PromotionStates.ClarificationBlocked:
                    clarificationBlockedCount += 1;
                    break;
                default:
                    promotionBlockedCount += 1;
                    break;
            }
        }

        await db.SaveChangesAsync(ct);
        return BuildResult(request, rows.Count, promotedCount, promotionBlockedCount, clarificationBlockedCount);
    }

    private static (string PromotionState, string TruthLayer) ResolveDecision(
        string resultStatus,
        bool hasContradictions,
        bool forcePromotionBlocked)
    {
        if (string.Equals(resultStatus, ModelPassResultStatuses.ResultReady, StringComparison.Ordinal))
        {
            if (forcePromotionBlocked)
            {
                return (Stage8PromotionStates.PromotionBlocked, ModelNormalizationTruthLayers.ProposalLayer);
            }

            return hasContradictions
                ? (Stage8PromotionStates.PromotionBlocked, ModelNormalizationTruthLayers.ConflictedOrObsolete)
                : (Stage8PromotionStates.Promoted, ModelNormalizationTruthLayers.CanonicalTruth);
        }

        if (string.Equals(resultStatus, ModelPassResultStatuses.NeedOperatorClarification, StringComparison.Ordinal))
        {
            return (Stage8PromotionStates.ClarificationBlocked, ModelNormalizationTruthLayers.DerivedButDurable);
        }

        return (Stage8PromotionStates.PromotionBlocked, ModelNormalizationTruthLayers.ProposalLayer);
    }

    private static bool HasNonEmptyJsonArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Array
                && doc.RootElement.GetArrayLength() > 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string MergeGateMetadataJson(
        string? existingMetadataJson,
        string promotionState,
        string resultStatus,
        string? triggerKind,
        string? triggerRef,
        string? runtimeControlState,
        bool forcedByRuntimeState,
        bool hasContradictions,
        DateTime appliedAtUtc)
    {
        JsonObject root;
        if (string.IsNullOrWhiteSpace(existingMetadataJson))
        {
            root = [];
        }
        else
        {
            try
            {
                var parsed = JsonNode.Parse(existingMetadataJson);
                root = parsed as JsonObject ?? [];
            }
            catch (JsonException)
            {
                root = [];
            }
        }

        root["stage8_gate"] = new JsonObject
        {
            ["promotion_state"] = promotionState,
            ["result_status"] = resultStatus,
            ["trigger_kind"] = string.IsNullOrWhiteSpace(triggerKind) ? null : triggerKind,
            ["trigger_ref"] = string.IsNullOrWhiteSpace(triggerRef) ? null : triggerRef,
            ["runtime_control_state"] = string.IsNullOrWhiteSpace(runtimeControlState) ? null : runtimeControlState,
            ["forced_by_runtime_state"] = forcedByRuntimeState,
            ["has_contradictions"] = hasContradictions,
            ["applied_at_utc"] = appliedAtUtc.ToString("O")
        };

        return root.ToJsonString();
    }

    private static Stage8OutcomeGateResult BuildResult(
        Stage8OutcomeGateRequest request,
        int affectedCount,
        int promotedCount,
        int promotionBlockedCount,
        int clarificationBlockedCount)
    {
        return new Stage8OutcomeGateResult
        {
            ScopeKey = request.ScopeKey,
            TargetFamily = request.TargetFamily,
            ResultStatus = request.ResultStatus,
            AffectedCount = affectedCount,
            PromotedCount = promotedCount,
            PromotionBlockedCount = promotionBlockedCount,
            ClarificationBlockedCount = clarificationBlockedCount
        };
    }
}
