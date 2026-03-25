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
                "/gaps" => (true, await HandleGapsAsync(args, transportChatId, sourceMessageId, senderId, ct)),
                "/answer" => (true, await HandleAnswerAsync(args, transportChatId, sourceMessageId, senderId, ct)),
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
                    if (persisted != null)
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

        var options = ParseJsonList(top.Question.AnswerOptionsJson, 4);
        var artifactPayload = new BotClarificationStateArtifact
        {
            QuestionId = top.Question.Id,
            QuestionText = top.Question.QuestionText,
            WhyItMatters = top.Question.WhyItMatters,
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
        sb.AppendLine($"question: {artifact.QuestionText}");
        sb.AppendLine($"why it matters: {artifact.WhyItMatters}");
        sb.AppendLine($"priority: {artifact.Priority} | status: {artifact.Status}");
        sb.AppendLine($"options: {(artifact.Options.Count == 0 ? "free text" : string.Join(" | ", artifact.Options))}");
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

    private static string BuildHelp()
    {
        return string.Join('\n',
            "scope: set BotChat defaults or pass case=<id> chat=<id> in command",
            "/state - current state summary",
            "/next - ranked next-move strategy",
            "/draft [tone=...] [notes] - main + softer + more direct alternatives",
            "/review <text> - risk review with safer/natural rewrites",
            "/gaps - top clarification question",
            "/answer <text> or /answer <question-id> | <text>",
            "/timeline - current and prior periods",
            "/offline <summary> - log offline event");
    }

    private sealed class BotClarificationStateArtifact
    {
        public Guid QuestionId { get; set; }
        public string QuestionText { get; set; } = string.Empty;
        public string WhyItMatters { get; set; } = string.Empty;
        public string Priority { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public List<string> Options { get; set; } = [];
        public bool IsDependencyBlocked { get; set; }
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
