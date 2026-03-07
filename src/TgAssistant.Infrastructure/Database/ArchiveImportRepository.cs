using Dapper;
using Microsoft.Extensions.Options;
using Npgsql;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Infrastructure.Database;

public class ArchiveImportRepository : IArchiveImportRepository
{
    private readonly string _connectionString;

    public ArchiveImportRepository(IOptions<DatabaseSettings> settings)
    {
        _connectionString = settings.Value.ConnectionString;
    }

    private NpgsqlConnection CreateConnection() => new(_connectionString);

    public async Task<ArchiveImportRun?> GetRunningRunAsync(string sourcePath, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<ArchiveImportRun>(new CommandDefinition(
            """
            SELECT id, source_path AS SourcePath, status, last_message_index AS LastMessageIndex,
                   imported_messages AS ImportedMessages, queued_media AS QueuedMedia,
                   total_messages AS TotalMessages, total_media AS TotalMedia,
                   estimated_cost_usd AS EstimatedCostUsd, error,
                   created_at AS CreatedAt, updated_at AS UpdatedAt
            FROM archive_import_runs
            WHERE source_path = @SourcePath AND status = @Status
            ORDER BY created_at DESC
            LIMIT 1
            """,
            new { SourcePath = sourcePath, Status = ArchiveImportRunStatus.Running },
            cancellationToken: ct));
    }

    public async Task<ArchiveImportRun> CreateRunAsync(ArchiveImportRun run, CancellationToken ct = default)
    {
        run.Id = run.Id == Guid.Empty ? Guid.NewGuid() : run.Id;

        await using var conn = CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO archive_import_runs (
                id, source_path, status, last_message_index,
                imported_messages, queued_media, total_messages,
                total_media, estimated_cost_usd, error
            ) VALUES (
                @Id, @SourcePath, @Status, @LastMessageIndex,
                @ImportedMessages, @QueuedMedia, @TotalMessages,
                @TotalMedia, @EstimatedCostUsd, @Error
            )
            """,
            run,
            cancellationToken: ct));

        return run;
    }

    public async Task UpdateProgressAsync(Guid runId, int lastMessageIndex, long importedMessages, long queuedMedia, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE archive_import_runs
            SET last_message_index = @LastMessageIndex,
                imported_messages = @ImportedMessages,
                queued_media = @QueuedMedia,
                updated_at = NOW()
            WHERE id = @RunId
            """,
            new { RunId = runId, LastMessageIndex = lastMessageIndex, ImportedMessages = importedMessages, QueuedMedia = queuedMedia },
            cancellationToken: ct));
    }

    public async Task CompleteRunAsync(Guid runId, ArchiveImportRunStatus status, string? error, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE archive_import_runs
            SET status = @Status,
                error = @Error,
                updated_at = NOW()
            WHERE id = @RunId
            """,
            new { RunId = runId, Status = status, Error = error },
            cancellationToken: ct));
    }
}
