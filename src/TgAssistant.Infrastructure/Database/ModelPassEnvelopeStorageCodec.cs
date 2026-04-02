using System.Text.Json;
using TgAssistant.Core.Models;

namespace TgAssistant.Infrastructure.Database;

public static class ModelPassEnvelopeStorageCodec
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string SerializeScope(ModelPassScope scope) => JsonSerializer.Serialize(scope, JsonOptions);

    public static ModelPassScope DeserializeScope(string json)
        => JsonSerializer.Deserialize<ModelPassScope>(json, JsonOptions) ?? new ModelPassScope();

    public static string SerializeSourceRefs(IReadOnlyCollection<ModelPassSourceRef> sourceRefs)
        => JsonSerializer.Serialize(sourceRefs, JsonOptions);

    public static List<ModelPassSourceRef> DeserializeSourceRefs(string json)
        => JsonSerializer.Deserialize<List<ModelPassSourceRef>>(json, JsonOptions) ?? [];

    public static string SerializeTruthSummary(ModelPassTruthSummary truthSummary)
        => JsonSerializer.Serialize(truthSummary, JsonOptions);

    public static ModelPassTruthSummary DeserializeTruthSummary(string json)
        => JsonSerializer.Deserialize<ModelPassTruthSummary>(json, JsonOptions) ?? new ModelPassTruthSummary();

    public static string SerializeConflicts(IReadOnlyCollection<ModelPassConflict> conflicts)
        => JsonSerializer.Serialize(conflicts, JsonOptions);

    public static List<ModelPassConflict> DeserializeConflicts(string json)
        => JsonSerializer.Deserialize<List<ModelPassConflict>>(json, JsonOptions) ?? [];

    public static string SerializeUnknowns(IReadOnlyCollection<ModelPassUnknown> unknowns)
        => JsonSerializer.Serialize(unknowns, JsonOptions);

    public static List<ModelPassUnknown> DeserializeUnknowns(string json)
        => JsonSerializer.Deserialize<List<ModelPassUnknown>>(json, JsonOptions) ?? [];

    public static string SerializeInputSummary(ModelPassEnvelope envelope)
    {
        var payload = new
        {
            schema_version = envelope.SchemaVersion,
            scope = envelope.Scope,
            source_refs = envelope.SourceRefs,
            truth_summary = envelope.TruthSummary,
            conflicts = envelope.Conflicts,
            unknowns = envelope.Unknowns
        };

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    public static string SerializeOutputSummary(ModelPassOutputSummary summary)
        => JsonSerializer.Serialize(summary, JsonOptions);

    public static ModelPassOutputSummary DeserializeOutputSummary(string json)
        => JsonSerializer.Deserialize<ModelPassOutputSummary>(json, JsonOptions) ?? new ModelPassOutputSummary();

    public static string SerializeFailureSummary(ModelPassEnvelope envelope)
    {
        if (!string.Equals(envelope.ResultStatus, ModelPassResultStatuses.BlockedInvalidInput, StringComparison.Ordinal))
        {
            return "{}";
        }

        var payload = new
        {
            blocked_reason = envelope.OutputSummary.BlockedReason
        };

        return JsonSerializer.Serialize(payload, JsonOptions);
    }
}
