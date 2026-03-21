using System.Globalization;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Processing.Archive.ExternalIngestion;

public class ExternalArchiveLinkagePlanner : IExternalArchiveLinkagePlanner
{
    public Task<List<ExternalArchiveLinkage>> PlanAsync(
        ExternalArchiveImportRequest request,
        ExternalArchiveRecord record,
        ExternalArchiveProvenance provenance,
        ExternalArchiveWeighting weighting,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var links = new List<ExternalArchiveLinkage>();

        AddGraphLinks(links, record, weighting.FinalWeight);
        AddPeriodLink(links, request, record, weighting.FinalWeight);
        AddEventLink(links, record, weighting.FinalWeight);
        AddClarificationLink(links, request, record, weighting);

        return Task.FromResult(links);
    }

    private static void AddGraphLinks(List<ExternalArchiveLinkage> links, ExternalArchiveRecord record, float confidence)
    {
        if (!string.IsNullOrWhiteSpace(record.SubjectActorKey))
        {
            links.Add(new ExternalArchiveLinkage
            {
                LinkType = ExternalArchiveLinkTypes.GraphLink,
                TargetType = "graph_node",
                TargetId = $"actor:{record.SubjectActorKey}",
                LinkConfidence = confidence,
                Reason = "subject_actor_key anchor"
            });
        }

        if (!string.IsNullOrWhiteSpace(record.TargetActorKey))
        {
            links.Add(new ExternalArchiveLinkage
            {
                LinkType = ExternalArchiveLinkTypes.GraphLink,
                TargetType = "graph_node",
                TargetId = $"actor:{record.TargetActorKey}",
                LinkConfidence = confidence,
                Reason = "target_actor_key anchor"
            });
        }

        if (!string.IsNullOrWhiteSpace(record.SubjectActorKey) && !string.IsNullOrWhiteSpace(record.TargetActorKey))
        {
            links.Add(new ExternalArchiveLinkage
            {
                LinkType = ExternalArchiveLinkTypes.GraphLink,
                TargetType = "graph_edge",
                TargetId = $"edge:{record.SubjectActorKey}:{record.TargetActorKey}:{record.RecordId}",
                LinkConfidence = confidence,
                Reason = "subject-target pair anchor"
            });
        }
    }

    private static void AddPeriodLink(
        List<ExternalArchiveLinkage> links,
        ExternalArchiveImportRequest request,
        ExternalArchiveRecord record,
        float confidence)
    {
        var weekBucket = ISOWeek.GetWeekOfYear(record.OccurredAtUtc);
        links.Add(new ExternalArchiveLinkage
        {
            LinkType = ExternalArchiveLinkTypes.PeriodLink,
            TargetType = "period_time_bucket",
            TargetId = $"case:{request.CaseId}:y{record.OccurredAtUtc:yyyy}:w{weekBucket:00}",
            LinkConfidence = confidence,
            Reason = "temporal pre-link for future period resolution"
        });
    }

    private static void AddEventLink(List<ExternalArchiveLinkage> links, ExternalArchiveRecord record, float confidence)
    {
        if (record.SourceMessageId.HasValue)
        {
            links.Add(new ExternalArchiveLinkage
            {
                LinkType = ExternalArchiveLinkTypes.EventLink,
                TargetType = "communication_event",
                TargetId = $"message:{record.SourceMessageId.Value}",
                LinkConfidence = confidence,
                Reason = "source_message_id anchor"
            });
            return;
        }

        if (record.RecordType == ExternalArchiveRecordTypes.Event)
        {
            links.Add(new ExternalArchiveLinkage
            {
                LinkType = ExternalArchiveLinkTypes.EventLink,
                TargetType = "offline_event_candidate",
                TargetId = $"external_event:{record.RecordId}",
                LinkConfidence = confidence,
                Reason = "record_type=event"
            });
        }
    }

    private static void AddClarificationLink(
        List<ExternalArchiveLinkage> links,
        ExternalArchiveImportRequest request,
        ExternalArchiveRecord record,
        ExternalArchiveWeighting weighting)
    {
        if (!weighting.NeedsClarification)
        {
            return;
        }

        links.Add(new ExternalArchiveLinkage
        {
            LinkType = ExternalArchiveLinkTypes.ClarificationLink,
            TargetType = "clarification_candidate",
            TargetId = $"clarify:{request.CaseId}:{record.RecordId}",
            LinkConfidence = Math.Min(0.8f, weighting.FinalWeight + 0.15f),
            Reason = "weighting policy requires clarification"
        });
    }
}
