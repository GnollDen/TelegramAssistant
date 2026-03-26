using TgAssistant.Core.Models;
using TgAssistant.Core.Prompts;

namespace TgAssistant.Intelligence.Stage5;

public static class Stage5PromptCatalog
{
    public const string CheapExtractionV10Id = "stage5_cheap_extract_v10";
    public const string CheapExtractionV11Id = "stage5_cheap_extract_v11";

    public static readonly ManagedPromptTemplate CheapExtractionV10 = new(
        Id: CheapExtractionV10Id,
        Name: "Stage5 Cheap Extraction v10",
        Description: "Primary cheap extraction prompt for Stage 5.",
        Version: "v10",
        SystemPrompt: AnalysisWorkerService.DefaultCheapPromptV10);

    public static readonly ManagedPromptTemplate CheapExtractionV11 = new(
        Id: CheapExtractionV11Id,
        Name: "Stage5 Cheap Extraction v11",
        Description: "Compact cheap extraction prompt for Stage 5 with preserved semantic contract.",
        Version: "v11",
        SystemPrompt: AnalysisWorkerService.DefaultCheapPromptV11);

    // Backward-compatible alias for legacy callsites.
    public static ManagedPromptTemplate CheapExtraction => CheapExtractionV10;

    public static readonly ManagedPromptTemplate ExpensiveReasoning = new(
        Id: "stage5_expensive_reason_v5",
        Name: "Stage5 Expensive Reasoning v5",
        Description: "High-accuracy resolver prompt for expensive extraction.",
        Version: "v5",
        SystemPrompt: AnalysisWorkerService.DefaultExpensivePrompt);

    public static readonly ManagedPromptTemplate SessionSummary = new(
        Id: "stage5_session_summary_v1",
        Name: "Stage5 Session Summary v1",
        Description: "Session/dialog summary contract used by inline and worker paths.",
        Version: "v1",
        SystemPrompt: AnalysisWorkerService.SummaryPrompt);

    public static readonly ManagedPromptTemplate DailyAggregate = new(
        Id: "stage5_daily_aggregate_v1",
        Name: "Stage5 Daily Aggregate v1",
        Description: "Cold path final daily crystallization prompt.",
        Version: "v1",
        SystemPrompt: DailyAggregateSystemPrompt);

    public static IReadOnlyList<ManagedPromptTemplate> ManagedPrompts { get; } =
    [
        CheapExtractionV10,
        CheapExtractionV11,
        ExpensiveReasoning,
        SessionSummary,
        DailyAggregate
    ];

    public static ManagedPromptTemplate ResolveCheapExtraction(string? id)
    {
        var normalizedId = string.IsNullOrWhiteSpace(id) ? string.Empty : id.Trim();
        if (string.Equals(normalizedId, CheapExtractionV11Id, StringComparison.OrdinalIgnoreCase))
        {
            return CheapExtractionV11;
        }

        return CheapExtractionV10;
    }

    private const string DailyAggregateSystemPrompt = """
You are a nightly memory crystallization module.
Return ONLY JSON object: {"summary":"..."}.

Task:
- merge episodic summaries and key claims into one final daily dossier summary
- keep durable facts, commitments, plan/status changes, conflicts, contacts, finance/work/health/location updates
- remove noise and duplicates
- preserve names as in source
- structure output as coherent day narrative with key outcomes and unresolved items
- write in Russian only (Cyrillic)
- no markdown, no extra fields
""";
}

public sealed record ManagedPromptTemplate(
    string Id,
    string Name,
    string? Description,
    string Version,
    string SystemPrompt)
{
    public string Checksum => PromptTemplateChecksum.Compute(SystemPrompt);

    public PromptTemplate ToTemplate()
    {
        return new PromptTemplate
        {
            Id = Id,
            Name = Name,
            Description = Description,
            Version = Version,
            Checksum = Checksum,
            SystemPrompt = SystemPrompt
        };
    }
}
