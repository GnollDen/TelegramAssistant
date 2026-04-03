using Microsoft.EntityFrameworkCore;
using Serilog;
using StackExchange.Redis;
using System.Text.Json;
using System.Net.Http.Headers;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database;
using TgAssistant.Infrastructure.Database.Ef;
using TgAssistant.Infrastructure.Redis;
using TgAssistant.Host.Launch;
using TgAssistant.Host.Health;
using TgAssistant.Host.Stage5Repair;
using TgAssistant.Host.Startup;
using TgAssistant.Intelligence.Stage5;
using TgAssistant.Intelligence.Stage6;
using TgAssistant.Intelligence.Stage6.AutoCases;
using TgAssistant.Intelligence.Stage6.Clarification;
using TgAssistant.Intelligence.Stage6.CompetingContext;
using TgAssistant.Intelligence.Stage6.Control;
using TgAssistant.Intelligence.Stage6.CurrentState;
using TgAssistant.Intelligence.Stage6.DraftReview;
using TgAssistant.Intelligence.Stage6.Drafts;
using TgAssistant.Intelligence.Stage6.Network;
using TgAssistant.Intelligence.Stage6.Outcome;
using TgAssistant.Intelligence.Stage6.Periodization;
using TgAssistant.Intelligence.Stage6.Profiles;
using TgAssistant.Intelligence.Stage6.Strategy;
using TgAssistant.Processing.Archive;
using TgAssistant.Processing.Archive.ExternalIngestion;
using TgAssistant.Processing.Workers;
using TgAssistant.Telegram.Listener;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("System", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("TgAssistant.Intelligence.Stage5", Serilog.Events.LogEventLevel.Debug)
    .MinimumLevel.Override("TgAssistant.Intelligence.Stage5.OpenRouterAnalysisService", Serilog.Events.LogEventLevel.Debug)
    .MinimumLevel.Override("TgAssistant.Processing.Media.OpenRouterMediaProcessor", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("TgAssistant.Processing.Media.OpenRouterVoiceParalinguisticsAnalyzer", Serilog.Events.LogEventLevel.Warning)
    .WriteTo.Async(a => a.Console())
    .CreateLogger();


try
{
    Log.Information("Starting Telegram Assistant...");
    var legacyStage6Entrypoints = new[]
    {
        "--stage6-execution-smoke",
        "--auto-case-smoke",
        "--stage6-light-ab-run"
    };
    var legacyStage6ArgPrefixes = new[]
    {
        "--stage6-light-ab-cases-file=",
        "--stage6-light-ab-output-dir=",
        "--stage6-light-ab-pass-label=",
        "--stage6-light-ab-model-override="
    };
    var requestedLegacyStage6Entrypoints = args
        .Where(arg => legacyStage6Entrypoints.Contains(arg, StringComparer.OrdinalIgnoreCase)
            || legacyStage6ArgPrefixes.Any(prefix => arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
    if (requestedLegacyStage6Entrypoints.Length > 0)
    {
        throw new InvalidOperationException(
            $"Legacy Stage6 helper entrypoints are not part of the active runtime contract: {string.Join(", ", requestedLegacyStage6Entrypoints)}. Use explicit legacy diagnostic switches instead.");
    }

    var runFoundationSmoke = args.Any(arg => string.Equals(arg, "--foundation-smoke", StringComparison.OrdinalIgnoreCase));
    var runClarificationSmoke = args.Any(arg => string.Equals(arg, "--clarification-smoke", StringComparison.OrdinalIgnoreCase));
    var runPeriodizationSmoke = args.Any(arg => string.Equals(arg, "--periodization-smoke", StringComparison.OrdinalIgnoreCase));
    var runStateSmoke = args.Any(arg => string.Equals(arg, "--state-smoke", StringComparison.OrdinalIgnoreCase));
    var runProfileSmoke = args.Any(arg => string.Equals(arg, "--profile-smoke", StringComparison.OrdinalIgnoreCase));
    var runStrategySmoke = args.Any(arg => string.Equals(arg, "--strategy-smoke", StringComparison.OrdinalIgnoreCase));
    var runDraftSmoke = args.Any(arg => string.Equals(arg, "--draft-smoke", StringComparison.OrdinalIgnoreCase));
    var runReviewSmoke = args.Any(arg => string.Equals(arg, "--review-smoke", StringComparison.OrdinalIgnoreCase));
    var runOutcomeSmoke = args.Any(arg => string.Equals(arg, "--outcome-smoke", StringComparison.OrdinalIgnoreCase));
    var runBudgetSmoke = args.Any(arg => string.Equals(arg, "--budget-smoke", StringComparison.OrdinalIgnoreCase));
    var runEvalSmoke = args.Any(arg => string.Equals(arg, "--eval-smoke", StringComparison.OrdinalIgnoreCase));
    var runCompetingContextSmoke = args.Any(arg => string.Equals(arg, "--competing-context-smoke", StringComparison.OrdinalIgnoreCase));
    var runLegacyBotSmoke = args.Any(arg => string.Equals(arg, "--legacy-bot-smoke", StringComparison.OrdinalIgnoreCase));
    var runLegacyAutoCaseSmoke = args.Any(arg => string.Equals(arg, "--legacy-auto-case-smoke", StringComparison.OrdinalIgnoreCase));
    var runLegacyNetworkSmoke = args.Any(arg => string.Equals(arg, "--legacy-network-smoke", StringComparison.OrdinalIgnoreCase));
    var includeLegacyStage6Diagnostics = runClarificationSmoke
        || runPeriodizationSmoke
        || runStateSmoke
        || runProfileSmoke
        || runStrategySmoke
        || runDraftSmoke
        || runReviewSmoke
        || runOutcomeSmoke
        || runBudgetSmoke
        || runEvalSmoke
        || runCompetingContextSmoke
        || runLegacyBotSmoke
        || runLegacyAutoCaseSmoke
        || runLegacyNetworkSmoke;
    var includeLegacyStage6ClusterDiagnostics = runLegacyBotSmoke
        || runLegacyAutoCaseSmoke
        || runLegacyNetworkSmoke;
    var runStage5Smoke = args.Any(arg => string.Equals(arg, "--stage5-smoke", StringComparison.OrdinalIgnoreCase));
    var runPassEnvelopeSmoke = args.Any(arg => string.Equals(arg, "--pass-envelope-smoke", StringComparison.OrdinalIgnoreCase));
    var runNormalizationSmoke = args.Any(arg => string.Equals(arg, "--normalization-smoke", StringComparison.OrdinalIgnoreCase));
    var runModelPassAuditSmoke = args.Any(arg => string.Equals(arg, "--model-pass-audit-smoke", StringComparison.OrdinalIgnoreCase));
    var runLlmGatewaySuccessSmoke = args.Any(arg => string.Equals(arg, "--llm-gateway-success-smoke", StringComparison.OrdinalIgnoreCase));
    var runLlmGatewayFailureSmoke = args.Any(arg => string.Equals(arg, "--llm-gateway-failure-smoke", StringComparison.OrdinalIgnoreCase));
    var runLlmGatewayExperimentSmoke = args.Any(arg => string.Equals(arg, "--llm-gateway-experiment-smoke", StringComparison.OrdinalIgnoreCase));
    var runLlmGatewayReplayAb = args.Any(arg => string.Equals(arg, "--llm-gateway-replay-ab", StringComparison.OrdinalIgnoreCase));
    var runEditDiffPilotSmoke = args.Any(arg => string.Equals(arg, "--edit-diff-pilot-smoke", StringComparison.OrdinalIgnoreCase));
    var llmGatewayReplayAbOutputArg = args.FirstOrDefault(arg => arg.StartsWith("--llm-gateway-replay-ab-output=", StringComparison.OrdinalIgnoreCase));
    var llmGatewayReplayAbOutput = llmGatewayReplayAbOutputArg is null
        ? null
        : llmGatewayReplayAbOutputArg["--llm-gateway-replay-ab-output=".Length..];
    var runStage6BootstrapSmoke = args.Any(arg => string.Equals(arg, "--stage6-bootstrap-smoke", StringComparison.OrdinalIgnoreCase));
    var runStage7DossierProfileSmoke = args.Any(arg => string.Equals(arg, "--stage7-dossier-profile-smoke", StringComparison.OrdinalIgnoreCase));
    var runStage7PairDynamicsSmoke = args.Any(arg => string.Equals(arg, "--stage7-pair-dynamics-smoke", StringComparison.OrdinalIgnoreCase));
    var runStage7TimelineSmoke = args.Any(arg => string.Equals(arg, "--stage7-timeline-smoke", StringComparison.OrdinalIgnoreCase));
    var runStage8RecomputeSmoke = args.Any(arg => string.Equals(arg, "--stage8-recompute-smoke", StringComparison.OrdinalIgnoreCase));
    var runLaunchSmoke = args.Any(arg => string.Equals(arg, "--launch-smoke", StringComparison.OrdinalIgnoreCase));
    var runExternalArchiveSmoke = args.Any(arg => string.Equals(arg, "--external-archive-smoke", StringComparison.OrdinalIgnoreCase));
    var runStage5ScopedRepair = args.Any(arg => string.Equals(arg, "--stage5-scoped-repair", StringComparison.OrdinalIgnoreCase));
    var runStage5ScopedRepairApply = args.Any(arg => string.Equals(arg, "--stage5-scoped-repair-apply", StringComparison.OrdinalIgnoreCase));
    if (runStage5ScopedRepairApply && !runStage5ScopedRepair)
    {
        throw new InvalidOperationException(
            "--stage5-scoped-repair-apply requires --stage5-scoped-repair. Refusing to continue in normal runtime mode.");
    }
    var stage5ScopedRepairChatArg = args.FirstOrDefault(arg => arg.StartsWith("--stage5-scoped-repair-chat-id=", StringComparison.OrdinalIgnoreCase));
    var stage5ScopedRepairAuditDirArg = args.FirstOrDefault(arg => arg.StartsWith("--stage5-scoped-repair-audit-dir=", StringComparison.OrdinalIgnoreCase));
    var stage5ScopedRepairChatId = stage5ScopedRepairChatArg is null
        ? 885574984L
        : long.TryParse(stage5ScopedRepairChatArg["--stage5-scoped-repair-chat-id=".Length..], out var parsedChatId)
            ? parsedChatId
            : 0L;
    var stage5ScopedRepairAuditDir = stage5ScopedRepairAuditDirArg is null
        ? null
        : stage5ScopedRepairAuditDirArg["--stage5-scoped-repair-audit-dir=".Length..];
    var riskBackupIdArg = args.FirstOrDefault(arg => arg.StartsWith("--risk-backup-id=", StringComparison.OrdinalIgnoreCase));
    var riskBackupCreatedAtArg = args.FirstOrDefault(arg => arg.StartsWith("--risk-backup-created-at-utc=", StringComparison.OrdinalIgnoreCase));
    var riskBackupScopeArg = args.FirstOrDefault(arg => arg.StartsWith("--risk-backup-scope=", StringComparison.OrdinalIgnoreCase));
    var riskBackupArtifactArg = args.FirstOrDefault(arg => arg.StartsWith("--risk-backup-artifact-uri=", StringComparison.OrdinalIgnoreCase));
    var riskBackupChecksumArg = args.FirstOrDefault(arg => arg.StartsWith("--risk-backup-checksum=", StringComparison.OrdinalIgnoreCase));
    var riskOperatorArg = args.FirstOrDefault(arg => arg.StartsWith("--risk-operator=", StringComparison.OrdinalIgnoreCase));
    var riskReasonArg = args.FirstOrDefault(arg => arg.StartsWith("--risk-reason=", StringComparison.OrdinalIgnoreCase));
    var riskAuditIdArg = args.FirstOrDefault(arg => arg.StartsWith("--risk-audit-id=", StringComparison.OrdinalIgnoreCase));
    var riskOverrideOperatorArg = args.FirstOrDefault(arg => arg.StartsWith("--risk-override-operator=", StringComparison.OrdinalIgnoreCase));
    var riskOverrideReasonArg = args.FirstOrDefault(arg => arg.StartsWith("--risk-override-reason=", StringComparison.OrdinalIgnoreCase));
    var riskOverrideApprovalTokenArg = args.FirstOrDefault(arg => arg.StartsWith("--risk-override-approval-token=", StringComparison.OrdinalIgnoreCase));
    var riskOverrideAuditIdArg = args.FirstOrDefault(arg => arg.StartsWith("--risk-override-audit-id=", StringComparison.OrdinalIgnoreCase));
    var externalArchiveImportArg = args.FirstOrDefault(arg => arg.StartsWith("--external-archive-import-file=", StringComparison.OrdinalIgnoreCase));
    var externalArchiveActorArg = args.FirstOrDefault(arg => arg.StartsWith("--external-archive-actor=", StringComparison.OrdinalIgnoreCase));
    var externalArchiveImportFile = externalArchiveImportArg is null
        ? null
        : externalArchiveImportArg["--external-archive-import-file=".Length..];
    var externalArchiveActor = externalArchiveActorArg is null
        ? "operator"
        : externalArchiveActorArg["--external-archive-actor=".Length..];
    var runListSmokes = args.Any(arg => string.Equals(arg, "--list-smokes", StringComparison.OrdinalIgnoreCase));
    var runRuntimeWiringCheck = args.Any(arg => string.Equals(arg, "--runtime-wiring-check", StringComparison.OrdinalIgnoreCase));
    var runLivenessCheck = args.Any(arg => string.Equals(arg, "--liveness-check", StringComparison.OrdinalIgnoreCase));
    var runReadinessCheck = args.Any(arg => string.Equals(arg, "--readiness-check", StringComparison.OrdinalIgnoreCase));
    var runHealthCheck = args.Any(arg => string.Equals(arg, "--healthcheck", StringComparison.OrdinalIgnoreCase));
    var preservedVerificationEntrypoints = new[]
    {
        "--foundation-smoke",
        "--stage5-smoke",
        "--pass-envelope-smoke",
        "--normalization-smoke",
        "--model-pass-audit-smoke",
        "--llm-gateway-success-smoke",
        "--llm-gateway-failure-smoke",
        "--llm-gateway-experiment-smoke",
        "--llm-gateway-replay-ab",
        "--edit-diff-pilot-smoke",
        "--stage6-bootstrap-smoke",
        "--stage7-dossier-profile-smoke",
        "--stage7-pair-dynamics-smoke",
        "--stage7-timeline-smoke",
        "--stage8-recompute-smoke",
        "--launch-smoke",
        "--external-archive-smoke"
    };

    var legacyDiagnosticEntrypoints = new[]
    {
        "--clarification-smoke",
        "--periodization-smoke",
        "--state-smoke",
        "--profile-smoke",
        "--strategy-smoke",
        "--draft-smoke",
        "--review-smoke",
        "--outcome-smoke",
        "--budget-smoke",
        "--eval-smoke",
        "--competing-context-smoke",
        "--legacy-bot-smoke",
        "--legacy-auto-case-smoke",
        "--legacy-network-smoke"
    };

    if (runListSmokes)
    {
        Log.Information("Available preserved verification entrypoints: {VerificationEntrypoints}", string.Join(", ", preservedVerificationEntrypoints));
        Log.Information("Legacy diagnostic-only entrypoints: {DiagnosticEntrypoints}", string.Join(", ", legacyDiagnosticEntrypoints));
        return;
    }

    if (runPassEnvelopeSmoke)
    {
        PassEnvelopeContractSmokeRunner.Run();
        Log.Information("Pass envelope contract smoke requested via --pass-envelope-smoke. Exiting after successful verification.");
        return;
    }

    if (runNormalizationSmoke)
    {
        NormalizationContractSmokeRunner.Run();
        Log.Information("Normalization contract smoke requested via --normalization-smoke. Exiting after successful verification.");
        return;
    }

    if (runModelPassAuditSmoke)
    {
        await ModelPassAuditSmokeRunner.RunAsync();
        Log.Information("Model pass audit smoke requested via --model-pass-audit-smoke. Exiting after successful verification.");
        return;
    }

    if (runLlmGatewaySuccessSmoke)
    {
        await LlmGatewaySuccessSmokeRunner.RunAsync();
        Log.Information("LLM gateway success smoke requested via --llm-gateway-success-smoke. Exiting after successful verification.");
        return;
    }

    if (runLlmGatewayFailureSmoke)
    {
        await LlmGatewayFailureSmokeRunner.RunAsync();
        Log.Information("LLM gateway failure smoke requested via --llm-gateway-failure-smoke. Exiting after successful verification.");
        return;
    }

    if (runLlmGatewayExperimentSmoke)
    {
        await LlmGatewayExperimentSmokeRunner.RunAsync();
        Log.Information("LLM gateway experiment smoke requested via --llm-gateway-experiment-smoke. Exiting after successful verification.");
        return;
    }

    if (runLlmGatewayReplayAb)
    {
        var report = await LlmGatewayReplayAbRunner.RunAsync(llmGatewayReplayAbOutput);
        var baselineSummary = report.BranchSummaries.First(summary => string.Equals(summary.Branch, "baseline", StringComparison.Ordinal));
        var candidateSummary = report.BranchSummaries.First(summary => string.Equals(summary.Branch, "candidate", StringComparison.Ordinal));
        Log.Information(
            "LLM gateway replay A/B requested via --llm-gateway-replay-ab. output={OutputPath}, baseline_error_rate={BaselineErrorRate:0.0000}, candidate_error_rate={CandidateErrorRate:0.0000}, parity_rate={ParityRate:0.0000}. Exiting after successful verification.",
            report.OutputPath,
            baselineSummary.ErrorRate,
            candidateSummary.ErrorRate,
            report.Comparison.ParityRate);
        return;
    }

    if (runEditDiffPilotSmoke)
    {
        // This smoke needs the real composition root because it verifies the runtime pilot toggle.
    }

    if (runStage6BootstrapSmoke)
    {
        await Stage6BootstrapSmokeRunner.RunAsync();
        Log.Information("Stage6 bootstrap smoke requested via --stage6-bootstrap-smoke. Exiting after successful verification.");
        return;
    }

    if (runStage7DossierProfileSmoke)
    {
        await Stage7DossierProfileSmokeRunner.RunAsync();
        Log.Information("Stage7 dossier/profile smoke requested via --stage7-dossier-profile-smoke. Exiting after successful verification.");
        return;
    }

    if (runStage7PairDynamicsSmoke)
    {
        await Stage7PairDynamicsSmokeRunner.RunAsync();
        Log.Information("Stage7 pair-dynamics smoke requested via --stage7-pair-dynamics-smoke. Exiting after successful verification.");
        return;
    }

    if (runStage7TimelineSmoke)
    {
        await Stage7TimelineSmokeRunner.RunAsync();
        Log.Information("Stage7 timeline smoke requested via --stage7-timeline-smoke. Exiting after successful verification.");
        return;
    }

    if (runStage8RecomputeSmoke)
    {
        await Stage8RecomputeQueueSmokeRunner.RunAsync();
        Log.Information("Stage8 recompute smoke requested via --stage8-recompute-smoke. Exiting after successful verification.");
        return;
    }

    var runtimeRoleSelection = new RuntimeRoleSelection(RuntimeWorkloadRole.None, "unresolved", string.Empty);

    var builder = Host.CreateDefaultBuilder(args)
        .UseSerilog()
        .ConfigureServices((context, services) =>
        {
            var config = context.Configuration;
            runtimeRoleSelection = RuntimeRoleParser.Parse(args, config);
            RuntimeStartupGuard.Validate(config, runtimeRoleSelection);
            services.AddTelegramAssistantCompositionRoot(
                config,
                runtimeRoleSelection,
                includeLegacyStage6Diagnostics,
                includeLegacyStage6ClusterDiagnostics);

            Log.Information(
                "Runtime role selection resolved: roles={Roles}, source={Source}, raw={RawValue}",
                runtimeRoleSelection.Roles,
                runtimeRoleSelection.Source,
                runtimeRoleSelection.RawValue);
        });

    var host = builder.Build();

    using (var scope = host.Services.CreateScope())
    {
        if (runLivenessCheck)
        {
            await RuntimeHealthProbeRunner.RunLivenessCheckAsync(runtimeRoleSelection, CancellationToken.None);
            Log.Information("Liveness check passed: process viability is healthy.");
            return;
        }

        if (runReadinessCheck || runHealthCheck)
        {
            await RuntimeHealthProbeRunner.RunReadinessCheckAsync(scope.ServiceProvider, runtimeRoleSelection, CancellationToken.None);
            Log.Information(
                "Readiness check passed: dependency and role-contract admission checks are healthy. alias_used={HealthAliasUsed}",
                runHealthCheck);
            return;
        }

        var dbInit = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();
        await dbInit.InitializeAsync();

        var redisQueue = scope.ServiceProvider.GetRequiredService<RedisMessageQueue>();
        await redisQueue.InitializeAsync();

        if (runFoundationSmoke)
        {
            var verificationService = scope.ServiceProvider.GetRequiredService<FoundationDomainVerificationService>();
            await verificationService.RunAsync();
            Log.Information("Foundation verification run requested via --foundation-smoke. Exiting after successful verification.");
            return;
        }

        if (runClarificationSmoke)
        {
            var verificationService = scope.ServiceProvider.GetRequiredService<ClarificationOrchestrationVerificationService>();
            await verificationService.RunAsync();
            Log.Information("Legacy Stage6 clarification diagnostic-only run requested via --clarification-smoke. Exiting after successful verification.");
            return;
        }

        if (runPeriodizationSmoke)
        {
            var verificationService = scope.ServiceProvider.GetRequiredService<PeriodizationVerificationService>();
            await verificationService.RunAsync();
            Log.Information("Legacy Stage6 periodization diagnostic-only run requested via --periodization-smoke. Exiting after successful verification.");
            return;
        }

        if (runStateSmoke)
        {
            var verificationService = scope.ServiceProvider.GetRequiredService<StateEngineVerificationService>();
            await verificationService.RunAsync();
            Log.Information("Legacy Stage6 state diagnostic-only run requested via --state-smoke. Exiting after successful verification.");
            return;
        }

        if (runProfileSmoke)
        {
            var verificationService = scope.ServiceProvider.GetRequiredService<ProfileEngineVerificationService>();
            await verificationService.RunAsync();
            Log.Information("Legacy Stage6 profile diagnostic-only run requested via --profile-smoke. Exiting after successful verification.");
            return;
        }

        if (runStrategySmoke)
        {
            var verificationService = scope.ServiceProvider.GetRequiredService<StrategyEngineVerificationService>();
            await verificationService.RunAsync();
            Log.Information("Legacy Stage6 strategy diagnostic-only run requested via --strategy-smoke. Exiting after successful verification.");
            return;
        }

        if (runDraftSmoke)
        {
            var verificationService = scope.ServiceProvider.GetRequiredService<DraftEngineVerificationService>();
            await verificationService.RunAsync();
            Log.Information("Legacy Stage6 draft diagnostic-only run requested via --draft-smoke. Exiting after successful verification.");
            return;
        }

        if (runReviewSmoke)
        {
            var verificationService = scope.ServiceProvider.GetRequiredService<DraftReviewVerificationService>();
            await verificationService.RunAsync();
            Log.Information("Legacy Stage6 review diagnostic-only run requested via --review-smoke. Exiting after successful verification.");
            return;
        }

        if (runOutcomeSmoke)
        {
            var verificationService = scope.ServiceProvider.GetRequiredService<OutcomeVerificationService>();
            await verificationService.RunAsync();
            Log.Information("Legacy Stage6 outcome diagnostic-only run requested via --outcome-smoke. Exiting after successful verification.");
            return;
        }

        if (runStage5Smoke)
        {
            var verificationService = scope.ServiceProvider.GetRequiredService<Stage5VerificationService>();
            await verificationService.RunAsync();
            var substrateVerificationService = scope.ServiceProvider.GetRequiredService<Stage5SubstrateDeterminismVerificationService>();
            await substrateVerificationService.RunAsync();
            Log.Information("Stage5 verification run requested via --stage5-smoke. Exiting after successful verification.");
            return;
        }

        if (runEditDiffPilotSmoke)
        {
            var result = await EditDiffPilotSmokeRunner.RunAsync(scope.ServiceProvider, CancellationToken.None);
            Log.Information(
                "Edit-diff pilot smoke requested via --edit-diff-pilot-smoke. legacy_provider={LegacyProvider}, legacy_model={LegacyModel}, gateway_provider={GatewayProvider}, gateway_model={GatewayModel}, gateway_fallback_applied={GatewayFallbackApplied}. Exiting after successful verification.",
                result.Legacy.Provider,
                result.Legacy.Model,
                result.Gateway.Provider,
                result.Gateway.Model,
                result.Gateway.FallbackApplied);
            return;
        }

        if (runBudgetSmoke)
        {
            var verificationService = scope.ServiceProvider.GetRequiredService<BudgetVerificationService>();
            await verificationService.RunAsync();
            Log.Information("Legacy Stage6 budget diagnostic-only run requested via --budget-smoke. Exiting after successful verification.");
            return;
        }

        if (runEvalSmoke)
        {
            var verificationService = scope.ServiceProvider.GetRequiredService<EvalVerificationService>();
            await verificationService.RunAsync();
            Log.Information("Legacy Stage6 eval diagnostic-only run requested via --eval-smoke. Exiting after successful verification.");
            return;
        }

        if (runLaunchSmoke)
        {
            var hostedServices = scope.ServiceProvider.GetServices<IHostedService>().Select(x => x.GetType().Name).OrderBy(x => x).ToList();
            Log.Information("Runtime wiring check (launch-smoke) passed. Hosted services resolved: {Count}", hostedServices.Count);

            var verificationService = scope.ServiceProvider.GetRequiredService<LaunchReadinessVerificationService>();
            await verificationService.RunAsync();
            Log.Information("Launch verification run requested via --launch-smoke. Exiting after successful verification.");
            return;
        }

        if (runExternalArchiveSmoke)
        {
            var verificationService = scope.ServiceProvider.GetRequiredService<ExternalArchiveVerificationService>();
            await verificationService.RunAsync();
            Log.Information("External archive verification run requested via --external-archive-smoke. Exiting after successful verification.");
            return;
        }

        if (runCompetingContextSmoke)
        {
            var verificationService = scope.ServiceProvider.GetRequiredService<CompetingContextVerificationService>();
            await verificationService.RunAsync();
            Log.Information("Legacy Stage6 competing-context diagnostic-only run requested via --competing-context-smoke. Exiting after successful verification.");
            return;
        }

        if (runLegacyBotSmoke)
        {
            var verificationService = scope.ServiceProvider.GetRequiredService<BotCommandVerificationService>();
            await verificationService.RunAsync();
            Log.Information("Legacy Stage6 bot diagnostic-only run requested via --legacy-bot-smoke. Exiting after successful verification.");
            return;
        }

        if (runLegacyAutoCaseSmoke)
        {
            var verificationService = scope.ServiceProvider.GetRequiredService<Stage6AutoCaseGenerationVerificationService>();
            await verificationService.RunAsync();
            Log.Information("Legacy Stage6 auto-case diagnostic-only run requested via --legacy-auto-case-smoke. Exiting after successful verification.");
            return;
        }

        if (runLegacyNetworkSmoke)
        {
            var service = scope.ServiceProvider.GetRequiredService<INetworkGraphService>();
            var smokeScope = CaseScopeFactory.CreateSmokeScope("legacy_network");
            var result = await service.BuildAsync(new NetworkBuildRequest
            {
                CaseId = smokeScope.CaseId,
                ChatId = smokeScope.ChatId,
                MessageLimit = 240,
                AsOfUtc = DateTime.UtcNow
            }, CancellationToken.None);
            Log.Information(
                "Legacy Stage6 network diagnostic-only run requested via --legacy-network-smoke. nodes={Nodes}, influence_edges={InfluenceEdges}, information_flows={InformationFlows}. Exiting after successful verification.",
                result.Nodes.Count,
                result.InfluenceEdges.Count,
                result.InformationFlows.Count);
            return;
        }

        if (runStage5ScopedRepair)
        {
            if (stage5ScopedRepairChatId <= 0)
            {
                throw new InvalidOperationException("Invalid --stage5-scoped-repair-chat-id value.");
            }

            BackupMetadataEvidence? backupEvidence = null;
            if (riskBackupIdArg is not null
                || riskBackupCreatedAtArg is not null
                || riskBackupScopeArg is not null
                || riskBackupArtifactArg is not null
                || riskBackupChecksumArg is not null)
            {
                if (riskBackupCreatedAtArg is null)
                {
                    throw new InvalidOperationException("Missing --risk-backup-created-at-utc for provided backup metadata.");
                }

                var backupCreatedRaw = riskBackupCreatedAtArg["--risk-backup-created-at-utc=".Length..];
                if (!DateTime.TryParse(
                        backupCreatedRaw,
                        null,
                        System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
                        out var parsedBackupCreatedAtUtc))
                {
                    throw new InvalidOperationException("Invalid --risk-backup-created-at-utc value. Expected ISO-8601 UTC.");
                }

                backupEvidence = new BackupMetadataEvidence
                {
                    BackupId = riskBackupIdArg is null ? string.Empty : riskBackupIdArg["--risk-backup-id=".Length..],
                    CreatedAtUtc = parsedBackupCreatedAtUtc,
                    Scope = riskBackupScopeArg is null ? string.Empty : riskBackupScopeArg["--risk-backup-scope=".Length..],
                    ArtifactUri = riskBackupArtifactArg is null ? string.Empty : riskBackupArtifactArg["--risk-backup-artifact-uri=".Length..],
                    Checksum = riskBackupChecksumArg is null ? string.Empty : riskBackupChecksumArg["--risk-backup-checksum=".Length..]
                };
            }

            var runOptions = new Stage5ScopedRepairExecutionOptions
            {
                BackupEvidence = backupEvidence,
                OperatorIdentity = riskOperatorArg is null ? string.Empty : riskOperatorArg["--risk-operator=".Length..],
                OperatorReason = riskReasonArg is null ? "stage5_scoped_repair_apply" : riskReasonArg["--risk-reason=".Length..],
                AuditId = riskAuditIdArg is null ? string.Empty : riskAuditIdArg["--risk-audit-id=".Length..],
                Override = new RiskOperationOverride
                {
                    OperatorIdentity = riskOverrideOperatorArg is null ? string.Empty : riskOverrideOperatorArg["--risk-override-operator=".Length..],
                    Reason = riskOverrideReasonArg is null ? string.Empty : riskOverrideReasonArg["--risk-override-reason=".Length..],
                    ApprovalToken = riskOverrideApprovalTokenArg is null ? string.Empty : riskOverrideApprovalTokenArg["--risk-override-approval-token=".Length..],
                    AuditId = riskOverrideAuditIdArg is null ? string.Empty : riskOverrideAuditIdArg["--risk-override-audit-id=".Length..]
                }
            };

            var repairCommand = scope.ServiceProvider.GetRequiredService<Stage5ScopedRepairCommand>();
            var result = await repairCommand.RunAsync(
                stage5ScopedRepairChatId,
                runStage5ScopedRepairApply,
                stage5ScopedRepairAuditDir,
                runOptions,
                CancellationToken.None);
            Log.Information(
                "Stage5 scoped repair command completed: mode={Mode}, chat_id={ChatId}, trusted_restore={TrustedRestore}, dual_migrations={DualMigrations}, orphan_placeholders={OrphanPlaceholders}, audit={AuditPath}",
                runStage5ScopedRepairApply ? "apply" : "dry-run",
                result.Summary.ChatId,
                result.Summary.Plan.TrustedSessionIndexes.Count,
                result.Summary.Plan.DualSourceMigrations.Count,
                result.Summary.Plan.OrphanPlaceholderMessageIds.Count,
                result.AuditPath);
            return;
        }

        if (!string.IsNullOrWhiteSpace(externalArchiveImportFile))
        {
            if (!File.Exists(externalArchiveImportFile))
            {
                throw new FileNotFoundException($"External archive import file was not found: {externalArchiveImportFile}");
            }

            var json = await File.ReadAllTextAsync(externalArchiveImportFile);
            var request = JsonSerializer.Deserialize<ExternalArchiveImportRequest>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ?? throw new InvalidOperationException("External archive import payload could not be deserialized.");
            if (string.IsNullOrWhiteSpace(request.Actor))
            {
                request.Actor = externalArchiveActor;
            }

            var ingestionService = scope.ServiceProvider.GetRequiredService<IExternalArchiveIngestionService>();
            var result = await ingestionService.IngestAsync(request);
            Log.Information(
                "External archive import completed via command mode. run_id={RunId}, case_id={CaseId}, is_replay={Replay}, persisted_records={Records}, persisted_linkages={Linkages}, rejected={Rejected}",
                result.Batch.RunId,
                result.Batch.CaseId,
                result.IsReplay,
                result.PersistedRecordCount,
                result.PersistedLinkageCount,
                result.Batch.RejectedCount);
            return;
        }

        if (runRuntimeWiringCheck)
        {
            var hostedServices = scope.ServiceProvider.GetServices<IHostedService>().Select(x => x.GetType().Name).OrderBy(x => x).ToList();
            Log.Information("Runtime wiring check passed. Hosted services resolved: {Count}", hostedServices.Count);
            foreach (var serviceName in hostedServices)
            {
                Log.Information("Hosted service registered: {ServiceName}", serviceName);
            }

            return;
        }
    }

    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
    Environment.ExitCode = 1;
}
finally
{
    Log.CloseAndFlush();
}
