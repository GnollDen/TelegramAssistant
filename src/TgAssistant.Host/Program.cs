using Serilog;
using StackExchange.Redis;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Interfaces;
using TgAssistant.Infrastructure.Database;
using TgAssistant.Infrastructure.Redis;
using TgAssistant.Processing.Media;
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

            // Configuration
            services.Configure<TelegramSettings>(config.GetSection(TelegramSettings.Section));
            services.Configure<RedisSettings>(config.GetSection(RedisSettings.Section));
            services.Configure<DatabaseSettings>(config.GetSection(DatabaseSettings.Section));
            services.Configure<GeminiSettings>(config.GetSection(GeminiSettings.Section));
            services.Configure<ClaudeSettings>(config.GetSection(ClaudeSettings.Section));
            services.Configure<BatchWorkerSettings>(config.GetSection(BatchWorkerSettings.Section));
            services.Configure<MediaSettings>(config.GetSection(MediaSettings.Section));

            // Redis
            services.AddSingleton<IConnectionMultiplexer>(_ =>
                ConnectionMultiplexer.Connect(
                    config.GetSection(RedisSettings.Section)
                        .GetValue<string>("ConnectionString") ?? "localhost:6379"));
            services.AddSingleton<RedisMessageQueue>();
            services.AddSingleton<IMessageQueue>(sp => sp.GetRequiredService<RedisMessageQueue>());

            // Database
            services.AddSingleton<DatabaseInitializer>();
            services.AddSingleton<IMessageRepository, MessageRepository>();

            // Media Processing (stub for now)
            services.AddSingleton<IMediaProcessor, StubMediaProcessor>();

            // Hosted Services
            services.AddHostedService<TelegramListenerService>();
            services.AddHostedService<BatchWorkerService>();
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