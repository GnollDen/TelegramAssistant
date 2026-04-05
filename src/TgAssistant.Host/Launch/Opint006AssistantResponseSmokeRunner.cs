using Microsoft.Extensions.Options;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database;

namespace TgAssistant.Host.Launch;

public static class Opint006AssistantResponseSmokeRunner
{
    public static void Run()
    {
        var service = new OperatorAssistantResponseGenerationService(Options.Create(new WebSettings()));
        var trackedPersonId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var request = new OperatorAssistantResponseGenerationRequest
        {
            TrackedPersonId = trackedPersonId,
            ScopeKey = "chat:opint-006-b1-smoke",
            Question = "What should we do next with this tracked person?",
            Session = new OperatorSessionContext
            {
                OperatorSessionId = "telegram:opint-006-b1-smoke",
                Surface = OperatorSurfaceTypes.Telegram,
                ActiveTrackedPersonId = trackedPersonId,
                ActiveScopeItemKey = "resolution:clarification:opint-006",
                ActiveMode = OperatorModeTypes.ResolutionDetail,
                AuthenticatedAtUtc = DateTime.UtcNow,
                LastSeenAtUtc = DateTime.UtcNow
            },
            ShortAnswer = new OperatorAssistantStatementInput
            {
                Text = "Use concise async outreach first.",
                TruthLabel = OperatorAssistantTruthLabels.Inference,
                TrustPercent = 74
            },
            WhatIsKnown =
            [
                new OperatorAssistantStatementInput
                {
                    Text = "Recent replies were mostly text within 12 hours.",
                    TruthLabel = OperatorAssistantTruthLabels.Fact,
                    TrustPercent = 89,
                    EvidenceRefs = ["evidence:known:1"]
                }
            ],
            WhatItMeans =
            [
                new OperatorAssistantStatementInput
                {
                    Text = "Call-first outreach likely lowers response chance.",
                    TruthLabel = OperatorAssistantTruthLabels.Inference,
                    TrustPercent = 71,
                    EvidenceRefs = ["evidence:known:1"]
                },
                new OperatorAssistantStatementInput
                {
                    Text = "Workload pressure may be driving delay variance.",
                    TruthLabel = OperatorAssistantTruthLabels.Hypothesis,
                    TrustPercent = 56
                }
            ],
            Recommendation = new OperatorAssistantStatementInput
            {
                Text = "Send one brief text with two concrete time options.",
                TrustPercent = 72
            },
            TrustPercent = 72,
            OpenInWebEnabled = true,
            OpenInWebScopeItemKey = "resolution:clarification:opint-006"
        };

        var response = service.BuildResponse(request, generatedAtUtc: DateTime.Parse("2026-04-04T00:00:00Z").ToUniversalTime());
        var validationErrors = service.Validate(response);
        if (validationErrors.Count > 0)
        {
            throw new InvalidOperationException($"OPINT-006-B1 smoke failed: response validation errors: {string.Join(", ", validationErrors)}");
        }

        if (response.Sections.WhatIsKnown.Any(x => !string.Equals(x.TruthLabel, OperatorAssistantTruthLabels.Fact, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("OPINT-006-B1 smoke failed: what_is_known entries must be Fact-labeled.");
        }

        if (response.Sections.WhatItMeans.Any(x => !OperatorAssistantTruthLabels.IsWhatItMeansSupported(x.TruthLabel)))
        {
            throw new InvalidOperationException("OPINT-006-B1 smoke failed: what_it_means entries must be Inference or Hypothesis.");
        }

        if (!string.Equals(response.Sections.Recommendation.TruthLabel, OperatorAssistantTruthLabels.Recommendation, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("OPINT-006-B1 smoke failed: recommendation entry must be Recommendation-labeled.");
        }

        var rendered = service.RenderTelegram(response);
        AssertContainsInOrder(
            rendered,
            "Short Answer",
            "What Is Known",
            "What It Means",
            "Recommendation",
            "Trust: 72%");

        var lines = rendered.Split(Environment.NewLine, StringSplitOptions.None);
        if (!string.Equals(lines[^1], "Trust: 72%", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("OPINT-006-B1 smoke failed: terminal trust line is missing or out of place.");
        }

        if (rendered.IndexOf("[Fact | ", StringComparison.Ordinal) < 0
            || rendered.IndexOf("[Inference | ", StringComparison.Ordinal) < 0
            || rendered.IndexOf("[Hypothesis | ", StringComparison.Ordinal) < 0
            || rendered.IndexOf("[Recommendation | ", StringComparison.Ordinal) < 0)
        {
            throw new InvalidOperationException("OPINT-006-B1 smoke failed: required truth labels are missing in rendered output.");
        }
    }

    private static void AssertContainsInOrder(string value, params string[] required)
    {
        var cursor = 0;
        foreach (var entry in required)
        {
            var index = value.IndexOf(entry, cursor, StringComparison.Ordinal);
            if (index < 0)
            {
                throw new InvalidOperationException($"OPINT-006-B1 smoke failed: required section '{entry}' is missing.");
            }

            cursor = index + entry.Length;
        }
    }
}
