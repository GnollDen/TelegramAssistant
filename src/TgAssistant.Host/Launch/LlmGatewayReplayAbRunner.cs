using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.LlmGateway;

namespace TgAssistant.Host.Launch;

public static class LlmGatewayReplayAbRunner
{
    private const string ExperimentLabel = "gateway_text_replay_ab";

    public static async Task<LlmGatewayReplayAbReport> RunAsync(string? outputPath = null, CancellationToken ct = default)
    {
        var settings = BuildSettings();
        var options = Options.Create(settings);
        var loggerFactory = LoggerFactory.Create(_ => { });
        var routingPolicy = new DefaultLlmRoutingPolicy(options);
        var baselineStickyKey = FindStickyKeyForBranch(routingPolicy, "baseline");
        var candidateStickyKey = FindStickyKeyForBranch(routingPolicy, "candidate");
        var replayCases = BuildReplayCases();

        var codexHandler = new RecordingHttpMessageHandler((request, body) => HandleBaselineRequest(request, body));
        var openRouterHandler = new RecordingHttpMessageHandler((request, body) => HandleCandidateRequest(request, body));
        var gateway = new LlmGatewayService(
            providers:
            [
                new CodexLbChatProviderClient(new HttpClient(codexHandler), options),
                new OpenRouterProviderClient(new HttpClient(openRouterHandler), options)
            ],
            routingPolicy,
            options,
            loggerFactory.CreateLogger<LlmGatewayService>(),
            new LlmGatewayMetrics());

        var caseComparisons = new List<LlmGatewayReplayCaseComparison>(replayCases.Count);
        foreach (var replayCase in replayCases)
        {
            var baseline = await ExecuteBranchAsync(
                gateway,
                replayCase,
                branch: "baseline",
                baselineStickyKey,
                ct);
            var candidate = await ExecuteBranchAsync(
                gateway,
                replayCase,
                branch: "candidate",
                candidateStickyKey,
                ct);

            caseComparisons.Add(BuildCaseComparison(replayCase, baseline, candidate));
        }

        var resolvedOutputPath = ResolveOutputPath(outputPath);
        var report = new LlmGatewayReplayAbReport
        {
            GeneratedAtUtc = DateTime.UtcNow,
            ExperimentLabel = ExperimentLabel,
            SafePathKey = "stage5_edit_diff",
            OutputPath = resolvedOutputPath,
            BaselineStickyRoutingKey = baselineStickyKey,
            CandidateStickyRoutingKey = candidateStickyKey,
            Cases = caseComparisons,
            BranchSummaries =
            [
                BuildBranchSummary("baseline", caseComparisons.Select(x => x.Baseline)),
                BuildBranchSummary("candidate", caseComparisons.Select(x => x.Candidate))
            ],
            Comparison = BuildComparisonSummary(caseComparisons)
        };

        Directory.CreateDirectory(Path.GetDirectoryName(resolvedOutputPath)!);
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(resolvedOutputPath, json, ct);
        return report;
    }

    private static async Task<LlmGatewayReplayBranchResult> ExecuteBranchAsync(
        LlmGatewayService gateway,
        ReplayCase replayCase,
        string branch,
        string stickyKey,
        CancellationToken ct)
    {
        try
        {
            var response = await gateway.ExecuteAsync(BuildReplayRequest(replayCase, stickyKey), ct);
            var normalized = TryNormalizeEditDiff(response.Output.StructuredPayloadJson ?? response.Output.Text, out var schemaError);
            var matchesExpected = normalized is not null && MatchesExpected(replayCase.Expected, normalized);

            return new LlmGatewayReplayBranchResult
            {
                Branch = branch,
                Provider = response.Provider,
                Model = response.Model,
                RequestId = response.RequestId,
                LatencyMs = response.LatencyMs,
                PromptTokens = response.Usage.PromptTokens,
                CompletionTokens = response.Usage.CompletionTokens,
                TotalTokens = response.Usage.TotalTokens,
                CostUsd = response.Usage.CostUsd,
                FallbackApplied = response.FallbackApplied,
                FallbackFromProvider = response.FallbackFromProvider,
                SchemaValid = normalized is not null,
                SchemaError = schemaError,
                NormalizedBehavior = normalized,
                MatchesExpectedBehavior = matchesExpected,
                RawProviderPayloadJson = response.RawProviderPayloadJson
            };
        }
        catch (LlmGatewayException ex)
        {
            return new LlmGatewayReplayBranchResult
            {
                Branch = branch,
                Provider = ex.Provider,
                LatencyMs = null,
                ErrorCategory = ex.Category.ToString(),
                ErrorRetryable = ex.Retryable,
                HttpStatusCode = ex.HttpStatus is null ? null : (int)ex.HttpStatus.Value,
                RawReasonCode = ex.RawReasonCode,
                SchemaValid = false,
                MatchesExpectedBehavior = false
            };
        }
    }

