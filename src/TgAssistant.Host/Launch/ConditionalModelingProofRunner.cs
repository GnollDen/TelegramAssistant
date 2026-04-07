using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Host.OperatorWeb;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Host.Launch;

public static class ConditionalModelingProofRunner
{
    private const string TriggerKind = "conditional_modeling_proof";
    private const string ActiveStatus = "active";
    private const string PersonTypeOperator = "operator";
    private const string PersonTypeTracked = "tracked_person";
    private const string CompositeBaselineExceptionRenderMode = "baseline-rule|exception-rule";

    public static async Task<ConditionalModelingProofReport> RunAsync(
        IServiceProvider services,
        string? outputPath = null,
        CancellationToken ct = default)
    {
        var resolvedOutputPath = ResolveOutputPath(outputPath);
        var report = new ConditionalModelingProofReport
        {
            GeneratedAtUtc = DateTime.UtcNow,
            OutputPath = resolvedOutputPath
        };

        Exception? fatal = null;
        try
        {
            using var scope = services.CreateScope();
            var appService = scope.ServiceProvider.GetRequiredService<IOperatorResolutionApplicationService>();
            var conditionalRepository = scope.ServiceProvider.GetRequiredService<IConditionalKnowledgeRepository>();
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TgAssistantDbContext>>();
            var workspaceHtml = ResolveWorkspaceShellHtml();

            await RunBaselinePlusExceptionPublishableCaseAsync(
                report,
                appService,
                conditionalRepository,
                dbFactory,
                workspaceHtml,
                ct);

            await RunPhaseDriftPublishableCaseAsync(
                report,
                appService,
                conditionalRepository,
                dbFactory,
                workspaceHtml,
                ct);

            await RunNoEvidenceRejectedWithHonestyStateCaseAsync(
                report,
                appService,
                conditionalRepository,
                dbFactory,
                workspaceHtml,
                ct);

            report.Passed = report.Cases.All(x => x.Passed);
        }
        catch (Exception ex)
        {
            fatal = ex;
            report.Passed = false;
            report.FatalError = ex.Message;
        }
        finally
        {
            Directory.CreateDirectory(Path.GetDirectoryName(resolvedOutputPath)!);
            var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(resolvedOutputPath, json, ct);
        }

        if (!report.Passed)
        {
            throw new InvalidOperationException("Conditional modeling proof failed.", fatal);
        }

        return report;
    }

