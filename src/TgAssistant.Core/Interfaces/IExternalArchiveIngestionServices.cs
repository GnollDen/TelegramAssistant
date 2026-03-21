using TgAssistant.Core.Models;

namespace TgAssistant.Core.Interfaces;

public interface IExternalArchiveImportContractValidator
{
    Task<ExternalArchiveContractValidationResult> ValidateAsync(ExternalArchiveImportRequest request, CancellationToken ct = default);
}

public interface IExternalArchiveProvenanceWeightingService
{
    Task<(ExternalArchiveProvenance Provenance, ExternalArchiveWeighting Weighting)> BuildAsync(
        ExternalArchiveImportRequest request,
        ExternalArchiveRecord record,
        CancellationToken ct = default);
}

public interface IExternalArchiveLinkagePlanner
{
    Task<List<ExternalArchiveLinkage>> PlanAsync(
        ExternalArchiveImportRequest request,
        ExternalArchiveRecord record,
        ExternalArchiveProvenance provenance,
        ExternalArchiveWeighting weighting,
        CancellationToken ct = default);
}

public interface IExternalArchivePreparationService
{
    Task<ExternalArchivePreparationResult> PrepareAsync(ExternalArchiveImportRequest request, CancellationToken ct = default);
}

public interface IExternalArchiveIngestionService
{
    Task<ExternalArchiveIngestionResult> IngestAsync(ExternalArchiveImportRequest request, CancellationToken ct = default);
}
