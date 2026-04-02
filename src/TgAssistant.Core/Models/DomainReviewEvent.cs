namespace TgAssistant.Core.Legacy.Models;

public class DomainReviewEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ObjectType { get; set; } = string.Empty;
    public string ObjectId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string? OldValueRef { get; set; }
    public string? NewValueRef { get; set; }
    public string? Reason { get; set; }
    public string Actor { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
