using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Telegram.Operator;

public sealed class TelegramOperatorWorkflowService
{
    private const string AuthDeniedSessionEventType = "auth_denied";
    private const string SessionAuthenticatedEventType = "session_authenticated";
    private const string ModeSwitchSessionEventType = "mode_switch";
    private const string TelegramAuthSource = "telegram_owner_allowlist";
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(8);

    private readonly TelegramSettings _settings;
    private readonly TelegramOperatorSessionStore _sessionStore;
    private readonly IOperatorResolutionApplicationService _operatorResolutionService;
    private readonly IOperatorSessionAuditService _auditService;
    private readonly ILogger<TelegramOperatorWorkflowService> _logger;

    public TelegramOperatorWorkflowService(
        IOptions<TelegramSettings> settings,
        TelegramOperatorSessionStore sessionStore,
        IOperatorResolutionApplicationService operatorResolutionService,
        IOperatorSessionAuditService auditService,
        ILogger<TelegramOperatorWorkflowService> logger)
    {
        _settings = settings.Value;
        _sessionStore = sessionStore;
        _operatorResolutionService = operatorResolutionService;
        _auditService = auditService;
        _logger = logger;
    }

    public async Task<TelegramOperatorResponse> HandleInteractionAsync(
        TelegramOperatorInteraction interaction,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(interaction);

        var nowUtc = DateTime.UtcNow;
        if (!IsAuthorized(interaction))
        {
            await AuditUnauthorizedInteractionAsync(interaction, nowUtc, ct);
            return new TelegramOperatorResponse
            {
                CallbackNotificationText = "Access denied.",
                Messages =
                [
                    CreateMessage("Access denied. Telegram operator mode accepts only the configured owner in a private chat.")
                ]
            };
        }

        var state = await GetOrCreateAuthorizedStateAsync(interaction, nowUtc, ct);
        state.UserId = interaction.UserId;
        state.Session.LastSeenAtUtc = nowUtc;

        TelegramOperatorResponse response;
        if (!string.IsNullOrWhiteSpace(interaction.CallbackData))
        {
            response = await HandleCallbackAsync(state, interaction, nowUtc, ct);
        }
        else
        {
            response = await HandleMessageAsync(state, interaction, nowUtc, ct);
        }

        _sessionStore.Set(state);
        return response;
    }

    private async Task<TelegramOperatorResponse> HandleMessageAsync(
        TelegramOperatorConversationState state,
        TelegramOperatorInteraction interaction,
        DateTime nowUtc,
        CancellationToken ct)
    {
        var text = interaction.MessageText?.Trim() ?? string.Empty;
        if (string.Equals(text, "/start", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "/menu", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "/modes", StringComparison.OrdinalIgnoreCase))
        {
            return CreateModeCard(state, note: null);
        }

        if (string.Equals(text, "/resolution", StringComparison.OrdinalIgnoreCase))
        {
            return await EnterResolutionModeAsync(state, interaction, nowUtc, ct);
        }

        if (state.SurfaceMode == TelegramOperatorSurfaceModes.Resolution)
        {
            return await RenderResolutionContextAsync(
                state,
                interaction,
                nowUtc,
                note: "Free chat is disabled in Telegram cockpit mode. Use the compact controls below.",
                ct);
        }

        return CreateModeCard(
            state,
            "Telegram no longer defaults to free chat. Choose a mode first.");
    }

