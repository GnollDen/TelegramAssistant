using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage5;

public static class ExtractionValidator
{
    /// <summary>
    /// Validates extraction payload against the source message context.
    /// </summary>
    public static bool ValidateExtractionForMessage(ExtractionItem item, Message message, out string? error)
    {
        if (item.MessageId != message.Id)
        {
            error = $"message_id_mismatch:{item.MessageId}!={message.Id}";
            return false;
        }

        return ValidateExtractionRecord(item, out error);
    }

    /// <summary>
    /// Validates extraction record shape and limits.
    /// </summary>
    public static bool ValidateExtractionRecord(ExtractionItem item, out string? error)
    {
        if (item.Entities.Count > 20 ||
            item.Observations.Count > 30 ||
            item.Claims.Count > 40 ||
            item.Facts.Count > 30 ||
            item.Relationships.Count > 20 ||
            item.Events.Count > 20 ||
            item.ProfileSignals.Count > 20)
        {
            error = "too_many_items";
            return false;
        }

        foreach (var entity in item.Entities)
        {
            if (!IsReasonableText(entity.Name, 120))
            {
                error = "invalid_entity_name";
                return false;
            }

            if (!HasTrustOrConfidence(entity.TrustFactor, entity.Confidence))
            {
                error = "entity_missing_trust_or_confidence";
                return false;
            }
        }

        foreach (var observation in item.Observations)
        {
            if (!IsReasonableText(observation.SubjectName, 120) ||
                !IsReasonableText(observation.Type, 64) ||
                (observation.ObjectName is not null && !IsReasonableText(observation.ObjectName, 120)) ||
                (observation.Value is not null && !IsReasonableText(observation.Value, 500)) ||
                (observation.Evidence is not null && !IsReasonableText(observation.Evidence, 500)))
            {
                error = "invalid_observation_payload";
                return false;
            }
        }

        foreach (var claim in item.Claims)
        {
            if (!IsReasonableText(claim.EntityName, 120) ||
                !IsReasonableText(claim.ClaimType, 64) ||
                !IsReasonableText(claim.Category, 64) ||
                !IsReasonableText(claim.Key, 96) ||
                !IsReasonableText(claim.Value, 500) ||
                (claim.Evidence is not null && !IsReasonableText(claim.Evidence, 500)))
            {
                error = "invalid_claim_payload";
                return false;
            }

            if (!IsValid01(claim.Confidence))
            {
                error = "claim_invalid_confidence";
                return false;
            }
        }

        foreach (var fact in item.Facts)
        {
            if (!IsReasonableText(fact.EntityName, 120) ||
                !IsReasonableText(fact.Category, 64) ||
                !IsReasonableText(fact.Key, 96) ||
                !IsReasonableText(fact.Value, 500))
            {
                error = "invalid_fact_payload";
                return false;
            }

            if (!HasTrustOrConfidence(fact.TrustFactor, fact.Confidence))
            {
                error = "fact_missing_trust_or_confidence";
                return false;
            }
        }

        foreach (var relationship in item.Relationships)
        {
            if (!IsReasonableText(relationship.FromEntityName, 120) ||
                !IsReasonableText(relationship.ToEntityName, 120) ||
                !IsReasonableText(relationship.Type, 64))
            {
                error = "invalid_relationship_payload";
                return false;
            }
        }

        foreach (var evt in item.Events)
        {
            if (!IsReasonableText(evt.Type, 64) ||
                !IsReasonableText(evt.SubjectName, 120) ||
                (evt.ObjectName is not null && !IsReasonableText(evt.ObjectName, 120)) ||
                (evt.Sentiment is not null && !IsReasonableText(evt.Sentiment, 32)) ||
                (evt.Summary is not null && !IsReasonableText(evt.Summary, 500)))
            {
                error = "invalid_event_payload";
                return false;
            }
        }

        foreach (var signal in item.ProfileSignals)
        {
            if (!IsReasonableText(signal.SubjectName, 120) ||
                !IsReasonableText(signal.Trait, 64) ||
                !IsReasonableText(signal.Direction, 32) ||
                (signal.Evidence is not null && !IsReasonableText(signal.Evidence, 500)))
            {
                error = "invalid_profile_signal_payload";
                return false;
            }
        }

        error = null;
        return true;
    }

    /// <summary>
    /// Validates a text field as non-empty, bounded and free from control characters.
    /// </summary>
    public static bool IsReasonableText(string? value, int maxLen)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.Trim();
        if (text.Length == 0 || text.Length > maxLen)
        {
            return false;
        }

        return text.All(ch => !char.IsControl(ch));
    }

    private static bool HasTrustOrConfidence(float trustFactor, float confidence)
    {
        return IsValid01(trustFactor) || IsValid01(confidence);
    }

    private static bool IsValid01(float value)
    {
        return value > 0f && value <= 1f;
    }
}
