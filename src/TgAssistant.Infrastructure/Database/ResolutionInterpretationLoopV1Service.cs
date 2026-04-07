using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Infrastructure.Database;

public sealed class ResolutionInterpretationLoopV1Service : IResolutionInterpretationLoopService
{
    private readonly IResolutionInterpretationModel _model;
    private readonly ILogger<ResolutionInterpretationLoopV1Service> _logger;
    private readonly ResolutionInterpretationLoopSettings _settings;
    private readonly string _canonicalScopeKey;
    private readonly int _maxAdditionalRetrievalRounds;
    private readonly int _maxInitialEvidenceItems;
    private readonly int _maxRequestedContextItems;

    public ResolutionInterpretationLoopV1Service(
        IResolutionInterpretationModel model,
        IOptions<ResolutionInterpretationLoopSettings> settings,
        ILogger<ResolutionInterpretationLoopV1Service> logger)
    {
        _model = model;
        _settings = settings.Value ?? new ResolutionInterpretationLoopSettings();
        _logger = logger;
        _canonicalScopeKey = string.IsNullOrWhiteSpace(_settings.CanonicalScopeKey)
            ? "chat:885574984"
            : _settings.CanonicalScopeKey.Trim();
        _maxAdditionalRetrievalRounds = Math.Max(1, _settings.MaxAdditionalRetrievalRounds);
        _maxInitialEvidenceItems = Math.Max(1, _settings.MaxInitialEvidenceItems);
        _maxRequestedContextItems = Math.Max(1, _settings.MaxRequestedContextItems);
    }

    public async Task<ResolutionInterpretationLoopResult> InterpretAsync(
        ResolutionInterpretationLoopRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var auditTrail = new List<ResolutionInterpretationAuditEntry>();
        var knownEvidenceRefs = request.Evidence
            .Select(BuildEvidenceRef)
            .Distinct(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);
        var cumulativeTotalTokens = 0;
        var cumulativeCostUsd = 0m;

        AddAuditEntry(
            auditTrail,
            step: "context_manifest",
            retrievalRound: 0,
            requestedContextType: ResolutionInterpretationContextTypes.None,
            status: "recorded",
            details: $"Initial bounded context contains {Math.Min(request.Evidence.Count, _maxInitialEvidenceItems)} evidence refs, {request.Notes.Count} notes, and {request.DurableContextSummaries.Count} durable summaries.",
            evidenceRefs: request.Evidence.Take(_maxInitialEvidenceItems).Select(BuildEvidenceRef));

        if (!_settings.Enabled)
        {
            AddAuditEntry(
                auditTrail,
                step: "scope_gate",
                retrievalRound: 0,
                requestedContextType: ResolutionInterpretationContextTypes.None,
                status: "skipped",
                details: "ResolutionInterpretationLoopV1 is disabled by runtime settings.");
            return BuildFallback(request, ResolutionInterpretationFailureReasons.LoopDisabled, auditTrail, knownEvidenceRefs);
        }

        if (_settings.CanonicalScopeOnly
            && !string.Equals(request.ScopeKey, _canonicalScopeKey, StringComparison.Ordinal))
        {
            AddAuditEntry(
                auditTrail,
                step: "scope_gate",
                retrievalRound: 0,
                requestedContextType: ResolutionInterpretationContextTypes.None,
                status: "skipped",
                details: $"ResolutionInterpretationLoopV1 is enabled only for '{_canonicalScopeKey}'.");
            return BuildFallback(request, ResolutionInterpretationFailureReasons.ScopeRejected, auditTrail, knownEvidenceRefs);
        }

        var budgetConfigurationFailure = ValidateBudgetConfiguration();
        if (budgetConfigurationFailure is not null)
        {
            AddAuditEntry(
                auditTrail,
                step: "budget_guard",
                retrievalRound: 0,
                requestedContextType: ResolutionInterpretationContextTypes.None,
                status: "invalid_configuration",
                details: $"Budget guard failed before model execution: {budgetConfigurationFailure}.");
            return BuildFallback(request, budgetConfigurationFailure, auditTrail, knownEvidenceRefs);
        }

        var initialContext = BuildInitialContext(request, _maxInitialEvidenceItems);
        ResolutionInterpretationModelResponse initialResponse;
        try
        {
            initialResponse = await _model.InterpretAsync(
                new ResolutionInterpretationModelRequest
                {
                    ScopeKey = request.ScopeKey,
                    ScopeItemKey = request.ScopeItemKey,
                    RetrievalRound = 0,
                    AllowedContextTypes = ResolutionInterpretationContextTypes.All,
                    Context = initialContext,
                    AdditionalContext = new ResolutionInterpretationAdditionalContext()
                },
                ct);
        }
        catch (ResolutionInterpretationSchemaException ex)
        {
            _logger.LogWarning(
                ex,
                "Resolution interpretation loop initial model payload failed schema validation: scope={ScopeKey}, scope_item_key={ScopeItemKey}",
                request.ScopeKey,
                request.ScopeItemKey);
            AddAuditEntry(
                auditTrail,
                step: "model_round",
                retrievalRound: 0,
                requestedContextType: ResolutionInterpretationContextTypes.None,
                status: "model_error",
                details: ex.Message);
            return BuildFallback(request, ResolutionInterpretationFailureReasons.SchemaInvalid, auditTrail, knownEvidenceRefs);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Resolution interpretation loop initial model pass failed: scope={ScopeKey}, scope_item_key={ScopeItemKey}",
                request.ScopeKey,
                request.ScopeItemKey);
            AddAuditEntry(
                auditTrail,
                step: "model_round",
                retrievalRound: 0,
                requestedContextType: ResolutionInterpretationContextTypes.None,
                status: "model_error",
                details: ex.Message);
            return BuildFallback(request, ResolutionInterpretationFailureReasons.RetrievalFailed, auditTrail, knownEvidenceRefs);
        }