    private static LlmGatewayReplayCaseComparison BuildCaseComparison(
        ReplayCase replayCase,
        LlmGatewayReplayBranchResult baseline,
        LlmGatewayReplayBranchResult candidate)
    {
        return new LlmGatewayReplayCaseComparison
        {
            CaseId = replayCase.CaseId,
            SafePathTaskKey = replayCase.TaskKey,
            BeforeText = replayCase.BeforeText,
            AfterText = replayCase.AfterText,
            Expected = replayCase.Expected,
            Baseline = baseline,
            Candidate = candidate,
            NormalizedOutputComparison = BuildNormalizedOutputComparison(baseline, candidate)
        };
    }

    private static string BuildNormalizedOutputComparison(
        LlmGatewayReplayBranchResult baseline,
        LlmGatewayReplayBranchResult candidate)
    {
        if (!string.IsNullOrWhiteSpace(baseline.ErrorCategory) || !string.IsNullOrWhiteSpace(candidate.ErrorCategory))
        {
            return "error";
        }

        if (!baseline.SchemaValid || !candidate.SchemaValid)
        {
            return "schema_mismatch";
        }

        if (baseline.NormalizedBehavior is null || candidate.NormalizedBehavior is null)
        {
            return "unavailable";
        }

        return AreEquivalent(baseline.NormalizedBehavior, candidate.NormalizedBehavior)
            ? "parity"
            : "diverged";
    }

    private static LlmGatewayReplayBranchSummary BuildBranchSummary(
        string branch,
        IEnumerable<LlmGatewayReplayBranchResult> results)
    {
        var materialized = results.ToList();
        var totalCases = materialized.Count;
        var successes = materialized.Count(x => string.IsNullOrWhiteSpace(x.ErrorCategory));
        var errorCount = totalCases - successes;
        var schemaValidCount = materialized.Count(x => x.SchemaValid);
        var expectedMatchCount = materialized.Count(x => x.MatchesExpectedBehavior);
        var latencyValues = materialized.Where(x => x.LatencyMs.HasValue).Select(x => x.LatencyMs!.Value).ToList();

        return new LlmGatewayReplayBranchSummary
        {
            Branch = branch,
            TotalCases = totalCases,
            SuccessCount = successes,
            ErrorCount = errorCount,
            ErrorRate = ComputeRate(errorCount, totalCases),
            SchemaValidCount = schemaValidCount,
            SchemaValidRate = ComputeRate(schemaValidCount, totalCases),
            ExpectedBehaviorMatchCount = expectedMatchCount,
            ExpectedBehaviorMatchRate = ComputeRate(expectedMatchCount, totalCases),
            AverageLatencyMs = latencyValues.Count == 0 ? null : Math.Round(latencyValues.Average(), 2),
            TotalTokens = materialized.Sum(x => x.TotalTokens ?? 0),
            TotalCostUsd = materialized.Sum(x => x.CostUsd ?? 0m)
        };
    }

    private static LlmGatewayReplayComparisonSummary BuildComparisonSummary(IReadOnlyList<LlmGatewayReplayCaseComparison> comparisons)
    {
        var parityCount = comparisons.Count(x => string.Equals(x.NormalizedOutputComparison, "parity", StringComparison.Ordinal));
        var divergedCount = comparisons.Count(x => string.Equals(x.NormalizedOutputComparison, "diverged", StringComparison.Ordinal));
        var schemaMismatchCount = comparisons.Count(x => string.Equals(x.NormalizedOutputComparison, "schema_mismatch", StringComparison.Ordinal));
        var errorCount = comparisons.Count(x => string.Equals(x.NormalizedOutputComparison, "error", StringComparison.Ordinal));

        return new LlmGatewayReplayComparisonSummary
        {
            TotalCases = comparisons.Count,
            ParityCount = parityCount,
            DivergedCount = divergedCount,
            SchemaMismatchCount = schemaMismatchCount,
            ErrorCount = errorCount,
            ParityRate = ComputeRate(parityCount, comparisons.Count)
        };
    }

