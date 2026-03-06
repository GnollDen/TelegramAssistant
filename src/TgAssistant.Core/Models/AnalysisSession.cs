namespace TgAssistant.Core.Models;

public class AnalysisSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EntityId { get; set; }
    public SessionPhase Phase { get; set; } = SessionPhase.Analysis;
    public SessionStatus Status { get; set; } = SessionStatus.Active;
    public string PromptTemplateId { get; set; } = string.Empty;
    public List<SessionMessage> Messages { get; set; } = new();
    public string? FinalReport { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class SessionMessage
{
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public enum SessionPhase { Analysis, Intro, Retro, Report, Strategy, Completed }
public enum SessionStatus { Active, Paused, Completed, Cancelled }
