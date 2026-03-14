using System.Text.RegularExpressions;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage5;

public static class ExtractionRefiner
{
    private static readonly Regex WordTokenRegex = new(@"[\p{L}\p{N}]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex HouseNumberTailRegex = new(
        @"^[\p{L}\-]+\s+[\p{L}\-]+(?:\s+[\p{L}\-]+){0,2}\s+\d+(?:/\d+)?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex AnyDigitRegex = new(@"\d", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex TimeTokenRegex = new(@"\b\d{1,2}(:\d{2})?\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex NumberTokenRegex = new(@"\b\d+\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Normalizes generic actor placeholders using message sender context.
    /// </summary>
    public static ExtractionItem NormalizeExtractionForMessage(ExtractionItem item, Message message)
    {
        var senderName = message.SenderName?.Trim();
        if (string.IsNullOrWhiteSpace(senderName))
        {
            return item;
        }

        foreach (var entity in item.Entities)
        {
            if (IsGenericEntityToken(entity.Name))
            {
                entity.Name = senderName;
                if (string.IsNullOrWhiteSpace(entity.Type))
                {
                    entity.Type = "Person";
                }
            }
        }

        foreach (var fact in item.Facts)
        {
            if (IsGenericEntityToken(fact.EntityName))
            {
                fact.EntityName = senderName;
            }
        }

        foreach (var claim in item.Claims)
        {
            if (IsGenericEntityToken(claim.EntityName))
            {
                claim.EntityName = senderName;
            }
        }

        foreach (var rel in item.Relationships)
        {
            var fromGeneric = IsGenericEntityToken(rel.FromEntityName);
            var toGeneric = IsGenericEntityToken(rel.ToEntityName);
            if (fromGeneric && toGeneric)
            {
                // Drop ambiguous self/self placeholders; they produce noisy links.
                rel.FromEntityName = string.Empty;
                rel.ToEntityName = string.Empty;
                continue;
            }

            if (fromGeneric)
            {
                rel.FromEntityName = senderName;
            }

            if (toGeneric)
            {
                rel.ToEntityName = senderName;
            }
        }

        foreach (var observation in item.Observations)
        {
            if (IsGenericEntityToken(observation.SubjectName))
            {
                observation.SubjectName = senderName;
            }

            if (!string.IsNullOrWhiteSpace(observation.ObjectName) && IsGenericEntityToken(observation.ObjectName))
            {
                observation.ObjectName = senderName;
            }
        }

        return item;
    }

    /// <summary>
    /// Sanitizes extraction by trimming, filtering invalid rows and normalizing defaults.
    /// </summary>
    public static ExtractionItem SanitizeExtraction(ExtractionItem item)
    {
        item.Reason = string.IsNullOrWhiteSpace(item.Reason) ? null : item.Reason.Trim();

        item.Entities = item.Entities
            .Where(e => !string.IsNullOrWhiteSpace(e.Name))
            .Select(e => new ExtractionEntity
            {
                Name = e.Name.Trim(),
                Type = string.IsNullOrWhiteSpace(e.Type) ? "Person" : e.Type.Trim(),
                Confidence = Clamp01(e.Confidence)
            })
            .ToList();

        item.Observations = item.Observations
            .Where(o => !string.IsNullOrWhiteSpace(o.SubjectName) && !string.IsNullOrWhiteSpace(o.Type))
            .Select(o => new ExtractionObservation
            {
                SubjectName = o.SubjectName.Trim(),
                Type = o.Type.Trim(),
                ObjectName = string.IsNullOrWhiteSpace(o.ObjectName) ? null : o.ObjectName.Trim(),
                Value = string.IsNullOrWhiteSpace(o.Value) ? null : o.Value.Trim(),
                Evidence = string.IsNullOrWhiteSpace(o.Evidence) ? null : o.Evidence.Trim(),
                Confidence = Clamp01(o.Confidence)
            })
            .Where(o => o.SubjectName.Length > 0 && o.Type.Length > 0)
            .ToList();

        item.Claims = item.Claims
            .Where(c => !string.IsNullOrWhiteSpace(c.EntityName) && !string.IsNullOrWhiteSpace(c.Key))
            .Select(c => new ExtractionClaim
            {
                EntityName = c.EntityName.Trim(),
                ClaimType = string.IsNullOrWhiteSpace(c.ClaimType) ? "fact" : c.ClaimType.Trim(),
                Category = string.IsNullOrWhiteSpace(c.Category) ? "general" : c.Category.Trim(),
                Key = c.Key.Trim(),
                Value = NormalizeClaimValue(c),
                Evidence = string.IsNullOrWhiteSpace(c.Evidence) ? null : c.Evidence.Trim(),
                Confidence = Clamp01(c.Confidence)
            })
            .Where(c => c.EntityName.Length > 0 && c.Key.Length > 0 && c.Value.Length > 0)
            .ToList();

        item.Facts = item.Facts
            .Where(f => !string.IsNullOrWhiteSpace(f.EntityName) && !string.IsNullOrWhiteSpace(f.Key))
            .Select(f => new ExtractionFact
            {
                EntityName = f.EntityName.Trim(),
                Category = string.IsNullOrWhiteSpace(f.Category) ? "general" : f.Category.Trim(),
                Key = f.Key.Trim(),
                Value = NormalizePlainValue(f.Value),
                Confidence = Clamp01(f.Confidence)
            })
            .Where(f => f.EntityName.Length > 0 && f.Key.Length > 0 && f.Value.Length > 0)
            .ToList();

        item.Relationships = item.Relationships
            .Where(r => !string.IsNullOrWhiteSpace(r.FromEntityName)
                        && !string.IsNullOrWhiteSpace(r.ToEntityName)
                        && !string.IsNullOrWhiteSpace(r.Type))
            .Select(r => new ExtractionRelationship
            {
                FromEntityName = r.FromEntityName.Trim(),
                ToEntityName = r.ToEntityName.Trim(),
                Type = r.Type.Trim(),
                Confidence = Clamp01(r.Confidence)
            })
            .Where(r => r.FromEntityName.Length > 0 && r.ToEntityName.Length > 0 && r.Type.Length > 0)
            .ToList();

        item.Events = item.Events
            .Where(e => !string.IsNullOrWhiteSpace(e.Type) && !string.IsNullOrWhiteSpace(e.SubjectName))
            .Select(e => new ExtractionEvent
            {
                Type = e.Type.Trim(),
                SubjectName = e.SubjectName.Trim(),
                ObjectName = string.IsNullOrWhiteSpace(e.ObjectName) ? null : e.ObjectName.Trim(),
                Sentiment = string.IsNullOrWhiteSpace(e.Sentiment) ? null : e.Sentiment.Trim().ToLowerInvariant(),
                Summary = string.IsNullOrWhiteSpace(e.Summary) ? null : e.Summary.Trim(),
                Confidence = Clamp01(e.Confidence)
            })
            .Where(e => e.Type.Length > 0 && e.SubjectName.Length > 0)
            .ToList();

        item.ProfileSignals = item.ProfileSignals
            .Where(s => !string.IsNullOrWhiteSpace(s.SubjectName) && !string.IsNullOrWhiteSpace(s.Trait))
            .Select(s => new ExtractionProfileSignal
            {
                SubjectName = s.SubjectName.Trim(),
                Trait = s.Trait.Trim().ToLowerInvariant(),
                Direction = string.IsNullOrWhiteSpace(s.Direction) ? "neutral" : s.Direction.Trim().ToLowerInvariant(),
                Evidence = string.IsNullOrWhiteSpace(s.Evidence) ? null : s.Evidence.Trim(),
                Confidence = Clamp01(s.Confidence)
            })
            .Where(s => s.SubjectName.Length > 0 && s.Trait.Length > 0)
            .ToList();

        return item;
    }

    /// <summary>
    /// Applies deterministic refinement heuristics using source message content.
    /// </summary>
    public static ExtractionItem RefineExtractionForMessage(ExtractionItem item, Message? message, AnalysisSettings settings)
    {
        if (message == null)
        {
            return item;
        }

        var rawContent = MessageContentBuilder.BuildSemanticContent(message);
        if (string.IsNullOrWhiteSpace(rawContent))
        {
            item.Entities = TrimUnreferencedEntities(item);
            return item;
        }

        var content = MessageContentBuilder.CollapseWhitespace(rawContent);
        var normalized = content.ToLowerInvariant();
        PromoteLocationFallback(item, message, rawContent, normalized);
        PromoteContactFallback(item, message, rawContent);
        PromoteWorkAssessmentFallback(item, message, content, normalized);
        PruneLowValueSignals(item, normalized, settings);
        item.Entities = TrimUnreferencedEntities(item);
        return item;
    }

    /// <summary>
    /// Finalizes extraction after expensive pass and clears expensive flag.
    /// </summary>
    public static ExtractionItem FinalizeResolvedExtraction(ExtractionItem item)
    {
        var effective = SanitizeExtraction(item);
        effective.RequiresExpensive = false;

        if (IsLowSignalExtraction(effective) && IsLowValueAmbiguityReason(effective.Reason))
        {
            effective.Entities = new List<ExtractionEntity>();
        }

        return effective;
    }

    /// <summary>
    /// Determines whether message extraction should enter expensive resolution.
    /// </summary>
    public static bool ShouldRunExpensivePass(Message? message, ExtractionItem extracted, AnalysisSettings settings)
    {
        if (message == null)
        {
            return false;
        }

        var content = MessageContentBuilder.BuildSemanticContent(message);
        if (string.IsNullOrWhiteSpace(content) || IsLikelyFillerMessage(content))
        {
            return false;
        }

        var lowSignalEscalation = ShouldEscalateLowSignalExtraction(message, extracted);
        if (lowSignalEscalation)
        {
            return true;
        }

        var hasLowConfidenceStructuredSignal =
            extracted.Facts.Any(f => f.Confidence < settings.CheapConfidenceThreshold && IsHighValueCategory(f.Category)) ||
            extracted.Relationships.Any(r => r.Confidence < settings.CheapConfidenceThreshold) ||
            extracted.Claims.Any(c => c.Confidence < settings.CheapConfidenceThreshold && IsHighValueCategory(c.Category)) ||
            extracted.Events.Any(e => e.Confidence < settings.CheapConfidenceThreshold) ||
            extracted.Observations.Any(o => o.Confidence < settings.CheapConfidenceThreshold && IsHighValueObservationType(o.Type));

        if (!extracted.RequiresExpensive && !hasLowConfidenceStructuredSignal)
        {
            return false;
        }

        return IsHighValueEscalationCandidate(content, extracted);
    }

    private static bool IsGenericEntityToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "sender" => true,
            "author" => true,
            "speaker" => true,
            "user" => true,
            "me" => true,
            "myself" => true,
            "self" => true,
            "i" => true,
            _ => false
        };
    }

