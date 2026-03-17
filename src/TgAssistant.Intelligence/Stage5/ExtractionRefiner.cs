using TgAssistant.Core.Configuration;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage5;

public static class ExtractionRefiner
{
    public static ExtractionItem NormalizeExtractionForMessage(ExtractionItem item, Message message)
    {
        _ = message;
        return item;
    }

    public static ExtractionItem SanitizeExtraction(ExtractionItem item)
    {
        item.Reason = NormalizeOptional(item.Reason);

        item.Entities = item.Entities
            .Where(entity => !string.IsNullOrWhiteSpace(entity.Name))
            .Select(entity => new ExtractionEntity
            {
                Name = NormalizeRequired(entity.Name),
                Type = string.IsNullOrWhiteSpace(entity.Type) ? "Person" : NormalizeRequired(entity.Type),
                Confidence = Clamp01(entity.Confidence),
                TrustFactor = ResolveTrustFactor(entity.TrustFactor, entity.Confidence),
                NeedsClarification = entity.NeedsClarification
            })
            .Where(entity => entity.Name.Length > 0)
            .ToList();

        item.Observations = item.Observations
            .Where(observation => !string.IsNullOrWhiteSpace(observation.SubjectName) &&
                                  !string.IsNullOrWhiteSpace(observation.Type))
            .Select(observation => new ExtractionObservation
            {
                SubjectName = NormalizeRequired(observation.SubjectName),
                Type = NormalizeRequired(observation.Type),
                ObjectName = NormalizeOptional(observation.ObjectName),
                Value = NormalizeOptional(observation.Value),
                Evidence = NormalizeOptional(observation.Evidence),
                Confidence = Clamp01(observation.Confidence)
            })
            .Where(observation => observation.SubjectName.Length > 0 && observation.Type.Length > 0)
            .ToList();

        item.Claims = item.Claims
            .Where(claim => !string.IsNullOrWhiteSpace(claim.EntityName) &&
                            !string.IsNullOrWhiteSpace(claim.Key) &&
                            !string.IsNullOrWhiteSpace(claim.Value))
            .Select(claim => new ExtractionClaim
            {
                EntityName = NormalizeRequired(claim.EntityName),
                ClaimType = string.IsNullOrWhiteSpace(claim.ClaimType) ? "fact" : NormalizeRequired(claim.ClaimType),
                Category = string.IsNullOrWhiteSpace(claim.Category) ? "general" : NormalizeRequired(claim.Category),
                Key = NormalizeRequired(claim.Key),
                Value = NormalizeRequired(claim.Value),
                Evidence = NormalizeOptional(claim.Evidence),
                Confidence = Clamp01(claim.Confidence)
            })
            .Where(claim => claim.EntityName.Length > 0 &&
                            claim.Key.Length > 0 &&
                            claim.Value.Length > 0)
            .ToList();

        item.Facts = item.Facts
            .Where(fact => !string.IsNullOrWhiteSpace(fact.EntityName) &&
                           !string.IsNullOrWhiteSpace(fact.Key) &&
                           !string.IsNullOrWhiteSpace(fact.Value))
            .Select(fact => new ExtractionFact
            {
                EntityName = NormalizeRequired(fact.EntityName),
                Category = string.IsNullOrWhiteSpace(fact.Category) ? "general" : NormalizeRequired(fact.Category),
                Key = NormalizeRequired(fact.Key),
                Value = NormalizeRequired(fact.Value),
                Confidence = Clamp01(fact.Confidence),
                TrustFactor = ResolveTrustFactor(fact.TrustFactor, fact.Confidence),
                NeedsClarification = fact.NeedsClarification
            })
            .Where(fact => fact.EntityName.Length > 0 &&
                           fact.Key.Length > 0 &&
                           fact.Value.Length > 0)
            .ToList();

        item.Relationships = item.Relationships
            .Where(relationship => !string.IsNullOrWhiteSpace(relationship.FromEntityName) &&
                                   !string.IsNullOrWhiteSpace(relationship.ToEntityName) &&
                                   !string.IsNullOrWhiteSpace(relationship.Type))
            .Select(relationship => new ExtractionRelationship
            {
                FromEntityName = NormalizeRequired(relationship.FromEntityName),
                ToEntityName = NormalizeRequired(relationship.ToEntityName),
                Type = NormalizeRequired(relationship.Type),
                Confidence = Clamp01(relationship.Confidence)
            })
            .Where(relationship => relationship.FromEntityName.Length > 0 &&
                                   relationship.ToEntityName.Length > 0 &&
                                   relationship.Type.Length > 0)
            .ToList();

        item.Events = item.Events
            .Where(evt => !string.IsNullOrWhiteSpace(evt.Type) &&
                          !string.IsNullOrWhiteSpace(evt.SubjectName))
            .Select(evt => new ExtractionEvent
            {
                Type = NormalizeRequired(evt.Type),
                SubjectName = NormalizeRequired(evt.SubjectName),
                ObjectName = NormalizeOptional(evt.ObjectName),
                Sentiment = NormalizeOptional(evt.Sentiment),
                Summary = NormalizeOptional(evt.Summary),
                Confidence = Clamp01(evt.Confidence)
            })
            .Where(evt => evt.Type.Length > 0 && evt.SubjectName.Length > 0)
            .ToList();

        item.ProfileSignals = item.ProfileSignals
            .Where(signal => !string.IsNullOrWhiteSpace(signal.SubjectName) &&
                             !string.IsNullOrWhiteSpace(signal.Trait))
            .Select(signal => new ExtractionProfileSignal
            {
                SubjectName = NormalizeRequired(signal.SubjectName),
                Trait = NormalizeRequired(signal.Trait),
                Direction = string.IsNullOrWhiteSpace(signal.Direction) ? "neutral" : NormalizeRequired(signal.Direction),
                Evidence = NormalizeOptional(signal.Evidence),
                Confidence = Clamp01(signal.Confidence)
            })
            .Where(signal => signal.SubjectName.Length > 0 && signal.Trait.Length > 0)
            .ToList();

        return item;
    }

