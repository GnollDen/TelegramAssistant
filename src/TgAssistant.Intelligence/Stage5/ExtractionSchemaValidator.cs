using System.Text.Json;

namespace TgAssistant.Intelligence.Stage5;

public class ExtractionSchemaValidator
{
    public bool TryParseBatch(string json, out ExtractionBatchResult result, out string? error)
    {
        result = new ExtractionBatchResult();
        error = null;

        if (string.IsNullOrWhiteSpace(json))
        {
            error = "empty_json";
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                error = "root_not_object";
                return false;
            }

            if (!doc.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            {
                error = "missing_items_array";
                return false;
            }

            var parsed = JsonSerializer.Deserialize<ExtractionBatchResult>(json, JsonOptions);
            if (parsed == null)
            {
                error = "deserialize_failed";
                return false;
            }

            for (var i = 0; i < items.GetArrayLength(); i++)
            {
                var item = items[i];
                if (item.ValueKind != JsonValueKind.Object)
                {
                    error = $"item_{i}_not_object";
                    return false;
                }

                if (!ValidateItem(item, i, out error))
                {
                    return false;
                }
            }

            result = parsed;
            return true;
        }
        catch (Exception ex)
        {
            error = $"json_parse_error:{ex.GetType().Name}";
            return false;
        }
    }

