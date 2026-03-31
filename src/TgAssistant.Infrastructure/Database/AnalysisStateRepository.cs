using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using TgAssistant.Core.Interfaces;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Infrastructure.Database;

internal static class AnalysisStateSignalKeys
{
    internal const string WatermarkMonotonicRegressionCountKey = "observability:watermark_monotonic_regression_count";
    internal const string WatermarkMonotonicRegressionMinuteKeyPrefix = "observability:watermark_monotonic_regression_minute:";

    internal static string BuildWatermarkMonotonicRegressionMinuteKey(DateTime utcTimestamp)
    {
        return $"{WatermarkMonotonicRegressionMinuteKeyPrefix}{utcTimestamp:yyyyMMddHHmm}";
    }
}

public class AnalysisStateRepository : IAnalysisStateRepository
{
    private readonly IDbContextFactory<TgAssistantDbContext> _dbFactory;
    private readonly IExtractionErrorRepository _extractionErrorRepository;
    private readonly ILogger<AnalysisStateRepository> _logger;

    public AnalysisStateRepository(
        IDbContextFactory<TgAssistantDbContext> dbFactory,
        IExtractionErrorRepository extractionErrorRepository,
        ILogger<AnalysisStateRepository> logger)
    {
        _dbFactory = dbFactory;
        _extractionErrorRepository = extractionErrorRepository;
        _logger = logger;
    }

    public async Task<long> GetWatermarkAsync(string key, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var row = await db.AnalysisStates.AsNoTracking().FirstOrDefaultAsync(x => x.Key == key, ct);
        return row?.Value ?? 0;
    }

    public async Task SetWatermarkAsync(string key, long value, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("analysis_state_key_required", nameof(key));
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var normalizedKey = key.Trim();
        const string upsertWatermarkSql =
            """
            INSERT INTO analysis_state (key, value, updated_at)
            VALUES (@key, @value, NOW())
            ON CONFLICT (key) DO UPDATE
            SET value = GREATEST(analysis_state.value, EXCLUDED.value),
                updated_at = CASE
                    WHEN EXCLUDED.value > analysis_state.value THEN NOW()
                    ELSE analysis_state.updated_at
                END
            RETURNING value;
            """;
        var persistedValue = await ExecuteScalarLongAsync(
            db,
            upsertWatermarkSql,
            new NpgsqlParameter("key", normalizedKey),
            new NpgsqlParameter("value", value),
            ct);

        if (persistedValue > value)
        {
            long? regressionTotal = null;
            try
            {
                regressionTotal = await IncrementWatermarkRegressionSignalsAsync(db, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to persist watermark monotonic regression counter signal: key={Key}, requested={Requested}, persisted={Persisted}",
                    normalizedKey,
                    value,
                    persistedValue);
            }

            _logger.LogWarning(
                "Blocked non-monotonic watermark update: key={Key}, requested={Requested}, persisted={Persisted}, regression_total={RegressionTotal}",
                normalizedKey,
                value,
                persistedValue,
                regressionTotal);
            await _extractionErrorRepository.LogAsync(
                "analysis_state",
                "watermark_regression_blocked",
                payload: $"key={normalizedKey};requested={value};persisted={persistedValue}",
                ct: ct);
        }
    }

    public async Task ResetWatermarksIfExistAsync(IReadOnlyCollection<string> keys, CancellationToken ct = default)
    {
        if (keys.Count == 0)
        {
            return;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var safeKeys = keys
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (safeKeys.Length == 0)
        {
            return;
        }

        var rows = await db.AnalysisStates
            .Where(x => safeKeys.Contains(x.Key) && x.Value != 0)
            .ToListAsync(ct);
        if (rows.Count == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        foreach (var row in rows)
        {
            row.Value = 0;
            row.UpdatedAt = now;
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task<long> IncrementWatermarkRegressionSignalsAsync(TgAssistantDbContext db, CancellationToken ct)
    {
        var total = await IncrementSignalAsync(
            db,
            AnalysisStateSignalKeys.WatermarkMonotonicRegressionCountKey,
            ct);
        await IncrementSignalAsync(
            db,
            AnalysisStateSignalKeys.BuildWatermarkMonotonicRegressionMinuteKey(DateTime.UtcNow),
            ct);
        return total;
    }

    private static Task<long> IncrementSignalAsync(TgAssistantDbContext db, string key, CancellationToken ct)
    {
        const string incrementSignalSql =
            """
            INSERT INTO analysis_state (key, value, updated_at)
            VALUES (@key, 1, NOW())
            ON CONFLICT (key) DO UPDATE
            SET value = analysis_state.value + 1,
                updated_at = NOW()
            RETURNING value;
            """;
        return ExecuteScalarLongAsync(
            db,
            incrementSignalSql,
            new NpgsqlParameter("key", key),
            ct);
    }

    private static async Task<long> ExecuteScalarLongAsync(
        TgAssistantDbContext db,
        string sql,
        NpgsqlParameter parameter,
        CancellationToken ct)
    {
        return await ExecuteScalarLongAsync(db, sql, [parameter], ct);
    }

    private static async Task<long> ExecuteScalarLongAsync(
        TgAssistantDbContext db,
        string sql,
        NpgsqlParameter parameter1,
        NpgsqlParameter parameter2,
        CancellationToken ct)
    {
        return await ExecuteScalarLongAsync(db, sql, [parameter1, parameter2], ct);
    }

    private static async Task<long> ExecuteScalarLongAsync(
        TgAssistantDbContext db,
        string sql,
        IEnumerable<NpgsqlParameter> parameters,
        CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var parameter in parameters)
        {
            command.Parameters.Add(parameter);
        }

        var scalar = await command.ExecuteScalarAsync(ct);
        return scalar switch
        {
            long l => l,
            int i => i,
            decimal d => (long)d,
            _ => Convert.ToInt64(scalar ?? 0L)
        };
    }
}
