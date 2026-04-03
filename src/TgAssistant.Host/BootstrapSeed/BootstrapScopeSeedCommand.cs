using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Host.BootstrapSeed;

public sealed class BootstrapScopeSeedCommand
{
    private const string ActiveStatus = "active";
    private const string OperatorPersonType = "operator_root";
    private const string TrackedPersonType = "tracked_person";
    private const string LinkType = "operator_tracked_seed";
    private const string SeedSourceKind = "bootstrap_scope_seed";
    private const string SeedProvenanceKind = "operator_seed_command";
    private const string SeedEvidenceKind = "bootstrap_scope_seed";
    private const string SeedTruthLayer = ModelNormalizationTruthLayers.CanonicalTruth;
    private const string SeedLinkRole = "subject";

    private readonly IDbContextFactory<TgAssistantDbContext> _dbFactory;
    private readonly IStage6BootstrapRepository _bootstrapRepository;
    private readonly ILogger<BootstrapScopeSeedCommand> _logger;

    public BootstrapScopeSeedCommand(
        IDbContextFactory<TgAssistantDbContext> dbFactory,
        IStage6BootstrapRepository bootstrapRepository,
        ILogger<BootstrapScopeSeedCommand> logger)
    {
        _dbFactory = dbFactory;
        _bootstrapRepository = bootstrapRepository;
        _logger = logger;
    }

