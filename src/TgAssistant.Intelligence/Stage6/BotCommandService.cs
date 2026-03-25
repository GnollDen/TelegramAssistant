using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage6;

public interface IBotCommandService
{
    Task<(bool Handled, string Reply)> TryHandleAsync(
        string userMessage,
        long? transportChatId,
        long? sourceMessageId,
        long? senderId,
        CancellationToken ct = default);
}

public class BotCommandService : IBotCommandService
{
    private readonly BotChatSettings _botSettings;
    private readonly TelegramSettings _telegramSettings;
    private readonly ICurrentStateEngine _currentStateEngine;
    private readonly IStrategyEngine _strategyEngine;
    private readonly IDraftEngine _draftEngine;
    private readonly IDraftReviewEngine _draftReviewEngine;
    private readonly IClarificationOrchestrator _clarificationOrchestrator;
    private readonly IPeriodRepository _periodRepository;
    private readonly IPeriodizationService _periodizationService;
    private readonly IOfflineEventRepository _offlineEventRepository;
    private readonly IStateProfileRepository _stateProfileRepository;
    private readonly IStrategyDraftRepository _strategyDraftRepository;
    private readonly IStage6ArtifactRepository _stage6ArtifactRepository;
    private readonly IStage6ArtifactFreshnessService _stage6ArtifactFreshnessService;
    private readonly IStage6CaseRepository _stage6CaseRepository;
    private readonly IClarificationRepository _clarificationRepository;
    private readonly IStage6UserContextRepository _stage6UserContextRepository;
    private readonly IDomainReviewEventRepository _domainReviewEventRepository;
    private readonly ILogger<BotCommandService> _logger;

    public BotCommandService(
        IOptions<BotChatSettings> botSettings,
        IOptions<TelegramSettings> telegramSettings,
        ICurrentStateEngine currentStateEngine,
        IStrategyEngine strategyEngine,
        IDraftEngine draftEngine,
        IDraftReviewEngine draftReviewEngine,
        IClarificationOrchestrator clarificationOrchestrator,
        IPeriodRepository periodRepository,
        IPeriodizationService periodizationService,
        IOfflineEventRepository offlineEventRepository,
        IStateProfileRepository stateProfileRepository,
        IStrategyDraftRepository strategyDraftRepository,
        IStage6ArtifactRepository stage6ArtifactRepository,
        IStage6ArtifactFreshnessService stage6ArtifactFreshnessService,
        IStage6CaseRepository stage6CaseRepository,
        IClarificationRepository clarificationRepository,
        IStage6UserContextRepository stage6UserContextRepository,
        IDomainReviewEventRepository domainReviewEventRepository,
        ILogger<BotCommandService> logger)
    {
        _botSettings = botSettings.Value;
        _telegramSettings = telegramSettings.Value;
        _currentStateEngine = currentStateEngine;
        _strategyEngine = strategyEngine;
        _draftEngine = draftEngine;
        _draftReviewEngine = draftReviewEngine;
        _clarificationOrchestrator = clarificationOrchestrator;
        _periodRepository = periodRepository;
        _periodizationService = periodizationService;
        _offlineEventRepository = offlineEventRepository;
        _stateProfileRepository = stateProfileRepository;
        _strategyDraftRepository = strategyDraftRepository;
        _stage6ArtifactRepository = stage6ArtifactRepository;
        _stage6ArtifactFreshnessService = stage6ArtifactFreshnessService;
        _stage6CaseRepository = stage6CaseRepository;
        _clarificationRepository = clarificationRepository;
        _stage6UserContextRepository = stage6UserContextRepository;
        _domainReviewEventRepository = domainReviewEventRepository;
        _logger = logger;
    }

