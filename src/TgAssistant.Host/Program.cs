using Microsoft.EntityFrameworkCore;
using Serilog;
using StackExchange.Redis;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Interfaces;
using TgAssistant.Infrastructure.Database;
using TgAssistant.Infrastructure.Database.Ef;
using TgAssistant.Infrastructure.Redis;
using TgAssistant.Intelligence.Stage5;
using TgAssistant.Processing.Archive;
using TgAssistant.Processing.Workers;
using TgAssistant.Telegram.Listener;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.Console()
    .WriteTo.File("logs/tgassistant-.log", rollingInterval: RollingInterval.Day)
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
            services.Configure<ArchiveImportSettings>(config.GetSection(ArchiveImportSettings.Section));

            services.Configure<AnalysisSettings>(config.GetSection(AnalysisSettings.Section));

            services.PostConfigure<TelegramSettings>(s =>
            {
                if (!string.IsNullOrEmpty(s.MonitoredChats) && s.MonitoredChatIds.Count == 0)
                {
                    s.MonitoredChatIds = s.MonitoredChats
                        .Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(id => long.Parse(id.Trim()))
                        .ToList();
                }
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
            services.AddSingleton<IEntityRepository, EntityRepository>();
            services.AddSingleton<IFactRepository, FactRepository>();
            services.AddSingleton<IRelationshipRepository, RelationshipRepository>();
            services.AddSingleton<ISummaryRepository, SummaryRepository>();

            services.AddHttpClient<IMediaProcessor, TgAssistant.Processing.Media.OpenRouterMediaProcessor>();


            services.AddHttpClient<OpenRouterAnalysisService>();

            services.AddSingleton<TelegramDesktopArchiveParser>();

            services.AddHostedService<TelegramListenerService>();
            services.AddHostedService<BatchWorkerService>();
            services.AddHostedService<ArchiveImportWorkerService>();
            services.AddHostedService<ArchiveMediaProcessorService>();
            services.AddHostedService<AnalysisWorkerService>();
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
