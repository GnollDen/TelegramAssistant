using Microsoft.Extensions.Configuration;

namespace TgAssistant.Host.Startup;

[Flags]
public enum RuntimeWorkloadRole
{
    None = 0,
    Ingest = 1 << 0,
    Stage5 = 1 << 1,
    Stage6 = 1 << 2,
    Web = 1 << 3,
    Ops = 1 << 4,
    Maintenance = 1 << 5,
    Mcp = 1 << 6,
    All = Ingest | Stage5 | Stage6 | Web | Ops | Maintenance | Mcp
}

public sealed record RuntimeRoleSelection(RuntimeWorkloadRole Roles, string Source, string RawValue)
{
    public bool Has(RuntimeWorkloadRole role) => (Roles & role) == role;
}

public static class RuntimeRoleParser
{
    private static readonly string[] RuntimeRolePrefixes =
    {
        "--runtime-role=",
        "--runtime-roles="
    };

    public static RuntimeRoleSelection Parse(string[] args, IConfiguration config)
    {
        var (rawArg, argPrefix) = TryGetArgValue(args);
        var rawConfig = config.GetValue<string>("Runtime:Roles")
            ?? config.GetValue<string>("Runtime:Role");

        if (string.IsNullOrWhiteSpace(rawArg) && string.IsNullOrWhiteSpace(rawConfig))
        {
            return new RuntimeRoleSelection(RuntimeWorkloadRole.All, "default", "all");
        }

        var rawRoles = string.IsNullOrWhiteSpace(rawArg) ? rawConfig! : rawArg!;
        var source = string.IsNullOrWhiteSpace(rawArg) ? "config" : $"arg:{argPrefix}";
        var roles = RuntimeWorkloadRole.None;
        foreach (var token in rawRoles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            roles |= ParseRoleToken(token);
        }

        return roles == RuntimeWorkloadRole.None
            ? new RuntimeRoleSelection(RuntimeWorkloadRole.All, $"{source}-invalid-fallback", rawRoles)
            : new RuntimeRoleSelection(roles, source, rawRoles);
    }

    private static RuntimeWorkloadRole ParseRoleToken(string token) => token.ToLowerInvariant() switch
    {
        "all" => RuntimeWorkloadRole.All,
        "ingest" => RuntimeWorkloadRole.Ingest,
        "stage5" => RuntimeWorkloadRole.Stage5,
        "stage6" => RuntimeWorkloadRole.Stage6,
        "web" => RuntimeWorkloadRole.Web,
        "ops" => RuntimeWorkloadRole.Ops,
        "maintenance" => RuntimeWorkloadRole.Maintenance,
        "mcp" => RuntimeWorkloadRole.Mcp,
        _ => RuntimeWorkloadRole.None
    };

    private static (string? Value, string Prefix) TryGetArgValue(string[] args)
    {
        foreach (var prefix in RuntimeRolePrefixes)
        {
            var arg = args.FirstOrDefault(x => x.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            if (arg is not null)
            {
                return (arg[prefix.Length..], prefix);
            }
        }

        return (null, "none");
    }
}
