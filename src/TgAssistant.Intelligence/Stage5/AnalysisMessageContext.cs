namespace TgAssistant.Intelligence.Stage5;

public class AnalysisMessageContext
{
    public List<string> LocalBurst { get; set; } = new();
    public List<string> SessionStart { get; set; } = new();
    public List<string> ExternalReplyContext { get; set; } = new();
    public List<string> HistoricalSummaries { get; set; } = new();
    public string? PreviousSessionSummary { get; set; }
}
