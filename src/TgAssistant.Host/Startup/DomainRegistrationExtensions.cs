using Microsoft.Extensions.DependencyInjection;
using TgAssistant.Core.Interfaces;
using TgAssistant.Host.BootstrapSeed;
using TgAssistant.Host.Launch;
using TgAssistant.Host.OperatorApi;
using TgAssistant.Host.Stage5Repair;
using TgAssistant.Infrastructure.Database;
using TgAssistant.Infrastructure.LlmGateway;
using TgAssistant.Intelligence.Stage5;
using TgAssistant.Intelligence.Stage6Bootstrap;
using TgAssistant.Intelligence.Stage7Formation;
using TgAssistant.Intelligence.Stage8Recompute;
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
using TgAssistant.Telegram.Operator;

namespace TgAssistant.Host.Startup;

public static partial class ServiceRegistrationExtensions
{
    public static IServiceCollection AddTelegramAssistantDomainServices(
        this IServiceCollection services,
        bool includeLegacyStage6Diagnostics = false,
        bool includeLegacyStage6ClusterDiagnostics = false,
        bool includeCorrectionRepositoryServices = false)
    {
        services.AddActiveRepositoryServices();

        if (includeLegacyStage6Diagnostics || includeLegacyStage6ClusterDiagnostics)
        {
            services.AddStage6LegacyRepositoryServices();
        }

        if (includeCorrectionRepositoryServices)
        {
            services.AddCorrectionRepositoryServices();
        }

        services.AddSingleton<IChatCoordinationService, ChatCoordinationService>();
        services.AddSingleton<FoundationDomainVerificationService>();
        services.AddSingleton<IBudgetGuardrailService, BudgetGuardrailService>();
        services.AddSingleton<LaunchReadinessVerificationService>();
        services.AddSingleton<Stage5ScopedRepairCommand>();
        services.AddSingleton<BootstrapScopeSeedCommand>();
        services.AddSingleton<IExternalArchiveImportContractValidator, ExternalArchiveImportContractValidator>();
        services.AddSingleton<IExternalArchiveProvenanceWeightingService, ExternalArchiveProvenanceWeightingService>();
        services.AddSingleton<IExternalArchiveLinkagePlanner, ExternalArchiveLinkagePlanner>();
        services.AddSingleton<IExternalArchivePreparationService, ExternalArchivePreparationService>();
        services.AddSingleton<IExternalArchiveIngestionService, ExternalArchiveIngestionService>();
        services.AddSingleton<ExternalArchiveVerificationService>();
        services.AddSingleton<Stage5SubstrateDeterminismVerificationService>();
        services.AddSingleton<IModelPassAuditService, ModelPassAuditService>();
        services.AddSingleton<IStage6BootstrapService, Stage6BootstrapService>();
        services.AddSingleton<IStage7DossierProfileService, Stage7DossierProfileFormationService>();
        services.AddSingleton<IStage7PairDynamicsService, Stage7PairDynamicsFormationService>();
        services.AddSingleton<IStage7TimelineService, Stage7TimelineFormationService>();
        services.AddSingleton<IRuntimeControlStateService, RuntimeControlStateService>();
        services.AddSingleton<IStage8RecomputeQueueService, Stage8RecomputeQueueService>();
        services.AddSingleton<IStage8RecomputeTriggerService, Stage8RecomputeTriggerService>();
        services.AddSingleton<IResolutionReadService, ResolutionReadProjectionService>();
        services.AddSingleton<IResolutionActionService, ResolutionActionCommandService>();
        services.AddSingleton<IOperatorResolutionApplicationService, OperatorResolutionApplicationService>();
        services.AddSingleton<IOperatorOfflineEventRepository, OperatorOfflineEventRepository>();
        services.AddSingleton<IOperatorAssistantResponseGenerationService, OperatorAssistantResponseGenerationService>();
        services.AddSingleton<IOperatorAssistantContextAssemblyService, OperatorAssistantContextAssemblyService>();
        services.AddSingleton<IOperatorSessionAuditService, OperatorSessionAuditService>();
        services.AddSingleton<WebOperatorSessionStore>();
        services.AddSingleton<WebOperatorAuthSessionResolver>();
        services.AddSingleton<TelegramOperatorSessionStore>();
        services.AddSingleton<TelegramOperatorWorkflowService>();

        services.AddSingleton<ExtractionSchemaValidator>();
        services.AddSingleton<MessageContentBuilder>();
        services.AddSingleton<AnalysisContextBuilder>();
        services.AddSingleton<SummaryHistoricalRetrievalService>();
        services.AddSingleton<ILlmContractSchemaProvider, EditDiffContractSchemaProvider>();
        services.AddSingleton<ILlmContractValidator, EditDiffContractValidator>();
        services.AddSingleton<ILlmContractNormalizer, OpenRouterContractNormalizer>();
        services.AddSingleton<EditDiffTextCompletionService>();
        services.AddSingleton<Stage5VerificationService>();
        services.AddSingleton<ExtractionApplier>();
        services.AddSingleton<ExpensivePassResolver>();
        services.AddSingleton<TelegramDesktopArchiveParser>();

        if (includeLegacyStage6Diagnostics)
        {
            services.AddLegacyStage6DiagnosticServices();
        }

        if (includeLegacyStage6ClusterDiagnostics)
        {
            services.AddLegacyStage6ClusterDiagnosticServices();
        }

        return services;
    }