    private async Task<TelegramOperatorResponse> HandleCallbackAsync(
        TelegramOperatorConversationState state,
        TelegramOperatorInteraction interaction,
        DateTime nowUtc,
        CancellationToken ct)
    {
        var callbackData = interaction.CallbackData!.Trim();
        if (string.Equals(callbackData, "mode:menu", StringComparison.Ordinal))
        {
            state.SurfaceMode = TelegramOperatorSurfaceModes.None;
            return CreateModeCard(state, note: null, callbackNotice: "Modes");
        }

        if (string.Equals(callbackData, "mode:resolution", StringComparison.Ordinal))
        {
            return await EnterResolutionModeAsync(state, interaction, nowUtc, ct);
        }

        if (string.Equals(callbackData, "mode:assistant", StringComparison.Ordinal)
            || string.Equals(callbackData, "mode:offline_event", StringComparison.Ordinal)
            || string.Equals(callbackData, "mode:alerts", StringComparison.Ordinal))
        {
            return CreateModeCard(
                state,
                "Only Resolution mode is in scope for OPINT-004-A. Assistant, Offline Event, and Alerts stay deferred.",
                callbackNotice: "Not in this slice");
        }

        if (string.Equals(callbackData, "resolution:switch-person", StringComparison.Ordinal))
        {
            return await RenderTrackedPersonPickerAsync(
                state,
                interaction,
                nowUtc,
                note: "Select the active tracked person for Resolution mode.",
                callbackNotice: "Switch tracked person",
                ct);
        }

        if (string.Equals(callbackData, "resolution:refresh", StringComparison.Ordinal))
        {
            return await RenderResolutionContextAsync(state, interaction, nowUtc, note: null, ct);
        }

        if (callbackData.StartsWith("tracked:", StringComparison.Ordinal))
        {
            return await SelectTrackedPersonAsync(state, interaction, callbackData["tracked:".Length..], nowUtc, ct);
        }

        return CreateModeCard(
            state,
            "That control is not available in OPINT-004-A.",
            callbackNotice: "Unsupported");
    }

    private async Task<TelegramOperatorResponse> EnterResolutionModeAsync(
        TelegramOperatorConversationState state,
        TelegramOperatorInteraction interaction,
        DateTime nowUtc,
        CancellationToken ct)
    {
        var previousActiveMode = state.Session.ActiveMode;
        var query = await _operatorResolutionService.QueryTrackedPersonsAsync(
            new OperatorTrackedPersonQueryRequest
            {
                OperatorIdentity = BuildAuthorizedIdentity(interaction, nowUtc),
                Session = CloneSession(state.Session),
                PreferredTrackedPersonId = state.Session.ActiveTrackedPersonId == Guid.Empty
                    ? null
                    : state.Session.ActiveTrackedPersonId,
                Limit = 25
            },
            ct);

        state.Session = query.Session;
        state.SurfaceMode = TelegramOperatorSurfaceModes.Resolution;
        UpdateActiveTrackedPersonState(state, query.ActiveTrackedPerson);

        if (!query.Accepted)
        {
            TelegramOperatorSessionStore.ClearResolutionContext(state);
            return CreateModeCard(
                state,
                $"Resolution mode could not start: {query.FailureReason ?? "unknown error"}.",
                callbackNotice: "Resolution unavailable");
        }

        if (query.ActiveTrackedPerson != null)
        {
            await AuditModeSwitchIfNeededAsync(
                interaction,
                state,
                previousActiveMode,
                selectionSource: query.SelectionSource,
                nowUtc,
                ct);
            return await RenderResolutionContextAsync(
                state,
                interaction,
                nowUtc,
                note: query.AutoSelected
                    ? $"Resolution mode auto-selected {query.ActiveTrackedPerson.DisplayName}."
                    : null,
                ct);
        }

        return BuildTrackedPersonPickerResponse(
            state,
            query.TrackedPersons,
            "Resolution mode requires an explicit active tracked person.",
            callbackNotice: "Pick tracked person");
    }

    private async Task<TelegramOperatorResponse> SelectTrackedPersonAsync(
        TelegramOperatorConversationState state,
        TelegramOperatorInteraction interaction,
        string trackedPersonValue,
        DateTime nowUtc,
        CancellationToken ct)
    {
        if (!Guid.TryParse(trackedPersonValue, out var trackedPersonId) || trackedPersonId == Guid.Empty)
        {
            return await RenderTrackedPersonPickerAsync(
                state,
                interaction,
                nowUtc,
                note: "Tracked person selection was invalid. Choose a listed person.",
                callbackNotice: "Invalid selection",
                ct);
        }

        var previousTrackedPersonId = state.Session.ActiveTrackedPersonId;
        var selection = await _operatorResolutionService.SelectTrackedPersonAsync(
            new OperatorTrackedPersonSelectionRequest
            {
                OperatorIdentity = BuildAuthorizedIdentity(interaction, nowUtc),
                Session = CloneSession(state.Session),
                TrackedPersonId = trackedPersonId,
                RequestedAtUtc = nowUtc
            },
            ct);

        state.Session = selection.Session;
        state.SurfaceMode = TelegramOperatorSurfaceModes.Resolution;
        UpdateActiveTrackedPersonState(state, selection.ActiveTrackedPerson);

        if (!selection.Accepted || selection.ActiveTrackedPerson == null)
        {
            return await RenderTrackedPersonPickerAsync(
                state,
                interaction,
                nowUtc,
                note: $"Tracked person switch failed: {selection.FailureReason ?? "unknown error"}.",
                callbackNotice: "Switch failed",
                ct);
        }

        _logger.LogInformation(
            "Telegram resolution context selected tracked person {TrackedPersonId} for chat {ChatId}. previous={PreviousTrackedPersonId}",
            selection.ActiveTrackedPerson.TrackedPersonId,
            interaction.ChatId,
            previousTrackedPersonId == Guid.Empty ? null : previousTrackedPersonId);

        return await RenderResolutionContextAsync(
            state,
            interaction,
            nowUtc,
            note: $"Resolution mode active for {selection.ActiveTrackedPerson.DisplayName}.",
            ct);
    }

