using System.Text.Json;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage5;

public sealed class EditDiffContractSchemaProvider : ILlmContractSchemaProvider
{
    private const string EditDiffSchemaJson =
        """
        {
          "type": "object",
          "additionalProperties": false,
          "required": [
            "classification",
            "summary",
            "should_affect_memory",
            "added_important",
            "removed_important",
            "confidence"
          ],
          "properties": {
            "classification": {
              "type": "string",
              "enum": [
                "typo",
                "formatting",
                "minor_rephrase",
                "meaning_changed",
                "important_added",
                "important_removed",
                "message_deleted",
                "unknown"
              ]
            },
            "summary": {
              "type": "string",
              "minLength": 1,
              "maxLength": 220
            },
            "should_affect_memory": {
              "type": "boolean"
            },
            "added_important": {
              "type": "boolean"
            },
            "removed_important": {
              "type": "boolean"
            },
            "confidence": {
              "type": "number",
              "minimum": 0,
              "maximum": 1
            }
          }
        }
        """;

    private const string SessionSummarySchemaJson =
        """
        {
          "type": "object",
          "additionalProperties": false,
          "required": [
            "summary"
          ],
          "properties": {
            "summary": {
              "type": "string",
              "minLength": 1,
              "maxLength": 1200
            }
          }
        }
        """;

    public LlmContractSchemaDescriptor? GetSchema(LlmContractKind kind)
    {
        if (kind == LlmContractKind.EditDiffV1)
        {
            return new LlmContractSchemaDescriptor
            {
                Kind = LlmContractKind.EditDiffV1,
                SchemaRef = "edit_diff/v1",
                SchemaName = "edit_diff_v1",
                SchemaJson = EditDiffSchemaJson,
                SystemInstruction =
                    "You are a contract normalizer. Convert reasoning notes into strict JSON that exactly matches the provided schema. Return only JSON."
            };
        }

        if (kind == LlmContractKind.SessionSummaryV1)
        {
            return new LlmContractSchemaDescriptor
            {
                Kind = LlmContractKind.SessionSummaryV1,
                SchemaRef = "session_summary/v1",
                SchemaName = "session_summary_v1",
                SchemaJson = SessionSummarySchemaJson,
                SystemInstruction =
                    "You are a contract normalizer. Convert raw summary reasoning into strict JSON with only the `summary` field. Return only JSON."
            };
        }

        return null;
    }
}

public sealed class EditDiffContractValidator : ILlmContractValidator
{
    private static readonly HashSet<string> AllowedClassifications =
    [
        "typo",
        "formatting",
        "minor_rephrase",
        "meaning_changed",
        "important_added",
        "important_removed",
        "message_deleted",
        "unknown"
    ];

    public LlmContractValidationResult Validate(LlmContractKind kind, string normalizedPayloadJson)
    {
        return kind switch
        {
            LlmContractKind.EditDiffV1 => ValidateEditDiff(normalizedPayloadJson),
            LlmContractKind.SessionSummaryV1 => ValidateSessionSummary(normalizedPayloadJson),
            _ => new LlmContractValidationResult
            {
                IsValid = false,
                Errors = ["schema:unsupported_contract_kind"]
            }
        };
    }

    private static LlmContractValidationResult ValidateEditDiff(string normalizedPayloadJson)
    {
        var errors = new List<string>();
        try
        {
            using var document = JsonDocument.Parse(normalizedPayloadJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                errors.Add("schema:root_not_object");
                return new LlmContractValidationResult { IsValid = false, Errors = errors };
            }

            var classification = TryGetRequiredString(root, "classification", errors);
            var summary = TryGetRequiredString(root, "summary", errors);
            var shouldAffectMemory = TryGetRequiredBoolean(root, "should_affect_memory", errors);
            var addedImportant = TryGetRequiredBoolean(root, "added_important", errors);
            var removedImportant = TryGetRequiredBoolean(root, "removed_important", errors);
            var confidence = TryGetRequiredDouble(root, "confidence", errors);

            if (errors.Count > 0)
            {
                return new LlmContractValidationResult { IsValid = false, Errors = errors };
            }

            if (!AllowedClassifications.Contains(classification!))
            {
                errors.Add("schema:classification_out_of_range");
            }

            if (string.IsNullOrWhiteSpace(summary) || summary.Length > 220)
            {
                errors.Add("schema:summary_invalid");
            }

            if (confidence is < 0d or > 1d)
            {
                errors.Add("schema:confidence_out_of_range");
            }

            if (string.Equals(classification, "message_deleted", StringComparison.Ordinal) && (!removedImportant!.Value || !shouldAffectMemory!.Value))
            {
                errors.Add("business:deleted_requires_removed_important_and_memory_impact");
            }

            if (string.Equals(classification, "important_added", StringComparison.Ordinal) && (!addedImportant!.Value || !shouldAffectMemory!.Value))
            {
                errors.Add("business:important_added_requires_added_important_and_memory_impact");
            }

            return new LlmContractValidationResult
            {
                IsValid = errors.Count == 0,
                Errors = errors
            };
        }
        catch (JsonException)
        {
            return new LlmContractValidationResult
            {
                IsValid = false,
                Errors = ["schema:json_parse_error"]
            };
        }
    }

    private static LlmContractValidationResult ValidateSessionSummary(string normalizedPayloadJson)
    {
        var errors = new List<string>();
        try
        {
            using var document = JsonDocument.Parse(normalizedPayloadJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                errors.Add("schema:root_not_object");
                return new LlmContractValidationResult { IsValid = false, Errors = errors };
            }

            var summary = TryGetRequiredString(root, "summary", errors);
            if (errors.Count > 0)
            {
                return new LlmContractValidationResult { IsValid = false, Errors = errors };
            }

            if (string.IsNullOrWhiteSpace(summary))
            {
                errors.Add("schema:summary_invalid");
            }
            else
            {
                var normalized = summary.Trim();
                if (normalized.Length > 1200)
                {
                    errors.Add("schema:summary_too_long");
                }
            }

            return new LlmContractValidationResult
            {
                IsValid = errors.Count == 0,
                Errors = errors
            };
        }
        catch (JsonException)
        {
            return new LlmContractValidationResult
            {
                IsValid = false,
                Errors = ["schema:json_parse_error"]
            };
        }
    }

    private static string? TryGetRequiredString(JsonElement root, string property, List<string> errors)
    {
        if (!root.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String)
        {
            errors.Add($"schema:missing_or_invalid:{property}");
            return null;
        }

        return value.GetString();
    }

    private static bool? TryGetRequiredBoolean(JsonElement root, string property, List<string> errors)
    {
        if (!root.TryGetProperty(property, out var value) || value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            errors.Add($"schema:missing_or_invalid:{property}");
            return null;
        }

        return value.GetBoolean();
    }

    private static double? TryGetRequiredDouble(JsonElement root, string property, List<string> errors)
    {
        if (!root.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Number)
        {
            errors.Add($"schema:missing_or_invalid:{property}");
            return null;
        }

        return value.GetDouble();
    }
}