    private static IServiceCollection AddLegacyStage6DiagnosticServices(this IServiceCollection services)
    {
        // Retained only for explicit cleanup diagnostics; this is not the active Stage 6/7/8 implementation path.
        services.AddSingleton<IStage6ArtifactFreshnessService, Stage6ArtifactFreshnessService>();
        services.AddSingleton<IClarificationAnswerApplier, ClarificationAnswerApplier>();
        services.AddSingleton<IClarificationDependencyResolver, ClarificationDependencyResolver>();
        services.AddSingleton<IRecomputeTargetPlanner, RecomputeTargetPlanner>();
        services.AddSingleton<IClarificationOrchestrator, ClarificationOrchestrator>();
        services.AddSingleton<ClarificationOrchestrationVerificationService>();
        services.AddSingleton<IPeriodBoundaryDetector, PeriodBoundaryDetector>();
        services.AddSingleton<ITimelineAssembler, TimelineAssembler>();
        services.AddSingleton<ITransitionBuilder, TransitionBuilder>();
        services.AddSingleton<IPeriodEvidenceAssembler, PeriodEvidenceAssembler>();
        services.AddSingleton<IPeriodProposalService, PeriodProposalService>();
        services.AddSingleton<IPeriodizationService, PeriodizationService>();
        services.AddSingleton<PeriodizationVerificationService>();
        services.AddSingleton<IStateScoreCalculator, StateScoreCalculator>();
        services.AddSingleton<IStateConfidenceEvaluator, StateConfidenceEvaluator>();
        services.AddSingleton<IDynamicLabelMapper, DynamicLabelMapper>();
        services.AddSingleton<IRelationshipStatusMapper, RelationshipStatusMapper>();
        services.AddSingleton<ICurrentStateEngine, CurrentStateEngine>();
        services.AddSingleton<StateEngineVerificationService>();
        services.AddSingleton<IProfileTraitExtractor, ProfileTraitExtractor>();
        services.AddSingleton<IPairProfileSynthesizer, PairProfileSynthesizer>();
        services.AddSingleton<IProfileConfidenceEvaluator, ProfileConfidenceEvaluator>();
        services.AddSingleton<IPatternSynthesisService, PatternSynthesisService>();
        services.AddSingleton<IProfileEngine, ProfileEngine>();
        services.AddSingleton<ProfileEngineVerificationService>();
        services.AddSingleton<IStrategyOptionGenerator, StrategyOptionGenerator>();
        services.AddSingleton<IStrategyRiskEvaluator, StrategyRiskEvaluator>();
        services.AddSingleton<IStrategyRanker, StrategyRanker>();
        services.AddSingleton<IStrategyConfidenceEvaluator, StrategyConfidenceEvaluator>();
        services.AddSingleton<IMicroStepPlanner, MicroStepPlanner>();
        services.AddSingleton<IStrategyEngine, StrategyEngine>();
        services.AddSingleton<StrategyEngineVerificationService>();
        services.AddSingleton<IDraftGenerator, DraftGenerator>();
        services.AddSingleton<IDraftStyleAdapter, DraftStyleAdapter>();
        services.AddSingleton<IDraftStrategyChecker, DraftStrategyChecker>();
        services.AddSingleton<IDraftPackagingService, DraftPackagingService>();
        services.AddSingleton<IDraftEngine, DraftEngine>();
        services.AddSingleton<DraftEngineVerificationService>();
        services.AddSingleton<IDraftRiskAssessor, DraftRiskAssessor>();
        services.AddSingleton<IDraftStrategyFitChecker, DraftStrategyFitChecker>();
        services.AddSingleton<ISaferRewriteGenerator, SaferRewriteGenerator>();
        services.AddSingleton<INaturalRewriteGenerator, NaturalRewriteGenerator>();
        services.AddSingleton<IDraftReviewEngine, DraftReviewEngine>();
        services.AddSingleton<DraftReviewVerificationService>();
        services.AddSingleton<IDraftActionMatcher, DraftActionMatcher>();
        services.AddSingleton<IObservedOutcomeRecorder, ObservedOutcomeRecorder>();
        services.AddSingleton<ILearningSignalBuilder, LearningSignalBuilder>();
        services.AddSingleton<IOutcomeService, OutcomeService>();
        services.AddSingleton<OutcomeVerificationService>();
        services.AddSingleton<IEvalHarnessService, EvalHarnessService>();
        services.AddSingleton<BudgetVerificationService>();
        services.AddSingleton<EvalVerificationService>();
        services.AddSingleton<ICompetingContextInterpretationService, CompetingContextInterpretationService>();
        services.AddSingleton<ICompetingContextRuntimeService, CompetingContextRuntimeService>();
        services.AddSingleton<CompetingContextVerificationService>();
        return services;
    }

