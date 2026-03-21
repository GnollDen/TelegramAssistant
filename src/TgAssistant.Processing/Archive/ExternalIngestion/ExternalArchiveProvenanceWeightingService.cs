using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Processing.Archive.ExternalIngestion;

public class ExternalArchiveProvenanceWeightingService : IExternalArchiveProvenanceWeightingService
{
    public Task<(ExternalArchiveProvenance Provenance, ExternalArchiveWeighting Weighting)> BuildAsync(
        ExternalArchiveImportRequest request,
        ExternalArchiveRecord record,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var baseWeight = ResolveBaseWeight(request.SourceClass);
        var confidenceMultiplier = Clamp01(record.Confidence);
        var corroborationMultiplier = 1f + Math.Min(0.2f, record.EvidenceRefs.Count * 0.05f);
        var finalWeight = Clamp01(baseWeight * confidenceMultiplier * corroborationMultiplier);
        var competingCapApplied = false;
        if (request.SourceClass == ExternalArchiveSourceClasses.CompetingRelationshipArchive)
        {
            var capped = Math.Min(0.49f, finalWeight);
            competingCapApplied = capped < finalWeight;
            finalWeight = capped;
        }

        var provenance = new ExternalArchiveProvenance
        {
            TruthLayer = ResolveTruthLayer(request.SourceClass),
            SourceClass = request.SourceClass,
            SourceRef = request.SourceRef,
            ImportBatchId = request.ImportBatchId ?? BuildImportBatchId(request),
            PayloadHash = ExternalArchiveHashing.ComputeSha256(record.RawPayloadJson)
        };

        var needsClarification = request.SourceClass is ExternalArchiveSourceClasses.CompetingRelationshipArchive
            or ExternalArchiveSourceClasses.IndirectMentionArchive
            || finalWeight < 0.55f;

        var weighting = new ExternalArchiveWeighting
        {
            BaseWeight = baseWeight,
            ConfidenceMultiplier = confidenceMultiplier,
            CorroborationMultiplier = corroborationMultiplier,
            FinalWeight = finalWeight,
            NeedsClarification = needsClarification,
            WeightingReason = BuildReason(
                request.SourceClass,
                confidenceMultiplier,
                corroborationMultiplier,
                needsClarification,
                competingCapApplied)
        };

        return Task.FromResult((provenance, weighting));
    }

    private static float ResolveBaseWeight(string sourceClass) => sourceClass switch
    {
        ExternalArchiveSourceClasses.SupportingContextArchive => 0.9f,
        ExternalArchiveSourceClasses.MutualGroupArchive => 0.8f,
        ExternalArchiveSourceClasses.IndirectMentionArchive => 0.6f,
        ExternalArchiveSourceClasses.CompetingRelationshipArchive => 0.45f,
        _ => 0.4f
    };

    private static string ResolveTruthLayer(string sourceClass) => sourceClass switch
    {
        ExternalArchiveSourceClasses.SupportingContextArchive => ExternalArchiveTruthLayers.ObservedFromChat,
        ExternalArchiveSourceClasses.MutualGroupArchive => ExternalArchiveTruthLayers.ObservedFromChat,
        ExternalArchiveSourceClasses.IndirectMentionArchive => ExternalArchiveTruthLayers.ModelInferred,
        ExternalArchiveSourceClasses.CompetingRelationshipArchive => ExternalArchiveTruthLayers.ModelHypothesis,
        _ => ExternalArchiveTruthLayers.ModelHypothesis
    };

    private static string BuildImportBatchId(ExternalArchiveImportRequest request)
    {
        var stable = $"{request.CaseId}:{request.SourceClass}:{request.SourceRef}:{request.ImportedAtUtc:O}";
        return ExternalArchiveHashing.ComputeSha256(stable)[..16];
    }

    private static string BuildReason(
        string sourceClass,
        float confidenceMultiplier,
        float corroborationMultiplier,
        bool needsClarification,
        bool competingCapApplied)
    {
        var reason = $"source_class={sourceClass}; confidence={confidenceMultiplier:0.00}; corroboration={corroborationMultiplier:0.00}";
        if (needsClarification)
        {
            reason += "; clarification_required=true";
        }
        if (competingCapApplied)
        {
            reason += "; competing_confidence_cap=0.49";
        }

        return reason;
    }

    private static float Clamp01(float value) => Math.Clamp(value, 0f, 1f);
}