    private static async Task RunBaselinePlusExceptionPublishableCaseAsync(
        ConditionalModelingProofReport report,
        IOperatorResolutionApplicationService appService,
        IConditionalKnowledgeRepository conditionalRepository,
        IDbContextFactory<TgAssistantDbContext> dbFactory,
        string workspaceHtml,
        CancellationToken ct)
    {
        var scopeKey = BuildScopeKey("baseline_exception");
        var (_, trackedPersonId) = await EnsureTrackedPersonAsync(dbFactory, scopeKey, ct);
        var nowUtc = DateTime.UtcNow;
        var subjectRef = $"person:{trackedPersonId:D}";
        var ruleId = Guid.NewGuid();

        var baselineState = await conditionalRepository.InsertAsync(
            new ConditionalKnowledgeWriteRequest
            {
                ScopeKey = scopeKey,
                TrackedPersonId = trackedPersonId,
                FactFamily = DossierFieldConditionalFamilies.ProfilePreference,
                SubjectRef = subjectRef,
                RuleKind = ConditionalKnowledgeRuleKinds.BaselineRule,
                RuleId = ruleId,
                BaselineValue = "weekday_morning_texting",
                ValidFromUtc = nowUtc.AddMinutes(-10),
                EvidenceRefs = [$"evidence:{Guid.NewGuid():D}"],
                TriggerKind = TriggerKind,
                TriggerRef = $"{TriggerKind}:baseline_exception:baseline"
            },
            ct);

        var exceptionState = await conditionalRepository.InsertAsync(
            new ConditionalKnowledgeWriteRequest
            {
                ScopeKey = scopeKey,
                TrackedPersonId = trackedPersonId,
                FactFamily = DossierFieldConditionalFamilies.ProfilePreference,
                SubjectRef = subjectRef,
                RuleKind = ConditionalKnowledgeRuleKinds.ExceptionRule,
                RuleId = ruleId,
                ParentRuleId = ruleId,
                ExceptionValue = "silent_mode_during_work_hours",
                ValidFromUtc = nowUtc.AddMinutes(-9),
                EvidenceRefs = [$"evidence:{Guid.NewGuid():D}"],
                TriggerKind = TriggerKind,
                TriggerRef = $"{TriggerKind}:baseline_exception:exception"
            },
            ct);

        var result = await appService.QueryPersonWorkspaceCurrentWorldAsync(
            new OperatorPersonWorkspaceCurrentWorldQueryRequest
            {
                TrackedPersonId = trackedPersonId,
                OperatorIdentity = BuildOperatorIdentity(nowUtc),
                Session = BuildOperatorSession(nowUtc, trackedPersonId)
            },
            ct);

        var baselineRow = result.CurrentWorld.BaselineRules.FirstOrDefault(x => x.RuleId == baselineState.RuleId);
        var exceptionRow = result.CurrentWorld.ExceptionRules.FirstOrDefault(x => x.ExceptionId == exceptionState.Id);
        var actualRenderModes = new[]
            {
                baselineRow?.RenderMode,
                exceptionRow?.RenderMode
            }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        var publicationMatches = string.Equals(
            result.CurrentWorld.ConditionalHonestyState.PublicationState,
            WsB5ResponsePublicationStates.Publishable,
            StringComparison.Ordinal)
            && string.Equals(baselineRow?.PublicationState, WsB5ResponsePublicationStates.Publishable, StringComparison.Ordinal)
            && string.Equals(exceptionRow?.PublicationState, WsB5ResponsePublicationStates.Publishable, StringComparison.Ordinal);
        var renderMatches = actualRenderModes.Length == 2
            && actualRenderModes.Contains(WsB5ConditionalRenderModes.BaselineRule, StringComparer.Ordinal)
            && actualRenderModes.Contains(WsB5ConditionalRenderModes.ExceptionRule, StringComparer.Ordinal);
        var webParityMatches = WebContainsBaselineExceptionRenderContract(workspaceHtml);

        report.Cases.Add(new ConditionalModelingProofCase
        {
            CaseId = "baseline_plus_exception_publishable",
            ScopeKey = scopeKey,
            TrackedPersonId = trackedPersonId,
            RuleId = baselineState.RuleId,
            ExceptionId = exceptionState.Id,
            PhaseMarkerId = null,
            ExpectedPublicationState = WsB5ResponsePublicationStates.Publishable,
            ActualPublicationState = result.CurrentWorld.ConditionalHonestyState.PublicationState,
            ExpectedRenderMode = CompositeBaselineExceptionRenderMode,
            ActualRenderMode = string.Join("|", actualRenderModes),
            Reason = "api_rows_and_web_contract_keep_baseline_and_exception_render_modes_distinct_with_publishable_honesty_state",
            Passed = result.Accepted
                && publicationMatches
                && renderMatches
                && webParityMatches
        });
    }

