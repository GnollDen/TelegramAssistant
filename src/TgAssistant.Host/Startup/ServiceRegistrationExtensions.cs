using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace TgAssistant.Host.Startup;

public static partial class ServiceRegistrationExtensions
{
    public static IServiceCollection AddTelegramAssistantCompositionRoot(
        this IServiceCollection services,
        IConfiguration config,
        RuntimeRoleSelection runtimeRoleSelection,
        bool includeLegacyStage6Diagnostics = false,
        bool includeLegacyStage6ClusterDiagnostics = false)
    {
        services
            .AddTelegramAssistantSettings(config)
            .AddTelegramAssistantInfrastructure(config)
            .AddTelegramAssistantDomainServices(
                includeLegacyStage6Diagnostics,
                includeLegacyStage6ClusterDiagnostics)
            .AddTelegramAssistantHttpClients(config)
            .AddRuntimeRoleSelection(runtimeRoleSelection)
            .AddTelegramAssistantHostedServices(runtimeRoleSelection);

        return services;
    }
}
