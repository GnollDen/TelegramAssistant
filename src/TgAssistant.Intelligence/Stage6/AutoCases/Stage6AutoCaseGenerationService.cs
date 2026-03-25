using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Intelligence.Stage6.AutoCases;

public class Stage6AutoCaseGenerationService
{
    private readonly Stage6AutoCaseGenerationSettings _settings;
    private readonly IDbContextFactory<TgAssistantDbContext> _dbFactory;
    private readonly IStage6CaseRepository _stage6CaseRepository;
    private readonly ILogger<Stage6AutoCaseGenerationService> _logger;

    public Stage6AutoCaseGenerationService(
        IOptions<Stage6AutoCaseGenerationSettings> settings,
        IDbContextFactory<TgAssistantDbContext> dbFactory,
        IStage6CaseRepository stage6CaseRepository,
        ILogger<Stage6AutoCaseGenerationService> logger)
    {
        _settings = settings.Value;
        _dbFactory = dbFactory;
        _stage6CaseRepository = stage6CaseRepository;
        _logger = logger;
    }

    public async Task<AutoCaseGenerationRunResult> RunOnceAsync(bool force = false, CancellationToken ct = default)
    {
        if (!_settings.Enabled && !force)
        {
            return new AutoCaseGenerationRunResult
            {
                IsSkipped = true
            };
        }

        var now = DateTime.UtcNow;
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var scopes = await LoadCandidateScopesAsync(db, now, ct);

        var result = new AutoCaseGenerationRunResult
        {
            ScopesConsidered = scopes.Count
        };

        foreach (var scope in scopes)
        {
            var scopeResult = await EvaluateScopeAsync(db, scope, now, ct);
            result.Created += scopeResult.Created;
            result.Updated += scopeResult.Updated;
            result.Reopened += scopeResult.Reopened;
            result.Staled += scopeResult.Staled;
            result.Suppressed += scopeResult.Suppressed;
        }

        _logger.LogInformation(
            "Stage6 auto-case generation run finished: scopes={Scopes}, created={Created}, updated={Updated}, reopened={Reopened}, staled={Staled}, suppressed={Suppressed}",
            result.ScopesConsidered,
            result.Created,
            result.Updated,
            result.Reopened,
            result.Staled,
            result.Suppressed);

        return result;
    }

    private async Task<List<CaseScopeRef>> LoadCandidateScopesAsync(TgAssistantDbContext db, DateTime now, CancellationToken ct)
    {
        var cutoff = now.AddHours(-Math.Max(1, _settings.ScopeLookbackHours));
        var refs = new List<CaseScopeRef>();

        refs.AddRange(await db.StateSnapshots
            .AsNoTracking()
            .Where(x => x.CreatedAt >= cutoff)
            .Select(x => new CaseScopeRef(x.CaseId, x.ChatId, x.CreatedAt))
            .ToListAsync(ct));

        refs.AddRange(await db.StrategyRecords
            .AsNoTracking()
            .Where(x => x.CreatedAt >= cutoff)
            .Select(x => new CaseScopeRef(x.CaseId, x.ChatId, x.CreatedAt))
            .ToListAsync(ct));

        refs.AddRange(await db.ClarificationQuestions
            .AsNoTracking()
            .Where(x => x.UpdatedAt >= cutoff)
            .Select(x => new CaseScopeRef(x.CaseId, x.ChatId, x.UpdatedAt))
            .ToListAsync(ct));

        refs.AddRange(await db.InboxItems
            .AsNoTracking()
            .Where(x => x.UpdatedAt >= cutoff)
            .Select(x => new CaseScopeRef(x.CaseId, x.ChatId, x.UpdatedAt))
            .ToListAsync(ct));

        refs.AddRange(await db.ConflictRecords
            .AsNoTracking()
            .Where(x => x.UpdatedAt >= cutoff)
            .Select(x => new CaseScopeRef(x.CaseId, x.ChatId, x.UpdatedAt))
            .ToListAsync(ct));

        refs.AddRange(await db.Stage6Artifacts
            .AsNoTracking()
            .Where(x => x.UpdatedAt >= cutoff)
            .Select(x => new CaseScopeRef(x.CaseId, x.ChatId, x.UpdatedAt))
            .ToListAsync(ct));

        refs.AddRange(await db.Stage6Cases
            .AsNoTracking()
            .Where(x => x.UpdatedAt >= cutoff)
            .Select(x => new CaseScopeRef(x.ScopeCaseId, x.ChatId, x.UpdatedAt))
            .ToListAsync(ct));

        return refs
            .GroupBy(x => x.CaseId)
            .Select(g =>
            {
                var chatId = g
                    .Where(x => x.ChatId.HasValue)
                    .OrderByDescending(x => x.ActivityAt)
                    .Select(x => x.ChatId)
                    .FirstOrDefault();
                var activityAt = g.Max(x => x.ActivityAt);
                return new CaseScopeRef(g.Key, chatId, activityAt);
            })
            .OrderByDescending(x => x.ActivityAt)
            .ToList();
    }

