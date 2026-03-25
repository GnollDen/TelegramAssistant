using TgAssistant.Core.Models;

namespace TgAssistant.Core.Interfaces;

public interface IStage6ArtifactFreshnessService
{
    Task<Stage6ArtifactEvidenceStamp> BuildEvidenceStampAsync(
        long caseId,
        long chatId,
        string artifactType,
        CancellationToken ct = default);

    TimeSpan ResolveTtl(string artifactType);
}
