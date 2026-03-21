using System.Text.Json;
using Microsoft.Extensions.Logging;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage6.CompetingContext;

public class CompetingContextVerificationService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IExternalArchiveIngestionService _externalArchiveIngestionService;
    private readonly ICompetingContextRuntimeService _competingContextRuntimeService;
    private readonly ICurrentStateEngine _currentStateEngine;
    private readonly IStrategyEngine _strategyEngine;
    private readonly IInboxConflictRepository _inboxConflictRepository;
    private readonly ILogger<CompetingContextVerificationService> _logger;

    public CompetingContextVerificationService(
        IExternalArchiveIngestionService externalArchiveIngestionService,
        ICompetingContextRuntimeService competingContextRuntimeService,
        ICurrentStateEngine currentStateEngine,
        IStrategyEngine strategyEngine,
        IInboxConflictRepository inboxConflictRepository,
        ILogger<CompetingContextVerificationService> logger)
    {
        _externalArchiveIngestionService = externalArchiveIngestionService;
        _competingContextRuntimeService = competingContextRuntimeService;
        _currentStateEngine = currentStateEngine;
        _strategyEngine = strategyEngine;
        _inboxConflictRepository = inboxConflictRepository;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var scope = CaseScopeFactory.CreateSmokeScope("competing_context");

        var request = BuildSmokeIngestionRequest(scope, now);
        var ingestion = await _externalArchiveIngestionService.IngestAsync(request, ct);
        if (ingestion.IsReplay)
        {
            throw new InvalidOperationException("Competing-context smoke failed: ingest unexpectedly replayed on first run.");
        }

        var runtime = await _competingContextRuntimeService.RunAsync(new CompetingContextRuntimeRequest
        {
            CaseId = scope.CaseId,
            ChatId = scope.ChatId,
            AsOfUtc = now,
            Actor = "competing_context_smoke",
            SourceType = "smoke",
            SourceId = "competing_context_runtime"
        }, ct);

        ValidateRuntimeEffects(runtime);

        var state = await _currentStateEngine.ComputeAsync(new CurrentStateRequest
        {
            CaseId = scope.CaseId,
            ChatId = scope.ChatId,
            Actor = "competing_context_smoke",
            SourceType = "smoke",
            SourceId = "competing_context_state",
            Persist = true,
            AsOfUtc = now
        }, ct);

        var strategy = await _strategyEngine.RunAsync(new StrategyEngineRequest
        {
            CaseId = scope.CaseId,
            ChatId = scope.ChatId,
            Actor = "competing_context_smoke",
            SourceType = "smoke",
            SourceId = "competing_context_strategy",
            Persist = true,
            AsOfUtc = now
        }, ct);

        EnsureStateModifiersVisible(state);
        EnsureStrategyConstraintsVisible(strategy);

        var conflicts = await _inboxConflictRepository.GetConflictRecordsAsync(scope.CaseId, null, ct);
        if (!conflicts.Any(x => x.ConflictType == "competing_override_blocked"))
        {
            throw new InvalidOperationException("Competing-context smoke failed: blocked override attempts were not recorded.");
        }

        if (!conflicts.Any(x => x.ConflictType.StartsWith("competing_", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Competing-context smoke failed: review-required competing artifacts are not visible in conflict surfaces.");
        }

        _logger.LogInformation(
            "Competing-context smoke passed. case_id={CaseId}, ingest_run_id={RunId}, source_records={SourceRecords}, graph_hints={GraphHints}, timeline_hints={TimelineHints}, state_mod_refs={StateRefs}, strategy_constraints={StrategyConstraints}, blocked={Blocked}, conflicts={Conflicts}",
            scope.CaseId,
            ingestion.Batch.RunId,
            runtime.SourceRecordIds.Count,
            runtime.Interpretation.GraphHints.Count,
            runtime.Interpretation.TimelineHints.Count,
            runtime.Interpretation.StateModifiers.RationaleRefs.Count,
            runtime.Interpretation.StrategyConstraints.Count,
            runtime.Interpretation.BlockedOverrideAttempts.Count,
            conflicts.Count);
    }

    private static void ValidateRuntimeEffects(CompetingContextRuntimeResult runtime)
    {
        if (runtime.IsAuthoritative)
        {
            throw new InvalidOperationException("Competing-context smoke failed: runtime output became authoritative.");
        }

        if (!runtime.RequiresExplicitReview)
        {
            throw new InvalidOperationException("Competing-context smoke failed: runtime output does not require explicit review.");
        }

        var requiredRecordIds = new[] { "cc-graph", "cc-timeline", "cc-state", "cc-strategy", "cc-blocked" };
        foreach (var recordId in requiredRecordIds)
        {
            if (!runtime.SourceRecordIds.Contains(recordId, StringComparer.Ordinal))
            {
                throw new InvalidOperationException($"Competing-context smoke failed: ingested external record '{recordId}' was not consumed by runtime.");
            }
        }

        if (runtime.Interpretation.GraphHints.Count == 0)
        {
            throw new InvalidOperationException("Competing-context smoke failed: additive graph hints were not produced.");
        }

        if (runtime.Interpretation.TimelineHints.Count == 0)
        {
            throw new InvalidOperationException("Competing-context smoke failed: timeline annotations were not produced.");
        }

        if (runtime.Interpretation.StateModifiers.RationaleRefs.Count == 0)
        {
            throw new InvalidOperationException("Competing-context smoke failed: bounded state modifiers were not produced.");
        }

        if (runtime.Interpretation.StrategyConstraints.Count == 0)
        {
            throw new InvalidOperationException("Competing-context smoke failed: strategy constraints were not produced.");
        }

        if (runtime.Interpretation.BlockedOverrideAttempts.Count == 0)
        {
            throw new InvalidOperationException("Competing-context smoke failed: blocked override attempts were not produced.");
        }
    }

    private static void EnsureStateModifiersVisible(CurrentStateResult state)
    {
        if (!state.Snapshot.RiskRefsJson.Contains("competing_context", StringComparison.OrdinalIgnoreCase)
            && !state.Scores.RiskRefs.Any(x => x.Contains("competing_context", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Competing-context smoke failed: competing state modifiers are not visible in state risk refs.");
        }
    }

    private static void EnsureStrategyConstraintsVisible(StrategyEngineResult strategy)
    {
        if (!strategy.Options.Any(x => HasCompetingRiskLabel(x.Risk)))
        {
            throw new InvalidOperationException("Competing-context smoke failed: competing strategy constraints are not visible in strategy options.");
        }
    }

    private static bool HasCompetingRiskLabel(string riskJson)
    {
        if (string.IsNullOrWhiteSpace(riskJson))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(riskJson);
            if (!doc.RootElement.TryGetProperty("labels", out var labelsNode)
                || labelsNode.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            return labelsNode.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => x.GetString())
                .Any(x => !string.IsNullOrWhiteSpace(x) && x.Contains("competing:", StringComparison.OrdinalIgnoreCase));
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static ExternalArchiveImportRequest BuildSmokeIngestionRequest(CaseScope scope, DateTime now)
    {
        return new ExternalArchiveImportRequest
        {
            CaseId = scope.CaseId,
            SourceClass = ExternalArchiveSourceClasses.CompetingRelationshipArchive,
            SourceRef = $"smoke_competing_source:{scope.CaseId}",
            ImportedAtUtc = now,
            ImportBatchId = $"smoke-competing-{now:yyyyMMddHHmmss}",
            Actor = "competing_context_smoke",
            Records =
            [
                BuildRecord(scope, now.AddDays(-5), "cc-graph", "graph", "complicating", 0.74f),
                BuildRecord(scope, now.AddDays(-4), "cc-timeline", "timeline", "period_signal", 0.66f),
                BuildRecord(scope, now.AddDays(-3), "cc-state", "state", "pressure_signal", 0.69f),
                BuildRecord(scope, now.AddDays(-2), "cc-strategy", "strategy", "escalation_guard", 0.63f),
                BuildRecord(scope, now.AddDays(-1), "cc-blocked", "state", "status_override", 0.82f)
            ]
        };
    }

    private static ExternalArchiveRecord BuildRecord(
        CaseScope scope,
        DateTime occurredAtUtc,
        string recordId,
        string signalType,
        string signalSubtype,
        float confidence)
    {
        var payload = JsonSerializer.Serialize(new
        {
            signal_type = signalType,
            signal_subtype = signalSubtype,
            source_type = "external_archive",
            source_id = $"smoke:{recordId}",
            observed_at_utc = occurredAtUtc,
            subject_actor_key = $"{scope.ChatId}:1001",
            competing_actor_key = $"{scope.ChatId}:2002",
            confidence,
            evidence_refs = new[] { $"evidence:{recordId}" },
            metadata = new
            {
                review_required = "true",
                scenario = "competing_context_smoke"
            }
        }, JsonOptions);

        return new ExternalArchiveRecord
        {
            RecordId = recordId,
            OccurredAtUtc = occurredAtUtc,
            RecordType = ExternalArchiveRecordTypes.RelationshipSignal,
            Text = $"smoke competing signal {signalType}:{signalSubtype}",
            SubjectActorKey = $"{scope.ChatId}:1001",
            TargetActorKey = $"{scope.ChatId}:2002",
            ChatId = scope.ChatId,
            SourceMessageId = Math.Abs(recordId.GetHashCode(StringComparison.Ordinal)),
            Confidence = confidence,
            EvidenceRefs = [$"evidence:{recordId}"],
            RawPayloadJson = payload
        };
    }
}
