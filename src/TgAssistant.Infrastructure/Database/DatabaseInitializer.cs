using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using TgAssistant.Core.Configuration;

namespace TgAssistant.Infrastructure.Database;

public class DatabaseInitializer
{
    private const long MigrationLockId = 642871345921780154;
    private static readonly string ResourcePrefix = $"{typeof(DatabaseInitializer).Namespace}.Migrations.";

    private readonly DatabaseSettings _settings;
    private readonly ILogger<DatabaseInitializer> _logger;

    public DatabaseInitializer(IOptions<DatabaseSettings> settings, ILogger<DatabaseInitializer> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_settings.ConnectionString);
        await conn.OpenAsync(ct);
        await AcquireMigrationLockAsync(conn, ct);

        try
        {
            await EnsureMigrationsTableAsync(conn, ct);

            var appliedMigrations = await LoadAppliedMigrationsAsync(conn, ct);
            var pendingCount = 0;

            foreach (var migration in LoadEmbeddedMigrations())
            {
                if (appliedMigrations.TryGetValue(migration.Id, out var appliedChecksum))
                {
                    if (!string.Equals(appliedChecksum, migration.Checksum, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"Database migration checksum mismatch for '{migration.Id}'. " +
                            "Do not edit applied migrations; create a new migration instead.");
                    }

                    continue;
                }

                await ApplyMigrationAsync(conn, migration, ct);
                pendingCount++;
                _logger.LogInformation("Applied database migration {MigrationId}", migration.Id);
            }

            _logger.LogInformation(
                pendingCount == 0
                    ? "Database schema is up to date"
                    : "Database schema initialized via {Count} migration(s)",
                pendingCount);
        }
        finally
        {
            await ReleaseMigrationLockAsync(conn, ct);
        }
    }

    private static async Task EnsureMigrationsTableAsync(NpgsqlConnection conn, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS schema_migrations (
                id TEXT PRIMARY KEY,
                checksum TEXT NOT NULL,
                applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );
            """;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<Dictionary<string, string>> LoadAppliedMigrationsAsync(NpgsqlConnection conn, CancellationToken ct)
    {
        var applied = new Dictionary<string, string>(StringComparer.Ordinal);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, checksum
            FROM schema_migrations
            ORDER BY id;
            """;

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            applied[reader.GetString(0)] = reader.GetString(1);
        }

        return applied;
    }

    private IEnumerable<SqlMigration> LoadEmbeddedMigrations()
    {
        var assembly = typeof(DatabaseInitializer).Assembly;
        var resources = assembly
            .GetManifestResourceNames()
            .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal) && name.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        if (resources.Length == 0)
        {
            throw new InvalidOperationException("No embedded database migrations were found.");
        }

        foreach (var resourceName in resources)
        {
            using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Migration resource '{resourceName}' could not be opened.");
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var sql = reader.ReadToEnd();
            var id = resourceName[ResourcePrefix.Length..];
            yield return new SqlMigration(id, sql, ComputeChecksum(sql));
        }
    }

    private static async Task ApplyMigrationAsync(NpgsqlConnection conn, SqlMigration migration, CancellationToken ct)
    {
        await using var tx = await conn.BeginTransactionAsync(ct);

        await using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = migration.Sql;
            await cmd.ExecuteNonQueryAsync(ct);
        }

        await using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO schema_migrations (id, checksum)
                VALUES (@id, @checksum);
                """;
            cmd.Parameters.AddWithValue("id", migration.Id);
            cmd.Parameters.AddWithValue("checksum", migration.Checksum);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
    }

    private static string ComputeChecksum(string sql)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sql));
        return Convert.ToHexString(hash);
    }

    private static async Task AcquireMigrationLockAsync(NpgsqlConnection conn, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT pg_advisory_lock(@lock_id);";
        cmd.Parameters.AddWithValue("lock_id", MigrationLockId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task ReleaseMigrationLockAsync(NpgsqlConnection conn, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT pg_advisory_unlock(@lock_id);";
        cmd.Parameters.AddWithValue("lock_id", MigrationLockId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private sealed record SqlMigration(string Id, string Sql, string Checksum);
}