    private static async Task RunPhaseDriftPublishableCaseAsync(
        ConditionalModelingProofReport report,
        IOperatorResolutionApplicationService appService,
        IConditionalKnowledgeRepository conditionalRepository,
        IDbContextFactory<TgAssistantDbContext> dbFactory,
        string workspaceHtml,
        CancellationToken ct)
    {
        var scopeKey = BuildScopeKey("phase_drift");
        var (_, trackedPersonId) = await EnsureTrackedPersonAsync(dbFactory, scopeKey, ct);
        var nowUtc = DateTime.UtcNow;
        var subjectRef = $"person:{trackedPersonId:D}";

        var phaseState = await conditionalRepository.InsertAsync(
            new ConditionalKnowledgeWriteRequest
            {
                ScopeKey = scopeKey,
                TrackedPersonId = trackedPersonId,
                FactFamily = DossierFieldConditionalFamilies.PhaseMarker,
                SubjectRef = subjectRef,
                RuleKind = ConditionalKnowledgeRuleKinds.PhaseMarker,
                RuleId = Guid.NewGuid(),
                PhaseLabel = "style_shift_detected",
                PhaseReason = "transition_to_shorter_replies",
                ValidFromUtc = nowUtc.AddMinutes(-11),
                ValidToUtc = nowUtc.AddMinutes(30),
                EvidenceRefs = [$"evidence:{Guid.NewGuid():D}"],
                TriggerKind = TriggerKind,
                TriggerRef = $"{TriggerKind}:phase_drift:phase"
            },
            ct);

        var result = await appService.QueryPersonWorkspaceCurrentWorldAsync(
            new OperatorPersonWorkspaceCurrentWorldQueryRequest
            {
                TrackedPersonId = trackedPersonId,
                OperatorIdentity = BuildOperatorIdentity(nowUtc),
                Session = BuildOperatorSession(nowUtc, trackedPersonId)
            },
            ct);

        var phaseRow = result.CurrentWorld.PhaseMarkers.FirstOrDefault(x => x.PhaseMarkerId == phaseState.Id);
        var publicationMatches = string.Equals(
            result.CurrentWorld.ConditionalHonestyState.PublicationState,
            WsB5ResponsePublicationStates.Publishable,
            StringComparison.Ordinal)
            && string.Equals(phaseRow?.PublicationState, WsB5ResponsePublicationStates.Publishable, StringComparison.Ordinal);
        var renderMatches = string.Equals(phaseRow?.RenderMode, WsB5ConditionalRenderModes.PhaseMarker, StringComparison.Ordinal);
        var webParityMatches = WebContainsPhaseRenderContract(workspaceHtml);

        report.Cases.Add(new ConditionalModelingProofCase
        {
            CaseId = "phase_drift_publishable",
            ScopeKey = scopeKey,
            TrackedPersonId = trackedPersonId,
            RuleId = phaseState.RuleId,
            ExceptionId = null,
            PhaseMarkerId = phaseState.Id,
            ExpectedPublicationState = WsB5ResponsePublicationStates.Publishable,
            ActualPublicationState = result.CurrentWorld.ConditionalHonestyState.PublicationState,
            ExpectedRenderMode = WsB5ConditionalRenderModes.PhaseMarker,
            ActualRenderMode = phaseRow?.RenderMode ?? string.Empty,
            Reason = "api_phase_marker_row_and_web_phase_section_preserve_phase_marker_render_mode_with_validity_window",
            Passed = result.Accepted
                && publicationMatches
                && renderMatches
                && webParityMatches
        });
    }

    private static async Task RunNoEvidenceRejectedWithHonestyStateCaseAsync(
        ConditionalModelingProofReport report,
        IOperatorResolutionApplicationService appService,
        IConditionalKnowledgeRepository conditionalRepository,
        IDbContextFactory<TgAssistantDbContext> dbFactory,
        string workspaceHtml,
        CancellationToken ct)
    {
        var scopeKey = BuildScopeKey("no_evidence");
        var (_, trackedPersonId) = await EnsureTrackedPersonAsync(dbFactory, scopeKey, ct);
        var nowUtc = DateTime.UtcNow;
        var subjectRef = $"person:{trackedPersonId:D}";
        var rejectedRuleId = Guid.NewGuid();
        var rejectedWithExpectedReason = false;
        string rejectionReason = "not_rejected";

        try
        {
            await conditionalRepository.InsertAsync(
                new ConditionalKnowledgeWriteRequest
                {
                    ScopeKey = scopeKey,
                    TrackedPersonId = trackedPersonId,
                    FactFamily = DossierFieldConditionalFamilies.ProfilePreference,
                    SubjectRef = subjectRef,
                    RuleKind = ConditionalKnowledgeRuleKinds.BaselineRule,
                    RuleId = rejectedRuleId,
                    BaselineValue = "unsupported_without_evidence",
                    ValidFromUtc = nowUtc.AddMinutes(-3),
                    EvidenceRefs = [],
                    TriggerKind = TriggerKind,
                    TriggerRef = $"{TriggerKind}:no_evidence:baseline"
                },
                ct);
        }
        catch (InvalidOperationException ex)
        {
            rejectionReason = ex.Message;
            rejectedWithExpectedReason = string.Equals(
                ex.Message,
                ConditionalKnowledgeFailureReasons.EvidenceRefsRequired,
                StringComparison.Ordinal);
        }

        var result = await appService.QueryPersonWorkspaceCurrentWorldAsync(
            new OperatorPersonWorkspaceCurrentWorldQueryRequest
            {
                TrackedPersonId = trackedPersonId,
                OperatorIdentity = BuildOperatorIdentity(nowUtc),
                Session = BuildOperatorSession(nowUtc, trackedPersonId)
            },
            ct);

        var publicationState = result.CurrentWorld.ConditionalHonestyState.PublicationState;
        var renderMode = result.CurrentWorld.ConditionalHonestyState.RenderMode;
        var baselineRowsForRejectedRule = result.CurrentWorld.BaselineRules.Count(x => x.RuleId == rejectedRuleId);
        var webParityMatches = WebContainsHonestyRenderContract(workspaceHtml);

        report.Cases.Add(new ConditionalModelingProofCase
        {
            CaseId = "no_evidence_rule_rejected_with_honesty_state",
            ScopeKey = scopeKey,
            TrackedPersonId = trackedPersonId,
            RuleId = rejectedRuleId,
            ExceptionId = null,
            PhaseMarkerId = null,
            ExpectedPublicationState = WsB5ResponsePublicationStates.InsufficientEvidence,
            ActualPublicationState = publicationState,
            ExpectedRenderMode = WsB5ConditionalRenderModes.HonestyState,
            ActualRenderMode = renderMode ?? string.Empty,
            Reason = $"missing_evidence_rule_insert_rejected_and_honesty_state_surfaces_non_publishable_status ({rejectionReason})",
            Passed = result.Accepted
                && rejectedWithExpectedReason
                && baselineRowsForRejectedRule == 0
                && string.Equals(publicationState, WsB5ResponsePublicationStates.InsufficientEvidence, StringComparison.Ordinal)
                && string.Equals(renderMode, WsB5ConditionalRenderModes.HonestyState, StringComparison.Ordinal)
                && webParityMatches
        });
    }

