using Microsoft.EntityFrameworkCore;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Infrastructure.Database;

public class EvalRepository : IEvalRepository
{
    private readonly IDbContextFactory<TgAssistantDbContext> _dbFactory;

    public EvalRepository(IDbContextFactory<TgAssistantDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<EvalRunResult> CreateRunAsync(EvalRunResult run, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var row = new DbEvalRun
        {
            Id = run.RunId,
            RunName = run.RunName,
            Passed = run.Passed,
            StartedAt = run.StartedAt,
            FinishedAt = run.FinishedAt,
            Summary = run.Summary,
            MetricsJson = string.IsNullOrWhiteSpace(run.MetricsJson) ? "{}" : run.MetricsJson
        };
        db.EvalRuns.Add(row);
        await db.SaveChangesAsync(ct);
        return run;
    }

    public async Task<EvalScenarioResult> AddScenarioResultAsync(EvalScenarioResult result, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var row = new DbEvalScenarioResult
        {
            Id = result.Id,
            RunId = result.RunId,
            ScenarioName = result.ScenarioName,
            Passed = result.Passed,
            Summary = result.Summary,
            MetricsJson = string.IsNullOrWhiteSpace(result.MetricsJson) ? "{}" : result.MetricsJson,
            CreatedAt = result.CreatedAt == default ? DateTime.UtcNow : result.CreatedAt
        };
        db.EvalScenarioResults.Add(row);
        await db.SaveChangesAsync(ct);
        return result;
    }

    public async Task CompleteRunAsync(Guid runId, bool passed, string summary, string metricsJson, DateTime finishedAt, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var row = await db.EvalRuns.FirstOrDefaultAsync(x => x.Id == runId, ct)
            ?? throw new InvalidOperationException($"Eval run not found: {runId}");
        row.Passed = passed;
        row.Summary = summary;
        row.MetricsJson = string.IsNullOrWhiteSpace(metricsJson) ? "{}" : metricsJson;
        row.FinishedAt = finishedAt;
        await db.SaveChangesAsync(ct);
    }

    public async Task<EvalRunResult?> GetRunByIdAsync(Guid runId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var row = await db.EvalRuns
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == runId, ct);
        if (row == null)
        {
            return null;
        }

        return Map(row, new List<EvalScenarioResult>());
    }

    public async Task<List<EvalScenarioResult>> GetScenarioResultsAsync(Guid runId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var rows = await db.EvalScenarioResults
            .AsNoTracking()
            .Where(x => x.RunId == runId)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(ct);

        return rows.Select(MapScenario).ToList();
    }

    public async Task<EvalRunResult?> GetLatestRunByNameAsync(string runName, CancellationToken ct = default)
    {
        var normalized = string.IsNullOrWhiteSpace(runName) ? "default" : runName.Trim();
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var row = await db.EvalRuns
            .AsNoTracking()
            .Where(x => x.RunName == normalized)
            .OrderByDescending(x => x.StartedAt)
            .FirstOrDefaultAsync(ct);

        if (row == null)
        {
            return null;
        }

        var scenarios = await db.EvalScenarioResults
            .AsNoTracking()
            .Where(x => x.RunId == row.Id)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(ct);

        return Map(row, scenarios.Select(MapScenario).ToList());
    }

    private static EvalRunResult Map(DbEvalRun row, List<EvalScenarioResult> scenarios)
    {
        return new EvalRunResult
        {
            RunId = row.Id,
            RunName = row.RunName,
            Passed = row.Passed,
            StartedAt = row.StartedAt,
            FinishedAt = row.FinishedAt,
            Summary = row.Summary,
            MetricsJson = string.IsNullOrWhiteSpace(row.MetricsJson) ? "{}" : row.MetricsJson,
            Scenarios = scenarios
        };
    }

    private static EvalScenarioResult MapScenario(DbEvalScenarioResult row)
    {
        return new EvalScenarioResult
        {
            Id = row.Id,
            RunId = row.RunId,
            ScenarioName = row.ScenarioName,
            Passed = row.Passed,
            Summary = row.Summary,
            MetricsJson = string.IsNullOrWhiteSpace(row.MetricsJson) ? "{}" : row.MetricsJson,
            CreatedAt = row.CreatedAt
        };
    }
}
