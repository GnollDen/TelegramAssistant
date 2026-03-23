using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TgAssistant.Core.Configuration;
using TgAssistant.Infrastructure.Database.Ef;
using TgAssistant.Intelligence.Stage6;

namespace TgAssistant.Host.Stage6Ab;

public sealed class Stage6LightAbRunCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly IBotChatService _botChatService;
    private readonly IDbContextFactory<TgAssistantDbContext> _dbFactory;
    private readonly AnalysisSettings _analysisSettings;
    private readonly ILogger<Stage6LightAbRunCommand> _logger;

    public Stage6LightAbRunCommand(
        IBotChatService botChatService,
        IDbContextFactory<TgAssistantDbContext> dbFactory,
        IOptions<AnalysisSettings> analysisSettings,
        ILogger<Stage6LightAbRunCommand> logger)
    {
        _botChatService = botChatService;
        _dbFactory = dbFactory;
        _analysisSettings = analysisSettings.Value;
        _logger = logger;
    }

    public async Task<Stage6LightAbRunResult> RunAsync(
        string casesFile,
        string outputDir,
        string passLabel,
        string? modelOverride,
        CancellationToken ct)
    {
        var normalizedCasesFile = (casesFile ?? string.Empty).Trim();
        var normalizedOutputDir = (outputDir ?? string.Empty).Trim();
        var normalizedPassLabel = string.IsNullOrWhiteSpace(passLabel) ? "pass" : passLabel.Trim();
        if (string.IsNullOrWhiteSpace(normalizedCasesFile))
        {
            throw new InvalidOperationException("--stage6-light-ab-cases-file is required.");
        }

        if (!File.Exists(normalizedCasesFile))
        {
            throw new FileNotFoundException($"Stage6 light A/B cases file was not found: {normalizedCasesFile}");
        }

        if (string.IsNullOrWhiteSpace(normalizedOutputDir))
        {
            normalizedOutputDir = Path.Combine("artifacts", "stage6_light_ab", DateTime.UtcNow.ToString("yyyyMMdd_HHmmss"));
        }

        var payload = await File.ReadAllTextAsync(normalizedCasesFile, ct);
        var cases = JsonSerializer.Deserialize<Stage6LightAbCaseSet>(payload, JsonOptions)
            ?? throw new InvalidOperationException("Stage6 light A/B cases payload could not be deserialized.");
        if (cases.Cases.Count == 0)
        {
            throw new InvalidOperationException("Stage6 light A/B cases payload is empty.");
        }

        var normalizedOverride = string.IsNullOrWhiteSpace(modelOverride) ? string.Empty : modelOverride.Trim();
        if (!string.IsNullOrWhiteSpace(normalizedOverride))
        {
            _analysisSettings.CheapBaselineModel = normalizedOverride;
        }

        var resolvedModel = string.IsNullOrWhiteSpace(_analysisSettings.CheapBaselineModel)
            ? _analysisSettings.CheapModel
            : _analysisSettings.CheapBaselineModel;

        var startedAt = DateTime.UtcNow;
        var caseResults = new List<Stage6LightAbCaseResult>(cases.Cases.Count);

        foreach (var scenario in cases.Cases)
        {
            ct.ThrowIfCancellationRequested();
            var caseId = string.IsNullOrWhiteSpace(scenario.CaseId)
                ? Guid.NewGuid().ToString("N")
                : scenario.CaseId.Trim();
            var caseStartedAt = DateTime.UtcNow;

            _logger.LogInformation(
                "Stage6 light A/B case started: pass={PassLabel}, case_id={CaseId}, chat_id={ChatId}, case_type={CaseType}",
                normalizedPassLabel,
                caseId,
                scenario.ChatId,
                scenario.CaseType);

            BotChatTurnDiagnostics? diagnostics = null;
            string? error = null;
            try
            {
                diagnostics = await _botChatService.GenerateReplyWithDiagnosticsAsync(
                    scenario.UserPrompt,
                    scenario.ChatId,
                    null,
                    null,
                    ct);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                _logger.LogWarning(
                    ex,
                    "Stage6 light A/B case failed: pass={PassLabel}, case_id={CaseId}",
                    normalizedPassLabel,
                    caseId);
            }

            var caseFinishedAt = DateTime.UtcNow;
            var usageSlice = await ReadUsageSliceAsync(caseStartedAt.AddSeconds(-1), caseFinishedAt.AddSeconds(3), ct);

            var result = new Stage6LightAbCaseResult
            {
                CaseId = caseId,
                ChatId = scenario.ChatId,
                CaseType = scenario.CaseType,
                WindowContextBasis = scenario.WindowContextBasis,
                UserPrompt = scenario.UserPrompt,
                StartedAtUtc = caseStartedAt,
                FinishedAtUtc = caseFinishedAt,
                Reply = diagnostics?.Reply ?? string.Empty,
                ResolvedModel = string.IsNullOrWhiteSpace(diagnostics?.ResolvedModel) ? resolvedModel : diagnostics!.ResolvedModel,
                ChatCalls = diagnostics?.ChatCompletionCalls ?? 0,
                EmbeddingCalls = diagnostics?.EmbeddingCalls ?? 0,
                ToolCalls = diagnostics?.ToolCallsExecuted ?? [],
                DroppedToolCalls = diagnostics?.DroppedToolCalls ?? 0,
                Error = error,
                UsageRows = usageSlice.Rows,
                UsageByPhaseModel = usageSlice.ByPhaseModel
            };

            caseResults.Add(result);
            _logger.LogInformation(
                "Stage6 light A/B case finished: pass={PassLabel}, case_id={CaseId}, chat_calls={ChatCalls}, embedding_calls={EmbeddingCalls}, tool_calls={ToolCalls}, failed={Failed}",
                normalizedPassLabel,
                caseId,
                result.ChatCalls,
                result.EmbeddingCalls,
                result.ToolCalls.Count,
                !string.IsNullOrWhiteSpace(result.Error));
        }

        var finishedAt = DateTime.UtcNow;
        Directory.CreateDirectory(normalizedOutputDir);

        var runArtifact = new Stage6LightAbRunArtifact
        {
            RunId = Guid.NewGuid().ToString("D"),
            PassLabel = normalizedPassLabel,
            CasesFile = normalizedCasesFile,
            ModelOverrideRequested = normalizedOverride,
            ModelResolved = resolvedModel,
            StartedAtUtc = startedAt,
            FinishedAtUtc = finishedAt,
            TotalCases = caseResults.Count,
            FailedCases = caseResults.Count(x => !string.IsNullOrWhiteSpace(x.Error)),
            TotalChatCalls = caseResults.Sum(x => x.ChatCalls),
            TotalEmbeddingCalls = caseResults.Sum(x => x.EmbeddingCalls),
            TotalToolCalls = caseResults.Sum(x => x.ToolCalls.Count),
            Cases = caseResults
        };

        var runFilePath = Path.Combine(normalizedOutputDir, $"stage6_light_ab_{normalizedPassLabel}.json");
        await File.WriteAllTextAsync(runFilePath, JsonSerializer.Serialize(runArtifact, JsonOptions), ct);

        var usageFilePath = Path.Combine(normalizedOutputDir, $"stage6_light_ab_{normalizedPassLabel}_usage_rows.csv");
        await File.WriteAllTextAsync(usageFilePath, BuildUsageCsv(caseResults), ct);

        _logger.LogInformation(
            "Stage6 light A/B pass finished: pass={PassLabel}, cases={CaseCount}, failed={FailedCases}, chat_calls={ChatCalls}, embedding_calls={EmbeddingCalls}, tool_calls={ToolCalls}, artifact={ArtifactPath}",
            normalizedPassLabel,
            runArtifact.TotalCases,
            runArtifact.FailedCases,
            runArtifact.TotalChatCalls,
            runArtifact.TotalEmbeddingCalls,
            runArtifact.TotalToolCalls,
            runFilePath);

        return new Stage6LightAbRunResult
        {
            ArtifactPath = runFilePath,
            UsageCsvPath = usageFilePath,
            OutputDirectory = normalizedOutputDir,
            Summary = runArtifact
        };
    }

    private async Task<UsageSlice> ReadUsageSliceAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var rows = await db.AnalysisUsageEvents
            .AsNoTracking()
            .Where(x => x.CreatedAt >= fromUtc && x.CreatedAt <= toUtc)
            .OrderBy(x => x.CreatedAt)
            .Select(x => new Stage6LightAbUsageRow
            {
                Phase = x.Phase,
                Model = x.Model,
                PromptTokens = x.PromptTokens,
                CompletionTokens = x.CompletionTokens,
                TotalTokens = x.TotalTokens,
                CostUsd = x.CostUsd,
                CreatedAtUtc = x.CreatedAt
            })
            .ToListAsync(ct);

        var byPhaseModel = rows
            .GroupBy(x => $"{x.Phase}|{x.Model}", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        return new UsageSlice(rows, byPhaseModel);
    }

    private static string BuildUsageCsv(List<Stage6LightAbCaseResult> cases)
    {
        var lines = new List<string>
        {
            "case_id,chat_id,case_type,phase,model,prompt_tokens,completion_tokens,total_tokens,cost_usd,created_at_utc"
        };

        foreach (var item in cases)
        {
            if (item.UsageRows.Count == 0)
            {
                lines.Add($"{Csv(item.CaseId)},{item.ChatId},{Csv(item.CaseType)},,,,,,,");
                continue;
            }

            foreach (var row in item.UsageRows)
            {
                lines.Add(string.Join(",",
                    Csv(item.CaseId),
                    item.ChatId,
                    Csv(item.CaseType),
                    Csv(row.Phase),
                    Csv(row.Model),
                    row.PromptTokens,
                    row.CompletionTokens,
                    row.TotalTokens,
                    row.CostUsd,
                    Csv(row.CreatedAtUtc.ToString("O"))));
            }
        }

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    private static string Csv(string? value)
    {
        var normalized = value ?? string.Empty;
        if (!normalized.Contains(',') && !normalized.Contains('"') && !normalized.Contains('\n'))
        {
            return normalized;
        }

        return $"\"{normalized.Replace("\"", "\"\"")}\"";
    }

    private sealed record UsageSlice(List<Stage6LightAbUsageRow> Rows, Dictionary<string, int> ByPhaseModel);
}

