using Microsoft.EntityFrameworkCore;
using Serilog;
using StackExchange.Redis;
using System.Net.Http.Headers;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Interfaces;
using TgAssistant.Infrastructure.Database;
using TgAssistant.Infrastructure.Database.Ef;
using TgAssistant.Infrastructure.Redis;
using TgAssistant.Intelligence.Stage5;
using TgAssistant.Intelligence.Stage6;
using TgAssistant.Processing.Archive;
using TgAssistant.Processing.Workers;
using TgAssistant.Telegram.Bot;
using TgAssistant.Telegram.Listener;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Warning()
    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("System", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("TgAssistant.Intelligence.Stage5.OpenRouterAnalysisService", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("TgAssistant.Processing.Media.OpenRouterMediaProcessor", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("TgAssistant.Processing.Media.OpenRouterVoiceParalinguisticsAnalyzer", Serilog.Events.LogEventLevel.Warning)
    .WriteTo.Async(a => a.Console())
    .CreateLogger();


try
{
    Log.Information("Starting Telegram Assistant...");

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
            services.AddHostedService<EntityEmbeddingWorkerService>();
            services.AddHostedService<FactEmbeddingBackfillWorkerService>();
            services.AddHostedService<Neo4jSyncWorkerService>();
            services.AddHostedService<EntityMergeCandidateWorkerService>();
            services.AddHostedService<EntityMergeCommandWorkerService>();
            services.AddHostedService<FactReviewCommandWorkerService>();
            services.AddHostedService<DailyKnowledgeCrystallizationWorkerService>();
            services.AddHostedService<Stage5MetricsWorkerService>();
            services.AddHostedService<MaintenanceWorkerService>();
        });

    var host = builder.Build();

    using (var scope = host.Services.CreateScope())
    {
        var dbInit = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();
        await dbInit.InitializeAsync();

        var redisQueue = scope.ServiceProvider.GetRequiredService<RedisMessageQueue>();
        await redisQueue.InitializeAsync();
    }

    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
