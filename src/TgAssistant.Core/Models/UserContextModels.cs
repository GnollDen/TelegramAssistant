namespace TgAssistant.Core.Models;

public static class UserContextSourceKinds
{
    public const string ClarificationAnswer = "clarification_answer";
    public const string LongFormContext = "long_form_context";
    public const string OfflineContextNote = "offline_context_note";
    public const string OperatorAnnotation = "operator_annotation";
    public const string UserContextCorrection = "user_context_correction";
}

public class Stage6UserContextEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? Stage6CaseId { get; set; }
    public long ScopeCaseId { get; set; }
    public long ChatId { get; set; }
    public string SourceKind { get; set; } = UserContextSourceKinds.ClarificationAnswer;
    public Guid? ClarificationQuestionId { get; set; }
    public string ContentText { get; set; } = string.Empty;
    public string? StructuredPayloadJson { get; set; }
    public string AppliesToRefsJson { get; set; } = "[]";
    public string EnteredVia { get; set; } = "bot";
    public float UserReportedCertainty { get; set; }
    public string SourceType { get; set; } = "user";
    public string SourceId { get; set; } = string.Empty;
    public long? SourceMessageId { get; set; }
    public Guid? SourceSessionId { get; set; }
    public Guid? SupersedesContextEntryId { get; set; }
    public string ConflictsWithRefsJson { get; set; } = "[]";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
