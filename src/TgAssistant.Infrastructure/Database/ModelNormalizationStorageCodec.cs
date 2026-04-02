using System.Text.Json;
using TgAssistant.Core.Models;

namespace TgAssistant.Infrastructure.Database;

public static class ModelNormalizationStorageCodec
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string SerializeCandidateCounts(ModelNormalizationCandidateCounts candidateCounts)
        => JsonSerializer.Serialize(candidateCounts, JsonOptions);

    public static ModelNormalizationCandidateCounts DeserializeCandidateCounts(string json)
        => JsonSerializer.Deserialize<ModelNormalizationCandidateCounts>(json, JsonOptions) ?? new ModelNormalizationCandidateCounts();

    public static string SerializeNormalizedPayload(ModelNormalizationPayload payload)
        => JsonSerializer.Serialize(payload, JsonOptions);

    public static ModelNormalizationPayload DeserializeNormalizedPayload(string json)
        => JsonSerializer.Deserialize<ModelNormalizationPayload>(json, JsonOptions) ?? new ModelNormalizationPayload();

    public static string SerializeConflictCandidates(IReadOnlyCollection<NormalizedConflictCandidate> conflicts)
        => JsonSerializer.Serialize(conflicts, JsonOptions);

    public static List<NormalizedConflictCandidate> DeserializeConflictCandidates(string json)
        => JsonSerializer.Deserialize<List<NormalizedConflictCandidate>>(json, JsonOptions) ?? [];

    public static string SerializeIssues(IReadOnlyCollection<ModelNormalizationIssue> issues)
        => JsonSerializer.Serialize(issues, JsonOptions);

    public static List<ModelNormalizationIssue> DeserializeIssues(string json)
        => JsonSerializer.Deserialize<List<ModelNormalizationIssue>>(json, JsonOptions) ?? [];
}
