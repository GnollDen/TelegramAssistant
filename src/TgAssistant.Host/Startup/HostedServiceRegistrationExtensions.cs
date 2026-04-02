using Microsoft.Extensions.DependencyInjection;
using TgAssistant.Intelligence.Stage5;
using TgAssistant.Processing.Archive;
using TgAssistant.Processing.Workers;
using TgAssistant.Telegram.Listener;

namespace TgAssistant.Host.Startup;

public static class HostedServiceRegistrationExtensions
{
    public static IServiceCollection AddRuntimeRoleSelection(this IServiceCollection services, RuntimeRoleSelection selection)
    {
        services.AddSingleton(selection);
        return services;
    }

    public static IServiceCollection AddTelegramAssistantHostedServices(this IServiceCollection services, RuntimeRoleSelection selection)
    {
        if (selection.Has(RuntimeWorkloadRole.Ingest))
        {
            services.AddIngestHostedServices();
        }

        if (selection.Has(RuntimeWorkloadRole.Stage5))
        {
            services.AddStage5HostedServices();
        }

        if (selection.Has(RuntimeWorkloadRole.Mcp))
        {
            services.AddMcpHostedServices();
        }

        if (selection.Has(RuntimeWorkloadRole.Ops))
        {
            services.AddOpsHostedServices();
        }

        if (selection.Has(RuntimeWorkloadRole.Maintenance))
        {
            services.AddMaintenanceHostedServices();
        }

        return services;
    }

    private static IServiceCollection AddIngestHostedServices(this IServiceCollection services)
    {
        services.AddHostedService<TelegramListenerService>();
        services.AddHostedService<HistoryBackfillService>();
        services.AddHostedService<BatchWorkerService>();
        services.AddHostedService<ArchiveImportWorkerService>();
        services.AddHostedService<ArchiveMediaProcessorService>();
        services.AddHostedService<VoiceParalinguisticsWorkerService>();
        return services;
    }

    private static IServiceCollection AddStage5HostedServices(this IServiceCollection services)
    {
        services.AddHostedService<EditDiffAnalysisWorkerService>();
        services.AddHostedService<AnalysisWorkerService>();
        services.AddHostedService<DialogSummaryWorkerService>();
        services.AddHostedService<EntityEmbeddingWorkerService>();
        services.AddHostedService<FactEmbeddingBackfillWorkerService>();
        services.AddHostedService<EntityMergeCandidateWorkerService>();
        services.AddHostedService<EntityMergeCommandWorkerService>();
        services.AddHostedService<FactReviewCommandWorkerService>();
        // Temporarily disabled: daily cold-path crystallization adds extra chat/embedding traffic during Stage5 runs.
        // services.AddHostedService<DailyKnowledgeCrystallizationWorkerService>();
        services.AddHostedService<Stage5MetricsWorkerService>();
        return services;
    }

    private static IServiceCollection AddMcpHostedServices(this IServiceCollection services)
    {
        // MCP server is deployed as a standalone TypeScript process.
        return services;
    }

    private static IServiceCollection AddOpsHostedServices(this IServiceCollection services)
    {
        // Keep reusable operational sync in the active baseline; bot workflow is legacy-only.
        services.AddHostedService<Neo4jSyncWorkerService>();
        return services;
    }

    private static IServiceCollection AddMaintenanceHostedServices(this IServiceCollection services)
    {
        services.AddHostedService<MaintenanceWorkerService>();
        return services;
    }
}
