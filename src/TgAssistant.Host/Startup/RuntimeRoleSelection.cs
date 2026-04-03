using Microsoft.Extensions.Configuration;

namespace TgAssistant.Host.Startup;

[Flags]
public enum RuntimeWorkloadRole
{
    None = 0,
    Ingest = 1 << 0,
    Stage5 = 1 << 1,
    Ops = 1 << 2,
    Maintenance = 1 << 3,
    All = Ingest | Stage5 | Ops | Maintenance
}

public sealed record RuntimeRoleSelection(RuntimeWorkloadRole Roles, string Source, string RawValue)
{
    public bool Has(RuntimeWorkloadRole role) => (Roles & role) == role;

    public static string[] AllowedCombinationDisplay { get; } =
    {
        "ingest",
        "ingest,ops",
        "stage5",
        "stage5,maintenance",
        "ingest,stage5,maintenance,ops"
    };
}

public static class RuntimeRoleParser
{
    private static readonly string[] RuntimeRolePrefixes =
    {
        "--runtime-role=",
        "--runtime-roles="
    };

    private static readonly HashSet<RuntimeWorkloadRole> AllowedCombinations = new()
    {
        RuntimeWorkloadRole.Ingest,
        RuntimeWorkloadRole.Ingest | RuntimeWorkloadRole.Ops,
        RuntimeWorkloadRole.Stage5,
        RuntimeWorkloadRole.Stage5 | RuntimeWorkloadRole.Maintenance,
        RuntimeWorkloadRole.Ingest | RuntimeWorkloadRole.Stage5 | RuntimeWorkloadRole.Maintenance | RuntimeWorkloadRole.Ops
    };

    public static RuntimeRoleSelection Parse(string[] args, IConfiguration config)
    {
        var (rawArg, argPrefix) = TryGetArgValue(args);
        var rawConfig = config.GetValue<string>("Runtime:Roles")
            ?? config.GetValue<string>("Runtime:Role");

        if (string.IsNullOrWhiteSpace(rawArg) && string.IsNullOrWhiteSpace(rawConfig))
        {
            throw new InvalidOperationException(
                "Runtime role is required. Set Runtime:Role/Runtime:Roles or pass --runtime-role=...");
        }

        var rawRoles = string.IsNullOrWhiteSpace(rawArg) ? rawConfig! : rawArg!;
        var source = string.IsNullOrWhiteSpace(rawArg) ? "config" : $"arg:{argPrefix}";
        var roles = ParseRolesOrThrow(rawRoles, source);
        if (!AllowedCombinations.Contains(roles))
        {
            throw new InvalidOperationException(
                $"Runtime role combination '{rawRoles}' is not allowed for Sprint 1. Allowed combinations: {string.Join("; ", RuntimeRoleSelection.AllowedCombinationDisplay)}.");
        }

        return new RuntimeRoleSelection(roles, source, rawRoles);
    }

    private static RuntimeWorkloadRole ParseRolesOrThrow(string rawRoles, string source)
    {
        var roles = RuntimeWorkloadRole.None;
        var unknownTokens = new List<string>();
        var tokens = rawRoles
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(token => token.ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (tokens.Count == 0)
        {
            throw new InvalidOperationException($"Runtime role input from {source} is empty.");
        }

        foreach (var token in tokens)
        {
            switch (token)
            {
                case "ingest":
                    roles |= RuntimeWorkloadRole.Ingest;
                    break;
                case "stage5":
                    roles |= RuntimeWorkloadRole.Stage5;
                    break;
                case "ops":
                    roles |= RuntimeWorkloadRole.Ops;
                    break;
                case "maintenance":
                    roles |= RuntimeWorkloadRole.Maintenance;
                    break;
                default:
                    unknownTokens.Add(token);
                    break;
            }
        }

        if (unknownTokens.Count > 0)
        {
            throw new InvalidOperationException(
                $"Unknown or disallowed runtime role token(s) from {source}: {string.Join(", ", unknownTokens)}.");
        }

        if (roles == RuntimeWorkloadRole.None)
        {
            throw new InvalidOperationException($"Runtime role input from {source} resolved to no valid roles.");
        }

        return roles;
    }

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