    public async Task<(bool Handled, string Reply)> TryHandleAsync(
        string userMessage,
        long? transportChatId,
        long? sourceMessageId,
        long? senderId,
        CancellationToken ct = default)
    {
        var normalized = (userMessage ?? string.Empty).Trim();
        if (!normalized.StartsWith('/'))
        {
            return (false, string.Empty);
        }

        var (command, args) = SplitCommand(normalized);
        try
        {
            return command switch
            {
                "/state" => (true, await HandleStateAsync(args, transportChatId, sourceMessageId, senderId, ct)),
                "/next" => (true, await HandleNextAsync(args, transportChatId, sourceMessageId, senderId, ct)),
                "/draft" => (true, await HandleDraftAsync(args, transportChatId, sourceMessageId, senderId, ct)),
                "/review" => (true, await HandleReviewAsync(args, transportChatId, sourceMessageId, senderId, ct)),
                "/cases" => (true, await HandleCasesAsync(args, transportChatId, ct)),
                "/case" => (true, await HandleCaseAsync(args, transportChatId, ct)),
                "/gaps" => (true, await HandleGapsAsync(args, transportChatId, sourceMessageId, senderId, ct)),
                "/answer" => (true, await HandleAnswerAsync(args, transportChatId, sourceMessageId, senderId, ct)),
                "/resolve" => (true, await HandleCaseStatusActionAsync(args, transportChatId, sourceMessageId, senderId, Stage6CaseStatuses.Resolved, "resolve", ct)),
                "/reject" => (true, await HandleCaseStatusActionAsync(args, transportChatId, sourceMessageId, senderId, Stage6CaseStatuses.Rejected, "reject", ct)),
                "/refresh" => (true, await HandleCaseStatusActionAsync(args, transportChatId, sourceMessageId, senderId, Stage6CaseStatuses.Ready, "refresh", ct)),
                "/annotate" => (true, await HandleAnnotateAsync(args, transportChatId, sourceMessageId, senderId, ct)),
                "/timeline" => (true, await HandleTimelineAsync(args, transportChatId, sourceMessageId, senderId, ct)),
                "/offline" => (true, await HandleOfflineAsync(args, transportChatId, sourceMessageId, senderId, ct)),
                "/help" => (true, BuildHelp()),
                _ => (false, string.Empty)
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Bot command failed. command={Command}", command);
            return (true, $"Command failed: {command}. {ex.Message}");
        }
    }

    private async Task<string> HandleStateAsync(string args, long? transportChatId, long? sourceMessageId, long? senderId, CancellationToken ct)
    {
        var (scope, _, scopeError) = ResolveScopeFromArgs(args, transportChatId);
        if (scope == null)
        {
            return scopeError;
        }

        var stateSnapshot = await TryGetReusableCurrentStateSnapshotAsync(scope.Value.CaseId, scope.Value.ChatId, ct);
        if (stateSnapshot == null)
        {
            stateSnapshot = (await _currentStateEngine.ComputeAsync(new CurrentStateRequest
            {
                CaseId = scope.Value.CaseId,
                ChatId = scope.Value.ChatId,
                Actor = BuildActor(senderId),
                SourceType = "telegram_command",
                SourceId = "/state",
                Persist = true
            }, ct)).Snapshot;
        }

        var strategy = await TryGetReusableStrategyAsync(scope.Value.CaseId, scope.Value.ChatId, ct)
            ?? await _strategyEngine.RunAsync(new StrategyEngineRequest
            {
                CaseId = scope.Value.CaseId,
                ChatId = scope.Value.ChatId,
                Actor = BuildActor(senderId),
                SourceType = "telegram_command",
                SourceId = "/state-next",
                Persist = true
            }, ct);

        var primary = strategy.Options.FirstOrDefault(x => x.IsPrimary) ?? strategy.Options.FirstOrDefault();
        var signals = ParseJsonList(stateSnapshot.KeySignalRefsJson, 3);
        var risks = ParseJsonList(stateSnapshot.RiskRefsJson, 3);

        var sb = new StringBuilder();
        sb.AppendLine($"state: {stateSnapshot.DynamicLabel} (conf {stateSnapshot.Confidence:0.00})");
        if (!string.IsNullOrWhiteSpace(stateSnapshot.AlternativeStatus))
        {
            sb.AppendLine($"status: {stateSnapshot.RelationshipStatus} | alt {stateSnapshot.AlternativeStatus}");
        }
        else
        {
            sb.AppendLine($"status: {stateSnapshot.RelationshipStatus}");
        }

        sb.AppendLine($"signals: {(signals.Count == 0 ? "limited evidence" : string.Join(", ", signals))}");
        sb.AppendLine($"risk: {(risks.Count == 0 ? "ambiguity remains" : string.Join(", ", risks))}");

        var next = string.IsNullOrWhiteSpace(strategy.MicroStep)
            ? primary?.Summary ?? "run /gaps and answer one blocking question"
            : strategy.MicroStep;
        sb.AppendLine($"next: {next}");

        var doNot = BuildDoNot(primary);
        sb.AppendLine($"do not: {doNot}");

        if (stateSnapshot.AmbiguityScore > 0.65f)
        {
            sb.AppendLine("note: ambiguity high, prefer softer moves and clarifications.");
        }

        return sb.ToString().Trim();
    }

    private async Task<string> HandleNextAsync(string args, long? transportChatId, long? sourceMessageId, long? senderId, CancellationToken ct)
    {
        var (scope, _, scopeError) = ResolveScopeFromArgs(args, transportChatId);
        if (scope == null)
        {
            return scopeError;
        }

        var result = await TryGetReusableStrategyAsync(scope.Value.CaseId, scope.Value.ChatId, ct)
            ?? await _strategyEngine.RunAsync(new StrategyEngineRequest
            {
                CaseId = scope.Value.CaseId,
                ChatId = scope.Value.ChatId,
                Actor = BuildActor(senderId),
                SourceType = "telegram_command",
                SourceId = "/next",
                Persist = true
            }, ct);

        var primary = result.Options.FirstOrDefault(x => x.IsPrimary) ?? result.Options.FirstOrDefault();
        if (primary == null)
        {
            return "No strategy options available yet. Try /gaps first.";
        }

        var sb = new StringBuilder();
        sb.AppendLine($"next: {primary.ActionType} — {primary.Summary}");
        sb.AppendLine($"purpose: {primary.Purpose}");
        sb.AppendLine($"risk: {ShortRisk(primary.Risk)}");
        sb.AppendLine($"ethics: {EthicsNote(primary.Risk)}");
        sb.AppendLine($"when: {primary.WhenToUse}");
        sb.AppendLine($"micro-step: {result.MicroStep}");

        var alternatives = result.Options.Where(x => !x.IsPrimary).Take(2).ToList();
        if (alternatives.Count > 0)
        {
            sb.AppendLine($"alternatives: {string.Join(" | ", alternatives.Select(x => $"{x.ActionType}: {x.Summary}"))}");
        }

        sb.AppendLine($"confidence: {result.Confidence.Confidence:0.00} ({(result.Confidence.HighUncertainty ? "high uncertainty" : "stable")})");
        return sb.ToString().Trim();
    }

    private async Task<string> HandleDraftAsync(string args, long? transportChatId, long? sourceMessageId, long? senderId, CancellationToken ct)
    {
        var (scope, commandArgs, scopeError) = ResolveScopeFromArgs(args, transportChatId);
        if (scope == null)
        {
            return scopeError;
        }

        var (tone, notes) = ParseDraftArgs(commandArgs);
        DraftEngineResult? result = null;
        if (string.IsNullOrWhiteSpace(tone) && string.IsNullOrWhiteSpace(notes))
        {
            var reusedDraft = await TryGetReusableDraftAsync(scope.Value.CaseId, scope.Value.ChatId, ct);
            if (reusedDraft != null)
            {
                result = new DraftEngineResult { Record = reusedDraft };
            }
        }

        result ??= await _draftEngine.RunAsync(new DraftEngineRequest
        {
            CaseId = scope.Value.CaseId,
            ChatId = scope.Value.ChatId,
            DesiredTone = tone,
            UserNotes = notes,
            Actor = BuildActor(senderId),
            SourceType = "telegram_command",
            SourceId = "/draft",
            Persist = true
        }, ct);

        var sb = new StringBuilder();
        sb.AppendLine($"main: {result.Record.MainDraft}");
        sb.AppendLine($"softer alternative: {result.Record.AltDraft1 ?? "-"}");
        sb.AppendLine($"more direct alternative: {result.Record.AltDraft2 ?? "-"}");
        sb.AppendLine($"why: strategy-linked draft (conf {result.Record.Confidence:0.00})");
        if (result.HasIntentConflict)
        {
            sb.AppendLine($"note: safer main kept due to conflict ({result.ConflictReason}).");
        }

        return sb.ToString().Trim();
    }

    private async Task<string> HandleReviewAsync(string args, long? transportChatId, long? sourceMessageId, long? senderId, CancellationToken ct)
    {
        var (scope, commandArgs, scopeError) = ResolveScopeFromArgs(args, transportChatId);
        if (scope == null)
        {
            return scopeError;
        }

        var candidate = commandArgs.Trim();
        Guid? draftRecordId = null;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            var latestDraft = await GetLatestDraftAsync(scope.Value.CaseId, scope.Value.ChatId, ct);
            if (latestDraft == null)
            {
                return "No candidate text found. Use /review <text> or create /draft first.";
            }

            draftRecordId = latestDraft.Id;
        }

        DraftReviewResult? result = null;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            result = await TryGetReusableReviewAsync(scope.Value.CaseId, scope.Value.ChatId, ct);
        }

        result ??= await _draftReviewEngine.RunAsync(new DraftReviewRequest
        {
            CaseId = scope.Value.CaseId,
            ChatId = scope.Value.ChatId,
            CandidateText = string.IsNullOrWhiteSpace(candidate) ? null : candidate,
            DraftRecordId = draftRecordId,
            Actor = BuildActor(senderId),
            SourceType = "telegram_command",
            SourceId = "/review",
            Persist = true
        }, ct);

        var sb = new StringBuilder();
        sb.AppendLine($"assessment: {result.Assessment}");
        sb.AppendLine($"risks: {(result.MainRisks.Count == 0 ? "none material" : string.Join("; ", result.MainRisks))}");
        sb.AppendLine($"labels: {(result.RiskLabels.Count == 0 ? "n/a" : string.Join(", ", result.RiskLabels))}");
        sb.AppendLine($"safer: {result.SaferRewrite}");
        sb.AppendLine($"more natural: {result.NaturalRewrite}");
        if (result.StrategyConflictDetected)
        {
            sb.AppendLine($"note: strategy conflict detected ({result.StrategyConflictNote}).");
        }