    private async Task<ScopeEvalResult> EvaluateScopeAsync(
        TgAssistantDbContext db,
        CaseScopeRef scope,
        DateTime now,
        CancellationToken ct)
    {
        var existingCases = (await _stage6CaseRepository.GetCasesAsync(scope.CaseId, ct: ct))
            .Where(x => string.Equals(x.SourceObjectType, AutoCaseSourceType, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(x => BuildCaseKey(x.CaseType, x.SourceObjectId), StringComparer.OrdinalIgnoreCase);

        var latestState = await db.StateSnapshots
            .AsNoTracking()
            .Where(x => x.CaseId == scope.CaseId)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(ct);

        var latestStrategy = await db.StrategyRecords
            .AsNoTracking()
            .Where(x => x.CaseId == scope.CaseId)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(ct);

        var openPeriod = await db.Periods
            .AsNoTracking()
            .Where(x => x.CaseId == scope.CaseId && x.IsOpen)
            .OrderByDescending(x => x.UpdatedAt)
            .FirstOrDefaultAsync(ct);

        var latestCurrentStateArtifact = await db.Stage6Artifacts
            .AsNoTracking()
            .Where(x => x.CaseId == scope.CaseId && x.ArtifactType == Stage6ArtifactTypes.CurrentState && x.IsCurrent)
            .OrderByDescending(x => x.GeneratedAt)
            .FirstOrDefaultAsync(ct);

        var latestDossierArtifact = await db.Stage6Artifacts
            .AsNoTracking()
            .Where(x => x.CaseId == scope.CaseId && x.ArtifactType == Stage6ArtifactTypes.Dossier && x.IsCurrent)
            .OrderByDescending(x => x.GeneratedAt)
            .FirstOrDefaultAsync(ct);

        var latestDraftArtifact = await db.Stage6Artifacts
            .AsNoTracking()
            .Where(x => x.CaseId == scope.CaseId && x.ArtifactType == Stage6ArtifactTypes.Draft && x.IsCurrent)
            .OrderByDescending(x => x.GeneratedAt)
            .FirstOrDefaultAsync(ct);

        var latestDraftOutcomeAt = await (
            from outcome in db.DraftOutcomes.AsNoTracking()
            join strategy in db.StrategyRecords.AsNoTracking() on outcome.StrategyRecordId equals strategy.Id
            where strategy.CaseId == scope.CaseId
            orderby outcome.CreatedAt descending
            select (DateTime?)outcome.CreatedAt
        ).FirstOrDefaultAsync(ct);

        var recentMessages = scope.ChatId.HasValue
            ? await db.Messages
                .AsNoTracking()
                .Where(x => x.ChatId == scope.ChatId.Value && x.Timestamp >= now.AddHours(-Math.Max(4, _settings.ScopeLookbackHours)))
                .OrderByDescending(x => x.Timestamp)
                .Take(120)
                .ToListAsync(ct)
            : [];

        var latestMessage = recentMessages.OrderByDescending(x => x.Timestamp).FirstOrDefault();
        var senderStats = recentMessages
            .GroupBy(x => x.SenderId)
            .Select(g => new SenderRef(g.Key, g.OrderByDescending(x => x.Timestamp).Select(x => x.SenderName).FirstOrDefault() ?? g.Key.ToString()))
            .ToList();

        var candidates = new List<AutoCaseCandidate>();
        AddIfNotNull(candidates, BuildRiskCase(scope, latestState, now));
        AddIfNotNull(candidates, BuildNeedsReviewCase(scope, latestStrategy, now));
        AddIfNotNull(candidates, BuildStateRefreshCase(scope, latestCurrentStateArtifact, latestState, recentMessages, now));
        AddIfNotNull(candidates, BuildDossierCandidateCase(scope, latestDossierArtifact, recentMessages, now));
        AddIfNotNull(candidates, BuildDraftCandidateCase(scope, latestStrategy, latestDraftArtifact, latestDraftOutcomeAt, recentMessages, now));

        var clarificationCandidates = BuildClarificationCases(
            scope,
            latestState,
            latestStrategy,
            openPeriod,
            latestMessage,
            senderStats,
            now);
        candidates.AddRange(clarificationCandidates);

        if (clarificationCandidates.Count == 0 && latestStrategy != null && latestStrategy.StrategyConfidence <= _settings.NeedsInputFallbackConfidenceThreshold)
        {
            candidates.Add(BuildNeedsInputFallbackCase(scope, latestStrategy, latestMessage, now));
        }

        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new ScopeEvalResult();
        foreach (var candidate in candidates)
        {
            var key = BuildCaseKey(candidate.CaseType, candidate.SourceObjectId);
            seenKeys.Add(key);
            existingCases.TryGetValue(key, out var existing);
            var outcome = await ReconcileCaseAsync(scope, candidate, existing, now, ct);
            result.Created += outcome.Created;
            result.Updated += outcome.Updated;
            result.Reopened += outcome.Reopened;
            result.Suppressed += outcome.Suppressed;
        }

        foreach (var existing in existingCases.Values.Where(x => IsActiveStatus(x.Status) && !seenKeys.Contains(BuildCaseKey(x.CaseType, x.SourceObjectId))))
        {
            if (now - existing.UpdatedAt < TimeSpan.FromHours(Math.Max(1, _settings.StaleAfterNoSignalHours)))
            {
                continue;
            }

            await _stage6CaseRepository.UpsertAsync(CloneWith(existing, Stage6CaseStatuses.Stale, now, "suppressed: no active generator signal"), ct);
            result.Staled++;
        }

        return result;
    }

    private async Task<CaseWriteOutcome> ReconcileCaseAsync(
        CaseScopeRef scope,
        AutoCaseCandidate candidate,
        Stage6CaseRecord? existing,
        DateTime now,
        CancellationToken ct)
    {
        if (existing == null)
        {
            await _stage6CaseRepository.UpsertAsync(ToRecord(scope, candidate, now), ct);
            return CaseWriteOutcome.CreatedCase();
        }

        var existingClosedAt = existing.ResolvedAt ?? existing.RejectedAt ?? existing.StaleAt ?? existing.UpdatedAt;
        var evidenceAdvanced = candidate.LatestEvidenceAtUtc.HasValue && candidate.LatestEvidenceAtUtc.Value > existingClosedAt.AddMinutes(1);
        var priorityChanged = !string.Equals(existing.Priority, candidate.Priority, StringComparison.OrdinalIgnoreCase);
        var confidenceChanged = !existing.Confidence.HasValue
            || Math.Abs(existing.Confidence.Value - candidate.Confidence) >= 0.05f;
        var reasonChanged = !string.Equals(existing.ReasonSummary, candidate.ReasonSummary, StringComparison.Ordinal);
        var materialChange = evidenceAdvanced || priorityChanged || confidenceChanged || reasonChanged;
        var inCooldown = now - existing.UpdatedAt < TimeSpan.FromMinutes(Math.Max(1, _settings.CaseUpdateCooldownMinutes));

        if (IsClosedStatus(existing.Status))
        {
            if (!evidenceAdvanced)
            {
                return CaseWriteOutcome.SuppressedCase();
            }

            var reopened = ToRecord(scope, candidate, now);
            reopened.Id = existing.Id;
            await _stage6CaseRepository.UpsertAsync(reopened, ct);
            return CaseWriteOutcome.ReopenedCase();
        }

        if (!materialChange && inCooldown)
        {
            return CaseWriteOutcome.SuppressedCase();
        }

        var updated = ToRecord(scope, candidate, now);
        updated.Id = existing.Id;
        await _stage6CaseRepository.UpsertAsync(updated, ct);
        return CaseWriteOutcome.UpdatedCase();
    }

    private AutoCaseCandidate? BuildRiskCase(CaseScopeRef scope, DbStateSnapshot? latestState, DateTime now)
    {
        if (latestState == null)
        {
            return null;
        }

        var riskScore = Math.Max(latestState.AvoidanceRiskScore, latestState.ExternalPressureScore);
        var hasRiskRefs = !string.IsNullOrWhiteSpace(latestState.RiskRefsJson) && latestState.RiskRefsJson != "[]";
        if (riskScore < _settings.RiskImportantThreshold && !hasRiskRefs)
        {
            return null;
        }

        var priority = riskScore >= _settings.RiskBlockingThreshold ? "blocking" : "important";
        var reason = $"State risk signal: avoidance={latestState.AvoidanceRiskScore:0.00}, pressure={latestState.ExternalPressureScore:0.00}, responsiveness={latestState.ResponsivenessScore:0.00}.";
        var evidence = new List<string> { $"state_snapshot:{latestState.Id}" };
        if (latestState.SourceMessageId.HasValue)
        {
            evidence.Add($"message:{latestState.SourceMessageId.Value}");
        }

        return new AutoCaseCandidate
        {
            CaseType = Stage6CaseTypes.Risk,
            Status = Stage6CaseStatuses.Ready,
            Priority = priority,
            Confidence = Math.Clamp(riskScore, 0f, 1f),
            ReasonSummary = reason,
            EvidenceRefsJson = JsonSerializer.Serialize(evidence),
            SubjectRefsJson = JsonSerializer.Serialize(BuildSubjectRefs(scope)),
            TargetArtifactTypesJson = JsonSerializer.Serialize(new[] { Stage6ArtifactTypes.Strategy, Stage6ArtifactTypes.Draft, Stage6ArtifactTypes.Review }),
            ReopenTriggerRulesJson = """["risk_score_increase","new_conflict_evidence","operator_reopen"]""",
            ProvenanceJson = JsonSerializer.Serialize(new
            {
                generator = "stage6_auto_case_generation",
                rule = "risk_state_signal",
                generated_at_utc = now,
                state_snapshot_id = latestState.Id
            }),
            SourceObjectId = "risk:state_snapshot",
            LatestEvidenceAtUtc = latestState.CreatedAt
        };
    }

    private AutoCaseCandidate? BuildNeedsReviewCase(CaseScopeRef scope, DbStrategyRecord? latestStrategy, DateTime now)
    {
        if (latestStrategy == null || latestStrategy.StrategyConfidence > _settings.NeedsReviewMaxConfidenceThreshold)
        {
            return null;
        }

        var priority = latestStrategy.StrategyConfidence < 0.4f ? "important" : "optional";
        var reason = $"Strategy confidence is low ({latestStrategy.StrategyConfidence:0.00}) for goal '{latestStrategy.RecommendedGoal}'.";
        return new AutoCaseCandidate
        {
            CaseType = Stage6CaseTypes.NeedsReview,
            Status = Stage6CaseStatuses.Ready,
            Priority = priority,
            Confidence = Math.Clamp(1f - latestStrategy.StrategyConfidence, 0f, 1f),
            ReasonSummary = reason,
            EvidenceRefsJson = JsonSerializer.Serialize(new[] { $"strategy_record:{latestStrategy.Id}" }),
            SubjectRefsJson = JsonSerializer.Serialize(BuildSubjectRefs(scope)),
            TargetArtifactTypesJson = JsonSerializer.Serialize(new[] { Stage6ArtifactTypes.Strategy, Stage6ArtifactTypes.Review }),
            ReopenTriggerRulesJson = """["new_strategy_evidence","operator_reopen"]""",
            ProvenanceJson = JsonSerializer.Serialize(new
            {
                generator = "stage6_auto_case_generation",
                rule = "needs_review_low_strategy_confidence",
                generated_at_utc = now,
                strategy_record_id = latestStrategy.Id
            }),
            SourceObjectId = "needs_review:low_strategy_confidence",
            LatestEvidenceAtUtc = latestStrategy.CreatedAt
        };
    }

    private AutoCaseCandidate? BuildStateRefreshCase(
        CaseScopeRef scope,
        DbStage6Artifact? latestCurrentStateArtifact,
        DbStateSnapshot? latestState,
        IReadOnlyList<DbMessage> recentMessages,
        DateTime now)
    {
        var baseAt = latestCurrentStateArtifact?.GeneratedAt
                     ?? latestState?.CreatedAt
                     ?? DateTime.MinValue;
        var messagesSince = recentMessages.Count(x => x.Timestamp > baseAt);
        var isStaleByAge = baseAt != DateTime.MinValue && now - baseAt >= TimeSpan.FromHours(Math.Max(1, _settings.StateRefreshMinAgeHours));
        var isStale = latestCurrentStateArtifact?.IsStale == true || isStaleByAge;
        if (!isStale && messagesSince < _settings.MinMessagesForStateRefresh)
        {
            return null;
        }

        if (messagesSince < _settings.MinMessagesForStateRefresh)
        {
            return null;
        }

        var latestMessage = recentMessages.OrderByDescending(x => x.Timestamp).FirstOrDefault();
        var priority = isStale && messagesSince >= (_settings.MinMessagesForStateRefresh * 2) ? "blocking" : "important";
        var reason = latestMessage == null
            ? $"Current-state refresh needed: {messagesSince} new messages since last state baseline."
            : $"Current-state refresh needed: {messagesSince} new messages since baseline; latest message #{latestMessage.Id} at {latestMessage.Timestamp:yyyy-MM-dd HH:mm} UTC.";

        var evidence = new List<string>();
        if (latestCurrentStateArtifact != null)
        {
            evidence.Add($"artifact:{latestCurrentStateArtifact.Id}");
        }

        if (latestState != null)
        {
            evidence.Add($"state_snapshot:{latestState.Id}");
        }

        if (latestMessage != null)
        {
            evidence.Add($"message:{latestMessage.Id}");
        }

        return new AutoCaseCandidate
        {
            CaseType = Stage6CaseTypes.StateRefreshNeeded,
            Status = Stage6CaseStatuses.Ready,
            Priority = priority,
            Confidence = Math.Clamp(messagesSince / (float)Math.Max(1, _settings.MinMessagesForStateRefresh * 3), 0f, 1f),
            ReasonSummary = reason,
            EvidenceRefsJson = JsonSerializer.Serialize(evidence),
            SubjectRefsJson = JsonSerializer.Serialize(BuildSubjectRefs(scope)),
            TargetArtifactTypesJson = JsonSerializer.Serialize(new[] { Stage6ArtifactTypes.CurrentState, Stage6ArtifactTypes.Strategy, Stage6ArtifactTypes.Dossier }),
            ReopenTriggerRulesJson = """["new_message_delta","artifact_marked_stale","operator_reopen"]""",
            ProvenanceJson = JsonSerializer.Serialize(new
            {
                generator = "stage6_auto_case_generation",
                rule = "state_refresh_message_delta",
                generated_at_utc = now,
                new_messages = messagesSince
            }),
            SourceObjectId = "state_refresh:current_state",
            LatestEvidenceAtUtc = latestMessage?.Timestamp ?? latestState?.CreatedAt
        };
    }

    private AutoCaseCandidate? BuildDossierCandidateCase(
        CaseScopeRef scope,
        DbStage6Artifact? latestDossierArtifact,
        IReadOnlyList<DbMessage> recentMessages,
        DateTime now)
    {
        var baseline = latestDossierArtifact?.GeneratedAt ?? DateTime.MinValue;
        var messagesSince = recentMessages.Count(x => x.Timestamp > baseline);
        if (messagesSince < _settings.MinMessagesForDossierCandidate)
        {
            return null;
        }

        if (latestDossierArtifact != null && now - latestDossierArtifact.GeneratedAt < TimeSpan.FromHours(Math.Max(1, _settings.DossierCandidateMinAgeHours)))
        {
            return null;
        }

        var latestMessage = recentMessages.OrderByDescending(x => x.Timestamp).FirstOrDefault();
        var priority = messagesSince >= (_settings.MinMessagesForDossierCandidate * 2) ? "important" : "optional";
        var reason = latestMessage == null
            ? $"Dossier candidate: {messagesSince} new evidence messages."
            : $"Dossier candidate: {messagesSince} new evidence messages; latest #{latestMessage.Id} at {latestMessage.Timestamp:yyyy-MM-dd HH:mm} UTC.";

        return new AutoCaseCandidate
        {
            CaseType = Stage6CaseTypes.DossierCandidate,
            Status = Stage6CaseStatuses.Ready,
            Priority = priority,
            Confidence = Math.Clamp(messagesSince / (float)Math.Max(1, _settings.MinMessagesForDossierCandidate * 2), 0f, 1f),
            ReasonSummary = reason,
            EvidenceRefsJson = latestMessage == null
                ? "[]"
                : JsonSerializer.Serialize(new[] { $"message:{latestMessage.Id}" }),
            SubjectRefsJson = JsonSerializer.Serialize(BuildSubjectRefs(scope)),
            TargetArtifactTypesJson = JsonSerializer.Serialize(new[] { Stage6ArtifactTypes.Dossier }),
            ReopenTriggerRulesJson = """["new_material_evidence","operator_reopen"]""",
            ProvenanceJson = JsonSerializer.Serialize(new
            {
                generator = "stage6_auto_case_generation",
                rule = "dossier_material_change",
                generated_at_utc = now,
                new_messages = messagesSince
            }),
            SourceObjectId = "dossier_candidate:material_change",
            LatestEvidenceAtUtc = latestMessage?.Timestamp
        };
    }

    private AutoCaseCandidate? BuildDraftCandidateCase(
        CaseScopeRef scope,
        DbStrategyRecord? latestStrategy,
        DbStage6Artifact? latestDraftArtifact,
        DateTime? latestDraftOutcomeAt,
        IReadOnlyList<DbMessage> recentMessages,
        DateTime now)
    {
        if (latestStrategy == null)
        {
            return null;
        }

        if (now - latestStrategy.CreatedAt > TimeSpan.FromHours(Math.Max(1, _settings.DraftCandidatePendingHours)))
        {
            return null;
        }

        var latestDraftAt = latestDraftArtifact?.GeneratedAt;
        if (latestDraftAt.HasValue && latestDraftAt.Value >= latestStrategy.CreatedAt)
        {
            return null;
        }

        if (latestDraftOutcomeAt.HasValue && latestDraftOutcomeAt.Value >= latestStrategy.CreatedAt)
        {
            return null;
        }

        var messagesSinceStrategy = recentMessages.Count(x => x.Timestamp > latestStrategy.CreatedAt);
        if (messagesSinceStrategy < _settings.MinMessagesForDraftCandidate)
        {
            return null;
        }

        var priority = latestStrategy.StrategyConfidence < _settings.NextStepBlockedConfidenceThreshold
            ? "blocking"
            : "important";
        var reason = $"Draft candidate: strategy '{latestStrategy.RecommendedGoal}' has no fresh draft outcome and {messagesSinceStrategy} new messages arrived.";
        return new AutoCaseCandidate
        {
            CaseType = Stage6CaseTypes.DraftCandidate,
            Status = Stage6CaseStatuses.Ready,
            Priority = priority,
            Confidence = Math.Clamp(1f - latestStrategy.StrategyConfidence, 0f, 1f),
            ReasonSummary = reason,
            EvidenceRefsJson = JsonSerializer.Serialize(new[] { $"strategy_record:{latestStrategy.Id}" }),
            SubjectRefsJson = JsonSerializer.Serialize(BuildSubjectRefs(scope)),
            TargetArtifactTypesJson = JsonSerializer.Serialize(new[] { Stage6ArtifactTypes.Draft, Stage6ArtifactTypes.Review }),
            ReopenTriggerRulesJson = """["new_strategy_record","new_message_delta","operator_reopen"]""",
            ProvenanceJson = JsonSerializer.Serialize(new
            {
                generator = "stage6_auto_case_generation",
                rule = "draft_candidate_pending_after_strategy",
                generated_at_utc = now,
                messages_since_strategy = messagesSinceStrategy
            }),
            SourceObjectId = "draft_candidate:pending_after_strategy",
            LatestEvidenceAtUtc = recentMessages.OrderByDescending(x => x.Timestamp).Select(x => (DateTime?)x.Timestamp).FirstOrDefault() ?? latestStrategy.CreatedAt
        };
    }

    private List<AutoCaseCandidate> BuildClarificationCases(
        CaseScopeRef scope,
        DbStateSnapshot? latestState,
        DbStrategyRecord? latestStrategy,
        DbPeriod? openPeriod,
        DbMessage? latestMessage,
        IReadOnlyList<SenderRef> senders,
        DateTime now)
    {
        var result = new List<AutoCaseCandidate>();
        if (latestMessage == null)
        {
            return result;
        }

        if (latestState == null || !latestState.SourceMessageId.HasValue || (openPeriod != null && openPeriod.OpenQuestionsCount > 0))
        {
            result.Add(new AutoCaseCandidate
            {
                CaseType = Stage6CaseTypes.ClarificationMissingData,
                CaseSubtype = "date_gap",
                ClarificationKind = "missing_data",
                Status = Stage6CaseStatuses.NeedsUserInput,
                Priority = "important",
                Confidence = 0.62f,
                ReasonSummary = $"Missing temporal anchoring for message #{latestMessage.Id}.",
                QuestionText = $"Уточните дату/период для сигнала из сообщения #{latestMessage.Id} от {latestMessage.Timestamp:yyyy-MM-dd HH:mm} UTC.",
                ResponseMode = "free_text",
                ResponseChannelHint = "bot_or_web",
                EvidenceRefsJson = JsonSerializer.Serialize(new[] { $"message:{latestMessage.Id}" }),
                SubjectRefsJson = JsonSerializer.Serialize(BuildSubjectRefs(scope)),
                TargetArtifactTypesJson = JsonSerializer.Serialize(new[] { Stage6ArtifactTypes.ClarificationState, Stage6ArtifactTypes.CurrentState, Stage6ArtifactTypes.Dossier }),
                ReopenTriggerRulesJson = """["new_evidence","operator_correction","artifact_stale_after_context_change"]""",
                ProvenanceJson = JsonSerializer.Serialize(new
                {
                    generator = "stage6_auto_case_generation",
                    rule = "clarification_date_gap",
                    generated_at_utc = now,
                    message_id = latestMessage.Id
                }),
                SourceObjectId = "clarification:missing_data:date_gap",
                LatestEvidenceAtUtc = latestMessage.Timestamp
            });
        }

        if (latestState != null
            && latestState.AmbiguityScore >= _settings.AmbiguityClarificationThreshold
            && senders.Count >= 2)
        {
            var senderLabels = string.Join(" / ", senders.Take(2).Select(x => x.Name));
            result.Add(new AutoCaseCandidate
            {
                CaseType = Stage6CaseTypes.ClarificationAmbiguity,
                CaseSubtype = "people_gap",
                ClarificationKind = "ambiguity",
                Status = Stage6CaseStatuses.NeedsUserInput,
                Priority = "important",
                Confidence = Math.Clamp(latestState.AmbiguityScore, 0f, 1f),
                ReasonSummary = $"Ambiguity remains between participants ({senderLabels}).",
                QuestionText = $"Кого именно касается сигнал из сообщения #{latestMessage.Id}: {senderLabels}?",
                ResponseMode = "single_or_free_text",
                ResponseChannelHint = "bot_or_web",
                EvidenceRefsJson = JsonSerializer.Serialize(new[] { $"message:{latestMessage.Id}", $"state_snapshot:{latestState.Id}" }),
                SubjectRefsJson = JsonSerializer.Serialize(BuildSubjectRefs(scope).Concat(senders.Take(2).Select(x => $"sender:{x.Id}")).ToArray()),
                TargetArtifactTypesJson = JsonSerializer.Serialize(new[] { Stage6ArtifactTypes.ClarificationState, Stage6ArtifactTypes.CurrentState, Stage6ArtifactTypes.Strategy }),
                ReopenTriggerRulesJson = """["new_evidence","operator_correction","artifact_stale_after_context_change"]""",
                ProvenanceJson = JsonSerializer.Serialize(new
                {
                    generator = "stage6_auto_case_generation",
                    rule = "clarification_people_gap",
                    generated_at_utc = now,
                    message_id = latestMessage.Id,
                    participants = senders.Take(2).Select(x => x.Name).ToArray()
                }),
                SourceObjectId = "clarification:ambiguity:people_gap",
                LatestEvidenceAtUtc = latestMessage.Timestamp
            });
        }

        if (latestState != null
            && !string.IsNullOrWhiteSpace(latestState.AlternativeStatus)
            && !string.Equals(latestState.AlternativeStatus, latestState.RelationshipStatus, StringComparison.OrdinalIgnoreCase)
            && (latestState.Confidence <= _settings.EvidenceConflictConfidenceThreshold || latestState.AmbiguityScore >= _settings.AmbiguityClarificationThreshold))
        {
            result.Add(new AutoCaseCandidate
            {
                CaseType = Stage6CaseTypes.ClarificationEvidenceInterpretationConflict,
                CaseSubtype = "state_conflict",
                ClarificationKind = "evidence_interpretation_conflict",
                Status = Stage6CaseStatuses.NeedsUserInput,
                Priority = "blocking",
                Confidence = Math.Clamp(1f - latestState.Confidence, 0f, 1f),
                ReasonSummary = $"Evidence and interpretation conflict: '{latestState.RelationshipStatus}' vs '{latestState.AlternativeStatus}'.",
                QuestionText = $"Есть конфликт интерпретации по сообщению #{latestMessage.Id}: подтвердите, ближе статус '{latestState.RelationshipStatus}' или '{latestState.AlternativeStatus}'?",
                ResponseMode = "single_or_free_text",
                ResponseChannelHint = "bot_or_web",
                EvidenceRefsJson = JsonSerializer.Serialize(new[] { $"state_snapshot:{latestState.Id}", $"message:{latestMessage.Id}" }),
                SubjectRefsJson = JsonSerializer.Serialize(BuildSubjectRefs(scope)),
                TargetArtifactTypesJson = JsonSerializer.Serialize(new[] { Stage6ArtifactTypes.ClarificationState, Stage6ArtifactTypes.CurrentState, Stage6ArtifactTypes.Strategy }),
                ReopenTriggerRulesJson = """["new_evidence","operator_correction","artifact_stale_after_context_change"]""",
                ProvenanceJson = JsonSerializer.Serialize(new
                {
                    generator = "stage6_auto_case_generation",
                    rule = "clarification_evidence_interpretation_conflict",
                    generated_at_utc = now,
                    state_snapshot_id = latestState.Id
                }),
                SourceObjectId = "clarification:evidence_conflict:state_status",
                LatestEvidenceAtUtc = latestState.CreatedAt
            });
        }

        if (latestStrategy != null && latestStrategy.StrategyConfidence <= _settings.NextStepBlockedConfidenceThreshold)
        {
            result.Add(new AutoCaseCandidate
            {
                CaseType = Stage6CaseTypes.ClarificationNextStepBlocked,
                CaseSubtype = "strategy_blocked",
                ClarificationKind = "next_step_blocked",
                Status = Stage6CaseStatuses.NeedsUserInput,
                Priority = "blocking",
                Confidence = Math.Clamp(1f - latestStrategy.StrategyConfidence, 0f, 1f),
                ReasonSummary = $"Next step is blocked: strategy confidence {latestStrategy.StrategyConfidence:0.00}.",
                QuestionText = $"Какой следующий шаг приоритетен после сообщения #{latestMessage.Id} ({latestMessage.Timestamp:yyyy-MM-dd HH:mm} UTC): подтвердить цель '{latestStrategy.RecommendedGoal}' или скорректировать её?",
                ResponseMode = "single_or_free_text",
                ResponseChannelHint = "bot_or_web",
                EvidenceRefsJson = JsonSerializer.Serialize(new[] { $"strategy_record:{latestStrategy.Id}", $"message:{latestMessage.Id}" }),
                SubjectRefsJson = JsonSerializer.Serialize(BuildSubjectRefs(scope)),
                TargetArtifactTypesJson = JsonSerializer.Serialize(new[] { Stage6ArtifactTypes.ClarificationState, Stage6ArtifactTypes.Strategy, Stage6ArtifactTypes.Draft }),
                ReopenTriggerRulesJson = """["new_evidence","operator_correction","artifact_stale_after_context_change"]""",
                ProvenanceJson = JsonSerializer.Serialize(new
                {
                    generator = "stage6_auto_case_generation",
                    rule = "clarification_next_step_blocked",
                    generated_at_utc = now,
                    strategy_record_id = latestStrategy.Id
                }),
                SourceObjectId = "clarification:next_step_blocked:strategy",
                LatestEvidenceAtUtc = latestStrategy.CreatedAt
            });
        }

        return result;
    }

    private AutoCaseCandidate BuildNeedsInputFallbackCase(
        CaseScopeRef scope,
        DbStrategyRecord strategy,
        DbMessage? latestMessage,
        DateTime now)
    {
        var reason = $"Operator input needed: strategy '{strategy.RecommendedGoal}' confidence is {strategy.StrategyConfidence:0.00}.";
        var question = latestMessage == null
            ? "Нужна конкретизация следующего шага перед генерацией обновлённой стратегии."
            : $"Нужна конкретизация цели после сообщения #{latestMessage.Id} ({latestMessage.Timestamp:yyyy-MM-dd HH:mm} UTC).";
        var evidence = latestMessage == null
            ? new[] { $"strategy_record:{strategy.Id}" }
            : new[] { $"strategy_record:{strategy.Id}", $"message:{latestMessage.Id}" };
        return new AutoCaseCandidate
        {
            CaseType = Stage6CaseTypes.NeedsInput,
            CaseSubtype = "strategy_alignment",
            ClarificationKind = "next_step_blocked",
            Status = Stage6CaseStatuses.NeedsUserInput,
            Priority = "important",
            Confidence = Math.Clamp(1f - strategy.StrategyConfidence, 0f, 1f),
            ReasonSummary = reason,
            QuestionText = question,
            ResponseMode = "free_text",
            ResponseChannelHint = "bot_or_web",
            EvidenceRefsJson = JsonSerializer.Serialize(evidence),
            SubjectRefsJson = JsonSerializer.Serialize(BuildSubjectRefs(scope)),
            TargetArtifactTypesJson = JsonSerializer.Serialize(new[] { Stage6ArtifactTypes.ClarificationState, Stage6ArtifactTypes.Strategy }),
            ReopenTriggerRulesJson = """["new_evidence","operator_correction","artifact_stale_after_context_change"]""",
            ProvenanceJson = JsonSerializer.Serialize(new
            {
                generator = "stage6_auto_case_generation",
                rule = "needs_input_strategy_alignment",
                generated_at_utc = now,
                strategy_record_id = strategy.Id
            }),
            SourceObjectId = "needs_input:strategy_alignment",
            LatestEvidenceAtUtc = strategy.CreatedAt
        };
    }

    private static Stage6CaseRecord ToRecord(CaseScopeRef scope, AutoCaseCandidate candidate, DateTime now)
    {
        return new Stage6CaseRecord
        {
            ScopeCaseId = scope.CaseId,
            ChatId = scope.ChatId,
            ScopeType = "chat",
            CaseType = candidate.CaseType,
            CaseSubtype = candidate.CaseSubtype,
            Status = candidate.Status,
            Priority = candidate.Priority,
            Confidence = Math.Clamp(candidate.Confidence, 0f, 1f),
            ReasonSummary = candidate.ReasonSummary,
            ClarificationKind = candidate.ClarificationKind,
            QuestionText = candidate.QuestionText,
            ResponseMode = candidate.ResponseMode,
            ResponseChannelHint = candidate.ResponseChannelHint,
            EvidenceRefsJson = candidate.EvidenceRefsJson,
            SubjectRefsJson = candidate.SubjectRefsJson,
            TargetArtifactTypesJson = candidate.TargetArtifactTypesJson,
            ReopenTriggerRulesJson = candidate.ReopenTriggerRulesJson,
            ProvenanceJson = candidate.ProvenanceJson,
            SourceObjectType = AutoCaseSourceType,
            SourceObjectId = candidate.SourceObjectId,
            UpdatedAt = now
        };
    }

    private static Stage6CaseRecord CloneWith(Stage6CaseRecord existing, string status, DateTime now, string reason)
    {
        return new Stage6CaseRecord
        {
            Id = existing.Id,
            ScopeCaseId = existing.ScopeCaseId,
            ChatId = existing.ChatId,
            ScopeType = existing.ScopeType,
            CaseType = existing.CaseType,
            CaseSubtype = existing.CaseSubtype,
            Status = status,
            Priority = existing.Priority,
            Confidence = existing.Confidence,
            ReasonSummary = $"{existing.ReasonSummary} ({reason})",
            ClarificationKind = existing.ClarificationKind,
            QuestionText = existing.QuestionText,
            ResponseMode = existing.ResponseMode,
            ResponseChannelHint = existing.ResponseChannelHint,
            EvidenceRefsJson = existing.EvidenceRefsJson,
            SubjectRefsJson = existing.SubjectRefsJson,
            TargetArtifactTypesJson = existing.TargetArtifactTypesJson,
            ReopenTriggerRulesJson = existing.ReopenTriggerRulesJson,
            ProvenanceJson = existing.ProvenanceJson,
            SourceObjectType = existing.SourceObjectType,
            SourceObjectId = existing.SourceObjectId,
            CreatedAt = existing.CreatedAt,
            UpdatedAt = now,
            ReadyAt = existing.ReadyAt,
            ResolvedAt = existing.ResolvedAt,
            RejectedAt = existing.RejectedAt,
            StaleAt = status == Stage6CaseStatuses.Stale ? now : existing.StaleAt
        };
    }

    private static string BuildCaseKey(string caseType, string sourceObjectId)
        => $"{caseType}|{sourceObjectId}";

    private static bool IsActiveStatus(string status)
        => status is Stage6CaseStatuses.New or Stage6CaseStatuses.Ready or Stage6CaseStatuses.NeedsUserInput;

    private static bool IsClosedStatus(string status)
        => status is Stage6CaseStatuses.Resolved or Stage6CaseStatuses.Rejected or Stage6CaseStatuses.Stale;

    private static void AddIfNotNull(ICollection<AutoCaseCandidate> list, AutoCaseCandidate? candidate)
    {
        if (candidate != null)
        {
            list.Add(candidate);
        }
    }

    private static List<string> BuildSubjectRefs(CaseScopeRef scope)
    {
        var refs = new List<string> { $"case:{scope.CaseId}" };
        if (scope.ChatId.HasValue)
        {
            refs.Add($"chat:{scope.ChatId.Value}");
        }

        return refs;
    }

    private const string AutoCaseSourceType = "auto_case_rule";
}

public class AutoCaseGenerationRunResult
{
    public bool IsSkipped { get; set; }
    public int ScopesConsidered { get; set; }
    public int Created { get; set; }
    public int Updated { get; set; }
    public int Reopened { get; set; }
    public int Staled { get; set; }
    public int Suppressed { get; set; }
}

internal sealed record CaseScopeRef(long CaseId, long? ChatId, DateTime ActivityAt);
internal sealed record SenderRef(long Id, string Name);

internal sealed class AutoCaseCandidate
{
    public string CaseType { get; init; } = Stage6CaseTypes.NeedsReview;
    public string? CaseSubtype { get; init; }
    public string Status { get; init; } = Stage6CaseStatuses.Ready;
    public string Priority { get; init; } = "important";
    public float Confidence { get; init; }
    public string ReasonSummary { get; init; } = string.Empty;
    public string? ClarificationKind { get; init; }
    public string? QuestionText { get; init; }
    public string? ResponseMode { get; init; }
    public string? ResponseChannelHint { get; init; }
    public string EvidenceRefsJson { get; init; } = "[]";
    public string SubjectRefsJson { get; init; } = "[]";
    public string TargetArtifactTypesJson { get; init; } = "[]";
    public string ReopenTriggerRulesJson { get; init; } = "[]";
    public string ProvenanceJson { get; init; } = "{}";
    public string SourceObjectId { get; init; } = string.Empty;
    public DateTime? LatestEvidenceAtUtc { get; init; }
}

internal sealed class ScopeEvalResult
{
    public int Created { get; set; }
    public int Updated { get; set; }
    public int Reopened { get; set; }
    public int Staled { get; set; }
    public int Suppressed { get; set; }
}

internal sealed class CaseWriteOutcome
{
    public int Created { get; private init; }
    public int Updated { get; private init; }
    public int Reopened { get; private init; }
    public int Suppressed { get; private init; }

    public static CaseWriteOutcome CreatedCase() => new() { Created = 1 };
    public static CaseWriteOutcome UpdatedCase() => new() { Updated = 1 };
    public static CaseWriteOutcome ReopenedCase() => new() { Reopened = 1 };
    public static CaseWriteOutcome SuppressedCase() => new() { Suppressed = 1 };
}
