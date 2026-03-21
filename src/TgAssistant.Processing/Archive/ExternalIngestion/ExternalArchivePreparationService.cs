using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Processing.Archive.ExternalIngestion;

public class ExternalArchivePreparationService : IExternalArchivePreparationService
{
    private readonly IExternalArchiveImportContractValidator _validator;
    private readonly IExternalArchiveProvenanceWeightingService _provenanceWeighting;
    private readonly IExternalArchiveLinkagePlanner _linkagePlanner;

    public ExternalArchivePreparationService(
        IExternalArchiveImportContractValidator validator,
        IExternalArchiveProvenanceWeightingService provenanceWeighting,
        IExternalArchiveLinkagePlanner linkagePlanner)
    {
        _validator = validator;
        _provenanceWeighting = provenanceWeighting;
        _linkagePlanner = linkagePlanner;
    }

    public async Task<ExternalArchivePreparationResult> PrepareAsync(ExternalArchiveImportRequest request, CancellationToken ct = default)
    {
        var validation = await _validator.ValidateAsync(request, ct);
        var result = new ExternalArchivePreparationResult
        {
            Request = request,
            Validation = validation
        };

        if (!validation.IsValid)
        {
            return result;
        }

        foreach (var record in request.Records)
        {
            ct.ThrowIfCancellationRequested();

            var (provenance, weighting) = await _provenanceWeighting.BuildAsync(request, record, ct);
            var links = await _linkagePlanner.PlanAsync(request, record, provenance, weighting, ct);

            result.PreparedRecords.Add(new ExternalArchivePreparedRecord
            {
                Record = record,
                Provenance = provenance,
                Weighting = weighting,
                Linkages = links
            });
        }

        return result;
    }
}
