using TgAssistant.Core.Models;
using System.Text.RegularExpressions;

namespace TgAssistant.Intelligence.Stage5;

public static class ExtractionValidator
{
    private static readonly Regex TimeLikeRegex = new(@"^(?:[01]?\d|2[0-3]):[0-5]\d(?:\s*[-–]\s*(?:[01]?\d|2[0-3]):[0-5]\d)?$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex DateLikeRegex = new(@"^(?:\d{4}-\d{2}-\d{2}|\d{1,2}[./-]\d{1,2}(?:[./-]\d{2,4})?)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex RatioLikeRegex = new(@"^\d{1,4}\s*/\s*\d{1,4}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex FormulaLikeRegex = new(@"^[0-9a-zA-Z().,+\-*/=^% ]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CompactScheduleLikeRegex = new(@"^(?:\d{1,2}\s*[xх]\s*\d{1,2}|\d{1,2}[:.]\d{2}\s*[/-]\s*\d{1,2}[:.]\d{2})$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

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

        var messageSemantic = MessageContentBuilder.BuildSemanticContent(message);
        var enforceRussianOutput = ExtractionSemanticContract.IsLikelyRussianText(messageSemantic);
        return ValidateExtractionRecord(item, out error, enforceRussianOutput);
    }

    /// <summary>
    /// Validates extraction record shape and limits.
    /// </summary>
    public static bool ValidateExtractionRecord(ExtractionItem item, out string? error, bool enforceRussianOutput = false)
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

            if (!ExtractionSemanticContract.AllowedEntityTypes.Contains(entity.Type))
            {
                error = "invalid_entity_type";
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

            if (!ExtractionSemanticContract.IsSnakeCase(observation.Type))
            {
                error = "invalid_observation_type";
                return false;
            }

            if (!IsValid01(observation.Confidence))
            {
                error = "observation_invalid_confidence";
                return false;
            }

            if (enforceRussianOutput &&
                !HasCyrillicOrEmpty(observation.Value) &&
                !HasCyrillicOrEmpty(observation.Evidence))
            {
                error = "observation_non_russian_output";
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

            if (!ExtractionSemanticContract.AllowedClaimTypes.Contains(claim.ClaimType))
            {
                error = "invalid_claim_type";
                return false;
            }

            if (!ExtractionSemanticContract.AllowedCategories.Contains(claim.Category))
            {
                error = "invalid_claim_category";
                return false;
            }

            if (!ExtractionSemanticContract.IsSnakeCase(claim.Key))
            {
                error = "invalid_claim_key_format";
                return false;
            }

            if (!ExtractionSemanticContract.IsAllowedKey(claim.Category, claim.Key))
            {
                var hint = ExtractionSemanticContract.GetUnsupportedKeyDriftHint(claim.Category, claim.Key);
                error = hint == null ? "invalid_claim_key" : $"invalid_claim_key:{hint}";
                return false;
            }

            if (!IsValid01(claim.Confidence))
            {
                error = "claim_invalid_confidence";
                return false;
            }

            if (enforceRussianOutput &&
                !HasCyrillicOrStructuredOrEmpty(claim.Value) &&
                !HasCyrillicOrStructuredOrEmpty(claim.Evidence))
            {
                error = "claim_non_russian_output";
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

            if (!ExtractionSemanticContract.AllowedCategories.Contains(fact.Category))
            {
                error = "invalid_fact_category";
                return false;
            }

            if (!ExtractionSemanticContract.IsSnakeCase(fact.Key))
            {
                error = "invalid_fact_key_format";
                return false;
            }

            if (!ExtractionSemanticContract.IsAllowedKey(fact.Category, fact.Key))
            {
                var hint = ExtractionSemanticContract.GetUnsupportedKeyDriftHint(fact.Category, fact.Key);
                error = hint == null ? "invalid_fact_key" : $"invalid_fact_key:{hint}";
                return false;
            }

            if (!HasTrustOrConfidence(fact.TrustFactor, fact.Confidence))
            {
                error = "fact_missing_trust_or_confidence";
                return false;
            }

            if (enforceRussianOutput && !HasCyrillicOrStructuredOrEmpty(fact.Value))
            {
                error = "fact_non_russian_output";
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

            if (!ExtractionSemanticContract.AllowedRelationshipTypes.Contains(relationship.Type))
            {
                error = "invalid_relationship_type";
                return false;
            }

            if (!IsValid01(relationship.Confidence))
            {
                error = "relationship_invalid_confidence";
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

            if (!ExtractionSemanticContract.IsSnakeCase(evt.Type))
            {
                error = "invalid_event_type";
                return false;
            }

            if (!IsValid01(evt.Confidence))
            {
                error = "event_invalid_confidence";
                return false;
            }

            if (enforceRussianOutput &&
                !HasCyrillicOrEmpty(evt.Summary) &&
                !HasCyrillicOrEmpty(evt.Sentiment))
            {
                error = "event_non_russian_output";
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

            if (!ExtractionSemanticContract.IsAllowedProfileTrait(signal.Trait) ||
                !ExtractionSemanticContract.IsSnakeCase(signal.Direction))
            {
                error = "invalid_profile_signal_semantics";
                return false;
            }

            if (!IsValid01(signal.Confidence))
            {
                error = "profile_signal_invalid_confidence";
                return false;
            }

            if (enforceRussianOutput && !HasCyrillicOrEmpty(signal.Evidence))
            {
                error = "profile_signal_non_russian_output";
                return false;
            }
        }

        if (item.RequiresExpensive && string.IsNullOrWhiteSpace(item.Reason))
        {
            error = "requires_expensive_without_reason";
            return false;
        }

        if (enforceRussianOutput && !HasCyrillicOrEmpty(item.Reason))
        {
            error = "reason_non_russian_output";
            return false;
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
        return value >= 0f && value <= 1f;
    }

    private static bool HasCyrillicOrEmpty(string? value)
    {
        return string.IsNullOrWhiteSpace(value) || ExtractionSemanticContract.IsLikelyRussianText(value);
    }

    private static bool HasCyrillicOrStructuredOrEmpty(string? value)
    {
        if (HasCyrillicOrEmpty(value))
        {
            return true;
        }

        return IsSafeStructuredValue(value);
    }

    private static bool IsSafeStructuredValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var compact = MessageContentBuilder.CollapseWhitespace(value).Trim();
        if (compact.Length == 0 || compact.Length > 64)
        {
            return false;
        }

        if (TimeLikeRegex.IsMatch(compact) ||
            DateLikeRegex.IsMatch(compact) ||
            RatioLikeRegex.IsMatch(compact) ||
            CompactScheduleLikeRegex.IsMatch(compact))
        {
            return true;
        }

        if (FormulaLikeRegex.IsMatch(compact) &&
            compact.IndexOfAny(['+', '-', '*', '/', '=', '^', '%']) >= 0)
        {
            return true;
        }

        return false;
    }
}
