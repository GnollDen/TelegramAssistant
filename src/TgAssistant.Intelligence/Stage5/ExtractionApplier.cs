using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Intelligence.Stage5;

public class ExtractionApplier
{
    private readonly AnalysisSettings _settings;
    private readonly IDbContextFactory<TgAssistantDbContext> _dbFactory;
    private readonly IEntityRepository _entityRepository;
    private readonly IEntityAliasRepository _entityAliasRepository;
    private readonly IFactRepository _factRepository;
    private readonly ICommunicationEventRepository _communicationEventRepository;
    private readonly IRelationshipRepository _relationshipRepository;
    private readonly IIntelligenceRepository _intelligenceRepository;

    public ExtractionApplier(
        IOptions<AnalysisSettings> settings,
        IDbContextFactory<TgAssistantDbContext> dbFactory,
        IEntityRepository entityRepository,
        IEntityAliasRepository entityAliasRepository,
        IFactRepository factRepository,
        ICommunicationEventRepository communicationEventRepository,
        IRelationshipRepository relationshipRepository,
        IIntelligenceRepository intelligenceRepository)
    {
        _settings = settings.Value;
        _dbFactory = dbFactory;
        _entityRepository = entityRepository;
        _entityAliasRepository = entityAliasRepository;
        _factRepository = factRepository;
        _communicationEventRepository = communicationEventRepository;
        _relationshipRepository = relationshipRepository;
        _intelligenceRepository = intelligenceRepository;
    }

    /// <summary>
    /// Applies extracted intelligence for one message atomically.
    /// </summary>
    public async Task ApplyExtractionAsync(long messageId, ExtractionItem extraction, Message? sourceMessage, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Transactional boundary: all entity/fact/relationship/intelligence writes for one message commit atomically.
        using var dbScope = AmbientDbContextScope.Enter(db);

        var entityByName = new Dictionary<string, Entity>(StringComparer.OrdinalIgnoreCase);
        var currentFactsByEntityId = new Dictionary<Guid, List<Fact>>();
        var eventBuffer = new List<CommunicationEvent>();
        var appliedFacts = new HashSet<ProjectedFactKey>();
        var senderName = sourceMessage?.SenderName?.Trim();
        var explicitRelationshipEntityTypes = BuildExplicitRelationshipEntityTypeMap(extraction);
        var existingRelationshipEntityByName = new Dictionary<string, Entity?>(StringComparer.OrdinalIgnoreCase);

        await PrewarmEntitiesAsync(extraction, entityByName, existingRelationshipEntityByName, ct);

        foreach (var entity in extraction.Entities.Where(e => !e.NeedsClarification && !string.IsNullOrWhiteSpace(e.Name)))
        {
            if (IsGenericEntityToken(entity.Name))
            {
                continue;
            }

            await GetOrCreateCachedEntityAsync(
                entityByName,
                entity.Name.Trim(),
                ParseEntityType(entity.Type),
                sourceMessage,
                senderName,
                entity.TrustFactor > 0f ? entity.TrustFactor : entity.Confidence,
                ct);
        }

        foreach (var fact in extraction.Facts.Where(f => !f.NeedsClarification && !string.IsNullOrWhiteSpace(f.EntityName) && !string.IsNullOrWhiteSpace(f.Key)))
        {
            await ApplyFactCandidateAsync(
                messageId,
                fact.EntityName,
                fact.Category,
                fact.Key,
                fact.Value,
                fact.Confidence,
                fact.TrustFactor,
                sourceMessage,
                senderName,
                entityByName,
                currentFactsByEntityId,
                eventBuffer,
                appliedFacts,
                skipIfAlreadyApplied: false,
                ct);
        }

        foreach (var rel in extraction.Relationships.Where(r => !string.IsNullOrWhiteSpace(r.FromEntityName) && !string.IsNullOrWhiteSpace(r.ToEntityName) && !string.IsNullOrWhiteSpace(r.Type)))
        {
            if (IsGenericEntityToken(rel.FromEntityName) || IsGenericEntityToken(rel.ToEntityName))
            {
                continue;
            }

            if (IsGenericRelationshipNoun(rel.ToEntityName))
            {
                continue;
            }

            if (rel.Confidence < _settings.MinRelationshipConfidence)
            {
                continue;
            }

            var from = await ResolveRelationshipEntityAsync(
                rel.FromEntityName,
                explicitRelationshipEntityTypes,
                existingRelationshipEntityByName,
                entityByName,
                sourceMessage,
                senderName,
                ct);
            var to = await ResolveRelationshipEntityAsync(
                rel.ToEntityName,
                explicitRelationshipEntityTypes,
                existingRelationshipEntityByName,
                entityByName,
                sourceMessage,
                senderName,
                ct);
            if (from == null || to == null)
            {
                continue;
            }

            await _relationshipRepository.UpsertAsync(new Relationship
            {
                FromEntityId = from.Id,
                ToEntityId = to.Id,
                Type = ExtractionSemanticContract.CanonicalizeRelationshipType(rel.Type),
                Status = ConfidenceStatus.Inferred,
                Confidence = rel.Confidence,
                SourceMessageId = messageId
            }, ct);
        }

        foreach (var evt in extraction.Events.Where(e => !string.IsNullOrWhiteSpace(e.Type) && !string.IsNullOrWhiteSpace(e.SubjectName)))
        {
            var subjectName = evt.SubjectName.Trim();
            if (IsGenericEntityToken(subjectName))
            {
                continue;
            }

            var subjectEntity = await GetOrCreateCachedEntityAsync(
                entityByName,
                subjectName,
                EntityType.Person,
                sourceMessage,
                senderName,
                null,
                ct);

            eventBuffer.Add(new CommunicationEvent
            {
                MessageId = messageId,
                EntityId = subjectEntity.Id,
                EventType = ExtractionSemanticContract.CanonicalizeEventType(evt.Type),
                ObjectName = string.IsNullOrWhiteSpace(evt.ObjectName) ? null : evt.ObjectName.Trim(),
                Sentiment = string.IsNullOrWhiteSpace(evt.Sentiment) ? null : evt.Sentiment.Trim().ToLowerInvariant(),
                Summary = string.IsNullOrWhiteSpace(evt.Summary) ? null : evt.Summary.Trim(),
                Confidence = evt.Confidence
            });
        }

        await PersistIntelligenceAsync(messageId, extraction, sourceMessage, senderName, entityByName, ct);
        await ProjectObservationsToRelationshipsAsync(
            messageId,
            extraction,
            sourceMessage,
            senderName,
            entityByName,
            explicitRelationshipEntityTypes,
            existingRelationshipEntityByName,
            ct);
        await ProjectClaimsToFactsAsync(
            messageId,
            sourceMessage,
            senderName,
            entityByName,
            currentFactsByEntityId,
            eventBuffer,
            appliedFacts,
            ct);

        if (eventBuffer.Count > 0)
        {
            await _communicationEventRepository.AddRangeAsync(eventBuffer, ct);
        }

        await tx.CommitAsync(ct);
    }

