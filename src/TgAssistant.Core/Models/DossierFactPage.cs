namespace TgAssistant.Core.Models;

public class DossierFactPage
{
    public List<Fact> Facts { get; set; } = new();
    public int TotalCount { get; set; }
}
