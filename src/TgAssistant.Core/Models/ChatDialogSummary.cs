namespace TgAssistant.Core.Models;

public enum ChatDialogSummaryType : short
{
    Day = 1,
    Session = 2
}

public class ChatDialogSummary
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public long ChatId { get; set; }
    public ChatDialogSummaryType SummaryType { get; set; } = ChatDialogSummaryType.Day;
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public long StartMessageId { get; set; }
    public long EndMessageId { get; set; }
    public int MessageCount { get; set; }
    public string Summary { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