    /// <summary>
    /// Replaces only intelligence rows (observations/claims) for one message atomically.
    /// Intended for low-cost refinement passes that should not re-project facts/events.
    /// </summary>
    public async Task ApplyIntelligenceOnlyAsync(long messageId, ExtractionItem extraction, Message? sourceMessage, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        using var dbScope = AmbientDbContextScope.Enter(db);

        var senderName = sourceMessage?.SenderName?.Trim();
        var entityByName = new Dictionary<string, Entity>(StringComparer.OrdinalIgnoreCase);
        var existingRelationshipEntityByName = new Dictionary<string, Entity?>(StringComparer.OrdinalIgnoreCase);
        await PrewarmEntitiesAsync(extraction, entityByName, existingRelationshipEntityByName, ct);
        await PersistIntelligenceAsync(messageId, extraction, sourceMessage, senderName, entityByName, ct);

        await tx.CommitAsync(ct);
    }

    private async Task PersistIntelligenceAsync(
        long messageId,
        ExtractionItem extraction,
        Message? sourceMessage,
        string? senderName,
        Dictionary<string, Entity> entityByName,
        CancellationToken ct)
    {
        var observations = SelectIntelligenceObservations(extraction);
        var claims = SelectIntelligenceClaims(extraction);

        var observationRows = new List<IntelligenceObservation>(observations.Count);
        foreach (var observation in observations)
        {
            var subjectName = observation.SubjectName.Trim();
            if (subjectName.Length == 0 || IsGenericEntityToken(subjectName))
            {
                continue;
            }

            var subjectEntity = await GetOrCreateCachedEntityAsync(
                entityByName,
                subjectName,
                EntityType.Person,
                sourceMessage,
                senderName,
                null,
                ct);
            observationRows.Add(new IntelligenceObservation
            {
                MessageId = messageId,
                EntityId = subjectEntity?.Id,
                SubjectName = subjectName,
                ObservationType = ExtractionSemanticContract.CanonicalizeObservationType(observation.Type),
                ObjectName = string.IsNullOrWhiteSpace(observation.ObjectName) ? null : observation.ObjectName.Trim(),
                Value = string.IsNullOrWhiteSpace(observation.Value) ? null : observation.Value.Trim(),
                Evidence = string.IsNullOrWhiteSpace(observation.Evidence) ? null : observation.Evidence.Trim(),
                Confidence = observation.Confidence,
                CreatedAt = DateTime.UtcNow
            });
        }

        var claimRows = new List<IntelligenceClaim>(claims.Count);
        foreach (var claim in claims)
        {
            var entityName = claim.EntityName.Trim();
            if (entityName.Length == 0 || IsGenericEntityToken(entityName))
            {
                continue;
            }

            var entity = await GetOrCreateCachedEntityAsync(
                entityByName,
                entityName,
                EntityType.Person,
                sourceMessage,
                senderName,
                null,
                ct);
            var normalizedCategory = ExtractionSemanticContract.CanonicalizeCategory(claim.Category);
            claimRows.Add(new IntelligenceClaim
            {
                MessageId = messageId,
                EntityId = entity?.Id,
                EntityName = entityName,
                ClaimType = string.IsNullOrWhiteSpace(claim.ClaimType)
                    ? "fact"
                    : ExtractionSemanticContract.CanonicalizeClaimType(claim.ClaimType),
                Category = normalizedCategory,
                Key = ExtractionSemanticContract.CanonicalizeKey(normalizedCategory, claim.Key),
                Value = claim.Value.Trim(),
                Evidence = string.IsNullOrWhiteSpace(claim.Evidence) ? null : claim.Evidence.Trim(),
                Status = ResolveFactStatus(normalizedCategory, claim.Confidence),
                Confidence = claim.Confidence,
                CreatedAt = DateTime.UtcNow
            });
        }

        await _intelligenceRepository.ReplaceMessageIntelligenceAsync(messageId, observationRows, claimRows, ct);
    }

