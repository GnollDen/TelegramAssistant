using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using TgAssistant.Core.Configuration;
using TgAssistant.Infrastructure.Database.Ef;
using TgAssistant.Infrastructure.Redis;
using TgAssistant.Host.Startup;
using TgAssistant.Intelligence.Stage5;

namespace TgAssistant.Host.Health;

public static class RuntimeHealthProbeRunner
{
    public static Task RunLivenessCheckAsync(RuntimeRoleSelection selection, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public static async Task RunReadinessCheckAsync(
        IServiceProvider services,
        RuntimeRoleSelection selection,
        CancellationToken ct = default)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(20));
        var token = timeoutCts.Token;

        var dbFactory = services.GetRequiredService<IDbContextFactory<TgAssistantDbContext>>();
        await using (var db = await dbFactory.CreateDbContextAsync(token))
        {
            if (!await db.Database.CanConnectAsync(token))
            {
                throw new InvalidOperationException("Readiness failed: database connectivity check failed.");
            }
        }

        var redis = services.GetRequiredService<IConnectionMultiplexer>();
        var redisDb = redis.GetDatabase();
        _ = await redisDb.PingAsync();

        var redisQueue = services.GetRequiredService<RedisMessageQueue>();
        await redisQueue.InitializeAsync();

        ValidateCoordinationAssumptions(services, selection);

        if (selection.Has(RuntimeWorkloadRole.Stage5) || selection.Has(RuntimeWorkloadRole.Stage6))
        {
            var stage5VerificationService = services.GetRequiredService<Stage5VerificationService>();
            await stage5VerificationService.RunAsync(token);
        }
    }

    private static void ValidateCoordinationAssumptions(IServiceProvider services, RuntimeRoleSelection selection)
    {
        if (!selection.Has(RuntimeWorkloadRole.Ingest) && !selection.Has(RuntimeWorkloadRole.Stage5))
        {
            return;
        }

        var coordinationSettings = services.GetRequiredService<IOptions<ChatCoordinationSettings>>().Value;
        if (!coordinationSettings.Enabled)
        {
            throw new InvalidOperationException("Readiness failed: ChatCoordination:Enabled must be true for ingest/stage5 roles.");
        }

        if (!coordinationSettings.PhaseGuardsEnabled)
        {
            throw new InvalidOperationException(
                "Readiness failed: ChatCoordination:PhaseGuardsEnabled must be true for ingest/stage5 roles.");
        }
    }
}