        return sb.ToString().Trim();
    }

    private async Task<string> HandleCasesAsync(string args, long? transportChatId, CancellationToken ct)
    {
        var (scope, commandArgs, scopeError) = ResolveScopeFromArgs(args, transportChatId);
        if (scope == null)
        {
            return scopeError;
        }

        var queueArgs = ParseCaseQueueArgs(commandArgs);
        var scopedCases = await LoadScopedCasesAsync(scope.Value.CaseId, scope.Value.ChatId, ct);
        var filtered = ApplyCaseStatusFilter(scopedCases, queueArgs.StatusFilter)
            .Take(queueArgs.Limit)
            .ToList();

        if (filtered.Count == 0)
        {
            return queueArgs.StatusFilter.Equals("active", StringComparison.OrdinalIgnoreCase)
                ? "No active Stage 6 cases in this scope. Clarification intake stays on /gaps and /answer."
                : $"No Stage 6 cases matched status='{queueArgs.StatusFilter}'.";
        }

        var sb = new StringBuilder();
        sb.AppendLine($"cases: shown={filtered.Count} | active={scopedCases.Count(x => IsActiveCaseStatus(x.Status))} | needs_input={scopedCases.Count(IsNeedsInputCase)} | ready={scopedCases.Count(x => x.Status == Stage6CaseStatuses.Ready)}");
        sb.AppendLine("clarification stays primary in bot: use /gaps or /answer first for needs-user-input items.");
        foreach (var item in filtered)
        {
            var summary = SummarizeCase(item, 88);
            var primaryAction = IsNeedsInputCase(item) ? "answer" : "case";
            sb.AppendLine($"{item.Id} | {item.Status} | {item.Priority} | {item.CaseType} | {summary} | primary={primaryAction}");
        }

        sb.AppendLine("detail: /case <stage6-case-id>");
        return sb.ToString().Trim();
    }

    private async Task<string> HandleCaseAsync(string args, long? transportChatId, CancellationToken ct)
    {
        var (scope, commandArgs, scopeError) = ResolveScopeFromArgs(args, transportChatId);
        if (scope == null)
        {
            return scopeError;
        }

        var target = ParseStage6CaseTarget(commandArgs);
        var caseRecord = target.Stage6CaseId.HasValue
            ? await LoadScopedCaseAsync(target.Stage6CaseId.Value, scope.Value, ct)
            : (await LoadScopedCasesAsync(scope.Value.CaseId, scope.Value.ChatId, ct)).FirstOrDefault(x => IsActiveCaseStatus(x.Status));
        if (caseRecord == null)
        {
            return target.Stage6CaseId.HasValue
                ? "Stage 6 case not found in current scope."
                : "No active Stage 6 case to show. Use /cases all for closed items.";
        }

        var clarificationQuestion = await TryGetLinkedClarificationQuestionAsync(caseRecord, ct);
        var evidenceRefs = ParseJsonList(caseRecord.EvidenceRefsJson, 4);
        var targetArtifactTypes = ParseJsonList(caseRecord.TargetArtifactTypesJson, 6);
        var sb = new StringBuilder();
        sb.AppendLine($"case: {caseRecord.Id}");
        sb.AppendLine($"type: {caseRecord.CaseType} ({caseRecord.CaseSubtype ?? "-"})");
        sb.AppendLine($"status: {caseRecord.Status} | priority: {caseRecord.Priority} | conf: {(caseRecord.Confidence ?? 0f):0.00}");
        sb.AppendLine($"evidence summary: {BuildCaseEvidenceSummary(caseRecord)}");
        sb.AppendLine($"evidence refs: {(evidenceRefs.Count == 0 ? "none recorded" : string.Join(" | ", evidenceRefs))}");
        if (clarificationQuestion != null)
        {
            sb.AppendLine($"question: {clarificationQuestion.QuestionText}");
            sb.AppendLine($"question status: {clarificationQuestion.Status} | options: {FormatOptions(clarificationQuestion.AnswerOptionsJson)}");
        }

        sb.AppendLine($"targets: {(targetArtifactTypes.Count == 0 ? "none recorded" : string.Join(", ", targetArtifactTypes))}");
        sb.AppendLine($"source: {caseRecord.SourceObjectType}:{caseRecord.SourceObjectId}");
        sb.AppendLine($"updated: {caseRecord.UpdatedAt:yyyy-MM-dd HH:mm} UTC");
        if (clarificationQuestion != null && clarificationQuestion.Status is "open" or "in_progress")
        {
            sb.AppendLine($"primary action: /answer {clarificationQuestion.Id} | <your answer>");
            sb.AppendLine($"secondary: /annotate {caseRecord.Id} | <note> | /reject {caseRecord.Id} [reason] | /refresh {caseRecord.Id} [reason]");
        }
        else
        {
            sb.AppendLine($"actions: /resolve {caseRecord.Id} [reason] | /reject {caseRecord.Id} [reason] | /refresh {caseRecord.Id} [reason] | /annotate {caseRecord.Id} | <note>");
        }

        return sb.ToString().Trim();
    }

    private async Task<string> HandleCaseStatusActionAsync(
        string args,
        long? transportChatId,
        long? sourceMessageId,
        long? senderId,
        string nextStatus,
        string action,
        CancellationToken ct)
    {
        var (scope, commandArgs, scopeError) = ResolveScopeFromArgs(args, transportChatId);
        if (scope == null)
        {
            return scopeError;
        }

        var target = ParseStage6CaseTarget(commandArgs);
        if (!target.Stage6CaseId.HasValue)
        {
            return $"Usage: /{action} <stage6-case-id>{(action == "refresh" ? " [reason]" : " [reason]")}";
        }

        var caseRecord = await LoadScopedCaseAsync(target.Stage6CaseId.Value, scope.Value, ct);
        if (caseRecord == null)
        {
            return "Stage 6 case not found in current scope.";
        }

        var actor = BuildActor(senderId);
        var reason = string.IsNullOrWhiteSpace(target.Text) ? $"telegram_{action}" : target.Text;
        var clarificationQuestion = await TryGetLinkedClarificationQuestionAsync(caseRecord, ct);
        if (action.Equals("refresh", StringComparison.OrdinalIgnoreCase))
        {
            return await RefreshCaseAsync(caseRecord, clarificationQuestion, scope.Value.ChatId, actor, reason, ct);
        }

        if (action.Equals("resolve", StringComparison.OrdinalIgnoreCase))
        {
            if (clarificationQuestion != null && clarificationQuestion.Status is "open" or "in_progress")
            {
                return $"Clarification intake remains primary. Use /answer {clarificationQuestion.Id} | <your answer> before resolving case {caseRecord.Id}.";
            }

            if (clarificationQuestion != null && clarificationQuestion.Status.Equals("answered", StringComparison.OrdinalIgnoreCase))
            {
                var resolvedQuestion = await _clarificationRepository.UpdateQuestionWorkflowAsync(
                    clarificationQuestion.Id,
                    "resolved",
                    clarificationQuestion.Priority,
                    actor,
                    reason,
                    ct);
                return resolvedQuestion
                    ? $"Resolved clarification case {caseRecord.Id} after recorded answer."
                    : $"Failed to resolve clarification case {caseRecord.Id}.";
            }
        }

        if (action.Equals("reject", StringComparison.OrdinalIgnoreCase) && clarificationQuestion != null)
        {
            var rejectedQuestion = await _clarificationRepository.UpdateQuestionWorkflowAsync(
                clarificationQuestion.Id,
                "rejected",
                clarificationQuestion.Priority,
                actor,
                reason,
                ct);
            return rejectedQuestion
                ? $"Rejected clarification case {caseRecord.Id}. Reopen with /refresh {caseRecord.Id} if new evidence arrives."
                : $"Failed to reject clarification case {caseRecord.Id}.";
        }

        if (caseRecord.Status.Equals(nextStatus, StringComparison.OrdinalIgnoreCase))
        {
            return $"Stage 6 case {caseRecord.Id} is already {nextStatus}.";
        }

        var updated = await _stage6CaseRepository.UpdateStatusAsync(caseRecord.Id, nextStatus, actor, reason, ct);
        if (!updated)
        {
            return $"Failed to {action} case {caseRecord.Id}.";
        }

        return action.Equals("resolve", StringComparison.OrdinalIgnoreCase)
            ? $"Resolved Stage 6 case {caseRecord.Id}."
            : $"Rejected Stage 6 case {caseRecord.Id}.";
    }

    private async Task<string> HandleAnnotateAsync(string args, long? transportChatId, long? sourceMessageId, long? senderId, CancellationToken ct)
    {
        var (scope, commandArgs, scopeError) = ResolveScopeFromArgs(args, transportChatId);
        if (scope == null)
        {
            return scopeError;
        }

        var target = ParseStage6CaseTarget(commandArgs);
        if (!target.Stage6CaseId.HasValue || string.IsNullOrWhiteSpace(target.Text))
        {
            return "Usage: /annotate <stage6-case-id> | <context note>";
        }

        var caseRecord = await LoadScopedCaseAsync(target.Stage6CaseId.Value, scope.Value, ct);
        if (caseRecord == null)
        {
            return "Stage 6 case not found in current scope.";
        }

        var clarificationQuestion = await TryGetLinkedClarificationQuestionAsync(caseRecord, ct);
        var actor = BuildActor(senderId);
        var note = target.Text.Trim();
        _ = await _stage6UserContextRepository.CreateAsync(new Stage6UserContextEntry
        {
            Stage6CaseId = caseRecord.Id,
            ScopeCaseId = caseRecord.ScopeCaseId,
            ChatId = caseRecord.ChatId ?? scope.Value.ChatId,
            SourceKind = UserContextSourceKinds.OperatorAnnotation,
            ClarificationQuestionId = clarificationQuestion?.Id,
            ContentText = note,
            StructuredPayloadJson = JsonSerializer.Serialize(new
            {
                case_type = caseRecord.CaseType,
                case_status = caseRecord.Status
            }),
            AppliesToRefsJson = JsonSerializer.Serialize(BuildContextRefs(caseRecord)),
            EnteredVia = "bot",
            UserReportedCertainty = 1f,
            SourceType = "telegram_operator",
            SourceId = "/annotate",
            SourceMessageId = sourceMessageId,
            SourceSessionId = null,
            ConflictsWithRefsJson = "[]",
            CreatedAt = DateTime.UtcNow
        }, ct);

        _ = await _domainReviewEventRepository.AddAsync(new DomainReviewEvent
        {
            ObjectType = "stage6_case",
            ObjectId = caseRecord.Id.ToString(),
            Action = "operator_annotate",
            NewValueRef = JsonSerializer.Serialize(new { note }),
            Reason = "bot_annotation",
            Actor = actor,
            CreatedAt = DateTime.UtcNow
        }, ct);

        var suffix = clarificationQuestion != null && clarificationQuestion.Status is "open" or "in_progress"
            ? $" Clarification answer path stays /answer {clarificationQuestion.Id} | <your answer>."
            : string.Empty;
        return $"annotation saved for case {caseRecord.Id}.{suffix}".Trim();
    }

    private async Task<string> HandleGapsAsync(string args, long? transportChatId, long? sourceMessageId, long? senderId, CancellationToken ct)
    {
        var (scope, _, scopeError) = ResolveScopeFromArgs(args, transportChatId);
        if (scope == null)
        {
            return scopeError;
        }

        var clarificationArtifact = await _stage6ArtifactRepository.GetCurrentAsync(
            scope.Value.CaseId,
            scope.Value.ChatId,
            Stage6ArtifactTypes.ClarificationState,
            Stage6ArtifactTypes.ChatScope(scope.Value.ChatId),
            ct);
        var clarificationEvidence = await _stage6ArtifactFreshnessService.BuildEvidenceStampAsync(
            scope.Value.CaseId,
            scope.Value.ChatId,
            Stage6ArtifactTypes.ClarificationState,
            ct);
        if (clarificationArtifact != null)
        {
            var freshness = Stage6ArtifactFreshness.Evaluate(clarificationArtifact, DateTime.UtcNow, clarificationEvidence.LatestEvidenceAtUtc);
            if (!freshness.IsStale)
            {
                try
                {
                    var persisted = JsonSerializer.Deserialize<BotClarificationStateArtifact>(clarificationArtifact.PayloadJson);
                    if (persisted != null && HasClarificationEvidenceContext(persisted))
                    {
                        _ = await _stage6ArtifactRepository.TouchReusedAsync(clarificationArtifact.Id, DateTime.UtcNow, ct);
                        return BuildGapsReply(persisted);
                    }
                }
                catch (JsonException)
                {
                    // fall through to regeneration
                }
            }

            _ = await _stage6ArtifactRepository.MarkStaleAsync(clarificationArtifact.Id, freshness.Reason ?? "stale", DateTime.UtcNow, ct);
        }

        var top = await GetTopQuestionAsync(scope.Value.CaseId, scope.Value.ChatId, ct);
        if (top == null)
        {
            return "No open clarification gaps right now. If uncertainty remains, run /state or /timeline then ask /gaps again.";
        }

        var linkedCase = (await _stage6CaseRepository.GetCasesAsync(scope.Value.CaseId, ct: ct))
            .Where(x => x.ChatId == null || x.ChatId == scope.Value.ChatId)
            .Where(x => x.SourceObjectType.Equals("clarification_question", StringComparison.OrdinalIgnoreCase)
                        && x.SourceObjectId.Equals(top.Question.Id.ToString(), StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.UpdatedAt)
            .FirstOrDefault();
        var linkedEvidenceRefs = linkedCase == null ? [] : ParseJsonList(linkedCase.EvidenceRefsJson, 4);
        var evidenceSummary = linkedCase == null
            ? "question-derived evidence only"
            : linkedEvidenceRefs.Count == 0
                ? BuildCaseEvidenceSummary(linkedCase)
                : $"{BuildCaseEvidenceSummary(linkedCase)} | refs: {string.Join(" | ", linkedEvidenceRefs)}";

        var options = ParseJsonList(top.Question.AnswerOptionsJson, 4);
        var artifactPayload = new BotClarificationStateArtifact
        {
            QuestionId = top.Question.Id,
            Stage6CaseId = linkedCase?.Id,
            QuestionText = top.Question.QuestionText,
            WhyItMatters = top.Question.WhyItMatters,
            EvidenceSummary = evidenceSummary,
            Priority = top.Question.Priority,
            Status = top.Question.Status,
            Options = options,
            IsDependencyBlocked = top.IsBlockedByDependency
        };
        _ = await _stage6ArtifactRepository.UpsertCurrentAsync(new Stage6ArtifactRecord
        {
            ArtifactType = Stage6ArtifactTypes.ClarificationState,
            CaseId = scope.Value.CaseId,
            ChatId = scope.Value.ChatId,
            ScopeKey = Stage6ArtifactTypes.ChatScope(scope.Value.ChatId),
            PayloadObjectType = "clarification_state",
            PayloadObjectId = top.Question.Id.ToString(),
            PayloadJson = JsonSerializer.Serialize(artifactPayload),
            FreshnessBasisHash = clarificationEvidence.BasisHash,
            FreshnessBasisJson = clarificationEvidence.BasisJson,
            GeneratedAt = DateTime.UtcNow,
            RefreshedAt = DateTime.UtcNow,
            StaleAt = DateTime.UtcNow.Add(_stage6ArtifactFreshnessService.ResolveTtl(Stage6ArtifactTypes.ClarificationState)),
            IsStale = false,
            SourceType = "telegram_command",
            SourceId = "/gaps",
            SourceMessageId = null,
            SourceSessionId = null,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        }, ct);

        return BuildGapsReply(artifactPayload);
    }

    private async Task<string> HandleAnswerAsync(string args, long? transportChatId, long? sourceMessageId, long? senderId, CancellationToken ct)
    {
        var (scope, commandArgs, scopeError) = ResolveScopeFromArgs(args, transportChatId);
        if (scope == null)
        {
            return scopeError;
        }

        if (string.IsNullOrWhiteSpace(commandArgs))
        {
            return "Usage: /answer <text> or /answer <question-id> | <text>";
        }

        var (questionId, answerText) = await ResolveAnswerTargetAsync(commandArgs, scope.Value.CaseId, scope.Value.ChatId, ct);
        if (questionId == Guid.Empty || string.IsNullOrWhiteSpace(answerText))
        {
            return "Cannot resolve answer target. Use /gaps to get current question, then /answer <question-id> | <text>.";
        }

        var applied = await _clarificationOrchestrator.ApplyAnswerAsync(new ClarificationApplyRequest
        {
            QuestionId = questionId,
            AnswerType = "text",
            AnswerValue = answerText,
            AnswerConfidence = 0.85f,
            SourceClass = "user_confirmed",
            SourceType = "telegram_user",
            SourceId = "/answer",
            SourceMessageId = null,
            MarkResolved = true,
            Actor = BuildActor(senderId),
            Reason = "telegram_answer"
        }, ct);

        var layers = applied.RecomputePlan.Targets
            .Select(x => x.Layer)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"saved answer for: {applied.Question.QuestionText}");
        sb.AppendLine($"question status: {applied.Question.Status}");
        sb.AppendLine($"dependency updates: {applied.DependencyUpdates.Count}");
        sb.AppendLine($"conflicts: {applied.Conflicts.Count}");
        sb.AppendLine($"recompute: {(layers.Count == 0 ? "none" : string.Join(", ", layers))}");
        return sb.ToString().Trim();
    }

    private async Task<string> HandleTimelineAsync(string args, long? transportChatId, long? sourceMessageId, long? senderId, CancellationToken ct)
    {
        var (scope, _, scopeError) = ResolveScopeFromArgs(args, transportChatId);
        if (scope == null)
        {
            return scopeError;
        }

        var periods = (await _periodRepository.GetPeriodsByCaseAsync(scope.Value.CaseId, ct))
            .Where(x => x.ChatId == null || x.ChatId == scope.Value.ChatId)
            .OrderByDescending(x => x.StartAt)
            .ToList();

        if (periods.Count == 0)
        {
            var run = await _periodizationService.RunAsync(new PeriodizationRunRequest
            {
                CaseId = scope.Value.CaseId,
                ChatId = scope.Value.ChatId,
                Actor = BuildActor(senderId),
                SourceType = "telegram_command",
                SourceId = "/timeline",
                Persist = true
            }, ct);

            periods = run.Periods.OrderByDescending(x => x.StartAt).ToList();
        }

        if (periods.Count == 0)
        {
            return "Timeline is empty. Need more messages/events before periodization can produce periods.";
        }

        var current = periods.FirstOrDefault(x => x.IsOpen) ?? periods[0];
        var prior = periods.Where(x => x.Id != current.Id).Take(3).ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"current: {FormatPeriodLine(current)}");
        foreach (var period in prior)
        {
            sb.AppendLine($"prior: {FormatPeriodLine(period)}");
        }

        var transitions = await _periodRepository.GetTransitionsByPeriodAsync(current.Id, ct);
        var unresolvedCount = transitions.Count(x => !x.IsResolved);
        if (unresolvedCount > 0)
        {
            sb.AppendLine($"unresolved transitions: {unresolvedCount}");
        }

        return sb.ToString().Trim();
    }

    private async Task<string> HandleOfflineAsync(string args, long? transportChatId, long? sourceMessageId, long? senderId, CancellationToken ct)
    {
        var (scope, commandArgs, scopeError) = ResolveScopeFromArgs(args, transportChatId);
        if (scope == null)
        {
            return scopeError;
        }

        var summary = commandArgs.Trim();
        if (summary.Length == 0)
        {
            return "Usage: /offline <what happened offline>. Example: /offline We met yesterday, tone was warmer, agreed to pause pressure.";
        }

        var title = summary.Length <= 72 ? summary : summary[..72].TrimEnd() + "...";
        var created = await _offlineEventRepository.CreateOfflineEventAsync(new OfflineEvent
        {
            CaseId = scope.Value.CaseId,
            ChatId = scope.Value.ChatId,
            EventType = "user_offline_note",
            Title = title,
            UserSummary = summary,
            TimestampStart = DateTime.UtcNow,
            TimestampEnd = DateTime.UtcNow,
            ReviewStatus = "pending",
            SourceType = "telegram_command",
            SourceId = "/offline",
            SourceMessageId = null,
            EvidenceRefsJson = "[]"
        }, ct);

        return $"Offline event logged: {created.Id}. Next: run /gaps or /state to integrate impact.";
    }

    private (CaseScope? Scope, string ArgsWithoutScope, string Error) ResolveScopeFromArgs(string args, long? transportChatId)
    {
        var tokens = (args ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        long? caseIdArg = null;
        long? chatIdArg = null;
        var remaining = new List<string>();
        foreach (var token in tokens)
        {
            if (token.StartsWith("case=", StringComparison.OrdinalIgnoreCase)
                && long.TryParse(token[5..], out var caseIdFromToken)
                && caseIdFromToken > 0)
            {
                caseIdArg = caseIdFromToken;
                continue;
            }

            if (token.StartsWith("chat=", StringComparison.OrdinalIgnoreCase)
                && long.TryParse(token[5..], out var chatIdFromToken)
                && chatIdFromToken > 0)
            {
                chatIdArg = chatIdFromToken;
                continue;
            }

            remaining.Add(token);
        }

        var caseId = caseIdArg ?? _botSettings.DefaultCaseId;
        if (caseId <= 0)
        {
            return (null, string.Join(' ', remaining), "Bot case scope is not configured. Set BotChat:DefaultCaseId or pass case=<id>.");
        }

        var chatId = chatIdArg ?? _botSettings.DefaultChatId;
        if (chatId <= 0 && _telegramSettings.MonitoredChatIds.Count > 0)
        {
            chatId = _telegramSettings.MonitoredChatIds.FirstOrDefault(x => x > 0);
        }

        if (chatId <= 0 && transportChatId.HasValue && transportChatId.Value > 0)
        {
            chatId = transportChatId.Value;
        }

        if (chatId <= 0)
        {
            return (null, string.Join(' ', remaining), "Bot chat scope is not configured. Set BotChat:DefaultChatId or pass chat=<id>.");
        }

        return (new CaseScope(caseId, chatId), string.Join(' ', remaining), string.Empty);
    }

    private async Task<ClarificationQueueItem?> GetTopQuestionAsync(long caseId, long chatId, CancellationToken ct)
    {
        var queue = await _clarificationOrchestrator.BuildQueueAsync(caseId, ct);
        return queue
            .Where(x => x.Question.ChatId == null || x.Question.ChatId == chatId)
            .OrderBy(x => x.IsBlockedByDependency ? 1 : 0)
            .ThenByDescending(x => x.QueueScore)
            .FirstOrDefault();
    }

    private async Task<(Guid QuestionId, string AnswerText)> ResolveAnswerTargetAsync(string args, long caseId, long chatId, CancellationToken ct)
    {
        var text = args.Trim();
        var pipeIndex = text.IndexOf('|');
        if (pipeIndex > 0)
        {
            var head = text[..pipeIndex].Trim();
            var tail = text[(pipeIndex + 1)..].Trim();
            if (Guid.TryParse(head, out var qid))
            {
                return (qid, tail);
            }
        }

        var firstSpace = text.IndexOf(' ');
        if (firstSpace > 0)
        {
            var maybeId = text[..firstSpace].Trim();
            if (Guid.TryParse(maybeId, out var qid))
            {
                return (qid, text[(firstSpace + 1)..].Trim());
            }
        }

        var top = await GetTopQuestionAsync(caseId, chatId, ct);
        if (top == null)
        {
            return (Guid.Empty, string.Empty);
        }

        return (top.Question.Id, text);
    }

    private async Task<DraftRecord?> GetLatestDraftAsync(long caseId, long chatId, CancellationToken ct)
    {
        var records = await _strategyDraftRepository.GetStrategyRecordsByCaseAsync(caseId, ct);
        var latestStrategy = records.FirstOrDefault(x => x.ChatId == null || x.ChatId == chatId);
        if (latestStrategy == null)
        {
            return null;
        }

        return (await _strategyDraftRepository.GetDraftRecordsByStrategyRecordIdAsync(latestStrategy.Id, ct))
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefault();
    }

    private async Task<StateSnapshot?> TryGetReusableCurrentStateSnapshotAsync(long caseId, long chatId, CancellationToken ct)
    {
        var artifact = await _stage6ArtifactRepository.GetCurrentAsync(
            caseId,
            chatId,
            Stage6ArtifactTypes.CurrentState,
            Stage6ArtifactTypes.ChatScope(chatId),
            ct);
        if (artifact == null)
        {
            return null;
        }

        var evidence = await _stage6ArtifactFreshnessService.BuildEvidenceStampAsync(caseId, chatId, Stage6ArtifactTypes.CurrentState, ct);
        var freshness = Stage6ArtifactFreshness.Evaluate(artifact, DateTime.UtcNow, evidence.LatestEvidenceAtUtc);
        if (freshness.IsStale || !Guid.TryParse(artifact.PayloadObjectId, out var snapshotId))
        {
            _ = await _stage6ArtifactRepository.MarkStaleAsync(artifact.Id, freshness.Reason ?? "stale", DateTime.UtcNow, ct);
            return null;
        }

        var snapshot = await _stateProfileRepository.GetStateSnapshotByIdAsync(snapshotId, ct);
        if (snapshot == null)
        {
            _ = await _stage6ArtifactRepository.MarkStaleAsync(artifact.Id, "missing_payload_object", DateTime.UtcNow, ct);
            return null;
        }

        _ = await _stage6ArtifactRepository.TouchReusedAsync(artifact.Id, DateTime.UtcNow, ct);
        return snapshot;
    }

    private async Task<StrategyEngineResult?> TryGetReusableStrategyAsync(long caseId, long chatId, CancellationToken ct)
    {
        var artifact = await _stage6ArtifactRepository.GetCurrentAsync(
            caseId,
            chatId,
            Stage6ArtifactTypes.Strategy,
            Stage6ArtifactTypes.ChatScope(chatId),
            ct);
        if (artifact == null)
        {
            return null;
        }

        var evidence = await _stage6ArtifactFreshnessService.BuildEvidenceStampAsync(caseId, chatId, Stage6ArtifactTypes.Strategy, ct);
        var freshness = Stage6ArtifactFreshness.Evaluate(artifact, DateTime.UtcNow, evidence.LatestEvidenceAtUtc);
        if (freshness.IsStale || !Guid.TryParse(artifact.PayloadObjectId, out var strategyId))
        {
            _ = await _stage6ArtifactRepository.MarkStaleAsync(artifact.Id, freshness.Reason ?? "stale", DateTime.UtcNow, ct);
            return null;
        }

        var record = await _strategyDraftRepository.GetStrategyRecordByIdAsync(strategyId, ct);
        if (record == null)
        {
            _ = await _stage6ArtifactRepository.MarkStaleAsync(artifact.Id, "missing_payload_object", DateTime.UtcNow, ct);
            return null;
        }

        var options = await _strategyDraftRepository.GetStrategyOptionsByRecordIdAsync(record.Id, ct);
        _ = await _stage6ArtifactRepository.TouchReusedAsync(artifact.Id, DateTime.UtcNow, ct);
        return new StrategyEngineResult
        {
            Record = record,
            Options = options,
            MicroStep = record.MicroStep,
            Horizon = ParseJsonList(record.HorizonJson, 6),
            WhyNotNotes = record.WhyNotOthers,
            Confidence = new StrategyConfidenceAssessment
            {
                Confidence = record.StrategyConfidence
            }
        };
    }

    private async Task<DraftRecord?> TryGetReusableDraftAsync(long caseId, long chatId, CancellationToken ct)
    {
        var artifact = await _stage6ArtifactRepository.GetCurrentAsync(
            caseId,
            chatId,
            Stage6ArtifactTypes.Draft,
            Stage6ArtifactTypes.ChatScope(chatId),
            ct);
        if (artifact == null)
        {
            return null;
        }

        var evidence = await _stage6ArtifactFreshnessService.BuildEvidenceStampAsync(caseId, chatId, Stage6ArtifactTypes.Draft, ct);
        var freshness = Stage6ArtifactFreshness.Evaluate(artifact, DateTime.UtcNow, evidence.LatestEvidenceAtUtc);
        if (freshness.IsStale || !Guid.TryParse(artifact.PayloadObjectId, out var draftId))
        {
            _ = await _stage6ArtifactRepository.MarkStaleAsync(artifact.Id, freshness.Reason ?? "stale", DateTime.UtcNow, ct);
            return null;
        }

        var draft = await _strategyDraftRepository.GetDraftRecordByIdAsync(draftId, ct);
        if (draft == null)
        {
            _ = await _stage6ArtifactRepository.MarkStaleAsync(artifact.Id, "missing_payload_object", DateTime.UtcNow, ct);
            return null;
        }

        _ = await _stage6ArtifactRepository.TouchReusedAsync(artifact.Id, DateTime.UtcNow, ct);
        return draft;
    }

    private async Task<DraftReviewResult?> TryGetReusableReviewAsync(long caseId, long chatId, CancellationToken ct)
    {
        var artifact = await _stage6ArtifactRepository.GetCurrentAsync(
            caseId,
            chatId,
            Stage6ArtifactTypes.Review,
            Stage6ArtifactTypes.ChatScope(chatId),
            ct);
        if (artifact == null)
        {
            return null;
        }

        var evidence = await _stage6ArtifactFreshnessService.BuildEvidenceStampAsync(caseId, chatId, Stage6ArtifactTypes.Review, ct);
        var freshness = Stage6ArtifactFreshness.Evaluate(artifact, DateTime.UtcNow, evidence.LatestEvidenceAtUtc);
        if (freshness.IsStale)
        {
            _ = await _stage6ArtifactRepository.MarkStaleAsync(artifact.Id, freshness.Reason ?? "stale", DateTime.UtcNow, ct);
            return null;
        }

        try
        {
            var review = JsonSerializer.Deserialize<DraftReviewResult>(artifact.PayloadJson);
            if (review == null)
            {
                _ = await _stage6ArtifactRepository.MarkStaleAsync(artifact.Id, "invalid_payload_json", DateTime.UtcNow, ct);
                return null;
            }

            _ = await _stage6ArtifactRepository.TouchReusedAsync(artifact.Id, DateTime.UtcNow, ct);
            return review;
        }
        catch (JsonException)
        {
            _ = await _stage6ArtifactRepository.MarkStaleAsync(artifact.Id, "invalid_payload_json", DateTime.UtcNow, ct);
            return null;
        }
    }

    private static string BuildGapsReply(BotClarificationStateArtifact artifact)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"evidence summary: {artifact.EvidenceSummary}");
        sb.AppendLine($"question: {artifact.QuestionText}");
        sb.AppendLine($"why it matters: {artifact.WhyItMatters}");
        sb.AppendLine($"priority: {artifact.Priority} | status: {artifact.Status}");
        sb.AppendLine($"options: {(artifact.Options.Count == 0 ? "free text" : string.Join(" | ", artifact.Options))}");
        if (artifact.Stage6CaseId.HasValue)
        {
            sb.AppendLine($"case detail: /case {artifact.Stage6CaseId}");
        }
        sb.AppendLine($"answer path: /answer {artifact.QuestionId} | <your answer>  (or /answer <your answer> for top question)");
        if (artifact.IsDependencyBlocked)
        {
            sb.AppendLine("note: this question is currently dependency-blocked; answer its parent first.");
        }

        return sb.ToString().Trim();
    }

    private static (string Command, string Args) SplitCommand(string normalized)
    {
        var firstSpace = normalized.IndexOf(' ');
        if (firstSpace < 0)
        {
            return (normalized.ToLowerInvariant(), string.Empty);
        }

        var command = normalized[..firstSpace].ToLowerInvariant();
        var args = normalized[(firstSpace + 1)..].Trim();
        return (command, args);
    }

    private static (string? Tone, string? Notes) ParseDraftArgs(string args)
    {
        if (string.IsNullOrWhiteSpace(args))
        {
            return (null, null);
        }

        var trimmed = args.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return (null, null);
        }

        string? tone = null;
        var toneIndex = trimmed.IndexOf("tone=", StringComparison.OrdinalIgnoreCase);
        if (toneIndex >= 0)
        {
            var raw = trimmed[(toneIndex + 5)..].TrimStart();
            if (raw.StartsWith('"'))
            {
                var closing = raw.IndexOf('"', 1);
                if (closing > 1)
                {
                    tone = raw[1..closing].Trim();
                    trimmed = (trimmed[..toneIndex] + " " + raw[(closing + 1)..]).Trim();
                }
                else
                {
                    tone = raw.Trim('"').Trim();
                    trimmed = trimmed[..toneIndex].Trim();
                }
            }
            else
            {
                var firstToken = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
                tone = firstToken?.Trim();
                if (!string.IsNullOrWhiteSpace(firstToken))
                {
                    var removeToken = $"tone={firstToken}";
                    trimmed = trimmed.Replace(removeToken, string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
                }
            }
        }

        var notes = trimmed.Replace("tone=", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
        return (string.IsNullOrWhiteSpace(tone) ? null : tone, string.IsNullOrWhiteSpace(notes) ? null : notes);
    }

    private async Task<List<Stage6CaseRecord>> LoadScopedCasesAsync(long caseId, long chatId, CancellationToken ct)
    {
        var records = await _stage6CaseRepository.GetCasesAsync(caseId, ct: ct);
        return records
            .Where(x => x.ChatId == null || x.ChatId == chatId)
            .OrderByDescending(GetCaseSortScore)
            .ThenByDescending(x => x.UpdatedAt)
            .ToList();
    }

    private async Task<Stage6CaseRecord?> LoadScopedCaseAsync(Guid stage6CaseId, CaseScope scope, CancellationToken ct)
    {
        var record = await _stage6CaseRepository.GetByIdAsync(stage6CaseId, ct);
        if (record == null || record.ScopeCaseId != scope.CaseId)
        {
            return null;
        }

        if (record.ChatId.HasValue && record.ChatId.Value != scope.ChatId)
        {
            return null;
        }

        return record;
    }

    private async Task<ClarificationQuestion?> TryGetLinkedClarificationQuestionAsync(Stage6CaseRecord record, CancellationToken ct)
    {
        if (!record.SourceObjectType.Equals("clarification_question", StringComparison.OrdinalIgnoreCase)
            || !Guid.TryParse(record.SourceObjectId, out var questionId))
        {
            return null;
        }

        return await _clarificationRepository.GetQuestionByIdAsync(questionId, ct);
    }

    private async Task<string> RefreshCaseAsync(
        Stage6CaseRecord caseRecord,
        ClarificationQuestion? clarificationQuestion,
        long fallbackChatId,
        string actor,
        string reason,
        CancellationToken ct)
    {
        var artifactTypes = ResolveRefreshArtifactTypes(caseRecord);
        var staleCount = 0;
        var chatId = caseRecord.ChatId ?? fallbackChatId;
        if (chatId > 0)
        {
            foreach (var artifactType in artifactTypes)
            {
                var current = await _stage6ArtifactRepository.GetCurrentAsync(
                    caseRecord.ScopeCaseId,
                    chatId,
                    artifactType,
                    Stage6ArtifactTypes.ChatScope(chatId),
                    ct);
                if (current == null)
                {
                    continue;
                }

                if (await _stage6ArtifactRepository.MarkStaleAsync(current.Id, reason, DateTime.UtcNow, ct))
                {
                    staleCount++;
                }
            }
        }

        var reopened = false;
        if (clarificationQuestion != null && clarificationQuestion.Status is "resolved" or "rejected" or "answered")
        {
            reopened = await _clarificationRepository.UpdateQuestionWorkflowAsync(
                clarificationQuestion.Id,
                "open",
                clarificationQuestion.Priority,
                actor,
                reason,
                ct);
        }
        else if (caseRecord.Status is Stage6CaseStatuses.Resolved or Stage6CaseStatuses.Rejected or Stage6CaseStatuses.Stale)
        {
            reopened = await _stage6CaseRepository.UpdateStatusAsync(
                caseRecord.Id,
                IsClarificationCaseType(caseRecord.CaseType) ? Stage6CaseStatuses.NeedsUserInput : Stage6CaseStatuses.Ready,
                actor,
                reason,
                ct);
        }

        _ = await _domainReviewEventRepository.AddAsync(new DomainReviewEvent
        {
            ObjectType = "stage6_case",
            ObjectId = caseRecord.Id.ToString(),
            Action = "operator_refresh",
            NewValueRef = JsonSerializer.Serialize(new
            {
                stale_count = staleCount,
                artifact_types = artifactTypes,
                reopened
            }),
            Reason = reason,
            Actor = actor,
            CreatedAt = DateTime.UtcNow
        }, ct);

        var nextStep = artifactTypes.Count == 0
            ? "no linked artifacts were recorded"
            : $"stale artifacts={staleCount}/{artifactTypes.Count} ({string.Join(", ", artifactTypes)})";
        if (clarificationQuestion != null)
        {
            var answerPath = reopened || clarificationQuestion.Status is "open" or "in_progress"
                ? $" Primary intake stays /answer {clarificationQuestion.Id} | <your answer>."
                : string.Empty;
            return $"refresh applied for case {caseRecord.Id}: {nextStep}; reopened={(reopened ? "yes" : "no")}.{answerPath}".Trim();
        }

        return $"refresh applied for case {caseRecord.Id}: {nextStep}; reopened={(reopened ? "yes" : "no")}.";
    }

    private static CaseQueueArgs ParseCaseQueueArgs(string args)
    {
        var result = new CaseQueueArgs();
        if (string.IsNullOrWhiteSpace(args))
        {
            return result;
        }

        var statusFilter = result.StatusFilter;
        var limit = result.Limit;
        var tokens = args.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var token in tokens)
        {
            if (token.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                statusFilter = "all";
                continue;
            }

            if (token.StartsWith("status=", StringComparison.OrdinalIgnoreCase))
            {
                statusFilter = NormalizeCaseQueueStatusFilter(token[7..]);
                continue;
            }

            if (token.StartsWith("limit=", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(token[6..], out var parsedLimit))
            {
                limit = Math.Clamp(parsedLimit, 1, 20);
            }
        }

        return new CaseQueueArgs
        {
            StatusFilter = statusFilter,
            Limit = limit
        };
    }

    private static IEnumerable<Stage6CaseRecord> ApplyCaseStatusFilter(IEnumerable<Stage6CaseRecord> cases, string filter)
    {
        return filter switch
        {
            "all" => cases,
            "active" => cases.Where(x => IsActiveCaseStatus(x.Status)),
            _ => cases.Where(x => x.Status.Equals(filter, StringComparison.OrdinalIgnoreCase))
        };
    }

    private static string NormalizeCaseQueueStatusFilter(string raw)
    {
        var normalized = (raw ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "all" => "all",
            "active" or "open" => "active",
            "needs_input" or "needs-user-input" or "needs_user_input" => Stage6CaseStatuses.NeedsUserInput,
            "ready" => Stage6CaseStatuses.Ready,
            "resolved" => Stage6CaseStatuses.Resolved,
            "rejected" => Stage6CaseStatuses.Rejected,
            "stale" => Stage6CaseStatuses.Stale,
            "new" => Stage6CaseStatuses.New,
            _ => "active"
        };
    }

    private static Stage6CaseTarget ParseStage6CaseTarget(string args)
    {
        var text = (args ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return new Stage6CaseTarget();
        }

        var pipeIndex = text.IndexOf('|');
        if (pipeIndex >= 0)
        {
            var head = text[..pipeIndex].Trim();
            var tail = text[(pipeIndex + 1)..].Trim();
            return Guid.TryParse(head, out var stage6CaseId)
                ? new Stage6CaseTarget { Stage6CaseId = stage6CaseId, Text = tail }
                : new Stage6CaseTarget { Text = text };
        }

        var firstSpace = text.IndexOf(' ');
        if (firstSpace < 0)
        {
            return Guid.TryParse(text, out var stage6CaseId)
                ? new Stage6CaseTarget { Stage6CaseId = stage6CaseId }
                : new Stage6CaseTarget { Text = text };
        }

        var idPart = text[..firstSpace].Trim();
        var remainder = text[(firstSpace + 1)..].Trim();
        return Guid.TryParse(idPart, out var parsedStage6CaseId)
            ? new Stage6CaseTarget { Stage6CaseId = parsedStage6CaseId, Text = remainder }
            : new Stage6CaseTarget { Text = text };
    }

    private static bool HasClarificationEvidenceContext(BotClarificationStateArtifact artifact)
    {
        return !string.IsNullOrWhiteSpace(artifact.EvidenceSummary) || artifact.Stage6CaseId.HasValue;
    }

    private static bool IsActiveCaseStatus(string status)
    {
        return status is Stage6CaseStatuses.New or Stage6CaseStatuses.Ready or Stage6CaseStatuses.NeedsUserInput;
    }

    private static bool IsClarificationCaseType(string caseType)
    {
        return caseType is Stage6CaseTypes.NeedsInput
            or Stage6CaseTypes.ClarificationMissingData
            or Stage6CaseTypes.ClarificationAmbiguity
            or Stage6CaseTypes.ClarificationEvidenceInterpretationConflict
            or Stage6CaseTypes.ClarificationNextStepBlocked;
    }

    private static bool IsNeedsInputCase(Stage6CaseRecord record)
    {
        return record.Status.Equals(Stage6CaseStatuses.NeedsUserInput, StringComparison.OrdinalIgnoreCase)
            && IsClarificationCaseType(record.CaseType);
    }

    private static int GetCaseSortScore(Stage6CaseRecord record)
    {
        var score = 0;
        if (IsNeedsInputCase(record))
        {
            score += 500;
        }

        score += record.Priority switch
        {
            "blocking" => 300,
            "important" => 200,
            _ => 100
        };

        score += record.Status switch
        {
            Stage6CaseStatuses.NeedsUserInput => 80,
            Stage6CaseStatuses.Ready => 60,
            Stage6CaseStatuses.New => 40,
            Stage6CaseStatuses.Stale => 20,
            _ => 0
        };

        score += record.CaseType.Equals(Stage6CaseTypes.Risk, StringComparison.OrdinalIgnoreCase) ? 40 : 0;
        score += (int)Math.Round(Math.Clamp(record.Confidence ?? 0f, 0f, 1f) * 20f);
        return score;
    }

    private static string SummarizeCase(Stage6CaseRecord record, int limit)
    {
        var summary = string.IsNullOrWhiteSpace(record.QuestionText)
            ? record.ReasonSummary
            : record.QuestionText;
        if (string.IsNullOrWhiteSpace(summary))
        {
            summary = record.CaseType;
        }

        summary = summary.Trim();
        return summary.Length <= limit
            ? summary
            : $"{summary[..limit].TrimEnd()}...";
    }

    private static string BuildCaseEvidenceSummary(Stage6CaseRecord record)
    {
        if (!string.IsNullOrWhiteSpace(record.ReasonSummary))
        {
            return record.ReasonSummary.Trim();
        }

        if (!string.IsNullOrWhiteSpace(record.QuestionText))
        {
            return record.QuestionText.Trim();
        }

        return "No evidence summary recorded.";
    }

    private static string FormatOptions(string? json)
    {
        var options = ParseJsonList(json, 4);
        return options.Count == 0 ? "free text" : string.Join(" | ", options);
    }

    private static List<string> ResolveRefreshArtifactTypes(Stage6CaseRecord record)
    {
        var mapped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in ParseJsonList(record.TargetArtifactTypesJson, 12))
        {
            switch (token.Trim().ToLowerInvariant())
            {
                case "state":
                case "status":
                case "current_state":
                    mapped.Add(Stage6ArtifactTypes.CurrentState);
                    break;
                case "strategy":
                    mapped.Add(Stage6ArtifactTypes.Strategy);
                    break;
                case "draft":
                    mapped.Add(Stage6ArtifactTypes.Draft);
                    break;
                case "review":
                    mapped.Add(Stage6ArtifactTypes.Review);
                    break;
                case "clarification":
                case "clarification_state":
                    mapped.Add(Stage6ArtifactTypes.ClarificationState);
                    break;
                case "dossier":
                    mapped.Add(Stage6ArtifactTypes.Dossier);
                    break;
            }
        }

        if (IsClarificationCaseType(record.CaseType))
        {
            mapped.Add(Stage6ArtifactTypes.ClarificationState);
        }

        return mapped.ToList();
    }

    private static List<string> BuildContextRefs(Stage6CaseRecord record)
    {
        var refs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            $"stage6_case:{record.Id}"
        };
        foreach (var artifactType in ResolveRefreshArtifactTypes(record))
        {
            refs.Add($"stage6_artifact_type:{artifactType}");
        }

        return refs.ToList();
    }

    private static string BuildHelp()
    {
        return string.Join('\n',
            "scope: set BotChat defaults or pass case=<id> chat=<id> in command",
            "clarification intake first: /gaps then /answer",
            "/state - current state summary",
            "/next - ranked next-move strategy",
            "/draft [tone=...] [notes] - main + softer + more direct alternatives",
            "/review <text> - risk review with safer/natural rewrites",
            "/cases [status=active|all|needs_user_input|ready|resolved|rejected|stale] [limit=n] - operator queue",
            "/case [stage6-case-id] - case details; defaults to top active case",
            "/gaps - top clarification question with evidence summary",
            "/answer <text> or /answer <question-id> | <text>",
            "/resolve <stage6-case-id> [reason] - resolve case",
            "/reject <stage6-case-id> [reason] - reject case",
            "/refresh <stage6-case-id> [reason] - stale linked artifacts / reopen case",
            "/annotate <stage6-case-id> | <text> - add operator context note",
            "/timeline - current and prior periods",
            "/offline <summary> - log offline event");
    }

    private sealed class BotClarificationStateArtifact
    {
        public Guid QuestionId { get; set; }
        public Guid? Stage6CaseId { get; set; }
        public string QuestionText { get; set; } = string.Empty;
        public string WhyItMatters { get; set; } = string.Empty;
        public string EvidenceSummary { get; set; } = string.Empty;
        public string Priority { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public List<string> Options { get; set; } = [];
        public bool IsDependencyBlocked { get; set; }
    }

    private sealed class CaseQueueArgs
    {
        public string StatusFilter { get; init; } = "active";
        public int Limit { get; init; } = 8;
    }

    private sealed class Stage6CaseTarget
    {
        public Guid? Stage6CaseId { get; init; }
        public string Text { get; init; } = string.Empty;
    }

    private static string BuildActor(long? senderId)
    {
        return senderId.HasValue && senderId.Value > 0 ? $"telegram:{senderId.Value}" : "telegram";
    }

    private static List<string> ParseJsonList(string? json, int limit)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return doc.RootElement.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => x.GetString()?.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Take(limit)
                .Cast<string>()
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string ShortRisk(string riskJson)
    {
        if (string.IsNullOrWhiteSpace(riskJson))
        {
            return "unknown";
        }

        try
        {
            using var doc = JsonDocument.Parse(riskJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return riskJson;
            }

            var labels = new List<string>();
            if (doc.RootElement.TryGetProperty("labels", out var labelsNode) && labelsNode.ValueKind == JsonValueKind.Array)
            {
                labels.AddRange(labelsNode.EnumerateArray()
                    .Where(x => x.ValueKind == JsonValueKind.String)
                    .Select(x => x.GetString())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Cast<string>());
            }

            var score = doc.RootElement.TryGetProperty("score", out var scoreNode) && scoreNode.ValueKind == JsonValueKind.Number
                ? scoreNode.GetSingle()
                : 0f;

            return labels.Count == 0
                ? $"score {score:0.00}"
                : $"{string.Join(',', labels.Take(3))} (score {score:0.00})";
        }
        catch (JsonException)
        {
            return riskJson;
        }
    }

    private static string BuildDoNot(StrategyOption? primary)
    {
        if (primary == null)
        {
            return "do not force escalation under ambiguity";
        }

        var risk = ShortRisk(primary.Risk);
        if (risk.Contains("overpressure", StringComparison.OrdinalIgnoreCase)
            || risk.Contains("premature_escalation", StringComparison.OrdinalIgnoreCase)
            || risk.Contains("timing_mismatch", StringComparison.OrdinalIgnoreCase))
        {
            return "do not push for immediate escalation or pressure-heavy asks";
        }

        if (risk.Contains("neediness_signal", StringComparison.OrdinalIgnoreCase)
            || risk.Contains("withdrawal_trigger", StringComparison.OrdinalIgnoreCase))
        {
            return "do not send multiple emotional follow-ups in one burst";
        }

        return "do not ignore ambiguity; clarify first when uncertain";
    }

    private static string FormatPeriodLine(Period period)
    {
        var end = period.EndAt?.ToString("yyyy-MM-dd") ?? "now";
        var summary = period.Summary?.Trim() ?? string.Empty;
        if (summary.Length > 120)
        {
            summary = summary[..120].TrimEnd() + "...";
        }

        return $"[{period.StartAt:yyyy-MM-dd}..{end}] {period.Label}; open_q={period.OpenQuestionsCount}; conf={period.InterpretationConfidence:0.00}; {summary}";
    }

    private static string EthicsNote(string riskJson)
    {
        if (string.IsNullOrWhiteSpace(riskJson))
        {
            return "neutral baseline";
        }

        try
        {
            using var doc = JsonDocument.Parse(riskJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("ethical_flags", out var flagsNode)
                || flagsNode.ValueKind != JsonValueKind.Array)
            {
                return "no explicit ethical flags";
            }

            var flags = flagsNode.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => x.GetString())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Cast<string>()
                .ToList();
            if (flags.Count == 0)
            {
                return "no explicit ethical flags";
            }

            if (flags.Any(x => x.Contains("violation", StringComparison.OrdinalIgnoreCase)
                               || x.Contains("contact_at_any_cost", StringComparison.OrdinalIgnoreCase)))
            {
                return "caution: pressure/manipulation risk detected";
            }

            if (flags.Any(x => x.Contains("clarity_dignity_aligned", StringComparison.OrdinalIgnoreCase)))
            {
                return "aligned with clarity/dignity/non-manipulation";
            }

            return string.Join(", ", flags.Take(3));
        }
        catch (JsonException)
        {
            return "unreadable risk payload";
        }
    }
}