    private async Task<Entity> GetOrCreateCachedEntityAsync(
        Dictionary<string, Entity> entityByName,
        string entityName,
        EntityType fallbackType,
        Message? sourceMessage,
        string? senderName,
        float? trustFactor,
        CancellationToken ct)
    {
        var normalized = entityName.Trim();
        var isSender = IsSenderEntityName(normalized, sourceMessage, senderName);
        if (entityByName.TryGetValue(normalized, out var cached)
            && (!isSender || !string.IsNullOrWhiteSpace(cached.ActorKey)))
        {
            return cached;
        }

        var entity = await UpsertEntityWithActorContextAsync(normalized, fallbackType, sourceMessage, senderName, trustFactor, ct);
        entityByName[normalized] = entity;
        entityByName[entity.Name] = entity;
        return entity;
    }

    private static bool IsSenderEntityName(string observedName, Message? sourceMessage, string? senderName)
    {
        return sourceMessage is not null
            && !string.IsNullOrWhiteSpace(senderName)
            && string.Equals(observedName, senderName.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private async Task PrewarmEntitiesAsync(
        ExtractionItem extraction,
        Dictionary<string, Entity> entityByName,
        Dictionary<string, Entity?> existingRelationshipEntityByName,
        CancellationToken ct)
    {
        var names = CollectReferencedEntityNames(extraction);
        if (names.Count == 0)
        {
            return;
        }

        var existingEntitiesByRequestedName = await _entityRepository.FindByNamesOrAliasesAsync(names, ct);
        foreach (var pair in existingEntitiesByRequestedName)
        {
            var requestedName = pair.Key;
            var entity = pair.Value;

            entityByName[requestedName] = entity;
            entityByName[entity.Name] = entity;
            existingRelationshipEntityByName[requestedName] = entity;
            existingRelationshipEntityByName[entity.Name] = entity;
        }
    }

    private async Task<List<Fact>> GetCurrentFactsCachedAsync(
        Dictionary<Guid, List<Fact>> currentFactsByEntityId,
        Guid entityId,
        CancellationToken ct)
    {
        if (currentFactsByEntityId.TryGetValue(entityId, out var cached))
        {
            return cached;
        }

        var facts = await _factRepository.GetCurrentByEntityAsync(entityId, ct);
        currentFactsByEntityId[entityId] = facts;
        return facts;
    }

    private async Task ProjectClaimsToFactsAsync(
        long messageId,
        Message? sourceMessage,
        string? senderName,
        Dictionary<string, Entity> entityByName,
        Dictionary<Guid, List<Fact>> currentFactsByEntityId,
        List<CommunicationEvent> eventBuffer,
        HashSet<ProjectedFactKey> appliedFacts,
        CancellationToken ct)
    {
        var claims = await _intelligenceRepository.GetClaimsByMessageAsync(messageId, ct);
        foreach (var claim in claims)
        {
            if (!IsProjectableFactClaimType(claim.ClaimType))
            {
                continue;
            }

            await ApplyFactCandidateAsync(
                messageId,
                claim.EntityName,
                claim.Category,
                claim.Key,
                claim.Value,
                claim.Confidence,
                null,
                sourceMessage,
                senderName,
                entityByName,
                currentFactsByEntityId,
                eventBuffer,
                appliedFacts,
                skipIfAlreadyApplied: true,
                ct);
        }
    }

    private async Task ProjectObservationsToRelationshipsAsync(
        long messageId,
        ExtractionItem extraction,
        Message? sourceMessage,
        string? senderName,
        Dictionary<string, Entity> entityByName,
        IReadOnlyDictionary<string, EntityType> explicitRelationshipEntityTypes,
        Dictionary<string, Entity?> existingRelationshipEntityByName,
        CancellationToken ct)
    {
        var observations = SelectIntelligenceObservations(extraction);

        foreach (var observation in observations)
        {
            var subjectName = observation.SubjectName.Trim();
            var objectName = observation.ObjectName?.Trim();
            if (subjectName.Length == 0
                || string.IsNullOrWhiteSpace(objectName)
                || IsGenericEntityToken(subjectName)
                || IsGenericEntityToken(objectName)
                || IsGenericRelationshipNoun(objectName)
                || string.Equals(subjectName, objectName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var fromEntity = await ResolveRelationshipEntityAsync(
                subjectName,
                explicitRelationshipEntityTypes,
                existingRelationshipEntityByName,
                entityByName,
                sourceMessage,
                senderName,
                ct);
            var toEntity = await ResolveRelationshipEntityAsync(
                objectName,
                explicitRelationshipEntityTypes,
                existingRelationshipEntityByName,
                entityByName,
                sourceMessage,
                senderName,
                ct);
            if (fromEntity == null || toEntity == null)
            {
                continue;
            }

            if (fromEntity.Id == toEntity.Id)
            {
                continue;
            }

            await _relationshipRepository.UpsertAsync(new Relationship
            {
                FromEntityId = fromEntity.Id,
                ToEntityId = toEntity.Id,
                Type = ResolveObservationRelationshipType(observation.Type, toEntity.Type == EntityType.Person),
                Status = ConfidenceStatus.Inferred,
                Confidence = observation.Confidence,
                SourceMessageId = messageId
            }, ct);
        }

        var persistedClaims = await _intelligenceRepository.GetClaimsByMessageAsync(messageId, ct);
        foreach (var claim in persistedClaims)
        {
            if (!string.Equals(claim.ClaimType?.Trim(), "relationship", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var fromEntityName = claim.EntityName.Trim();
            var toEntityName = claim.Value.Trim();
            var relationshipType = ExtractionSemanticContract.CanonicalizeRelationshipType(claim.Key);
            if (fromEntityName.Length == 0
                || toEntityName.Length == 0
                || relationshipType.Length == 0
                || IsGenericEntityToken(fromEntityName)
                || IsGenericEntityToken(toEntityName)
                || IsGenericRelationshipNoun(toEntityName)
                || string.Equals(fromEntityName, toEntityName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var fromEntity = await ResolveRelationshipEntityAsync(
                fromEntityName,
                explicitRelationshipEntityTypes,
                existingRelationshipEntityByName,
                entityByName,
                sourceMessage,
                senderName,
                ct);
            var toEntity = await ResolveRelationshipEntityAsync(
                toEntityName,
                explicitRelationshipEntityTypes,
                existingRelationshipEntityByName,
                entityByName,
                sourceMessage,
                senderName,
                ct);
            if (fromEntity == null || toEntity == null)
            {
                continue;
            }

            if (fromEntity.Id == toEntity.Id)
            {
                continue;
            }

            await _relationshipRepository.UpsertAsync(new Relationship
            {
                FromEntityId = fromEntity.Id,
                ToEntityId = toEntity.Id,
                Type = relationshipType,
                Status = ConfidenceStatus.Inferred,
                Confidence = claim.Confidence,
                SourceMessageId = messageId
            }, ct);
        }
    }

    private async Task ApplyFactCandidateAsync(
        long messageId,
        string entityName,
        string? category,
        string key,
        string? value,
        float confidence,
        float? trustFactor,
        Message? sourceMessage,
        string? senderName,
        Dictionary<string, Entity> entityByName,
        Dictionary<Guid, List<Fact>> currentFactsByEntityId,
        List<CommunicationEvent> eventBuffer,
        HashSet<ProjectedFactKey> appliedFacts,
        bool skipIfAlreadyApplied,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(entityName) || string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        var normalizedEntityName = entityName.Trim();
        if (IsGenericEntityToken(normalizedEntityName))
        {
            return;
        }

        var normalizedCategory = ExtractionSemanticContract.CanonicalizeCategory(category);
        var normalizedKey = ExtractionSemanticContract.CanonicalizeKey(normalizedCategory, key);
        var normalizedValue = (value ?? string.Empty).Trim();
        var factThreshold = IsSensitiveCategory(normalizedCategory)
            ? _settings.MinSensitiveFactConfidence
            : _settings.MinFactConfidence;
        if (confidence < factThreshold)
        {
            return;
        }

        var entity = await GetOrCreateCachedEntityAsync(
            entityByName,
            normalizedEntityName,
            EntityType.Person,
            sourceMessage,
            senderName,
            null,
            ct);

        var projectedFactKey = ToProjectedFactKey(entity.Id, normalizedCategory, normalizedKey, normalizedValue);
        if (skipIfAlreadyApplied && appliedFacts.Contains(projectedFactKey))
        {
            return;
        }

        var current = await GetCurrentFactsCachedAsync(currentFactsByEntityId, entity.Id, ct);
        var sameKey = current.FirstOrDefault(x => x.Category.Equals(normalizedCategory, StringComparison.OrdinalIgnoreCase)
                                                  && x.Key.Equals(normalizedKey, StringComparison.OrdinalIgnoreCase));
        var strategy = GetFactConflictStrategy(normalizedCategory, sameKey);
        var status = ResolveFactStatus(normalizedCategory, confidence);
        var newFact = new Fact
        {
            EntityId = entity.Id,
            Category = normalizedCategory,
            Key = normalizedKey,
            Value = normalizedValue,
            Status = status,
            Confidence = confidence,
            TrustFactor = ResolveTrustFactor(trustFactor, confidence),
            SourceMessageId = messageId,
            ValidFrom = DateTime.UtcNow,
            IsCurrent = true,
            DecayClass = ResolveFactDecayClass(normalizedCategory, normalizedKey)
        };

        if (sameKey != null && !string.Equals(sameKey.Value, newFact.Value, StringComparison.OrdinalIgnoreCase))
        {
            eventBuffer.Add(new CommunicationEvent
            {
                MessageId = messageId,
                EntityId = entity.Id,
                EventType = "fact_contradiction",
                ObjectName = $"{normalizedCategory}:{normalizedKey}",
                Sentiment = null,
                Summary = $"value changed from '{sameKey.Value}' to '{newFact.Value}'",
                Confidence = Math.Max(0.6f, confidence)
            });

            switch (strategy)
            {
                case FactConflictStrategy.Supersede:
                    await _factRepository.SupersedeFactAsync(sameKey.Id, newFact, ct);
                    currentFactsByEntityId.Remove(entity.Id);
                    break;
                case FactConflictStrategy.Parallel:
                case FactConflictStrategy.Tentative:
                    await _factRepository.UpsertAsync(newFact, ct);
                    currentFactsByEntityId.Remove(entity.Id);
                    break;
            }
        }
        else
        {
            await _factRepository.UpsertAsync(newFact, ct);
            currentFactsByEntityId.Remove(entity.Id);
        }

        appliedFacts.Add(projectedFactKey);
    }

    private static List<ExtractionObservation> SelectIntelligenceObservations(ExtractionItem extraction)
    {
        if (extraction.Observations.Count > 0)
        {
            return extraction.Observations;
        }

        return extraction.Events
            .Where(e => !string.IsNullOrWhiteSpace(e.Type) && !string.IsNullOrWhiteSpace(e.SubjectName))
            .Select(e => new ExtractionObservation
            {
                SubjectName = e.SubjectName,
                Type = e.Type,
                ObjectName = e.ObjectName,
                Value = e.Summary,
                Evidence = e.Summary,
                Confidence = e.Confidence
            })
            .ToList();
    }

    private static List<ExtractionClaim> SelectIntelligenceClaims(ExtractionItem extraction)
    {
        if (extraction.Claims.Count > 0)
        {
            return extraction.Claims;
        }

        var claims = new List<ExtractionClaim>();
        claims.AddRange(extraction.Facts.Select(f => new ExtractionClaim
        {
            EntityName = f.EntityName,
            ClaimType = "fact",
            Category = f.Category,
            Key = f.Key,
            Value = f.Value,
            Evidence = f.Value,
            Confidence = f.Confidence
        }));
        claims.AddRange(extraction.Relationships.Select(r => new ExtractionClaim
        {
            EntityName = r.FromEntityName,
            ClaimType = "relationship",
            Category = "relationship",
            Key = r.Type,
            Value = r.ToEntityName,
            Evidence = $"{r.FromEntityName} -> {r.Type} -> {r.ToEntityName}",
            Confidence = r.Confidence
        }));
        claims.AddRange(extraction.ProfileSignals.Select(s => new ExtractionClaim
        {
            EntityName = s.SubjectName,
            ClaimType = "profile_signal",
            Category = "profile",
            Key = s.Trait,
            Value = s.Direction,
            Evidence = s.Evidence,
            Confidence = s.Confidence
        }));
        return claims;
    }

    private static EntityType ParseEntityType(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "organization" => EntityType.Organization,
            "place" => EntityType.Place,
            "pet" => EntityType.Pet,
            "event" => EntityType.Event,
            _ => EntityType.Person
        };
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

    private static bool IsSensitiveCategory(string? category)
    {
        return category?.Trim().ToLowerInvariant() switch
        {
            "health" => true,
            "finance" => true,
            "money" => true,
            "relationship" => true,
            "legal" => true,
            _ => false
        };
    }

    private static bool IsProjectableFactClaimType(string? claimType)
    {
        return claimType?.Trim().ToLowerInvariant() switch
        {
            "fact" => true,
            "state" => true,
            "preference" => true,
            _ => false
        };
    }

    private static string ResolveObservationRelationshipType(string? observationType, bool objectIsPerson)
    {
        return observationType?.Trim().ToLowerInvariant() switch
        {
            "request" => "communicates_with",
            "intent" => "communicates_with",
            "question" => "communicates_with",
            "health_update" when objectIsPerson => "cares_for",
            "contact_share" => "knows",
            "relationship_signal" => "related_to",
            _ => "interacts_with"
        };
    }

    private static string ResolveFactDecayClass(string? category, string? key)
    {
        var normalizedCategory = string.IsNullOrWhiteSpace(category) ? string.Empty : category.Trim().ToLowerInvariant();
        var normalizedKey = string.IsNullOrWhiteSpace(key) ? string.Empty : key.Trim().ToLowerInvariant();

        if (ContainsAnyToken(normalizedCategory, "availability", "schedule"))
        {
            return "instant";
        }

        if (ContainsAnyToken(normalizedCategory, "birth", "blood", "allergy", "chronic", "education", "children", "name", "identity")
            || ContainsAnyToken(normalizedKey, "birth_date", "blood_type", "allergy", "degree", "child"))
        {
            return "permanent";
        }

        if (ContainsAnyToken(normalizedKey, "free_time", "eta", "current_location", "on_my_way"))
        {
            return "instant";
        }

        if (ContainsAnyToken(normalizedCategory, "health", "project", "purchase", "activity", "mood", "communication")
            || ContainsAnyToken(normalizedKey, "illness", "medication", "treatment", "symptom", "plan", "goal"))
        {
            return "fast";
        }

        if (ContainsAnyToken(normalizedCategory, "work", "career", "location", "family", "relationship", "education", "housing"))
        {
            return "slow";
        }

        return "slow";
    }

    private static Dictionary<string, EntityType> BuildExplicitRelationshipEntityTypeMap(ExtractionItem extraction)
    {
        var map = new Dictionary<string, EntityType>(StringComparer.OrdinalIgnoreCase);
        foreach (var extractionEntity in extraction.Entities)
        {
            var entityName = extractionEntity.Name?.Trim();
            if (string.IsNullOrWhiteSpace(entityName) || IsGenericEntityToken(entityName))
            {
                continue;
            }

            var entityType = ParseEntityType(extractionEntity.Type);
            if (!IsRelationshipEntityTypeAllowed(entityType))
            {
                continue;
            }

            map.TryAdd(entityName, entityType);
        }

        return map;
    }

    private static HashSet<string> CollectReferencedEntityNames(ExtractionItem extraction)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        static void AddName(HashSet<string> buffer, string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            var normalized = value.Trim();
            if (normalized.Length == 0 || IsGenericEntityToken(normalized))
            {
                return;
            }

            buffer.Add(normalized);
        }

        foreach (var entity in extraction.Entities)
        {
            AddName(names, entity.Name);
        }

        foreach (var fact in extraction.Facts)
        {
            AddName(names, fact.EntityName);
        }

        foreach (var claim in SelectIntelligenceClaims(extraction))
        {
            AddName(names, claim.EntityName);
            if (string.Equals(claim.ClaimType?.Trim(), "relationship", StringComparison.OrdinalIgnoreCase))
            {
                AddName(names, claim.Value);
            }
        }

        foreach (var observation in SelectIntelligenceObservations(extraction))
        {
            AddName(names, observation.SubjectName);
            AddName(names, observation.ObjectName);
        }

        foreach (var relation in extraction.Relationships)
        {
            AddName(names, relation.FromEntityName);
            AddName(names, relation.ToEntityName);
        }

        foreach (var evt in extraction.Events)
        {
            AddName(names, evt.SubjectName);
            AddName(names, evt.ObjectName);
        }

        foreach (var profileSignal in extraction.ProfileSignals)
        {
            AddName(names, profileSignal.SubjectName);
        }

        return names;
    }

    private async Task<Entity?> ResolveRelationshipEntityAsync(
        string entityName,
        IReadOnlyDictionary<string, EntityType> explicitRelationshipEntityTypes,
        Dictionary<string, Entity?> existingRelationshipEntityByName,
        Dictionary<string, Entity> entityByName,
        Message? sourceMessage,
        string? senderName,
        CancellationToken ct)
    {
        var normalizedName = entityName.Trim();
        if (normalizedName.Length == 0 || IsGenericEntityToken(normalizedName))
        {
            return null;
        }

        var existingEntity = await FindExistingRelationshipEntityAsync(normalizedName, existingRelationshipEntityByName, ct);
        if (existingEntity != null)
        {
            if (!IsRelationshipEntityTypeAllowed(existingEntity.Type))
            {
                return null;
            }

            entityByName[normalizedName] = existingEntity;
            entityByName[existingEntity.Name] = existingEntity;
            return existingEntity;
        }

        if (!explicitRelationshipEntityTypes.TryGetValue(normalizedName, out var fallbackType))
        {
            return null;
        }

        if (!IsRelationshipEntityTypeAllowed(fallbackType))
        {
            return null;
        }

        if (entityByName.TryGetValue(normalizedName, out var cachedEntity) && IsRelationshipEntityTypeAllowed(cachedEntity.Type))
        {
            return cachedEntity;
        }

        var createdEntity = await GetOrCreateCachedEntityAsync(
            entityByName,
            normalizedName,
            fallbackType,
            sourceMessage,
            senderName,
            null,
            ct);
        return IsRelationshipEntityTypeAllowed(createdEntity.Type) ? createdEntity : null;
    }

    private async Task<Entity?> FindExistingRelationshipEntityAsync(
        string entityName,
        Dictionary<string, Entity?> existingRelationshipEntityByName,
        CancellationToken ct)
    {
        var normalizedName = entityName.Trim();
        if (existingRelationshipEntityByName.TryGetValue(normalizedName, out var cached))
        {
            return cached;
        }

        var existing = await _entityRepository.FindByNameOrAliasAsync(normalizedName, ct);
        existingRelationshipEntityByName[normalizedName] = existing;
        if (existing != null && !existingRelationshipEntityByName.ContainsKey(existing.Name))
        {
            existingRelationshipEntityByName[existing.Name] = existing;
        }

        return existing;
    }

    private static bool IsRelationshipEntityTypeAllowed(EntityType entityType)
    {
        return entityType is EntityType.Person or EntityType.Organization or EntityType.Pet;
    }

    private static bool IsGenericRelationshipNoun(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var normalized = value.Trim().ToLowerInvariant();
        return normalized switch
        {
            "antibiotic" => true,
            "antibiotics" => true,
            "team" => true,
            "house" => true,
            "беклог" => true,
            _ => false
        };
    }

    private static bool ContainsAnyToken(string source, params string[] tokens)
    {
        foreach (var token in tokens)
        {
            if (source.Contains(token, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private FactConflictStrategy GetFactConflictStrategy(string? category, Fact? sameKey)
    {
        return category?.Trim().ToLowerInvariant() switch
        {
            // For sensitive domains keep conflicting versions in parallel for manual review.
            "health" => FactConflictStrategy.Parallel,
            "finance" => FactConflictStrategy.Parallel,
            "money" => FactConflictStrategy.Parallel,
            "relationship" => FactConflictStrategy.Parallel,
            "legal" => FactConflictStrategy.Parallel,
            // Availability/schedule updates are temporal: supersede stale values, keep recent versions in parallel.
            "availability" => sameKey != null
                              && (DateTime.UtcNow - sameKey.UpdatedAt).TotalHours > _settings.TemporalFactSupersedeTtlHours
                ? FactConflictStrategy.Supersede
                : FactConflictStrategy.Parallel,
            "schedule" => sameKey != null
                          && (DateTime.UtcNow - sameKey.UpdatedAt).TotalHours > _settings.TemporalFactSupersedeTtlHours
                ? FactConflictStrategy.Supersede
                : FactConflictStrategy.Parallel,
            // Career and other stable profile fields are safe to supersede on direct conflict.
            "career" => FactConflictStrategy.Supersede,
            _ => FactConflictStrategy.Supersede
        };
    }

    private enum FactConflictStrategy
    {
        Supersede = 0,
        Parallel = 1,
        Tentative = 2
    }

    private ConfidenceStatus ResolveFactStatus(string category, float confidence)
    {
        if (IsSensitiveCategory(category) || confidence < _settings.CheapConfidenceThreshold)
        {
            return ConfidenceStatus.Tentative;
        }

        if (confidence >= _settings.AutoConfirmFactConfidence)
        {
            return ConfidenceStatus.Confirmed;
        }

        return ConfidenceStatus.Inferred;
    }

    private async Task<Entity> UpsertEntityWithActorContextAsync(
        string name,
        EntityType fallbackType,
        Message? sourceMessage,
        string? senderName,
        float? trustFactor,
        CancellationToken ct)
    {
        var observedName = name.Trim();
        var isSender = IsSenderEntityName(observedName, sourceMessage, senderName);

        if (isSender)
        {
            var senderEntityPayload = new Entity
            {
                Name = senderName!,
                Type = EntityType.Person,
                ActorKey = BuildActorKey(sourceMessage!.ChatId, sourceMessage.SenderId),
                TelegramUserId = sourceMessage.SenderId > 0 ? sourceMessage.SenderId : null
            };
            if (trustFactor.HasValue)
            {
                senderEntityPayload.TrustFactor = ResolveTrustFactor(trustFactor, null);
            }

            var senderEntity = await _entityRepository.UpsertAsync(senderEntityPayload, ct);

            await ObserveAliasAsync(senderEntity, observedName, sourceMessage.Id, ct);
            return senderEntity;
        }

        var entityPayload = new Entity
        {
            Name = observedName,
            Type = fallbackType
        };
        if (trustFactor.HasValue)
        {
            entityPayload.TrustFactor = ResolveTrustFactor(trustFactor, null);
        }

        var entity = await _entityRepository.UpsertAsync(entityPayload, ct);

        var sourceMessageId = sourceMessage?.Id;
        await ObserveAliasAsync(entity, observedName, sourceMessageId, ct);
        return entity;
    }

    private static string BuildActorKey(long chatId, long senderId) => $"{chatId}:{senderId}";

    private async Task ObserveAliasAsync(Entity entity, string observedName, long? sourceMessageId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(observedName))
        {
            return;
        }

        if (string.Equals(entity.Name, observedName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await _entityAliasRepository.UpsertAliasAsync(entity.Id, observedName, sourceMessageId, 0.9f, ct);
    }

    private static ProjectedFactKey ToProjectedFactKey(Guid entityId, string category, string key, string value)
    {
        return new ProjectedFactKey(
            entityId,
            category.Trim().ToLowerInvariant(),
            key.Trim().ToLowerInvariant(),
            value.Trim().ToLowerInvariant());
    }

    private static float ResolveTrustFactor(float? trustFactor, float? confidence)
    {
        if (trustFactor.HasValue && trustFactor.Value > 0f)
        {
            return Math.Clamp(trustFactor.Value, 0f, 1f);
        }

        if (confidence.HasValue && confidence.Value >= 0f)
        {
            return Math.Clamp(confidence.Value, 0f, 1f);
        }

        return 0f;
    }

    private readonly record struct ProjectedFactKey(Guid EntityId, string Category, string Key, string Value);
}
