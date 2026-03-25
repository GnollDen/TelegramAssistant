using Microsoft.Extensions.Configuration;

namespace TgAssistant.Host.Startup;

public static class RuntimeStartupGuard
{
    public static void Validate(IConfiguration config, RuntimeRoleSelection selection)
    {
        ValidateRequiredConnectionStrings(config);
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

        if (selection.Has(RuntimeWorkloadRole.Ops))
        {
            var botToken = config.GetValue<string>("Telegram:BotToken")?.Trim() ?? string.Empty;
            var botOwnerId = config.GetValue<long>("BotChat:OwnerId");
            var telegramOwnerId = config.GetValue<long>("Telegram:OwnerUserId");
            var effectiveOwnerId = botOwnerId > 0 ? botOwnerId : telegramOwnerId;
            if (string.IsNullOrWhiteSpace(botToken))
            {
                throw new InvalidOperationException("Ops role requires Telegram:BotToken.");
            }

            if (effectiveOwnerId <= 0)
            {
                throw new InvalidOperationException(
                    "Ops role requires owner identity via BotChat:OwnerId or Telegram:OwnerUserId.");
            }
        }

        if (selection.Has(RuntimeWorkloadRole.Web))
        {
            var webUrl = config.GetValue<string>("Web:Url")?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(webUrl))
            {
                throw new InvalidOperationException("Web role requires Web:Url.");
            }

            var requireAccessToken = config.GetValue<bool?>("Web:RequireOperatorAccessToken") ?? true;
            if (requireAccessToken)
            {
                var accessToken = config.GetValue<string>("Web:OperatorAccessToken")?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(accessToken))
                {
                    throw new InvalidOperationException(
                        "Web role requires Web:OperatorAccessToken when Web:RequireOperatorAccessToken=true.");
                }

                if (LooksLikePlaceholderSecret(accessToken))
                {
                    throw new InvalidOperationException(
                        "Web:OperatorAccessToken contains placeholder or unsafe secret material.");
                }
            }
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
