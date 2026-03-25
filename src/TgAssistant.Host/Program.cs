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
using TgAssistant.Host.Stage6Ab;
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
using TgAssistant.Telegram.Bot;
using TgAssistant.Telegram.Listener;
using TgAssistant.Web.Read;

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
    var runFoundationSmoke = args.Any(arg => string.Equals(arg, "--foundation-smoke", StringComparison.OrdinalIgnoreCase));
    var runClarificationSmoke = args.Any(arg => string.Equals(arg, "--clarification-smoke", StringComparison.OrdinalIgnoreCase));
    var runPeriodizationSmoke = args.Any(arg => string.Equals(arg, "--periodization-smoke", StringComparison.OrdinalIgnoreCase));
    var runStateSmoke = args.Any(arg => string.Equals(arg, "--state-smoke", StringComparison.OrdinalIgnoreCase));
    var runProfileSmoke = args.Any(arg => string.Equals(arg, "--profile-smoke", StringComparison.OrdinalIgnoreCase));
    var runStrategySmoke = args.Any(arg => string.Equals(arg, "--strategy-smoke", StringComparison.OrdinalIgnoreCase));
    var runDraftSmoke = args.Any(arg => string.Equals(arg, "--draft-smoke", StringComparison.OrdinalIgnoreCase));
    var runReviewSmoke = args.Any(arg => string.Equals(arg, "--review-smoke", StringComparison.OrdinalIgnoreCase));
    var runBotSmoke = args.Any(arg => string.Equals(arg, "--bot-smoke", StringComparison.OrdinalIgnoreCase));
    var runWebSmoke = args.Any(arg => string.Equals(arg, "--web-smoke", StringComparison.OrdinalIgnoreCase));
    var runWebReviewSmoke = args.Any(arg => string.Equals(arg, "--web-review-smoke", StringComparison.OrdinalIgnoreCase));
    var runOpsWebSmoke = args.Any(arg => string.Equals(arg, "--ops-web-smoke", StringComparison.OrdinalIgnoreCase));
    var runSearchSmoke = args.Any(arg => string.Equals(arg, "--search-smoke", StringComparison.OrdinalIgnoreCase));
    var runNetworkSmoke = args.Any(arg => string.Equals(arg, "--network-smoke", StringComparison.OrdinalIgnoreCase));
    var runOutcomeSmoke = args.Any(arg => string.Equals(arg, "--outcome-smoke", StringComparison.OrdinalIgnoreCase));
    var runStage5Smoke = args.Any(arg => string.Equals(arg, "--stage5-smoke", StringComparison.OrdinalIgnoreCase));
    var runBudgetSmoke = args.Any(arg => string.Equals(arg, "--budget-smoke", StringComparison.OrdinalIgnoreCase));
    var runStage6ExecutionSmoke = args.Any(arg => string.Equals(arg, "--stage6-execution-smoke", StringComparison.OrdinalIgnoreCase));
    var runEvalSmoke = args.Any(arg => string.Equals(arg, "--eval-smoke", StringComparison.OrdinalIgnoreCase));
    var runLaunchSmoke = args.Any(arg => string.Equals(arg, "--launch-smoke", StringComparison.OrdinalIgnoreCase));
    var runExternalArchiveSmoke = args.Any(arg => string.Equals(arg, "--external-archive-smoke", StringComparison.OrdinalIgnoreCase));
    var runCompetingContextSmoke = args.Any(arg => string.Equals(arg, "--competing-context-smoke", StringComparison.OrdinalIgnoreCase));
    var runAutoCaseSmoke = args.Any(arg => string.Equals(arg, "--auto-case-smoke", StringComparison.OrdinalIgnoreCase));
    var runStage5ScopedRepair = args.Any(arg => string.Equals(arg, "--stage5-scoped-repair", StringComparison.OrdinalIgnoreCase));
    var runStage5ScopedRepairApply = args.Any(arg => string.Equals(arg, "--stage5-scoped-repair-apply", StringComparison.OrdinalIgnoreCase));
    var runStage6LightAb = args.Any(arg => string.Equals(arg, "--stage6-light-ab-run", StringComparison.OrdinalIgnoreCase));
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
    var stage6LightAbCasesFileArg = args.FirstOrDefault(arg => arg.StartsWith("--stage6-light-ab-cases-file=", StringComparison.OrdinalIgnoreCase));
    var stage6LightAbOutputDirArg = args.FirstOrDefault(arg => arg.StartsWith("--stage6-light-ab-output-dir=", StringComparison.OrdinalIgnoreCase));
    var stage6LightAbPassLabelArg = args.FirstOrDefault(arg => arg.StartsWith("--stage6-light-ab-pass-label=", StringComparison.OrdinalIgnoreCase));
    var stage6LightAbModelOverrideArg = args.FirstOrDefault(arg => arg.StartsWith("--stage6-light-ab-model-override=", StringComparison.OrdinalIgnoreCase));
    var stage6LightAbCasesFile = stage6LightAbCasesFileArg is null
        ? string.Empty
        : stage6LightAbCasesFileArg["--stage6-light-ab-cases-file=".Length..];
    var stage6LightAbOutputDir = stage6LightAbOutputDirArg is null
        ? string.Empty
        : stage6LightAbOutputDirArg["--stage6-light-ab-output-dir=".Length..];
    var stage6LightAbPassLabel = stage6LightAbPassLabelArg is null
        ? "pass"
        : stage6LightAbPassLabelArg["--stage6-light-ab-pass-label=".Length..];
    var stage6LightAbModelOverride = stage6LightAbModelOverrideArg is null
        ? string.Empty
        : stage6LightAbModelOverrideArg["--stage6-light-ab-model-override=".Length..];
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
    var smokeEntrypoints = new[]
    {
        "--foundation-smoke",
        "--clarification-smoke",
        "--periodization-smoke",
        "--state-smoke",
        "--profile-smoke",
        "--strategy-smoke",
        "--draft-smoke",
        "--review-smoke",
        "--bot-smoke",
        "--web-smoke",
        "--web-review-smoke",
        "--ops-web-smoke",
        "--search-smoke",
        "--network-smoke",
        "--outcome-smoke",
        "--stage5-smoke",
        "--budget-smoke",
        "--stage6-execution-smoke",
        "--eval-smoke",
        "--launch-smoke",
        "--external-archive-smoke",
        "--competing-context-smoke",
        "--auto-case-smoke"
    };

    if (runListSmokes)
    {
        Log.Information("Available smoke entrypoints: {SmokeEntrypoints}", string.Join(", ", smokeEntrypoints));
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
            services.AddTelegramAssistantCompositionRoot(config, runtimeRoleSelection);

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
            Log.Information("Foundation smoke run requested via --foundation-smoke. Exiting after successful verification.");
            return;
        }

        if (runClarificationSmoke)
        {
            var verificationService = scope.ServiceProvider.GetRequiredService<ClarificationOrchestrationVerificationService>();
            await verificationService.RunAsync();
            Log.Information("Clarification smoke run requested via --clarification-smoke. Exiting after successful verification.");
            return;
        }

        if (runPeriodizationSmoke)
        {
            var verificationService = scope.ServiceProvider.GetRequiredService<PeriodizationVerificationService>();
            await verificationService.RunAsync();
            Log.Information("Periodization smoke run requested via --periodization-smoke. Exiting after successful verification.");
            return;
        }

        if (runStateSmoke)
        {
            var verificationService = scope.ServiceProvider.GetRequiredService<StateEngineVerificationService>();
            await verificationService.RunAsync();
            Log.Information("State smoke run requested via --state-smoke. Exiting after successful verification.");
            return;
        }

        if (runProfileSmoke)
        {
            var verificationService = scope.ServiceProvider.GetRequiredService<ProfileEngineVerificationService>();
            await verificationService.RunAsync();
            Log.Information("Profile smoke run requested via --profile-smoke. Exiting after successful verification.");
            return;
        }

        if (runStrategySmoke)
        {
            var verificationService = scope.ServiceProvider.GetRequiredService<StrategyEngineVerificationService>();
            await verificationService.RunAsync();
            Log.Information("Strategy smoke run requested via --strategy-smoke. Exiting after successful verification.");
            return;
        }

        if (runDraftSmoke)
        {
            var verificationService = scope.ServiceProvider.GetRequiredService<DraftEngineVerificationService>();
            await verificationService.RunAsync();
            Log.Information("Draft smoke run requested via --draft-smoke. Exiting after successful verification.");
            return;
        }

        if (runReviewSmoke)
        {
            var verificationService = scope.ServiceProvider.GetRequiredService<DraftReviewVerificationService>();
            await verificationService.RunAsync();
            Log.Information("Review smoke run requested via --review-smoke. Exiting after successful verification.");
            return;
        }

        if (runBotSmoke)
        {
            var verificationService = scope.ServiceProvider.GetRequiredService<BotCommandVerificationService>();
            await verificationService.RunAsync();
            Log.Information("Bot smoke run requested via --bot-smoke. Exiting after successful verification.");
            return;
        }

        if (runWebSmoke)
        {
            var verificationService = scope.ServiceProvider.GetRequiredService<WebReadVerificationService>();
            await verificationService.RunAsync();
            Log.Information("Web smoke run requested via --web-smoke. Exiting after successful verification.");
            return;
        }

        if (runWebReviewSmoke)
        {
            var verificationService = scope.ServiceProvider.GetRequiredService<WebReviewVerificationService>();
            await verificationService.RunAsync();
            Log.Information("Web review smoke run requested via --web-review-smoke. Exiting after successful verification.");
            return;
        }

        if (runOpsWebSmoke)
        {
            var verificationService = scope.ServiceProvider.GetRequiredService<WebOpsVerificationService>();
            await verificationService.RunAsync();
            Log.Information("Ops web smoke run requested via --ops-web-smoke. Exiting after successful verification.");
            return;
        }

        if (runSearchSmoke)
        {
            var verificationService = scope.ServiceProvider.GetRequiredService<WebSearchVerificationService>();
            await verificationService.RunAsync();
            Log.Information("Search smoke run requested via --search-smoke. Exiting after successful verification.");
            return;
        }

        if (runNetworkSmoke)
        {
            var verificationService = scope.ServiceProvider.GetRequiredService<NetworkVerificationService>();
            await verificationService.RunAsync();
            Log.Information("Network smoke run requested via --network-smoke. Exiting after successful verification.");
            return;
        }

        if (runOutcomeSmoke)
        {
            var verificationService = scope.ServiceProvider.GetRequiredService<OutcomeVerificationService>();
            await verificationService.RunAsync();
            Log.Information("Outcome smoke run requested via --outcome-smoke. Exiting after successful verification.");
            return;
        }

        if (runStage5Smoke)
        {
            var verificationService = scope.ServiceProvider.GetRequiredService<Stage5VerificationService>();
            await verificationService.RunAsync();
            Log.Information("Stage5 smoke run requested via --stage5-smoke. Exiting after successful verification.");
            return;
        }

        if (runBudgetSmoke)
        {
            var verificationService = scope.ServiceProvider.GetRequiredService<BudgetVerificationService>();
            await verificationService.RunAsync();
            Log.Information("Budget smoke run requested via --budget-smoke. Exiting after successful verification.");
            return;
        }

        if (runStage6ExecutionSmoke)
        {
            var verificationService = scope.ServiceProvider.GetRequiredService<Stage6ExecutionDisciplineVerificationService>();
            await verificationService.RunAsync();
            Log.Information("Stage6 execution smoke run requested via --stage6-execution-smoke. Exiting after successful verification.");
            return;
        }

        if (runEvalSmoke)
        {
            var verificationService = scope.ServiceProvider.GetRequiredService<EvalVerificationService>();
            await verificationService.RunAsync();
            Log.Information("Eval smoke run requested via --eval-smoke. Exiting after successful verification.");
            return;
        }

        if (runLaunchSmoke)
        {
            var hostedServices = scope.ServiceProvider.GetServices<IHostedService>().Select(x => x.GetType().Name).OrderBy(x => x).ToList();
            Log.Information("Runtime wiring check (launch-smoke) passed. Hosted services resolved: {Count}", hostedServices.Count);

            var verificationService = scope.ServiceProvider.GetRequiredService<LaunchReadinessVerificationService>();
            await verificationService.RunAsync();
            Log.Information("Launch smoke run requested via --launch-smoke. Exiting after successful verification.");
            return;
        }

        if (runExternalArchiveSmoke)
        {
            var verificationService = scope.ServiceProvider.GetRequiredService<ExternalArchiveVerificationService>();
            await verificationService.RunAsync();
            Log.Information("External archive smoke run requested via --external-archive-smoke. Exiting after successful verification.");
            return;
        }

        if (runCompetingContextSmoke)
        {
            var verificationService = scope.ServiceProvider.GetRequiredService<CompetingContextVerificationService>();
            await verificationService.RunAsync();
            Log.Information("Competing context smoke run requested via --competing-context-smoke. Exiting after successful verification.");
            return;
        }

        if (runAutoCaseSmoke)
        {
            var verificationService = scope.ServiceProvider.GetRequiredService<Stage6AutoCaseGenerationVerificationService>();
            await verificationService.RunAsync();
            Log.Information("Auto-case smoke run requested via --auto-case-smoke. Exiting after successful verification.");
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

        if (runStage6LightAb)
        {
            var command = scope.ServiceProvider.GetRequiredService<Stage6LightAbRunCommand>();
            var result = await command.RunAsync(
                stage6LightAbCasesFile,
                stage6LightAbOutputDir,
                stage6LightAbPassLabel,
                stage6LightAbModelOverride,
                CancellationToken.None);
            Log.Information(
                "Stage6 light A/B command completed: pass={PassLabel}, model={Model}, total_cases={TotalCases}, failed={FailedCases}, chat_calls={ChatCalls}, embedding_calls={EmbeddingCalls}, tool_calls={ToolCalls}, artifact={ArtifactPath}, usage_csv={UsageCsv}",
                result.Summary.PassLabel,
                result.Summary.ModelResolved,
                result.Summary.TotalCases,
                result.Summary.FailedCases,
                result.Summary.TotalChatCalls,
                result.Summary.TotalEmbeddingCalls,
                result.Summary.TotalToolCalls,
                result.ArtifactPath,
                result.UsageCsvPath);
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