    private static decimal ComputeRate(int numerator, int denominator)
    {
        if (denominator <= 0)
        {
            return 0m;
        }

        return Math.Round((decimal)numerator / denominator, 4);
    }

    private static bool MatchesExpected(ReplayExpectedOutput expected, ReplayNormalizedOutput actual)
    {
        return string.Equals(expected.Classification, actual.Classification, StringComparison.Ordinal)
            && expected.ShouldAffectMemory == actual.ShouldAffectMemory
            && expected.AddedImportant == actual.AddedImportant
            && expected.RemovedImportant == actual.RemovedImportant
            && !string.IsNullOrWhiteSpace(actual.Summary)
            && actual.Confidence >= 0f
            && actual.Confidence <= 1f;
    }

    private static bool AreEquivalent(ReplayNormalizedOutput left, ReplayNormalizedOutput right)
    {
        return string.Equals(left.Classification, right.Classification, StringComparison.Ordinal)
            && left.ShouldAffectMemory == right.ShouldAffectMemory
            && left.AddedImportant == right.AddedImportant
            && left.RemovedImportant == right.RemovedImportant;
    }

    private static ReplayNormalizedOutput? TryNormalizeEditDiff(string? json, out string? schemaError)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            schemaError = "empty_json";
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                schemaError = "root_not_object";
                return null;
            }

            if (!TryGetRequiredString(root, "classification", out var classification)
                || !TryGetRequiredString(root, "summary", out var summary)
                || !TryGetRequiredBoolean(root, "should_affect_memory", out var shouldAffectMemory)
                || !TryGetRequiredBoolean(root, "added_important", out var addedImportant)
                || !TryGetRequiredBoolean(root, "removed_important", out var removedImportant)
                || !TryGetRequiredSingle(root, "confidence", out var confidence))
            {
                schemaError = "missing_or_invalid_field";
                return null;
            }

            schemaError = null;
            return new ReplayNormalizedOutput
            {
                Classification = classification,
                Summary = summary,
                ShouldAffectMemory = shouldAffectMemory,
                AddedImportant = addedImportant,
                RemovedImportant = removedImportant,
                Confidence = confidence
            };
        }
        catch (JsonException)
        {
            schemaError = "json_parse_error";
            return null;
        }
    }

    private static bool TryGetRequiredString(JsonElement root, string name, out string value)
    {
        value = string.Empty;
        if (!root.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString()?.Trim() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryGetRequiredBoolean(JsonElement root, string name, out bool value)
    {
        value = false;
        if (!root.TryGetProperty(name, out var property) || property.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return false;
        }

        value = property.GetBoolean();
        return true;
    }

    private static bool TryGetRequiredSingle(JsonElement root, string name, out float value)
    {
        value = 0f;
        if (!root.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.Number)
        {
            return false;
        }

        value = (float)property.GetDouble();
        return true;
    }

    private static LlmGatewaySettings BuildSettings()
    {
        return new LlmGatewaySettings
        {
            Enabled = true,
            LogRawProviderPayloadJson = true,
            Routing = new Dictionary<string, LlmGatewayRouteSettings>(StringComparer.OrdinalIgnoreCase)
            {
                ["text_chat"] = new()
                {
                    PrimaryProvider = CodexLbChatProviderClient.ProviderIdValue,
                    PrimaryModel = "edit-diff-baseline-default",
                    FallbackProviders =
                    [
                        new LlmGatewayProviderTargetSettings
                        {
                            Provider = OpenRouterProviderClient.ProviderIdValue,
                            Model = "edit-diff-candidate-default"
                        }
                    ]
                }
            },
            Providers = new Dictionary<string, LlmGatewayProviderSettings>(StringComparer.OrdinalIgnoreCase)
            {
                [CodexLbChatProviderClient.ProviderIdValue] = new()
                {
                    BaseUrl = "http://codex-lb.local",
                    ApiKey = "codex-replay-key",
                    DefaultModel = "codex-edit-diff-default",
                    ChatCompletionsPath = "/v1/chat/completions"
                },
                [OpenRouterProviderClient.ProviderIdValue] = new()
                {
                    BaseUrl = "https://openrouter.local/api/v1",
                    ApiKey = "openrouter-replay-key",
                    DefaultModel = "openrouter-edit-diff-default",
                    ChatCompletionsPath = "/chat/completions",
                    EmbeddingsPath = "/embeddings"
                }
            },
            Experiments = new Dictionary<string, LlmGatewayExperimentSettings>(StringComparer.OrdinalIgnoreCase)
            {
                [ExperimentLabel] = new()
                {
                    Enabled = true,
                    Branches =
                    [
                        new LlmGatewayExperimentBranchSettings
                        {
                            Branch = "baseline",
                            WeightPercent = 50,
                            Provider = CodexLbChatProviderClient.ProviderIdValue,
                            Model = "edit-diff-baseline-model"
                        },
                        new LlmGatewayExperimentBranchSettings
                        {
                            Branch = "candidate",
                            WeightPercent = 50,
                            Provider = OpenRouterProviderClient.ProviderIdValue,
                            Model = "edit-diff-candidate-model"
                        }
                    ]
                }
            }
        };
    }

    private static List<ReplayCase> BuildReplayCases()
    {
        return
        [
            new ReplayCase
            {
                CaseId = "typo_fix",
                TaskKey = "edit_diff",
                BeforeText = "Встреча завтра в 19:00 у метро.",
                AfterText = "Встреча завтра в 19:00 у метро!",
                Expected = new ReplayExpectedOutput
                {
                    Classification = "formatting",
                    Summary = "Косметическая правка пунктуации без изменения смысла.",
                    ShouldAffectMemory = false,
                    AddedImportant = false,
                    RemovedImportant = false,
                    Confidence = 0.93f
                }
            },
            new ReplayCase
            {
                CaseId = "meaning_change",
                TaskKey = "edit_diff",
                BeforeText = "Я смогу приехать в пятницу.",
                AfterText = "Я не смогу приехать в пятницу.",
                Expected = new ReplayExpectedOutput
                {
                    Classification = "meaning_changed",
                    Summary = "Правка изменила смысл сообщения о возможности приезда.",
                    ShouldAffectMemory = true,
                    AddedImportant = false,
                    RemovedImportant = false,
                    Confidence = 0.95f
                }
            },
            new ReplayCase
            {
                CaseId = "deleted_message",
                TaskKey = "edit_diff",
                BeforeText = "Билеты уже куплены, выезжаю утром.",
                AfterText = "[DELETED]",
                Expected = new ReplayExpectedOutput
                {
                    Classification = "message_deleted",
                    Summary = "Удалено сообщение с потенциально значимой логистикой.",
                    ShouldAffectMemory = true,
                    AddedImportant = false,
                    RemovedImportant = true,
                    Confidence = 0.91f
                }
            },
            new ReplayCase
            {
                CaseId = "time_added",
                TaskKey = "edit_diff",
                BeforeText = "Созвонимся вечером.",
                AfterText = "Созвонимся сегодня в 21:30.",
                Expected = new ReplayExpectedOutput
                {
                    Classification = "important_added",
                    Summary = "В правке появилось конкретное время звонка.",
                    ShouldAffectMemory = true,
                    AddedImportant = true,
                    RemovedImportant = false,
                    Confidence = 0.94f
                }
            }
        ];
    }

    private static LlmGatewayRequest BuildReplayRequest(ReplayCase replayCase, string stickyKey)
    {
        return new LlmGatewayRequest
        {
            Modality = LlmModality.TextChat,
            TaskKey = replayCase.TaskKey,
            ResponseMode = LlmResponseMode.JsonObject,
            Limits = new LlmExecutionLimits
            {
                MaxTokens = 320,
                Temperature = 0.1f,
                TimeoutMs = 5000
            },
            Trace = new LlmTraceContext
            {
                PathKey = "replay:stage5_edit_diff",
                RequestId = $"gateway-replay-{replayCase.CaseId}"
            },
            Experiment = new LlmGatewayExperimentContext
            {
                Label = ExperimentLabel,
                StickyRoutingKey = stickyKey
            },
            Messages =
            [
                LlmGatewayMessage.FromText(LlmMessageRole.System, """
You analyze Telegram message edits for memory impact.
Return ONLY JSON with fields:
- classification: typo | formatting | minor_rephrase | meaning_changed | important_added | important_removed | message_deleted | unknown
- summary: concise Russian summary of what changed (max 220 chars)
- should_affect_memory: boolean
- added_important: boolean
- removed_important: boolean
- confidence: number 0..1
"""),
                LlmGatewayMessage.FromText(LlmMessageRole.User, $"""
case_id: {replayCase.CaseId}
chat_id: 885574984
message_id: replay-{replayCase.CaseId}

BEFORE:
{replayCase.BeforeText}

AFTER:
{replayCase.AfterText}
""")
            ]
        };
    }

    private static HttpResponseMessage HandleBaselineRequest(HttpRequestMessage request, string body)
    {
        var caseId = AssertAndReadCaseId(
            request,
            body,
            expectedPath: "/v1/chat/completions",
            expectedApiKey: "codex-replay-key",
            expectedModel: "edit-diff-baseline-model");

        Thread.Sleep(caseId switch
        {
            "typo_fix" => 8,
            "meaning_change" => 11,
            "deleted_message" => 10,
            "time_added" => 9,
            _ => 7
        });

        return BuildJsonResponse(
            BuildSuccessPayload(caseId, baseline: true),
            $"baseline-{caseId}");
    }

    private static HttpResponseMessage HandleCandidateRequest(HttpRequestMessage request, string body)
    {
        var caseId = AssertAndReadCaseId(
            request,
            body,
            expectedPath: "/api/v1/chat/completions",
            expectedApiKey: "openrouter-replay-key",
            expectedModel: "edit-diff-candidate-model");

        Thread.Sleep(caseId switch
        {
            "typo_fix" => 14,
            "meaning_change" => 17,
            "deleted_message" => 13,
            "time_added" => 12,
            _ => 10
        });

        return caseId switch
        {
            "time_added" => BuildErrorResponse(HttpStatusCode.BadGateway, "{\"error\":\"candidate upstream timeout\"}", $"candidate-{caseId}"),
            _ => BuildJsonResponse(BuildSuccessPayload(caseId, baseline: false), $"candidate-{caseId}")
        };
    }

    private static object BuildSuccessPayload(string caseId, bool baseline)
    {
        var content = baseline
            ? caseId switch
            {
                "typo_fix" => """{"classification":"formatting","summary":"Косметическая правка пунктуации без изменения смысла.","should_affect_memory":false,"added_important":false,"removed_important":false,"confidence":0.93}""",
                "meaning_change" => """{"classification":"meaning_changed","summary":"Правка изменила смысл сообщения о возможности приезда.","should_affect_memory":true,"added_important":false,"removed_important":false,"confidence":0.95}""",
                "deleted_message" => """{"classification":"message_deleted","summary":"Удалено сообщение с потенциально значимой логистикой.","should_affect_memory":true,"added_important":false,"removed_important":true,"confidence":0.91}""",
                "time_added" => """{"classification":"important_added","summary":"В правке появилось конкретное время звонка.","should_affect_memory":true,"added_important":true,"removed_important":false,"confidence":0.94}""",
                _ => throw new InvalidOperationException($"Unknown replay case '{caseId}'.")
            }
            : caseId switch
            {
                "typo_fix" => """{"classification":"formatting","summary":"Пунктуационная правка без смысловых изменений.","should_affect_memory":false,"added_important":false,"removed_important":false,"confidence":0.91}""",
                "meaning_change" => """{"classification":"minor_rephrase","summary":"Похоже на небольшую переформулировку.","should_affect_memory":false,"added_important":false,"removed_important":false,"confidence":0.62}""",
                "deleted_message" => """{"classification":"message_deleted","summary":"Удалено сообщение","should_affect_memory":true,"added_important":false,"removed_important":true,"confidence":"high"}""",
                _ => throw new InvalidOperationException($"Unknown replay case '{caseId}'.")
            };

        return new
        {
            id = $"{(baseline ? "baseline" : "candidate")}-{caseId}",
            choices = new[]
            {
                new
                {
                    message = new
                    {
                        content
                    }
                }
            },
            usage = new
            {
                prompt_tokens = baseline ? 110 : 118,
                completion_tokens = baseline ? 42 : 48,
                total_tokens = baseline ? 152 : 166,
                cost = baseline ? 0.0042m : 0.0051m
            }
        };
    }

    private static string AssertAndReadCaseId(
        HttpRequestMessage request,
        string body,
        string expectedPath,
        string expectedApiKey,
        string expectedModel)
    {
        if (request.Method != HttpMethod.Post)
        {
            throw new InvalidOperationException("LLM gateway replay A/B failed: provider request did not use POST.");
        }

        if (!string.Equals(request.RequestUri?.AbsolutePath, expectedPath, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"LLM gateway replay A/B failed: unexpected provider path '{request.RequestUri?.AbsolutePath}'.");
        }

        if (!string.Equals(request.Headers.Authorization?.Scheme, "Bearer", StringComparison.Ordinal)
            || !string.Equals(request.Headers.Authorization?.Parameter, expectedApiKey, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("LLM gateway replay A/B failed: provider authorization header missing.");
        }

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        AssertJsonString(root, "model", expectedModel, "replay model");

        if (!root.TryGetProperty("response_format", out var responseFormat)
            || !responseFormat.TryGetProperty("type", out var formatType)
            || !string.Equals(formatType.GetString(), "json_object", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("LLM gateway replay A/B failed: request did not preserve json_object response mode.");
        }

        if (!root.TryGetProperty("messages", out var messages) || messages.ValueKind != JsonValueKind.Array || messages.GetArrayLength() != 2)
        {
            throw new InvalidOperationException("LLM gateway replay A/B failed: replay request did not serialize expected messages.");
        }

        var userContent = messages[1].GetProperty("content").GetString() ?? string.Empty;
        var caseIdLine = userContent
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(line => line.StartsWith("case_id:", StringComparison.Ordinal));
        if (string.IsNullOrWhiteSpace(caseIdLine))
        {
            throw new InvalidOperationException("LLM gateway replay A/B failed: replay request missing case_id.");
        }

        return caseIdLine["case_id:".Length..].Trim();
    }

    private static string FindStickyKeyForBranch(DefaultLlmRoutingPolicy routingPolicy, string expectedBranch)
    {
        for (var index = 0; index < 512; index++)
        {
            var stickyKey = $"replay-branch-{index}";
            var decision = routingPolicy.Resolve(BuildReplayRequest(BuildReplayCases()[0], stickyKey));
            if (string.Equals(decision.Experiment?.Branch, expectedBranch, StringComparison.Ordinal))
            {
                return stickyKey;
            }
        }

        throw new InvalidOperationException($"LLM gateway replay A/B failed: unable to find sticky routing key for branch '{expectedBranch}'.");
    }

    private static string ResolveOutputPath(string? outputPath)
    {
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            return Path.GetFullPath(outputPath);
        }

        var repositoryRoot = FindRepositoryRoot();
        return Path.GetFullPath(Path.Combine(
            repositoryRoot,
            "artifacts",
            "llm-gateway",
            "gateway_text_replay_ab_report.json"));
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            if (current.GetFiles("TelegramAssistant.sln").Length > 0)
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return Directory.GetCurrentDirectory();
    }

    private static void AssertJsonString(JsonElement element, string propertyName, string expected, string label)
    {
        if (!element.TryGetProperty(propertyName, out var property) || !string.Equals(property.GetString(), expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"LLM gateway replay A/B failed: expected {label}='{expected}'.");
        }
    }

    private static HttpResponseMessage BuildJsonResponse(object payload, string requestId)
    {
        var json = JsonSerializer.Serialize(payload);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json),
            Headers =
            {
                { "x-request-id", requestId }
            }
        };
    }

    private static HttpResponseMessage BuildErrorResponse(HttpStatusCode statusCode, string payload, string requestId)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(payload),
            Headers =
            {
                { "x-request-id", requestId }
            }
        };
    }

    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, string, HttpResponseMessage> _handler;

        public RecordingHttpMessageHandler(Func<HttpRequestMessage, string, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            return _handler(request, body);
        }
    }

    private sealed class ReplayCase
    {
        public string CaseId { get; set; } = string.Empty;
        public string TaskKey { get; set; } = string.Empty;
        public string BeforeText { get; set; } = string.Empty;
        public string AfterText { get; set; } = string.Empty;
        public ReplayExpectedOutput Expected { get; set; } = new();
    }

    public sealed class LlmGatewayReplayAbReport
    {
        public DateTime GeneratedAtUtc { get; set; }
        public string ExperimentLabel { get; set; } = string.Empty;
        public string SafePathKey { get; set; } = string.Empty;
        public string OutputPath { get; set; } = string.Empty;
        public string BaselineStickyRoutingKey { get; set; } = string.Empty;
        public string CandidateStickyRoutingKey { get; set; } = string.Empty;
        public List<LlmGatewayReplayCaseComparison> Cases { get; set; } = new();
        public List<LlmGatewayReplayBranchSummary> BranchSummaries { get; set; } = new();
        public LlmGatewayReplayComparisonSummary Comparison { get; set; } = new();
    }

    public sealed class LlmGatewayReplayCaseComparison
    {
        public string CaseId { get; set; } = string.Empty;
        public string SafePathTaskKey { get; set; } = string.Empty;
        public string BeforeText { get; set; } = string.Empty;
        public string AfterText { get; set; } = string.Empty;
        public ReplayExpectedOutput Expected { get; set; } = new();
        public LlmGatewayReplayBranchResult Baseline { get; set; } = new();
        public LlmGatewayReplayBranchResult Candidate { get; set; } = new();
        public string NormalizedOutputComparison { get; set; } = string.Empty;
    }

    public sealed class LlmGatewayReplayBranchResult
    {
        public string Branch { get; set; } = string.Empty;
        public string? Provider { get; set; }
        public string? Model { get; set; }
        public string? RequestId { get; set; }
        public int? LatencyMs { get; set; }
        public int? PromptTokens { get; set; }
        public int? CompletionTokens { get; set; }
        public int? TotalTokens { get; set; }
        public decimal? CostUsd { get; set; }
        public bool FallbackApplied { get; set; }
        public string? FallbackFromProvider { get; set; }
        public bool SchemaValid { get; set; }
        public string? SchemaError { get; set; }
        public ReplayNormalizedOutput? NormalizedBehavior { get; set; }
        public bool MatchesExpectedBehavior { get; set; }
        public string? ErrorCategory { get; set; }
        public bool? ErrorRetryable { get; set; }
        public int? HttpStatusCode { get; set; }
        public string? RawReasonCode { get; set; }
        public string? RawProviderPayloadJson { get; set; }
    }

    public sealed class LlmGatewayReplayBranchSummary
    {
        public string Branch { get; set; } = string.Empty;
        public int TotalCases { get; set; }
        public int SuccessCount { get; set; }
        public int ErrorCount { get; set; }
        public decimal ErrorRate { get; set; }
        public int SchemaValidCount { get; set; }
        public decimal SchemaValidRate { get; set; }
        public int ExpectedBehaviorMatchCount { get; set; }
        public decimal ExpectedBehaviorMatchRate { get; set; }
        public double? AverageLatencyMs { get; set; }
        public int TotalTokens { get; set; }
        public decimal TotalCostUsd { get; set; }
    }

    public sealed class LlmGatewayReplayComparisonSummary
    {
        public int TotalCases { get; set; }
        public int ParityCount { get; set; }
        public int DivergedCount { get; set; }
        public int SchemaMismatchCount { get; set; }
        public int ErrorCount { get; set; }
        public decimal ParityRate { get; set; }
    }

    public sealed class ReplayExpectedOutput
    {
        public string Classification { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public bool ShouldAffectMemory { get; set; }
        public bool AddedImportant { get; set; }
        public bool RemovedImportant { get; set; }
        public float Confidence { get; set; }
    }

    public sealed class ReplayNormalizedOutput
    {
        public string Classification { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public bool ShouldAffectMemory { get; set; }
        public bool AddedImportant { get; set; }
        public bool RemovedImportant { get; set; }
        public float Confidence { get; set; }
    }
}
