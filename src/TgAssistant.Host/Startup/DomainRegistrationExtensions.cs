using Microsoft.Extensions.DependencyInjection;
using TgAssistant.Core.Interfaces;
using TgAssistant.Host.Launch;
using TgAssistant.Host.Stage5Repair;
using TgAssistant.Infrastructure.Database;
using TgAssistant.Intelligence.Stage5;
using TgAssistant.Intelligence.Stage6Bootstrap;
using TgAssistant.Intelligence.Stage7Formation;
using TgAssistant.Intelligence.Stage8Recompute;
using TgAssistant.Intelligence.Stage6;
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
using TgAssistant.Telegram.Bot;
using TgAssistant.Web.Read;

namespace TgAssistant.Host.Startup;

public static partial class ServiceRegistrationExtensions
{
    public static IServiceCollection AddTelegramAssistantDomainServices(
        this IServiceCollection services,
        bool includeLegacyStage6Diagnostics = false,
        bool includeLegacyWebDiagnostics = false,
        bool includeLegacyBotDiagnostics = false)
    {
        services
            .AddActiveRepositoryServices()
            .AddLegacyRepositoryServices();

        services.AddSingleton<IChatCoordinationService, ChatCoordinationService>();
        services.AddSingleton<FoundationDomainVerificationService>();
        services.AddSingleton<IBudgetGuardrailService, BudgetGuardrailService>();
        services.AddSingleton<LaunchReadinessVerificationService>();
        services.AddSingleton<Stage5ScopedRepairCommand>();
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
        services.AddSingleton<IStage8RecomputeQueueService, Stage8RecomputeQueueService>();

        services.AddSingleton<ExtractionSchemaValidator>();
        services.AddSingleton<MessageContentBuilder>();
        services.AddSingleton<AnalysisContextBuilder>();
        services.AddSingleton<SummaryHistoricalRetrievalService>();
        services.AddSingleton<Stage5VerificationService>();
        services.AddSingleton<ExtractionApplier>();
        services.AddSingleton<ExpensivePassResolver>();
        services.AddSingleton<TelegramDesktopArchiveParser>();

        if (includeLegacyStage6Diagnostics)
        {
            services.AddLegacyStage6DiagnosticServices();
        }

        if (includeLegacyBotDiagnostics)
        {
            services.AddLegacyBotDiagnosticServices();
        }

        if (includeLegacyWebDiagnostics)
        {
            services.AddLegacyWebDiagnosticServices();
        }

        return services;
    }

    private static IServiceCollection AddLegacyStage6DiagnosticServices(this IServiceCollection services)
    {
        // Retained only for explicit cleanup diagnostics; this is not the active Stage 6/7/8 implementation path.
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
        services.AddSingleton<INodeRoleResolver, NodeRoleResolver>();
        services.AddSingleton<IInfluenceEdgeBuilder, InfluenceEdgeBuilder>();
        services.AddSingleton<IInformationFlowBuilder, InformationFlowBuilder>();
        services.AddSingleton<INetworkScoringService, NetworkScoringService>();
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

    private static IServiceCollection AddLegacyBotDiagnosticServices(this IServiceCollection services)
    {
        // Retained only for explicit cleanup diagnostics; this is not the active operator surface.
        services.AddSingleton<IBotCommandService, BotCommandService>();
        services.AddSingleton<BotCommandVerificationService>();
        services.AddSingleton<IBotChatService, BotChatService>();
        return services;
    }

    private static IServiceCollection AddLegacyWebDiagnosticServices(this IServiceCollection services)
    {
        // Retained only for explicit cleanup diagnostics; this is not the active operator surface.
        services.AddSingleton<IWebReadService, WebReadService>();
        services.AddSingleton<IWebReviewService, WebReviewService>();
        services.AddSingleton<IWebOpsService, WebOpsService>();
        services.AddSingleton<IWebSearchService, WebSearchService>();
        services.AddSingleton<IWebRouteRenderer, WebRouteRenderer>();
        services.AddSingleton<WebReadVerificationService>();
        services.AddSingleton<WebReviewVerificationService>();
        services.AddSingleton<WebOpsVerificationService>();
        services.AddSingleton<WebSearchVerificationService>();
        services.AddSingleton<INetworkGraphService, NetworkGraphService>();
        services.AddSingleton<NetworkVerificationService>();
        return services;
    }

    private static IServiceCollection AddActiveRepositoryServices(this IServiceCollection services)
    {
        // Active baseline repository surface: retained substrate plus retained-with-refactor data stores.
        services.AddSingleton<IMessageRepository, MessageRepository>();
        services.AddSingleton<IRealtimeMessageSubstrateRepository, RealtimeMessageSubstrateRepository>();
        services.AddSingleton<IArchiveMessageSubstrateRepository, ArchiveMessageSubstrateRepository>();
        services.AddSingleton<IModelPassEnvelopeRepository, ModelPassEnvelopeRepository>();
        services.AddSingleton<IModelOutputNormalizer, ModelOutputNormalizer>();
        services.AddSingleton<IModelPassAuditStore, ModelPassAuditStore>();
        services.AddSingleton<IStage6BootstrapRepository, Stage6BootstrapRepository>();
        services.AddSingleton<IStage7DossierProfileRepository, Stage7DossierProfileRepository>();
        services.AddSingleton<IStage7PairDynamicsRepository, Stage7PairDynamicsRepository>();
        services.AddSingleton<IStage7TimelineRepository, Stage7TimelineRepository>();
        services.AddSingleton<IStage8RecomputeQueueRepository, Stage8RecomputeQueueRepository>();
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
        services.AddSingleton<ISummaryRepository, SummaryRepository>();
        services.AddSingleton<IChatDialogSummaryRepository, ChatDialogSummaryRepository>();
        services.AddSingleton<IChatSessionRepository, ChatSessionRepository>();
        services.AddSingleton<IBudgetOpsRepository, BudgetOpsRepository>();
        services.AddSingleton<IEvalRepository, EvalRepository>();
        services.AddSingleton<IExternalArchiveIngestionRepository, ExternalArchiveIngestionRepository>();
        return services;
    }

    private static IServiceCollection AddLegacyRepositoryServices(this IServiceCollection services)
    {
        // Frozen legacy repository surface: mapped for cleanup, diagnostics, and controlled migration only.
        services.AddSingleton<IPeriodRepository, PeriodRepository>();
        services.AddSingleton<IClarificationRepository, ClarificationRepository>();
        services.AddSingleton<IOfflineEventRepository, OfflineEventRepository>();
        services.AddSingleton<IStateProfileRepository, StateProfileRepository>();
        services.AddSingleton<IStrategyDraftRepository, StrategyDraftRepository>();
        services.AddSingleton<IInboxConflictRepository, InboxConflictRepository>();
        services.AddSingleton<IDependencyLinkRepository, DependencyLinkRepository>();
        services.AddSingleton<IDomainReviewEventRepository, DomainReviewEventRepository>();
        services.AddSingleton<IStage6ArtifactRepository, Stage6ArtifactRepository>();
        services.AddSingleton<IStage6CaseRepository, Stage6CaseRepository>();
        services.AddSingleton<IStage6UserContextRepository, Stage6UserContextRepository>();
        services.AddSingleton<IStage6FeedbackRepository, Stage6FeedbackRepository>();
        services.AddSingleton<IStage6CaseOutcomeRepository, Stage6CaseOutcomeRepository>();
        return services;
    }
}
