using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using System.Text.Json;
using TgAssistant.Core.Configuration;
using TgAssistant.Infrastructure.Database.Ef;
using TgAssistant.Infrastructure.LlmGateway;
using TgAssistant.Host.Launch;
using TgAssistant.Host.Startup;
using TgAssistant.Intelligence.Stage5;

namespace TgAssistant.Host.Health;

public static class RuntimeHealthProbeRunner
{
    private static readonly string[] RequiredPhaseBGateSteps =
    [
        "phase-b-stage-semantic-contract-proof",
        "phase-b-temporal-person-state-proof",
        "phase-b-person-history-proof",
        "phase-b-current-world-proof",
        "phase-b-conditional-modeling-proof",
        "phase-b-iterative-reintegration-proof",
        "phase-b-ai-conflict-session-v1-proof",
        "phase-b-stage7-dossier-profile-smoke",
        "phase-b-stage7-timeline-smoke",
        "phase-b-stage8-recompute-smoke"
    ];

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

        ValidateLlmGatewayReadiness(services);

        var dbFactory = services.GetRequiredService<IDbContextFactory<TgAssistantDbContext>>();
        await using (var db = await dbFactory.CreateDbContextAsync(token))
        {
            if (!await db.Database.CanConnectAsync(token))
            {
                throw new InvalidOperationException("Readiness failed: database connectivity check failed.");
            }
        }

        await ValidateRedisQueueReadinessAsync(services, token);

        ValidateCoordinationAssumptions(services, selection);

        if (selection.Has(RuntimeWorkloadRole.Stage5))
        {
            var stage5VerificationService = services.GetRequiredService<Stage5VerificationService>();
            await stage5VerificationService.RunAsync(token);
        }

        EnsurePhaseBAdmissionMarkerExists();
    }

    private static void EnsurePhaseBAdmissionMarkerExists()
    {
        var markerPath = Path.Combine(
            HostArtifactsPathResolver.ResolveHostArtifactsRoot(),
            "phase-b",
            "launch-smoke",
            "phase-b-launch-gate.marker.json");
        if (!File.Exists(markerPath))
        {
            throw new InvalidOperationException(
                $"Readiness failed: phase-b launch gate marker is missing at '{markerPath}'. Run --launch-smoke first.");
        }

        using var document = JsonDocument.Parse(File.ReadAllText(markerPath));
        var root = document.RootElement;
        if (!root.TryGetProperty("passed", out var passedNode) || !passedNode.GetBoolean())
        {
            throw new InvalidOperationException("Readiness failed: phase-b launch gate marker is not marked as passed.");
        }

        if (!root.TryGetProperty("generatedAtUtc", out var generatedAtNode)
            || !DateTime.TryParse(generatedAtNode.GetString(), out var generatedAtUtc))
        {
            throw new InvalidOperationException("Readiness failed: phase-b launch gate marker missing generatedAtUtc.");
        }

        var generatedAtUtcNormalized = DateTime.SpecifyKind(generatedAtUtc, DateTimeKind.Utc);
        if (generatedAtUtcNormalized < DateTime.UtcNow.AddHours(-24))
        {
            throw new InvalidOperationException(
                $"Readiness failed: phase-b launch gate marker is stale ({generatedAtUtcNormalized:O}).");
        }

        if (!root.TryGetProperty("requiredPhaseBGateSteps", out var stepsNode) || stepsNode.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Readiness failed: phase-b launch gate marker missing required step list.");
        }

        var recordedSteps = stepsNode.EnumerateArray()
            .Where(x => x.ValueKind == JsonValueKind.String)
            .Select(x => x.GetString() ?? string.Empty)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var requiredStep in RequiredPhaseBGateSteps)
        {
            if (!recordedSteps.Contains(requiredStep))
            {
                throw new InvalidOperationException(
                    $"Readiness failed: phase-b launch gate marker does not include required step '{requiredStep}'.");
            }
        }
    }

    private static async Task ValidateRedisQueueReadinessAsync(IServiceProvider services, CancellationToken ct)
    {
        var redisSettings = services.GetRequiredService<IOptions<RedisSettings>>().Value;
        if (string.IsNullOrWhiteSpace(redisSettings.StreamName))
        {
            throw new InvalidOperationException("Readiness failed: Redis:StreamName must be configured.");
        }

        if (string.IsNullOrWhiteSpace(redisSettings.ConsumerGroup))
        {
            throw new InvalidOperationException("Readiness failed: Redis:ConsumerGroup must be configured.");
        }

        var redis = services.GetRequiredService<IConnectionMultiplexer>();
        var redisDb = redis.GetDatabase();
        _ = await redisDb.PingAsync();

        var streamName = redisSettings.StreamName.Trim();
        var groupName = redisSettings.ConsumerGroup.Trim();
        var streamExists = await redisDb.KeyExistsAsync(streamName);
        if (!streamExists)
        {
            throw new InvalidOperationException(
                $"Readiness failed: Redis stream '{streamName}' does not exist.");
        }

        var groups = await redisDb.StreamGroupInfoAsync(streamName);
        if (!groups.Any(x => string.Equals(x.Name, groupName, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                $"Readiness failed: Redis consumer group '{groupName}' does not exist on stream '{streamName}'.");
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

    private static void ValidateLlmGatewayReadiness(IServiceProvider services)
    {
        var gatewaySettings = services.GetRequiredService<IOptions<LlmGatewaySettings>>().Value;
        try
        {
            LlmGatewaySettingsValidator.ValidateOrThrow(gatewaySettings);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentNullException)
        {
            throw new InvalidOperationException($"Readiness failed: gateway configuration invalid. {ex.Message}", ex);
        }
    }
}
