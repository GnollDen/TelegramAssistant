using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Headers;
using TgAssistant.Core.Configuration;
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
        services.AddHttpClient<CodexLbChatProviderClient>();
        services.AddHttpClient<OpenRouterProviderClient>();
        services.AddTransient<ILlmProviderClient>(sp => sp.GetRequiredService<CodexLbChatProviderClient>());
        services.AddTransient<ILlmProviderClient>(sp => sp.GetRequiredService<OpenRouterProviderClient>());
        services.AddTransient<ILlmGateway, LlmGatewayService>();
        services.AddSingleton<ILlmRoutingPolicy, DefaultLlmRoutingPolicy>();

        return services;
    }
}