        AddAuditEntry(
            auditTrail,
            step: "model_round",
            retrievalRound: 0,
            requestedContextType: initialResponse.Interpretation.RequestedContextType,
            status: "completed",
            details: $"Initial model response received. total_tokens={initialResponse.TotalTokens}.",
            provider: initialResponse.Provider,
            model: initialResponse.Model,
            requestId: initialResponse.RequestId,
            latencyMs: initialResponse.LatencyMs,
            promptTokens: initialResponse.PromptTokens,
            completionTokens: initialResponse.CompletionTokens,
            totalTokens: initialResponse.TotalTokens,
            costUsd: initialResponse.CostUsd,
            evidenceRefs: initialResponse.Interpretation.EvidenceRefsUsed);

        var initialBudgetFailure = TryEvaluateBudgetFailure(
            initialResponse,
            ref cumulativeTotalTokens,
            ref cumulativeCostUsd);
        if (initialBudgetFailure is not null)
        {
            AddAuditEntry(
                auditTrail,
                step: "budget_guard",
                retrievalRound: 0,
                requestedContextType: initialResponse.Interpretation.RequestedContextType,
                status: "breached",
                details: $"Budget ceiling reached after initial model round: {initialBudgetFailure}.",
                provider: initialResponse.Provider,
                model: initialResponse.Model,
                requestId: initialResponse.RequestId,
                latencyMs: initialResponse.LatencyMs,
                promptTokens: initialResponse.PromptTokens,
                completionTokens: initialResponse.CompletionTokens,
                totalTokens: initialResponse.TotalTokens,
                costUsd: initialResponse.CostUsd);
            return BuildFallback(request, initialBudgetFailure, auditTrail, knownEvidenceRefs);
        }

        var normalizedInitial = NormalizeResult(request, initialResponse.Interpretation, knownEvidenceRefs);
        if (normalizedInitial.ContextSufficient
            || string.Equals(normalizedInitial.RequestedContextType, ResolutionInterpretationContextTypes.None, StringComparison.Ordinal))
        {
            normalizedInitial.Applied = true;
            normalizedInitial.UsedFallback = false;
            normalizedInitial.AuditTrail = auditTrail;
            return normalizedInitial;
        }

        var requestedContextType = normalizedInitial.RequestedContextType;
        if (!ResolutionInterpretationContextTypes.IsSupported(requestedContextType)
            || string.Equals(requestedContextType, ResolutionInterpretationContextTypes.None, StringComparison.Ordinal))
        {
            AddAuditEntry(
                auditTrail,
                step: "retrieval_gate",
                retrievalRound: 0,
                requestedContextType: requestedContextType,
                status: "rejected",
                details: "Requested context type was not admitted by the bounded whitelist.");
            return BuildFallback(request, ResolutionInterpretationFailureReasons.ScopeRejected, auditTrail, knownEvidenceRefs);
        }

