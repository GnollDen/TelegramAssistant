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
        var redisConnectionString = config.GetSection(RedisSettings.Section).GetValue<string>("ConnectionString");
        if (string.IsNullOrWhiteSpace(redisConnectionString))
        {
            throw new InvalidOperationException("Redis:ConnectionString is required.");
        }

        services.AddSingleton<IConnectionMultiplexer>(_ =>
            ConnectionMultiplexer.Connect(redisConnectionString));

        services.AddSingleton<RedisMessageQueue>();
        services.AddSingleton<IMessageQueue>(sp => sp.GetRequiredService<RedisMessageQueue>());

        var connectionString = config.GetSection(DatabaseSettings.Section).GetValue<string>("ConnectionString");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Database:ConnectionString is required.");
        }

        services.AddDbContextFactory<TgAssistantDbContext>(opt =>
        {
            opt.UseNpgsql(connectionString);
        });

        services.AddSingleton<DatabaseInitializer>();

        return services;
    }
}
