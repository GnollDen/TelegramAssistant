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
        var targetPersonId = ResolveTargetPersonId(request);
        var hasExplicitScopeOnlyTarget = string.Equals(request.TargetRef, $"scope:{scopeKey}", StringComparison.Ordinal);
        var rowsQuery = db.DurableObjectMetadata
            .Where(x => x.ScopeKey == scopeKey && objectFamilies.Contains(x.ObjectFamily));
        if (targetPersonId.HasValue && !hasExplicitScopeOnlyTarget)
        {
            rowsQuery = rowsQuery.Where(x =>
                x.OwnerPersonId == targetPersonId.Value
                || x.RelatedPersonId == targetPersonId.Value);
        }

        var rows = await rowsQuery.ToListAsync(ct);

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
            var isReadyResult = string.Equals(request.ResultStatus, ModelPassResultStatuses.ResultReady, StringComparison.Ordinal);
            var recencyAssessment = DurableDecayPolicyCatalog.Assess(
                row.ObjectFamily,
                row.DecayPolicyJson,
                row.MetadataJson,
                hasContradictions && isReadyResult,
                now);
            var decision = ResolveDecision(request.ResultStatus, hasContradictions, request.ForcePromotionBlocked, recencyAssessment);
            row.PromotionState = decision.PromotionState;
            row.TruthLayer = decision.TruthLayer;
            if (recencyAssessment.FreshnessCap.HasValue)
            {
                row.Freshness = Math.Min(row.Freshness, recencyAssessment.FreshnessCap.Value);
            }

            if (recencyAssessment.StabilityCap.HasValue)
            {
                row.Stability = Math.Min(row.Stability, recencyAssessment.StabilityCap.Value);
            }

            row.LastPromotionRunId = request.ModelPassRunId;
            row.MetadataJson = MergeGateMetadataJson(
                row.MetadataJson,
                decision.PromotionState,
                request.ResultStatus,
                request.TargetRef,
                targetPersonId,
                request.TriggerKind,
                request.TriggerRef,
                request.RuntimeControlState,
                request.ForcePromotionBlocked,
                hasContradictions,
                recencyAssessment,
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
        bool forcePromotionBlocked,
        DurableRecencyAssessment recencyAssessment)
    {
        if (string.Equals(resultStatus, ModelPassResultStatuses.ResultReady, StringComparison.Ordinal))
        {
            if (forcePromotionBlocked)
            {
                return (Stage8PromotionStates.PromotionBlocked, ModelNormalizationTruthLayers.ProposalLayer);
            }

            if (recencyAssessment.ShouldDowngrade
                && !string.IsNullOrWhiteSpace(recencyAssessment.RecommendedPromotionState)
                && !string.IsNullOrWhiteSpace(recencyAssessment.RecommendedTruthLayer))
            {
                return (recencyAssessment.RecommendedPromotionState, recencyAssessment.RecommendedTruthLayer);
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
        string? targetRef,
        Guid? targetPersonId,
        string? triggerKind,
        string? triggerRef,
        string? runtimeControlState,
        bool forcedByRuntimeState,
        bool hasContradictions,
        DurableRecencyAssessment recencyAssessment,
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
            ["target_ref"] = string.IsNullOrWhiteSpace(targetRef) ? null : targetRef,
            ["target_person_id"] = targetPersonId?.ToString("D"),
            ["trigger_kind"] = string.IsNullOrWhiteSpace(triggerKind) ? null : triggerKind,
            ["trigger_ref"] = string.IsNullOrWhiteSpace(triggerRef) ? null : triggerRef,
            ["runtime_control_state"] = string.IsNullOrWhiteSpace(runtimeControlState) ? null : runtimeControlState,
            ["forced_by_runtime_state"] = forcedByRuntimeState,
            ["has_contradictions"] = hasContradictions,
            ["recency_state"] = recencyAssessment.State,
            ["recency_age_days"] = recencyAssessment.AgeDays,
            ["recency_downgraded"] = recencyAssessment.ShouldDowngrade,
            ["latest_evidence_at_utc"] = recencyAssessment.LatestEvidenceAtUtc?.ToString("O"),
            ["applied_at_utc"] = appliedAtUtc.ToString("O")
        };

        return root.ToJsonString();
    }

    private static Guid? ResolveTargetPersonId(Stage8OutcomeGateRequest request)
    {
        if (request.PersonId.HasValue)
        {
            return request.PersonId.Value;
        }

        if (!string.IsNullOrWhiteSpace(request.TargetRef)
            && request.TargetRef.StartsWith("person:", StringComparison.OrdinalIgnoreCase)
            && Guid.TryParse(request.TargetRef["person:".Length..], out var parsedPersonId))
        {
            return parsedPersonId;
        }

        return null;
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
