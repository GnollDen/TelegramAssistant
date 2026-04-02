using Microsoft.EntityFrameworkCore;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Infrastructure.Database;

public class ModelPassEnvelopeRepository : IModelPassEnvelopeRepository
{
    private const string CompletedLifecycleStatus = "completed";

    private readonly IDbContextFactory<TgAssistantDbContext> _dbFactory;

    public ModelPassEnvelopeRepository(IDbContextFactory<TgAssistantDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<ModelPassEnvelope> UpsertAsync(ModelPassEnvelope envelope, CancellationToken ct = default)
    {
        ModelPassEnvelopeValidator.Validate(envelope);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var row = await db.ModelPassRuns.FirstOrDefaultAsync(x => x.Id == envelope.RunId, ct);
        var now = DateTime.UtcNow;

        if (row == null)
        {
            row = new DbModelPassRun
            {
                Id = envelope.RunId,
                CreatedAt = now
            };
            db.ModelPassRuns.Add(row);
        }

        row.ScopeKey = envelope.ScopeKey.Trim();
        row.Stage = envelope.Stage.Trim();
        row.PassFamily = envelope.PassFamily.Trim();
        row.RunKind = envelope.RunKind.Trim();
        row.Status = CompletedLifecycleStatus;
        row.ResultStatus = envelope.ResultStatus;
        row.TargetType = envelope.Target.TargetType.Trim();
        row.TargetRef = envelope.Target.TargetRef.Trim();
        row.PersonId = envelope.PersonId;
        row.SourceObjectId = envelope.SourceObjectId;
        row.EvidenceItemId = envelope.EvidenceItemId;
        row.TriggerKind = NormalizeNullable(envelope.TriggerKind);
        row.TriggerRef = NormalizeNullable(envelope.TriggerRef);
        row.SchemaVersion = envelope.SchemaVersion;
        row.RequestedModel = NormalizeNullable(envelope.RequestedModel);
        row.ScopeJson = ModelPassEnvelopeStorageCodec.SerializeScope(envelope.Scope);
        row.SourceRefsJson = ModelPassEnvelopeStorageCodec.SerializeSourceRefs(envelope.SourceRefs);
        row.TruthSummaryJson = ModelPassEnvelopeStorageCodec.SerializeTruthSummary(envelope.TruthSummary);
        row.ConflictsJson = ModelPassEnvelopeStorageCodec.SerializeConflicts(envelope.Conflicts);
        row.UnknownsJson = ModelPassEnvelopeStorageCodec.SerializeUnknowns(envelope.Unknowns);
        row.InputSummaryJson = ModelPassEnvelopeStorageCodec.SerializeInputSummary(envelope);
        row.OutputSummaryJson = ModelPassEnvelopeStorageCodec.SerializeOutputSummary(envelope.OutputSummary);
        row.FailureJson = ModelPassEnvelopeStorageCodec.SerializeFailureSummary(envelope);
        row.StartedAt = envelope.StartedAtUtc == default ? now : envelope.StartedAtUtc;
        row.FinishedAt = envelope.FinishedAtUtc ?? now;
        row.MetricsJson = string.IsNullOrWhiteSpace(row.MetricsJson) ? "{}" : row.MetricsJson;

        await db.SaveChangesAsync(ct);
        return Map(row);
    }

    public async Task<ModelPassEnvelope?> GetByIdAsync(Guid runId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var row = await db.ModelPassRuns
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == runId, ct);

        return row == null ? null : Map(row);
    }

    private static ModelPassEnvelope Map(DbModelPassRun row)
    {
        return new ModelPassEnvelope
        {
            RunId = row.Id,
            SchemaVersion = row.SchemaVersion,
            Stage = row.Stage,
            PassFamily = row.PassFamily,
            RunKind = row.RunKind,
            ScopeKey = row.ScopeKey,
            Scope = ModelPassEnvelopeStorageCodec.DeserializeScope(row.ScopeJson),
            Target = new ModelPassTarget
            {
                TargetType = row.TargetType,
                TargetRef = row.TargetRef
            },
            PersonId = row.PersonId,
            SourceObjectId = row.SourceObjectId,
            EvidenceItemId = row.EvidenceItemId,
            RequestedModel = row.RequestedModel,
            TriggerKind = row.TriggerKind,
            TriggerRef = row.TriggerRef,
            SourceRefs = ModelPassEnvelopeStorageCodec.DeserializeSourceRefs(row.SourceRefsJson),
            TruthSummary = ModelPassEnvelopeStorageCodec.DeserializeTruthSummary(row.TruthSummaryJson),
            Conflicts = ModelPassEnvelopeStorageCodec.DeserializeConflicts(row.ConflictsJson),
            Unknowns = ModelPassEnvelopeStorageCodec.DeserializeUnknowns(row.UnknownsJson),
            ResultStatus = row.ResultStatus,
            OutputSummary = ModelPassEnvelopeStorageCodec.DeserializeOutputSummary(row.OutputSummaryJson),
            StartedAtUtc = row.StartedAt,
            FinishedAtUtc = row.FinishedAt
        };
    }

    private static string? NormalizeNullable(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
