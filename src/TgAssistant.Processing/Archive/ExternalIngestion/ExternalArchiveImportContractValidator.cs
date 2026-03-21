using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Processing.Archive.ExternalIngestion;

public class ExternalArchiveImportContractValidator : IExternalArchiveImportContractValidator
{
    public Task<ExternalArchiveContractValidationResult> ValidateAsync(ExternalArchiveImportRequest request, CancellationToken ct = default)
    {
        var result = new ExternalArchiveContractValidationResult();
        var recordIds = new HashSet<string>(StringComparer.Ordinal);

        if (request.CaseId <= 0)
        {
            result.Errors.Add("case_id must be greater than zero.");
        }

        if (!ExternalArchiveSourceClasses.Allowed.Contains(request.SourceClass))
        {
            result.Errors.Add($"unsupported source_class '{request.SourceClass}'.");
        }

        if (string.IsNullOrWhiteSpace(request.SourceRef))
        {
            result.Errors.Add("source_ref is required.");
        }

        if (request.Records.Count == 0)
        {
            result.Errors.Add("records must not be empty.");
            return Task.FromResult(result);
        }

        for (var i = 0; i < request.Records.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var record = request.Records[i];
            var prefix = $"records[{i}]";

            if (string.IsNullOrWhiteSpace(record.RecordId))
            {
                result.Errors.Add($"{prefix}.record_id is required.");
            }
            else if (!recordIds.Add(record.RecordId))
            {
                result.Errors.Add($"{prefix}.record_id must be unique within one batch.");
            }

            if (record.OccurredAtUtc == default)
            {
                result.Errors.Add($"{prefix}.occurred_at_utc is required.");
            }

            if (!ExternalArchiveRecordTypes.Allowed.Contains(record.RecordType))
            {
                result.Errors.Add($"{prefix}.record_type '{record.RecordType}' is not supported.");
            }

            if (record.Confidence < 0f || record.Confidence > 1f)
            {
                result.Errors.Add($"{prefix}.confidence must be in range 0..1.");
            }

            if (string.IsNullOrWhiteSpace(record.RawPayloadJson))
            {
                result.Errors.Add($"{prefix}.raw_payload_json is required.");
            }

            var hasProvenanceAnchor =
                record.SourceMessageId.HasValue
                || record.SourceSessionId.HasValue
                || record.ChatId.HasValue
                || record.EvidenceRefs.Count > 0;
            if (!hasProvenanceAnchor)
            {
                result.Errors.Add($"{prefix} must include at least one provenance anchor (chat/message/session/evidence_refs).");
            }
        }

        return Task.FromResult(result);
    }
}
