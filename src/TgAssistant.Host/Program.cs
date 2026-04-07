using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using StackExchange.Redis;
using System.Text.Json;
using System.Net.Http.Headers;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database;
using TgAssistant.Infrastructure.Database.Ef;
using TgAssistant.Infrastructure.Redis;
using TgAssistant.Host.Launch;
using TgAssistant.Host.BootstrapSeed;
using TgAssistant.Host.Health;
using TgAssistant.Host.OperatorApi;
using TgAssistant.Host.OperatorWeb;
using TgAssistant.Host.Stage5Repair;
using TgAssistant.Host.Startup;
using TgAssistant.Intelligence.Stage5;
using TgAssistant.Intelligence.Stage6;
using TgAssistant.Intelligence.Stage6.AutoCases;
using TgAssistant.Intelligence.Stage6.Clarification;
using TgAssistant.Intelligence.Stage6.CompetingContext;
using TgAssistant.Intelligence.Stage6.Control;
using TgAssistant.Intelligence.Stage6.CurrentState;
using TgAssistant.Intelligence.Stage6.DraftReview;
using TgAssistant.Intelligence.Stage6.Drafts;
using TgAssistant.Intelligence.Stage6.Network;
using TgAssistant.Intelligence.Stage6.Outcome;
using TgAssistant.Intelligence.Stage6.Periodization;
using TgAssistant.Intelligence.Stage6.Profiles;
using TgAssistant.Intelligence.Stage6.Strategy;
using TgAssistant.Processing.Archive;
using TgAssistant.Processing.Archive.ExternalIngestion;
using TgAssistant.Processing.Workers;
using TgAssistant.Telegram.Operator;
using TgAssistant.Telegram.Listener;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("System", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("TgAssistant.Intelligence.Stage5", Serilog.Events.LogEventLevel.Debug)
    .MinimumLevel.Override("TgAssistant.Intelligence.Stage5.OpenRouterAnalysisService", Serilog.Events.LogEventLevel.Debug)
    .MinimumLevel.Override("TgAssistant.Processing.Media.OpenRouterMediaProcessor", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("TgAssistant.Processing.Media.OpenRouterVoiceParalinguisticsAnalyzer", Serilog.Events.LogEventLevel.Warning)
    .WriteTo.Async(a => a.Console())
    .CreateLogger();


try
{
    Log.Information("Starting Telegram Assistant...");
    var legacyStage6Entrypoints = new[]
    {
        "--stage6-execution-smoke",
        "--auto-case-smoke",
        "--stage6-light-ab-run"
    };
    var legacyStage6ArgPrefixes = new[]
    {
        "--stage6-light-ab-cases-file=",
        "--stage6-light-ab-output-dir=",
        "--stage6-light-ab-pass-label=",
        "--stage6-light-ab-model-override="
    };
    var requestedLegacyStage6Entrypoints = args
        .Where(arg => legacyStage6Entrypoints.Contains(arg, StringComparer.OrdinalIgnoreCase)
            || legacyStage6ArgPrefixes.Any(prefix => arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
    if (requestedLegacyStage6Entrypoints.Length > 0)
    {
        throw new InvalidOperationException(
            $"Legacy Stage6 helper entrypoints are not part of the active runtime contract: {string.Join(", ", requestedLegacyStage6Entrypoints)}. Use explicit legacy diagnostic switches instead.");
    }

    var runFoundationSmoke = args.Any(arg => string.Equals(arg, "--foundation-smoke", StringComparison.OrdinalIgnoreCase));
    var runClarificationSmoke = args.Any(arg => string.Equals(arg, "--clarification-smoke", StringComparison.OrdinalIgnoreCase));
    var runPeriodizationSmoke = args.Any(arg => string.Equals(arg, "--periodization-smoke", StringComparison.OrdinalIgnoreCase));
    var runStateSmoke = args.Any(arg => string.Equals(arg, "--state-smoke", StringComparison.OrdinalIgnoreCase));
    var runProfileSmoke = args.Any(arg => string.Equals(arg, "--profile-smoke", StringComparison.OrdinalIgnoreCase));
    var runStrategySmoke = args.Any(arg => string.Equals(arg, "--strategy-smoke", StringComparison.OrdinalIgnoreCase));
    var runDraftSmoke = args.Any(arg => string.Equals(arg, "--draft-smoke", StringComparison.OrdinalIgnoreCase));
    var runReviewSmoke = args.Any(arg => string.Equals(arg, "--review-smoke", StringComparison.OrdinalIgnoreCase));
    var runOutcomeSmoke = args.Any(arg => string.Equals(arg, "--outcome-smoke", StringComparison.OrdinalIgnoreCase));
    var runBudgetSmoke = args.Any(arg => string.Equals(arg, "--budget-smoke", StringComparison.OrdinalIgnoreCase));
    var runEvalSmoke = args.Any(arg => string.Equals(arg, "--eval-smoke", StringComparison.OrdinalIgnoreCase));
    var runCompetingContextSmoke = args.Any(arg => string.Equals(arg, "--competing-context-smoke", StringComparison.OrdinalIgnoreCase));
    var runLegacyBotSmoke = args.Any(arg => string.Equals(arg, "--legacy-bot-smoke", StringComparison.OrdinalIgnoreCase));
    var runLegacyAutoCaseSmoke = args.Any(arg => string.Equals(arg, "--legacy-auto-case-smoke", StringComparison.OrdinalIgnoreCase));
    var runLegacyNetworkSmoke = args.Any(arg => string.Equals(arg, "--legacy-network-smoke", StringComparison.OrdinalIgnoreCase));
    var allowLegacyStage8Bridge = args.Any(arg => string.Equals(arg, "--allow-legacy-stage8-bridge", StringComparison.OrdinalIgnoreCase));
    var includeLegacyStage6Diagnostics = runClarificationSmoke
        || runPeriodizationSmoke
        || runStateSmoke
        || runProfileSmoke
        || runStrategySmoke
        || runDraftSmoke
        || runReviewSmoke
        || runOutcomeSmoke
        || runBudgetSmoke
        || runEvalSmoke
        || runCompetingContextSmoke
        || runLegacyBotSmoke
        || runLegacyAutoCaseSmoke
        || runLegacyNetworkSmoke;
    var includeLegacyStage6ClusterDiagnostics = runLegacyBotSmoke
        || runLegacyAutoCaseSmoke
        || runLegacyNetworkSmoke;
    var runStage5Smoke = args.Any(arg => string.Equals(arg, "--stage5-smoke", StringComparison.OrdinalIgnoreCase));
    var runPassEnvelopeSmoke = args.Any(arg => string.Equals(arg, "--pass-envelope-smoke", StringComparison.OrdinalIgnoreCase));
    var runNormalizationSmoke = args.Any(arg => string.Equals(arg, "--normalization-smoke", StringComparison.OrdinalIgnoreCase));
    var runModelPassAuditSmoke = args.Any(arg => string.Equals(arg, "--model-pass-audit-smoke", StringComparison.OrdinalIgnoreCase));
    var runLlmGatewaySuccessSmoke = args.Any(arg => string.Equals(arg, "--llm-gateway-success-smoke", StringComparison.OrdinalIgnoreCase));
    var runLlmGatewayCodexMediaSmoke = args.Any(arg => string.Equals(arg, "--llm-gateway-codex-media-smoke", StringComparison.OrdinalIgnoreCase));
    var runLlmGatewayFailureSmoke = args.Any(arg => string.Equals(arg, "--llm-gateway-failure-smoke", StringComparison.OrdinalIgnoreCase));
    var runLlmGatewayAnalyticsSmoke = args.Any(arg => string.Equals(arg, "--llm-gateway-analytics-smoke", StringComparison.OrdinalIgnoreCase));
    var runLlmGatewayAnalyticsValidate = args.Any(arg => string.Equals(arg, "--llm-gateway-analytics-validate", StringComparison.OrdinalIgnoreCase));
    var runLlmGatewayTransportValidate = args.Any(arg => string.Equals(arg, "--llm-gateway-transport-validate", StringComparison.OrdinalIgnoreCase));
    var runLlmGatewayExperimentSmoke = args.Any(arg => string.Equals(arg, "--llm-gateway-experiment-smoke", StringComparison.OrdinalIgnoreCase));
    var runLlmContractNormalizationSmoke = args.Any(arg => string.Equals(arg, "--llm-contract-normalization-smoke", StringComparison.OrdinalIgnoreCase));
    var runLlmContractFamilyValidate = args.Any(arg => string.Equals(arg, "--llm-contract-family-validate", StringComparison.OrdinalIgnoreCase));
    var runLlmGatewayReplayAb = args.Any(arg => string.Equals(arg, "--llm-gateway-replay-ab", StringComparison.OrdinalIgnoreCase));
    var runEditDiffPilotSmoke = args.Any(arg => string.Equals(arg, "--edit-diff-pilot-smoke", StringComparison.OrdinalIgnoreCase));
    var runEditDiffPilotValidate = args.Any(arg => string.Equals(arg, "--edit-diff-pilot-validate", StringComparison.OrdinalIgnoreCase));
    var llmGatewayReplayAbOutputArg = args.FirstOrDefault(arg => arg.StartsWith("--llm-gateway-replay-ab-output=", StringComparison.OrdinalIgnoreCase));
    var llmGatewayReplayAbOutput = llmGatewayReplayAbOutputArg is null
        ? null
        : llmGatewayReplayAbOutputArg["--llm-gateway-replay-ab-output=".Length..];
    var llmGatewayAnalyticsValidateOutputArg = args.FirstOrDefault(arg => arg.StartsWith("--llm-gateway-analytics-validate-output=", StringComparison.OrdinalIgnoreCase));
    var llmGatewayAnalyticsValidateOutput = llmGatewayAnalyticsValidateOutputArg is null
        ? null
        : llmGatewayAnalyticsValidateOutputArg["--llm-gateway-analytics-validate-output=".Length..];
    var llmGatewayTransportValidateOutputArg = args.FirstOrDefault(arg => arg.StartsWith("--llm-gateway-transport-validate-output=", StringComparison.OrdinalIgnoreCase));
    var llmGatewayTransportValidateOutput = llmGatewayTransportValidateOutputArg is null
        ? null
        : llmGatewayTransportValidateOutputArg["--llm-gateway-transport-validate-output=".Length..];
    var llmContractFamilyValidateOutputArg = args.FirstOrDefault(arg => arg.StartsWith("--llm-contract-family-validate-output=", StringComparison.OrdinalIgnoreCase));
    var llmContractFamilyValidateOutput = llmContractFamilyValidateOutputArg is null
        ? null
        : llmContractFamilyValidateOutputArg["--llm-contract-family-validate-output=".Length..];
    var editDiffPilotValidateOutputArg = args.FirstOrDefault(arg => arg.StartsWith("--edit-diff-pilot-validate-output=", StringComparison.OrdinalIgnoreCase));
    var editDiffPilotValidateOutput = editDiffPilotValidateOutputArg is null
        ? null
        : editDiffPilotValidateOutputArg["--edit-diff-pilot-validate-output=".Length..];
    var opint003ValidateOutputArg = args.FirstOrDefault(arg => arg.StartsWith("--opint-003-d-validate-output=", StringComparison.OrdinalIgnoreCase));
    var opint003ValidateOutput = opint003ValidateOutputArg is null
        ? null
        : opint003ValidateOutputArg["--opint-003-d-validate-output=".Length..];
    var opint004SmokeOutputArg = args.FirstOrDefault(arg => arg.StartsWith("--opint-004-a-smoke-output=", StringComparison.OrdinalIgnoreCase));
    var opint004SmokeOutput = opint004SmokeOutputArg is null
        ? null
        : opint004SmokeOutputArg["--opint-004-a-smoke-output=".Length..];
    var opint007B1SmokeOutputArg = args.FirstOrDefault(arg => arg.StartsWith("--opint-007-b1-smoke-output=", StringComparison.OrdinalIgnoreCase));
    var opint007B1SmokeOutput = opint007B1SmokeOutputArg is null
        ? null
        : opint007B1SmokeOutputArg["--opint-007-b1-smoke-output=".Length..];
    var opint007B2SmokeOutputArg = args.FirstOrDefault(arg => arg.StartsWith("--opint-007-b2-smoke-output=", StringComparison.OrdinalIgnoreCase));
    var opint007B2SmokeOutput = opint007B2SmokeOutputArg is null
        ? null
        : opint007B2SmokeOutputArg["--opint-007-b2-smoke-output=".Length..];
    var opint007B3SmokeOutputArg = args.FirstOrDefault(arg => arg.StartsWith("--opint-007-b3-smoke-output=", StringComparison.OrdinalIgnoreCase));
    var opint007B3SmokeOutput = opint007B3SmokeOutputArg is null
        ? null
        : opint007B3SmokeOutputArg["--opint-007-b3-smoke-output=".Length..];
    var opint009ASmokeOutputArg = args.FirstOrDefault(arg => arg.StartsWith("--opint-009-a-smoke-output=", StringComparison.OrdinalIgnoreCase));
    var opint009ASmokeOutput = opint009ASmokeOutputArg is null
        ? null
        : opint009ASmokeOutputArg["--opint-009-a-smoke-output=".Length..];
    var opint009BSmokeOutputArg = args.FirstOrDefault(arg => arg.StartsWith("--opint-009-b-smoke-output=", StringComparison.OrdinalIgnoreCase));
    var opint009BSmokeOutput = opint009BSmokeOutputArg is null
        ? null
        : opint009BSmokeOutputArg["--opint-009-b-smoke-output=".Length..];
    var opint009C1SmokeOutputArg = args.FirstOrDefault(arg => arg.StartsWith("--opint-009-c1-smoke-output=", StringComparison.OrdinalIgnoreCase));
    var opint009C1SmokeOutput = opint009C1SmokeOutputArg is null
        ? null
        : opint009C1SmokeOutputArg["--opint-009-c1-smoke-output=".Length..];
    var opint009C2SmokeOutputArg = args.FirstOrDefault(arg => arg.StartsWith("--opint-009-c2-smoke-output=", StringComparison.OrdinalIgnoreCase));
    var opint009C2SmokeOutput = opint009C2SmokeOutputArg is null
        ? null
        : opint009C2SmokeOutputArg["--opint-009-c2-smoke-output=".Length..];
    var opint009DSmokeOutputArg = args.FirstOrDefault(arg => arg.StartsWith("--opint-009-d-smoke-output=", StringComparison.OrdinalIgnoreCase));
    var opint009DSmokeOutput = opint009DSmokeOutputArg is null
        ? null
        : opint009DSmokeOutputArg["--opint-009-d-smoke-output=".Length..];
    var opint012ASmokeOutputArg = args.FirstOrDefault(arg => arg.StartsWith("--opint-012-a-smoke-output=", StringComparison.OrdinalIgnoreCase));
    var opint012ASmokeOutput = opint012ASmokeOutputArg is null
        ? null
        : opint012ASmokeOutputArg["--opint-012-a-smoke-output=".Length..];
    var opint012BSmokeOutputArg = args.FirstOrDefault(arg => arg.StartsWith("--opint-012-b-smoke-output=", StringComparison.OrdinalIgnoreCase));
    var opint012BSmokeOutput = opint012BSmokeOutputArg is null
        ? null
        : opint012BSmokeOutputArg["--opint-012-b-smoke-output=".Length..];
    var opintHomeDashboardSmokeOutputArg = args.FirstOrDefault(arg => arg.StartsWith("--opint-home-dashboard-smoke-output=", StringComparison.OrdinalIgnoreCase));
    var opintHomeDashboardSmokeOutput = opintHomeDashboardSmokeOutputArg is null
        ? null
        : opintHomeDashboardSmokeOutputArg["--opint-home-dashboard-smoke-output=".Length..];
    var resolutionInterpretationLoopValidateOutputArg = args.FirstOrDefault(arg => arg.StartsWith("--resolution-interpretation-loop-v1-validate-output=", StringComparison.OrdinalIgnoreCase));
    var resolutionInterpretationLoopValidateOutput = resolutionInterpretationLoopValidateOutputArg is null
        ? null
        : resolutionInterpretationLoopValidateOutputArg["--resolution-interpretation-loop-v1-validate-output=".Length..];
    var runtimeControlDetailProofOutputArg = args.FirstOrDefault(arg => arg.StartsWith("--runtime-control-detail-proof-output=", StringComparison.OrdinalIgnoreCase));
    var runtimeControlDetailProofOutput = runtimeControlDetailProofOutputArg is null
        ? null
        : runtimeControlDetailProofOutputArg["--runtime-control-detail-proof-output=".Length..];
    var temporalPersonStateProofOutputArg = args.FirstOrDefault(arg => arg.StartsWith("--temporal-person-state-proof-output=", StringComparison.OrdinalIgnoreCase));
    var temporalPersonStateProofOutput = temporalPersonStateProofOutputArg is null
        ? null
        : temporalPersonStateProofOutputArg["--temporal-person-state-proof-output=".Length..];
    var personHistoryProofOutputArg = args.FirstOrDefault(arg => arg.StartsWith("--person-history-proof-output=", StringComparison.OrdinalIgnoreCase));
    var personHistoryProofOutput = personHistoryProofOutputArg is null
        ? null
        : personHistoryProofOutputArg["--person-history-proof-output=".Length..];
    var iterativeReintegrationProofOutputArg = args.FirstOrDefault(arg => arg.StartsWith("--iterative-reintegration-proof-output=", StringComparison.OrdinalIgnoreCase));
    var iterativeReintegrationProofOutput = iterativeReintegrationProofOutputArg is null
        ? null
        : iterativeReintegrationProofOutputArg["--iterative-reintegration-proof-output=".Length..];
    var stageSemanticContractProofOutputArg = args.FirstOrDefault(arg => arg.StartsWith("--stage-semantic-contract-proof-output=", StringComparison.OrdinalIgnoreCase));
    var stageSemanticContractProofOutput = stageSemanticContractProofOutputArg is null
        ? null
        : stageSemanticContractProofOutputArg["--stage-semantic-contract-proof-output=".Length..];
    var aiConflictSessionV1ProofOutputArg = args.FirstOrDefault(arg => arg.StartsWith("--ai-conflict-session-v1-proof-output=", StringComparison.OrdinalIgnoreCase));
    var aiConflictSessionV1ProofOutput = aiConflictSessionV1ProofOutputArg is null
        ? null
        : aiConflictSessionV1ProofOutputArg["--ai-conflict-session-v1-proof-output=".Length..];
    var runStage6BootstrapSmoke = args.Any(arg => string.Equals(arg, "--stage6-bootstrap-smoke", StringComparison.OrdinalIgnoreCase));
    var runStage7DossierProfileSmoke = args.Any(arg => string.Equals(arg, "--stage7-dossier-profile-smoke", StringComparison.OrdinalIgnoreCase));
    var runStage7PairDynamicsSmoke = args.Any(arg => string.Equals(arg, "--stage7-pair-dynamics-smoke", StringComparison.OrdinalIgnoreCase));
    var runStage7TimelineSmoke = args.Any(arg => string.Equals(arg, "--stage7-timeline-smoke", StringComparison.OrdinalIgnoreCase));
    var runResolutionRecomputeContractSmoke = args.Any(arg => string.Equals(arg, "--resolution-recompute-contract-smoke", StringComparison.OrdinalIgnoreCase));
    var runStage8RelatedConflictSmoke = args.Any(arg => string.Equals(arg, "--stage8-related-conflict-smoke", StringComparison.OrdinalIgnoreCase));
    var runStage8RecomputeSmoke = args.Any(arg => string.Equals(arg, "--stage8-recompute-smoke", StringComparison.OrdinalIgnoreCase));
    var runOpint006B1Smoke = args.Any(arg => string.Equals(arg, "--opint-006-b1-smoke", StringComparison.OrdinalIgnoreCase));
    var runOpint006B2Smoke = args.Any(arg => string.Equals(arg, "--opint-006-b2-smoke", StringComparison.OrdinalIgnoreCase));
    var runOpint006CSmoke = args.Any(arg => string.Equals(arg, "--opint-006-c-smoke", StringComparison.OrdinalIgnoreCase));
    var runOpint003Validate = args.Any(arg => string.Equals(arg, "--opint-003-d-validate", StringComparison.OrdinalIgnoreCase));
    var runOpint004Smoke = args.Any(arg => string.Equals(arg, "--opint-004-a-smoke", StringComparison.OrdinalIgnoreCase));
    var runOpint007B1Smoke = args.Any(arg => string.Equals(arg, "--opint-007-b1-smoke", StringComparison.OrdinalIgnoreCase));
    var runOpint007B2Smoke = args.Any(arg => string.Equals(arg, "--opint-007-b2-smoke", StringComparison.OrdinalIgnoreCase));
    var runOpint007B3Smoke = args.Any(arg => string.Equals(arg, "--opint-007-b3-smoke", StringComparison.OrdinalIgnoreCase));
    var runOpint009ASmoke = args.Any(arg => string.Equals(arg, "--opint-009-a-smoke", StringComparison.OrdinalIgnoreCase));
    var runOpint009BSmoke = args.Any(arg => string.Equals(arg, "--opint-009-b-smoke", StringComparison.OrdinalIgnoreCase));
    var runOpint009C1Smoke = args.Any(arg => string.Equals(arg, "--opint-009-c1-smoke", StringComparison.OrdinalIgnoreCase));
    var runOpint009C2Smoke = args.Any(arg => string.Equals(arg, "--opint-009-c2-smoke", StringComparison.OrdinalIgnoreCase));
    var runOpint009DSmoke = args.Any(arg => string.Equals(arg, "--opint-009-d-smoke", StringComparison.OrdinalIgnoreCase));
    var runOpint012ASmoke = args.Any(arg => string.Equals(arg, "--opint-012-a-smoke", StringComparison.OrdinalIgnoreCase));
    var runOpint012BSmoke = args.Any(arg => string.Equals(arg, "--opint-012-b-smoke", StringComparison.OrdinalIgnoreCase));
    var runOpintHomeDashboardSmoke = args.Any(arg => string.Equals(arg, "--opint-home-dashboard-smoke", StringComparison.OrdinalIgnoreCase));
    var runResolutionInterpretationLoopValidate = args.Any(arg => string.Equals(arg, "--resolution-interpretation-loop-v1-validate", StringComparison.OrdinalIgnoreCase));
    var runRuntimeControlDetailProof = args.Any(arg => string.Equals(arg, "--runtime-control-detail-proof", StringComparison.OrdinalIgnoreCase));
    var runTemporalPersonStateProof = args.Any(arg => string.Equals(arg, "--temporal-person-state-proof", StringComparison.OrdinalIgnoreCase));
    var runPersonHistoryProof = args.Any(arg => string.Equals(arg, "--person-history-proof", StringComparison.OrdinalIgnoreCase));
    var runIterativeReintegrationProof = args.Any(arg => string.Equals(arg, "--iterative-reintegration-proof", StringComparison.OrdinalIgnoreCase));
    var runStageSemanticContractProof = args.Any(arg => string.Equals(arg, "--stage-semantic-contract-proof", StringComparison.OrdinalIgnoreCase));
    var runAiConflictSessionV1Proof = args.Any(arg => string.Equals(arg, "--ai-conflict-session-v1-proof", StringComparison.OrdinalIgnoreCase));
    var runLaunchSmoke = args.Any(arg => string.Equals(arg, "--launch-smoke", StringComparison.OrdinalIgnoreCase));
    var runExternalArchiveSmoke = args.Any(arg => string.Equals(arg, "--external-archive-smoke", StringComparison.OrdinalIgnoreCase));
    var runStage5ScopedRepair = args.Any(arg => string.Equals(arg, "--stage5-scoped-repair", StringComparison.OrdinalIgnoreCase));
    var runStage5ScopedRepairApply = args.Any(arg => string.Equals(arg, "--stage5-scoped-repair-apply", StringComparison.OrdinalIgnoreCase));
    if (runStage5ScopedRepairApply && !runStage5ScopedRepair)
    {
        throw new InvalidOperationException(
            "--stage5-scoped-repair-apply requires --stage5-scoped-repair. Refusing to continue in normal runtime mode.");
    }
    var stage5ScopedRepairChatArg = args.FirstOrDefault(arg => arg.StartsWith("--stage5-scoped-repair-chat-id=", StringComparison.OrdinalIgnoreCase));
    var stage5ScopedRepairAuditDirArg = args.FirstOrDefault(arg => arg.StartsWith("--stage5-scoped-repair-audit-dir=", StringComparison.OrdinalIgnoreCase));
    var stage5ScopedRepairChatId = stage5ScopedRepairChatArg is null
        ? 885574984L
        : long.TryParse(stage5ScopedRepairChatArg["--stage5-scoped-repair-chat-id=".Length..], out var parsedChatId)
            ? parsedChatId
            : 0L;
    var stage5ScopedRepairAuditDir = stage5ScopedRepairAuditDirArg is null
        ? null
        : stage5ScopedRepairAuditDirArg["--stage5-scoped-repair-audit-dir=".Length..];
    var riskBackupIdArg = args.FirstOrDefault(arg => arg.StartsWith("--risk-backup-id=", StringComparison.OrdinalIgnoreCase));
    var riskBackupCreatedAtArg = args.FirstOrDefault(arg => arg.StartsWith("--risk-backup-created-at-utc=", StringComparison.OrdinalIgnoreCase));
    var riskBackupScopeArg = args.FirstOrDefault(arg => arg.StartsWith("--risk-backup-scope=", StringComparison.OrdinalIgnoreCase));
    var riskBackupArtifactArg = args.FirstOrDefault(arg => arg.StartsWith("--risk-backup-artifact-uri=", StringComparison.OrdinalIgnoreCase));
    var riskBackupChecksumArg = args.FirstOrDefault(arg => arg.StartsWith("--risk-backup-checksum=", StringComparison.OrdinalIgnoreCase));
    var riskOperatorArg = args.FirstOrDefault(arg => arg.StartsWith("--risk-operator=", StringComparison.OrdinalIgnoreCase));
    var riskReasonArg = args.FirstOrDefault(arg => arg.StartsWith("--risk-reason=", StringComparison.OrdinalIgnoreCase));
    var riskAuditIdArg = args.FirstOrDefault(arg => arg.StartsWith("--risk-audit-id=", StringComparison.OrdinalIgnoreCase));
    var riskOverrideOperatorArg = args.FirstOrDefault(arg => arg.StartsWith("--risk-override-operator=", StringComparison.OrdinalIgnoreCase));
    var riskOverrideReasonArg = args.FirstOrDefault(arg => arg.StartsWith("--risk-override-reason=", StringComparison.OrdinalIgnoreCase));
    var riskOverrideApprovalTokenArg = args.FirstOrDefault(arg => arg.StartsWith("--risk-override-approval-token=", StringComparison.OrdinalIgnoreCase));
    var riskOverrideAuditIdArg = args.FirstOrDefault(arg => arg.StartsWith("--risk-override-audit-id=", StringComparison.OrdinalIgnoreCase));
    var externalArchiveImportArg = args.FirstOrDefault(arg => arg.StartsWith("--external-archive-import-file=", StringComparison.OrdinalIgnoreCase));
    var externalArchiveActorArg = args.FirstOrDefault(arg => arg.StartsWith("--external-archive-actor=", StringComparison.OrdinalIgnoreCase));
    var externalArchiveImportFile = externalArchiveImportArg is null
        ? null
        : externalArchiveImportArg["--external-archive-import-file=".Length..];
    var externalArchiveActor = externalArchiveActorArg is null
        ? "operator"
        : externalArchiveActorArg["--external-archive-actor=".Length..];
    var runListSmokes = args.Any(arg => string.Equals(arg, "--list-smokes", StringComparison.OrdinalIgnoreCase));
    var runRuntimeWiringCheck = args.Any(arg => string.Equals(arg, "--runtime-wiring-check", StringComparison.OrdinalIgnoreCase));
    var runLivenessCheck = args.Any(arg => string.Equals(arg, "--liveness-check", StringComparison.OrdinalIgnoreCase));
    var runReadinessCheck = args.Any(arg => string.Equals(arg, "--readiness-check", StringComparison.OrdinalIgnoreCase));
    var runHealthCheck = args.Any(arg => string.Equals(arg, "--healthcheck", StringComparison.OrdinalIgnoreCase));
    var runOperatorSchemaInit = args.Any(arg => string.Equals(arg, "--operator-schema-init", StringComparison.OrdinalIgnoreCase));
    var runSeedBootstrapScope = args.Any(arg => string.Equals(arg, "--seed-bootstrap-scope", StringComparison.OrdinalIgnoreCase));
    var bootstrapSeedRequest = runSeedBootstrapScope
        ? BootstrapScopeSeedArgsParser.ParseOrThrow(args)
        : null;
    var preservedVerificationEntrypoints = new[]
    {
        "--foundation-smoke",
        "--stage5-smoke",
        "--pass-envelope-smoke",
        "--normalization-smoke",
        "--model-pass-audit-smoke",
        "--llm-gateway-success-smoke",
        "--llm-gateway-codex-media-smoke",
        "--llm-gateway-failure-smoke",
        "--llm-gateway-analytics-smoke",
        "--llm-gateway-analytics-validate",
        "--llm-gateway-transport-validate",
        "--llm-gateway-experiment-smoke",
        "--llm-contract-normalization-smoke",
        "--llm-contract-family-validate",
        "--llm-gateway-replay-ab",
        "--edit-diff-pilot-smoke",
        "--edit-diff-pilot-validate",
        "--stage6-bootstrap-smoke",
        "--stage7-dossier-profile-smoke",
        "--stage7-pair-dynamics-smoke",
        "--stage7-timeline-smoke",
        "--resolution-recompute-contract-smoke",
        "--stage8-related-conflict-smoke",
        "--stage8-recompute-smoke",
        "--opint-006-b1-smoke",
        "--opint-006-b2-smoke",
        "--opint-006-c-smoke",
        "--opint-004-a-smoke",
        "--opint-007-b1-smoke",
        "--opint-007-b2-smoke",
        "--opint-007-b3-smoke",
        "--opint-009-a-smoke",
        "--opint-009-b-smoke",
        "--opint-009-c1-smoke",
        "--opint-009-c2-smoke",
        "--opint-009-d-smoke",
        "--opint-012-a-smoke",
        "--opint-012-b-smoke",
        "--opint-home-dashboard-smoke",
        "--resolution-interpretation-loop-v1-validate",
        "--temporal-person-state-proof",
        "--person-history-proof",
        "--stage-semantic-contract-proof",
        "--ai-conflict-session-v1-proof",
        "--launch-smoke",
        "--external-archive-smoke"
    };

    var legacyDiagnosticEntrypoints = new[]
    {
        "--clarification-smoke",
        "--periodization-smoke",
        "--state-smoke",
        "--profile-smoke",
        "--strategy-smoke",
        "--draft-smoke",
        "--review-smoke",
        "--outcome-smoke",
        "--budget-smoke",
        "--eval-smoke",
        "--competing-context-smoke",
        "--legacy-bot-smoke",
        "--legacy-auto-case-smoke",
        "--legacy-network-smoke"
    };

    var requestedLegacyDiagnosticEntrypoints = args
        .Where(arg => legacyDiagnosticEntrypoints.Contains(arg, StringComparer.OrdinalIgnoreCase))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
    if (requestedLegacyDiagnosticEntrypoints.Length > 0 && !allowLegacyStage8Bridge)
    {
        throw new InvalidOperationException(
            $"Legacy Stage6 diagnostic entrypoints keep a retained domain-review to active Stage8 bridge and now require explicit admission via --allow-legacy-stage8-bridge: {string.Join(", ", requestedLegacyDiagnosticEntrypoints)}.");
    }

    if (runListSmokes)
    {
        Log.Information("Available preserved verification entrypoints: {VerificationEntrypoints}", string.Join(", ", preservedVerificationEntrypoints));
        Log.Information("Legacy diagnostic-only entrypoints: {DiagnosticEntrypoints}", string.Join(", ", legacyDiagnosticEntrypoints));
        Log.Information(
            "Legacy diagnostic-only entrypoints also require --allow-legacy-stage8-bridge because they retain the explicit legacy-to-active Stage8 bridge for bounded diagnostics.");
        return;
    }

    if (runPassEnvelopeSmoke)
    {
        PassEnvelopeContractSmokeRunner.Run();
        Log.Information("Pass envelope contract smoke requested via --pass-envelope-smoke. Exiting after successful verification.");
        return;
    }

    if (runNormalizationSmoke)
    {
        NormalizationContractSmokeRunner.Run();
        Log.Information("Normalization contract smoke requested via --normalization-smoke. Exiting after successful verification.");
        return;
    }

    if (runModelPassAuditSmoke)
    {
        await ModelPassAuditSmokeRunner.RunAsync();
        Log.Information("Model pass audit smoke requested via --model-pass-audit-smoke. Exiting after successful verification.");
        return;
    }

    if (runLlmGatewaySuccessSmoke)
    {
        await LlmGatewaySuccessSmokeRunner.RunAsync();
        Log.Information("LLM gateway success smoke requested via --llm-gateway-success-smoke. Exiting after successful verification.");
        return;
    }

    if (runLlmGatewayCodexMediaSmoke)
    {
        await LlmGatewayCodexMediaSmokeRunner.RunAsync();
        Log.Information("LLM gateway codex-media smoke requested via --llm-gateway-codex-media-smoke. Exiting after successful verification.");
        return;
    }

    if (runLlmGatewayFailureSmoke)
    {
        await LlmGatewayFailureSmokeRunner.RunAsync();
        Log.Information("LLM gateway failure smoke requested via --llm-gateway-failure-smoke. Exiting after successful verification.");
        return;
    }

    if (runLlmGatewayAnalyticsSmoke)
    {
        await LlmGatewayAnalyticsSmokeRunner.RunAsync();
        Log.Information("LLM gateway analytics smoke requested via --llm-gateway-analytics-smoke. Exiting after successful verification.");
        return;
    }

    if (runLlmGatewayAnalyticsValidate)
    {
        var report = await LlmGatewayAnalyticsValidationRunner.RunAsync(llmGatewayAnalyticsValidateOutput);
        Log.Information(
            "LLM gateway analytics validation requested via --llm-gateway-analytics-validate. output={OutputPath}, checks_passed={ChecksPassed}, recommendation={Recommendation}. Exiting after successful verification.",
            report.OutputPath,
            report.AllChecksPassed,
            report.Recommendation);
        return;
    }

    if (runLlmGatewayTransportValidate)
    {
        var report = await LlmGatewayTransportValidationRunner.RunAsync(llmGatewayTransportValidateOutput);
        Log.Information(
            "LLM gateway transport validation requested via --llm-gateway-transport-validate. output={OutputPath}, checks_passed={ChecksPassed}, recommendation={Recommendation}. Exiting after successful verification.",
            report.OutputPath,
            report.AllChecksPassed,
            report.Recommendation);
        return;
    }

    if (runLlmGatewayExperimentSmoke)
    {
        await LlmGatewayExperimentSmokeRunner.RunAsync();
        Log.Information("LLM gateway experiment smoke requested via --llm-gateway-experiment-smoke. Exiting after successful verification.");
        return;
    }

    if (runLlmContractNormalizationSmoke)
    {
        await LlmContractNormalizationSmokeRunner.RunAsync();
        Log.Information("LLM contract normalization smoke requested via --llm-contract-normalization-smoke. Exiting after successful verification.");
        return;
    }

    if (runLlmContractFamilyValidate)
    {
        var report = await LlmContractFamilyValidationRunner.RunAsync(llmContractFamilyValidateOutput);
        Log.Information(
            "LLM contract family validation requested via --llm-contract-family-validate. output={OutputPath}, family={Family}, checks_passed={ChecksPassed}, recommendation={Recommendation}. Exiting after successful verification.",
            report.OutputPath,
            report.ContractFamily,
            report.AllChecksPassed,
            report.Recommendation);
        return;
    }

    if (runLlmGatewayReplayAb)
    {
        var report = await LlmGatewayReplayAbRunner.RunAsync(llmGatewayReplayAbOutput);
        var baselineSummary = report.BranchSummaries.First(summary => string.Equals(summary.Branch, "baseline", StringComparison.Ordinal));
        var candidateSummary = report.BranchSummaries.First(summary => string.Equals(summary.Branch, "candidate", StringComparison.Ordinal));
        Log.Information(
            "LLM gateway replay A/B requested via --llm-gateway-replay-ab. output={OutputPath}, baseline_error_rate={BaselineErrorRate:0.0000}, candidate_error_rate={CandidateErrorRate:0.0000}, parity_rate={ParityRate:0.0000}. Exiting after successful verification.",
            report.OutputPath,
            baselineSummary.ErrorRate,
            candidateSummary.ErrorRate,
            report.Comparison.ParityRate);
        return;
    }

    if (runEditDiffPilotSmoke)
    {
        // This smoke needs the real composition root because it verifies the runtime pilot toggle.
    }

    if (runEditDiffPilotValidate)
    {
        // This validation needs the real composition root because it verifies replay parity and gateway governance on the runtime pilot path.
    }

    if (runStage6BootstrapSmoke)
    {
        await Stage6BootstrapSmokeRunner.RunAsync();
        Log.Information("Stage6 bootstrap smoke requested via --stage6-bootstrap-smoke. Exiting after successful verification.");
        return;
    }

    if (runStage7DossierProfileSmoke)
    {
        await Stage7DossierProfileSmokeRunner.RunAsync();
        Log.Information("Stage7 dossier/profile smoke requested via --stage7-dossier-profile-smoke. Exiting after successful verification.");
        return;
    }

    if (runStage7PairDynamicsSmoke)
    {
        await Stage7PairDynamicsSmokeRunner.RunAsync();
        Log.Information("Stage7 pair-dynamics smoke requested via --stage7-pair-dynamics-smoke. Exiting after successful verification.");
        return;
    }

    if (runStage7TimelineSmoke)
    {
        await Stage7TimelineSmokeRunner.RunAsync();
        Log.Information("Stage7 timeline smoke requested via --stage7-timeline-smoke. Exiting after successful verification.");
        return;
    }

    if (runResolutionRecomputeContractSmoke)
    {
        await ResolutionRecomputeContractSmokeRunner.RunAsync();
        Log.Information("Resolution recompute contract smoke requested via --resolution-recompute-contract-smoke. Exiting after successful verification.");
        return;
    }

    if (runStage8RelatedConflictSmoke)
    {
        Stage8RelatedConflictSmokeRunner.Run();
        Log.Information("Stage8 related conflict smoke requested via --stage8-related-conflict-smoke. Exiting after successful verification.");
        return;
    }

    if (runStage8RecomputeSmoke)
    {
        await Stage8RecomputeQueueSmokeRunner.RunAsync();
        Log.Information("Stage8 recompute smoke requested via --stage8-recompute-smoke. Exiting after successful verification.");
        return;
    }

    if (runOpint006B1Smoke)
    {
        Opint006AssistantResponseSmokeRunner.Run();
        Log.Information("OPINT-006-B1 smoke requested via --opint-006-b1-smoke. Exiting after successful verification.");
        return;
    }

    if (runOpint006B2Smoke)
    {
        await Opint006AssistantContextAssemblySmokeRunner.RunAsync();
        Log.Information("OPINT-006-B2 smoke requested via --opint-006-b2-smoke. Exiting after successful verification.");
        return;
    }

    if (runOpint006CSmoke)
    {
        await Opint006TelegramAssistantModeSmokeRunner.RunAsync();
        Log.Information("OPINT-006-C smoke requested via --opint-006-c-smoke. Exiting after successful verification.");
        return;
    }

    if (runOpint009ASmoke)
    {
        var report = await Opint009AlertPolicySmokeRunner.RunAsync(new OperatorAlertPolicyService(), opint009ASmokeOutput, CancellationToken.None);
        Log.Information(
            "OPINT-009-A smoke requested via --opint-009-a-smoke. output={OutputPath}, passed={Passed}, telegram_push_rule_count={TelegramPushRuleCount}, suppressed_rule_count={SuppressedRuleCount}. Exiting after successful verification.",
            report.OutputPath,
            report.AllChecksPassed,
            report.RuleDefinitions.Count(x => string.Equals(x.EscalationBoundary, OperatorAlertEscalationBoundaries.TelegramPushAcknowledge, StringComparison.Ordinal)),
            report.RuleDefinitions.Count(x => string.Equals(x.EscalationBoundary, OperatorAlertEscalationBoundaries.Suppressed, StringComparison.Ordinal)));
        return;
    }

    if (runOpint009BSmoke)
    {
        var report = await Opint009TelegramAlertsSmokeRunner.RunAsync(opint009BSmokeOutput, CancellationToken.None);
        Log.Information(
            "OPINT-009-B smoke requested via --opint-009-b-smoke. output={OutputPath}, passed={Passed}, tracked_person_id={TrackedPersonId}, scope_item_key={ScopeItemKey}. Exiting after successful verification.",
            report.OutputPath,
            report.AllChecksPassed,
            report.ActiveTrackedPersonId,
            report.CriticalAlertScopeItemKey);
        return;
    }

    if (runOpint009C1Smoke)
    {
        var report = await Opint009WebAlertsSmokeRunner.RunAsync(opint009C1SmokeOutput, CancellationToken.None);
        Log.Information(
            "OPINT-009-C1 smoke requested via --opint-009-c1-smoke. output={OutputPath}, passed={Passed}, group_count={GroupCount}, total_alerts={TotalAlerts}. Exiting after successful verification.",
            report.OutputPath,
            report.AllChecksPassed,
            report.GroupCount,
            report.TotalAlerts);
        return;
    }

    if (runOpint009C2Smoke)
    {
        var report = await Opint009WebAlertsWidgetsSmokeRunner.RunAsync(opint009C2SmokeOutput, CancellationToken.None);
        Log.Information(
            "OPINT-009-C2 smoke requested via --opint-009-c2-smoke. output={OutputPath}, passed={Passed}, active_scope_ack_count={ActiveScopeAcknowledgementCount}, bounded_facet_url_count={BoundedFacetUrlCount}. Exiting after successful verification.",
            report.OutputPath,
            report.AllChecksPassed,
            report.ActiveScopeAcknowledgementCount,
            report.BoundedFacetUrlCount);
        return;
    }

    if (runOpint009DSmoke)
    {
        var report = await Opint009AlertsValidationSmokeRunner.RunAsync(opint009DSmokeOutput, CancellationToken.None);
        Log.Information(
            "OPINT-009-D smoke requested via --opint-009-d-smoke. output={OutputPath}, passed={Passed}, deep_link_validated={DeepLinkRecoveryValidated}, acknowledgement_validated={AcknowledgementValidated}. Exiting after successful verification.",
            report.OutputPath,
            report.AllChecksPassed,
            report.DeepLinkRecoveryValidated,
            report.AcknowledgementValidated);
        return;
    }

    if (runOpint012ASmoke)
    {
        var report = await Opint012ATelegramResolutionParitySmokeRunner.RunAsync(opint012ASmokeOutput, CancellationToken.None);
        Log.Information(
            "OPINT-012-A smoke requested via --opint-012-a-smoke. output={OutputPath}, passed={Passed}, tracked_person_id={TrackedPersonId}, scope_item_key={ScopeItemKey}. Exiting after successful verification.",
            report.OutputPath,
            report.AllChecksPassed,
            report.TrackedPersonId,
            report.ScopeItemKey);
        return;
    }

    if (runOpint012BSmoke)
    {
        var report = await Opint012BWebResolutionParitySmokeRunner.RunAsync(opint012BSmokeOutput, CancellationToken.None);
        Log.Information(
            "OPINT-012-B smoke requested via --opint-012-b-smoke. output={OutputPath}, passed={Passed}, render_contract={RenderContract}, null_trust_omits_percent={NullTrustOmitsPercent}. Exiting after successful verification.",
            report.OutputPath,
            report.AllChecksPassed,
            report.RendererUsesDisplayLabelTrustPercent,
            report.NullTrustOmitsPercent);
        return;
    }

    if (runOpintHomeDashboardSmoke)
    {
        var report = await OpintHomeDashboardSmokeRunner.RunAsync(opintHomeDashboardSmokeOutput, CancellationToken.None);
        Log.Information(
            "OPINT home/dashboard smoke requested via --opint-home-dashboard-smoke. output={OutputPath}, passed={Passed}, api_shape={ApiShape}, degraded_sources_order={DegradedSourcesOrder}, target_url_allow_list={TargetUrlAllowList}. Exiting after successful verification.",
            report.OutputPath,
            report.AllChecksPassed,
            report.ApiShapeValidated,
            report.DegradedSourcesOrderValidated,
            report.TargetUrlAllowListValidated);
        return;
    }

    if (runResolutionInterpretationLoopValidate)
    {
        var report = await ResolutionInterpretationLoopValidationRunner.RunAsync(
            new ServiceCollection().BuildServiceProvider(),
            resolutionInterpretationLoopValidateOutput,
            CancellationToken.None);
        Log.Information(
            "Resolution interpretation loop V1 validation requested via --resolution-interpretation-loop-v1-validate. output={OutputPath}, passed={Passed}, tracked_person_id={TrackedPersonId}, scope_item_key={ScopeItemKey}, requested_context_type={RequestedContextType}, audit_entries={AuditEntryCount}, used_fallback={UsedFallback}. Exiting after successful verification.",
            report.OutputPath,
            report.Passed,
            report.TrackedPersonId,
            report.ScopeItemKey,
            report.RequestedContextType,
            report.AuditTrail.Count,
            report.UsedFallback);
        return;
    }

    if (runStageSemanticContractProof)
    {
        var report = await StageSemanticContractProofRunner.RunAsync(stageSemanticContractProofOutput, CancellationToken.None);
        Log.Information(
            "Stage semantic contract proof requested via --stage-semantic-contract-proof. output={OutputPath}, passed={Passed}, case_count={CaseCount}. Exiting after successful verification.",
            report.OutputPath,
            report.Passed,
            report.Cases.Count);
        return;
    }

    var runtimeRoleSelection = new RuntimeRoleSelection(RuntimeWorkloadRole.None, "unresolved", string.Empty);

    var builder = Host.CreateDefaultBuilder(args)
        .UseSerilog()
        .ConfigureServices((context, services) =>
        {
            var config = context.Configuration;
            runtimeRoleSelection = RuntimeRoleParser.Parse(args, config);
            RuntimeStartupGuard.Validate(config, runtimeRoleSelection);
            services.AddTelegramAssistantCompositionRoot(
                config,
                runtimeRoleSelection,
                includeLegacyStage6Diagnostics,
                includeLegacyStage6ClusterDiagnostics);

            Log.Information(
                "Runtime role selection resolved: roles={Roles}, source={Source}, raw={RawValue}",
                runtimeRoleSelection.Roles,
                runtimeRoleSelection.Source,
                runtimeRoleSelection.RawValue);
        })
        .ConfigureWebHostDefaults(webBuilder =>
        {
            webBuilder.ConfigureServices(services =>
            {
                services.AddRouting();
            });

            webBuilder.Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapOperatorWebShell();
                    endpoints.MapOperatorApi();
                });
            });
        });

    var host = builder.Build();

    using (var scope = host.Services.CreateScope())
    {
        if (runRuntimeControlDetailProof)
        {
            var report = await RuntimeControlDetailBoundedProofRunner.RunAsync(
                host.Services,
                runtimeControlDetailProofOutput,
                CancellationToken.None);
            Log.Information(
                "Runtime-control detail proof requested via --runtime-control-detail-proof. output={OutputPath}, passed={Passed}, tracked_person_id={TrackedPersonId}, active_runtime_control_state={ActiveRuntimeControlState}, review_only_claims_empty={ReviewOnlyClaimsEmpty}, promotion_blocked_live_item_present={PromotionBlockedLiveItemPresent}. Exiting after successful verification.",
                report.OutputPath,
                report.Passed,
                report.TrackedPersonId,
                report.ActiveRuntimeControlState,
                report.ReviewOnly.ClaimsAndEvidenceEmpty,
                report.PromotionBlocked.LiveItemPresent);
            return;
        }

        if (runTemporalPersonStateProof)
        {
            var report = await TemporalPersonStateProofRunner.RunAsync(
                host.Services,
                temporalPersonStateProofOutput,
                CancellationToken.None);
            Log.Information(
                "Temporal person-state proof requested via --temporal-person-state-proof. output={OutputPath}, passed={Passed}, row_count={RowCount}, scope_key={ScopeKey}. Exiting after successful verification.",
                report.OutputPath,
                report.Passed,
                report.Rows.Count,
                report.ScopeKey);
            return;
        }

        if (runPersonHistoryProof)
        {
            var report = await PersonHistoryProofRunner.RunAsync(
                host.Services,
                personHistoryProofOutput,
                CancellationToken.None);
            Log.Information(
                "Person-history proof requested via --person-history-proof. output={OutputPath}, passed={Passed}, row_count={RowCount}, scope_key={ScopeKey}. Exiting after successful verification.",
                report.OutputPath,
                report.Passed,
                report.Cases.Count,
                report.ScopeKey);
            return;
        }

        if (runIterativeReintegrationProof)
        {
            var report = await IterativeReintegrationProofRunner.RunAsync(
                host.Services,
                iterativeReintegrationProofOutput,
                CancellationToken.None);
            Log.Information(
                "Iterative reintegration proof requested via --iterative-reintegration-proof. output={OutputPath}, passed={Passed}, row_count={RowCount}, scope_key={ScopeKey}. Exiting after successful verification.",
                report.OutputPath,
                report.Passed,
                report.Rows.Count,
                report.ScopeKey);
            return;
        }

        if (runAiConflictSessionV1Proof)
        {
            var report = await AiConflictResolutionSessionV1ProofRunner.RunAsync(
                host.Services,
                aiConflictSessionV1ProofOutput,
                CancellationToken.None);
            Log.Information(
                "AI conflict session V1 proof requested via --ai-conflict-session-v1-proof. output={OutputPath}, passed={Passed}, scope_item_key={ScopeItemKey}, start_state={StartState}, final_state={FinalState}, action_applied={ApplyAccepted}, deterministic_apply_path_confirmed={DeterministicApplyPathConfirmed}. Exiting after successful verification.",
                report.OutputPath,
                report.Passed,
                report.ScopeItemKey,
                report.StartState,
                report.FinalState,
                report.ApplyAccepted,
                report.DeterministicApplyPathConfirmed);
            return;
        }

        if (runLivenessCheck)
        {
            await RuntimeHealthProbeRunner.RunLivenessCheckAsync(runtimeRoleSelection, CancellationToken.None);
            Log.Information("Liveness check passed: process viability is healthy.");
            return;
        }

        if (runReadinessCheck || runHealthCheck)
        {
            await RuntimeHealthProbeRunner.RunReadinessCheckAsync(scope.ServiceProvider, runtimeRoleSelection, CancellationToken.None);
            Log.Information(
                "Readiness check passed: dependency and role-contract admission checks are healthy. alias_used={HealthAliasUsed}",
                runHealthCheck);
            return;
        }

        if (runSeedBootstrapScope)
        {
            if (!runtimeRoleSelection.Has(RuntimeWorkloadRole.Ops))
            {
                throw new InvalidOperationException("--seed-bootstrap-scope is operator-only and requires runtime role including 'ops'.");
            }

            if (runOperatorSchemaInit)
            {
                throw new InvalidOperationException("--seed-bootstrap-scope cannot be combined with --operator-schema-init.");
            }

            var seedCommand = scope.ServiceProvider.GetRequiredService<BootstrapScopeSeedCommand>();
            var seedResult = await seedCommand.RunAsync(bootstrapSeedRequest!, CancellationToken.None);
            var report = BootstrapScopeSeedReportFormatter.Format(seedResult);
            Log.Information(
                "Bootstrap scope seed command completed via --seed-bootstrap-scope. mode={Mode}, contract_status={ContractStatus}, bootstrap_ready={BootstrapReady}, scope_key={ScopeKey}, operator_person_id={OperatorPersonId}, tracked_person_id={TrackedPersonId}",
                seedResult.DryRun ? "dry-run" : "apply",
                seedResult.ContractStatus,
                seedResult.BootstrapReady,
                seedResult.ScopeKey,
                seedResult.OperatorPersonId,
                seedResult.TrackedPersonId);
            Console.WriteLine(report);
            return;
        }

        if (runOperatorSchemaInit)
        {
            var dbInit = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();
            await dbInit.InitializeAsync();
            Log.Information("Operator-requested schema initialization completed via --operator-schema-init.");
        }
        else
        {
            Log.Information(
                "Default startup path skips schema-changing initialization. Use --operator-schema-init for explicit operator-only schema migration mode.");
        }

        var redisQueue = scope.ServiceProvider.GetRequiredService<RedisMessageQueue>();
        await redisQueue.InitializeAsync();

        if (runFoundationSmoke)
        {
            var verificationService = scope.ServiceProvider.GetRequiredService<FoundationDomainVerificationService>();
            await verificationService.RunAsync();
            Log.Information("Foundation verification run requested via --foundation-smoke. Exiting after successful verification.");
            return;
        }

        if (runClarificationSmoke)
        {
            var verificationService = scope.ServiceProvider.GetRequiredService<ClarificationOrchestrationVerificationService>();
            await verificationService.RunAsync();
            Log.Information("Legacy Stage6 clarification diagnostic-only run requested via --clarification-smoke. Exiting after successful verification.");
            return;
        }

        if (runPeriodizationSmoke)
        {
            var verificationService = scope.ServiceProvider.GetRequiredService<PeriodizationVerificationService>();
            await verificationService.RunAsync();
            Log.Information("Legacy Stage6 periodization diagnostic-only run requested via --periodization-smoke. Exiting after successful verification.");
            return;
        }

        if (runStateSmoke)
        {
            var verificationService = scope.ServiceProvider.GetRequiredService<StateEngineVerificationService>();
            await verificationService.RunAsync();
            Log.Information("Legacy Stage6 state diagnostic-only run requested via --state-smoke. Exiting after successful verification.");
            return;
        }

        if (runProfileSmoke)
        {
            var verificationService = scope.ServiceProvider.GetRequiredService<ProfileEngineVerificationService>();
            await verificationService.RunAsync();
            Log.Information("Legacy Stage6 profile diagnostic-only run requested via --profile-smoke. Exiting after successful verification.");
            return;
        }

        if (runStrategySmoke)
        {
            var verificationService = scope.ServiceProvider.GetRequiredService<StrategyEngineVerificationService>();
            await verificationService.RunAsync();
            Log.Information("Legacy Stage6 strategy diagnostic-only run requested via --strategy-smoke. Exiting after successful verification.");
            return;
        }

        if (runDraftSmoke)
        {
            var verificationService = scope.ServiceProvider.GetRequiredService<DraftEngineVerificationService>();
            await verificationService.RunAsync();
            Log.Information("Legacy Stage6 draft diagnostic-only run requested via --draft-smoke. Exiting after successful verification.");
            return;
        }

        if (runReviewSmoke)
        {
            var verificationService = scope.ServiceProvider.GetRequiredService<DraftReviewVerificationService>();
            await verificationService.RunAsync();
            Log.Information("Legacy Stage6 review diagnostic-only run requested via --review-smoke. Exiting after successful verification.");
            return;
        }

        if (runOutcomeSmoke)
        {
            var verificationService = scope.ServiceProvider.GetRequiredService<OutcomeVerificationService>();
            await verificationService.RunAsync();
            Log.Information("Legacy Stage6 outcome diagnostic-only run requested via --outcome-smoke. Exiting after successful verification.");
            return;
        }

        if (runStage5Smoke)
        {
            var verificationService = scope.ServiceProvider.GetRequiredService<Stage5VerificationService>();
            await verificationService.RunAsync();
            var substrateVerificationService = scope.ServiceProvider.GetRequiredService<Stage5SubstrateDeterminismVerificationService>();
            await substrateVerificationService.RunAsync();
            Log.Information("Stage5 verification run requested via --stage5-smoke. Exiting after successful verification.");
            return;
        }

        if (runEditDiffPilotSmoke)
        {
            var result = await EditDiffPilotSmokeRunner.RunAsync(scope.ServiceProvider, CancellationToken.None);
            Log.Information(
                "Edit-diff pilot smoke requested via --edit-diff-pilot-smoke. legacy_provider={LegacyProvider}, legacy_model={LegacyModel}, gateway_provider={GatewayProvider}, gateway_model={GatewayModel}, gateway_fallback_applied={GatewayFallbackApplied}. Exiting after successful verification.",
                result.Legacy.Provider,
                result.Legacy.Model,
                result.Gateway.Provider,
                result.Gateway.Model,
                result.Gateway.FallbackApplied);
            return;
        }

        if (runEditDiffPilotValidate)
        {
            var report = await EditDiffPilotValidationRunner.RunAsync(scope.ServiceProvider, editDiffPilotValidateOutput, CancellationToken.None);
            Log.Information(
                "Edit-diff pilot validation requested via --edit-diff-pilot-validate. output={OutputPath}, parity_rate={ParityRate:0.0000}, gateway_fallback_rate={FallbackRate:0.0000}, budget_semantics_compatible={BudgetCompatibility}, recommendation={Recommendation}. Exiting after successful verification.",
                report.OutputPath,
                report.Comparison.ParityRate,
                report.GatewaySummary.FallbackRate,
                report.BudgetCompatibility.QuotaRegistrationCompatible,
                report.RolloutRecommendation);
            return;
        }

        if (runOpint003Validate)
        {
            var report = await Opint003LoopValidationRunner.RunAsync(scope.ServiceProvider, opint003ValidateOutput, CancellationToken.None);
            Log.Information(
                "OPINT-003-D validation requested via --opint-003-d-validate. output={OutputPath}, passed={Passed}, scenarios={ScenarioPassCount}/{ScenarioTotal}, related_conflicts_created={CreatedCount}, related_conflicts_resolved={ResolvedCount}. Exiting after successful verification.",
                report.OutputPath,
                report.AllChecksPassed,
                report.KeyMetrics.ScenariosPassed,
                report.KeyMetrics.ScenariosTotal,
                report.KeyMetrics.RelatedConflictCreated,
                report.KeyMetrics.RelatedConflictResolved);
            return;
        }

        if (runOpint004Smoke)
        {
            var report = await Opint004TelegramModeSmokeRunner.RunAsync(scope.ServiceProvider, opint004SmokeOutput, CancellationToken.None);
            Log.Information(
                "OPINT-004-A smoke requested via --opint-004-a-smoke. output={OutputPath}, passed={Passed}, tracked_person_switches={TrackedPersonSwitchCount}, unauthorized_denied={UnauthorizedDeniedCount}. Exiting after successful verification.",
                report.OutputPath,
                report.AllChecksPassed,
                report.AuditChecks.AcceptedTrackedPersonSwitchCount,
                report.AuditChecks.UnauthorizedDeniedCount);
            return;
        }

        if (runOpint007B1Smoke)
        {
            var report = await Opint007OfflineEventCaptureSmokeRunner.RunAsync(scope.ServiceProvider, opint007B1SmokeOutput, CancellationToken.None);
            Log.Information(
                "OPINT-007-B1 smoke requested via --opint-007-b1-smoke. output={OutputPath}, passed={Passed}, offline_event_id={OfflineEventId}, tracked_person_id={TrackedPersonId}. Exiting after successful verification.",
                report.OutputPath,
                report.AllChecksPassed,
                report.SavedOfflineEventId,
                report.SessionSnapshot.ActiveTrackedPersonId);
            return;
        }

        if (runOpint007B2Smoke)
        {
            var policy = scope.ServiceProvider.GetRequiredService<OfflineEventClarificationPolicy>();
            var report = await Opint007OfflineEventClarificationPolicySmokeRunner.RunAsync(policy, opint007B2SmokeOutput, CancellationToken.None);
            Log.Information(
                "OPINT-007-B2 smoke requested via --opint-007-b2-smoke. output={OutputPath}, passed={Passed}, top_question_key={TopQuestionKey}. Exiting after successful verification.",
                report.OutputPath,
                report.AllChecksPassed,
                report.RankingTopQuestionKey);
            return;
        }

        if (runOpint007B3Smoke)
        {
            var report = await Opint007OfflineEventClarificationOrchestrationSmokeRunner.RunAsync(scope.ServiceProvider, opint007B3SmokeOutput, CancellationToken.None);
            Log.Information(
                "OPINT-007-B3 smoke requested via --opint-007-b3-smoke. output={OutputPath}, passed={Passed}, offline_event_id={OfflineEventId}, confidence={Confidence}. Exiting after successful verification.",
                report.OutputPath,
                report.AllChecksPassed,
                report.SavedOfflineEventId,
                report.StoredEvent?.Confidence);
            return;
        }

        if (runBudgetSmoke)
        {
            var verificationService = scope.ServiceProvider.GetRequiredService<BudgetVerificationService>();
            await verificationService.RunAsync();
            Log.Information("Legacy Stage6 budget diagnostic-only run requested via --budget-smoke. Exiting after successful verification.");
            return;
        }

        if (runEvalSmoke)
        {
            var verificationService = scope.ServiceProvider.GetRequiredService<EvalVerificationService>();
            await verificationService.RunAsync();
            Log.Information("Legacy Stage6 eval diagnostic-only run requested via --eval-smoke. Exiting after successful verification.");
            return;
        }

        if (runLaunchSmoke)
        {
            var hostedServices = scope.ServiceProvider.GetServices<IHostedService>().Select(x => x.GetType().Name).OrderBy(x => x).ToList();
            Log.Information("Runtime wiring check (launch-smoke) passed. Hosted services resolved: {Count}", hostedServices.Count);

            var verificationService = scope.ServiceProvider.GetRequiredService<LaunchReadinessVerificationService>();
            await verificationService.RunAsync();
            Log.Information("Launch verification run requested via --launch-smoke. Exiting after successful verification.");
            return;
        }

        if (runExternalArchiveSmoke)
        {
            var verificationService = scope.ServiceProvider.GetRequiredService<ExternalArchiveVerificationService>();
            await verificationService.RunAsync();
            Log.Information("External archive verification run requested via --external-archive-smoke. Exiting after successful verification.");
            return;
        }

        if (runCompetingContextSmoke)
        {
            var verificationService = scope.ServiceProvider.GetRequiredService<CompetingContextVerificationService>();
            await verificationService.RunAsync();
            Log.Information("Legacy Stage6 competing-context diagnostic-only run requested via --competing-context-smoke. Exiting after successful verification.");
            return;
        }

        if (runLegacyBotSmoke)
        {
            var verificationService = scope.ServiceProvider.GetRequiredService<BotCommandVerificationService>();
            await verificationService.RunAsync();
            Log.Information("Legacy Stage6 bot diagnostic-only run requested via --legacy-bot-smoke. Exiting after successful verification.");
            return;
        }

        if (runLegacyAutoCaseSmoke)
        {
            var verificationService = scope.ServiceProvider.GetRequiredService<Stage6AutoCaseGenerationVerificationService>();
            await verificationService.RunAsync();
            Log.Information("Legacy Stage6 auto-case diagnostic-only run requested via --legacy-auto-case-smoke. Exiting after successful verification.");
            return;
        }

        if (runLegacyNetworkSmoke)
        {
            var service = scope.ServiceProvider.GetRequiredService<INetworkGraphService>();
            var smokeScope = CaseScopeFactory.CreateSmokeScope("legacy_network");
            var result = await service.BuildAsync(new NetworkBuildRequest
            {
                CaseId = smokeScope.CaseId,
                ChatId = smokeScope.ChatId,
                MessageLimit = 240,
                AsOfUtc = DateTime.UtcNow
            }, CancellationToken.None);
            Log.Information(
                "Legacy Stage6 network diagnostic-only run requested via --legacy-network-smoke. nodes={Nodes}, influence_edges={InfluenceEdges}, information_flows={InformationFlows}. Exiting after successful verification.",
                result.Nodes.Count,
                result.InfluenceEdges.Count,
                result.InformationFlows.Count);
            return;
        }

        if (runStage5ScopedRepair)
        {
            if (stage5ScopedRepairChatId <= 0)
            {
                throw new InvalidOperationException("Invalid --stage5-scoped-repair-chat-id value.");
            }

            BackupMetadataEvidence? backupEvidence = null;
            if (riskBackupIdArg is not null
                || riskBackupCreatedAtArg is not null
                || riskBackupScopeArg is not null
                || riskBackupArtifactArg is not null
                || riskBackupChecksumArg is not null)
            {
                if (riskBackupCreatedAtArg is null)
                {
                    throw new InvalidOperationException("Missing --risk-backup-created-at-utc for provided backup metadata.");
                }

                var backupCreatedRaw = riskBackupCreatedAtArg["--risk-backup-created-at-utc=".Length..];
                if (!DateTime.TryParse(
                        backupCreatedRaw,
                        null,
                        System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
                        out var parsedBackupCreatedAtUtc))
                {
                    throw new InvalidOperationException("Invalid --risk-backup-created-at-utc value. Expected ISO-8601 UTC.");
                }

                backupEvidence = new BackupMetadataEvidence
                {
                    BackupId = riskBackupIdArg is null ? string.Empty : riskBackupIdArg["--risk-backup-id=".Length..],
                    CreatedAtUtc = parsedBackupCreatedAtUtc,
                    Scope = riskBackupScopeArg is null ? string.Empty : riskBackupScopeArg["--risk-backup-scope=".Length..],
                    ArtifactUri = riskBackupArtifactArg is null ? string.Empty : riskBackupArtifactArg["--risk-backup-artifact-uri=".Length..],
                    Checksum = riskBackupChecksumArg is null ? string.Empty : riskBackupChecksumArg["--risk-backup-checksum=".Length..]
                };
            }

            var runOptions = new Stage5ScopedRepairExecutionOptions
            {
                BackupEvidence = backupEvidence,
                OperatorIdentity = riskOperatorArg is null ? string.Empty : riskOperatorArg["--risk-operator=".Length..],
                OperatorReason = riskReasonArg is null ? "stage5_scoped_repair_apply" : riskReasonArg["--risk-reason=".Length..],
                AuditId = riskAuditIdArg is null ? string.Empty : riskAuditIdArg["--risk-audit-id=".Length..],
                Override = new RiskOperationOverride
                {
                    OperatorIdentity = riskOverrideOperatorArg is null ? string.Empty : riskOverrideOperatorArg["--risk-override-operator=".Length..],
                    Reason = riskOverrideReasonArg is null ? string.Empty : riskOverrideReasonArg["--risk-override-reason=".Length..],
                    ApprovalToken = riskOverrideApprovalTokenArg is null ? string.Empty : riskOverrideApprovalTokenArg["--risk-override-approval-token=".Length..],
                    AuditId = riskOverrideAuditIdArg is null ? string.Empty : riskOverrideAuditIdArg["--risk-override-audit-id=".Length..]
                }
            };

            var repairCommand = scope.ServiceProvider.GetRequiredService<Stage5ScopedRepairCommand>();
            var result = await repairCommand.RunAsync(
                stage5ScopedRepairChatId,
                runStage5ScopedRepairApply,
                stage5ScopedRepairAuditDir,
                runOptions,
                CancellationToken.None);
            Log.Information(
                "Stage5 scoped repair command completed: mode={Mode}, chat_id={ChatId}, trusted_restore={TrustedRestore}, dual_migrations={DualMigrations}, orphan_placeholders={OrphanPlaceholders}, audit={AuditPath}",
                runStage5ScopedRepairApply ? "apply" : "dry-run",
                result.Summary.ChatId,
                result.Summary.Plan.TrustedSessionIndexes.Count,
                result.Summary.Plan.DualSourceMigrations.Count,
                result.Summary.Plan.OrphanPlaceholderMessageIds.Count,
                result.AuditPath);
            return;
        }

        if (!string.IsNullOrWhiteSpace(externalArchiveImportFile))
        {
            if (!File.Exists(externalArchiveImportFile))
            {
                throw new FileNotFoundException($"External archive import file was not found: {externalArchiveImportFile}");
            }

            var json = await File.ReadAllTextAsync(externalArchiveImportFile);
            var request = JsonSerializer.Deserialize<ExternalArchiveImportRequest>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ?? throw new InvalidOperationException("External archive import payload could not be deserialized.");
            if (string.IsNullOrWhiteSpace(request.Actor))
            {
                request.Actor = externalArchiveActor;
            }

            var ingestionService = scope.ServiceProvider.GetRequiredService<IExternalArchiveIngestionService>();
            var result = await ingestionService.IngestAsync(request);
            Log.Information(
                "External archive import completed via command mode. run_id={RunId}, case_id={CaseId}, is_replay={Replay}, persisted_records={Records}, persisted_linkages={Linkages}, rejected={Rejected}",
                result.Batch.RunId,
                result.Batch.CaseId,
                result.IsReplay,
                result.PersistedRecordCount,
                result.PersistedLinkageCount,
                result.Batch.RejectedCount);
            return;
        }

        if (runRuntimeWiringCheck)
        {
            var hostedServices = scope.ServiceProvider.GetServices<IHostedService>().Select(x => x.GetType().Name).OrderBy(x => x).ToList();
            Log.Information("Runtime wiring check passed. Hosted services resolved: {Count}", hostedServices.Count);
            foreach (var serviceName in hostedServices)
            {
                Log.Information("Hosted service registered: {ServiceName}", serviceName);
            }

            return;
        }
    }

    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
    Environment.ExitCode = 1;
}
finally
{
    Log.CloseAndFlush();
}
