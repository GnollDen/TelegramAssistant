namespace TgAssistant.Core.Models;

public class AnalysisWatermark
{
    public string Key { get; set; } = string.Empty;
    public long Value { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
