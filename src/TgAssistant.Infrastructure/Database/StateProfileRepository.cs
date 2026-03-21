using Microsoft.EntityFrameworkCore;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Infrastructure.Database;

public class StateProfileRepository : IStateProfileRepository
{
    private readonly IDbContextFactory<TgAssistantDbContext> _dbFactory;

    public StateProfileRepository(IDbContextFactory<TgAssistantDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<StateSnapshot> CreateStateSnapshotAsync(StateSnapshot snapshot, CancellationToken ct = default)
    {
        var row = new DbStateSnapshot
        {
            Id = snapshot.Id == Guid.Empty ? Guid.NewGuid() : snapshot.Id,
            CaseId = snapshot.CaseId,
            ChatId = snapshot.ChatId,
            AsOf = snapshot.AsOf,
            DynamicLabel = snapshot.DynamicLabel,
            RelationshipStatus = snapshot.RelationshipStatus,
            AlternativeStatus = snapshot.AlternativeStatus,
            InitiativeScore = snapshot.InitiativeScore,
            ResponsivenessScore = snapshot.ResponsivenessScore,
            OpennessScore = snapshot.OpennessScore,
            WarmthScore = snapshot.WarmthScore,
            ReciprocityScore = snapshot.ReciprocityScore,
            AmbiguityScore = snapshot.AmbiguityScore,
            AvoidanceRiskScore = snapshot.AvoidanceRiskScore,
            EscalationReadinessScore = snapshot.EscalationReadinessScore,
            ExternalPressureScore = snapshot.ExternalPressureScore,
            Confidence = snapshot.Confidence,
            PeriodId = snapshot.PeriodId,
            KeySignalRefsJson = snapshot.KeySignalRefsJson,
            RiskRefsJson = snapshot.RiskRefsJson,
            SourceSessionId = snapshot.SourceSessionId,
            SourceMessageId = snapshot.SourceMessageId,
            CreatedAt = snapshot.CreatedAt == default ? DateTime.UtcNow : snapshot.CreatedAt
        };

        await WithDbContextAsync(async db =>
        {
            db.StateSnapshots.Add(row);
            await db.SaveChangesAsync(ct);
        }, ct);

        return ToDomain(row);
    }

    public async Task<StateSnapshot?> GetStateSnapshotByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await WithDbContextAsync(async db =>
        {
            var row = await db.StateSnapshots.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            return row == null ? null : ToDomain(row);
        }, ct);
    }

    public async Task<List<StateSnapshot>> GetStateSnapshotsByCaseAsync(long caseId, int limit = 20, CancellationToken ct = default)
    {
        return await WithDbContextAsync(async db =>
        {
            var rows = await db.StateSnapshots
                .AsNoTracking()
                .Where(x => x.CaseId == caseId)
                .OrderByDescending(x => x.AsOf)
                .Take(Math.Max(1, limit))
                .ToListAsync(ct);
            return rows.Select(ToDomain).ToList();
        }, ct);
    }

    public async Task<ProfileSnapshot> CreateProfileSnapshotAsync(ProfileSnapshot snapshot, CancellationToken ct = default)
    {
        var row = new DbProfileSnapshot
        {
            Id = snapshot.Id == Guid.Empty ? Guid.NewGuid() : snapshot.Id,
            SubjectType = snapshot.SubjectType,
            SubjectId = snapshot.SubjectId,
            CaseId = snapshot.CaseId,
            ChatId = snapshot.ChatId,
            PeriodId = snapshot.PeriodId,
            Summary = snapshot.Summary,
            Confidence = snapshot.Confidence,
            Stability = snapshot.Stability,
            SourceSessionId = snapshot.SourceSessionId,
            SourceMessageId = snapshot.SourceMessageId,
            CreatedAt = snapshot.CreatedAt == default ? DateTime.UtcNow : snapshot.CreatedAt
        };

        await WithDbContextAsync(async db =>
        {
            db.ProfileSnapshots.Add(row);
            await db.SaveChangesAsync(ct);
        }, ct);

        return ToDomain(row);
    }

