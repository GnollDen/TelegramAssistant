// Legacy Stage6 surface retained for reference and diagnostics only. Not part of the PRD target architecture.

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Intelligence.Stage6.AutoCases;

public class Stage6AutoCaseGenerationVerificationService
{
    private readonly Stage6AutoCaseGenerationService _autoCaseGenerationService;
    private readonly IStage6CaseRepository _stage6CaseRepository;
    private readonly IDbContextFactory<TgAssistantDbContext> _dbFactory;
    private readonly ILogger<Stage6AutoCaseGenerationVerificationService> _logger;

    public Stage6AutoCaseGenerationVerificationService(
        Stage6AutoCaseGenerationService autoCaseGenerationService,
        IStage6CaseRepository stage6CaseRepository,
        IDbContextFactory<TgAssistantDbContext> dbFactory,
        ILogger<Stage6AutoCaseGenerationVerificationService> logger)
    {
        _autoCaseGenerationService = autoCaseGenerationService;
        _stage6CaseRepository = stage6CaseRepository;
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        var caseScope = CaseScopeFactory.CreateSmokeScope("auto_case_generation");
        var caseId = caseScope.CaseId;
        var chatId = caseScope.ChatId;
        var now = DateTime.UtcNow;

        await SeedScenarioAsync(caseId, chatId, now, ct);

        _ = await _autoCaseGenerationService.RunOnceAsync(force: true, ct: ct);
        var riskCase = await _stage6CaseRepository.GetBySourceAsync(caseId, Stage6CaseTypes.Risk, "auto_case_rule", "risk:state_snapshot", ct);
        if (riskCase == null)
        {
            throw new InvalidOperationException("Auto-case smoke failed: risk case was not generated.");
        }

        var clarificationCase = await _stage6CaseRepository.GetBySourceAsync(
            caseId,
            Stage6CaseTypes.ClarificationMissingData,
            "auto_case_rule",
            "clarification:missing_data:date_gap",
            ct);
        if (clarificationCase == null || string.IsNullOrWhiteSpace(clarificationCase.QuestionText))
        {
            throw new InvalidOperationException("Auto-case smoke failed: clarification case was not generated.");
        }

        if (!clarificationCase.QuestionText.Contains("сообщения #", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Auto-case smoke failed: clarification question is not grounded in concrete message gaps.");
        }

        var firstCount = (await _stage6CaseRepository.GetCasesAsync(caseId, ct: ct))
            .Count(x => string.Equals(x.SourceObjectType, "auto_case_rule", StringComparison.OrdinalIgnoreCase));

        _ = await _autoCaseGenerationService.RunOnceAsync(force: true, ct: ct);
        var secondCount = (await _stage6CaseRepository.GetCasesAsync(caseId, ct: ct))
            .Count(x => string.Equals(x.SourceObjectType, "auto_case_rule", StringComparison.OrdinalIgnoreCase));
        if (firstCount != secondCount)
        {
            throw new InvalidOperationException($"Auto-case smoke failed: dedupe did not hold ({firstCount} -> {secondCount}).");
        }

        if (!string.Equals(riskCase.Priority, "blocking", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Auto-case smoke failed: risk case priority is not meaningful (expected blocking).");
        }

        _ = await _stage6CaseRepository.UpdateStatusAsync(riskCase.Id, Stage6CaseStatuses.Resolved, "auto_case_smoke", "resolved_for_reopen_check", ct);
        await AddNewRiskEvidenceAsync(caseId, chatId, now.AddMinutes(5), ct);
        _ = await _autoCaseGenerationService.RunOnceAsync(force: true, ct: ct);

        var reopenedRiskCase = await _stage6CaseRepository.GetBySourceAsync(caseId, Stage6CaseTypes.Risk, "auto_case_rule", "risk:state_snapshot", ct);
        if (reopenedRiskCase == null || reopenedRiskCase.Status != Stage6CaseStatuses.Ready)
        {
            throw new InvalidOperationException("Auto-case smoke failed: resolved risk case was not reopened after new evidence.");
        }

        await AddLowChurnMessageAsync(chatId, now.AddMinutes(6), ct);
        var beforeChurnCases = (await _stage6CaseRepository.GetCasesAsync(caseId, caseType: Stage6CaseTypes.StateRefreshNeeded, ct: ct))
            .Count(x => string.Equals(x.SourceObjectType, "auto_case_rule", StringComparison.OrdinalIgnoreCase)
                        && string.Equals(x.SourceObjectId, "state_refresh:current_state", StringComparison.OrdinalIgnoreCase));
        _ = await _autoCaseGenerationService.RunOnceAsync(force: true, ct: ct);
        var afterChurnCases = (await _stage6CaseRepository.GetCasesAsync(caseId, caseType: Stage6CaseTypes.StateRefreshNeeded, ct: ct))
            .Count(x => string.Equals(x.SourceObjectType, "auto_case_rule", StringComparison.OrdinalIgnoreCase)
                        && string.Equals(x.SourceObjectId, "state_refresh:current_state", StringComparison.OrdinalIgnoreCase));
        if (afterChurnCases > 1 || (beforeChurnCases > 0 && afterChurnCases > beforeChurnCases))
        {
            throw new InvalidOperationException("Auto-case smoke failed: ordinary message churn multiplied semantic state-refresh cases.");
        }

        _logger.LogInformation(
            "Auto-case smoke passed. case_id={CaseId}, first_count={FirstCount}, second_count={SecondCount}, reopened_status={ReopenedStatus}, churn_before={ChurnBefore}, churn_after={ChurnAfter}",
            caseId,
            firstCount,
            secondCount,
            reopenedRiskCase?.Status ?? "missing",
            beforeChurnCases,
            afterChurnCases);
    }

    private async Task SeedScenarioAsync(long caseId, long chatId, DateTime now, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var message1 = new DbMessage
        {
            Id = caseId + 100,
            TelegramMessageId = caseId + 100,
            ChatId = chatId,
            SenderId = 101,
            SenderName = "Alex",
            Timestamp = now.AddMinutes(-20),
            Text = "Need clarity before we move.",
            MediaType = 0,
            ProcessingStatus = 1,
            Source = 1,
            CreatedAt = now.AddMinutes(-20)
        };
        var message2 = new DbMessage
        {
            Id = caseId + 101,
            TelegramMessageId = caseId + 101,
            ChatId = chatId,
            SenderId = 202,
            SenderName = "Sam",
            Timestamp = now.AddMinutes(-10),
            Text = "Not sure who should reply first.",
            MediaType = 0,
            ProcessingStatus = 1,
            Source = 1,
            CreatedAt = now.AddMinutes(-10)
        };

        db.Messages.AddRange(message1, message2);
        await db.SaveChangesAsync(ct);

        db.StateSnapshots.Add(new DbStateSnapshot
        {
            Id = Guid.NewGuid(),
            CaseId = caseId,
            ChatId = chatId,
            AsOf = now.AddMinutes(-9),
            DynamicLabel = "uncertain",
            RelationshipStatus = "warming",
            AlternativeStatus = "cooling",
            InitiativeScore = 0.45f,
            ResponsivenessScore = 0.35f,
            OpennessScore = 0.41f,
            WarmthScore = 0.52f,
            ReciprocityScore = 0.38f,
            AmbiguityScore = 0.77f,
            AvoidanceRiskScore = 0.82f,
            EscalationReadinessScore = 0.28f,
            ExternalPressureScore = 0.79f,
            Confidence = 0.39f,
            RiskRefsJson = """["timing_mismatch","pressure_risk"]""",
            SourceMessageId = message2.Id,
            CreatedAt = now.AddMinutes(-9)
        });

        db.StrategyRecords.Add(new DbStrategyRecord
        {
            Id = Guid.NewGuid(),
            CaseId = caseId,
            ChatId = chatId,
            StrategyConfidence = 0.41f,
            RecommendedGoal = "stabilize contact cadence",
            WhyNotOthers = "uncertain ownership of next step",
            MicroStep = "ask one grounded clarification",
            SourceMessageId = message2.Id,
            CreatedAt = now.AddMinutes(-8)
        });

        db.Stage6Artifacts.Add(new DbStage6Artifact
        {
            Id = Guid.NewGuid(),
            ArtifactType = Stage6ArtifactTypes.CurrentState,
            CaseId = caseId,
            ChatId = chatId,
            ScopeKey = Stage6ArtifactTypes.ChatScope(chatId),
            PayloadJson = "{}",
            FreshnessBasisHash = "smoke",
            FreshnessBasisJson = "{}",
            GeneratedAt = now.AddHours(-8),
            IsStale = true,
            IsCurrent = true,
            SourceType = "smoke",
            SourceId = "state-artifact",
            CreatedAt = now.AddHours(-8),
            UpdatedAt = now.AddHours(-8)
        });

        db.Stage6Artifacts.Add(new DbStage6Artifact
        {
            Id = Guid.NewGuid(),
            ArtifactType = Stage6ArtifactTypes.Dossier,
            CaseId = caseId,
            ChatId = chatId,
            ScopeKey = Stage6ArtifactTypes.ChatScope(chatId),
            PayloadJson = "{}",
            FreshnessBasisHash = "smoke",
            FreshnessBasisJson = "{}",
            GeneratedAt = now.AddDays(-3),
            IsStale = false,
            IsCurrent = true,
            SourceType = "smoke",
            SourceId = "dossier-artifact",
            CreatedAt = now.AddDays(-3),
            UpdatedAt = now.AddDays(-3)
        });

        db.Periods.Add(new DbPeriod
        {
            Id = Guid.NewGuid(),
            CaseId = caseId,
            ChatId = chatId,
            Label = "active",
            StartAt = now.AddDays(-1),
            IsOpen = true,
            Summary = "active period",
            KeySignalsJson = "[]",
            WhatHelped = string.Empty,
            WhatHurt = string.Empty,
            OpenQuestionsCount = 2,
            BoundaryConfidence = 0.6f,
            InterpretationConfidence = 0.4f,
            ReviewPriority = 2,
            IsSensitive = false,
            StatusSnapshot = "uncertain",
            DynamicSnapshot = "mixed",
            SourceType = "smoke",
            SourceId = "period",
            EvidenceRefsJson = "[]",
            CreatedAt = now.AddHours(-12),
            UpdatedAt = now.AddMinutes(-7)
        });

        await db.SaveChangesAsync(ct);
    }

    private async Task AddNewRiskEvidenceAsync(long caseId, long chatId, DateTime atUtc, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var msgId = caseId + 102;
        db.Messages.Add(new DbMessage
        {
            Id = msgId,
            TelegramMessageId = msgId,
            ChatId = chatId,
            SenderId = 101,
            SenderName = "Alex",
            Timestamp = atUtc,
            Text = "Pressure increased, need immediate clarification.",
            MediaType = 0,
            ProcessingStatus = 1,
            Source = 1,
            CreatedAt = atUtc
        });
        await db.SaveChangesAsync(ct);

        db.StateSnapshots.Add(new DbStateSnapshot
        {
            Id = Guid.NewGuid(),
            CaseId = caseId,
            ChatId = chatId,
            AsOf = atUtc,
            DynamicLabel = "high_pressure",
            RelationshipStatus = "uncertain",
            AlternativeStatus = "cooling",
            InitiativeScore = 0.42f,
            ResponsivenessScore = 0.31f,
            OpennessScore = 0.37f,
            WarmthScore = 0.43f,
            ReciprocityScore = 0.35f,
            AmbiguityScore = 0.81f,
            AvoidanceRiskScore = 0.9f,
            EscalationReadinessScore = 0.24f,
            ExternalPressureScore = 0.88f,
            Confidence = 0.32f,
            RiskRefsJson = """["pressure_risk","escalation_risk"]""",
            SourceMessageId = msgId,
            CreatedAt = atUtc
        });

        await db.SaveChangesAsync(ct);
    }

    private async Task AddLowChurnMessageAsync(long chatId, DateTime atUtc, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var messageId = DateTime.UtcNow.Ticks % int.MaxValue;
        db.Messages.Add(new DbMessage
        {
            Id = messageId,
            TelegramMessageId = messageId,
            ChatId = chatId,
            SenderId = 101,
            SenderName = "Alex",
            Timestamp = atUtc,
            Text = "ok",
            MediaType = 0,
            ProcessingStatus = 1,
            Source = 1,
            CreatedAt = atUtc
        });

        await db.SaveChangesAsync(ct);
    }
}
