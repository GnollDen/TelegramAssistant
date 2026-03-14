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
    private readonly IFactReviewCommandRepository _factReviewCommandRepository;
    private readonly IIntelligenceRepository _intelligenceRepository;

    public ExtractionApplier(
        IOptions<AnalysisSettings> settings,
        IDbContextFactory<TgAssistantDbContext> dbFactory,
        IEntityRepository entityRepository,
        IEntityAliasRepository entityAliasRepository,
        IFactRepository factRepository,
        ICommunicationEventRepository communicationEventRepository,
        IRelationshipRepository relationshipRepository,
        IFactReviewCommandRepository factReviewCommandRepository,
        IIntelligenceRepository intelligenceRepository)
    {
        _settings = settings.Value;
        _dbFactory = dbFactory;
        _entityRepository = entityRepository;
        _entityAliasRepository = entityAliasRepository;
        _factRepository = factRepository;
        _communicationEventRepository = communicationEventRepository;
        _relationshipRepository = relationshipRepository;
        _factReviewCommandRepository = factReviewCommandRepository;
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
        var senderName = sourceMessage?.SenderName?.Trim();

        foreach (var entity in extraction.Entities.Where(e => !string.IsNullOrWhiteSpace(e.Name)))
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
                ct);
        }

        foreach (var fact in extraction.Facts.Where(f => !string.IsNullOrWhiteSpace(f.EntityName) && !string.IsNullOrWhiteSpace(f.Key)))
        {
            if (IsGenericEntityToken(fact.EntityName))
            {
                continue;
            }

            var normalizedCategory = string.IsNullOrWhiteSpace(fact.Category) ? "general" : fact.Category.Trim();
            var factThreshold = IsSensitiveCategory(normalizedCategory)
                ? _settings.MinSensitiveFactConfidence
                : _settings.MinFactConfidence;
            if (fact.Confidence < factThreshold)
            {
                continue;
            }

            var entity = await GetOrCreateCachedEntityAsync(
                entityByName,
                fact.EntityName.Trim(),
                EntityType.Person,
                sourceMessage,
                senderName,
                ct);

            var current = await GetCurrentFactsCachedAsync(currentFactsByEntityId, entity.Id, ct);
            var sameKey = current.FirstOrDefault(x => x.Category.Equals(normalizedCategory, StringComparison.OrdinalIgnoreCase)
                                                 && x.Key.Equals(fact.Key.Trim(), StringComparison.OrdinalIgnoreCase));
            var strategy = GetFactConflictStrategy(normalizedCategory, sameKey);
            var status = ResolveFactStatus(normalizedCategory, fact.Confidence);

            var newFact = new Fact
            {
                EntityId = entity.Id,
                Category = normalizedCategory,
                Key = fact.Key.Trim(),
                Value = (fact.Value ?? string.Empty).Trim(),
                Status = status,
                Confidence = fact.Confidence,
                SourceMessageId = messageId,
                ValidFrom = DateTime.UtcNow,
                IsCurrent = true
            };

            if (sameKey != null && !string.Equals(sameKey.Value, newFact.Value, StringComparison.OrdinalIgnoreCase))
            {
                eventBuffer.Add(new CommunicationEvent
                {
                    MessageId = messageId,
                    EntityId = entity.Id,
                    EventType = "fact_contradiction",
                    ObjectName = $"{normalizedCategory}:{fact.Key.Trim()}",
                    Sentiment = null,
                    Summary = $"value changed from '{sameKey.Value}' to '{newFact.Value}'",
                    Confidence = Math.Max(0.6f, fact.Confidence)
                });

                switch (strategy)
                {
                    case FactConflictStrategy.Supersede:
                        await _factRepository.SupersedeFactAsync(sameKey.Id, newFact, ct);
                        currentFactsByEntityId.Remove(entity.Id);
                        await QueueFactReviewIfNeededAsync(newFact, factThreshold, ct);
                        break;
                    case FactConflictStrategy.Parallel:
                    case FactConflictStrategy.Tentative:
                        var parallelSaved = await _factRepository.UpsertAsync(newFact, ct);
                        currentFactsByEntityId.Remove(entity.Id);
                        await QueueFactReviewIfNeededAsync(parallelSaved, factThreshold, ct);
                        break;
                }
            }
            else
            {
                var saved = await _factRepository.UpsertAsync(newFact, ct);
                currentFactsByEntityId.Remove(entity.Id);
                await QueueFactReviewIfNeededAsync(saved, factThreshold, ct);
            }
        }

        foreach (var rel in extraction.Relationships.Where(r => !string.IsNullOrWhiteSpace(r.FromEntityName) && !string.IsNullOrWhiteSpace(r.ToEntityName) && !string.IsNullOrWhiteSpace(r.Type)))
        {
            if (IsGenericEntityToken(rel.FromEntityName) || IsGenericEntityToken(rel.ToEntityName))
            {
                continue;
            }

            if (rel.Confidence < _settings.MinRelationshipConfidence)
            {
                continue;
            }

            var from = await GetOrCreateCachedEntityAsync(entityByName, rel.FromEntityName.Trim(), EntityType.Person, sourceMessage, senderName, ct);
            var to = await GetOrCreateCachedEntityAsync(entityByName, rel.ToEntityName.Trim(), EntityType.Person, sourceMessage, senderName, ct);

            await _relationshipRepository.UpsertAsync(new Relationship
            {
                FromEntityId = from.Id,
                ToEntityId = to.Id,
                Type = rel.Type.Trim().ToLowerInvariant(),
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
                ct);

            eventBuffer.Add(new CommunicationEvent
            {
                MessageId = messageId,
                EntityId = subjectEntity.Id,
                EventType = evt.Type.Trim().ToLowerInvariant(),
                ObjectName = string.IsNullOrWhiteSpace(evt.ObjectName) ? null : evt.ObjectName.Trim(),
                Sentiment = string.IsNullOrWhiteSpace(evt.Sentiment) ? null : evt.Sentiment.Trim().ToLowerInvariant(),
                Summary = string.IsNullOrWhiteSpace(evt.Summary) ? null : evt.Summary.Trim(),
                Confidence = evt.Confidence
            });
        }

        await PersistIntelligenceAsync(messageId, extraction, sourceMessage, senderName, entityByName, ct);

        if (eventBuffer.Count > 0)
        {
            await _communicationEventRepository.AddRangeAsync(eventBuffer, ct);
        }

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
                ct);
            observationRows.Add(new IntelligenceObservation
            {
                MessageId = messageId,
                EntityId = subjectEntity?.Id,
                SubjectName = subjectName,
                ObservationType = observation.Type.Trim().ToLowerInvariant(),
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
                ct);
            var normalizedCategory = string.IsNullOrWhiteSpace(claim.Category) ? "general" : claim.Category.Trim().ToLowerInvariant();
            claimRows.Add(new IntelligenceClaim
            {
                MessageId = messageId,
                EntityId = entity?.Id,
                EntityName = entityName,
                ClaimType = string.IsNullOrWhiteSpace(claim.ClaimType) ? "fact" : claim.ClaimType.Trim().ToLowerInvariant(),
                Category = normalizedCategory,
                Key = claim.Key.Trim(),
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
        CancellationToken ct)
    {
        var normalized = entityName.Trim();
        if (entityByName.TryGetValue(normalized, out var cached))
        {
            return cached;
        }

        var entity = await UpsertEntityWithActorContextAsync(normalized, fallbackType, sourceMessage, senderName, ct);
        entityByName[normalized] = entity;
        entityByName[entity.Name] = entity;
        return entity;
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

    private async Task QueueFactReviewIfNeededAsync(Fact fact, float threshold, CancellationToken ct)
    {
        if (fact.Status != ConfidenceStatus.Tentative)
        {
            return;
        }

        var reason = $"auto_review category={fact.Category} confidence={fact.Confidence:0.00} threshold={threshold:0.00}";
        await _factReviewCommandRepository.EnqueueAsync(fact.Id, "approve", reason, ct);
    }

    private async Task<Entity> UpsertEntityWithActorContextAsync(
        string name,
        EntityType fallbackType,
        Message? sourceMessage,
        string? senderName,
        CancellationToken ct)
    {
        var observedName = name.Trim();
        var isSender = sourceMessage is not null
            && !string.IsNullOrWhiteSpace(senderName)
            && string.Equals(observedName, senderName, StringComparison.OrdinalIgnoreCase);

        if (isSender)
        {
            var senderEntity = await _entityRepository.UpsertAsync(new Entity
            {
                Name = senderName!,
                Type = EntityType.Person,
                ActorKey = BuildActorKey(sourceMessage!.ChatId, sourceMessage.SenderId),
                TelegramUserId = sourceMessage.SenderId > 0 ? sourceMessage.SenderId : null
            }, ct);

            await ObserveAliasAsync(senderEntity, observedName, sourceMessage.Id, ct);
            return senderEntity;
        }

        var entity = await _entityRepository.UpsertAsync(new Entity
        {
            Name = observedName,
            Type = fallbackType
        }, ct);

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
}