    public async Task<BootstrapScopeSeedResult> RunAsync(
        BootstrapScopeSeedRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = DateTime.UtcNow;
        var result = new BootstrapScopeSeedResult
        {
            ScopeKey = NormalizeRequired(request.ScopeKey),
            DryRun = !request.Apply,
            ApplyRequested = request.Apply
        };

        result.OperatorDisplayName = NormalizeRequired(request.OperatorFullName);
        result.TrackedDisplayName = NormalizeRequired(request.TrackedFullName);
        result.OperatorCanonicalName = NormalizeCanonical(request.OperatorCanonicalName, result.OperatorDisplayName);
        result.TrackedCanonicalName = NormalizeCanonical(request.TrackedCanonicalName, result.TrackedDisplayName);

        ValidateBaseContract(request, result);
        if (result.ValidationErrors.Count > 0)
        {
            result.ContractStatus = BootstrapSeedContractStatuses.InvalidSeedContract;
            result.BootstrapReady = false;
            result.RelationStatus = "invalid_contract";
            result.EvidenceLinkStatus = "invalid_contract";
            return result;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var plan = await BuildPlanAsync(db, request, result, now, ct);
        if (result.ValidationErrors.Count > 0)
        {
            result.ContractStatus = BootstrapSeedContractStatuses.InvalidSeedContract;
            result.BootstrapReady = false;
            result.RelationStatus = "invalid_contract";
            result.EvidenceLinkStatus = "invalid_contract";
            return result;
        }

        PopulatePlannedActions(result, plan);

        if (request.Apply)
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await ApplyPlanAsync(db, request, result, plan, now, ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        await FinalizeReadinessAsync(result, ct);
        _logger.LogInformation(
            "Bootstrap scope seed command finished: mode={Mode}, scope_key={ScopeKey}, contract_status={ContractStatus}, bootstrap_ready={BootstrapReady}, operator_person_id={OperatorPersonId}, tracked_person_id={TrackedPersonId}",
            result.DryRun ? "dry-run" : "apply",
            result.ScopeKey,
            result.ContractStatus,
            result.BootstrapReady,
            result.OperatorPersonId,
            result.TrackedPersonId);
        return result;
    }

    private static void PopulatePlannedActions(BootstrapScopeSeedResult result, BootstrapScopeSeedPlan plan)
    {
        result.OperatorAction = plan.Operator.NewPerson == null ? BootstrapSeedAction.WouldReuse : BootstrapSeedAction.WouldCreate;
        result.TrackedAction = plan.Tracked.NewPerson == null ? BootstrapSeedAction.WouldReuse : BootstrapSeedAction.WouldCreate;
        result.RelationAction = plan.NewLink == null ? BootstrapSeedAction.WouldReuse : BootstrapSeedAction.WouldCreate;

        if (plan.EvidenceStatus == BootstrapEvidenceStatus.ReusedExisting)
        {
            result.SourceAction = BootstrapSeedAction.WouldReuse;
            result.EvidenceAction = BootstrapSeedAction.WouldReuse;
            result.EvidenceLinkAction = BootstrapSeedAction.WouldReuse;
            return;
        }

        result.SourceAction = plan.NewSource == null ? BootstrapSeedAction.WouldReuse : BootstrapSeedAction.WouldCreate;
        result.EvidenceAction = plan.NewEvidence == null ? BootstrapSeedAction.WouldReuse : BootstrapSeedAction.WouldCreate;
        result.EvidenceLinkAction = plan.NewEvidenceLink == null ? BootstrapSeedAction.WouldReuse : BootstrapSeedAction.WouldCreate;
    }

    private static void ValidateBaseContract(BootstrapScopeSeedRequest request, BootstrapScopeSeedResult result)
    {
        if (string.IsNullOrWhiteSpace(result.ScopeKey))
        {
            result.ValidationErrors.Add("--seed-scope-key is required.");
        }

        if (string.IsNullOrWhiteSpace(result.OperatorDisplayName))
        {
            result.ValidationErrors.Add("--seed-operator-full-name is required.");
        }

        if (string.IsNullOrWhiteSpace(result.TrackedDisplayName))
        {
            result.ValidationErrors.Add("--seed-tracked-full-name is required.");
        }

        if (request.OperatorPersonId != null && request.TrackedPersonId != null && request.OperatorPersonId == request.TrackedPersonId)
        {
            result.ValidationErrors.Add("operator and tracked person ids must not be equal.");
        }
    }

    private async Task<BootstrapScopeSeedPlan> BuildPlanAsync(
        TgAssistantDbContext db,
        BootstrapScopeSeedRequest request,
        BootstrapScopeSeedResult result,
        DateTime now,
        CancellationToken ct)
    {
        var plan = new BootstrapScopeSeedPlan();
        var scopeKey = result.ScopeKey!;

        var operatorResolution = await ResolvePersonAsync(
            db,
            scopeKey,
            OperatorPersonType,
            result.OperatorDisplayName!,
            result.OperatorCanonicalName!,
            request.OperatorPersonId,
            request.OperatorTelegramUserId,
            NormalizeUsername(request.OperatorTelegramUsername),
            now,
            ct);
        if (operatorResolution.Error != null)
        {
            result.ValidationErrors.Add(operatorResolution.Error);
            return plan;
        }

        var trackedResolution = await ResolvePersonAsync(
            db,
            scopeKey,
            TrackedPersonType,
            result.TrackedDisplayName!,
            result.TrackedCanonicalName!,
            request.TrackedPersonId,
            request.TrackedTelegramUserId,
            NormalizeUsername(request.TrackedTelegramUsername),
            now,
            ct);
        if (trackedResolution.Error != null)
        {
            result.ValidationErrors.Add(trackedResolution.Error);
            return plan;
        }

        plan.Operator = operatorResolution;
        plan.Tracked = trackedResolution;
        result.OperatorPersonId = operatorResolution.Person?.Id ?? operatorResolution.NewPerson?.Id;
        result.TrackedPersonId = trackedResolution.Person?.Id ?? trackedResolution.NewPerson?.Id;

        if (result.OperatorPersonId == null || result.TrackedPersonId == null)
        {
            result.ValidationErrors.Add("failed to resolve operator/tracked person contract.");
            return plan;
        }

        if (result.OperatorPersonId == result.TrackedPersonId)
        {
            result.ValidationErrors.Add("operator and tracked person resolved to the same person id.");
            return plan;
        }

        var existingLinks = await db.PersonOperatorLinks
            .Where(x => x.ScopeKey == scopeKey
                && x.PersonId == result.TrackedPersonId.Value
                && x.Status == ActiveStatus)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(ct);

        var conflictingLinks = existingLinks
            .Where(x => x.OperatorPersonId != result.OperatorPersonId.Value)
            .ToList();
        if (conflictingLinks.Count > 0)
        {
            result.ValidationErrors.Add("tracked person has active operator link(s) to a different operator in this scope.");
            return plan;
        }

        plan.Link = existingLinks.FirstOrDefault(x => x.OperatorPersonId == result.OperatorPersonId.Value);
        if (plan.Link == null)
        {
            plan.NewLink = new DbPersonOperatorLink
            {
                ScopeKey = scopeKey,
                OperatorPersonId = result.OperatorPersonId.Value,
                PersonId = result.TrackedPersonId.Value,
                LinkType = LinkType,
                Status = ActiveStatus,
                SourceBindingType = ResolveBindingType(request),
                SourceBindingValue = ResolveBindingValue(request),
                SourceBindingNormalized = ResolveBindingNormalized(request),
                SourceMessageId = request.SourceMessageId,
                CreatedAt = now,
                UpdatedAt = now
            };
        }

        var hasEvidence = await HasActiveEvidenceAsync(db, scopeKey, result.TrackedPersonId.Value, ct);
        if (hasEvidence)
        {
            plan.EvidenceStatus = BootstrapEvidenceStatus.ReusedExisting;
            return plan;
        }

        var sourceRef = BuildSeedSourceRef(scopeKey, result.TrackedPersonId.Value);
        plan.Source = await db.SourceObjects
            .FirstOrDefaultAsync(
                x => x.ScopeKey == scopeKey
                    && x.SourceKind == SeedSourceKind
                    && x.SourceRef == sourceRef,
                ct);
        if (plan.Source == null)
        {
            plan.NewSource = new DbSourceObject
            {
                Id = Guid.NewGuid(),
                ScopeKey = scopeKey,
                SourceKind = SeedSourceKind,
                SourceRef = sourceRef,
                ProvenanceKind = SeedProvenanceKind,
                ProvenanceRef = sourceRef,
                ProvenanceNormalized = sourceRef.ToLowerInvariant(),
                Status = ActiveStatus,
                DisplayLabel = $"Bootstrap seed for tracked person {result.TrackedPersonId:D}",
                ChatId = request.ChatId,
                SourceMessageId = request.SourceMessageId,
                OccurredAt = now,
                PayloadJson = JsonSerializer.Serialize(new
                {
                    seed_type = "bootstrap_scope",
                    scope_key = scopeKey,
                    tracked_person_id = result.TrackedPersonId,
                    operator_person_id = result.OperatorPersonId
                }),
                MetadataJson = JsonSerializer.Serialize(new
                {
                    command = "seed-bootstrap-scope",
                    created_at_utc = now
                }),
                CreatedAt = now,
                UpdatedAt = now
            };
        }

        var sourceObjectId = plan.Source?.Id ?? plan.NewSource!.Id;
        plan.Evidence = await db.EvidenceItems
            .Where(x => x.ScopeKey == scopeKey
                && x.SourceObjectId == sourceObjectId
                && x.EvidenceKind == SeedEvidenceKind
                && x.TruthLayer == SeedTruthLayer
                && x.Status == ActiveStatus)
            .OrderByDescending(x => x.UpdatedAt)
            .FirstOrDefaultAsync(ct);
        if (plan.Evidence == null)
        {
            plan.NewEvidence = new DbEvidenceItem
            {
                Id = Guid.NewGuid(),
                ScopeKey = scopeKey,
                SourceObjectId = sourceObjectId,
                EvidenceKind = SeedEvidenceKind,
                Status = ActiveStatus,
                TruthLayer = SeedTruthLayer,
                SummaryText = "Minimal bootstrap seed evidence.",
                StructuredPayloadJson = JsonSerializer.Serialize(new
                {
                    summary = "Bootstrap seed evidence",
                    scope_key = scopeKey,
                    tracked_person_id = result.TrackedPersonId,
                    operator_person_id = result.OperatorPersonId
                }),
                ProvenanceJson = JsonSerializer.Serialize(new
                {
                    source_kind = SeedSourceKind,
                    source_ref = sourceRef,
                    command = "seed-bootstrap-scope"
                }),
                Confidence = 1.0f,
                ObservedAt = now,
                CreatedAt = now,
                UpdatedAt = now
            };
        }

        var evidenceId = plan.Evidence?.Id ?? plan.NewEvidence!.Id;
        plan.EvidenceLink = await db.EvidenceItemPersonLinks
            .FirstOrDefaultAsync(
                x => x.EvidenceItemId == evidenceId
                    && x.PersonId == result.TrackedPersonId.Value
                    && x.LinkRole == SeedLinkRole,
                ct);
        if (plan.EvidenceLink == null)
        {
            plan.NewEvidenceLink = new DbEvidenceItemPersonLink
            {
                EvidenceItemId = evidenceId,
                PersonId = result.TrackedPersonId.Value,
                ScopeKey = scopeKey,
                LinkRole = SeedLinkRole,
                IsPrimary = true,
                CreatedAt = now
            };
        }

        plan.EvidenceStatus = BootstrapEvidenceStatus.RequiresSeedPackage;
        return plan;
    }

    private async Task ApplyPlanAsync(
        TgAssistantDbContext db,
        BootstrapScopeSeedRequest request,
        BootstrapScopeSeedResult result,
        BootstrapScopeSeedPlan plan,
        DateTime now,
        CancellationToken ct)
    {
        if (plan.Operator.NewPerson != null)
        {
            db.Persons.Add(plan.Operator.NewPerson);
            plan.Operator.Person = plan.Operator.NewPerson;
            result.OperatorAction = BootstrapSeedAction.Created;
        }
        else
        {
            result.OperatorAction = BootstrapSeedAction.Reused;
        }

        if (plan.Tracked.NewPerson != null)
        {
            db.Persons.Add(plan.Tracked.NewPerson);
            plan.Tracked.Person = plan.Tracked.NewPerson;
            result.TrackedAction = BootstrapSeedAction.Created;
        }
        else
        {
            result.TrackedAction = BootstrapSeedAction.Reused;
        }

        if (plan.NewLink != null)
        {
            db.PersonOperatorLinks.Add(plan.NewLink);
            plan.Link = plan.NewLink;
            result.RelationAction = BootstrapSeedAction.Created;
        }
        else
        {
            result.RelationAction = BootstrapSeedAction.Reused;
        }

        if (plan.EvidenceStatus == BootstrapEvidenceStatus.ReusedExisting)
        {
            result.SourceAction = BootstrapSeedAction.Reused;
            result.EvidenceAction = BootstrapSeedAction.Reused;
            result.EvidenceLinkAction = BootstrapSeedAction.Reused;
            return;
        }

        if (plan.NewSource != null)
        {
            db.SourceObjects.Add(plan.NewSource);
            plan.Source = plan.NewSource;
            result.SourceAction = BootstrapSeedAction.Created;
        }
        else
        {
            result.SourceAction = BootstrapSeedAction.Reused;
        }

        if (plan.NewEvidence != null)
        {
            plan.NewEvidence.SourceObjectId = plan.Source!.Id;
            db.EvidenceItems.Add(plan.NewEvidence);
            plan.Evidence = plan.NewEvidence;
            result.EvidenceAction = BootstrapSeedAction.Created;
        }
        else
        {
            result.EvidenceAction = BootstrapSeedAction.Reused;
        }

        if (plan.NewEvidenceLink != null)
        {
            plan.NewEvidenceLink.EvidenceItemId = plan.Evidence!.Id;
            db.EvidenceItemPersonLinks.Add(plan.NewEvidenceLink);
            plan.EvidenceLink = plan.NewEvidenceLink;
            result.EvidenceLinkAction = BootstrapSeedAction.Created;
        }
        else
        {
            result.EvidenceLinkAction = BootstrapSeedAction.Reused;
        }

        // Keep optional identity hints bounded and explicit; avoid silent rewrites of established identities.
        ApplyOptionalIdentityHints(plan.Operator.Person!, request.OperatorTelegramUserId, NormalizeUsername(request.OperatorTelegramUsername), request.ChatId, now);
        ApplyOptionalIdentityHints(plan.Tracked.Person!, request.TrackedTelegramUserId, NormalizeUsername(request.TrackedTelegramUsername), request.ChatId, now);
        await Task.CompletedTask;
    }

    private async Task FinalizeReadinessAsync(BootstrapScopeSeedResult result, CancellationToken ct)
    {
        if (result.ValidationErrors.Count > 0)
        {
            result.ContractStatus = BootstrapSeedContractStatuses.InvalidSeedContract;
            result.BootstrapReady = false;
            return;
        }

        var resolution = await _bootstrapRepository.ResolveScopeAsync(new Stage6BootstrapRequest
        {
            ScopeKey = result.ScopeKey
        }, ct);

        result.RelationStatus = resolution.OperatorPerson == null
            ? "operator_link_missing_or_invalid"
            : "active";
        result.EvidenceLinkStatus = resolution.EvidenceCount > 0
            ? "present"
            : "missing";

        switch (resolution.ResolutionStatus)
        {
            case Stage6BootstrapStatuses.Ready:
                result.BootstrapReady = true;
                result.ContractStatus = BootstrapSeedContractStatuses.SeededAndBootstrapReady;
                break;
            case Stage6BootstrapStatuses.NeedMoreData:
            case Stage6BootstrapStatuses.NeedOperatorClarification:
                result.BootstrapReady = false;
                result.ContractStatus = BootstrapSeedContractStatuses.SeededButMissingPrerequisite;
                break;
            default:
                result.BootstrapReady = false;
                result.ContractStatus = BootstrapSeedContractStatuses.InvalidSeedContract;
                break;
        }
    }

    private static async Task<bool> HasActiveEvidenceAsync(
        TgAssistantDbContext db,
        string scopeKey,
        Guid trackedPersonId,
        CancellationToken ct)
    {
        return await (
            from link in db.EvidenceItemPersonLinks
            join evidence in db.EvidenceItems on link.EvidenceItemId equals evidence.Id
            where link.ScopeKey == scopeKey
                && link.PersonId == trackedPersonId
                && evidence.Status == ActiveStatus
            select evidence.Id)
            .AnyAsync(ct);
    }

    private async Task<PersonResolution> ResolvePersonAsync(
        TgAssistantDbContext db,
        string scopeKey,
        string personType,
        string displayName,
        string canonicalName,
        Guid? explicitPersonId,
        long? telegramUserId,
        string? telegramUsername,
        DateTime now,
        CancellationToken ct)
    {
        if (explicitPersonId != null)
        {
            var row = await db.Persons
                .FirstOrDefaultAsync(x => x.Id == explicitPersonId.Value && x.Status == ActiveStatus, ct);
            if (row == null)
            {
                return PersonResolution.ErrorResult($"provided {personType} id '{explicitPersonId:D}' was not found in active status.");
            }

            if (!string.Equals(row.ScopeKey, scopeKey, StringComparison.Ordinal))
            {
                return PersonResolution.ErrorResult(
                    $"provided {personType} id '{explicitPersonId:D}' belongs to scope '{row.ScopeKey}', expected '{scopeKey}'.");
            }

            if (!string.Equals(row.PersonType, personType, StringComparison.Ordinal))
            {
                return PersonResolution.ErrorResult(
                    $"provided person id '{explicitPersonId:D}' has person_type '{row.PersonType}', expected '{personType}'.");
            }

            var selectorError = ValidateSelectorCompatibility(row, displayName, canonicalName, telegramUserId, telegramUsername);
            if (selectorError != null)
            {
                return PersonResolution.ErrorResult(selectorError);
            }

            return new PersonResolution
            {
                Person = row
            };
        }

        var candidates = await db.Persons
            .Where(x => x.ScopeKey == scopeKey && x.PersonType == personType && x.Status == ActiveStatus)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(ct);
        var normalizedDisplay = NormalizeForComparison(displayName);
        var normalizedCanonical = NormalizeForComparison(canonicalName);
        var matches = candidates
            .Where(x =>
                string.Equals(NormalizeForComparison(x.DisplayName), normalizedDisplay, StringComparison.Ordinal)
                || string.Equals(NormalizeForComparison(x.CanonicalName), normalizedCanonical, StringComparison.Ordinal)
                || (telegramUserId != null && x.PrimaryTelegramUserId == telegramUserId)
                || (!string.IsNullOrWhiteSpace(telegramUsername)
                    && string.Equals(NormalizeUsername(x.PrimaryTelegramUsername), telegramUsername, StringComparison.Ordinal)))
            .ToList();

        if (matches.Count > 1)
        {
            return PersonResolution.ErrorResult(
                $"ambiguous {personType} contract in scope '{scopeKey}': matched {matches.Count} active persons for provided selectors.");
        }

        if (matches.Count == 1)
        {
            return new PersonResolution
            {
                Person = matches[0]
            };
        }

        var newPerson = new DbPerson
        {
            Id = Guid.NewGuid(),
            ScopeKey = scopeKey,
            PersonType = personType,
            DisplayName = displayName,
            CanonicalName = canonicalName,
            Status = ActiveStatus,
            PrimaryTelegramUserId = telegramUserId,
            PrimaryTelegramUsername = telegramUsername,
            PrimaryActorKey = BuildActorKey(scopeKey, telegramUserId, null),
            MetadataJson = JsonSerializer.Serialize(new
            {
                seed_contract = "bootstrap_scope",
                created_by = "seed-bootstrap-scope"
            }),
            CreatedAt = now,
            UpdatedAt = now
        };

        return new PersonResolution
        {
            NewPerson = newPerson
        };
    }

    private static string? ValidateSelectorCompatibility(
        DbPerson person,
        string displayName,
        string canonicalName,
        long? telegramUserId,
        string? telegramUsername)
    {
        if (!string.Equals(NormalizeForComparison(person.DisplayName), NormalizeForComparison(displayName), StringComparison.Ordinal))
        {
            return $"provided full name does not match existing person '{person.Id:D}' display_name.";
        }

        if (!string.Equals(NormalizeForComparison(person.CanonicalName), NormalizeForComparison(canonicalName), StringComparison.Ordinal))
        {
            return $"provided canonical name does not match existing person '{person.Id:D}' canonical_name.";
        }

        if (telegramUserId != null && person.PrimaryTelegramUserId != null && person.PrimaryTelegramUserId != telegramUserId)
        {
            return $"provided telegram user id conflicts with existing person '{person.Id:D}'.";
        }

        var existingUsername = NormalizeUsername(person.PrimaryTelegramUsername);
        if (telegramUsername != null && existingUsername != null && !string.Equals(existingUsername, telegramUsername, StringComparison.Ordinal))
        {
            return $"provided telegram username conflicts with existing person '{person.Id:D}'.";
        }

        return null;
    }

    private static void ApplyOptionalIdentityHints(
        DbPerson person,
        long? telegramUserId,
        string? telegramUsername,
        long? chatId,
        DateTime now)
    {
        var changed = false;
        if (telegramUserId != null && person.PrimaryTelegramUserId == null)
        {
            person.PrimaryTelegramUserId = telegramUserId;
            changed = true;
        }

        if (!string.IsNullOrWhiteSpace(telegramUsername) && string.IsNullOrWhiteSpace(person.PrimaryTelegramUsername))
        {
            person.PrimaryTelegramUsername = telegramUsername;
            changed = true;
        }

        if (person.PrimaryActorKey == null)
        {
            var actorKey = BuildActorKey(person.ScopeKey, person.PrimaryTelegramUserId, chatId);
            if (actorKey != null)
            {
                person.PrimaryActorKey = actorKey;
                changed = true;
            }
        }

        if (changed)
        {
            person.UpdatedAt = now;
        }
    }

    private static string? ResolveBindingType(BootstrapScopeSeedRequest request)
    {
        if (request.TrackedTelegramUserId != null || request.OperatorTelegramUserId != null)
        {
            return "telegram_user_id";
        }

        if (!string.IsNullOrWhiteSpace(request.TrackedTelegramUsername) || !string.IsNullOrWhiteSpace(request.OperatorTelegramUsername))
        {
            return "telegram_username";
        }

        return "seed_scope";
    }

    private static string ResolveBindingValue(BootstrapScopeSeedRequest request)
    {
        if (request.TrackedTelegramUserId != null)
        {
            return request.TrackedTelegramUserId.Value.ToString();
        }

        if (!string.IsNullOrWhiteSpace(request.TrackedTelegramUsername))
        {
            return NormalizeUsername(request.TrackedTelegramUsername)!;
        }

        return "bootstrap_scope_seed";
    }

    private static string ResolveBindingNormalized(BootstrapScopeSeedRequest request)
        => ResolveBindingValue(request).Trim().ToLowerInvariant();

    private static string BuildSeedSourceRef(string scopeKey, Guid trackedPersonId)
        => $"{scopeKey}:tracked:{trackedPersonId:D}:bootstrap_seed";

    private static string? BuildActorKey(string scopeKey, long? telegramUserId, long? chatId)
    {
        if (telegramUserId == null)
        {
            return null;
        }

        var resolvedChatId = chatId ?? TryParseChatIdFromScope(scopeKey);
        return resolvedChatId == null ? null : $"{resolvedChatId.Value}:{telegramUserId.Value}";
    }

    private static long? TryParseChatIdFromScope(string scopeKey)
    {
        if (!scopeKey.StartsWith("chat:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var raw = scopeKey["chat:".Length..];
        return long.TryParse(raw, out var chatId) ? chatId : null;
    }

    private static string NormalizeRequired(string? value)
        => value?.Trim() ?? string.Empty;

    private static string NormalizeCanonical(string? canonical, string fallbackDisplayName)
    {
        if (!string.IsNullOrWhiteSpace(canonical))
        {
            return CollapseWhitespace(canonical).ToLowerInvariant();
        }

        return CollapseWhitespace(fallbackDisplayName).ToLowerInvariant();
    }

    private static string NormalizeForComparison(string? value)
        => CollapseWhitespace(value ?? string.Empty).ToLowerInvariant();

    private static string CollapseWhitespace(string value)
        => string.Join(' ', value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static string? NormalizeUsername(string? username)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return null;
        }

        var normalized = username.Trim();
        if (normalized.StartsWith('@'))
        {
            normalized = normalized[1..];
        }

        return normalized.ToLowerInvariant();
    }
}

public sealed class BootstrapScopeSeedRequest
{
    public string? ScopeKey { get; set; }
    public string? OperatorFullName { get; set; }
    public string? TrackedFullName { get; set; }
    public Guid? OperatorPersonId { get; set; }
    public Guid? TrackedPersonId { get; set; }
    public string? OperatorCanonicalName { get; set; }
    public string? TrackedCanonicalName { get; set; }
    public long? ChatId { get; set; }
    public long? TrackedTelegramUserId { get; set; }
    public string? TrackedTelegramUsername { get; set; }
    public long? OperatorTelegramUserId { get; set; }
    public string? OperatorTelegramUsername { get; set; }
    public long? SourceMessageId { get; set; }
    public bool Apply { get; set; }
}

public static class BootstrapScopeSeedArgsParser
{
    public static BootstrapScopeSeedRequest ParseOrThrow(string[] args)
    {
        var apply = HasFlag(args, "--seed-apply");
        var explicitDryRun = HasFlag(args, "--seed-dry-run");
        if (apply && explicitDryRun)
        {
            throw new InvalidOperationException("Use either --seed-apply or --seed-dry-run, not both.");
        }

        return new BootstrapScopeSeedRequest
        {
            ScopeKey = GetValue(args, "--seed-scope-key="),
            OperatorFullName = GetValue(args, "--seed-operator-full-name="),
            TrackedFullName = GetValue(args, "--seed-tracked-full-name="),
            OperatorPersonId = ParseGuid(GetValue(args, "--seed-operator-person-id="), "--seed-operator-person-id"),
            TrackedPersonId = ParseGuid(GetValue(args, "--seed-tracked-person-id="), "--seed-tracked-person-id"),
            OperatorCanonicalName = GetValue(args, "--seed-operator-canonical-name="),
            TrackedCanonicalName = GetValue(args, "--seed-tracked-canonical-name="),
            ChatId = ParseLong(GetValue(args, "--seed-chat-id="), "--seed-chat-id"),
            TrackedTelegramUserId = ParseLong(GetValue(args, "--seed-tracked-telegram-user-id="), "--seed-tracked-telegram-user-id"),
            TrackedTelegramUsername = GetValue(args, "--seed-tracked-telegram-username="),
            OperatorTelegramUserId = ParseLong(GetValue(args, "--seed-operator-telegram-user-id="), "--seed-operator-telegram-user-id"),
            OperatorTelegramUsername = GetValue(args, "--seed-operator-telegram-username="),
            SourceMessageId = ParseLong(GetValue(args, "--seed-source-message-id="), "--seed-source-message-id"),
            Apply = apply
        };
    }

    private static bool HasFlag(string[] args, string flag)
        => args.Any(x => string.Equals(x, flag, StringComparison.OrdinalIgnoreCase));

    private static string? GetValue(string[] args, string prefix)
    {
        var arg = args.FirstOrDefault(x => x.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        return arg is null ? null : arg[prefix.Length..];
    }

    private static Guid? ParseGuid(string? raw, string argumentName)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (!Guid.TryParse(raw, out var parsed))
        {
            throw new InvalidOperationException($"Invalid {argumentName} value '{raw}'. Expected GUID.");
        }

        return parsed;
    }

    private static long? ParseLong(string? raw, string argumentName)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (!long.TryParse(raw, out var parsed))
        {
            throw new InvalidOperationException($"Invalid {argumentName} value '{raw}'. Expected 64-bit integer.");
        }

        return parsed;
    }
}

public sealed class BootstrapScopeSeedResult
{
    public bool DryRun { get; set; }
    public bool ApplyRequested { get; set; }
    public string? ScopeKey { get; set; }
    public Guid? OperatorPersonId { get; set; }
    public Guid? TrackedPersonId { get; set; }
    public string? OperatorDisplayName { get; set; }
    public string? TrackedDisplayName { get; set; }
    public string? OperatorCanonicalName { get; set; }
    public string? TrackedCanonicalName { get; set; }
    public BootstrapSeedAction OperatorAction { get; set; } = BootstrapSeedAction.WouldReuse;
    public BootstrapSeedAction TrackedAction { get; set; } = BootstrapSeedAction.WouldReuse;
    public BootstrapSeedAction RelationAction { get; set; } = BootstrapSeedAction.WouldReuse;
    public BootstrapSeedAction SourceAction { get; set; } = BootstrapSeedAction.WouldReuse;
    public BootstrapSeedAction EvidenceAction { get; set; } = BootstrapSeedAction.WouldReuse;
    public BootstrapSeedAction EvidenceLinkAction { get; set; } = BootstrapSeedAction.WouldReuse;
    public string RelationStatus { get; set; } = "unknown";
    public string EvidenceLinkStatus { get; set; } = "unknown";
    public bool BootstrapReady { get; set; }
    public string ContractStatus { get; set; } = BootstrapSeedContractStatuses.InvalidSeedContract;
    public List<string> ValidationErrors { get; } = [];
}

public static class BootstrapSeedContractStatuses
{
    public const string SeededAndBootstrapReady = "seeded_and_bootstrap_ready";
    public const string SeededButMissingPrerequisite = "seeded_but_still_missing_prerequisite";
    public const string InvalidSeedContract = "invalid_seed_contract";
}

public enum BootstrapSeedAction
{
    Created,
    Reused,
    WouldCreate,
    WouldReuse
}

internal sealed class BootstrapScopeSeedPlan
{
    public PersonResolution Operator { get; set; } = new();
    public PersonResolution Tracked { get; set; } = new();
    public DbPersonOperatorLink? Link { get; set; }
    public DbPersonOperatorLink? NewLink { get; set; }
    public DbSourceObject? Source { get; set; }
    public DbSourceObject? NewSource { get; set; }
    public DbEvidenceItem? Evidence { get; set; }
    public DbEvidenceItem? NewEvidence { get; set; }
    public DbEvidenceItemPersonLink? EvidenceLink { get; set; }
    public DbEvidenceItemPersonLink? NewEvidenceLink { get; set; }
    public BootstrapEvidenceStatus EvidenceStatus { get; set; }
}

internal sealed class PersonResolution
{
    public DbPerson? Person { get; set; }
    public DbPerson? NewPerson { get; set; }
    public string? Error { get; set; }

    public static PersonResolution ErrorResult(string error) => new() { Error = error };
}

internal enum BootstrapEvidenceStatus
{
    ReusedExisting,
    RequiresSeedPackage
}

public static class BootstrapScopeSeedReportFormatter
{
    public static string Format(BootstrapScopeSeedResult result)
    {
        var lines = new List<string>
        {
            "bootstrap_scope_seed_report:",
            $"  mode: {(result.DryRun ? "dry-run" : "apply")}",
            $"  scope_key: {result.ScopeKey ?? "n/a"}",
            $"  operator_person_id: {result.OperatorPersonId?.ToString("D") ?? "n/a"}",
            $"  tracked_person_id: {result.TrackedPersonId?.ToString("D") ?? "n/a"}",
            $"  relation_status: {result.RelationStatus}",
            $"  evidence_link_status: {result.EvidenceLinkStatus}",
            $"  contract_status: {result.ContractStatus}",
            $"  bootstrap_ready: {(result.BootstrapReady ? "yes" : "no")}",
            $"  actions: operator={result.OperatorAction}, tracked={result.TrackedAction}, relation={result.RelationAction}, source={result.SourceAction}, evidence={result.EvidenceAction}, evidence_link={result.EvidenceLinkAction}"
        };

        if (result.ValidationErrors.Count > 0)
        {
            lines.Add("  validation_errors:");
            lines.AddRange(result.ValidationErrors.Select(x => $"    - {x}"));
        }

        return string.Join(Environment.NewLine, lines);
    }
}
