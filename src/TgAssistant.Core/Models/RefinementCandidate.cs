namespace TgAssistant.Core.Models;

public class RefinementCandidate
{
    public long ExtractionId { get; set; }
    public long MessageId { get; set; }
    public string CheapJson { get; set; } = "{}";
    public DateTime ExtractionUpdatedAt { get; set; }
    public int ExistingClaimsCount { get; set; }
    public Message Message { get; set; } = new();
}
