using Microsoft.Extensions.Options;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Infrastructure.Database;

public sealed class OperatorAssistantResponseGenerationService : IOperatorAssistantResponseGenerationService
{
    private static readonly string[] ForbiddenHandoffLeakMarkers =
    [
        "open_in_web",
        "tracked_person_id",
        "scope_item_key",
        "operator_session_id",
        "target_api",
        "handoff_token"
    ];

    private const string DefaultKnownFallback = "Recent bounded evidence is limited in the active tracked-person scope.";
    private const string DefaultMeansFallback = "Current interpretation remains uncertain due to limited bounded evidence.";
    private const string DefaultRecommendationFallback = "Ask one clarifying follow-up question before taking action.";
    private const string DefaultShortAnswerFallback = "Available bounded evidence is limited for a high-confidence answer.";

    private readonly WebSettings _webSettings;

    public OperatorAssistantResponseGenerationService(IOptions<WebSettings> webSettings)
    {
        _webSettings = webSettings.Value ?? new WebSettings();
    }

    public OperatorAssistantResponseEnvelope BuildResponse(
        OperatorAssistantResponseGenerationRequest request,
        DateTime? generatedAtUtc = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        var session = request.Session ?? new OperatorSessionContext();
        if (request.TrackedPersonId == Guid.Empty)
        {
            throw new InvalidOperationException(OperatorAssistantFailureReasons.MissingActiveTrackedPerson);
        }

        if (session.ActiveTrackedPersonId != Guid.Empty && session.ActiveTrackedPersonId != request.TrackedPersonId)
        {
            throw new InvalidOperationException(OperatorAssistantFailureReasons.TrackedPersonScopeMismatch);
        }

        if (!string.IsNullOrWhiteSpace(session.ActiveScopeItemKey)
            && !string.IsNullOrWhiteSpace(request.OpenInWebScopeItemKey)
            && !string.Equals(session.ActiveScopeItemKey.Trim(), request.OpenInWebScopeItemKey.Trim(), StringComparison.Ordinal))
        {
            throw new InvalidOperationException(OperatorAssistantFailureReasons.SessionScopeItemMismatch);
        }

        var shortAnswer = NormalizeShortAnswer(request.ShortAnswer);
        var known = NormalizeWhatIsKnown(request.WhatIsKnown);
        var means = NormalizeWhatItMeans(request.WhatItMeans);
        var recommendation = NormalizeRecommendation(request.Recommendation);
        var trust = ClampPercent(request.TrustPercent ?? recommendation.TrustPercent);
        var generatedAt = (generatedAtUtc ?? DateTime.UtcNow).ToUniversalTime();
        var scopeItemKey = NormalizeOptional(request.OpenInWebScopeItemKey) ?? NormalizeOptional(session.ActiveScopeItemKey) ?? string.Empty;
        var operatorSessionId = NormalizeOptional(session.OperatorSessionId) ?? string.Empty;
        var scopeKey = NormalizeRequired(request.ScopeKey, "scope_missing");

        var response = new OperatorAssistantResponseEnvelope
        {
            ContractVersion = OperatorAssistantContractVersions.OpintAssistantV1,
            Surface = OperatorAssistantSurfaces.TelegramAssistant,
            TrackedPersonId = request.TrackedPersonId,
            ScopeKey = scopeKey,
            OperatorSessionId = operatorSessionId,
            Question = NormalizeRequired(request.Question, "question_missing"),
            GeneratedAtUtc = generatedAt,
            Sections = new OperatorAssistantResponseSections
            {
                ShortAnswer = shortAnswer,
                WhatIsKnown = known,
                WhatItMeans = means,
                Recommendation = recommendation
            },
            TrustPercent = trust,
            OpenInWeb = new OperatorAssistantOpenInWebContract
            {
                Enabled = request.OpenInWebEnabled,
                TargetApi = NormalizeOptional(request.OpenInWebTargetApi) ?? "/api/operator/resolution/detail/query",
                TrackedPersonId = request.TrackedPersonId,
                ScopeItemKey = scopeItemKey,
                ActiveMode = NormalizeOptional(request.OpenInWebActiveMode) ?? OperatorModeTypes.ResolutionDetail,
                HandoffToken = NormalizeOptional(request.OpenInWebHandoffToken)
                    ?? BuildHandoffToken(request.TrackedPersonId, scopeItemKey, operatorSessionId, generatedAt)
            },
            Guardrails = new OperatorAssistantGuardrailContract
            {
                ScopeBounded = true,
                McpDependent = false
            }
        };

        var validationErrors = Validate(response);
        if (validationErrors.Count > 0)
        {
            throw new InvalidOperationException($"assistant_response_validation_failed:{string.Join("|", validationErrors)}");
        }

        return response;
    }