    private async Task<TelegramOperatorResponse> RenderTrackedPersonPickerAsync(
        TelegramOperatorConversationState state,
        TelegramOperatorInteraction interaction,
        DateTime nowUtc,
        string? note,
        string? callbackNotice,
        CancellationToken ct)
    {
        var query = await _operatorResolutionService.QueryTrackedPersonsAsync(
            new OperatorTrackedPersonQueryRequest
            {
                OperatorIdentity = BuildAuthorizedIdentity(interaction, nowUtc),
                Session = CloneSession(state.Session),
                PreferredTrackedPersonId = state.Session.ActiveTrackedPersonId == Guid.Empty
                    ? null
                    : state.Session.ActiveTrackedPersonId,
                Limit = 25
            },
            ct);

        state.Session = query.Session;
        state.SurfaceMode = TelegramOperatorSurfaceModes.Resolution;
        UpdateActiveTrackedPersonState(state, query.ActiveTrackedPerson);

        if (!query.Accepted)
        {
            TelegramOperatorSessionStore.ClearResolutionContext(state);
            return CreateModeCard(
                state,
                $"Tracked person selection is unavailable: {query.FailureReason ?? "unknown error"}.",
                callbackNotice: "Selection unavailable");
        }

        return BuildTrackedPersonPickerResponse(state, query.TrackedPersons, note, callbackNotice);
    }

    private async Task<TelegramOperatorResponse> RenderResolutionContextAsync(
        TelegramOperatorConversationState state,
        TelegramOperatorInteraction interaction,
        DateTime nowUtc,
        string? note,
        CancellationToken ct)
    {
        if (state.Session.ActiveTrackedPersonId == Guid.Empty)
        {
            return await RenderTrackedPersonPickerAsync(
                state,
                interaction,
                nowUtc,
                note ?? "Resolution mode requires an active tracked person.",
                callbackNotice: "Pick tracked person",
                ct);
        }

        var queue = await _operatorResolutionService.GetResolutionQueueAsync(
            new OperatorResolutionQueueQueryRequest
            {
                OperatorIdentity = BuildAuthorizedIdentity(interaction, nowUtc),
                Session = CloneSession(state.Session),
                TrackedPersonId = state.Session.ActiveTrackedPersonId,
                SortBy = ResolutionQueueSortFields.Priority,
                SortDirection = ResolutionSortDirections.Desc,
                Limit = 10
            },
            ct);

        state.Session = queue.Session;
        state.SurfaceMode = TelegramOperatorSurfaceModes.Resolution;
        if (!string.IsNullOrWhiteSpace(queue.Queue.TrackedPersonDisplayName))
        {
            state.ActiveTrackedPersonDisplayName = queue.Queue.TrackedPersonDisplayName;
        }

        if (!queue.Accepted)
        {
            TelegramOperatorSessionStore.ClearResolutionContext(state);
            return await RenderTrackedPersonPickerAsync(
                state,
                interaction,
                nowUtc,
                note: $"Resolution context expired: {queue.FailureReason ?? "unknown error"}. Re-select the tracked person.",
                callbackNotice: "Resolution reset",
                ct);
        }

        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(note))
        {
            lines.Add(note.Trim());
            lines.Add(string.Empty);
        }

        lines.Add("Resolution Mode");
        lines.Add($"Active tracked person: {state.ActiveTrackedPersonDisplayName ?? "unknown"}");
        lines.Add($"Open items: {queue.Queue.TotalOpenCount}");
        lines.Add($"Visible in current summary: {queue.Queue.FilteredCount}");

