namespace TgAssistant.Core.Models;

public class TextEmbedding
{
    public long Id { get; set; }
    public string OwnerType { get; set; } = string.Empty;
    public string OwnerId { get; set; } = string.Empty;
    public string SourceText { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public float[] Vector { get; set; } = Array.Empty<float>();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
