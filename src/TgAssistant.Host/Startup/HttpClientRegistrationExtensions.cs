using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TgAssistant.Core.Interfaces;
using TgAssistant.Infrastructure.LlmGateway;
using TgAssistant.Intelligence.Stage5;
using TgAssistant.Processing.Workers;

namespace TgAssistant.Host.Startup;

public static partial class ServiceRegistrationExtensions
{
    public static IServiceCollection AddTelegramAssistantHttpClients(this IServiceCollection services, IConfiguration config)
    {
        services.AddHttpClient<IMediaProcessor, TgAssistant.Processing.Media.OpenRouterMediaProcessor>();
        services.AddHttpClient<IVoiceParalinguisticsAnalyzer, TgAssistant.Processing.Media.OpenRouterVoiceParalinguisticsAnalyzer>();
        services.AddHttpClient<OpenRouterAnalysisService>();
        services.AddHttpClient<ITextEmbeddingGenerator, OpenRouterEmbeddingService>();

        services.AddHttpClient<Neo4jSyncWorkerService>();
        services.AddHttpClient<CodexLbChatProviderClient>();
        services.AddHttpClient<OpenRouterProviderClient>();
        services.AddTransient<ILlmProviderClient>(sp => sp.GetRequiredService<CodexLbChatProviderClient>());
        services.AddTransient<ILlmProviderClient>(sp => sp.GetRequiredService<OpenRouterProviderClient>());
        services.AddSingleton<LlmGatewayMetrics>();
        services.AddTransient<ILlmGateway, LlmGatewayService>();
        services.AddSingleton<ILlmRoutingPolicy, DefaultLlmRoutingPolicy>();

        return services;
    }
}