    public IReadOnlyList<string> Validate(OperatorAssistantResponseEnvelope response)
    {
        ArgumentNullException.ThrowIfNull(response);

        var errors = new List<string>();
        if (!string.Equals(response.ContractVersion, OperatorAssistantContractVersions.OpintAssistantV1, StringComparison.Ordinal))
        {
            errors.Add("contract_version_invalid");
        }

        if (!string.Equals(response.Surface, OperatorAssistantSurfaces.TelegramAssistant, StringComparison.Ordinal))
        {
            errors.Add("surface_invalid");
        }

        if (response.TrackedPersonId == Guid.Empty)
        {
            errors.Add("tracked_person_id_missing");
        }

        if (string.IsNullOrWhiteSpace(response.ScopeKey))
        {
            errors.Add("scope_key_missing");
        }

        if (response.Sections == null)
        {
            errors.Add("sections_missing");
            return errors;
        }

        if (!OperatorAssistantTruthLabels.IsShortAnswerSupported(response.Sections.ShortAnswer?.TruthLabel))
        {
            errors.Add("short_answer_truth_label_invalid");
        }

        if (response.Sections.WhatIsKnown.Any(x => !string.Equals(x.TruthLabel, OperatorAssistantTruthLabels.Fact, StringComparison.Ordinal)))
        {
            errors.Add("what_is_known_truth_label_invalid");
        }

        if (response.Sections.WhatItMeans.Any(x => !OperatorAssistantTruthLabels.IsWhatItMeansSupported(x.TruthLabel)))
        {
            errors.Add("what_it_means_truth_label_invalid");
        }

        if (!string.Equals(response.Sections.Recommendation?.TruthLabel, OperatorAssistantTruthLabels.Recommendation, StringComparison.Ordinal))
        {
            errors.Add("recommendation_truth_label_invalid");
        }

        var shortAnswerTrust = response.Sections.ShortAnswer?.TrustPercent;
        var recommendationTrust = response.Sections.Recommendation?.TrustPercent;
        if (!IsPercent(response.TrustPercent)
            || shortAnswerTrust == null
            || !IsPercent(shortAnswerTrust.Value)
            || recommendationTrust == null
            || !IsPercent(recommendationTrust.Value)
            || response.Sections.WhatIsKnown.Any(x => !IsPercent(x.TrustPercent))
            || response.Sections.WhatItMeans.Any(x => !IsPercent(x.TrustPercent)))
        {
            errors.Add("trust_percent_invalid");
        }

        if (response.OpenInWeb == null)
        {
            errors.Add("open_in_web_missing");
        }
        else if (response.OpenInWeb.TrackedPersonId != response.TrackedPersonId)
        {
            errors.Add("open_in_web_tracked_person_mismatch");
        }

        if (response.Guardrails == null)
        {
            errors.Add("guardrails_missing");
        }
        else
        {
            if (!response.Guardrails.ScopeBounded)
            {
                errors.Add("scope_bounded_invalid");
            }

            if (response.Guardrails.McpDependent)
            {
                errors.Add("mcp_dependent_invalid");
            }

            if (!response.Guardrails.ReadModelBounded)
            {
                errors.Add("read_model_bounded_invalid");
            }

            if (response.Guardrails.ReadModelAudit.Any(x =>
                    !x.Bounded
                    || x.TrackedPersonId != response.TrackedPersonId
                    || !string.Equals(NormalizeOptional(x.ScopeKey), NormalizeOptional(response.ScopeKey), StringComparison.Ordinal)))
            {
                errors.Add("read_model_audit_scope_invalid");
            }
        }

        return errors;
    }

