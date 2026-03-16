namespace TgAssistant.Intelligence.Stage5;

public class SummaryHistoricalHint
{
    public int SessionIndex { get; set; }
    public float Similarity { get; set; }
    public string Summary { get; set; } = string.Empty;
}