        if (queue.Queue.ItemTypeCounts.Count > 0)
        {
            lines.Add($"Top item types: {FormatFacetCounts(queue.Queue.ItemTypeCounts)}");
        }

        if (queue.Queue.RuntimeState != null)
        {
            lines.Add($"Runtime: {queue.Queue.RuntimeState.State}");
        }

        lines.Add("Compact resolution cards and actions remain deferred to OPINT-004-B/C.");

        return new TelegramOperatorResponse
        {
            CallbackNotificationText = interaction.CallbackData == null ? null : "Resolution",
            Messages =
            [
                new TelegramOperatorMessage
                {
                    Text = string.Join(Environment.NewLine, lines),
                    Buttons =
                    [
                        [
                            new TelegramOperatorButton { Text = "Refresh", CallbackData = "resolution:refresh" },
                            new TelegramOperatorButton { Text = "Switch Person", CallbackData = "resolution:switch-person" }
                        ],
                        [
                            new TelegramOperatorButton { Text = "Modes", CallbackData = "mode:menu" }
                        ]
                    ]
                }
            ]
        };
    }

    private TelegramOperatorResponse BuildTrackedPersonPickerResponse(
        TelegramOperatorConversationState state,
        IReadOnlyList<OperatorTrackedPersonScopeSummary> trackedPersons,
        string? note,
        string? callbackNotice)
    {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(note))
        {
            lines.Add(note.Trim());
            lines.Add(string.Empty);
        }

        lines.Add("Resolution Mode");
        lines.Add($"Active tracked person: {state.ActiveTrackedPersonDisplayName ?? "none"}");
        lines.Add("Select the tracked person for this bounded resolution session.");

        var buttons = new List<List<TelegramOperatorButton>>();
        foreach (var person in trackedPersons.Take(12))
        {
            buttons.Add(
            [
                new TelegramOperatorButton
                {
                    Text = person.DisplayName,
                    CallbackData = $"tracked:{person.TrackedPersonId:D}"
                }
            ]);
        }

        buttons.Add(
        [
            new TelegramOperatorButton { Text = "Modes", CallbackData = "mode:menu" }
        ]);

        return new TelegramOperatorResponse
        {
            CallbackNotificationText = callbackNotice,
            Messages =
            [
                new TelegramOperatorMessage
                {
                    Text = string.Join(Environment.NewLine, lines),
                    Buttons = buttons
                }
            ]
        };
    }

    private TelegramOperatorResponse CreateModeCard(
        TelegramOperatorConversationState? state,
        string? note,
        string? callbackNotice = null)
    {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(note))
        {
            lines.Add(note.Trim());
            lines.Add(string.Empty);
        }

        lines.Add("Operator Cockpit");
        lines.Add($"Active tracked person: {state?.ActiveTrackedPersonDisplayName ?? "none"}");
        lines.Add("Choose a mode.");

        return new TelegramOperatorResponse
        {
            CallbackNotificationText = callbackNotice,
            Messages =
            [
                new TelegramOperatorMessage
                {
                    Text = string.Join(Environment.NewLine, lines),
                    Buttons =
                    [
                        [
                            new TelegramOperatorButton { Text = "Assistant", CallbackData = "mode:assistant" },
                            new TelegramOperatorButton { Text = "Resolution", CallbackData = "mode:resolution" }
                        ],
                        [
                            new TelegramOperatorButton { Text = "Offline Event", CallbackData = "mode:offline_event" },
                            new TelegramOperatorButton { Text = "Alerts", CallbackData = "mode:alerts" }
                        ]
                    ]
                }
            ]
        };
    }

    private async Task<TelegramOperatorConversationState> GetOrCreateAuthorizedStateAsync(
        TelegramOperatorInteraction interaction,
        DateTime nowUtc,
        CancellationToken ct)
    {
        var state = _sessionStore.GetOrAdd(
            interaction.ChatId,
            () => CreateFreshState(interaction, nowUtc));

        var createdNewSession = false;
        if (state.UserId != 0 && state.UserId != interaction.UserId)
        {
            state = CreateFreshState(interaction, nowUtc);
            createdNewSession = true;
        }
        else if (state.Session.ExpiresAtUtc.HasValue && state.Session.ExpiresAtUtc.Value <= nowUtc)
        {
            state = CreateFreshState(interaction, nowUtc);
            createdNewSession = true;
        }
        else if (string.IsNullOrWhiteSpace(state.Session.OperatorSessionId))
        {
            state = CreateFreshState(interaction, nowUtc);
            createdNewSession = true;
        }

        if (createdNewSession || state.Session.AuthenticatedAtUtc == nowUtc)
        {
            await _auditService.RecordSessionEventAsync(
                new OperatorSessionAuditRequest
                {
                    RequestId = BuildRequestId("session-authenticated", interaction.ChatId, nowUtc),
                    SessionEventType = SessionAuthenticatedEventType,
                    DecisionOutcome = OperatorAuditDecisionOutcomes.Accepted,
                    OperatorIdentity = BuildAuthorizedIdentity(interaction, nowUtc),
                    Session = CloneSession(state.Session),
                    Details =
                    {
                        ["chat_id"] = interaction.ChatId,
                        ["surface_mode"] = state.SurfaceMode,
                        ["restored_from_existing_state"] = !createdNewSession
                    },
                    EventTimeUtc = nowUtc
                },
                ct);
        }

        return state;
    }

    private async Task AuditModeSwitchIfNeededAsync(
        TelegramOperatorInteraction interaction,
        TelegramOperatorConversationState state,
        string previousActiveMode,
        string? selectionSource,
        DateTime nowUtc,
        CancellationToken ct)
    {
        if (string.Equals(previousActiveMode, state.Session.ActiveMode, StringComparison.Ordinal)
            && state.SurfaceMode == TelegramOperatorSurfaceModes.Resolution)
        {
            return;
        }

        await _auditService.RecordSessionEventAsync(
            new OperatorSessionAuditRequest
            {
                RequestId = BuildRequestId("mode-switch", interaction.ChatId, nowUtc),
                SessionEventType = ModeSwitchSessionEventType,
                DecisionOutcome = OperatorAuditDecisionOutcomes.Accepted,
                ScopeKey = null,
                TrackedPersonId = state.Session.ActiveTrackedPersonId == Guid.Empty ? null : state.Session.ActiveTrackedPersonId,
                OperatorIdentity = BuildAuthorizedIdentity(interaction, nowUtc),
                Session = CloneSession(state.Session),
                Details =
                {
                    ["previous_active_mode"] = string.IsNullOrWhiteSpace(previousActiveMode) ? null : previousActiveMode,
                    ["resulting_active_mode"] = state.Session.ActiveMode,
                    ["selection_source"] = selectionSource
                },
                EventTimeUtc = nowUtc
            },
            ct);
    }

    private async Task AuditUnauthorizedInteractionAsync(
        TelegramOperatorInteraction interaction,
        DateTime nowUtc,
        CancellationToken ct)
    {
        var deniedSession = new OperatorSessionContext
        {
            OperatorSessionId = $"telegram-denied:{interaction.ChatId}:{interaction.UserId}",
            Surface = OperatorSurfaceTypes.Telegram,
            AuthenticatedAtUtc = nowUtc,
            LastSeenAtUtc = nowUtc,
            ActiveMode = "unknown"
        };

        await _auditService.RecordSessionEventAsync(
            new OperatorSessionAuditRequest
            {
                RequestId = BuildRequestId("auth-denied", interaction.ChatId, nowUtc),
                SessionEventType = AuthDeniedSessionEventType,
                DecisionOutcome = OperatorAuditDecisionOutcomes.Denied,
                FailureReason = !interaction.IsPrivateChat
                    ? "telegram_private_chat_required"
                    : "telegram_owner_not_authorized",
                OperatorIdentity = new OperatorIdentityContext
                {
                    OperatorId = interaction.UserId > 0 ? $"telegram-user:{interaction.UserId}" : "telegram-user:unknown",
                    OperatorDisplay = interaction.UserDisplayName ?? $"Telegram User {interaction.UserId}",
                    SurfaceSubject = $"telegram:{interaction.UserId}",
                    AuthSource = TelegramAuthSource,
                    AuthTimeUtc = nowUtc
                },
                Session = deniedSession,
                Details =
                {
                    ["chat_id"] = interaction.ChatId,
                    ["user_id"] = interaction.UserId,
                    ["is_private_chat"] = interaction.IsPrivateChat,
                    ["callback_data"] = interaction.CallbackData,
                    ["message_text"] = interaction.MessageText
                },
                EventTimeUtc = nowUtc
            },
            ct);

        _logger.LogWarning(
            "Unauthorized Telegram operator interaction denied. chat_id={ChatId}, user_id={UserId}, private_chat={IsPrivateChat}",
            interaction.ChatId,
            interaction.UserId,
            interaction.IsPrivateChat);
    }

    private bool IsAuthorized(TelegramOperatorInteraction interaction)
        => interaction.IsPrivateChat
            && _settings.OwnerUserId > 0
            && interaction.UserId > 0
            && interaction.UserId == _settings.OwnerUserId;

    private TelegramOperatorConversationState CreateFreshState(TelegramOperatorInteraction interaction, DateTime nowUtc)
    {
        return new TelegramOperatorConversationState
        {
            ChatId = interaction.ChatId,
            UserId = interaction.UserId,
            SurfaceMode = TelegramOperatorSurfaceModes.None,
            Session = new OperatorSessionContext
            {
                OperatorSessionId = $"telegram:{interaction.ChatId}:{Guid.NewGuid():N}",
                Surface = OperatorSurfaceTypes.Telegram,
                AuthenticatedAtUtc = nowUtc,
                LastSeenAtUtc = nowUtc,
                ExpiresAtUtc = nowUtc.Add(SessionLifetime),
                ActiveMode = string.Empty
            }
        };
    }

    private OperatorIdentityContext BuildAuthorizedIdentity(TelegramOperatorInteraction interaction, DateTime nowUtc)
    {
        return new OperatorIdentityContext
        {
            OperatorId = $"telegram-owner:{_settings.OwnerUserId}",
            OperatorDisplay = interaction.UserDisplayName ?? $"Telegram Owner {_settings.OwnerUserId}",
            SurfaceSubject = $"telegram:{interaction.UserId}",
            AuthSource = TelegramAuthSource,
            AuthTimeUtc = nowUtc
        };
    }

    private static OperatorSessionContext CloneSession(OperatorSessionContext session)
    {
        return new OperatorSessionContext
        {
            OperatorSessionId = session.OperatorSessionId,
            Surface = session.Surface,
            AuthenticatedAtUtc = session.AuthenticatedAtUtc,
            LastSeenAtUtc = session.LastSeenAtUtc,
            ExpiresAtUtc = session.ExpiresAtUtc,
            ActiveTrackedPersonId = session.ActiveTrackedPersonId,
            ActiveScopeItemKey = session.ActiveScopeItemKey,
            ActiveMode = session.ActiveMode,
            UnfinishedStep = session.UnfinishedStep == null
                ? null
                : new OperatorWorkflowStepContext
                {
                    StepKind = session.UnfinishedStep.StepKind,
                    StepState = session.UnfinishedStep.StepState,
                    StartedAtUtc = session.UnfinishedStep.StartedAtUtc,
                    BoundTrackedPersonId = session.UnfinishedStep.BoundTrackedPersonId,
                    BoundScopeItemKey = session.UnfinishedStep.BoundScopeItemKey
                }
        };
    }

    private static void UpdateActiveTrackedPersonState(
        TelegramOperatorConversationState state,
        OperatorTrackedPersonScopeSummary? activeTrackedPerson)
    {
        if (activeTrackedPerson == null)
        {
            if (state.Session.ActiveTrackedPersonId == Guid.Empty)
            {
                state.ActiveTrackedPersonDisplayName = null;
            }

            return;
        }

        state.ActiveTrackedPersonDisplayName = activeTrackedPerson.DisplayName;
    }

    private static string BuildRequestId(string prefix, long chatId, DateTime nowUtc)
        => $"{prefix}:{chatId}:{nowUtc:yyyyMMddHHmmssfff}";

    private static string FormatFacetCounts(IEnumerable<ResolutionFacetCount> counts)
    {
        return string.Join(
            ", ",
            counts
                .OrderByDescending(x => x.Count)
                .ThenBy(x => x.Key, StringComparer.Ordinal)
                .Take(3)
                .Select(x => $"{x.Key} {x.Count}"));
    }

    private static TelegramOperatorMessage CreateMessage(string text)
        => new() { Text = text };
}