    private static IServiceCollection AddLegacyStage6ClusterDiagnosticServices(this IServiceCollection services)
    {
        // Legacy Stage6 bot/autocase/network cluster: available only behind explicit legacy diagnostic switches.
        services.AddSingleton<IBotCommandService, BotCommandService>();
        services.AddSingleton<IBotChatService, BotChatService>();
        services.AddSingleton<BotCommandVerificationService>();
        services.AddSingleton<Stage6AutoCaseGenerationService>();
        services.AddSingleton<Stage6AutoCaseGenerationVerificationService>();
        services.AddSingleton<INodeRoleResolver, NodeRoleResolver>();
        services.AddSingleton<IInfluenceEdgeBuilder, InfluenceEdgeBuilder>();
        services.AddSingleton<IInformationFlowBuilder, InformationFlowBuilder>();
        services.AddSingleton<INetworkScoringService, NetworkScoringService>();
        services.AddSingleton<INetworkGraphService, NetworkGraphService>();
        return services;
    }

    private static IServiceCollection AddActiveRepositoryServices(this IServiceCollection services)
    {
        // Active baseline repository surface: retained substrate, active operational stores, and
        // retained-with-refactor data stores still used by the default runtime composition.
        services.AddSingleton<IMessageRepository, MessageRepository>();
        services.AddSingleton<IRealtimeMessageSubstrateRepository, RealtimeMessageSubstrateRepository>();
        services.AddSingleton<IArchiveMessageSubstrateRepository, ArchiveMessageSubstrateRepository>();
        services.AddSingleton<IModelOutputNormalizer, ModelOutputNormalizer>();
        services.AddSingleton<IModelPassAuditStore, ModelPassAuditStore>();
        services.AddSingleton<IStage6BootstrapRepository, Stage6BootstrapRepository>();
        services.AddSingleton<IStage7DossierProfileRepository, Stage7DossierProfileRepository>();
        services.AddSingleton<IStage7PairDynamicsRepository, Stage7PairDynamicsRepository>();
        services.AddSingleton<IStage7TimelineRepository, Stage7TimelineRepository>();
        services.AddSingleton<IStage8RecomputeQueueRepository, Stage8RecomputeQueueRepository>();
        services.AddSingleton<IStage8OutcomeGateRepository, Stage8OutcomeGateRepository>();
        services.AddSingleton<IStage8RelatedConflictRepository, Stage8RelatedConflictRepository>();
        services.AddSingleton<IRuntimeDefectRepository, RuntimeDefectRepository>();
        services.AddSingleton<IClarificationBranchStateRepository, ClarificationBranchStateRepository>();
        services.AddSingleton<IRuntimeControlStateRepository, RuntimeControlStateRepository>();
        services.AddSingleton<IArchiveImportRepository, ArchiveImportRepository>();
        services.AddSingleton<IStickerCacheRepository, StickerCacheRepository>();
        services.AddSingleton<IPromptTemplateRepository, PromptTemplateRepository>();
        services.AddSingleton<IAnalysisStateRepository, AnalysisStateRepository>();
        services.AddSingleton<IMessageExtractionRepository, MessageExtractionRepository>();
        services.AddSingleton<IIntelligenceRepository, IntelligenceRepository>();
        services.AddSingleton<IExtractionErrorRepository, ExtractionErrorRepository>();
        services.AddSingleton<IEntityRepository, EntityRepository>();
        services.AddSingleton<IEntityAliasRepository, EntityAliasRepository>();
        services.AddSingleton<IEntityMergeRepository, EntityMergeRepository>();
        services.AddSingleton<IEntityMergeCommandRepository, EntityMergeCommandRepository>();
        services.AddSingleton<IStage5MetricsRepository, Stage5MetricsRepository>();
        services.AddSingleton<IAnalysisUsageRepository, AnalysisUsageRepository>();
        services.AddSingleton<ICommunicationEventRepository, CommunicationEventRepository>();
        services.AddSingleton<IEmbeddingRepository, EmbeddingRepository>();
        services.AddSingleton<IMaintenanceRepository, MaintenanceRepository>();
        services.AddSingleton<IFactReviewCommandRepository, FactReviewCommandRepository>();
        services.AddSingleton<IFactRepository, FactRepository>();
        services.AddSingleton<IRelationshipRepository, RelationshipRepository>();
        services.AddSingleton<IChatDialogSummaryRepository, ChatDialogSummaryRepository>();
        services.AddSingleton<IChatSessionRepository, ChatSessionRepository>();
        services.AddSingleton<IBudgetOpsRepository, BudgetOpsRepository>();
        services.AddSingleton<IExternalArchiveIngestionRepository, ExternalArchiveIngestionRepository>();
        return services;
    }