        var additionalContext = BuildAdditionalContext(request, requestedContextType);
        if (!HasAdditionalContext(additionalContext))
        {
            AddAuditEntry(
                auditTrail,
                step: "retrieval_round",
                retrievalRound: 1,
                requestedContextType: requestedContextType,
                status: "empty",
                details: $"Requested context '{requestedContextType}' had no bounded data available.");
            return BuildFallback(request, ResolutionInterpretationFailureReasons.RetrievalFailed, auditTrail, knownEvidenceRefs);
        }

        AddAuditEntry(
            auditTrail,
            step: "retrieval_round",
            retrievalRound: 1,
            requestedContextType: requestedContextType,
            status: "completed",
            details: $"Returned one bounded retrieval round for '{requestedContextType}'.",
            evidenceRefs: additionalContext.Evidence.Select(BuildEvidenceRef));

        ResolutionInterpretationModelResponse finalResponse;
        try
        {
            finalResponse = await _model.InterpretAsync(
                new ResolutionInterpretationModelRequest
                {
                    ScopeKey = request.ScopeKey,
                    ScopeItemKey = request.ScopeItemKey,
                    RetrievalRound = _maxAdditionalRetrievalRounds,
                    AllowedContextTypes = ResolutionInterpretationContextTypes.All,
                    Context = initialContext,
                    AdditionalContext = additionalContext
                },
                ct);
        }
        catch (ResolutionInterpretationSchemaException ex)
        {
            _logger.LogWarning(
                ex,
                "Resolution interpretation loop final model payload failed schema validation: scope={ScopeKey}, scope_item_key={ScopeItemKey}, requested_context_type={RequestedContextType}",
                request.ScopeKey,
                request.ScopeItemKey,
                requestedContextType);
            AddAuditEntry(
                auditTrail,
                step: "model_round",
                retrievalRound: 1,
                requestedContextType: requestedContextType,
                status: "model_error",
                details: ex.Message);
            return BuildFallback(request, ResolutionInterpretationFailureReasons.SchemaInvalid, auditTrail, knownEvidenceRefs);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Resolution interpretation loop final model pass failed: scope={ScopeKey}, scope_item_key={ScopeItemKey}, requested_context_type={RequestedContextType}",
                request.ScopeKey,
                request.ScopeItemKey,
                requestedContextType);
            AddAuditEntry(
                auditTrail,
                step: "model_round",
                retrievalRound: 1,
                requestedContextType: requestedContextType,
                status: "model_error",
                details: ex.Message);
            return BuildFallback(request, ResolutionInterpretationFailureReasons.RetrievalFailed, auditTrail, knownEvidenceRefs);
        }

        AddAuditEntry(
            auditTrail,
            step: "model_round",
            retrievalRound: 1,
            requestedContextType: requestedContextType,
            status: "completed",
            details: $"Final model response received after one bounded retrieval round. total_tokens={finalResponse.TotalTokens}.",
            provider: finalResponse.Provider,
            model: finalResponse.Model,
            requestId: finalResponse.RequestId,
            latencyMs: finalResponse.LatencyMs,
            promptTokens: finalResponse.PromptTokens,
            completionTokens: finalResponse.CompletionTokens,
            totalTokens: finalResponse.TotalTokens,
            costUsd: finalResponse.CostUsd,
            evidenceRefs: finalResponse.Interpretation.EvidenceRefsUsed);

        var finalBudgetFailure = TryEvaluateBudgetFailure(
            finalResponse,
            ref cumulativeTotalTokens,
            ref cumulativeCostUsd);
        if (finalBudgetFailure is not null)
        {
            AddAuditEntry(
                auditTrail,
                step: "budget_guard",
                retrievalRound: 1,
                requestedContextType: requestedContextType,
                status: "breached",
                details: $"Budget ceiling reached after final model round: {finalBudgetFailure}.",
                provider: finalResponse.Provider,
                model: finalResponse.Model,
                requestId: finalResponse.RequestId,
                latencyMs: finalResponse.LatencyMs,
                promptTokens: finalResponse.PromptTokens,
                completionTokens: finalResponse.CompletionTokens,
                totalTokens: finalResponse.TotalTokens,
                costUsd: finalResponse.CostUsd);
            return BuildFallback(request, finalBudgetFailure, auditTrail, knownEvidenceRefs);
        }

        var normalizedFinal = NormalizeResult(request, finalResponse.Interpretation, knownEvidenceRefs);
        normalizedFinal.Applied = true;
        normalizedFinal.UsedFallback = false;
        normalizedFinal.AuditTrail = auditTrail;
        return normalizedFinal;
    }

    private static ResolutionInterpretationLoopRequest BuildInitialContext(
        ResolutionInterpretationLoopRequest request,
        int maxInitialEvidenceItems)
    {
        return new ResolutionInterpretationLoopRequest
        {
            TrackedPersonId = request.TrackedPersonId,
            ScopeKey = request.ScopeKey,
            ScopeItemKey = request.ScopeItemKey,
            Item = CloneItem(request.Item),
            SourceKind = request.SourceKind,
            SourceRef = request.SourceRef,
            RequiredAction = request.RequiredAction,
            Notes = request.Notes.Take(6).Select(CloneNote).ToList(),
            Evidence = request.Evidence.Take(maxInitialEvidenceItems).Select(CloneEvidence).ToList(),
            DurableContextSummaries = request.DurableContextSummaries.Take(1).ToList()
        };
    }

    private ResolutionInterpretationAdditionalContext BuildAdditionalContext(
        ResolutionInterpretationLoopRequest request,
        string requestedContextType)
    {
        return requestedContextType switch
        {
            ResolutionInterpretationContextTypes.AdditionalEvidence => new ResolutionInterpretationAdditionalContext
            {
                Evidence = request.Evidence
                    .Skip(_maxInitialEvidenceItems)
                    .Take(_maxRequestedContextItems)
                    .Select(CloneEvidence)
                    .ToList()
            },
            ResolutionInterpretationContextTypes.DurableContext => new ResolutionInterpretationAdditionalContext
            {
                DurableContextSummaries = request.DurableContextSummaries
                    .Skip(1)
                    .Take(_maxRequestedContextItems)
                    .ToList()
            },
            _ => new ResolutionInterpretationAdditionalContext()
        };
    }

    private static bool HasAdditionalContext(ResolutionInterpretationAdditionalContext context)
        => context.Evidence.Count > 0 || context.DurableContextSummaries.Count > 0;

    private string? ValidateBudgetConfiguration()
    {
        if (_settings.MaxInputTokens <= 0
            || _settings.MaxOutputTokens <= 0
            || _settings.MaxTotalTokens <= 0
            || _settings.MaxCostUsdPerLoop <= 0m)
        {
            return InvalidBudgetConfiguration;
        }

        return null;
    }

    private string? TryEvaluateBudgetFailure(
        ResolutionInterpretationModelResponse response,
        ref int cumulativeTotalTokens,
        ref decimal cumulativeCostUsd)
    {
        if (response.PromptTokens.HasValue && response.PromptTokens.Value > _settings.MaxInputTokens)
        {
            return InputTokenBudgetExceeded;
        }

        if (response.CompletionTokens.HasValue && response.CompletionTokens.Value > _settings.MaxOutputTokens)
        {
            return OutputTokenBudgetExceeded;
        }

        if (response.TotalTokens.HasValue)
        {
            cumulativeTotalTokens += response.TotalTokens.Value;
        }

        if (response.CostUsd.HasValue)
        {
            cumulativeCostUsd += response.CostUsd.Value;
        }

        if (cumulativeTotalTokens > _settings.MaxTotalTokens)
        {
            return TotalTokenBudgetExceeded;
        }

        if (cumulativeCostUsd > _settings.MaxCostUsdPerLoop)
        {
            return CostBudgetExceeded;
        }

        if (!response.PromptTokens.HasValue
            || !response.CompletionTokens.HasValue
            || !response.TotalTokens.HasValue
            || !response.CostUsd.HasValue)
        {
            return UsageUnavailable;
        }

        return ValidateBudgetConfiguration();
    }

    private static string InputTokenBudgetExceeded => ResolutionInterpretationFailureReasons.InputTokenBudgetExceeded;
    private static string OutputTokenBudgetExceeded => ResolutionInterpretationFailureReasons.OutputTokenBudgetExceeded;
    private static string TotalTokenBudgetExceeded => ResolutionInterpretationFailureReasons.TotalTokenBudgetExceeded;
    private static string CostBudgetExceeded => ResolutionInterpretationFailureReasons.CostBudgetExceeded;
    private static string UsageUnavailable => ResolutionInterpretationFailureReasons.UsageUnavailable;
    private static string InvalidBudgetConfiguration => ResolutionInterpretationFailureReasons.InvalidBudgetConfiguration;

    private static ResolutionInterpretationLoopResult NormalizeResult(
        ResolutionInterpretationLoopRequest request,
        ResolutionInterpretationLoopResult raw,
        HashSet<string> knownEvidenceRefs)
    {
        var normalizedReviewDecision = ResolutionInterpretationReviewRecommendations.Review;
        var rawDecision = raw.ReviewRecommendation?.Decision;
        if (string.Equals(rawDecision, ResolutionInterpretationReviewRecommendations.NoReview, StringComparison.OrdinalIgnoreCase))
        {
            normalizedReviewDecision = ResolutionInterpretationReviewRecommendations.NoReview;
        }

        var uncertainties = raw.ExplicitUncertainties
            .Select(x => NormalizeText(x, 220))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var claims = new List<ResolutionInterpretationClaim>();
        foreach (var claim in raw.KeyClaims)
        {
            var summary = NormalizeText(claim.Summary, 220);
            if (string.IsNullOrWhiteSpace(summary))
            {
                continue;
            }

            var normalizedClaimType = ResolutionInterpretationClaimTypes.Normalize(claim.ClaimType);
            var evidenceRefs = claim.EvidenceRefs
                .Select(NormalizeEvidenceRef)
                .Where(x => x != null && knownEvidenceRefs.Contains(x))
                .Cast<string>()
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (evidenceRefs.Count == 0 && !HasExplicitUncertaintySignal(normalizedClaimType))
            {
                uncertainties.Add($"Claim omitted due to missing evidence refs: {summary}");
                continue;
            }

            if (evidenceRefs.Count == 0)
            {
                uncertainties.Add($"Uncertainty retained as {normalizedClaimType} without direct evidence refs: {summary}");
            }

            claims.Add(new ResolutionInterpretationClaim
            {
                ClaimType = normalizedClaimType,
                DisplayLabel = ResolutionInterpretationClaimTypes.ToDisplayLabel(normalizedClaimType),
                TrustPercent = DeriveTrustPercent(claim.Confidence),
                Summary = summary,
                EvidenceRefs = evidenceRefs
            });
        }

        var evidenceRefsUsed = raw.EvidenceRefsUsed
            .Select(NormalizeEvidenceRef)
            .Where(x => x != null && knownEvidenceRefs.Contains(x))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (evidenceRefsUsed.Count == 0 && claims.Count > 0)
        {
            evidenceRefsUsed = claims
                .SelectMany(x => x.EvidenceRefs)
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        var normalizedRequestedContextType = ResolutionInterpretationContextTypes.Normalize(raw.RequestedContextType);
        if (raw.ContextSufficient)
        {
            normalizedRequestedContextType = ResolutionInterpretationContextTypes.None;
        }

        var summaryText = NormalizeText(raw.InterpretationSummary, 320);
        if (string.IsNullOrWhiteSpace(summaryText))
        {
            summaryText = BuildFallbackSummary(request);
        }

        var reviewReason = NormalizeText(raw.ReviewRecommendation?.Reason, 220);
        if (string.IsNullOrWhiteSpace(reviewReason))
        {
            reviewReason = normalizedReviewDecision == ResolutionInterpretationReviewRecommendations.NoReview
                ? "Bounded evidence appears coherent enough that no extra operator review is recommended by the interpretation loop."
                : "Bounded evidence still warrants operator review before changing the surfaced resolution state.";
        }

        if (claims.Count == 0 && evidenceRefsUsed.Count == 0)
        {
            uncertainties.Add("No evidence-backed claims survived deterministic normalization.");
        }

        return new ResolutionInterpretationLoopResult
        {
            ContextSufficient = raw.ContextSufficient,
            RequestedContextType = normalizedRequestedContextType,
            InterpretationSummary = summaryText,
            KeyClaims = claims,
            ExplicitUncertainties = uncertainties
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.Ordinal)
                .ToList(),
            ReviewRecommendation = new ResolutionInterpretationReviewRecommendation
            {
                Decision = normalizedReviewDecision,
                DisplayLabel = OperatorAssistantTruthLabels.Recommendation,
                TrustPercent = DeriveTrustPercent(raw.ReviewRecommendation?.Confidence),
                Reason = reviewReason
            },
            EvidenceRefsUsed = evidenceRefsUsed
        };
    }

    private static ResolutionInterpretationLoopResult BuildFallback(
        ResolutionInterpretationLoopRequest request,
        string failureReason,
        List<ResolutionInterpretationAuditEntry> auditTrail,
        HashSet<string> knownEvidenceRefs)
    {
        var evidenceRefs = request.Evidence
            .Select(BuildEvidenceRef)
            .Where(knownEvidenceRefs.Contains)
            .Take(2)
            .ToList();

        AddAuditEntry(
            auditTrail,
            step: "fallback",
            retrievalRound: 0,
            requestedContextType: ResolutionInterpretationContextTypes.None,
            status: "applied",
            details: $"Deterministic fallback applied: {failureReason}.",
            evidenceRefs: evidenceRefs);

        var claims = new List<ResolutionInterpretationClaim>();
        if (evidenceRefs.Count > 0)
        {
            claims.Add(new ResolutionInterpretationClaim
            {
                ClaimType = ResolutionInterpretationClaimTypes.Inference,
                DisplayLabel = OperatorAssistantTruthLabels.Inference,
                TrustPercent = null,
                Summary = $"Bounded evidence still supports surfacing '{request.Item.Title}' for operator attention.",
                EvidenceRefs = [evidenceRefs[0]]
            });
        }

        var uncertainties = new List<string>
        {
            $"Deterministic fallback applied because '{failureReason}'."
        };
        if (claims.Count == 0)
        {
            uncertainties.Add("No evidence-backed interpretation claim was available in the bounded fallback path.");
        }

        return new ResolutionInterpretationLoopResult
        {
            Applied = true,
            UsedFallback = true,
            FailureReason = failureReason,
            ContextSufficient = request.Evidence.Count > 0,
            RequestedContextType = ResolutionInterpretationContextTypes.None,
            InterpretationSummary = BuildFallbackSummary(request),
            KeyClaims = claims,
            ExplicitUncertainties = uncertainties,
            ReviewRecommendation = new ResolutionInterpretationReviewRecommendation
            {
                Decision = ResolutionInterpretationReviewRecommendations.Review,
                DisplayLabel = OperatorAssistantTruthLabels.Recommendation,
                TrustPercent = null,
                Reason = "The existing deterministic projection already surfaced this item for operator review, so the fallback path preserves that review posture."
            },
            EvidenceRefsUsed = evidenceRefs,
            AuditTrail = auditTrail
        };
    }

    private static int? DeriveTrustPercent(float? confidence)
    {
        if (confidence.HasValue)
        {
            return OperatorTruthTrustFormatter.ToTrustPercent(confidence.Value);
        }

        return null;
    }

    private static string BuildFallbackSummary(ResolutionInterpretationLoopRequest request)
    {
        var title = NormalizeText(request.Item.Title, 120);
        var summary = NormalizeText(request.Item.Summary, 180);
        if (string.IsNullOrWhiteSpace(title))
        {
            return string.IsNullOrWhiteSpace(summary)
                ? "Deterministic fallback preserved the existing bounded resolution detail."
                : summary;
        }

        return string.IsNullOrWhiteSpace(summary)
            ? $"Deterministic fallback preserved the existing bounded interpretation for '{title}'."
            : $"{title}: {summary}";
    }

    private static void AddAuditEntry(
        List<ResolutionInterpretationAuditEntry> auditTrail,
        string step,
        int retrievalRound,
        string requestedContextType,
        string status,
        string details,
        IEnumerable<string>? evidenceRefs = null,
        string? provider = null,
        string? model = null,
        string? requestId = null,
        int? latencyMs = null,
        int? promptTokens = null,
        int? completionTokens = null,
        int? totalTokens = null,
        decimal? costUsd = null)
    {
        auditTrail.Add(new ResolutionInterpretationAuditEntry
        {
            Step = step,
            RetrievalRound = retrievalRound,
            RequestedContextType = ResolutionInterpretationContextTypes.Normalize(requestedContextType),
            Status = status,
            Details = details,
            Provider = provider,
            Model = model,
            RequestId = requestId,
            LatencyMs = latencyMs,
            PromptTokens = promptTokens,
            CompletionTokens = completionTokens,
            TotalTokens = totalTokens,
            CostUsd = costUsd,
            EvidenceRefs = evidenceRefs?
                .Select(NormalizeEvidenceRef)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Cast<string>()
                .Distinct(StringComparer.Ordinal)
                .ToList()
                ?? [],
            ObservedAtUtc = DateTime.UtcNow
        });
    }

    private static ResolutionItemSummary CloneItem(ResolutionItemSummary item)
    {
        return new ResolutionItemSummary
        {
            ScopeItemKey = item.ScopeItemKey,
            ItemType = item.ItemType,
            Title = item.Title,
            Summary = item.Summary,
            WhyItMatters = item.WhyItMatters,
            HumanShortTitle = item.HumanShortTitle,
            WhatHappened = item.WhatHappened,
            WhyOperatorAnswerNeeded = item.WhyOperatorAnswerNeeded,
            WhatToDoPrompt = item.WhatToDoPrompt,
            EvidenceHint = item.EvidenceHint,
            SecondaryText = item.SecondaryText,
            AffectedFamily = item.AffectedFamily,
            AffectedObjectRef = item.AffectedObjectRef,
            TrustFactor = item.TrustFactor,
            Status = item.Status,
            EvidenceCount = item.EvidenceCount,
            UpdatedAtUtc = item.UpdatedAtUtc,
            Priority = item.Priority,
            RecommendedNextAction = item.RecommendedNextAction,
            AvailableActions = [.. item.AvailableActions]
        };
    }

    private static ResolutionDetailNote CloneNote(ResolutionDetailNote note)
        => new()
        {
            Kind = note.Kind,
            Text = note.Text
        };

    private static ResolutionEvidenceSummary CloneEvidence(ResolutionEvidenceSummary evidence)
        => new()
        {
            EvidenceItemId = evidence.EvidenceItemId,
            Summary = evidence.Summary,
            TrustFactor = evidence.TrustFactor,
            ObservedAtUtc = evidence.ObservedAtUtc,
            SenderDisplay = evidence.SenderDisplay,
            SourceRef = evidence.SourceRef,
            SourceLabel = evidence.SourceLabel,
            RelevanceHint = evidence.RelevanceHint,
            RelevanceHintIsHeuristic = evidence.RelevanceHintIsHeuristic,
            DecisionLinkage = evidence.DecisionLinkage == null
                ? null
                : new ResolutionEvidenceDecisionLinkage
                {
                    LinkType = evidence.DecisionLinkage.LinkType,
                    LinkTarget = evidence.DecisionLinkage.LinkTarget,
                    ReviewQuestion = evidence.DecisionLinkage.ReviewQuestion,
                    Stance = evidence.DecisionLinkage.Stance,
                    Summary = evidence.DecisionLinkage.Summary,
                    IsHeuristic = evidence.DecisionLinkage.IsHeuristic,
                    HeuristicCalibration = evidence.DecisionLinkage.HeuristicCalibration
                }
        };

    private static string BuildEvidenceRef(ResolutionEvidenceSummary evidence)
        => NormalizeEvidenceRef(evidence.SourceRef) ?? $"evidence:{evidence.EvidenceItemId:D}";

    private static string? NormalizeEvidenceRef(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    private static string NormalizeText(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength
            ? trimmed
            : trimmed[..maxLength].TrimEnd();
    }

    private static bool HasExplicitUncertaintySignal(string claimType)
        => string.Equals(claimType, ResolutionInterpretationClaimTypes.Inference, StringComparison.Ordinal)
           || string.Equals(claimType, ResolutionInterpretationClaimTypes.Hypothesis, StringComparison.Ordinal);
}