    private static void PromoteLocationFallback(ExtractionItem item, Message message, string content, string normalizedContent)
    {
        if (HasLocationSignal(item))
        {
            return;
        }

        if (!TryExtractAddressLikeText(content, normalizedContent, out var address))
        {
            return;
        }

        var senderName = message.SenderName?.Trim();
        if (string.IsNullOrWhiteSpace(senderName))
        {
            return;
        }

        var evidence = MessageContentBuilder.TruncateForContext(address, 200);
        AddEntityIfMissing(item.Entities, address, "Place", 0.9f);
        AddObservationIfMissing(item.Observations, new ExtractionObservation
        {
            SubjectName = senderName,
            Type = "location_update",
            ObjectName = address,
            Value = address,
            Evidence = evidence,
            Confidence = 0.84f
        });
        AddClaimIfMissing(item.Claims, new ExtractionClaim
        {
            EntityName = senderName,
            ClaimType = "fact",
            Category = "location",
            Key = "shared_location",
            Value = address,
            Evidence = evidence,
            Confidence = 0.84f
        });
        AddFactIfMissing(item.Facts, new ExtractionFact
        {
            EntityName = senderName,
            Category = "location",
            Key = "shared_location",
            Value = address,
            Confidence = 0.84f
        });
    }

    private static void PromoteContactFallback(ExtractionItem item, Message message, string content)
    {
        if (HasContactSignal(item))
        {
            return;
        }

        if (!TryExtractContactShare(content, out var contactName, out var handle, out var evidence))
        {
            return;
        }

        var senderName = message.SenderName?.Trim();
        if (string.IsNullOrWhiteSpace(senderName))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(contactName))
        {
            AddEntityIfMissing(item.Entities, contactName, "Person", 0.9f);
        }

