using Microsoft.Extensions.Configuration;
using TgAssistant.Core.Configuration;
using TgAssistant.Infrastructure.LlmGateway;

namespace TgAssistant.Host.Startup;

public static class RuntimeStartupGuard
{
    public static void Validate(IConfiguration config, RuntimeRoleSelection selection)
    {
        ValidateRequiredConnectionStrings(config);
        ValidateLlmGatewayStartupConfig(config);
        ValidateRoleSpecificConfig(config, selection);
    }

    private static void ValidateRequiredConnectionStrings(IConfiguration config)
    {
        var dbConnectionString = config.GetValue<string>("Database:ConnectionString")?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(dbConnectionString))
        {
            throw new InvalidOperationException("Database:ConnectionString is required for runtime startup.");
        }

        if (LooksLikePlaceholderSecret(dbConnectionString))
        {
            throw new InvalidOperationException(
                "Database:ConnectionString contains placeholder or unsafe secret material. Provide a real credential.");
        }

        var redisConnectionString = config.GetValue<string>("Redis:ConnectionString")?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(redisConnectionString))
        {
            throw new InvalidOperationException("Redis:ConnectionString is required for runtime startup.");
        }
    }

    private static void ValidateRoleSpecificConfig(IConfiguration config, RuntimeRoleSelection selection)
    {
        if (selection.Has(RuntimeWorkloadRole.Ingest))
        {
            var apiId = config.GetValue<int>("Telegram:ApiId");
            var apiHash = config.GetValue<string>("Telegram:ApiHash")?.Trim() ?? string.Empty;
            var phoneNumber = config.GetValue<string>("Telegram:PhoneNumber")?.Trim() ?? string.Empty;
            var ownerUserId = config.GetValue<long>("Telegram:OwnerUserId");
            if (apiId <= 0 || string.IsNullOrWhiteSpace(apiHash) || string.IsNullOrWhiteSpace(phoneNumber) || ownerUserId <= 0)
            {
                throw new InvalidOperationException(
                    "Ingest role requires Telegram:ApiId, Telegram:ApiHash, Telegram:PhoneNumber, and Telegram:OwnerUserId.");
            }
        }

    }

    private static void ValidateLlmGatewayStartupConfig(IConfiguration config)
    {
        var gatewaySettings = new LlmGatewaySettings();
        config.GetSection(LlmGatewaySettings.Section).Bind(gatewaySettings);

        try
        {
            LlmGatewaySettingsValidator.ValidateOrThrow(gatewaySettings);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentNullException)
        {
            throw new InvalidOperationException($"Runtime startup validation failed for LlmGateway: {ex.Message}", ex);
        }
    }

    private static bool LooksLikePlaceholderSecret(string value)
    {
        return value.Contains("changeme", StringComparison.OrdinalIgnoreCase)
            || value.Contains("your_strong_password_here", StringComparison.OrdinalIgnoreCase)
            || value.Contains("__required", StringComparison.OrdinalIgnoreCase)
            || value.Contains("placeholder", StringComparison.OrdinalIgnoreCase);
    }
}
