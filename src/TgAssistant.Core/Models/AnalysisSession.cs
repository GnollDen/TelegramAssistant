namespace TgAssistant.Core.Models;

/// <summary>
/// Stateful session for multi-step analysis workflows (intro, retro, strategy).
/// Tracks which phase we're in and conversation history within the session.
/// </summary>
public class AnalysisSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EntityId { get; set; }
    
    public SessionPhase Phase { get; set; } = SessionPhase.Analysis;
    public SessionStatus Status { get; set; } = SessionStatus.Active;
    
    /// <summary>
    /// The prompt template used for this session.
    /// </summary>
    public string PromptTemplateId { get; set; } = string.Empty;
    
    /// <summary>
    /// Conversation history within this session (Q&A pairs for intro/retro).
    /// </summary>
    public List<SessionMessage> Messages { get; set; } = new();
    
    /// <summary>
    /// Final report generated after all phases complete.
    /// </summary>
    public string? FinalReport { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class SessionMessage
{
    public string Role { get; set; } = string.Empty; // "assistant" or "user"
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public enum SessionPhase
{
    Analysis,   // initial archive analysis
    Intro,      // Q&A to gather context not in messages
    Retro,      // retrospective analysis Q&A
    Report,     // generating final report
    Strategy,   // ongoing strategy mode
    Completed
}

public enum SessionStatus
{
    Active,
    Paused,
    Completed,
    Cancelled
}
