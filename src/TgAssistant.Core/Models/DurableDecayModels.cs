using System.Text.Json;
using System.Text.Json.Serialization;

namespace TgAssistant.Core.Models;

public static class DurableDecayClasses
{
    public const string StableTrait = "stable_trait";
    public const string SituationalState = "situational_state";
    public const string LocalEpisode = "local_episode";
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
}
