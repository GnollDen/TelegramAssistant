using TgAssistant.Core.Configuration;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage5;

public static class ExtractionRefiner
{
    private static readonly string[] WeakValueTokens =
    [
        "да",
        "нет",
        "ага",
        "угу",
        "ок",
        "окей",
        "ясно",
        "понял",
        "поняла",
        "норм",
        "нормально",
        "лол",
        "ахах",
        "хаха"
    ];

    private static readonly string[] JokeTokens =
    [
        "ахах",
        "хаха",
        "лол",
        "шут",
        "ржу",
        ")))",
        "ха-ха",
        "сарказ"
    ];

    private static readonly string[] ActionableTokens =
    [
        "когда",
        "где",
        "во сколько",
        "адрес",
        "завтра",
        "сегодня",
        "встреч",
        "созвон",
        "подъед",
        "поед",
        "такси",
        "деньг",
        "перевод",
        "работ",
        "боле",
        "лекар",
        "распис",
        "@",
        "http"
    ];

    public static ExtractionItem NormalizeExtractionForMessage(ExtractionItem item, Message message)
    {
        _ = message;
        return NormalizeContractIds(item);
    }

    public static ExtractionItem SanitizeExtraction(ExtractionItem item)
    {
        item.Reason = NormalizeOptional(item.Reason);

        item.Entities = item.Entities
            .Where(entity => !string.IsNullOrWhiteSpace(entity.Name))
            .Select(entity => new ExtractionEntity
            {
                Name = NormalizeRequired(entity.Name),
                Type = string.IsNullOrWhiteSpace(entity.Type) ? "Person" : NormalizeEntityType(entity.Type),
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
                Type = NormalizeEnumToken(observation.Type),
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
                ClaimType = string.IsNullOrWhiteSpace(claim.ClaimType) ? "fact" : NormalizeEnumToken(claim.ClaimType),
                Category = string.IsNullOrWhiteSpace(claim.Category) ? "general" : NormalizeEnumToken(claim.Category),
                Key = NormalizeEnumToken(claim.Key),
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
                Category = string.IsNullOrWhiteSpace(fact.Category) ? "general" : NormalizeEnumToken(fact.Category),
                Key = NormalizeEnumToken(fact.Key),
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
                Type = NormalizeRequired(relationship.Type).ToLowerInvariant(),
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
                Type = NormalizeEnumToken(evt.Type),
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
                Trait = NormalizeEnumToken(signal.Trait),
                Direction = string.IsNullOrWhiteSpace(signal.Direction) ? "neutral" : NormalizeEnumToken(signal.Direction),
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
        effective = ApplyTargetedSignalPolish(effective, message);

        if (message == null || message.MediaType == MediaType.None)
        {
            return effective;
        }

        return ApplyMediaTrustCaps(effective, message);
    }

    public static ExtractionItem FinalizeResolvedExtraction(ExtractionItem item)
    {
        var effective = NormalizeContractIds(item);
        effective = SanitizeExtraction(effective);
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

    private static string NormalizeEnumToken(string value)
    {
        return ExtractionSemanticContract.NormalizeToken(value);
    }

    private static string NormalizeEntityType(string value)
    {
        var normalized = NormalizeRequired(value);
        if (normalized.Equals("person", StringComparison.OrdinalIgnoreCase))
        {
            return "Person";
        }

        if (normalized.Equals("organization", StringComparison.OrdinalIgnoreCase))
        {
            return "Organization";
        }

        if (normalized.Equals("place", StringComparison.OrdinalIgnoreCase))
        {
            return "Place";
        }

        if (normalized.Equals("pet", StringComparison.OrdinalIgnoreCase))
        {
            return "Pet";
        }

        if (normalized.Equals("event", StringComparison.OrdinalIgnoreCase))
        {
            return "Event";
        }

        return normalized;
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

    private static ExtractionItem NormalizeContractIds(ExtractionItem item)
    {
        item.Claims = item.Claims
            .Select(claim =>
            {
                var category = ExtractionSemanticContract.CanonicalizeCategory(claim.Category);
                return new ExtractionClaim
                {
                    EntityName = claim.EntityName,
                    ClaimType = ExtractionSemanticContract.CanonicalizeClaimType(claim.ClaimType),
                    Category = category,
                    Key = ExtractionSemanticContract.CanonicalizeKey(category, claim.Key),
                    Value = claim.Value,
                    Evidence = claim.Evidence,
                    Confidence = claim.Confidence
                };
            })
            .ToList();

        item.Facts = item.Facts
            .Select(fact =>
            {
                var category = ExtractionSemanticContract.CanonicalizeCategory(fact.Category);
                return new ExtractionFact
                {
                    EntityName = fact.EntityName,
                    Category = category,
                    Key = ExtractionSemanticContract.CanonicalizeKey(category, fact.Key),
                    Value = fact.Value,
                    Confidence = fact.Confidence,
                    TrustFactor = fact.TrustFactor,
                    NeedsClarification = fact.NeedsClarification
                };
            })
            .ToList();

        item.Relationships = item.Relationships
            .Select(relationship => new ExtractionRelationship
            {
                FromEntityName = relationship.FromEntityName,
                ToEntityName = relationship.ToEntityName,
                Type = ExtractionSemanticContract.CanonicalizeRelationshipType(relationship.Type),
                Confidence = relationship.Confidence
            })
            .ToList();

        item.Observations = item.Observations
            .Select(observation => new ExtractionObservation
            {
                SubjectName = observation.SubjectName,
                Type = ExtractionSemanticContract.CanonicalizeObservationType(observation.Type),
                ObjectName = observation.ObjectName,
                Value = observation.Value,
                Evidence = observation.Evidence,
                Confidence = observation.Confidence
            })
            .ToList();

        item.Events = item.Events
            .Select(evt => new ExtractionEvent
            {
                Type = ExtractionSemanticContract.CanonicalizeEventType(evt.Type),
                SubjectName = evt.SubjectName,
                ObjectName = evt.ObjectName,
                Sentiment = evt.Sentiment,
                Summary = evt.Summary,
                Confidence = evt.Confidence
            })
            .ToList();

        item.ProfileSignals = item.ProfileSignals
            .Select(signal => new ExtractionProfileSignal
            {
                SubjectName = signal.SubjectName,
                Trait = ExtractionSemanticContract.CanonicalizeTrait(signal.Trait),
                Direction = signal.Direction,
                Evidence = signal.Evidence,
                Confidence = signal.Confidence
            })
            .ToList();

        return item;
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

    private static ExtractionItem ApplyTargetedSignalPolish(ExtractionItem item, Message? message)
    {
        var lowSignalQuestionOrJoke = IsLikelyLowSignalQuestionOrJoke(message);

        item.Facts = item.Facts
            .Where(fact => ShouldKeepFact(fact, lowSignalQuestionOrJoke))
            .ToList();
        item.Claims = item.Claims
            .Where(claim => ShouldKeepClaim(claim, lowSignalQuestionOrJoke))
            .ToList();
        item.Observations = item.Observations
            .Where(observation => ShouldKeepObservation(observation, lowSignalQuestionOrJoke))
            .ToList();

        if (lowSignalQuestionOrJoke)
        {
            item.Relationships = item.Relationships
                .Where(relationship => relationship.Confidence >= 0.75f)
                .ToList();
            item.Events = item.Events
                .Where(evt => evt.Confidence >= 0.75f)
                .ToList();
            item.ProfileSignals = item.ProfileSignals
                .Where(signal => signal.Confidence >= 0.75f)
                .ToList();
        }

        item.Facts = item.Facts
            .DistinctBy(BuildFactSignature)
            .ToList();

        item.Claims = item.Claims
            .DistinctBy(BuildClaimSignature)
            .Where(claim => !item.Facts.Any(fact => IsClaimDuplicateOfFact(claim, fact)))
            .ToList();

        item.Observations = item.Observations
            .DistinctBy(BuildObservationSignature)
            .Where(observation => !IsObservationDuplicatingFactOrClaim(observation, item.Facts, item.Claims))
            .ToList();

        return item;
    }

    private static bool ShouldKeepFact(ExtractionFact fact, bool lowSignalQuestionOrJoke)
    {
        if (IsWeakTextValue(fact.Value))
        {
            return false;
        }

        var trust = Math.Max(fact.Confidence, fact.TrustFactor);
        if (trust < 0.45f)
        {
            return false;
        }

        if (lowSignalQuestionOrJoke && trust < 0.75f)
        {
            return false;
        }

        return true;
    }

    private static bool ShouldKeepClaim(ExtractionClaim claim, bool lowSignalQuestionOrJoke)
    {
        if (IsWeakTextValue(claim.Value))
        {
            return false;
        }

        if (claim.Confidence < 0.35f)
        {
            return false;
        }

        if (lowSignalQuestionOrJoke && claim.Confidence < 0.7f)
        {
            return false;
        }

        return true;
    }

    private static bool ShouldKeepObservation(ExtractionObservation observation, bool lowSignalQuestionOrJoke)
    {
        if (IsWeakTextValue(observation.Value) && IsWeakTextValue(observation.Evidence))
        {
            return false;
        }

        if (observation.Confidence < 0.35f)
        {
            return false;
        }

        if (lowSignalQuestionOrJoke && observation.Confidence < 0.7f)
        {
            return false;
        }

        return true;
    }

    private static bool IsWeakTextValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.Length <= 2)
        {
            return true;
        }

        return WeakValueTokens.Any(token => normalized.Equals(token, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsLikelyLowSignalQuestionOrJoke(Message? message)
    {
        if (message == null)
        {
            return false;
        }

        var semantic = MessageContentBuilder.CollapseWhitespace(MessageContentBuilder.BuildSemanticContent(message));
        if (string.IsNullOrWhiteSpace(semantic) || semantic.Length > 100)
        {
            return false;
        }

        var lower = semantic.ToLowerInvariant();
        var hasQuestion = lower.Contains('?') || lower.Contains(" ли ");
        var hasJoke = JokeTokens.Any(token => lower.Contains(token, StringComparison.OrdinalIgnoreCase));
        if (!hasQuestion && !hasJoke)
        {
            return false;
        }

        var hasActionable = ActionableTokens.Any(token => lower.Contains(token, StringComparison.OrdinalIgnoreCase))
                            || lower.Any(char.IsDigit);
        return !hasActionable;
    }

    private static string BuildFactSignature(ExtractionFact fact)
    {
        return string.Join('|',
            NormalizeCompare(fact.EntityName),
            NormalizeCompare(fact.Category),
            NormalizeCompare(fact.Key),
            NormalizeCompare(fact.Value));
    }

    private static string BuildClaimSignature(ExtractionClaim claim)
    {
        return string.Join('|',
            NormalizeCompare(claim.EntityName),
            NormalizeCompare(claim.Category),
            NormalizeCompare(claim.Key),
            NormalizeCompare(claim.Value));
    }

    private static string BuildObservationSignature(ExtractionObservation observation)
    {
        return string.Join('|',
            NormalizeCompare(observation.SubjectName),
            NormalizeCompare(observation.Type),
            NormalizeCompare(observation.ObjectName),
            NormalizeCompare(observation.Value),
            NormalizeCompare(observation.Evidence));
    }

    private static bool IsClaimDuplicateOfFact(ExtractionClaim claim, ExtractionFact fact)
    {
        return string.Equals(NormalizeCompare(claim.EntityName), NormalizeCompare(fact.EntityName), StringComparison.Ordinal)
               && string.Equals(NormalizeCompare(claim.Category), NormalizeCompare(fact.Category), StringComparison.Ordinal)
               && string.Equals(NormalizeCompare(claim.Key), NormalizeCompare(fact.Key), StringComparison.Ordinal)
               && string.Equals(NormalizeCompare(claim.Value), NormalizeCompare(fact.Value), StringComparison.Ordinal);
    }

    private static bool IsObservationDuplicatingFactOrClaim(
        ExtractionObservation observation,
        IReadOnlyCollection<ExtractionFact> facts,
        IReadOnlyCollection<ExtractionClaim> claims)
    {
        var subject = NormalizeCompare(observation.SubjectName);
        var observationValue = NormalizeCompare(observation.Value);
        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(observationValue))
        {
            return false;
        }

        foreach (var fact in facts)
        {
            if (!string.Equals(subject, NormalizeCompare(fact.EntityName), StringComparison.Ordinal))
            {
                continue;
            }

            if (!string.Equals(observationValue, NormalizeCompare(fact.Value), StringComparison.Ordinal))
            {
                continue;
            }

            if (HasSemanticOverlap(observation.Type, fact.Category, fact.Key))
            {
                return true;
            }
        }

        foreach (var claim in claims)
        {
            if (!string.Equals(subject, NormalizeCompare(claim.EntityName), StringComparison.Ordinal))
            {
                continue;
            }

            if (!string.Equals(observationValue, NormalizeCompare(claim.Value), StringComparison.Ordinal))
            {
                continue;
            }

            if (HasSemanticOverlap(observation.Type, claim.Category, claim.Key))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasSemanticOverlap(string? observationType, string? category, string? key)
    {
        var type = NormalizeCompare(observationType);
        if (string.IsNullOrWhiteSpace(type))
        {
            return false;
        }

        var categoryNorm = NormalizeCompare(category);
        var keyNorm = NormalizeCompare(key);
        if (!string.IsNullOrWhiteSpace(categoryNorm) && type.Contains(categoryNorm, StringComparison.Ordinal))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(keyNorm) && type.Contains(keyNorm, StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }

    private static string NormalizeCompare(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return MessageContentBuilder.CollapseWhitespace(value).Trim().ToLowerInvariant();
    }
}