        var contactValue = string.IsNullOrWhiteSpace(contactName)
            ? handle
            : $"{contactName} {handle}";

        AddObservationIfMissing(item.Observations, new ExtractionObservation
        {
            SubjectName = senderName,
            Type = "contact_share",
            ObjectName = string.IsNullOrWhiteSpace(contactName) ? handle : contactName,
            Value = handle,
            Evidence = evidence,
            Confidence = 0.84f
        });
        AddClaimIfMissing(item.Claims, new ExtractionClaim
        {
            EntityName = senderName,
            ClaimType = "fact",
            Category = "contact",
            Key = "shared_contact",
            Value = contactValue,
            Evidence = evidence,
            Confidence = 0.84f
        });
        AddFactIfMissing(item.Facts, new ExtractionFact
        {
            EntityName = senderName,
            Category = "contact",
            Key = "shared_contact",
            Value = contactValue,
            Confidence = 0.84f
        });
    }

    private static void PromoteWorkAssessmentFallback(ExtractionItem item, Message message, string content, string normalizedContent)
    {
        if (item.Claims.Any(c => string.Equals(c.Category, "work", StringComparison.OrdinalIgnoreCase)) ||
            item.Facts.Any(f => string.Equals(f.Category, "work", StringComparison.OrdinalIgnoreCase)) ||
            item.Observations.Any(o => string.Equals(o.Type, "work_assessment", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        if (!LooksLikeWorkAssessmentMessage(normalizedContent))
        {
            return;
        }

        var senderName = message.SenderName?.Trim();
        if (string.IsNullOrWhiteSpace(senderName))
        {
            return;
        }

        var evidence = MessageContentBuilder.TruncateForContext(content, 220);
        AddObservationIfMissing(item.Observations, new ExtractionObservation
        {
            SubjectName = senderName,
            Type = "work_assessment",
            ObjectName = "team",
            Value = evidence,
            Evidence = evidence,
            Confidence = 0.8f
        });
        AddClaimIfMissing(item.Claims, new ExtractionClaim
        {
            EntityName = senderName,
            ClaimType = "state",
            Category = "work",
            Key = "team_assessment",
            Value = evidence,
            Evidence = evidence,
            Confidence = 0.8f
        });
    }

    private static void PruneLowValueSignals(ExtractionItem item, string normalizedContent, AnalysisSettings settings)
    {
        var hasHighValueCategory = item.Facts.Any(f => IsHighValueCategory(f.Category))
                                   || item.Claims.Any(c => IsHighValueCategory(c.Category));
        if (hasHighValueCategory)
        {
            return;
        }

        var hasConfidentSignal = item.Facts.Any(f => f.Confidence >= settings.MinFactConfidence)
                                 || item.Claims.Any(c => c.Confidence >= settings.MinFactConfidence);
        if (hasConfidentSignal)
        {
            return;
        }

        if (HasHighValueStructuredSignal(item))
        {
            return;
        }

        var keepOperational = HasConcreteActionableAnchor(normalizedContent) && HasOperationalSignal(item);
        var keepContextual = HasStrongDossierSignal(normalizedContent) && HasNonTrivialStructuredSignal(item);

        if (!keepOperational && !keepContextual)
        {
            item.Observations.Clear();
            item.Claims.Clear();
            item.Facts.Clear();
            item.Relationships.Clear();
            item.Events.Clear();
            item.ProfileSignals.Clear();
            return;
        }

        item.Observations = item.Observations
            .Where(o => ShouldKeepOperationalObservation(o, normalizedContent))
            .ToList();

        item.Claims = item.Claims
            .Where(c => ShouldKeepOperationalClaim(c, normalizedContent))
            .ToList();

        item.Facts = item.Facts
            .Where(ShouldKeepOperationalFact)
            .ToList();
    }

    private static bool HasOperationalSignal(ExtractionItem item)
    {
        return item.Observations.Any(o => IsOperationalObservationType(o.Type))
               || item.Claims.Any(IsOperationalClaim)
               || item.Facts.Any(ShouldKeepOperationalFact);
    }

    private static bool HasNonTrivialStructuredSignal(ExtractionItem item)
    {
        return item.Observations.Count > 0
               || item.Claims.Count > 0
               || item.Facts.Count > 0
               || item.Relationships.Count > 0
               || item.Events.Count > 0
               || item.ProfileSignals.Count > 0;
    }

    private static bool ShouldKeepOperationalObservation(ExtractionObservation observation, string normalizedContent)
    {
        if (IsHighValueObservationType(observation.Type))
        {
            return true;
        }

        var type = observation.Type.Trim().ToLowerInvariant();
        return type switch
        {
            "request" or "question" or "intent" => HasConcreteActionableAnchor(normalizedContent),
            "status_update" => HasStrongDossierSignal(normalizedContent),
            _ => false
        };
    }

    private static bool ShouldKeepOperationalClaim(ExtractionClaim claim, string normalizedContent)
    {
        if (IsHighValueCategory(claim.Category) || IsLocationOrContactKey(claim.Key))
        {
            return true;
        }

        var claimType = string.IsNullOrWhiteSpace(claim.ClaimType)
            ? string.Empty
            : claim.ClaimType.Trim().ToLowerInvariant();

        return claimType is "intent" or "need"
               && HasConcreteActionableAnchor(normalizedContent);
    }

    private static bool ShouldKeepOperationalFact(ExtractionFact fact)
    {
        return IsHighValueCategory(fact.Category) || IsLocationOrContactKey(fact.Key);
    }

    private static bool IsOperationalObservationType(string? type)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            return false;
        }

        return type.Trim().ToLowerInvariant() is
            "request" or "question" or "intent" or "status_update"
            or "availability_update" or "schedule_update" or "movement"
            or "location_update" or "contact_share" or "work_assessment";
    }

    private static bool IsOperationalClaim(ExtractionClaim claim)
    {
        return IsHighValueCategory(claim.Category)
               || IsLocationOrContactKey(claim.Key)
               || string.Equals(claim.ClaimType, "intent", StringComparison.OrdinalIgnoreCase)
               || string.Equals(claim.ClaimType, "need", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasLocationSignal(ExtractionItem item)
    {
        return item.Facts.Any(f => string.Equals(f.Category, "location", StringComparison.OrdinalIgnoreCase))
               || item.Claims.Any(c => string.Equals(c.Category, "location", StringComparison.OrdinalIgnoreCase))
               || item.Observations.Any(o => string.Equals(o.Type, "location_update", StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasContactSignal(ExtractionItem item)
    {
        return item.Facts.Any(f => string.Equals(f.Category, "contact", StringComparison.OrdinalIgnoreCase) || IsLocationOrContactKey(f.Key))
               || item.Claims.Any(c => string.Equals(c.Category, "contact", StringComparison.OrdinalIgnoreCase) || IsLocationOrContactKey(c.Key))
               || item.Observations.Any(o => string.Equals(o.Type, "contact_share", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsLocationOrContactKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        return key.Trim().ToLowerInvariant() is "shared_location" or "shared_address" or "shared_contact";
    }

    private static bool TryExtractAddressLikeText(string content, string normalizedContent, out string address)
    {
        address = string.Empty;
        var lines = MessageContentBuilder.SplitMeaningfulLines(content).ToList();
        var hasMapLink = normalizedContent.Contains("yandex.ru/maps", StringComparison.Ordinal)
                         || normalizedContent.Contains("google.com/maps", StringComparison.Ordinal)
                         || normalizedContent.Contains("2gis.ru", StringComparison.Ordinal);

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.Contains("http://", StringComparison.OrdinalIgnoreCase) || trimmed.Contains("https://", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var normalizedLine = trimmed.ToLowerInvariant();
            var hasAddressLexeme = ContainsAny(normalizedLine,
                "ул", "улиц", "просп", "шоссе", "переул", "бул", "дом", "д.", "кв", "корп", "подъезд", "этаж", "адрес");

            var looksLikeHouseNumberTail = HouseNumberTailRegex.IsMatch(trimmed);
            if ((hasAddressLexeme && AnyDigitRegex.IsMatch(trimmed)) || (hasMapLink && looksLikeHouseNumberTail) || looksLikeHouseNumberTail)
            {
                address = trimmed;
                return true;
            }
        }

        return false;
    }

    private static bool TryExtractContactShare(string content, out string contactName, out string handle, out string evidence)
    {
        contactName = string.Empty;
        handle = string.Empty;
        evidence = string.Empty;

        var match = Regex.Match(
            content,
            @"(?im)^(?<name>[\p{L}][\p{L}\-]+(?:\s+[\p{L}][\p{L}\-]+){0,2})\s+@(?<handle>[A-Za-z0-9_]{3,32})\s*$");

        if (!match.Success)
        {
            return false;
        }

        contactName = match.Groups["name"].Value.Trim();
        handle = "@" + match.Groups["handle"].Value.Trim();
        evidence = MessageContentBuilder.TruncateForContext(match.Value.Trim(), 160);
        return true;
    }

    private static bool LooksLikeWorkAssessmentMessage(string normalizedContent)
    {
        if (!ContainsAny(normalizedContent, "команд", "трайб", "алерт", "дефект", "тойл", "работ", "релиз", "проект"))
        {
            return false;
        }

        return ContainsAny(normalizedContent,
            "не сравним", "хуже", "лучше", "все плохо", "всё плохо", "пиздец", "амеб", "амёб",
            "шикар", "горди", "прибавилось", "мастер класс", "мастер-класс", "хорошо", "плохо");
    }

    private static void AddEntityIfMissing(List<ExtractionEntity> entities, string name, string type, float confidence)
    {
        if (string.IsNullOrWhiteSpace(name) || entities.Any(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        entities.Add(new ExtractionEntity
        {
            Name = name,
            Type = type,
            Confidence = confidence
        });
    }

    private static void AddObservationIfMissing(List<ExtractionObservation> observations, ExtractionObservation observation)
    {
        if (observations.Any(existing =>
                string.Equals(existing.SubjectName, observation.SubjectName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(existing.Type, observation.Type, StringComparison.OrdinalIgnoreCase)
                && string.Equals(existing.Value, observation.Value, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        observations.Add(observation);
    }

    private static void AddClaimIfMissing(List<ExtractionClaim> claims, ExtractionClaim claim)
    {
        if (claims.Any(existing =>
                string.Equals(existing.EntityName, claim.EntityName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(existing.Category, claim.Category, StringComparison.OrdinalIgnoreCase)
                && string.Equals(existing.Key, claim.Key, StringComparison.OrdinalIgnoreCase)
                && string.Equals(existing.Value, claim.Value, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        claims.Add(claim);
    }

    private static void AddFactIfMissing(List<ExtractionFact> facts, ExtractionFact fact)
    {
        if (facts.Any(existing =>
                string.Equals(existing.EntityName, fact.EntityName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(existing.Category, fact.Category, StringComparison.OrdinalIgnoreCase)
                && string.Equals(existing.Key, fact.Key, StringComparison.OrdinalIgnoreCase)
                && string.Equals(existing.Value, fact.Value, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        facts.Add(fact);
    }

    private static List<ExtractionEntity> TrimUnreferencedEntities(ExtractionItem item)
    {
        var referencedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var fact in item.Facts)
        {
            if (!string.IsNullOrWhiteSpace(fact.EntityName))
            {
                referencedNames.Add(fact.EntityName);
            }
        }

        foreach (var claim in item.Claims)
        {
            if (!string.IsNullOrWhiteSpace(claim.EntityName))
            {
                referencedNames.Add(claim.EntityName);
            }
        }

        foreach (var relation in item.Relationships)
        {
            if (!string.IsNullOrWhiteSpace(relation.FromEntityName))
            {
                referencedNames.Add(relation.FromEntityName);
            }

            if (!string.IsNullOrWhiteSpace(relation.ToEntityName))
            {
                referencedNames.Add(relation.ToEntityName);
            }
        }

        foreach (var observation in item.Observations)
        {
            if (!string.IsNullOrWhiteSpace(observation.SubjectName))
            {
                referencedNames.Add(observation.SubjectName);
            }

            if (!string.IsNullOrWhiteSpace(observation.ObjectName))
            {
                referencedNames.Add(observation.ObjectName);
            }
        }

        foreach (var evt in item.Events)
        {
            if (!string.IsNullOrWhiteSpace(evt.SubjectName))
            {
                referencedNames.Add(evt.SubjectName);
            }

            if (!string.IsNullOrWhiteSpace(evt.ObjectName))
            {
                referencedNames.Add(evt.ObjectName);
            }
        }

        foreach (var signal in item.ProfileSignals)
        {
            if (!string.IsNullOrWhiteSpace(signal.SubjectName))
            {
                referencedNames.Add(signal.SubjectName);
            }
        }

        return item.Entities
            .Where(e => !string.IsNullOrWhiteSpace(e.Name) && referencedNames.Contains(e.Name))
            .ToList();
    }

    private static float Clamp01(float value) => Math.Max(0f, Math.Min(1f, value));

    private static string NormalizeClaimValue(ExtractionClaim claim)
    {
        var direct = NormalizePlainValue(claim.Value);
        if (direct.Length > 0)
        {
            return direct;
        }

        var evidence = NormalizePlainValue(claim.Evidence);
        if (evidence.Length > 0)
        {
            return evidence;
        }

        return HumanizeKey(claim.Key);
    }

    private static string NormalizePlainValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    private static string HumanizeKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        return key.Trim().Replace('_', ' ').Replace('-', ' ');
    }

    private static bool ShouldEscalateLowSignalExtraction(Message message, ExtractionItem extracted)
    {
        if (!IsLowSignalExtraction(extracted))
        {
            return false;
        }

        var content = MessageContentBuilder.BuildSemanticContent(message);
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        if (IsLikelyFillerMessage(content))
        {
            return false;
        }

        return HasSemanticSignal(content);
    }

    private static bool IsLowSignalExtraction(ExtractionItem item)
    {
        return item.Claims.Count == 0
               && item.Observations.Count == 0
               && item.Facts.Count == 0
               && item.Events.Count == 0
               && item.Relationships.Count == 0
               && item.ProfileSignals.Count == 0;
    }

    private static bool IsLikelyFillerMessage(string text)
    {
        var normalized = text.Trim().ToLowerInvariant();
        if (normalized.Length == 0)
        {
            return true;
        }

        if (!normalized.Any(char.IsLetterOrDigit))
        {
            return true;
        }

        var tokens = WordTokenRegex.Matches(normalized)
            .Select(m => m.Value)
            .ToList();
        if (tokens.Count == 0)
        {
            return true;
        }

        var filler = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "\u043E\u043A", "ok", "\u0430\u0433\u0430", "\u0443\u0433\u0443", "\u044F\u0441\u043D\u043E",
            "\u043F\u043E\u043D", "\u043F\u043E\u043D\u044F\u0442\u043D\u043E", "\u0441\u043F\u0441",
            "\u0441\u043F\u0430\u0441\u0438\u0431\u043E", "thanks", "thank", "\u043B\u043E\u043B", "haha",
            "\u0445\u0430\u0445\u0430", "\u0445\u0430\u0445", "\u043C\u043C", "\u044D\u043C", "\u0443\u0433\u0443\u0443"
        };

        return tokens.All(t => filler.Contains(t));
    }

    private static bool HasSemanticSignal(string text)
    {
        var normalized = text.ToLowerInvariant();
        var tokenCount = WordTokenRegex.Matches(normalized).Count;

        if (tokenCount >= 6)
        {
            return true;
        }

        if (HasStrongDossierSignal(normalized))
        {
            return true;
        }

        return HasConcreteActionableAnchor(normalized) && tokenCount >= 3;
    }

    private static bool IsHighValueEscalationCandidate(string text, ExtractionItem extracted)
    {
        var normalized = text.ToLowerInvariant();
        var tokenCount = WordTokenRegex.Matches(normalized).Count;

        if (HasHighValueStructuredSignal(extracted))
        {
            return true;
        }

        if (HasStrongDossierSignal(normalized))
        {
            return true;
        }

        return tokenCount >= 8 && HasConcreteActionableAnchor(normalized);
    }

    private static bool HasHighValueStructuredSignal(ExtractionItem extracted)
    {
        return extracted.Relationships.Count > 0
               || extracted.Events.Count > 0
               || extracted.Facts.Any(f => IsHighValueCategory(f.Category))
               || extracted.Claims.Any(c => IsHighValueCategory(c.Category))
               || extracted.Observations.Any(o => IsHighValueObservationType(o.Type));
    }

    private static bool IsHighValueCategory(string? category)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return false;
        }

        return category.Trim().ToLowerInvariant() switch
        {
            "availability" => true,
            "schedule" => true,
            "travel" => true,
            "transportation" => true,
            "work" => true,
            "finance" => true,
            "health" => true,
            "relationship" => true,
            "location" => true,
            "contact" => true,
            "purchase" => true,
            "family" => true,
            "career" => true,
            "education" => true,
            "project" => true,
            _ => false
        };
    }

    private static bool IsHighValueObservationType(string? type)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            return false;
        }

        return type.Trim().ToLowerInvariant() switch
        {
            "availability_update" => true,
            "movement" => true,
            "travel_plan" => true,
            "schedule_update" => true,
            "work_update" => true,
            "work_status" => true,
            "work_assessment" => true,
            "health_update" => true,
            "health_report" => true,
            "location_update" => true,
            "contact_share" => true,
            "relationship_signal" => true,
            _ => false
        };
    }

    private static bool HasStrongDossierSignal(string normalized)
    {
        return ContainsAny(normalized,
            "работ", "офис", "уволи", "зарплат", "доход", "кредит", "ипотек", "долг", "лимит", "команд", "трайб", "алерт", "дефект", "тойл", "проект",
            "боль", "врач", "больниц", "беремен", "травм", "температур", "диагноз", "лекар", "антибиот", "леч",
            "встреч", "развел", "расст", "муж", "жена", "парень", "девуш", "любл",
            "купил", "продал", "заказ", "стоим", "цена", "руб", "тыс", "деньг",
            "домой", "дома", "приед", "уед", "поед", "вылет", "летим", "поезд", "такси", "забрать", "выхожу", "еду",
            "сегодня", "завтра", "послезавтра", "через ", "буду ", "свобод", "занят",
            "адрес", "улица", "ул.", "просп", "шоссе", "подъезд", "этаж", "@");
    }

    private static bool HasConcreteActionableAnchor(string normalized)
    {
        return TimeTokenRegex.IsMatch(normalized)
               || NumberTokenRegex.IsMatch(normalized)
               || ContainsAny(normalized,
                   "в ", "на ", "к ", "до ", "после ", "через ",
                   "сегодня", "завтра", "послезавтра", "утром", "днем", "днём", "вечером",
                   "минут", "час", "домой", "дом", "офис", "работ", "встреч", "звон", "позвон", "куп", "прод", "верн",
                   "адрес", "улица", "ул.", "просп", "подъезд", "этаж", "забрать", "такси", "@", "maps");
    }

    private static bool IsLowValueAmbiguityReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return false;
        }

        var normalized = reason.Trim().ToLowerInvariant();
        return normalized.Contains("ambiguous", StringComparison.Ordinal)
               || normalized.Contains("incomplete", StringComparison.Ordinal)
               || normalized.Contains("too little context", StringComparison.Ordinal)
               || normalized.Contains("missing context", StringComparison.Ordinal)
               || normalized.Contains("unclear", StringComparison.Ordinal);
    }

    private static bool ContainsAny(string text, params string[] patterns)
    {
        foreach (var pattern in patterns)
        {
            if (text.Contains(pattern, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
