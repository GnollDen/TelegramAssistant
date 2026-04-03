using System.Text.Json;
using System.Text.Json.Serialization;

namespace TgAssistant.Core.Models;

public static class DurableDecayClasses
{
    public const string StableTrait = "stable_trait";
    public const string SituationalState = "situational_state";
    public const string LocalEpisode = "local_episode";
}

public static class DurableRecencyStates
{
    public const string Unknown = "unknown";
    public const string Fresh = "fresh";
    public const string ReviewDue = "review_due";
    public const string Expired = "expired";
}

public sealed class DurableDecayPolicySnapshot
{
    [JsonPropertyName("object_family")]
    public string ObjectFamily { get; init; } = string.Empty;

    [JsonPropertyName("decay_class")]
    public string DecayClass { get; init; } = string.Empty;

    [JsonPropertyName("fresh_for_days")]
    public int FreshForDays { get; init; }

    [JsonPropertyName("review_after_days")]
    public int ReviewAfterDays { get; init; }

    [JsonPropertyName("expire_after_days")]
    public int ExpireAfterDays { get; init; }

    [JsonPropertyName("decay_strategy")]
    public string DecayStrategy { get; init; } = string.Empty;

    [JsonPropertyName("policy_note")]
    public string PolicyNote { get; init; } = string.Empty;
}

public sealed class DurableRecencyAssessment
{
    [JsonPropertyName("state")]
    public string State { get; init; } = DurableRecencyStates.Unknown;

    [JsonPropertyName("age_days")]
    public double? AgeDays { get; init; }

    [JsonPropertyName("latest_evidence_at_utc")]
    public DateTime? LatestEvidenceAtUtc { get; init; }

    [JsonPropertyName("has_contradictions")]
    public bool HasContradictions { get; init; }

    [JsonPropertyName("should_downgrade")]
    public bool ShouldDowngrade { get; init; }

    [JsonPropertyName("freshness_cap")]
    public float? FreshnessCap { get; init; }

    [JsonPropertyName("stability_cap")]
    public float? StabilityCap { get; init; }

    [JsonPropertyName("recommended_promotion_state")]
    public string? RecommendedPromotionState { get; init; }

    [JsonPropertyName("recommended_truth_layer")]
    public string? RecommendedTruthLayer { get; init; }
}

public static class DurableDecayPolicyCatalog
{
    private static readonly IReadOnlyDictionary<string, DurableDecayPolicySnapshot> Policies =
        new Dictionary<string, DurableDecayPolicySnapshot>(StringComparer.Ordinal)
        {
            [Stage7DurableObjectFamilies.Dossier] = new DurableDecayPolicySnapshot
            {
                ObjectFamily = Stage7DurableObjectFamilies.Dossier,
                DecayClass = DurableDecayClasses.StableTrait,
                FreshForDays = 30,
                ReviewAfterDays = 90,
                ExpireAfterDays = 365,
                DecayStrategy = "slow_decay",
                PolicyNote = "Person-level dossier facts remain durable unless contradicted or explicitly invalidated."
            },
            [Stage7DurableObjectFamilies.Profile] = new DurableDecayPolicySnapshot
            {
                ObjectFamily = Stage7DurableObjectFamilies.Profile,
                DecayClass = DurableDecayClasses.StableTrait,
                FreshForDays = 21,
                ReviewAfterDays = 60,
                ExpireAfterDays = 180,
                DecayStrategy = "slow_decay",
                PolicyNote = "Behavioral profile traits persist longer than situational states but still require periodic review."
            },
            [Stage7DurableObjectFamilies.PairDynamics] = new DurableDecayPolicySnapshot
            {
                ObjectFamily = Stage7DurableObjectFamilies.PairDynamics,
                DecayClass = DurableDecayClasses.SituationalState,
                FreshForDays = 10,
                ReviewAfterDays = 30,
                ExpireAfterDays = 90,
                DecayStrategy = "context_sensitive",
                PolicyNote = "Relationship dynamics stay durable within a context window and should down-rank faster than traits."
            },
            [Stage7DurableObjectFamilies.Event] = new DurableDecayPolicySnapshot
            {
                ObjectFamily = Stage7DurableObjectFamilies.Event,
                DecayClass = DurableDecayClasses.LocalEpisode,
                FreshForDays = 2,
                ReviewAfterDays = 7,
                ExpireAfterDays = 30,
                DecayStrategy = "episode_bound",
                PolicyNote = "Single events should become historical context quickly and must not behave like stable traits."
            },
            [Stage7DurableObjectFamilies.TimelineEpisode] = new DurableDecayPolicySnapshot
            {
                ObjectFamily = Stage7DurableObjectFamilies.TimelineEpisode,
                DecayClass = DurableDecayClasses.LocalEpisode,
                FreshForDays = 5,
                ReviewAfterDays = 14,
                ExpireAfterDays = 45,
                DecayStrategy = "episode_bound",
                PolicyNote = "Local episodes remain relevant for a short narrative window before yielding to newer evidence."
            },
            [Stage7DurableObjectFamilies.StoryArc] = new DurableDecayPolicySnapshot
            {
                ObjectFamily = Stage7DurableObjectFamilies.StoryArc,
                DecayClass = DurableDecayClasses.SituationalState,
                FreshForDays = 14,
                ReviewAfterDays = 45,
                ExpireAfterDays = 120,
                DecayStrategy = "context_sensitive",
                PolicyNote = "Story arcs outlive local episodes but should still decay faster than stable person traits."
            }
        };