    private static string ResolveWorkspaceShellHtml()
    {
        var html = typeof(OperatorWebEndpointExtensions)
            .GetField("OperatorPersonWorkspaceShellHtml", BindingFlags.NonPublic | BindingFlags.Static)
            ?.GetRawConstantValue() as string;

        if (string.IsNullOrWhiteSpace(html))
        {
            throw new InvalidOperationException("Conditional modeling proof failed: person-workspace shell HTML constant not found.");
        }

        return html;
    }

    private static bool WebContainsBaselineExceptionRenderContract(string html)
        => html.Contains("\"current-world-baseline-rules\"", StringComparison.Ordinal)
            && html.Contains("\"current-world-exception-rules\"", StringComparison.Ordinal)
            && html.Contains("\"baseline-rule\"", StringComparison.Ordinal)
            && html.Contains("\"exception-rule\"", StringComparison.Ordinal)
            && html.Contains("No rows are available for this render mode.", StringComparison.Ordinal);

    private static bool WebContainsPhaseRenderContract(string html)
        => html.Contains("\"current-world-phase-markers\"", StringComparison.Ordinal)
            && html.Contains("\"phase-marker\"", StringComparison.Ordinal)
            && html.Contains("<p><strong>Window:</strong>", StringComparison.Ordinal);

    private static bool WebContainsHonestyRenderContract(string html)
        => html.Contains("\"current-world-honesty-state\"", StringComparison.Ordinal)
            && html.Contains("\"honesty-state\"", StringComparison.Ordinal)
            && html.Contains("Current-world output is explicitly insufficient_evidence; publishable content is intentionally withheld.", StringComparison.Ordinal);

    private static OperatorIdentityContext BuildOperatorIdentity(DateTime nowUtc)
    {
        return new OperatorIdentityContext
        {
            OperatorId = "proof-operator",
            OperatorDisplay = "Proof Operator",
            SurfaceSubject = "conditional_modeling_proof",
            AuthSource = "proof",
            AuthTimeUtc = nowUtc.AddMinutes(-1)
        };
    }

    private static OperatorSessionContext BuildOperatorSession(DateTime nowUtc, Guid activeTrackedPersonId)
    {
        return new OperatorSessionContext
        {
            OperatorSessionId = $"conditional-proof:{Guid.NewGuid():N}",
            Surface = OperatorSurfaceTypes.Web,
            AuthenticatedAtUtc = nowUtc.AddMinutes(-1),
            LastSeenAtUtc = nowUtc,
            ActiveMode = OperatorModeTypes.ResolutionQueue,
            ActiveTrackedPersonId = activeTrackedPersonId
        };
    }