    private static bool ValidateItem(JsonElement item, int index, out string? error)
    {
        error = null;

        if (item.TryGetProperty("message_id", out var messageId) &&
            messageId.ValueKind is not JsonValueKind.Number)
        {
            error = $"item_{index}_message_id_not_number";
            return false;
        }

        if (!ValidateEntityArray(item, index, out error))
        {
            return false;
        }

        if (!ValidateFactArray(item, index, out error))
        {
            return false;
        }

        if (!ValidateRelationshipArray(item, index, out error))
        {
            return false;
        }

        if (!ValidateEventArray(item, index, out error))
        {
            return false;
        }

        if (!ValidateProfileSignalArray(item, index, out error))
        {
            return false;
        }

        if (item.TryGetProperty("requires_expensive", out var requiresExpensive) &&
            requiresExpensive.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
        {
            error = $"item_{index}_requires_expensive_not_bool";
            return false;
        }

        if (item.TryGetProperty("reason", out var reason) &&
            reason.ValueKind != JsonValueKind.String &&
            reason.ValueKind != JsonValueKind.Null)
        {
            error = $"item_{index}_reason_not_string";
            return false;
        }

        return true;
    }

    private static bool ValidateEntityArray(JsonElement item, int index, out string? error)
    {
        error = null;
        if (!item.TryGetProperty("entities", out var entities))
        {
            return true;
        }

        if (entities.ValueKind != JsonValueKind.Array)
        {
            error = $"item_{index}_entities_not_array";
            return false;
        }

        for (var i = 0; i < entities.GetArrayLength(); i++)
        {
            var entity = entities[i];
            if (entity.ValueKind != JsonValueKind.Object)
            {
                error = $"item_{index}_entity_{i}_not_object";
                return false;
            }

            if (!IsStringOrMissing(entity, "name") ||
                !IsStringOrMissing(entity, "type") ||
                !IsNumberOrMissing(entity, "confidence"))
            {
                error = $"item_{index}_entity_{i}_invalid_fields";
                return false;
            }
        }

        return true;
    }

    private static bool ValidateFactArray(JsonElement item, int index, out string? error)
    {
        error = null;
        if (!item.TryGetProperty("facts", out var facts))
        {
            return true;
        }

        if (facts.ValueKind != JsonValueKind.Array)
        {
            error = $"item_{index}_facts_not_array";
            return false;
        }

        for (var i = 0; i < facts.GetArrayLength(); i++)
        {
            var fact = facts[i];
            if (fact.ValueKind != JsonValueKind.Object)
            {
                error = $"item_{index}_fact_{i}_not_object";
                return false;
            }

            if (!IsStringOrMissing(fact, "entity_name") ||
                !IsStringOrMissing(fact, "category") ||
                !IsStringOrMissing(fact, "key") ||
                !IsStringOrMissing(fact, "value") ||
                !IsNumberOrMissing(fact, "confidence"))
            {
                error = $"item_{index}_fact_{i}_invalid_fields";
                return false;
            }
        }

        return true;
    }

    private static bool ValidateRelationshipArray(JsonElement item, int index, out string? error)
    {
        error = null;
        if (!item.TryGetProperty("relationships", out var relationships))
        {
            return true;
        }

        if (relationships.ValueKind != JsonValueKind.Array)
        {
            error = $"item_{index}_relationships_not_array";
            return false;
        }

        for (var i = 0; i < relationships.GetArrayLength(); i++)
        {
            var relationship = relationships[i];
            if (relationship.ValueKind != JsonValueKind.Object)
            {
                error = $"item_{index}_relationship_{i}_not_object";
                return false;
            }

            if (!IsStringOrMissing(relationship, "from_entity_name") ||
                !IsStringOrMissing(relationship, "to_entity_name") ||
                !IsStringOrMissing(relationship, "type") ||
                !IsNumberOrMissing(relationship, "confidence"))
            {
                error = $"item_{index}_relationship_{i}_invalid_fields";
                return false;
            }
        }

        return true;
    }

    private static bool ValidateEventArray(JsonElement item, int index, out string? error)
    {
        error = null;
        if (!item.TryGetProperty("events", out var events))
        {
            return true;
        }

        if (events.ValueKind != JsonValueKind.Array)
        {
            error = $"item_{index}_events_not_array";
            return false;
        }

        for (var i = 0; i < events.GetArrayLength(); i++)
        {
            var evt = events[i];
            if (evt.ValueKind != JsonValueKind.Object)
            {
                error = $"item_{index}_event_{i}_not_object";
                return false;
            }

            if (!IsStringOrMissing(evt, "type") ||
                !IsStringOrMissing(evt, "subject_name") ||
                !IsStringOrMissing(evt, "object_name") ||
                !IsStringOrMissing(evt, "sentiment") ||
                !IsStringOrMissing(evt, "summary") ||
                !IsNumberOrMissing(evt, "confidence"))
            {
                error = $"item_{index}_event_{i}_invalid_fields";
                return false;
            }
        }

        return true;
    }

    private static bool ValidateProfileSignalArray(JsonElement item, int index, out string? error)
    {
        error = null;
        if (!item.TryGetProperty("profile_signals", out var signals))
        {
            return true;
        }

        if (signals.ValueKind != JsonValueKind.Array)
        {
            error = $"item_{index}_profile_signals_not_array";
            return false;
        }

        for (var i = 0; i < signals.GetArrayLength(); i++)
        {
            var signal = signals[i];
            if (signal.ValueKind != JsonValueKind.Object)
            {
                error = $"item_{index}_profile_signal_{i}_not_object";
                return false;
            }

            if (!IsStringOrMissing(signal, "subject_name") ||
                !IsStringOrMissing(signal, "trait") ||
                !IsStringOrMissing(signal, "direction") ||
                !IsStringOrMissing(signal, "evidence") ||
                !IsNumberOrMissing(signal, "confidence"))
            {
                error = $"item_{index}_profile_signal_{i}_invalid_fields";
                return false;
            }
        }

        return true;
    }

    private static bool IsStringOrMissing(JsonElement obj, string propName)
    {
        return !obj.TryGetProperty(propName, out var value) ||
               value.ValueKind == JsonValueKind.String ||
               value.ValueKind == JsonValueKind.Null;
    }

    private static bool IsNumberOrMissing(JsonElement obj, string propName)
    {
        return !obj.TryGetProperty(propName, out var value) ||
               value.ValueKind == JsonValueKind.Number ||
               value.ValueKind == JsonValueKind.Null;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };
}