    public async Task<ProfileSnapshot?> GetProfileSnapshotByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await WithDbContextAsync(async db =>
        {
            var row = await db.ProfileSnapshots.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            return row == null ? null : ToDomain(row);
        }, ct);
    }

    public async Task<List<ProfileSnapshot>> GetProfileSnapshotsByCaseAsync(long caseId, string subjectType, string subjectId, CancellationToken ct = default)
    {
        return await WithDbContextAsync(async db =>
        {
            var rows = await db.ProfileSnapshots
                .AsNoTracking()
                .Where(x => x.CaseId == caseId && x.SubjectType == subjectType && x.SubjectId == subjectId)
                .OrderByDescending(x => x.CreatedAt)
                .ToListAsync(ct);
            return rows.Select(ToDomain).ToList();
        }, ct);
    }

    public async Task<ProfileTrait> CreateProfileTraitAsync(ProfileTrait trait, CancellationToken ct = default)
    {
        var row = new DbProfileTrait
        {
            Id = trait.Id == Guid.Empty ? Guid.NewGuid() : trait.Id,
            ProfileSnapshotId = trait.ProfileSnapshotId,
            TraitKey = trait.TraitKey,
            ValueLabel = trait.ValueLabel,
            Confidence = trait.Confidence,
            Stability = trait.Stability,
            IsSensitive = trait.IsSensitive,
            EvidenceRefsJson = trait.EvidenceRefsJson,
            SourceSessionId = trait.SourceSessionId,
            SourceMessageId = trait.SourceMessageId,
            CreatedAt = trait.CreatedAt == default ? DateTime.UtcNow : trait.CreatedAt
        };

        await WithDbContextAsync(async db =>
        {
            db.ProfileTraits.Add(row);
            await db.SaveChangesAsync(ct);
        }, ct);

        return ToDomain(row);
    }

    public async Task<List<ProfileTrait>> GetProfileTraitsBySnapshotIdAsync(Guid profileSnapshotId, CancellationToken ct = default)
    {
        return await WithDbContextAsync(async db =>
        {
            var rows = await db.ProfileTraits
                .AsNoTracking()
                .Where(x => x.ProfileSnapshotId == profileSnapshotId)
                .OrderBy(x => x.TraitKey)
                .ToListAsync(ct);
            return rows.Select(ToDomain).ToList();
        }, ct);
    }

    private static StateSnapshot ToDomain(DbStateSnapshot row) => new()
    {
        Id = row.Id,
        CaseId = row.CaseId,
        ChatId = row.ChatId,
        AsOf = row.AsOf,
        DynamicLabel = row.DynamicLabel,
        RelationshipStatus = row.RelationshipStatus,
        AlternativeStatus = row.AlternativeStatus,
        InitiativeScore = row.InitiativeScore,
        ResponsivenessScore = row.ResponsivenessScore,
        OpennessScore = row.OpennessScore,
        WarmthScore = row.WarmthScore,
        ReciprocityScore = row.ReciprocityScore,
        AmbiguityScore = row.AmbiguityScore,
        AvoidanceRiskScore = row.AvoidanceRiskScore,
        EscalationReadinessScore = row.EscalationReadinessScore,
        ExternalPressureScore = row.ExternalPressureScore,
        Confidence = row.Confidence,
        PeriodId = row.PeriodId,
        KeySignalRefsJson = row.KeySignalRefsJson,
        RiskRefsJson = row.RiskRefsJson,
        CreatedAt = row.CreatedAt,
        SourceSessionId = row.SourceSessionId,
        SourceMessageId = row.SourceMessageId
    };

    private static ProfileSnapshot ToDomain(DbProfileSnapshot row) => new()
    {
        Id = row.Id,
        SubjectType = row.SubjectType,
        SubjectId = row.SubjectId,
        CaseId = row.CaseId,
        ChatId = row.ChatId,
        PeriodId = row.PeriodId,
        Summary = row.Summary,
        Confidence = row.Confidence,
        Stability = row.Stability,
        CreatedAt = row.CreatedAt,
        SourceSessionId = row.SourceSessionId,
        SourceMessageId = row.SourceMessageId
    };

    private static ProfileTrait ToDomain(DbProfileTrait row) => new()
    {
        Id = row.Id,
        ProfileSnapshotId = row.ProfileSnapshotId,
        TraitKey = row.TraitKey,
        ValueLabel = row.ValueLabel,
        Confidence = row.Confidence,
        Stability = row.Stability,
        IsSensitive = row.IsSensitive,
        EvidenceRefsJson = row.EvidenceRefsJson,
        CreatedAt = row.CreatedAt,
        SourceSessionId = row.SourceSessionId,
        SourceMessageId = row.SourceMessageId
    };

    private async Task<TResult> WithDbContextAsync<TResult>(Func<TgAssistantDbContext, Task<TResult>> action, CancellationToken ct)
    {
        var ambientDb = AmbientDbContextScope.Current;
        if (ambientDb is not null)
        {
            return await action(ambientDb);
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await action(db);
    }

    private async Task WithDbContextAsync(Func<TgAssistantDbContext, Task> action, CancellationToken ct)
    {
        var ambientDb = AmbientDbContextScope.Current;
        if (ambientDb is not null)
        {
            await action(ambientDb);
            return;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        await action(db);
    }
}
