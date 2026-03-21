using System.Text.Json;
using Microsoft.Extensions.Logging;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Processing.Archive.ExternalIngestion;

public class ExternalArchiveVerificationService
{
    private readonly IExternalArchiveImportContractValidator _validator;
    private readonly IExternalArchiveIngestionService _ingestionService;
    private readonly IExternalArchiveIngestionRepository _repository;
    private readonly ILogger<ExternalArchiveVerificationService> _logger;

    public ExternalArchiveVerificationService(
        IExternalArchiveImportContractValidator validator,
        IExternalArchiveIngestionService ingestionService,
        IExternalArchiveIngestionRepository repository,
        ILogger<ExternalArchiveVerificationService> logger)
    {
        _validator = validator;
        _ingestionService = ingestionService;
        _repository = repository;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var request = BuildSmokeRequest(now);

        var validation = await _validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException($"External archive smoke failed: contract validation errors: {string.Join(" | ", validation.Errors)}");
        }

        var firstRun = await _ingestionService.IngestAsync(request, ct);
        if (firstRun.IsReplay)
        {
            throw new InvalidOperationException("External archive smoke failed: first ingest unexpectedly became replay.");
        }

        if (firstRun.PersistedRecordCount == 0)
        {
            throw new InvalidOperationException("External archive smoke failed: persisted records are empty.");
        }

        if (firstRun.PersistedLinkageCount == 0)
        {
            throw new InvalidOperationException("External archive smoke failed: linkage artifacts were not produced.");
        }

        var persistedRecords = await _repository.GetRecordsByRunIdAsync(firstRun.Batch.RunId, ct);
        if (!persistedRecords.Any(x => !string.IsNullOrWhiteSpace(x.TruthLayer) && !string.IsNullOrWhiteSpace(x.SourceClass)))
        {
            throw new InvalidOperationException("External archive smoke failed: provenance fields were not stored.");
        }

        if (!persistedRecords.Any(x => x.FinalWeight > 0f))
        {
            throw new InvalidOperationException("External archive smoke failed: weighting fields were not stored.");
        }

        var linkages = await _repository.GetLinkageArtifactsByRunIdAsync(firstRun.Batch.RunId, ct);
        if (!linkages.Any(x => x.LinkType == ExternalArchiveLinkTypes.GraphLink)
            || !linkages.Any(x => x.LinkType == ExternalArchiveLinkTypes.PeriodLink)
            || !linkages.Any(x => x.LinkType == ExternalArchiveLinkTypes.ClarificationLink))
        {
            throw new InvalidOperationException("External archive smoke failed: expected linkage artifacts were not produced.");
        }

        var secondRun = await _ingestionService.IngestAsync(request, ct);
        if (!secondRun.IsReplay)
        {
            throw new InvalidOperationException("External archive smoke failed: idempotent replay protection did not trigger.");
        }

        _logger.LogInformation(
            "External archive smoke passed. run_id={RunId}, case_id={CaseId}, persisted_records={Records}, persisted_linkages={Links}, replay_run={ReplayRun}",
            firstRun.Batch.RunId,
            firstRun.Batch.CaseId,
            firstRun.PersistedRecordCount,
            firstRun.PersistedLinkageCount,
            secondRun.Batch.RunId);
    }

    private static ExternalArchiveImportRequest BuildSmokeRequest(DateTime now)
    {
        var caseScope = CaseScopeFactory.CreateSmokeScope("external_archive");
        var sourceRef = $"smoke_external_source:{caseScope.CaseId}";

        var record1Payload = JsonSerializer.Serialize(new
        {
            source = "smoke",
            note = "supporting context message",
            at = now.AddDays(-3)
        });
        var record2Payload = JsonSerializer.Serialize(new
        {
            source = "smoke",
            note = "relationship signal with ambiguity",
            at = now.AddDays(-2)
        });

        return new ExternalArchiveImportRequest
        {
            CaseId = caseScope.CaseId,
            SourceClass = ExternalArchiveSourceClasses.IndirectMentionArchive,
            SourceRef = sourceRef,
            ImportedAtUtc = now,
            ImportBatchId = $"smoke-{now:yyyyMMddHHmmss}",
            Actor = "external_archive_smoke",
            Records =
            [
                new ExternalArchiveRecord
                {
                    RecordId = "smoke-r1",
                    OccurredAtUtc = now.AddDays(-3),
                    RecordType = ExternalArchiveRecordTypes.Message,
                    Text = "Saw them together at the meetup.",
                    SubjectActorKey = $"{caseScope.ChatId}:1001",
                    TargetActorKey = $"{caseScope.ChatId}:2002",
                    ChatId = caseScope.ChatId,
                    Confidence = 0.68f,
                    EvidenceRefs = ["chat:meetup", "msg:external:1"],
                    RawPayloadJson = record1Payload
                },
                new ExternalArchiveRecord
                {
                    RecordId = "smoke-r2",
                    OccurredAtUtc = now.AddDays(-2),
                    RecordType = ExternalArchiveRecordTypes.RelationshipSignal,
                    Text = "A third-party mention indicates possible pressure.",
                    SubjectActorKey = $"{caseScope.ChatId}:2002",
                    TargetActorKey = $"{caseScope.ChatId}:3003",
                    SourceSessionId = Guid.NewGuid(),
                    Confidence = 0.54f,
                    EvidenceRefs = ["note:operator:1"],
                    RawPayloadJson = record2Payload
                }
            ]
        };
    }
}