    public string RenderTelegram(OperatorAssistantResponseEnvelope response)
    {
        ArgumentNullException.ThrowIfNull(response);
        var validationErrors = Validate(response);
        if (validationErrors.Count > 0)
        {
            throw new InvalidOperationException($"assistant_response_validation_failed:{string.Join("|", validationErrors)}");
        }

        var lines = new List<string>
        {
            "Short Answer",
            FormatLine(response.Sections.ShortAnswer),
            string.Empty,
            "What Is Known"
        };

        lines.AddRange(response.Sections.WhatIsKnown.Select(x => $"- {FormatLine(x)}"));
        lines.Add(string.Empty);
        lines.Add("What It Means");
        lines.AddRange(response.Sections.WhatItMeans.Select(x => $"- {FormatLine(x)}"));
        lines.Add(string.Empty);
        lines.Add("Recommendation");
        lines.Add(FormatLine(response.Sections.Recommendation));
        lines.Add(string.Empty);
        lines.Add($"Trust: {ClampPercent(response.TrustPercent)}%");
        return string.Join(Environment.NewLine, lines);
    }

    private static OperatorAssistantStatement NormalizeShortAnswer(OperatorAssistantStatementInput? input)
    {
        var label = OperatorAssistantTruthLabels.IsShortAnswerSupported(input?.TruthLabel)
            ? input!.TruthLabel.Trim()
            : OperatorAssistantTruthLabels.Inference;
        return CreateStatement(input, label, DefaultShortAnswerFallback);
    }

    private static List<OperatorAssistantStatement> NormalizeWhatIsKnown(IEnumerable<OperatorAssistantStatementInput> statements)
    {
        var result = statements?
            .Select(x => CreateStatement(x, OperatorAssistantTruthLabels.Fact, DefaultKnownFallback))
            .ToList()
            ?? [];
        if (result.Count == 0)
        {
            result.Add(CreateStatement(null, OperatorAssistantTruthLabels.Fact, DefaultKnownFallback));
        }

        return result;
    }

    private static List<OperatorAssistantStatement> NormalizeWhatItMeans(IEnumerable<OperatorAssistantStatementInput> statements)
    {
        var result = statements?
            .Select(x =>
            {
                var label = OperatorAssistantTruthLabels.IsWhatItMeansSupported(x.TruthLabel)
                    ? x.TruthLabel.Trim()
                    : OperatorAssistantTruthLabels.Inference;
                return CreateStatement(x, label, DefaultMeansFallback);
            })
            .ToList()
            ?? [];
        if (result.Count == 0)
        {
            result.Add(CreateStatement(null, OperatorAssistantTruthLabels.Inference, DefaultMeansFallback));
        }

        return result;
    }

    private static OperatorAssistantStatement NormalizeRecommendation(OperatorAssistantStatementInput? input)
        => CreateStatement(input, OperatorAssistantTruthLabels.Recommendation, DefaultRecommendationFallback);

    private static OperatorAssistantStatement CreateStatement(
        OperatorAssistantStatementInput? input,
        string truthLabel,
        string defaultText)
    {
        var statementText = NormalizeOptional(input?.Text) ?? defaultText;
        if (ContainsForbiddenHandoffLeak(statementText))
        {
            statementText = defaultText;
        }

        return new OperatorAssistantStatement
        {
            TruthLabel = truthLabel,
            Text = statementText,
            TrustPercent = ClampPercent(input?.TrustPercent ?? 50),
            EvidenceRefs = [.. (input?.EvidenceRefs ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim())]
        };
    }

    private static bool ContainsForbiddenHandoffLeak(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return ForbiddenHandoffLeakMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsPercent(int value)
        => value is >= 0 and <= 100;

    private static int ClampPercent(int value)
        => Math.Clamp(value, 0, 100);

    private static string NormalizeRequired(string? value, string fallback)
        => NormalizeOptional(value) ?? fallback;

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private string BuildHandoffToken(Guid trackedPersonId, string scopeItemKey, string operatorSessionId, DateTime generatedAtUtc)
    {
        var signingSecret = OperatorHandoffTokenCodec.ResolveSigningSecret(_webSettings);
        if (string.IsNullOrWhiteSpace(signingSecret))
        {
            return string.Empty;
        }

        return OperatorHandoffTokenCodec.CreateToken(
            OperatorHandoffTokenCodec.AssistantResolutionContext,
            trackedPersonId,
            scopeItemKey,
            operatorSessionId,
            signingSecret,
            generatedAtUtc);
    }

    private static string FormatLine(OperatorAssistantStatement statement)
        => $"[{statement.TruthLabel} | {ClampPercent(statement.TrustPercent)}%] {statement.Text}";
}
