using System.Text.Json;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Infrastructure.Database;

public class ModelOutputNormalizer : IModelOutputNormalizer
{
    private const int MaxShortTextLength = 256;
    private const int MaxLongTextLength = 1200;

    public ModelNormalizationResult Normalize(ModelNormalizationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Envelope);

        ModelPassEnvelopeValidator.Validate(request.Envelope);

        var result = CreateBaseResult(request.Envelope);
        var normalizedJson = NormalizeRawModelOutput(request.RawModelOutput);
        if (normalizedJson == null)
        {
            return Block(result, "Model output is not a JSON object and cannot be normalized safely.");
        }

        try
        {
            using var document = JsonDocument.Parse(normalizedJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return Block(result, "Model output must be a JSON object.");
            }

            var root = document.RootElement;
            var requestedStatus = ReadOptionalString(root, "result_status") ?? request.Envelope.ResultStatus;
            if (!ModelPassResultStatuses.All.Contains(requestedStatus, StringComparer.Ordinal))
            {
                return Block(result, $"Unsupported normalization result_status '{requestedStatus}'.");
            }

            var payload = new ModelNormalizationPayload();
            var issues = new List<ModelNormalizationIssue>();

            ParseFacts(root, payload.Facts, issues);
            ParseInferences(root, payload.Inferences, issues);
            ParseHypotheses(root, payload.Hypotheses, issues);
            ParseConflicts(root, payload.Conflicts, issues);

            result.NormalizedPayload = payload;
            result.CandidateCounts = new ModelNormalizationCandidateCounts
            {
                Facts = payload.Facts.Count,
                Inferences = payload.Inferences.Count,
                Hypotheses = payload.Hypotheses.Count,
                Conflicts = payload.Conflicts.Count
            };
            result.Issues = issues;

            var validCandidateCount = payload.Facts.Count + payload.Inferences.Count + payload.Hypotheses.Count + payload.Conflicts.Count;
            if (string.Equals(requestedStatus, ModelPassResultStatuses.BlockedInvalidInput, StringComparison.Ordinal))
            {
                var blockedReason = ReadBlockedReason(root) ?? "Model output explicitly reported blocked_invalid_input.";
                return Block(result, blockedReason);
            }

            if (string.Equals(requestedStatus, ModelPassResultStatuses.ResultReady, StringComparison.Ordinal))
            {
                if (validCandidateCount == 0)
                {
                    return Block(result, "result_ready output did not produce any typed normalization candidates.");
                }

                if (issues.Count > 0)
                {
                    result.Status = ModelPassResultStatuses.NeedOperatorClarification;
                    return result;
                }

                result.Status = ModelPassResultStatuses.ResultReady;
                return result;
            }

            if (issues.Count > 0 && validCandidateCount == 0)
            {
                return Block(result, "Normalization candidates were rejected because the model output shape was malformed.");
            }

            result.Status = requestedStatus;
            return result;
        }
        catch (JsonException)
        {
            return Block(result, "Model output is not valid JSON and was blocked before durable normalization.");
        }
    }

    private static ModelNormalizationResult CreateBaseResult(ModelPassEnvelope envelope)
    {
        return new ModelNormalizationResult
        {
            ModelPassRunId = envelope.RunId,
            ScopeKey = envelope.ScopeKey,
            TargetType = envelope.Target.TargetType,
            TargetRef = envelope.Target.TargetRef,
            TruthLayer = envelope.TruthSummary.TruthLayer,
            PersonId = envelope.PersonId,
            SourceObjectId = envelope.SourceObjectId,
            EvidenceItemId = envelope.EvidenceItemId
        };
    }

    private static string? NormalizeRawModelOutput(string rawModelOutput)
    {
        if (string.IsNullOrWhiteSpace(rawModelOutput))
        {
            return null;
        }

        var normalized = rawModelOutput.Trim();
        normalized = normalized.Replace("```json", string.Empty, StringComparison.OrdinalIgnoreCase);
        normalized = normalized.Replace("```", string.Empty, StringComparison.Ordinal);
        normalized = normalized.Trim();
        return normalized.StartsWith('{') && normalized.EndsWith('}')
            ? normalized
            : null;
    }

    private static ModelNormalizationResult Block(ModelNormalizationResult result, string blockedReason)
    {
        result.Status = ModelPassResultStatuses.BlockedInvalidInput;
        result.BlockedReason = blockedReason;
        result.NormalizedPayload = new ModelNormalizationPayload();
        result.CandidateCounts = new ModelNormalizationCandidateCounts();
        return result;
    }

    private static string? ReadOptionalString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String
            ? property.GetString()?.Trim()
            : null;
    }

    private static string? ReadBlockedReason(JsonElement root)
    {
        if (root.TryGetProperty("output_summary", out var outputSummary)
            && outputSummary.ValueKind == JsonValueKind.Object)
        {
            return ReadOptionalString(outputSummary, "blocked_reason");
        }

        return ReadOptionalString(root, "blocked_reason");
    }

    private static void ParseFacts(JsonElement root, List<NormalizedFactCandidate> candidates, List<ModelNormalizationIssue> issues)
    {
        ParseCandidateArray(
            root,
            "facts",
            TryParseFact,
            candidates,
            issues);
    }

    private static void ParseInferences(JsonElement root, List<NormalizedInferenceCandidate> candidates, List<ModelNormalizationIssue> issues)
    {
        ParseCandidateArray(
            root,
            "inferences",
            TryParseInference,
            candidates,
            issues);
    }

    private static void ParseHypotheses(JsonElement root, List<NormalizedHypothesisCandidate> candidates, List<ModelNormalizationIssue> issues)
    {
        ParseCandidateArray(
            root,
            "hypotheses",
            TryParseHypothesis,
            candidates,
            issues);
    }

    private static void ParseConflicts(JsonElement root, List<NormalizedConflictCandidate> candidates, List<ModelNormalizationIssue> issues)
    {
        ParseCandidateArray(
            root,
            "conflicts",
            TryParseConflict,
            candidates,
            issues);
    }

    private static void ParseCandidateArray<TCandidate>(
        JsonElement root,
        string propertyName,
        TryParseCandidate<TCandidate> parser,
        List<TCandidate> candidates,
        List<ModelNormalizationIssue> issues)
        where TCandidate : class
    {
        if (!root.TryGetProperty(propertyName, out var property))
        {
            return;
        }

        if (property.ValueKind != JsonValueKind.Array)
        {
            issues.Add(CreateIssue("error", "invalid_array", $"'{propertyName}' must be an array.", propertyName));
            return;
        }

        var index = 0;
        foreach (var element in property.EnumerateArray())
        {
            var path = $"{propertyName}[{index}]";
            if (parser(element, path, out var candidate, out var issue))
            {
                candidates.Add(candidate!);
            }
            else if (issue != null)
            {
                issues.Add(issue);
            }

            index++;
        }
    }

    private delegate bool TryParseCandidate<TCandidate>(
        JsonElement element,
        string path,
        out TCandidate? candidate,
        out ModelNormalizationIssue? issue)
        where TCandidate : class;

    private static bool TryParseFact(
        JsonElement element,
        string path,
        out NormalizedFactCandidate? candidate,
        out ModelNormalizationIssue? issue)
    {
        candidate = null;
        issue = null;
        if (!TryReadObject(element, path, out var jsonObjectIssue))
        {
            issue = jsonObjectIssue;
            return false;
        }

        if (!TryReadRequiredString(element, "category", path, MaxShortTextLength, out var category, out issue)
            || !TryReadRequiredString(element, "key", path, MaxShortTextLength, out var key, out issue)
            || !TryReadRequiredString(element, "value", path, MaxLongTextLength, out var value, out issue)
            || !TryReadTruthLayer(element, path, out var truthLayer, out issue)
            || !TryReadConfidence(element, path, out var confidence, out issue)
            || !TryReadEvidenceRefs(element, path, out var evidenceRefs, out issue))
        {
            return false;
        }

        candidate = new NormalizedFactCandidate
        {
            Category = category,
            Key = key,
            Value = value,
            TruthLayer = truthLayer,
            Confidence = confidence,
            EvidenceRefs = evidenceRefs
        };
        return true;
    }

    private static bool TryParseInference(
        JsonElement element,
        string path,
        out NormalizedInferenceCandidate? candidate,
        out ModelNormalizationIssue? issue)
    {
        candidate = null;
        issue = null;
        if (!TryReadObject(element, path, out var jsonObjectIssue))
        {
            issue = jsonObjectIssue;
            return false;
        }

        if (!TryReadRequiredString(element, "inference_type", path, MaxShortTextLength, out var inferenceType, out issue)
            || !TryReadRequiredString(element, "subject_type", path, MaxShortTextLength, out var subjectType, out issue)
            || !TryReadRequiredString(element, "subject_ref", path, MaxShortTextLength, out var subjectRef, out issue)
            || !TryReadRequiredString(element, "summary", path, MaxLongTextLength, out var summary, out issue)
            || !TryReadTruthLayer(element, path, out var truthLayer, out issue)
            || !TryReadConfidence(element, path, out var confidence, out issue)
            || !TryReadEvidenceRefs(element, path, out var evidenceRefs, out issue))
        {
            return false;
        }

        candidate = new NormalizedInferenceCandidate
        {
            InferenceType = inferenceType,
            SubjectType = subjectType,
            SubjectRef = subjectRef,
            Summary = summary,
            TruthLayer = truthLayer,
            Confidence = confidence,
            EvidenceRefs = evidenceRefs
        };
        return true;
    }

    private static bool TryParseHypothesis(
        JsonElement element,
        string path,
        out NormalizedHypothesisCandidate? candidate,
        out ModelNormalizationIssue? issue)
    {
        candidate = null;
        issue = null;
        if (!TryReadObject(element, path, out var jsonObjectIssue))
        {
            issue = jsonObjectIssue;
            return false;
        }

        if (!TryReadRequiredString(element, "hypothesis_type", path, MaxShortTextLength, out var hypothesisType, out issue)
            || !TryReadRequiredString(element, "subject_type", path, MaxShortTextLength, out var subjectType, out issue)
            || !TryReadRequiredString(element, "subject_ref", path, MaxShortTextLength, out var subjectRef, out issue)
            || !TryReadRequiredString(element, "statement", path, MaxLongTextLength, out var statement, out issue)
            || !TryReadTruthLayer(element, path, out var truthLayer, out issue)
            || !TryReadConfidence(element, path, out var confidence, out issue)
            || !TryReadEvidenceRefs(element, path, out var evidenceRefs, out issue))
        {
            return false;
        }

        candidate = new NormalizedHypothesisCandidate
        {
            HypothesisType = hypothesisType,
            SubjectType = subjectType,
            SubjectRef = subjectRef,
            Statement = statement,
            TruthLayer = truthLayer,
            Confidence = confidence,
            EvidenceRefs = evidenceRefs
        };
        return true;
    }

    private static bool TryParseConflict(
        JsonElement element,
        string path,
        out NormalizedConflictCandidate? candidate,
        out ModelNormalizationIssue? issue)
    {
        candidate = null;
        issue = null;
        if (!TryReadObject(element, path, out var jsonObjectIssue))
        {
            issue = jsonObjectIssue;
            return false;
        }

        if (!TryReadRequiredString(element, "conflict_type", path, MaxShortTextLength, out var conflictType, out issue)
            || !TryReadRequiredString(element, "summary", path, MaxLongTextLength, out var summary, out issue)
            || !TryReadTruthLayer(element, path, out var truthLayer, out issue)
            || !TryReadConfidence(element, path, out var confidence, out issue)
            || !TryReadEvidenceRefs(element, path, out var evidenceRefs, out issue))
        {
            return false;
        }

        candidate = new NormalizedConflictCandidate
        {
            ConflictType = conflictType,
            Summary = summary,
            TruthLayer = truthLayer,
            RelatedObjectRef = ReadOptionalString(element, "related_object_ref"),
            Confidence = confidence,
            EvidenceRefs = evidenceRefs
        };
        return true;
    }

    private static bool TryReadObject(JsonElement element, string path, out ModelNormalizationIssue? issue)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            issue = null;
            return true;
        }

        issue = CreateIssue("error", "invalid_object", $"'{path}' must be an object.", path);
        return false;
    }

    private static bool TryReadRequiredString(
        JsonElement element,
        string propertyName,
        string path,
        int maxLength,
        out string value,
        out ModelNormalizationIssue? issue)
    {
        value = string.Empty;
        issue = null;
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            issue = CreateIssue("error", "missing_string", $"'{path}.{propertyName}' must be a string.", $"{path}.{propertyName}");
            return false;
        }

        value = property.GetString()?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value) || value.Length > maxLength)
        {
            issue = CreateIssue(
                "error",
                "invalid_string_length",
                $"'{path}.{propertyName}' must be non-empty and at most {maxLength} characters.",
                $"{path}.{propertyName}");
            return false;
        }

        return true;
    }

    private static bool TryReadTruthLayer(
        JsonElement element,
        string path,
        out string truthLayer,
        out ModelNormalizationIssue? issue)
    {
        truthLayer = string.Empty;
        issue = null;
        if (!TryReadRequiredString(element, "truth_layer", path, MaxShortTextLength, out truthLayer, out issue))
        {
            return false;
        }

        if (!ModelNormalizationTruthLayers.All.Contains(truthLayer, StringComparer.Ordinal))
        {
            issue = CreateIssue(
                "error",
                "invalid_truth_layer",
                $"'{path}.truth_layer' is not part of the approved truth-layer set.",
                $"{path}.truth_layer");
            return false;
        }

        return true;
    }

    private static bool TryReadConfidence(
        JsonElement element,
        string path,
        out float confidence,
        out ModelNormalizationIssue? issue)
    {
        confidence = 0;
        issue = null;
        if (!element.TryGetProperty("confidence", out var property)
            || (property.ValueKind != JsonValueKind.Number && property.ValueKind != JsonValueKind.String))
        {
            issue = CreateIssue("error", "missing_confidence", $"'{path}.confidence' must be numeric.", $"{path}.confidence");
            return false;
        }

        var parsed = property.ValueKind == JsonValueKind.Number
            ? property.GetSingle()
            : float.TryParse(property.GetString(), out var stringValue)
                ? stringValue
                : float.NaN;
        if (float.IsNaN(parsed) || float.IsInfinity(parsed) || parsed < 0 || parsed > 1)
        {
            issue = CreateIssue(
                "error",
                "invalid_confidence",
                $"'{path}.confidence' must be between 0 and 1.",
                $"{path}.confidence");
            return false;
        }

        confidence = parsed;
        return true;
    }

    private static bool TryReadEvidenceRefs(
        JsonElement element,
        string path,
        out List<string> evidenceRefs,
        out ModelNormalizationIssue? issue)
    {
        evidenceRefs = [];
        issue = null;
        if (!element.TryGetProperty("evidence_refs", out var property))
        {
            return true;
        }

        if (property.ValueKind != JsonValueKind.Array)
        {
            issue = CreateIssue(
                "error",
                "invalid_evidence_refs",
                $"'{path}.evidence_refs' must be an array of strings.",
                $"{path}.evidence_refs");
            return false;
        }

        var index = 0;
        foreach (var item in property.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                issue = CreateIssue(
                    "error",
                    "invalid_evidence_ref",
                    $"'{path}.evidence_refs[{index}]' must be a string.",
                    $"{path}.evidence_refs[{index}]");
                return false;
            }

            var value = item.GetString()?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(value) || value.Length > MaxShortTextLength)
            {
                issue = CreateIssue(
                    "error",
                    "invalid_evidence_ref_length",
                    $"'{path}.evidence_refs[{index}]' must be non-empty and at most {MaxShortTextLength} characters.",
                    $"{path}.evidence_refs[{index}]");
                return false;
            }

            evidenceRefs.Add(value);
            index++;
        }

        return true;
    }

    private static ModelNormalizationIssue CreateIssue(string severity, string code, string summary, string? path)
    {
        return new ModelNormalizationIssue
        {
            Severity = severity,
            Code = code,
            Summary = summary,
            Path = path
        };
    }
}