    public static ExtractionItem RefineExtractionForMessage(ExtractionItem item, Message? message, AnalysisSettings settings)
    {
        _ = settings;
        var effective = SanitizeExtraction(item);
        if (message == null || message.MediaType == MediaType.None)
        {
            return effective;
        }

        return ApplyMediaTrustCaps(effective, message);
    }

    public static ExtractionItem FinalizeResolvedExtraction(ExtractionItem item)
    {
        var effective = SanitizeExtraction(item);
        effective.RequiresExpensive = false;
        return effective;
    }

    public static bool ShouldRunExpensivePass(Message? message, ExtractionItem extracted, AnalysisSettings settings)
    {
        if (message == null)
        {
            return false;
        }

        return extracted.RequiresExpensive;
    }

    private static string NormalizeRequired(string value)
    {
        return MessageContentBuilder.CollapseWhitespace(value).Trim();
    }

    private static string? NormalizeOptional(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = MessageContentBuilder.CollapseWhitespace(value).Trim();
        return normalized.Length == 0 ? null : normalized;
    }

    private static float ResolveTrustFactor(float trustFactor, float confidence)
    {
        // v10 baseline mapping: keep explicit trust_factor, fallback to confidence.
        if (trustFactor > 0f)
        {
            return Clamp01(trustFactor);
        }

        return Clamp01(confidence);
    }

    private static float Clamp01(float value)
    {
        return Math.Clamp(value, 0f, 1f);
    }

    private static ExtractionItem ApplyMediaTrustCaps(ExtractionItem item, Message message)
    {
        var hasText = !string.IsNullOrWhiteSpace(message.Text);
        var hasTranscript = !string.IsNullOrWhiteSpace(message.MediaTranscription);
        var hasDescription = !string.IsNullOrWhiteSpace(message.MediaDescription);
        var hasParalinguistics = !string.IsNullOrWhiteSpace(message.MediaParalinguisticsJson);

        var trustCap = ResolveMediaTrustCap(hasText, hasTranscript, hasDescription, hasParalinguistics);
        if (trustCap >= 1f)
        {
            return item;
        }

        item.Entities = item.Entities
            .Select(entity =>
            {
                entity.TrustFactor = Math.Min(entity.TrustFactor, trustCap);
                entity.Confidence = Math.Min(entity.Confidence, trustCap);
                return entity;
            })
            .ToList();

        item.Facts = item.Facts
            .Select(fact =>
            {
                fact.TrustFactor = Math.Min(fact.TrustFactor, trustCap);
                fact.Confidence = Math.Min(fact.Confidence, trustCap);
                if (trustCap <= 0.7f)
                {
                    fact.NeedsClarification = true;
                }

                return fact;
            })
            .ToList();

        item.Claims = item.Claims
            .Select(claim =>
            {
                claim.Confidence = Math.Min(claim.Confidence, trustCap);
                return claim;
            })
            .ToList();

        item.Observations = item.Observations
            .Select(observation =>
            {
                observation.Confidence = Math.Min(observation.Confidence, trustCap);
                return observation;
            })
            .ToList();

        item.Events = item.Events
            .Select(evt =>
            {
                evt.Confidence = Math.Min(evt.Confidence, trustCap);
                return evt;
            })
            .ToList();

        item.Relationships = item.Relationships
            .Select(relationship =>
            {
                relationship.Confidence = Math.Min(relationship.Confidence, trustCap);
                return relationship;
            })
            .ToList();

        item.ProfileSignals = item.ProfileSignals
            .Select(signal =>
            {
                signal.Confidence = Math.Min(signal.Confidence, trustCap);
                return signal;
            })
            .ToList();

        return item;
    }

    private static float ResolveMediaTrustCap(
        bool hasText,
        bool hasTranscript,
        bool hasDescription,
        bool hasParalinguistics)
    {
        if (hasTranscript)
        {
            return 0.85f;
        }

        if (hasText && hasDescription)
        {
            return 0.8f;
        }

        if (hasDescription && !hasText)
        {
            return 0.7f;
        }

        if (hasParalinguistics)
        {
            return 0.65f;
        }

        return 0.55f;
    }
}
