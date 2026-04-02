namespace TgAssistant.Core.Models;

public class ModelPassAuditRecord
{
    public Guid ModelPassRunId { get; set; }
    public Guid NormalizationRunId { get; set; }
    public ModelPassEnvelope Envelope { get; set; } = new();
    public ModelNormalizationResult Normalization { get; set; } = new();
}