    public static DurableDecayPolicySnapshot Resolve(string objectFamily)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectFamily);

        if (Policies.TryGetValue(objectFamily, out var policy))
        {
            return policy;
        }

        throw new InvalidOperationException($"No durable decay policy is defined for object family '{objectFamily}'.");
    }

    public static string Serialize(string objectFamily) => JsonSerializer.Serialize(Resolve(objectFamily));

    public static float ComputeFreshness(string objectFamily, DateTime? latestEvidenceAtUtc, DateTime? referenceUtc = null)
    {
        if (latestEvidenceAtUtc == null)
        {
            return 0.25f;
        }

        var policy = Resolve(objectFamily);
        var assessment = Assess(objectFamily, Serialize(objectFamily), BuildLatestEvidenceMetadataJson(latestEvidenceAtUtc.Value), hasContradictions: false, referenceUtc);
        return assessment.State switch
        {
            DurableRecencyStates.Fresh => 1.0f,
            DurableRecencyStates.ReviewDue => InterpolateFreshness(
                assessment.AgeDays ?? policy.FreshForDays,
                policy.FreshForDays,
                policy.ReviewAfterDays,
                0.82f,
                0.58f),
            DurableRecencyStates.Expired => InterpolateFreshness(
                assessment.AgeDays ?? policy.ExpireAfterDays,
                policy.ReviewAfterDays,
                policy.ExpireAfterDays,
                0.42f,
                0.20f),
            _ => 0.25f
        };
    }

    public static DurableRecencyAssessment Assess(
        string objectFamily,
        string? decayPolicyJson,
        string? metadataJson,
        bool hasContradictions,
        DateTime? referenceUtc = null)
    {
        var policy = DeserializePolicy(objectFamily, decayPolicyJson);
        var latestEvidenceAtUtc = ExtractLatestEvidenceAtUtc(metadataJson);
        if (latestEvidenceAtUtc == null)
        {
            return new DurableRecencyAssessment
            {
                State = DurableRecencyStates.Unknown,
                HasContradictions = hasContradictions
            };
        }

        var now = referenceUtc ?? DateTime.UtcNow;
        var ageDays = Math.Max(0d, (now - latestEvidenceAtUtc.Value).TotalDays);
        var state = ageDays <= policy.FreshForDays
            ? DurableRecencyStates.Fresh
            : ageDays <= policy.ExpireAfterDays
                ? DurableRecencyStates.ReviewDue
                : DurableRecencyStates.Expired;

        if (!hasContradictions)
        {
            return new DurableRecencyAssessment
            {
                State = state,
                AgeDays = ageDays,
                LatestEvidenceAtUtc = latestEvidenceAtUtc,
                HasContradictions = false
            };
        }

        return policy.DecayClass switch
        {
            DurableDecayClasses.LocalEpisode => BuildAssessment(
                state,
                ageDays,
                latestEvidenceAtUtc.Value,
                freshnessCap: state == DurableRecencyStates.Fresh ? null : 0.22f,
                stabilityCap: state == DurableRecencyStates.Fresh ? null : 0.35f,
                shouldDowngrade: state != DurableRecencyStates.Fresh,
                truthLayer: state == DurableRecencyStates.Fresh ? null : ModelNormalizationTruthLayers.ProposalLayer),
            DurableDecayClasses.SituationalState => BuildAssessment(
                state,
                ageDays,
                latestEvidenceAtUtc.Value,
                freshnessCap: state == DurableRecencyStates.Expired ? 0.24f : state == DurableRecencyStates.ReviewDue ? 0.45f : null,
                stabilityCap: state == DurableRecencyStates.Expired ? 0.38f : state == DurableRecencyStates.ReviewDue ? 0.58f : null,
                shouldDowngrade: state != DurableRecencyStates.Fresh,
                truthLayer: state == DurableRecencyStates.Expired
                    ? ModelNormalizationTruthLayers.ProposalLayer
                    : state == DurableRecencyStates.ReviewDue
                        ? ModelNormalizationTruthLayers.ConflictedOrObsolete
                        : null),
            DurableDecayClasses.StableTrait => BuildAssessment(
                state,
                ageDays,
                latestEvidenceAtUtc.Value,
                freshnessCap: state == DurableRecencyStates.Expired ? 0.35f : null,
                stabilityCap: state == DurableRecencyStates.Expired ? 0.50f : null,
                shouldDowngrade: state == DurableRecencyStates.Expired,
                truthLayer: state == DurableRecencyStates.Expired ? ModelNormalizationTruthLayers.ProposalLayer : null),
            _ => new DurableRecencyAssessment
            {
                State = state,
                AgeDays = ageDays,
                LatestEvidenceAtUtc = latestEvidenceAtUtc,
                HasContradictions = true
            }
        };
    }

    private static DurableRecencyAssessment BuildAssessment(
        string state,
        double ageDays,
        DateTime latestEvidenceAtUtc,
        float? freshnessCap,
        float? stabilityCap,
        bool shouldDowngrade,
        string? truthLayer)
    {
        return new DurableRecencyAssessment
        {
            State = state,
            AgeDays = ageDays,
            LatestEvidenceAtUtc = latestEvidenceAtUtc,
            HasContradictions = true,
            ShouldDowngrade = shouldDowngrade,
            FreshnessCap = freshnessCap,
            StabilityCap = stabilityCap,
            RecommendedPromotionState = shouldDowngrade ? Stage8PromotionStates.PromotionBlocked : null,
            RecommendedTruthLayer = truthLayer
        };
    }

    private static DurableDecayPolicySnapshot DeserializePolicy(string objectFamily, string? decayPolicyJson)
    {
        if (!string.IsNullOrWhiteSpace(decayPolicyJson))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<DurableDecayPolicySnapshot>(decayPolicyJson);
                if (parsed != null && !string.IsNullOrWhiteSpace(parsed.ObjectFamily))
                {
                    return parsed;
                }
            }
            catch (JsonException)
            {
            }
        }

        return Resolve(objectFamily);
    }

    private static DateTime? ExtractLatestEvidenceAtUtc(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(metadataJson);
            if (!doc.RootElement.TryGetProperty("latest_evidence_at_utc", out var latestEvidenceElement)
                || latestEvidenceElement.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            return latestEvidenceElement.GetDateTime().ToUniversalTime();
        }
        catch (JsonException)
        {
            return null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static string BuildLatestEvidenceMetadataJson(DateTime latestEvidenceAtUtc)
    {
        return JsonSerializer.Serialize(new
        {
            latest_evidence_at_utc = latestEvidenceAtUtc.ToUniversalTime().ToString("O")
        });
    }

    private static float InterpolateFreshness(double ageDays, int fromDays, int toDays, float fromScore, float toScore)
    {
        if (toDays <= fromDays)
        {
            return toScore;
        }

        if (ageDays <= fromDays)
        {
            return fromScore;
        }

        if (ageDays >= toDays)
        {
            return toScore;
        }

        var progress = (ageDays - fromDays) / (toDays - fromDays);
        return (float)(fromScore + ((toScore - fromScore) * progress));
    }
}
