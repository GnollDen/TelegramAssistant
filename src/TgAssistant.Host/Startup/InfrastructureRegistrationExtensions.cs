using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Interfaces;
using TgAssistant.Infrastructure.Database;
using TgAssistant.Infrastructure.Database.Ef;
using TgAssistant.Infrastructure.Redis;

namespace TgAssistant.Host.Startup;

public static partial class ServiceRegistrationExtensions
{
    public static IServiceCollection AddTelegramAssistantInfrastructure(this IServiceCollection services, IConfiguration config)
    {
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

        return services;
    }
}