    private static IServiceCollection AddCorrectionRepositoryServices(this IServiceCollection services)
    {
        // Correction-store wiring is intentionally explicit-only. The reversible merge path remains
        // in code, but it is not part of the default runtime composition until an operator/API
        // entrypoint is promoted and bounded follow-up hardening is complete.
        services.AddSingleton<IIdentityMergeRepository, IdentityMergeRepository>();
        return services;
    }

    private static IServiceCollection AddStage6LegacyRepositoryServices(this IServiceCollection services)
    {
        // Frozen Stage6 repository surface: mapped for explicit legacy diagnostics only.
        services.AddSingleton<IPeriodRepository, PeriodRepository>();
        services.AddSingleton<IClarificationRepository, ClarificationRepository>();
        services.AddSingleton<IOfflineEventRepository, OfflineEventRepository>();
        services.AddSingleton<IStateProfileRepository, StateProfileRepository>();
        services.AddSingleton<IInboxConflictRepository, InboxConflictRepository>();
        services.AddSingleton<IStrategyDraftRepository, StrategyDraftRepository>();
        services.AddSingleton<IDependencyLinkRepository, DependencyLinkRepository>();
        services.AddSingleton<IStage6ArtifactRepository, Stage6ArtifactRepository>();
        services.AddSingleton<IStage6CaseRepository, Stage6CaseRepository>();
        services.AddSingleton<IStage6UserContextRepository, Stage6UserContextRepository>();
        services.AddSingleton<IStage6FeedbackRepository, Stage6FeedbackRepository>();
        services.AddSingleton<IStage6CaseOutcomeRepository, Stage6CaseOutcomeRepository>();
        services.AddSingleton<IEvalRepository, EvalRepository>();
        services.AddSingleton<IDomainReviewEventRepository, DomainReviewEventRepository>();
        return services;
    }
}
