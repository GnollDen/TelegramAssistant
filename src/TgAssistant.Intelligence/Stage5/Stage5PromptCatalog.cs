using TgAssistant.Core.Models;
using TgAssistant.Core.Prompts;

namespace TgAssistant.Intelligence.Stage5;

public static class Stage5PromptCatalog
{
    public static readonly ManagedPromptTemplate CheapExtraction = new(
        Id: "stage5_cheap_extract_v10",
        Name: "Stage5 Cheap Extraction v10",
        Description: "Primary cheap extraction prompt for Stage 5.",
        Version: "v10",
        SystemPrompt: AnalysisWorkerService.DefaultCheapPrompt);

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