    private static async Task<(Guid OperatorPersonId, Guid TrackedPersonId)> EnsureTrackedPersonAsync(
        IDbContextFactory<TgAssistantDbContext> dbFactory,
        string scopeKey,
        CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var nowUtc = DateTime.UtcNow;

        var operatorPerson = await db.Persons
            .FirstOrDefaultAsync(x =>
                x.ScopeKey == scopeKey
                && x.PersonType == PersonTypeOperator
                && x.Status == ActiveStatus,
                ct);
        if (operatorPerson == null)
        {
            operatorPerson = new DbPerson
            {
                Id = Guid.NewGuid(),
                ScopeKey = scopeKey,
                PersonType = PersonTypeOperator,
                DisplayName = "PHB-018B Proof Operator",
                CanonicalName = "phb_018b_proof_operator",
                Status = ActiveStatus,
                MetadataJson = "{}",
                CreatedAt = nowUtc,
                UpdatedAt = nowUtc
            };
            db.Persons.Add(operatorPerson);
        }

        var trackedPerson = await db.Persons
            .FirstOrDefaultAsync(x =>
                x.ScopeKey == scopeKey
                && x.PersonType == PersonTypeTracked
                && x.Status == ActiveStatus,
                ct);
        if (trackedPerson == null)
        {
            trackedPerson = new DbPerson
            {
                Id = Guid.NewGuid(),
                ScopeKey = scopeKey,
                PersonType = PersonTypeTracked,
                DisplayName = "PHB-018B Proof Tracked Person",
                CanonicalName = "phb_018b_proof_tracked_person",
                Status = ActiveStatus,
                MetadataJson = "{}",
                CreatedAt = nowUtc,
                UpdatedAt = nowUtc
            };
            db.Persons.Add(trackedPerson);
        }

        await db.SaveChangesAsync(ct);

        var operatorLink = await db.PersonOperatorLinks
            .FirstOrDefaultAsync(x =>
                x.ScopeKey == scopeKey
                && x.OperatorPersonId == operatorPerson.Id
                && x.PersonId == trackedPerson.Id
                && x.Status == ActiveStatus,
                ct);
        if (operatorLink == null)
        {
            db.PersonOperatorLinks.Add(new DbPersonOperatorLink
            {
                ScopeKey = scopeKey,
                OperatorPersonId = operatorPerson.Id,
                PersonId = trackedPerson.Id,
                LinkType = "operator_tracked",
                Status = ActiveStatus,
                CreatedAt = nowUtc,
                UpdatedAt = nowUtc
            });
        }

        await db.SaveChangesAsync(ct);
        return (operatorPerson.Id, trackedPerson.Id);
    }

    private static string BuildScopeKey(string caseKey)
        => $"proof:phb_018b:{caseKey}:{DateTime.UtcNow:yyyyMMddHHmmssfff}:{Guid.NewGuid():N}";

    private static string ResolveOutputPath(string? outputPath)
    {
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            return Path.GetFullPath(outputPath.Trim());
        }

        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "artifacts",
            "phase-b",
            "conditional-modeling-proof.json"));
    }
}

public sealed class ConditionalModelingProofReport
{
    [JsonPropertyName("generated_at_utc")]
    public DateTime GeneratedAtUtc { get; set; }

    [JsonPropertyName("output_path")]
    public string OutputPath { get; set; } = string.Empty;

    [JsonPropertyName("passed")]
    public bool Passed { get; set; }

    [JsonPropertyName("fatal_error")]
    public string? FatalError { get; set; }

    [JsonPropertyName("cases")]
    public List<ConditionalModelingProofCase> Cases { get; set; } = [];
}

public sealed class ConditionalModelingProofCase
{
    [JsonPropertyName("case_id")]
    public string CaseId { get; set; } = string.Empty;

    [JsonPropertyName("scope_key")]
    public string ScopeKey { get; set; } = string.Empty;

    [JsonPropertyName("tracked_person_id")]
    public Guid TrackedPersonId { get; set; }

    [JsonPropertyName("rule_id")]
    public Guid RuleId { get; set; }

    [JsonPropertyName("exception_id")]
    public Guid? ExceptionId { get; set; }

    [JsonPropertyName("phase_marker_id")]
    public Guid? PhaseMarkerId { get; set; }

    [JsonPropertyName("expected_publication_state")]
    public string ExpectedPublicationState { get; set; } = string.Empty;

    [JsonPropertyName("actual_publication_state")]
    public string ActualPublicationState { get; set; } = string.Empty;

    [JsonPropertyName("expected_render_mode")]
    public string ExpectedRenderMode { get; set; } = string.Empty;

    [JsonPropertyName("actual_render_mode")]
    public string ActualRenderMode { get; set; } = string.Empty;

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;

    [JsonPropertyName("passed")]
    public bool Passed { get; set; }
}