public sealed class Stage6LightAbCaseSet
{
    public string DatasetId { get; set; } = string.Empty;
    public List<Stage6LightAbCaseInput> Cases { get; set; } = [];
}

public sealed class Stage6LightAbCaseInput
{
    public string CaseId { get; set; } = string.Empty;
    public long ChatId { get; set; }
    public string CaseType { get; set; } = string.Empty;
    public string WindowContextBasis { get; set; } = string.Empty;
    public string UserPrompt { get; set; } = string.Empty;
}

public sealed class Stage6LightAbCaseResult
{
    public string CaseId { get; set; } = string.Empty;
    public long ChatId { get; set; }
    public string CaseType { get; set; } = string.Empty;
    public string WindowContextBasis { get; set; } = string.Empty;
    public string UserPrompt { get; set; } = string.Empty;
    public DateTime StartedAtUtc { get; set; }
    public DateTime FinishedAtUtc { get; set; }
    public string Reply { get; set; } = string.Empty;
    public string ResolvedModel { get; set; } = string.Empty;
    public int ChatCalls { get; set; }
    public int EmbeddingCalls { get; set; }
    public List<string> ToolCalls { get; set; } = [];
    public int DroppedToolCalls { get; set; }
    public string? Error { get; set; }
    public List<Stage6LightAbUsageRow> UsageRows { get; set; } = [];
    public Dictionary<string, int> UsageByPhaseModel { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class Stage6LightAbUsageRow
{
    public string Phase { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public int TotalTokens { get; set; }
    public decimal CostUsd { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class Stage6LightAbRunArtifact
{
    public string RunId { get; set; } = string.Empty;
    public string PassLabel { get; set; } = string.Empty;
    public string CasesFile { get; set; } = string.Empty;
    public string ModelOverrideRequested { get; set; } = string.Empty;
    public string ModelResolved { get; set; } = string.Empty;
    public DateTime StartedAtUtc { get; set; }
    public DateTime FinishedAtUtc { get; set; }
    public int TotalCases { get; set; }
    public int FailedCases { get; set; }
    public int TotalChatCalls { get; set; }
    public int TotalEmbeddingCalls { get; set; }
    public int TotalToolCalls { get; set; }
    public List<Stage6LightAbCaseResult> Cases { get; set; } = [];
}

public sealed class Stage6LightAbRunResult
{
    public string ArtifactPath { get; set; } = string.Empty;
    public string UsageCsvPath { get; set; } = string.Empty;
    public string OutputDirectory { get; set; } = string.Empty;
    public Stage6LightAbRunArtifact Summary { get; set; } = new();
}
