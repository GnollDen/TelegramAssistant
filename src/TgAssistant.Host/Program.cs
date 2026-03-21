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
using TgAssistant.Intelligence.Stage5;
using TgAssistant.Intelligence.Stage6;
using TgAssistant.Intelligence.Stage6.Clarification;
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
    var runEvalSmoke = args.Any(arg => string.Equals(arg, "--eval-smoke", StringComparison.OrdinalIgnoreCase));
    var runExternalArchiveSmoke = args.Any(arg => string.Equals(arg, "--external-archive-smoke", StringComparison.OrdinalIgnoreCase));
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
        "--eval-smoke",
        "--external-archive-smoke"
    };

    if (runListSmokes)
    {
        Log.Information("Available smoke entrypoints: {SmokeEntrypoints}", string.Join(", ", smokeEntrypoints));
        return;
    }

    var builder = Host.CreateDefaultBuilder(args)
        .UseSerilog()
        .ConfigureServices((context, services) =>
        {
            var config = context.Configuration;

            services.Configure<TelegramSettings>(config.GetSection(TelegramSettings.Section));
            services.Configure<RedisSettings>(config.GetSection(RedisSettings.Section));
            services.Configure<DatabaseSettings>(config.GetSection(DatabaseSettings.Section));
            services.Configure<GeminiSettings>(config.GetSection(GeminiSettings.Section));
            services.Configure<ClaudeSettings>(config.GetSection(ClaudeSettings.Section));
            services.Configure<BatchWorkerSettings>(config.GetSection(BatchWorkerSettings.Section));
            services.Configure<MediaSettings>(config.GetSection(MediaSettings.Section));
            services.Configure<VoiceParalinguisticsSettings>(config.GetSection(VoiceParalinguisticsSettings.Section));
            services.Configure<ArchiveImportSettings>(config.GetSection(ArchiveImportSettings.Section));
            services.Configure<BackfillSettings>(config.GetSection(BackfillSettings.Section));
            services.Configure<AnalysisSettings>(config.GetSection(AnalysisSettings.Section));
            services.Configure<AggregationSettings>(config.GetSection(AggregationSettings.Section));
            services.Configure<MergeSettings>(config.GetSection(MergeSettings.Section));
            services.Configure<MonitoringSettings>(config.GetSection(MonitoringSettings.Section));
            services.Configure<MaintenanceSettings>(config.GetSection(MaintenanceSettings.Section));
            services.Configure<Neo4jSettings>(config.GetSection(Neo4jSettings.Section));
            services.Configure<EmbeddingSettings>(config.GetSection(EmbeddingSettings.Section));
            services.Configure<BotChatSettings>(config.GetSection(BotChatSettings.Section));
            services.Configure<BudgetGuardrailSettings>(config.GetSection(BudgetGuardrailSettings.Section));
            services.Configure<EvalHarnessSettings>(config.GetSection(EvalHarnessSettings.Section));

            services.PostConfigure<TelegramSettings>(s =>
            {
                if (string.IsNullOrWhiteSpace(s.MonitoredChats))
                {
                    return;
                }

                var needsParse = s.MonitoredChatIds.Count == 0 || s.MonitoredChatIds.All(id => id <= 0);
                if (!needsParse)
                {
                    s.MonitoredChatIds = s.MonitoredChatIds.Where(id => id > 0).Distinct().ToList();
                    return;
                }

                s.MonitoredChatIds = s.MonitoredChats
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(raw => long.TryParse(raw.Trim(), out var id) ? id : 0)
                    .Where(id => id > 0)
                    .Distinct()
                    .ToList();
            });

            services.PostConfigure<BackfillSettings>(s =>
            {
                var parsed = s.ChatIds
                    .Where(id => id > 0)
                    .ToList();

                var raw = config.GetSection(BackfillSettings.Section).GetValue<string>("ChatIds");
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    parsed.AddRange(raw
                        .Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(value => long.TryParse(value.Trim(), out var id) ? id : 0)
                        .Where(id => id > 0));
                }

                s.ChatIds = parsed.Distinct().ToList();
            });

            services.AddSingleton<IConnectionMultiplexer>(_ =>
                ConnectionMultiplexer.Connect(
                    config.GetSection(RedisSettings.Section).GetValue<string>("ConnectionString") ?? "localhost:6379"));

            services.AddSingleton<RedisMessageQueue>();
            services.AddSingleton<IMessageQueue>(sp => sp.GetRequiredService<RedisMessageQueue>());

            services.AddDbContextFactory<TgAssistantDbContext>(opt =>
            {
                var cs = config.GetSection(DatabaseSettings.Section).GetValue<string>("ConnectionString")
                         ?? "Host=localhost;Database=tgassistant;Username=tgassistant;Password=changeme";
                opt.UseNpgsql(cs);
            });

            services.AddSingleton<DatabaseInitializer>();

            // Data layer (EF Core)
            services.AddSingleton<IMessageRepository, MessageRepository>();
            services.AddSingleton<IArchiveImportRepository, ArchiveImportRepository>();
            services.AddSingleton<IStickerCacheRepository, StickerCacheRepository>();
            services.AddSingleton<IPromptTemplateRepository, PromptTemplateRepository>();
            services.AddSingleton<IAnalysisStateRepository, AnalysisStateRepository>();
            services.AddSingleton<IMessageExtractionRepository, MessageExtractionRepository>();
            services.AddSingleton<IIntelligenceRepository, IntelligenceRepository>();
            services.AddSingleton<IExtractionErrorRepository, ExtractionErrorRepository>();
            services.AddSingleton<IEntityRepository, EntityRepository>();
            services.AddSingleton<IEntityAliasRepository, EntityAliasRepository>();
            services.AddSingleton<IEntityMergeRepository, EntityMergeRepository>();
            services.AddSingleton<IEntityMergeCommandRepository, EntityMergeCommandRepository>();
            services.AddSingleton<IStage5MetricsRepository, Stage5MetricsRepository>();
            services.AddSingleton<IAnalysisUsageRepository, AnalysisUsageRepository>();
            services.AddSingleton<ICommunicationEventRepository, CommunicationEventRepository>();
            services.AddSingleton<IEmbeddingRepository, EmbeddingRepository>();
            services.AddSingleton<IMaintenanceRepository, MaintenanceRepository>();
            services.AddSingleton<IFactReviewCommandRepository, FactReviewCommandRepository>();
            services.AddSingleton<IFactRepository, FactRepository>();
            services.AddSingleton<IRelationshipRepository, RelationshipRepository>();
            services.AddSingleton<ISummaryRepository, SummaryRepository>();
            services.AddSingleton<IChatDialogSummaryRepository, ChatDialogSummaryRepository>();
            services.AddSingleton<IChatSessionRepository, ChatSessionRepository>();
            services.AddSingleton<IPeriodRepository, PeriodRepository>();
            services.AddSingleton<IClarificationRepository, ClarificationRepository>();
            services.AddSingleton<IOfflineEventRepository, OfflineEventRepository>();
            services.AddSingleton<IStateProfileRepository, StateProfileRepository>();
            services.AddSingleton<IStrategyDraftRepository, StrategyDraftRepository>();
            services.AddSingleton<IInboxConflictRepository, InboxConflictRepository>();
            services.AddSingleton<IDependencyLinkRepository, DependencyLinkRepository>();
            services.AddSingleton<IDomainReviewEventRepository, DomainReviewEventRepository>();
            services.AddSingleton<IBudgetOpsRepository, BudgetOpsRepository>();
            services.AddSingleton<IEvalRepository, EvalRepository>();
            services.AddSingleton<IExternalArchiveIngestionRepository, ExternalArchiveIngestionRepository>();
            services.AddSingleton<FoundationDomainVerificationService>();
            services.AddSingleton<IClarificationAnswerApplier, ClarificationAnswerApplier>();
            services.AddSingleton<IClarificationDependencyResolver, ClarificationDependencyResolver>();
            services.AddSingleton<IRecomputeTargetPlanner, RecomputeTargetPlanner>();
            services.AddSingleton<IClarificationOrchestrator, ClarificationOrchestrator>();
            services.AddSingleton<ClarificationOrchestrationVerificationService>();
            services.AddSingleton<IPeriodBoundaryDetector, PeriodBoundaryDetector>();
            services.AddSingleton<ITimelineAssembler, TimelineAssembler>();
            services.AddSingleton<ITransitionBuilder, TransitionBuilder>();
            services.AddSingleton<IPeriodEvidenceAssembler, PeriodEvidenceAssembler>();
            services.AddSingleton<IPeriodProposalService, PeriodProposalService>();
            services.AddSingleton<IPeriodizationService, PeriodizationService>();
            services.AddSingleton<PeriodizationVerificationService>();
            services.AddSingleton<IStateScoreCalculator, StateScoreCalculator>();
            services.AddSingleton<IStateConfidenceEvaluator, StateConfidenceEvaluator>();
            services.AddSingleton<IDynamicLabelMapper, DynamicLabelMapper>();
            services.AddSingleton<IRelationshipStatusMapper, RelationshipStatusMapper>();
            services.AddSingleton<ICurrentStateEngine, CurrentStateEngine>();
            services.AddSingleton<StateEngineVerificationService>();
            services.AddSingleton<IProfileTraitExtractor, ProfileTraitExtractor>();
            services.AddSingleton<IPairProfileSynthesizer, PairProfileSynthesizer>();
            services.AddSingleton<IProfileConfidenceEvaluator, ProfileConfidenceEvaluator>();
            services.AddSingleton<IPatternSynthesisService, PatternSynthesisService>();
            services.AddSingleton<IProfileEngine, ProfileEngine>();
            services.AddSingleton<ProfileEngineVerificationService>();
            services.AddSingleton<IStrategyOptionGenerator, StrategyOptionGenerator>();
            services.AddSingleton<IStrategyRiskEvaluator, StrategyRiskEvaluator>();
            services.AddSingleton<IStrategyRanker, StrategyRanker>();
            services.AddSingleton<IStrategyConfidenceEvaluator, StrategyConfidenceEvaluator>();
            services.AddSingleton<IMicroStepPlanner, MicroStepPlanner>();
            services.AddSingleton<IStrategyEngine, StrategyEngine>();
            services.AddSingleton<StrategyEngineVerificationService>();
            services.AddSingleton<IDraftGenerator, DraftGenerator>();
            services.AddSingleton<IDraftStyleAdapter, DraftStyleAdapter>();
            services.AddSingleton<IDraftStrategyChecker, DraftStrategyChecker>();
            services.AddSingleton<IDraftPackagingService, DraftPackagingService>();
            services.AddSingleton<IDraftEngine, DraftEngine>();
            services.AddSingleton<DraftEngineVerificationService>();
            services.AddSingleton<IDraftRiskAssessor, DraftRiskAssessor>();
            services.AddSingleton<IDraftStrategyFitChecker, DraftStrategyFitChecker>();
            services.AddSingleton<ISaferRewriteGenerator, SaferRewriteGenerator>();
            services.AddSingleton<INaturalRewriteGenerator, NaturalRewriteGenerator>();
            services.AddSingleton<IDraftReviewEngine, DraftReviewEngine>();
            services.AddSingleton<DraftReviewVerificationService>();
            services.AddSingleton<IBotCommandService, BotCommandService>();
            services.AddSingleton<BotCommandVerificationService>();
            services.AddSingleton<IWebReadService, WebReadService>();
            services.AddSingleton<IWebReviewService, WebReviewService>();
            services.AddSingleton<IWebOpsService, WebOpsService>();
            services.AddSingleton<IWebSearchService, WebSearchService>();
            services.AddSingleton<IWebRouteRenderer, WebRouteRenderer>();
            services.AddSingleton<WebReadVerificationService>();
            services.AddSingleton<WebReviewVerificationService>();
            services.AddSingleton<WebOpsVerificationService>();
            services.AddSingleton<WebSearchVerificationService>();
            services.AddSingleton<INodeRoleResolver, NodeRoleResolver>();
            services.AddSingleton<IInfluenceEdgeBuilder, InfluenceEdgeBuilder>();
            services.AddSingleton<IInformationFlowBuilder, InformationFlowBuilder>();
            services.AddSingleton<INetworkScoringService, NetworkScoringService>();
            services.AddSingleton<INetworkGraphService, NetworkGraphService>();
            services.AddSingleton<NetworkVerificationService>();
            services.AddSingleton<IDraftActionMatcher, DraftActionMatcher>();
            services.AddSingleton<IObservedOutcomeRecorder, ObservedOutcomeRecorder>();
            services.AddSingleton<ILearningSignalBuilder, LearningSignalBuilder>();
            services.AddSingleton<IOutcomeService, OutcomeService>();
            services.AddSingleton<OutcomeVerificationService>();
            services.AddSingleton<IBudgetGuardrailService, BudgetGuardrailService>();
            services.AddSingleton<IEvalHarnessService, EvalHarnessService>();
            services.AddSingleton<BudgetVerificationService>();
            services.AddSingleton<EvalVerificationService>();
            services.AddSingleton<IExternalArchiveImportContractValidator, ExternalArchiveImportContractValidator>();
            services.AddSingleton<IExternalArchiveProvenanceWeightingService, ExternalArchiveProvenanceWeightingService>();
            services.AddSingleton<IExternalArchiveLinkagePlanner, ExternalArchiveLinkagePlanner>();
            services.AddSingleton<IExternalArchivePreparationService, ExternalArchivePreparationService>();
            services.AddSingleton<IExternalArchiveIngestionService, ExternalArchiveIngestionService>();
            services.AddSingleton<ExternalArchiveVerificationService>();

            services.AddHttpClient<IMediaProcessor, TgAssistant.Processing.Media.OpenRouterMediaProcessor>();
            services.AddHttpClient<IVoiceParalinguisticsAnalyzer, TgAssistant.Processing.Media.OpenRouterVoiceParalinguisticsAnalyzer>();

            var claudeBaseUrl = config.GetSection(ClaudeSettings.Section).GetValue<string>("BaseUrl") ?? "https://openrouter.ai";
            var claudeApiKey = config.GetSection(ClaudeSettings.Section).GetValue<string>("ApiKey") ?? string.Empty;
            var analysisTimeoutSeconds = config.GetSection(AnalysisSettings.Section).GetValue<int>("HttpTimeoutSeconds");
            services.AddHttpClient<OpenRouterAnalysisService>("analysis", client =>
            {
                client.BaseAddress = new Uri(claudeBaseUrl);
                if (!string.IsNullOrWhiteSpace(claudeApiKey))
                {
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", claudeApiKey);
                }

                client.Timeout = TimeSpan.FromSeconds(Math.Max(30, analysisTimeoutSeconds));
            });
            services.AddHttpClient<ITextEmbeddingGenerator, OpenRouterEmbeddingService>("embedding", client =>
            {
                client.BaseAddress = new Uri(claudeBaseUrl);
                if (!string.IsNullOrWhiteSpace(claudeApiKey))
                {
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", claudeApiKey);
                }

                client.Timeout = TimeSpan.FromSeconds(60);
            });
            services.AddHttpClient<Neo4jSyncWorkerService>();
            services.AddSingleton<ExtractionSchemaValidator>();
            services.AddSingleton<MessageContentBuilder>();
            services.AddSingleton<AnalysisContextBuilder>();
            services.AddSingleton<SummaryHistoricalRetrievalService>();
            services.AddSingleton<Stage5VerificationService>();
            services.AddSingleton<ExtractionApplier>();
            services.AddSingleton<ExpensivePassResolver>();
            services.AddSingleton<IBotChatService, BotChatService>();

            services.AddSingleton<TelegramDesktopArchiveParser>();

            services.AddHostedService<TelegramListenerService>();
            services.AddHostedService<TelegramBotHostedService>();
            services.AddHostedService<HistoryBackfillService>();
            services.AddHostedService<BatchWorkerService>();
            services.AddHostedService<ArchiveImportWorkerService>();
            services.AddHostedService<ArchiveMediaProcessorService>();
            services.AddHostedService<VoiceParalinguisticsWorkerService>();
            services.AddHostedService<EditDiffAnalysisWorkerService>();
            services.AddHostedService<AnalysisWorkerService>();
            services.AddHostedService<DialogSummaryWorkerService>();
            services.AddHostedService<EntityEmbeddingWorkerService>();
            services.AddHostedService<FactEmbeddingBackfillWorkerService>();
            services.AddHostedService<Neo4jSyncWorkerService>();
            services.AddHostedService<EntityMergeCandidateWorkerService>();
            services.AddHostedService<EntityMergeCommandWorkerService>();
            services.AddHostedService<FactReviewCommandWorkerService>();
            // Temporarily disabled: daily cold-path crystallization adds extra chat/embedding traffic during Stage5 runs.
            // services.AddHostedService<DailyKnowledgeCrystallizationWorkerService>();
            services.AddHostedService<Stage5MetricsWorkerService>();
            services.AddHostedService<MaintenanceWorkerService>();
        });

    var host = builder.Build();

    using (var scope = host.Services.CreateScope())
    {
        if (runHealthCheck)
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TgAssistantDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync(timeoutCts.Token);
            if (!await db.Database.CanConnectAsync(timeoutCts.Token))
            {
                throw new InvalidOperationException("Database connectivity check failed.");
            }

            var redis = scope.ServiceProvider.GetRequiredService<IConnectionMultiplexer>();
            var redisDb = redis.GetDatabase();
            _ = await redisDb.PingAsync();

            Log.Information("Healthcheck passed: database and redis are reachable.");
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

        if (runEvalSmoke)
        {
            var verificationService = scope.ServiceProvider.GetRequiredService<EvalVerificationService>();
            await verificationService.RunAsync();
            Log.Information("Eval smoke run requested via --eval-smoke. Exiting after successful verification.");
            return;
        }

        if (runExternalArchiveSmoke)
        {
            var verificationService = scope.ServiceProvider.GetRequiredService<ExternalArchiveVerificationService>();
            await verificationService.RunAsync();
            Log.Information("External archive smoke run requested via --external-archive-smoke. Exiting after successful verification.");
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
