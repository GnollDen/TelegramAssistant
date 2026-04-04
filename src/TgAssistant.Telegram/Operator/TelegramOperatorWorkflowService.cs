using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Telegram.Operator;

public sealed class TelegramOperatorWorkflowService
{
    private const string AuthDeniedSessionEventType = "auth_denied";
    private const string SessionAuthenticatedEventType = "session_authenticated";
    private const string ModeSwitchSessionEventType = "mode_switch";
    private const string AlertAcknowledgedSessionEventType = "alert_acknowledged";
    private const string TelegramAuthSource = "telegram_owner_allowlist";
    private const string TrackedPersonCallbackPrefix = "tracked:";
    private const string AssistantTrackedPersonCallbackPrefix = "assistant:tracked:";
    private const string AlertsTrackedPersonCallbackPrefix = "alerts:tracked:";
    private const string OfflineTrackedPersonCallbackPrefix = "offline:tracked:";
    private const string AssistantSwitchPersonCallback = "assistant:switch-person";
    private const string AlertsSwitchPersonCallback = "alerts:switch-person";
    private const string OfflineSwitchPersonCallback = "offline:switch-person";
    private const string AlertAcknowledgeCallbackPrefix = "alert:ack:";
    private const string OfflineCaptureSummaryCallback = "offline:capture-summary";
    private const string OfflineCaptureRecordingCallback = "offline:capture-recording";
    private const string OfflineClarifyNextCallback = "offline:clarify-next";
    private const string OfflineSaveDraftCallback = "offline:save";
    private const string OfflineSaveFinalCallback = "offline:save-final";
    private const string ResolutionActionCallbackPrefix = "ra:";
    private const string ResolutionCancelInputCallback = "resolution:cancel-input";
    private const string OfflineCancelInputCallback = "offline:cancel-input";
    private const string PendingActionInputStepKind = "resolution_action_input";
    private const string PendingOfflineEventInputStepKind = "offline_event_input";
    private const string OfflineEventInputKindSummary = "summary";
    private const string OfflineEventInputKindRecordingReference = "recording_reference";
    private const string OfflineEventInputKindClarificationAnswer = "clarification_answer";
    private const int MaxResolutionCards = 3;
    private const int MaxExplanationLength = 1000;
    private const int MaxAssistantQuestionLength = 1000;
    private const int MaxOfflineEventSummaryLength = 2000;
    private const int MaxOfflineEventRecordingReferenceLength = 1000;
    private const int EvidencePreviewLimit = 3;
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(8);

    private readonly TelegramSettings _settings;
    private readonly WebSettings _webSettings;
    private readonly TelegramOperatorSessionStore _sessionStore;
    private readonly IOperatorResolutionApplicationService _operatorResolutionService;
    private readonly IOperatorAlertPolicyService _operatorAlertPolicyService;
    private readonly IOperatorAssistantResponseGenerationService _assistantResponseGenerationService;
    private readonly IOperatorAssistantContextAssemblyService _assistantContextAssemblyService;
    private readonly IOperatorOfflineEventRepository _operatorOfflineEventRepository;
    private readonly OfflineEventClarificationPolicy _offlineEventClarificationPolicy;
    private readonly IOperatorSessionAuditService _auditService;
    private readonly ILogger<TelegramOperatorWorkflowService> _logger;

    public TelegramOperatorWorkflowService(
        IOptions<TelegramSettings> settings,
        IOptions<WebSettings> webSettings,
        TelegramOperatorSessionStore sessionStore,
        IOperatorResolutionApplicationService operatorResolutionService,
        IOperatorAlertPolicyService operatorAlertPolicyService,
        IOperatorAssistantResponseGenerationService assistantResponseGenerationService,
        IOperatorAssistantContextAssemblyService assistantContextAssemblyService,
        IOperatorOfflineEventRepository operatorOfflineEventRepository,
        OfflineEventClarificationPolicy offlineEventClarificationPolicy,
        IOperatorSessionAuditService auditService,
        ILogger<TelegramOperatorWorkflowService> logger)
    {
        _settings = settings.Value;
        _webSettings = webSettings.Value;
        _sessionStore = sessionStore;
        _operatorResolutionService = operatorResolutionService;
        _operatorAlertPolicyService = operatorAlertPolicyService;
        _assistantResponseGenerationService = assistantResponseGenerationService;
        _assistantContextAssemblyService = assistantContextAssemblyService;
        _operatorOfflineEventRepository = operatorOfflineEventRepository;
        _offlineEventClarificationPolicy = offlineEventClarificationPolicy;
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
            state.PendingResolutionInput = null;
            state.PendingOfflineEventInput = null;
            state.Session.UnfinishedStep = null;
            return CreateModeCard(state, note: null);
        }

        if (string.Equals(text, "/resolution", StringComparison.OrdinalIgnoreCase))
        {
            return await EnterResolutionModeAsync(state, interaction, nowUtc, ct);
        }

        if (string.Equals(text, "/assistant", StringComparison.OrdinalIgnoreCase))
        {
            return await EnterAssistantModeAsync(state, interaction, nowUtc, ct);
        }

        if (string.Equals(text, "/cancel", StringComparison.OrdinalIgnoreCase))
        {
            if (state.PendingOfflineEventInput != null)
            {
                return await CancelPendingOfflineEventInputAsync(state, interaction, nowUtc, ct);
            }

            return await CancelPendingResolutionInputAsync(state, interaction, nowUtc, ct);
        }

        if (state.PendingResolutionInput != null)
        {
            return await SubmitPendingResolutionInputAsync(state, interaction, text, nowUtc, ct);
        }

        if (state.PendingOfflineEventInput != null)
        {
            return await SubmitPendingOfflineEventInputAsync(state, interaction, text, nowUtc, ct);
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

        if (state.SurfaceMode == TelegramOperatorSurfaceModes.Assistant)
        {
            return await SubmitAssistantQuestionAsync(state, interaction, text, nowUtc, ct);
        }

        if (state.SurfaceMode == TelegramOperatorSurfaceModes.OfflineEvent)
        {
            return await RenderOfflineEventContextAsync(
                state,
                interaction,
                nowUtc,
                note: "Use the offline controls to capture summary, answer clarification questions, then finalize save.",
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
            if (state.PendingOfflineEventInput != null)
            {
                return CreatePendingOfflineEventInputPrompt(
                    state,
                    "An offline-event input step is in progress. Send text or choose Cancel.");
            }

            state.SurfaceMode = TelegramOperatorSurfaceModes.None;
            state.ResolutionCardBindings.Clear();
            state.PendingResolutionInput = null;
            state.Session.UnfinishedStep = null;
            return CreateModeCard(state, note: null, callbackNotice: "Modes");
        }

        if (string.Equals(callbackData, "mode:resolution", StringComparison.Ordinal))
        {
            if (state.PendingResolutionInput != null)
            {
                return CreatePendingResolutionInputPrompt(
                    state,
                    "A resolution action input is in progress. Send explanation text or choose Cancel.");
            }

            if (state.PendingOfflineEventInput != null)
            {
                return CreatePendingOfflineEventInputPrompt(
                    state,
                    "An offline-event input step is in progress. Send text or choose Cancel.");
            }

            return await EnterResolutionModeAsync(state, interaction, nowUtc, ct);
        }

        if (string.Equals(callbackData, "mode:assistant", StringComparison.Ordinal))
        {
            if (state.PendingResolutionInput != null)
            {
                return CreatePendingResolutionInputPrompt(
                    state,
                    "A resolution action input is in progress. Send explanation text or choose Cancel.");
            }

            if (state.PendingOfflineEventInput != null)
            {
                return CreatePendingOfflineEventInputPrompt(
                    state,
                    "An offline-event input step is in progress. Send text or choose Cancel.");
            }

            return await EnterAssistantModeAsync(state, interaction, nowUtc, ct);
        }

        if (string.Equals(callbackData, "mode:offline_event", StringComparison.Ordinal))
        {
            if (state.PendingResolutionInput != null)
            {
                return CreatePendingResolutionInputPrompt(
                    state,
                    "A resolution action input is in progress. Send explanation text or choose Cancel.");
            }

            if (state.PendingOfflineEventInput != null)
            {
                return CreatePendingOfflineEventInputPrompt(
                    state,
                    "An offline-event input step is in progress. Send text or choose Cancel.");
            }

            return await EnterOfflineEventModeAsync(state, interaction, nowUtc, ct);
        }

        if (string.Equals(callbackData, "mode:alerts", StringComparison.Ordinal))
        {
            if (state.PendingResolutionInput != null)
            {
                return CreatePendingResolutionInputPrompt(
                    state,
                    "A resolution action input is in progress. Send explanation text or choose Cancel.");
            }

            if (state.PendingOfflineEventInput != null)
            {
                return CreatePendingOfflineEventInputPrompt(
                    state,
                    "An offline-event input step is in progress. Send text or choose Cancel.");
            }

            return await EnterAlertsModeAsync(state, interaction, nowUtc, ct);
        }

        if (string.Equals(callbackData, "resolution:switch-person", StringComparison.Ordinal))
        {
            if (state.PendingResolutionInput != null)
            {
                return CreatePendingResolutionInputPrompt(
                    state,
                    "Finish or cancel the pending action input before switching tracked person.");
            }

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
            if (state.PendingResolutionInput != null)
            {
                return CreatePendingResolutionInputPrompt(
                    state,
                    "Finish or cancel the pending action input before refreshing the queue.");
            }

            return await RenderResolutionContextAsync(state, interaction, nowUtc, note: null, ct);
        }

        if (string.Equals(callbackData, AssistantSwitchPersonCallback, StringComparison.Ordinal))
        {
            if (state.PendingResolutionInput != null)
            {
                return CreatePendingResolutionInputPrompt(
                    state,
                    "Finish or cancel the pending action input before switching tracked person.");
            }

            return await RenderAssistantTrackedPersonPickerAsync(
                state,
                interaction,
                nowUtc,
                note: "Select the active tracked person for Assistant mode.",
                callbackNotice: "Switch tracked person",
                ct);
        }

        if (string.Equals(callbackData, AlertsSwitchPersonCallback, StringComparison.Ordinal))
        {
            if (state.PendingResolutionInput != null)
            {
                return CreatePendingResolutionInputPrompt(
                    state,
                    "Finish or cancel the pending action input before switching tracked person.");
            }

            if (state.PendingOfflineEventInput != null)
            {
                return CreatePendingOfflineEventInputPrompt(
                    state,
                    "Finish or cancel the pending offline-event input before switching tracked person.");
            }

            return await RenderAlertsTrackedPersonPickerAsync(
                state,
                interaction,
                nowUtc,
                note: "Select the active tracked person for Alerts mode.",
                callbackNotice: "Switch tracked person",
                ct);
        }

        if (string.Equals(callbackData, OfflineSwitchPersonCallback, StringComparison.Ordinal))
        {
            if (state.PendingOfflineEventInput != null)
            {
                return CreatePendingOfflineEventInputPrompt(
                    state,
                    "Finish or cancel the pending offline-event input before switching tracked person.");
            }

            return await RenderOfflineTrackedPersonPickerAsync(
                state,
                interaction,
                nowUtc,
                note: "Select the active tracked person for Offline Event mode.",
                callbackNotice: "Switch tracked person",
                ct);
        }

        if (string.Equals(callbackData, OfflineCaptureSummaryCallback, StringComparison.Ordinal))
        {
            return await StartPendingOfflineEventInputAsync(
                state,
                interaction,
                OfflineEventInputKindSummary,
                nowUtc,
                ct);
        }

        if (string.Equals(callbackData, OfflineCaptureRecordingCallback, StringComparison.Ordinal))
        {
            return await StartPendingOfflineEventInputAsync(
                state,
                interaction,
                OfflineEventInputKindRecordingReference,
                nowUtc,
                ct);
        }

        if (string.Equals(callbackData, OfflineSaveDraftCallback, StringComparison.Ordinal))
        {
            return await SaveOfflineEventDraftAsync(state, interaction, nowUtc, ct);
        }

        if (string.Equals(callbackData, OfflineClarifyNextCallback, StringComparison.Ordinal))
        {
            return await StartPendingOfflineClarificationAnswerAsync(state, interaction, nowUtc, ct);
        }

        if (string.Equals(callbackData, OfflineSaveFinalCallback, StringComparison.Ordinal))
        {
            return await SaveOfflineEventFinalAsync(state, interaction, nowUtc, ct);
        }

        if (string.Equals(callbackData, ResolutionCancelInputCallback, StringComparison.Ordinal))
        {
            return await CancelPendingResolutionInputAsync(state, interaction, nowUtc, ct);
        }

        if (string.Equals(callbackData, OfflineCancelInputCallback, StringComparison.Ordinal))
        {
            return await CancelPendingOfflineEventInputAsync(state, interaction, nowUtc, ct);
        }

        if (callbackData.StartsWith(TrackedPersonCallbackPrefix, StringComparison.Ordinal))
        {
            if (state.PendingResolutionInput != null)
            {
                return CreatePendingResolutionInputPrompt(
                    state,
                    "Finish or cancel the pending action input before changing tracked person.");
            }

            return await SelectTrackedPersonAsync(state, interaction, callbackData[TrackedPersonCallbackPrefix.Length..], nowUtc, ct);
        }

        if (callbackData.StartsWith(AssistantTrackedPersonCallbackPrefix, StringComparison.Ordinal))
        {
            if (state.PendingResolutionInput != null)
            {
                return CreatePendingResolutionInputPrompt(
                    state,
                    "Finish or cancel the pending action input before changing tracked person.");
            }

            return await SelectTrackedPersonForAssistantAsync(
                state,
                interaction,
                callbackData[AssistantTrackedPersonCallbackPrefix.Length..],
                nowUtc,
                ct);
        }

        if (callbackData.StartsWith(AlertsTrackedPersonCallbackPrefix, StringComparison.Ordinal))
        {
            if (state.PendingResolutionInput != null)
            {
                return CreatePendingResolutionInputPrompt(
                    state,
                    "Finish or cancel the pending action input before changing tracked person.");
            }

            if (state.PendingOfflineEventInput != null)
            {
                return CreatePendingOfflineEventInputPrompt(
                    state,
                    "Finish or cancel the pending offline-event input before changing tracked person.");
            }

            return await SelectTrackedPersonForAlertsAsync(
                state,
                interaction,
                callbackData[AlertsTrackedPersonCallbackPrefix.Length..],
                nowUtc,
                ct);
        }

        if (callbackData.StartsWith(OfflineTrackedPersonCallbackPrefix, StringComparison.Ordinal))
        {
            if (state.PendingOfflineEventInput != null)
            {
                return CreatePendingOfflineEventInputPrompt(
                    state,
                    "Finish or cancel the pending offline-event input before changing tracked person.");
            }

            return await SelectTrackedPersonForOfflineAsync(
                state,
                interaction,
                callbackData[OfflineTrackedPersonCallbackPrefix.Length..],
                nowUtc,
                ct);
        }

        if (callbackData.StartsWith(ResolutionActionCallbackPrefix, StringComparison.Ordinal))
        {
            return await HandleResolutionActionCallbackAsync(
                state,
                interaction,
                callbackData[ResolutionActionCallbackPrefix.Length..],
                nowUtc,
                ct);
        }

        if (callbackData.StartsWith(AlertAcknowledgeCallbackPrefix, StringComparison.Ordinal))
        {
            return await AcknowledgeAlertAsync(
                state,
                interaction,
                callbackData[AlertAcknowledgeCallbackPrefix.Length..],
                nowUtc,
                ct);
        }

        return CreateModeCard(
            state,
            "That control is not available in the current Telegram workflow slice.",
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

    private async Task<TelegramOperatorResponse> EnterAssistantModeAsync(
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
        state.SurfaceMode = TelegramOperatorSurfaceModes.Assistant;
        state.Session.ActiveMode = OperatorModeTypes.Assistant;
        UpdateActiveTrackedPersonState(state, query.ActiveTrackedPerson);

        if (!query.Accepted)
        {
            TelegramOperatorSessionStore.ClearResolutionContext(state);
            return CreateModeCard(
                state,
                $"Assistant mode could not start: {query.FailureReason ?? "unknown error"}.",
                callbackNotice: "Assistant unavailable");
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
            return CreateAssistantReadyCard(
                state,
                note: query.AutoSelected
                    ? $"Assistant mode auto-selected {query.ActiveTrackedPerson.DisplayName}."
                    : null,
                callbackNotice: "Assistant");
        }

        return BuildAssistantTrackedPersonPickerResponse(
            state,
            query.TrackedPersons,
            "Assistant mode requires an explicit active tracked person.",
            callbackNotice: "Pick tracked person");
    }

    private async Task<TelegramOperatorResponse> EnterAlertsModeAsync(
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
        state.SurfaceMode = TelegramOperatorSurfaceModes.Alerts;
        state.Session.ActiveMode = OperatorModeTypes.ResolutionQueue;
        UpdateActiveTrackedPersonState(state, query.ActiveTrackedPerson);

        if (!query.Accepted)
        {
            TelegramOperatorSessionStore.ClearResolutionContext(state);
            return CreateModeCard(
                state,
                $"Alerts mode could not start: {query.FailureReason ?? "unknown error"}.",
                callbackNotice: "Alerts unavailable");
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
            return await RenderAlertsContextAsync(
                state,
                interaction,
                nowUtc,
                note: query.AutoSelected
                    ? $"Alerts mode auto-selected {query.ActiveTrackedPerson.DisplayName}."
                    : null,
                ct);
        }

        return BuildAlertsTrackedPersonPickerResponse(
            state,
            query.TrackedPersons,
            "Alerts mode requires an explicit active tracked person.",
            callbackNotice: "Pick tracked person");
    }

    private async Task<TelegramOperatorResponse> EnterOfflineEventModeAsync(
        TelegramOperatorConversationState state,
        TelegramOperatorInteraction interaction,
        DateTime nowUtc,
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
        state.SurfaceMode = TelegramOperatorSurfaceModes.OfflineEvent;
        state.Session.ActiveMode = OperatorModeTypes.OfflineEvent;
        UpdateActiveTrackedPersonState(state, query.ActiveTrackedPerson);

        if (!query.Accepted)
        {
            TelegramOperatorSessionStore.ClearResolutionContext(state);
            return CreateModeCard(
                state,
                $"Offline Event mode could not start: {query.FailureReason ?? "unknown error"}.",
                callbackNotice: "Offline Event unavailable");
        }

        if (query.ActiveTrackedPerson != null)
        {
            EnsureOfflineDraftContext(state, nowUtc);
            return await RenderOfflineEventContextAsync(
                state,
                interaction,
                nowUtc,
                note: query.AutoSelected
                    ? $"Offline Event mode auto-selected {query.ActiveTrackedPerson.DisplayName}."
                    : null,
                ct);
        }

        return BuildOfflineTrackedPersonPickerResponse(
            state,
            query.TrackedPersons,
            "Offline Event mode requires an explicit active tracked person.",
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

    private async Task<TelegramOperatorResponse> SelectTrackedPersonForAssistantAsync(
        TelegramOperatorConversationState state,
        TelegramOperatorInteraction interaction,
        string trackedPersonValue,
        DateTime nowUtc,
        CancellationToken ct)
    {
        if (!Guid.TryParse(trackedPersonValue, out var trackedPersonId) || trackedPersonId == Guid.Empty)
        {
            return await RenderAssistantTrackedPersonPickerAsync(
                state,
                interaction,
                nowUtc,
                note: "Tracked person selection was invalid. Choose a listed person.",
                callbackNotice: "Invalid selection",
                ct);
        }

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
        state.SurfaceMode = TelegramOperatorSurfaceModes.Assistant;
        state.Session.ActiveMode = OperatorModeTypes.Assistant;
        UpdateActiveTrackedPersonState(state, selection.ActiveTrackedPerson);

        if (!selection.Accepted || selection.ActiveTrackedPerson == null)
        {
            return await RenderAssistantTrackedPersonPickerAsync(
                state,
                interaction,
                nowUtc,
                note: $"Tracked person switch failed: {selection.FailureReason ?? "unknown error"}.",
                callbackNotice: "Switch failed",
                ct);
        }

        return CreateAssistantReadyCard(
            state,
            note: $"Assistant mode active for {selection.ActiveTrackedPerson.DisplayName}.",
            callbackNotice: "Assistant");
    }

    private async Task<TelegramOperatorResponse> SelectTrackedPersonForAlertsAsync(
        TelegramOperatorConversationState state,
        TelegramOperatorInteraction interaction,
        string trackedPersonValue,
        DateTime nowUtc,
        CancellationToken ct)
    {
        if (!Guid.TryParse(trackedPersonValue, out var trackedPersonId) || trackedPersonId == Guid.Empty)
        {
            return await RenderAlertsTrackedPersonPickerAsync(
                state,
                interaction,
                nowUtc,
                note: "Tracked person selection was invalid. Choose a listed person.",
                callbackNotice: "Invalid selection",
                ct);
        }

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
        state.SurfaceMode = TelegramOperatorSurfaceModes.Alerts;
        state.Session.ActiveMode = OperatorModeTypes.ResolutionQueue;
        UpdateActiveTrackedPersonState(state, selection.ActiveTrackedPerson);

        if (!selection.Accepted || selection.ActiveTrackedPerson == null)
        {
            return await RenderAlertsTrackedPersonPickerAsync(
                state,
                interaction,
                nowUtc,
                note: $"Tracked person switch failed: {selection.FailureReason ?? "unknown error"}.",
                callbackNotice: "Switch failed",
                ct);
        }

        return await RenderAlertsContextAsync(
            state,
            interaction,
            nowUtc,
            note: $"Alerts mode active for {selection.ActiveTrackedPerson.DisplayName}.",
            ct);
    }

    private async Task<TelegramOperatorResponse> SelectTrackedPersonForOfflineAsync(
        TelegramOperatorConversationState state,
        TelegramOperatorInteraction interaction,
        string trackedPersonValue,
        DateTime nowUtc,
        CancellationToken ct)
    {
        if (!Guid.TryParse(trackedPersonValue, out var trackedPersonId) || trackedPersonId == Guid.Empty)
        {
            return await RenderOfflineTrackedPersonPickerAsync(
                state,
                interaction,
                nowUtc,
                note: "Tracked person selection was invalid. Choose a listed person.",
                callbackNotice: "Invalid selection",
                ct);
        }

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
        state.SurfaceMode = TelegramOperatorSurfaceModes.OfflineEvent;
        state.Session.ActiveMode = OperatorModeTypes.OfflineEvent;
        UpdateActiveTrackedPersonState(state, selection.ActiveTrackedPerson);

        if (!selection.Accepted || selection.ActiveTrackedPerson == null)
        {
            return await RenderOfflineTrackedPersonPickerAsync(
                state,
                interaction,
                nowUtc,
                note: $"Tracked person switch failed: {selection.FailureReason ?? "unknown error"}.",
                callbackNotice: "Switch failed",
                ct);
        }

        state.PendingOfflineEventInput = null;
        state.Session.UnfinishedStep = null;
        state.OfflineEventDraft = null;
        EnsureOfflineDraftContext(state, nowUtc);

        return await RenderOfflineEventContextAsync(
            state,
            interaction,
            nowUtc,
            note: $"Offline Event mode active for {selection.ActiveTrackedPerson.DisplayName}.",
            ct);
    }

    private async Task<TelegramOperatorResponse> HandleResolutionActionCallbackAsync(
        TelegramOperatorConversationState state,
        TelegramOperatorInteraction interaction,
        string actionPayload,
        DateTime nowUtc,
        CancellationToken ct)
    {
        if (state.PendingResolutionInput != null)
        {
            return CreatePendingResolutionInputPrompt(
                state,
                "A resolution action input is in progress. Send explanation text or choose Cancel.");
        }

        var separatorIndex = actionPayload.IndexOf(':', StringComparison.Ordinal);
        if (separatorIndex <= 0 || separatorIndex == actionPayload.Length - 1)
        {
            return await RenderResolutionContextAsync(
                state,
                interaction,
                nowUtc,
                note: "Resolution action callback was invalid. Refresh the queue and retry.",
                ct);
        }

        var actionType = ResolutionActionTypes.Normalize(actionPayload[..separatorIndex]);
        var cardToken = actionPayload[(separatorIndex + 1)..].Trim();
        if (!ResolutionActionTypes.IsSupported(actionType))
        {
            return await RenderResolutionContextAsync(
                state,
                interaction,
                nowUtc,
                note: "That action is not supported by the bounded resolution contract.",
                ct);
        }

        if (!state.ResolutionCardBindings.TryGetValue(cardToken, out var binding))
        {
            return await RenderResolutionContextAsync(
                state,
                interaction,
                nowUtc,
                note: "That card is no longer current. Refresh the queue and retry.",
                ct);
        }

        if (!binding.AvailableActions.Contains(actionType, StringComparer.Ordinal))
        {
            return await RenderResolutionContextAsync(
                state,
                interaction,
                nowUtc,
                note: $"{FormatActionLabel(actionType)} is not available for this resolution item.",
                ct);
        }

        if (string.Equals(actionType, ResolutionActionTypes.Approve, StringComparison.Ordinal))
        {
            return await SubmitApproveActionAsync(state, interaction, binding, nowUtc, ct);
        }

        if (actionType is ResolutionActionTypes.Reject or ResolutionActionTypes.Defer or ResolutionActionTypes.Clarify)
        {
            return await StartPendingResolutionInputAsync(state, interaction, binding, actionType, nowUtc, ct);
        }

        if (string.Equals(actionType, ResolutionActionTypes.Evidence, StringComparison.Ordinal))
        {
            return await RenderEvidencePreviewAsync(state, interaction, binding, nowUtc, ct);
        }

        if (string.Equals(actionType, ResolutionActionTypes.OpenWeb, StringComparison.Ordinal))
        {
            return await RenderOpenWebHandoffAsync(state, interaction, binding, nowUtc, ct);
        }

        return await RenderResolutionContextAsync(
            state,
            interaction,
            nowUtc,
            note: $"{FormatActionLabel(actionType)} is not currently available in Telegram resolution mode.",
            ct);
    }

    private async Task<TelegramOperatorResponse> StartPendingResolutionInputAsync(
        TelegramOperatorConversationState state,
        TelegramOperatorInteraction interaction,
        TelegramResolutionCardBinding binding,
        string actionType,
        DateTime nowUtc,
        CancellationToken ct)
    {
        var detail = await _operatorResolutionService.GetResolutionDetailAsync(
            new OperatorResolutionDetailQueryRequest
            {
                OperatorIdentity = BuildAuthorizedIdentity(interaction, nowUtc),
                Session = CloneSession(state.Session),
                TrackedPersonId = state.Session.ActiveTrackedPersonId,
                ScopeItemKey = binding.ScopeItemKey,
                EvidenceLimit = 1,
                EvidenceSortBy = ResolutionEvidenceSortFields.ObservedAt,
                EvidenceSortDirection = ResolutionSortDirections.Desc
            },
            ct);

        state.Session = detail.Session;
        state.SurfaceMode = TelegramOperatorSurfaceModes.Resolution;
        if (!detail.Accepted || detail.Detail.Item == null)
        {
            return await RenderResolutionContextAsync(
                state,
                interaction,
                nowUtc,
                note: $"{FormatActionLabel(actionType)} could not bind the current item: {detail.FailureReason ?? "resolution item unavailable"}.",
                ct);
        }

        state.PendingResolutionInput = new TelegramPendingResolutionInput
        {
            ActionType = actionType,
            ScopeItemKey = binding.ScopeItemKey,
            ItemType = detail.Detail.Item.ItemType,
            ItemTitle = detail.Detail.Item.Title,
            StartedAtUtc = nowUtc,
            BoundTrackedPersonId = state.Session.ActiveTrackedPersonId
        };
        state.Session.ActiveScopeItemKey = binding.ScopeItemKey;
        state.Session.ActiveMode = OperatorModeTypes.Clarification;
        state.Session.UnfinishedStep = new OperatorWorkflowStepContext
        {
            StepKind = PendingActionInputStepKind,
            StepState = actionType,
            StartedAtUtc = nowUtc,
            BoundTrackedPersonId = state.Session.ActiveTrackedPersonId,
            BoundScopeItemKey = binding.ScopeItemKey
        };

        return CreatePendingResolutionInputPrompt(
            state,
            $"Enter explanation for {FormatActionLabel(actionType)} on {binding.Title}. Send /cancel to abort.");
    }

    private async Task<TelegramOperatorResponse> SubmitPendingResolutionInputAsync(
        TelegramOperatorConversationState state,
        TelegramOperatorInteraction interaction,
        string messageText,
        DateTime nowUtc,
        CancellationToken ct)
    {
        var pending = state.PendingResolutionInput;
        if (pending == null)
        {
            return await RenderResolutionContextAsync(state, interaction, nowUtc, note: null, ct);
        }

        if (string.IsNullOrWhiteSpace(messageText))
        {
            return CreatePendingResolutionInputPrompt(
                state,
                $"Explanation is required for {FormatActionLabel(pending.ActionType)}. Enter text or send /cancel.");
        }

        if (messageText.Length > MaxExplanationLength)
        {
            return CreatePendingResolutionInputPrompt(
                state,
                $"Explanation is too long ({messageText.Length} chars). Limit is {MaxExplanationLength}. Edit and resend or /cancel.");
        }

        var explanation = messageText.Trim();
        var operatorIdentity = BuildAuthorizedIdentity(interaction, nowUtc);
        var baselineQueueSnapshot = await TryCaptureQueueDeltaSnapshotAsync(state, interaction, nowUtc, ct);
        var actionRequest = new ResolutionActionRequest
        {
            RequestId = BuildActionRequestId(interaction.ChatId, pending.ActionType),
            TrackedPersonId = pending.BoundTrackedPersonId,
            ScopeItemKey = pending.ScopeItemKey,
            ActionType = pending.ActionType,
            Explanation = explanation,
            ClarificationPayload = BuildClarificationPayload(pending, state, operatorIdentity, explanation, interaction, nowUtc),
            OperatorIdentity = operatorIdentity,
            Session = CloneSession(state.Session),
            SubmittedAtUtc = nowUtc
        };
        var action = await _operatorResolutionService.SubmitResolutionActionAsync(actionRequest, ct);

        state.Session = action.Session;
        state.SurfaceMode = TelegramOperatorSurfaceModes.Resolution;
        state.PendingResolutionInput = null;
        state.Session.UnfinishedStep = null;

        var note = action.Accepted
            ? action.Action.Recompute?.Enqueued == true
                ? $"{FormatActionLabel(pending.ActionType)} accepted for {pending.ItemTitle}. Bounded recompute was enqueued."
                : $"{FormatActionLabel(pending.ActionType)} accepted for {pending.ItemTitle}."
            : $"{FormatActionLabel(pending.ActionType)} failed for {pending.ItemTitle}: {action.FailureReason ?? action.Action.FailureReason ?? "unknown error"}.";
        return await RenderResolutionContextAsync(
            state,
            interaction,
            nowUtc,
            note,
            ct,
            new ResolutionActionFeedbackContext
            {
                ActionType = pending.ActionType,
                ItemTitle = pending.ItemTitle,
                Accepted = action.Accepted,
                Recompute = action.Action.Recompute,
                BaselineQueueSnapshot = baselineQueueSnapshot
            });
    }

    private async Task<TelegramOperatorResponse> CancelPendingResolutionInputAsync(
        TelegramOperatorConversationState state,
        TelegramOperatorInteraction interaction,
        DateTime nowUtc,
        CancellationToken ct)
    {
        var pending = state.PendingResolutionInput;
        if (pending == null)
        {
            if (state.SurfaceMode == TelegramOperatorSurfaceModes.Resolution)
            {
                return await RenderResolutionContextAsync(state, interaction, nowUtc, note: "No pending action input.", ct);
            }

            return CreateModeCard(state, note: "No pending action input.");
        }

        state.PendingResolutionInput = null;
        state.Session.UnfinishedStep = null;
        state.Session.ActiveMode = OperatorModeTypes.ResolutionDetail;
        state.Session.ActiveScopeItemKey = pending.ScopeItemKey;

        return await RenderResolutionContextAsync(
            state,
            interaction,
            nowUtc,
            note: $"{FormatActionLabel(pending.ActionType)} input canceled for {pending.ItemTitle}.",
            ct);
    }

    private async Task<TelegramOperatorResponse> SubmitAssistantQuestionAsync(
        TelegramOperatorConversationState state,
        TelegramOperatorInteraction interaction,
        string messageText,
        DateTime nowUtc,
        CancellationToken ct)
    {
        if (state.Session.ActiveTrackedPersonId == Guid.Empty)
        {
            return await RenderAssistantTrackedPersonPickerAsync(
                state,
                interaction,
                nowUtc,
                note: "Assistant mode requires an active tracked person.",
                callbackNotice: "Pick tracked person",
                ct);
        }

        if (string.IsNullOrWhiteSpace(messageText))
        {
            return CreateAssistantReadyCard(
                state,
                note: "Send a bounded question for the active tracked person.",
                callbackNotice: null);
        }

        if (messageText.Length > MaxAssistantQuestionLength)
        {
            return CreateAssistantReadyCard(
                state,
                note: $"Question is too long ({messageText.Length} chars). Limit is {MaxAssistantQuestionLength}.",
                callbackNotice: "Question too long");
        }

        if (string.IsNullOrWhiteSpace(state.ActiveTrackedPersonScopeKey))
        {
            return await RenderAssistantTrackedPersonPickerAsync(
                state,
                interaction,
                nowUtc,
                note: "Active tracked-person scope is unavailable. Re-select tracked person and retry.",
                callbackNotice: "Scope unavailable",
                ct);
        }

        try
        {
            var response = await _assistantContextAssemblyService.BuildBoundedResponseAsync(
                new OperatorAssistantContextAssemblyRequest
                {
                    OperatorIdentity = BuildAuthorizedIdentity(interaction, nowUtc),
                    Session = CloneSession(state.Session),
                    TrackedPersonId = state.Session.ActiveTrackedPersonId,
                    ScopeKey = state.ActiveTrackedPersonScopeKey,
                    ScopeItemKey = state.Session.ActiveScopeItemKey,
                    Question = messageText.Trim(),
                    QueueLimit = 10,
                    EvidenceLimit = 3,
                    OpenInWebEnabled = true,
                    OpenInWebTargetApi = "/api/operator/resolution/detail/query",
                    OpenInWebActiveMode = OperatorModeTypes.ResolutionDetail
                },
                generatedAtUtc: nowUtc,
                ct);

            state.SurfaceMode = TelegramOperatorSurfaceModes.Assistant;
            state.Session.ActiveMode = OperatorModeTypes.Assistant;
            state.Session.ActiveScopeItemKey = string.IsNullOrWhiteSpace(response.OpenInWeb.ScopeItemKey)
                ? state.Session.ActiveScopeItemKey
                : response.OpenInWeb.ScopeItemKey.Trim();
            state.Session.UnfinishedStep = null;

            var rendered = _assistantResponseGenerationService.RenderTelegram(response);
            var lines = new List<string>
            {
                "Assistant Mode",
                $"Active tracked person: {state.ActiveTrackedPersonDisplayName ?? "unknown"}",
                $"Question: {response.Question}",
                string.Empty,
                rendered
            };

            var buttons = new List<List<TelegramOperatorButton>>();
            if (response.OpenInWeb.Enabled)
            {
                var webUrl = BuildAssistantOpenWebUrl(response);
                if (!string.IsNullOrWhiteSpace(webUrl))
                {
                    buttons.Add(
                    [
                        new TelegramOperatorButton
                        {
                            Text = "Open in Web",
                            Url = webUrl
                        }
                    ]);
                }
            }

            buttons.Add(
            [
                new TelegramOperatorButton { Text = "Switch Person", CallbackData = AssistantSwitchPersonCallback },
                new TelegramOperatorButton { Text = "Modes", CallbackData = "mode:menu" }
            ]);

            return new TelegramOperatorResponse
            {
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
        catch (InvalidOperationException ex)
        {
            var failureReason = NormalizeAssistantFailureReason(ex.Message);
            return CreateAssistantReadyCard(
                state,
                note: $"Assistant response blocked: {failureReason}.",
                callbackNotice: "Assistant blocked");
        }
    }

    private async Task<TelegramOperatorResponse> StartPendingOfflineEventInputAsync(
        TelegramOperatorConversationState state,
        TelegramOperatorInteraction interaction,
        string inputKind,
        DateTime nowUtc,
        CancellationToken ct)
    {
        if (state.PendingResolutionInput != null)
        {
            return CreatePendingResolutionInputPrompt(
                state,
                "A resolution action input is in progress. Send explanation text or choose Cancel.");
        }

        if (state.Session.ActiveTrackedPersonId == Guid.Empty)
        {
            return await RenderOfflineTrackedPersonPickerAsync(
                state,
                interaction,
                nowUtc,
                note: "Offline Event mode requires an active tracked person.",
                callbackNotice: "Pick tracked person",
                ct);
        }

        if (string.IsNullOrWhiteSpace(state.ActiveTrackedPersonScopeKey))
        {
            return await RenderOfflineTrackedPersonPickerAsync(
                state,
                interaction,
                nowUtc,
                note: "Active tracked-person scope is unavailable. Re-select tracked person and retry.",
                callbackNotice: "Scope unavailable",
                ct);
        }

        EnsureOfflineDraftContext(state, nowUtc);
        var clarificationQuestionKey = string.Equals(inputKind, OfflineEventInputKindClarificationAnswer, StringComparison.Ordinal)
            ? state.OfflineEventDraft?.ClarificationState?.NextQuestionKey
            : null;
        state.PendingOfflineEventInput = new TelegramPendingOfflineEventInput
        {
            InputKind = inputKind,
            ClarificationQuestionKey = clarificationQuestionKey,
            StartedAtUtc = nowUtc,
            BoundTrackedPersonId = state.Session.ActiveTrackedPersonId
        };
        state.SurfaceMode = TelegramOperatorSurfaceModes.OfflineEvent;
        state.Session.ActiveMode = OperatorModeTypes.OfflineEvent;
        state.Session.UnfinishedStep = new OperatorWorkflowStepContext
        {
            StepKind = PendingOfflineEventInputStepKind,
            StepState = inputKind,
            StartedAtUtc = nowUtc,
            BoundTrackedPersonId = state.Session.ActiveTrackedPersonId,
            BoundScopeItemKey = state.ActiveTrackedPersonScopeKey ?? string.Empty
        };

        var prompt = string.Equals(inputKind, OfflineEventInputKindSummary, StringComparison.Ordinal)
            ? $"Enter offline-event summary (max {MaxOfflineEventSummaryLength} chars). Send /cancel to abort."
            : string.Equals(inputKind, OfflineEventInputKindRecordingReference, StringComparison.Ordinal)
                ? $"Enter recording reference URL or note (max {MaxOfflineEventRecordingReferenceLength} chars). Send /cancel to abort."
                : "Answer the clarification question. If unknown, respond with \"I don't know\". Send /cancel to abort.";
        return CreatePendingOfflineEventInputPrompt(state, prompt);
    }

    private async Task<TelegramOperatorResponse> StartPendingOfflineClarificationAnswerAsync(
        TelegramOperatorConversationState state,
        TelegramOperatorInteraction interaction,
        DateTime nowUtc,
        CancellationToken ct)
    {
        EnsureOfflineDraftContext(state, nowUtc);
        var clarificationState = state.OfflineEventDraft!.ClarificationState
            ?? _offlineEventClarificationPolicy.CreateInitialState(
                state.OfflineEventDraft.Summary,
                state.OfflineEventDraft.RecordingReference,
                nowUtc);
        state.OfflineEventDraft.ClarificationState = clarificationState;

        if (string.Equals(clarificationState.LoopStatus, OfflineEventClarificationLoopStatuses.Stopped, StringComparison.Ordinal))
        {
            return await RenderOfflineEventContextAsync(
                state,
                interaction,
                nowUtc,
                note: "Clarification loop is already stopped. Use Save Final to persist this event.",
                ct);
        }

        var nextQuestion = clarificationState.Questions
            .Where(x => string.Equals(x.Status, OfflineEventClarificationQuestionStatuses.Queued, StringComparison.Ordinal))
            .OrderBy(x => x.PriorityRank)
            .FirstOrDefault();
        if (nextQuestion == null)
        {
            clarificationState.LoopStatus = OfflineEventClarificationLoopStatuses.Stopped;
            clarificationState.StopReason = OfflineEventClarificationStopReasons.Exhausted;
            clarificationState.StopDetail = "clarification_question_pool_exhausted";
            clarificationState.StoppedAtUtc = nowUtc;
            clarificationState.NextQuestionKey = null;

            return await RenderOfflineEventContextAsync(
                state,
                interaction,
                nowUtc,
                note: "No remaining clarification questions. Use Save Final to persist this event.",
                ct);
        }

        clarificationState.NextQuestionKey = nextQuestion.Key;
        state.PendingOfflineEventInput = new TelegramPendingOfflineEventInput
        {
            InputKind = OfflineEventInputKindClarificationAnswer,
            ClarificationQuestionKey = nextQuestion.Key,
            StartedAtUtc = nowUtc,
            BoundTrackedPersonId = state.Session.ActiveTrackedPersonId
        };
        state.SurfaceMode = TelegramOperatorSurfaceModes.OfflineEvent;
        state.Session.ActiveMode = OperatorModeTypes.OfflineEvent;
        state.Session.UnfinishedStep = new OperatorWorkflowStepContext
        {
            StepKind = PendingOfflineEventInputStepKind,
            StepState = $"{OfflineEventInputKindClarificationAnswer}:{nextQuestion.Key}",
            StartedAtUtc = nowUtc,
            BoundTrackedPersonId = state.Session.ActiveTrackedPersonId,
            BoundScopeItemKey = state.ActiveTrackedPersonScopeKey ?? string.Empty
        };

        return CreatePendingOfflineEventInputPrompt(
            state,
            $"Question: {nextQuestion.Text}");
    }

    private async Task<TelegramOperatorResponse> SubmitPendingOfflineEventInputAsync(
        TelegramOperatorConversationState state,
        TelegramOperatorInteraction interaction,
        string messageText,
        DateTime nowUtc,
        CancellationToken ct)
    {
        var pending = state.PendingOfflineEventInput;
        if (pending == null)
        {
            return await RenderOfflineEventContextAsync(state, interaction, nowUtc, note: null, ct);
        }

        if (string.IsNullOrWhiteSpace(messageText))
        {
            return CreatePendingOfflineEventInputPrompt(
                state,
                "Input cannot be empty. Enter text or send /cancel.");
        }

        var value = messageText.Trim();
        if (string.Equals(pending.InputKind, OfflineEventInputKindClarificationAnswer, StringComparison.Ordinal))
        {
            return await SubmitOfflineClarificationAnswerAsync(state, interaction, pending, value, nowUtc, ct);
        }

        var maxLength = string.Equals(pending.InputKind, OfflineEventInputKindSummary, StringComparison.Ordinal)
            ? MaxOfflineEventSummaryLength
            : MaxOfflineEventRecordingReferenceLength;
        if (value.Length > maxLength)
        {
            return CreatePendingOfflineEventInputPrompt(
                state,
                $"Input is too long ({value.Length} chars). Limit is {maxLength}. Edit and resend or /cancel.");
        }

        EnsureOfflineDraftContext(state, nowUtc);
        if (string.Equals(pending.InputKind, OfflineEventInputKindSummary, StringComparison.Ordinal))
        {
            state.OfflineEventDraft!.Summary = value;
        }
        else
        {
            state.OfflineEventDraft!.RecordingReference = value;
        }

        state.OfflineEventDraft.ClarificationState = _offlineEventClarificationPolicy.CreateInitialState(
            state.OfflineEventDraft.Summary,
            state.OfflineEventDraft.RecordingReference,
            nowUtc);
        state.OfflineEventDraft.UpdatedAtUtc = nowUtc;
        state.PendingOfflineEventInput = null;
        state.SurfaceMode = TelegramOperatorSurfaceModes.OfflineEvent;
        state.Session.ActiveMode = OperatorModeTypes.OfflineEvent;
        state.Session.UnfinishedStep = null;

        var updatedLabel = string.Equals(pending.InputKind, OfflineEventInputKindSummary, StringComparison.Ordinal)
            ? "Summary updated."
            : "Recording reference updated.";
        return await RenderOfflineEventContextAsync(state, interaction, nowUtc, note: updatedLabel, ct);
    }

    private async Task<TelegramOperatorResponse> SubmitOfflineClarificationAnswerAsync(
        TelegramOperatorConversationState state,
        TelegramOperatorInteraction interaction,
        TelegramPendingOfflineEventInput pending,
        string answer,
        DateTime nowUtc,
        CancellationToken ct)
    {
        EnsureOfflineDraftContext(state, nowUtc);
        var clarificationState = state.OfflineEventDraft!.ClarificationState
            ?? _offlineEventClarificationPolicy.CreateInitialState(
                state.OfflineEventDraft.Summary,
                state.OfflineEventDraft.RecordingReference,
                nowUtc);
        state.OfflineEventDraft.ClarificationState = clarificationState;

        var questionKey = pending.ClarificationQuestionKey
            ?? clarificationState.NextQuestionKey;
        if (string.IsNullOrWhiteSpace(questionKey))
        {
            state.PendingOfflineEventInput = null;
            state.Session.UnfinishedStep = null;
            return await RenderOfflineEventContextAsync(
                state,
                interaction,
                nowUtc,
                note: "No active clarification question is available. Use Clarify Next to continue.",
                ct);
        }

        OfflineEventClarificationEvaluationResult evaluation;
        try
        {
            evaluation = _offlineEventClarificationPolicy.ApplyAnswer(
                clarificationState,
                questionKey,
                answer,
                nowUtc);
        }
        catch (InvalidOperationException)
        {
            state.PendingOfflineEventInput = null;
            state.Session.UnfinishedStep = null;
            return await RenderOfflineEventContextAsync(
                state,
                interaction,
                nowUtc,
                note: "Clarification question context expired. Use Clarify Next to continue.",
                ct);
        }

        state.OfflineEventDraft.UpdatedAtUtc = nowUtc;
        state.PendingOfflineEventInput = null;
        state.Session.UnfinishedStep = null;
        state.SurfaceMode = TelegramOperatorSurfaceModes.OfflineEvent;
        state.Session.ActiveMode = OperatorModeTypes.OfflineEvent;

        var note = evaluation.StopTriggered
            ? $"Clarification stop triggered ({FormatOfflineStopReason(evaluation.StopReason)}). Trust {Math.Round(evaluation.PartialConfidence * 100f):0}%."
            : $"Clarification answer captured. Next question ready. Trust {Math.Round(evaluation.PartialConfidence * 100f):0}%";
        return await RenderOfflineEventContextAsync(state, interaction, nowUtc, note, ct);
    }

    private async Task<TelegramOperatorResponse> CancelPendingOfflineEventInputAsync(
        TelegramOperatorConversationState state,
        TelegramOperatorInteraction interaction,
        DateTime nowUtc,
        CancellationToken ct)
    {
        var pending = state.PendingOfflineEventInput;
        if (pending == null)
        {
            if (state.SurfaceMode == TelegramOperatorSurfaceModes.OfflineEvent)
            {
                return await RenderOfflineEventContextAsync(state, interaction, nowUtc, note: "No pending offline-event input.", ct);
            }

            return CreateModeCard(state, note: "No pending offline-event input.");
        }

        state.PendingOfflineEventInput = null;
        state.Session.UnfinishedStep = null;
        state.Session.ActiveMode = OperatorModeTypes.OfflineEvent;

        var itemLabel = string.Equals(pending.InputKind, OfflineEventInputKindSummary, StringComparison.Ordinal)
            ? "summary"
            : "recording reference";
        return await RenderOfflineEventContextAsync(
            state,
            interaction,
            nowUtc,
            note: $"{itemLabel} input canceled.",
            ct);
    }

    private async Task<TelegramOperatorResponse> SaveOfflineEventDraftAsync(
        TelegramOperatorConversationState state,
        TelegramOperatorInteraction interaction,
        DateTime nowUtc,
        CancellationToken ct)
    {
        if (state.PendingOfflineEventInput != null)
        {
            return CreatePendingOfflineEventInputPrompt(
                state,
                "Finish or cancel the pending input before saving.");
        }

        if (state.Session.ActiveTrackedPersonId == Guid.Empty)
        {
            return await RenderOfflineTrackedPersonPickerAsync(
                state,
                interaction,
                nowUtc,
                note: "Offline Event mode requires an active tracked person.",
                callbackNotice: "Pick tracked person",
                ct);
        }

        if (string.IsNullOrWhiteSpace(state.ActiveTrackedPersonScopeKey))
        {
            return await RenderOfflineTrackedPersonPickerAsync(
                state,
                interaction,
                nowUtc,
                note: "Active tracked-person scope is unavailable. Re-select tracked person and retry.",
                callbackNotice: "Scope unavailable",
                ct);
        }

        EnsureOfflineDraftContext(state, nowUtc);
        var summary = NormalizeOptional(state.OfflineEventDraft?.Summary);
        if (string.IsNullOrWhiteSpace(summary))
        {
            return await RenderOfflineEventContextAsync(
                state,
                interaction,
                nowUtc,
                note: "Summary is required before saving. Choose Capture Summary.",
                ct);
        }

        var request = new OperatorOfflineEventCreateRequest
        {
            TrackedPersonId = state.Session.ActiveTrackedPersonId,
            ScopeKey = state.ActiveTrackedPersonScopeKey!,
            Summary = summary,
            RecordingReference = NormalizeOptional(state.OfflineEventDraft?.RecordingReference),
            Status = OperatorOfflineEventStatuses.Draft,
            CapturePayloadJson = BuildOfflineEventCapturePayloadJson(state, interaction, nowUtc),
            ClarificationStateJson = BuildOfflineEventClarificationStateJson(state, nowUtc),
            TimelineLinkageJson = "{}",
            Confidence = state.OfflineEventDraft?.ClarificationState?.PartialConfidence,
            CapturedAtUtc = state.OfflineEventDraft!.StartedAtUtc,
            SavedAtUtc = null,
            OperatorIdentity = BuildAuthorizedIdentity(interaction, nowUtc),
            Session = CloneSession(state.Session)
        };

        var saved = await _operatorOfflineEventRepository.CreateAsync(request, ct);

        state.SurfaceMode = TelegramOperatorSurfaceModes.OfflineEvent;
        state.Session.ActiveMode = OperatorModeTypes.OfflineEvent;
        state.Session.ActiveScopeItemKey = $"offline_event:{saved.OfflineEventId:D}";
        state.Session.UnfinishedStep = null;
        state.PendingOfflineEventInput = null;
        state.OfflineEventDraft = null;

        return await RenderOfflineEventContextAsync(
            state,
            interaction,
            nowUtc,
            note: $"Offline-event draft saved: {saved.OfflineEventId:D}",
            ct);
    }

    private async Task<TelegramOperatorResponse> SaveOfflineEventFinalAsync(
        TelegramOperatorConversationState state,
        TelegramOperatorInteraction interaction,
        DateTime nowUtc,
        CancellationToken ct)
    {
        if (state.PendingOfflineEventInput != null)
        {
            return CreatePendingOfflineEventInputPrompt(
                state,
                "Finish or cancel the pending input before final save.");
        }

        if (state.Session.ActiveTrackedPersonId == Guid.Empty)
        {
            return await RenderOfflineTrackedPersonPickerAsync(
                state,
                interaction,
                nowUtc,
                note: "Offline Event mode requires an active tracked person.",
                callbackNotice: "Pick tracked person",
                ct);
        }

        if (string.IsNullOrWhiteSpace(state.ActiveTrackedPersonScopeKey))
        {
            return await RenderOfflineTrackedPersonPickerAsync(
                state,
                interaction,
                nowUtc,
                note: "Active tracked-person scope is unavailable. Re-select tracked person and retry.",
                callbackNotice: "Scope unavailable",
                ct);
        }

        EnsureOfflineDraftContext(state, nowUtc);
        var clarificationState = state.OfflineEventDraft!.ClarificationState
            ?? _offlineEventClarificationPolicy.CreateInitialState(
                state.OfflineEventDraft.Summary,
                state.OfflineEventDraft.RecordingReference,
                nowUtc);
        state.OfflineEventDraft.ClarificationState = clarificationState;

        if (!string.Equals(clarificationState.LoopStatus, OfflineEventClarificationLoopStatuses.Stopped, StringComparison.Ordinal))
        {
            return await RenderOfflineEventContextAsync(
                state,
                interaction,
                nowUtc,
                note: "Final save is available after clarification stop conditions are reached.",
                ct);
        }

        var summary = NormalizeOptional(state.OfflineEventDraft.Summary);
        if (string.IsNullOrWhiteSpace(summary))
        {
            return await RenderOfflineEventContextAsync(
                state,
                interaction,
                nowUtc,
                note: "Summary is required before final save.",
                ct);
        }

        var request = new OperatorOfflineEventCreateRequest
        {
            TrackedPersonId = state.Session.ActiveTrackedPersonId,
            ScopeKey = state.ActiveTrackedPersonScopeKey ?? string.Empty,
            Summary = summary,
            RecordingReference = NormalizeOptional(state.OfflineEventDraft.RecordingReference),
            Status = OperatorOfflineEventStatuses.Saved,
            CapturePayloadJson = BuildOfflineEventCapturePayloadJson(state, interaction, nowUtc),
            ClarificationStateJson = BuildOfflineEventClarificationStateJson(state, nowUtc),
            TimelineLinkageJson = "{}",
            Confidence = clarificationState.PartialConfidence,
            CapturedAtUtc = state.OfflineEventDraft.StartedAtUtc,
            SavedAtUtc = nowUtc,
            OperatorIdentity = BuildAuthorizedIdentity(interaction, nowUtc),
            Session = CloneSession(state.Session)
        };

        var saved = await _operatorOfflineEventRepository.CreateAsync(request, ct);
        state.SurfaceMode = TelegramOperatorSurfaceModes.OfflineEvent;
        state.Session.ActiveMode = OperatorModeTypes.OfflineEvent;
        state.Session.ActiveScopeItemKey = $"offline_event:{saved.OfflineEventId:D}";
        state.Session.UnfinishedStep = null;
        state.PendingOfflineEventInput = null;
        state.OfflineEventDraft = null;

        var trust = Math.Round((request.Confidence ?? 0f) * 100f);
        return await RenderOfflineEventContextAsync(
            state,
            interaction,
            nowUtc,
            note: $"Offline-event final save completed: {saved.OfflineEventId:D} (Trust {trust:0}%).",
            ct);
    }

    private async Task<TelegramOperatorResponse> RenderOfflineEventContextAsync(
        TelegramOperatorConversationState state,
        TelegramOperatorInteraction interaction,
        DateTime nowUtc,
        string? note,
        CancellationToken ct)
    {
        if (state.Session.ActiveTrackedPersonId == Guid.Empty)
        {
            return await RenderOfflineTrackedPersonPickerAsync(
                state,
                interaction,
                nowUtc,
                note ?? "Offline Event mode requires an active tracked person.",
                callbackNotice: "Pick tracked person",
                ct);
        }

        if (string.IsNullOrWhiteSpace(state.ActiveTrackedPersonScopeKey))
        {
            return await RenderOfflineTrackedPersonPickerAsync(
                state,
                interaction,
                nowUtc,
                note: "Active tracked-person scope is unavailable. Re-select tracked person and retry.",
                callbackNotice: "Scope unavailable",
                ct);
        }

        EnsureOfflineDraftContext(state, nowUtc);
        state.SurfaceMode = TelegramOperatorSurfaceModes.OfflineEvent;
        state.Session.ActiveMode = OperatorModeTypes.OfflineEvent;

        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(note))
        {
            lines.Add(note.Trim());
            lines.Add(string.Empty);
        }

        lines.Add("Offline Event Mode");
        lines.Add($"Active tracked person: {state.ActiveTrackedPersonDisplayName ?? "unknown"}");
        lines.Add($"Scope: {state.ActiveTrackedPersonScopeKey}");
        lines.Add($"Summary: {TrimForInline(state.OfflineEventDraft?.Summary, 280)}");
        lines.Add($"Recording ref: {TrimForInline(state.OfflineEventDraft?.RecordingReference, 180)}");
        var clarificationState = state.OfflineEventDraft?.ClarificationState
            ?? _offlineEventClarificationPolicy.CreateInitialState(
                state.OfflineEventDraft?.Summary,
                state.OfflineEventDraft?.RecordingReference,
                nowUtc);
        var answeredCount = clarificationState.History.Count;
        var questionCount = clarificationState.Questions.Count;
        var nextQuestion = clarificationState.Questions
            .Where(x => string.Equals(x.Status, OfflineEventClarificationQuestionStatuses.Queued, StringComparison.Ordinal))
            .OrderBy(x => x.PriorityRank)
            .FirstOrDefault();
        lines.Add($"Clarification progress: {answeredCount}/{questionCount}");
        lines.Add($"Loop status: {clarificationState.LoopStatus}");
        lines.Add($"Trust: {Math.Round(clarificationState.PartialConfidence * 100f):0}%");
        if (!string.IsNullOrWhiteSpace(clarificationState.StopReason))
        {
            lines.Add($"Stop reason: {FormatOfflineStopReason(clarificationState.StopReason)}");
        }
        if (nextQuestion != null)
        {
            lines.Add($"Top clarification question: {nextQuestion.Text}");
            lines.Add($"Expected gain: {nextQuestion.ExpectedInformationGain:0.00}");
        }
        else
        {
            lines.Add("Top clarification question: none (loop stopped).");
        }

        lines.Add("Stop rule: repetition, no-new-context, unknown-answer pattern, or sustained low gain.");

        return new TelegramOperatorResponse
        {
            CallbackNotificationText = interaction.CallbackData == null ? null : "Offline Event",
            Messages =
            [
                new TelegramOperatorMessage
                {
                    Text = string.Join(Environment.NewLine, lines),
                    Buttons =
                    [
                        [
                            new TelegramOperatorButton { Text = "Capture Summary", CallbackData = OfflineCaptureSummaryCallback },
                            new TelegramOperatorButton { Text = "Add Recording Ref", CallbackData = OfflineCaptureRecordingCallback }
                        ],
                        [
                            new TelegramOperatorButton { Text = "Clarify Next", CallbackData = OfflineClarifyNextCallback },
                            new TelegramOperatorButton { Text = "Save Draft", CallbackData = OfflineSaveDraftCallback },
                            new TelegramOperatorButton { Text = "Save Final", CallbackData = OfflineSaveFinalCallback }
                        ],
                        [
                            new TelegramOperatorButton { Text = "Switch Person", CallbackData = OfflineSwitchPersonCallback }
                        ],
                        [
                            new TelegramOperatorButton { Text = "Modes", CallbackData = "mode:menu" }
                        ]
                    ]
                }
            ]
        };
    }

    private async Task<TelegramOperatorResponse> RenderOfflineTrackedPersonPickerAsync(
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
        state.SurfaceMode = TelegramOperatorSurfaceModes.OfflineEvent;
        state.Session.ActiveMode = OperatorModeTypes.OfflineEvent;
        UpdateActiveTrackedPersonState(state, query.ActiveTrackedPerson);

        if (!query.Accepted)
        {
            TelegramOperatorSessionStore.ClearResolutionContext(state);
            return CreateModeCard(
                state,
                $"Tracked person selection is unavailable: {query.FailureReason ?? "unknown error"}.",
                callbackNotice: "Selection unavailable");
        }

        return BuildOfflineTrackedPersonPickerResponse(state, query.TrackedPersons, note, callbackNotice);
    }

    private static ResolutionClarificationPayload BuildClarificationPayload(
        TelegramPendingResolutionInput pending,
        TelegramOperatorConversationState state,
        OperatorIdentityContext operatorIdentity,
        string explanation,
        TelegramOperatorInteraction interaction,
        DateTime nowUtc)
    {
        var payload = new ResolutionClarificationPayload
        {
            Summary = $"{FormatActionLabel(pending.ActionType)} explanation captured in Telegram resolution mode."
        };
        payload.Responses.Add(
            new ResolutionClarificationResponse
            {
                QuestionKey = "operator_explanation",
                QuestionText = $"Operator explanation for {pending.ActionType}",
                AnswerValue = explanation,
                AnswerKind = "free_text"
            });
        payload.Metadata["surface"] = OperatorSurfaceTypes.Telegram;
        payload.Metadata["operator_id"] = operatorIdentity.OperatorId;
        payload.Metadata["operator_display"] = operatorIdentity.OperatorDisplay;
        payload.Metadata["operator_subject"] = $"telegram:{interaction.UserId}";
        payload.Metadata["operator_session_id"] = state.Session.OperatorSessionId;
        payload.Metadata["tracked_person_id"] = pending.BoundTrackedPersonId.ToString("D");
        payload.Metadata["scope_item_key"] = pending.ScopeItemKey;
        payload.Metadata["item_type"] = pending.ItemType;
        payload.Metadata["action_type"] = pending.ActionType;
        payload.Metadata["step_kind"] = PendingActionInputStepKind;
        payload.Metadata["step_started_at_utc"] = pending.StartedAtUtc.ToString("O");
        payload.Metadata["captured_at_utc"] = nowUtc.ToString("O");
        return payload;
    }

    private static TelegramOperatorResponse CreatePendingResolutionInputPrompt(
        TelegramOperatorConversationState state,
        string note)
    {
        var pending = state.PendingResolutionInput;
        if (pending == null)
        {
            return new TelegramOperatorResponse
            {
                CallbackNotificationText = "No pending input",
                Messages =
                [
                    CreateMessage("No pending action input.")
                ]
            };
        }

        var lines = new List<string>
        {
            "Resolution Input",
            $"Action: {FormatActionLabel(pending.ActionType)}",
            $"Item: {pending.ItemTitle}",
            $"Scope: {pending.ScopeItemKey}",
            note
        };

        return new TelegramOperatorResponse
        {
            CallbackNotificationText = "Input required",
            Messages =
            [
                new TelegramOperatorMessage
                {
                    Text = string.Join(Environment.NewLine, lines),
                    Buttons =
                    [
                        [
                            new TelegramOperatorButton
                            {
                                Text = "Cancel",
                                CallbackData = ResolutionCancelInputCallback
                            }
                        ]
                    ]
                }
            ]
        };
    }

    private async Task<TelegramOperatorResponse> SubmitApproveActionAsync(
        TelegramOperatorConversationState state,
        TelegramOperatorInteraction interaction,
        TelegramResolutionCardBinding binding,
        DateTime nowUtc,
        CancellationToken ct)
    {
        var detail = await _operatorResolutionService.GetResolutionDetailAsync(
            new OperatorResolutionDetailQueryRequest
            {
                OperatorIdentity = BuildAuthorizedIdentity(interaction, nowUtc),
                Session = CloneSession(state.Session),
                TrackedPersonId = state.Session.ActiveTrackedPersonId,
                ScopeItemKey = binding.ScopeItemKey,
                EvidenceLimit = 1,
                EvidenceSortBy = ResolutionEvidenceSortFields.ObservedAt,
                EvidenceSortDirection = ResolutionSortDirections.Desc
            },
            ct);

        state.Session = detail.Session;
        state.SurfaceMode = TelegramOperatorSurfaceModes.Resolution;
        if (!detail.Accepted || detail.Detail.Item == null)
        {
            return await RenderResolutionContextAsync(
                state,
                interaction,
                nowUtc,
                note: $"Approve could not bind the current item: {detail.FailureReason ?? "resolution item unavailable"}.",
                ct);
        }

        var baselineQueueSnapshot = await TryCaptureQueueDeltaSnapshotAsync(state, interaction, nowUtc, ct);
        var action = await _operatorResolutionService.SubmitResolutionActionAsync(
            new ResolutionActionRequest
            {
                RequestId = BuildActionRequestId(interaction.ChatId, ResolutionActionTypes.Approve),
                TrackedPersonId = state.Session.ActiveTrackedPersonId,
                ScopeItemKey = binding.ScopeItemKey,
                ActionType = ResolutionActionTypes.Approve,
                OperatorIdentity = BuildAuthorizedIdentity(interaction, nowUtc),
                Session = CloneSession(state.Session),
                SubmittedAtUtc = nowUtc
            },
            ct);

        state.Session = action.Session;
        state.SurfaceMode = TelegramOperatorSurfaceModes.Resolution;

        var note = action.Accepted
            ? action.Action.Recompute?.Enqueued == true
                ? $"Approve accepted for {binding.Title}. Bounded recompute was enqueued."
                : $"Approve accepted for {binding.Title}."
            : $"Approve failed for {binding.Title}: {action.FailureReason ?? action.Action.FailureReason ?? "unknown error"}.";
        return await RenderResolutionContextAsync(
            state,
            interaction,
            nowUtc,
            note,
            ct,
            new ResolutionActionFeedbackContext
            {
                ActionType = ResolutionActionTypes.Approve,
                ItemTitle = binding.Title,
                Accepted = action.Accepted,
                Recompute = action.Action.Recompute,
                BaselineQueueSnapshot = baselineQueueSnapshot
            });
    }

    private async Task<TelegramOperatorResponse> RenderEvidencePreviewAsync(
        TelegramOperatorConversationState state,
        TelegramOperatorInteraction interaction,
        TelegramResolutionCardBinding binding,
        DateTime nowUtc,
        CancellationToken ct)
    {
        var detail = await _operatorResolutionService.GetResolutionDetailAsync(
            new OperatorResolutionDetailQueryRequest
            {
                OperatorIdentity = BuildAuthorizedIdentity(interaction, nowUtc),
                Session = CloneSession(state.Session),
                TrackedPersonId = state.Session.ActiveTrackedPersonId,
                ScopeItemKey = binding.ScopeItemKey,
                EvidenceLimit = EvidencePreviewLimit,
                EvidenceSortBy = ResolutionEvidenceSortFields.ObservedAt,
                EvidenceSortDirection = ResolutionSortDirections.Desc
            },
            ct);

        state.Session = detail.Session;
        state.SurfaceMode = TelegramOperatorSurfaceModes.Resolution;
        if (!detail.Accepted || detail.Detail.Item == null)
        {
            return await RenderResolutionContextAsync(
                state,
                interaction,
                nowUtc,
                note: $"Evidence preview unavailable for {binding.Title}: {detail.FailureReason ?? "resolution item unavailable"}.",
                ct);
        }

        var item = detail.Detail.Item;
        state.Session.ActiveMode = OperatorModeTypes.Evidence;
        state.Session.ActiveScopeItemKey = binding.ScopeItemKey;
        state.Session.UnfinishedStep = null;

        var lines = new List<string>
        {
            "Evidence Preview",
            $"Item: {item.Title}",
            $"Type: {item.ItemType}",
            $"Scope: {item.ScopeItemKey}",
            $"Status: {item.Status}",
            $"Trust: {FormatTrust(item.TrustFactor)}",
            $"Showing top evidence: {Math.Min(item.Evidence.Count, EvidencePreviewLimit)} of {item.EvidenceCount}"
        };
        foreach (var (evidence, index) in item.Evidence.Take(EvidencePreviewLimit).Select((value, index) => (value, index)))
        {
            var evidenceLine = $"{index + 1}. {TrimForInline(evidence.Summary, 160)}";
            var observedAt = evidence.ObservedAtUtc.HasValue
                ? FormatUtc(evidence.ObservedAtUtc.Value)
                : "unknown";
            var sourceLabel = string.IsNullOrWhiteSpace(evidence.SourceLabel)
                ? "unknown source"
                : evidence.SourceLabel;
            lines.Add(evidenceLine);
            lines.Add($"   Trust {FormatTrust(evidence.TrustFactor)} | Observed {observedAt} | Source {sourceLabel}");
        }

        if (item.Evidence.Count == 0)
        {
            lines.Add("No bounded evidence summaries are currently projected for this item.");
        }

        lines.Add("Use Open Web for deep analysis.");

        return new TelegramOperatorResponse
        {
            CallbackNotificationText = "Evidence",
            Messages =
            [
                new TelegramOperatorMessage
                {
                    Text = string.Join(Environment.NewLine, lines),
                    Buttons =
                    [
                        [
                            new TelegramOperatorButton
                            {
                                Text = "Open Web",
                                CallbackData = $"{ResolutionActionCallbackPrefix}{ResolutionActionTypes.OpenWeb}:{binding.Token}"
                            }
                        ],
                        [
                            new TelegramOperatorButton { Text = "Back to Queue", CallbackData = "resolution:refresh" },
                            new TelegramOperatorButton { Text = "Modes", CallbackData = "mode:menu" }
                        ]
                    ]
                }
            ]
        };
    }

    private async Task<TelegramOperatorResponse> RenderOpenWebHandoffAsync(
        TelegramOperatorConversationState state,
        TelegramOperatorInteraction interaction,
        TelegramResolutionCardBinding binding,
        DateTime nowUtc,
        CancellationToken ct)
    {
        var detail = await _operatorResolutionService.GetResolutionDetailAsync(
            new OperatorResolutionDetailQueryRequest
            {
                OperatorIdentity = BuildAuthorizedIdentity(interaction, nowUtc),
                Session = CloneSession(state.Session),
                TrackedPersonId = state.Session.ActiveTrackedPersonId,
                ScopeItemKey = binding.ScopeItemKey,
                EvidenceLimit = 1,
                EvidenceSortBy = ResolutionEvidenceSortFields.ObservedAt,
                EvidenceSortDirection = ResolutionSortDirections.Desc
            },
            ct);

        state.Session = detail.Session;
        state.SurfaceMode = TelegramOperatorSurfaceModes.Resolution;
        if (!detail.Accepted || detail.Detail.Item == null)
        {
            return await RenderResolutionContextAsync(
                state,
                interaction,
                nowUtc,
                note: $"Open Web handoff unavailable for {binding.Title}: {detail.FailureReason ?? "resolution item unavailable"}.",
                ct);
        }

        var item = detail.Detail.Item;
        state.Session.ActiveMode = OperatorModeTypes.ResolutionDetail;
        state.Session.ActiveScopeItemKey = binding.ScopeItemKey;
        state.Session.UnfinishedStep = null;

        var handoffToken = BuildOpenWebHandoffToken(state.Session, item, nowUtc);
        var lines = new List<string>
        {
            "Open Web Handoff",
            "Use this bounded context in the web operator surface.",
            $"tracked_person_id={state.Session.ActiveTrackedPersonId:D}",
            $"scope_item_key={item.ScopeItemKey}",
            $"operator_session_id={state.Session.OperatorSessionId}",
            $"active_mode={OperatorModeTypes.ResolutionDetail}",
            "target_api=/api/operator/resolution/detail/query",
            $"handoff_token={handoffToken}",
            "Legacy web routes are not used for this handoff."
        };

        return new TelegramOperatorResponse
        {
            CallbackNotificationText = "Open Web",
            Messages =
            [
                new TelegramOperatorMessage
                {
                    Text = string.Join(Environment.NewLine, lines),
                    Buttons =
                    [
                        [
                            new TelegramOperatorButton
                            {
                                Text = "Evidence",
                                CallbackData = $"{ResolutionActionCallbackPrefix}{ResolutionActionTypes.Evidence}:{binding.Token}"
                            }
                        ],
                        [
                            new TelegramOperatorButton { Text = "Back to Queue", CallbackData = "resolution:refresh" },
                            new TelegramOperatorButton { Text = "Modes", CallbackData = "mode:menu" }
                        ]
                    ]
                }
            ]
        };
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

    private async Task<TelegramOperatorResponse> RenderAssistantTrackedPersonPickerAsync(
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
        state.SurfaceMode = TelegramOperatorSurfaceModes.Assistant;
        state.Session.ActiveMode = OperatorModeTypes.Assistant;
        UpdateActiveTrackedPersonState(state, query.ActiveTrackedPerson);

        if (!query.Accepted)
        {
            TelegramOperatorSessionStore.ClearResolutionContext(state);
            return CreateModeCard(
                state,
                $"Tracked person selection is unavailable: {query.FailureReason ?? "unknown error"}.",
                callbackNotice: "Selection unavailable");
        }

        return BuildAssistantTrackedPersonPickerResponse(state, query.TrackedPersons, note, callbackNotice);
    }

    private async Task<TelegramOperatorResponse> RenderAlertsTrackedPersonPickerAsync(
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
        state.SurfaceMode = TelegramOperatorSurfaceModes.Alerts;
        state.Session.ActiveMode = OperatorModeTypes.ResolutionQueue;
        UpdateActiveTrackedPersonState(state, query.ActiveTrackedPerson);

        if (!query.Accepted)
        {
            TelegramOperatorSessionStore.ClearResolutionContext(state);
            return CreateModeCard(
                state,
                $"Tracked person selection is unavailable: {query.FailureReason ?? "unknown error"}.",
                callbackNotice: "Selection unavailable");
        }

        return BuildAlertsTrackedPersonPickerResponse(state, query.TrackedPersons, note, callbackNotice);
    }

    private async Task<TelegramOperatorResponse> RenderResolutionContextAsync(
        TelegramOperatorConversationState state,
        TelegramOperatorInteraction interaction,
        DateTime nowUtc,
        string? note,
        CancellationToken ct,
        ResolutionActionFeedbackContext? actionFeedback = null)
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
        if (!string.IsNullOrWhiteSpace(queue.Queue.ScopeKey))
        {
            state.ActiveTrackedPersonScopeKey = queue.Queue.ScopeKey;
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

        var cardMessages = BuildResolutionCardMessages(state, queue.Queue.Items);
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

        AddResolutionActionFeedbackLines(lines, actionFeedback, queue.Queue);

        if (queue.Queue.Items.Count == 0)
        {
            lines.Add("No open resolution items are currently projected for this bounded scope.");
        }
        else
        {
            lines.Add($"Showing compact cards: {cardMessages.Count} of {queue.Queue.Items.Count} visible items.");
        }

        var messages = new List<TelegramOperatorMessage>
        {
            new()
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
        };
        messages.AddRange(cardMessages);

        return new TelegramOperatorResponse
        {
            CallbackNotificationText = interaction.CallbackData == null ? null : "Resolution",
            Messages = messages
        };
    }

    private async Task<TelegramOperatorResponse> RenderAlertsContextAsync(
        TelegramOperatorConversationState state,
        TelegramOperatorInteraction interaction,
        DateTime nowUtc,
        string? note,
        CancellationToken ct)
    {
        if (state.Session.ActiveTrackedPersonId == Guid.Empty)
        {
            return await RenderAlertsTrackedPersonPickerAsync(
                state,
                interaction,
                nowUtc,
                note ?? "Alerts mode requires an active tracked person.",
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
                Limit = 25
            },
            ct);

        state.Session = queue.Session;
        state.SurfaceMode = TelegramOperatorSurfaceModes.Alerts;
        state.Session.ActiveMode = OperatorModeTypes.ResolutionQueue;
        if (!string.IsNullOrWhiteSpace(queue.Queue.TrackedPersonDisplayName))
        {
            state.ActiveTrackedPersonDisplayName = queue.Queue.TrackedPersonDisplayName;
        }
        if (!string.IsNullOrWhiteSpace(queue.Queue.ScopeKey))
        {
            state.ActiveTrackedPersonScopeKey = queue.Queue.ScopeKey;
        }

        if (!queue.Accepted)
        {
            TelegramOperatorSessionStore.ClearResolutionContext(state);
            return await RenderAlertsTrackedPersonPickerAsync(
                state,
                interaction,
                nowUtc,
                note: $"Alerts context expired: {queue.FailureReason ?? "unknown error"}. Re-select the tracked person.",
                callbackNotice: "Alerts reset",
                ct);
        }

        var cardMessages = BuildAlertCardMessages(state, queue.Queue, nowUtc);
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(note))
        {
            lines.Add(note.Trim());
            lines.Add(string.Empty);
        }

        lines.Add("Alerts Mode");
        lines.Add($"Active tracked person: {state.ActiveTrackedPersonDisplayName ?? "unknown"}");
        lines.Add("Telegram shows only workflow-critical alerts that require acknowledgement.");
        lines.Add($"Visible critical alerts: {cardMessages.Count}");
        lines.Add($"Acknowledged this session: {state.AcknowledgedAlertScopeItemKeys.Count}");
        if (queue.Queue.RuntimeState != null)
        {
            lines.Add($"Runtime: {queue.Queue.RuntimeState.State}");
        }

        if (cardMessages.Count == 0)
        {
            lines.Add("No Telegram-push critical alerts are currently projected for this bounded scope.");
        }
        else
        {
            lines.Add($"Showing compact alert cards: {cardMessages.Count}.");
        }

        var messages = new List<TelegramOperatorMessage>
        {
            new()
            {
                Text = string.Join(Environment.NewLine, lines),
                Buttons =
                [
                    [
                        new TelegramOperatorButton { Text = "Refresh", CallbackData = "mode:alerts" },
                        new TelegramOperatorButton { Text = "Switch Person", CallbackData = AlertsSwitchPersonCallback }
                    ],
                    [
                        new TelegramOperatorButton { Text = "Resolution", CallbackData = "mode:resolution" },
                        new TelegramOperatorButton { Text = "Modes", CallbackData = "mode:menu" }
                    ]
                ]
            }
        };
        messages.AddRange(cardMessages);

        return new TelegramOperatorResponse
        {
            CallbackNotificationText = interaction.CallbackData == null ? null : "Alerts",
            Messages = messages
        };
    }

    private async Task<TelegramOperatorResponse> AcknowledgeAlertAsync(
        TelegramOperatorConversationState state,
        TelegramOperatorInteraction interaction,
        string alertToken,
        DateTime nowUtc,
        CancellationToken ct)
    {
        var normalizedToken = alertToken.Trim();
        if (!state.AlertCardBindings.TryGetValue(normalizedToken, out var binding))
        {
            return await RenderAlertsContextAsync(
                state,
                interaction,
                nowUtc,
                note: "That alert card is no longer current. Refresh alerts and retry.",
                ct);
        }

        if (!state.AcknowledgedAlertScopeItemKeys.Add(binding.ScopeItemKey))
        {
            return await RenderAlertsContextAsync(
                state,
                interaction,
                nowUtc,
                note: $"Alert already acknowledged for {binding.Title}.",
                ct);
        }

        await _auditService.RecordSessionEventAsync(
            new OperatorSessionAuditRequest
            {
                RequestId = BuildRequestId("alert-ack", interaction.ChatId, nowUtc),
                SessionEventType = AlertAcknowledgedSessionEventType,
                DecisionOutcome = OperatorAuditDecisionOutcomes.Accepted,
                ScopeKey = state.ActiveTrackedPersonScopeKey,
                TrackedPersonId = state.Session.ActiveTrackedPersonId == Guid.Empty ? null : state.Session.ActiveTrackedPersonId,
                ScopeItemKey = binding.ScopeItemKey,
                ItemType = binding.ItemType,
                OperatorIdentity = BuildAuthorizedIdentity(interaction, nowUtc),
                Session = CloneSession(state.Session),
                Details =
                {
                    ["title"] = binding.Title,
                    ["alert_rule_id"] = binding.AlertRuleId,
                    ["alert_reason"] = binding.AlertReason
                },
                EventTimeUtc = nowUtc
            },
            ct);

        return await RenderAlertsContextAsync(
            state,
            interaction,
            nowUtc,
            note: $"Acknowledged alert for {binding.Title}.",
            ct);
    }

    private async Task<ResolutionQueueDeltaSnapshot?> TryCaptureQueueDeltaSnapshotAsync(
        TelegramOperatorConversationState state,
        TelegramOperatorInteraction interaction,
        DateTime nowUtc,
        CancellationToken ct)
    {
        if (state.Session.ActiveTrackedPersonId == Guid.Empty)
        {
            return null;
        }

        var baseline = await _operatorResolutionService.GetResolutionQueueAsync(
            new OperatorResolutionQueueQueryRequest
            {
                OperatorIdentity = BuildAuthorizedIdentity(interaction, nowUtc),
                Session = CloneSession(state.Session),
                TrackedPersonId = state.Session.ActiveTrackedPersonId,
                SortBy = ResolutionQueueSortFields.Priority,
                SortDirection = ResolutionSortDirections.Desc,
                Limit = 50
            },
            ct);

        return baseline.Accepted
            ? CaptureQueueDeltaSnapshot(baseline.Queue)
            : null;
    }

    private static void AddResolutionActionFeedbackLines(
        List<string> lines,
        ResolutionActionFeedbackContext? actionFeedback,
        ResolutionQueueResult queue)
    {
        if (actionFeedback == null)
        {
            return;
        }

        lines.Add(string.Empty);
        lines.Add($"Post-action feedback: {FormatActionLabel(actionFeedback.ActionType)} on {actionFeedback.ItemTitle}");
        if (!actionFeedback.Accepted)
        {
            lines.Add("Recompute lifecycle: not started (action was not accepted).");
            return;
        }

        var recompute = actionFeedback.Recompute;
        if (recompute == null)
        {
            lines.Add("Recompute lifecycle: unavailable.");
            return;
        }

        lines.Add(
            $"Recompute lifecycle: {FormatRecomputeLifecycleLabel(recompute.LifecycleStatus)}"
            + (recompute.LifecycleUpdatedAtUtc.HasValue
                ? $" (updated {FormatUtc(recompute.LifecycleUpdatedAtUtc.Value)})"
                : string.Empty));
        if (recompute.CompletedAtUtc.HasValue)
        {
            lines.Add($"Recompute completed: {FormatUtc(recompute.CompletedAtUtc.Value)}");
        }

        if (!string.IsNullOrWhiteSpace(recompute.LastResultStatus))
        {
            lines.Add($"Recompute result: {recompute.LastResultStatus}");
        }

        if (!string.IsNullOrWhiteSpace(recompute.FailureReason))
        {
            lines.Add($"Recompute failure: {recompute.FailureReason}");
        }

        if (actionFeedback.BaselineQueueSnapshot == null)
        {
            lines.Add("Related-conflict delta: unavailable.");
            return;
        }

        var postSnapshot = CaptureQueueDeltaSnapshot(queue);
        var unresolved = postSnapshot.RelatedConflictKeys.Count;
        var autoResolved = actionFeedback.BaselineQueueSnapshot.RelatedConflictKeys
            .Except(postSnapshot.RelatedConflictKeys, StringComparer.Ordinal)
            .Count();
        var newlyEmerged = postSnapshot.RelatedConflictKeys
            .Except(actionFeedback.BaselineQueueSnapshot.RelatedConflictKeys, StringComparer.Ordinal)
            .Count();

        lines.Add($"Related conflicts: unresolved {unresolved}");
        lines.Add($"Auto-resolved since action: {autoResolved}");
        lines.Add($"Newly-emerged since action: {newlyEmerged}");
    }

    private List<TelegramOperatorMessage> BuildAlertCardMessages(
        TelegramOperatorConversationState state,
        ResolutionQueueResult queue,
        DateTime nowUtc)
    {
        state.AlertCardBindings.Clear();
        state.AlertCardGeneration++;

        var messages = new List<TelegramOperatorMessage>();
        var generation = state.AlertCardGeneration;
        var alertItems = queue.Items
            .Select(item => new
            {
                Item = item,
                Decision = EvaluateTelegramAlertPolicy(item, queue)
            })
            .Where(entry => entry.Decision.PushTelegram && entry.Decision.RequiresAcknowledgement)
            .Take(MaxResolutionCards)
            .ToList();

        foreach (var (entry, index) in alertItems.Select((entry, index) => (entry, index)))
        {
            var token = $"{generation:x}{index + 1:x}";
            var openWebUrl = BuildResolutionOpenWebUrl(state.Session, entry.Item.ScopeItemKey, nowUtc);
            var binding = new TelegramAlertCardBinding
            {
                Token = token,
                ScopeItemKey = entry.Item.ScopeItemKey,
                ItemType = entry.Item.ItemType,
                Title = entry.Item.Title,
                AlertRuleId = entry.Decision.RuleId,
                AlertReason = entry.Decision.Reason,
                OpenWebUrl = openWebUrl
            };
            state.AlertCardBindings[token] = binding;
            messages.Add(
                new TelegramOperatorMessage
                {
                    Text = BuildAlertCardText(entry.Item, binding, state.AcknowledgedAlertScopeItemKeys.Contains(entry.Item.ScopeItemKey)),
                    Buttons = BuildAlertCardButtons(binding, state.AcknowledgedAlertScopeItemKeys.Contains(entry.Item.ScopeItemKey))
                });
        }

        return messages;
    }

    private List<TelegramOperatorMessage> BuildResolutionCardMessages(
        TelegramOperatorConversationState state,
        IReadOnlyList<ResolutionItemSummary> items)
    {
        state.ResolutionCardBindings.Clear();
        state.ResolutionCardGeneration++;

        var messages = new List<TelegramOperatorMessage>();
        var generation = state.ResolutionCardGeneration;
        foreach (var (item, index) in items.Take(MaxResolutionCards).Select((item, index) => (item, index)))
        {
            var token = $"{generation:x}{index + 1:x}";
            var binding = new TelegramResolutionCardBinding
            {
                Token = token,
                ScopeItemKey = item.ScopeItemKey,
                ItemType = item.ItemType,
                Title = item.Title,
                AvailableActions = [.. item.AvailableActions.Select(ResolutionActionTypes.Normalize)]
            };
            state.ResolutionCardBindings[token] = binding;
            messages.Add(
                new TelegramOperatorMessage
                {
                    Text = BuildResolutionCardText(item),
                    Buttons = BuildResolutionCardButtons(binding)
                });
        }

        return messages;
    }

    private OperatorAlertPolicyDecision EvaluateTelegramAlertPolicy(ResolutionItemSummary item, ResolutionQueueResult queue)
    {
        var sourceClass = ResolveAlertSourceClass(item);
        return _operatorAlertPolicyService.Evaluate(new OperatorAlertPolicyInput
        {
            SourceClass = sourceClass,
            ScopeKey = queue.ScopeKey,
            TrackedPersonId = queue.TrackedPersonId,
            ScopeItemKey = item.ScopeItemKey,
            ItemType = item.ItemType,
            Priority = item.Priority,
            RuntimeState = sourceClass == OperatorAlertSourceClasses.RuntimeControlState ? queue.RuntimeState?.State : null,
            RuntimeDefectClass = sourceClass == OperatorAlertSourceClasses.RuntimeDefect ? ResolveRuntimeDefectClass(item) : null,
            RuntimeDefectSeverity = sourceClass == OperatorAlertSourceClasses.RuntimeDefect ? item.Priority : null,
            IsBlockingWorkflow = IsBlockingAlertItem(item),
            IsActiveTrackedPersonScope = queue.TrackedPersonId.HasValue && queue.TrackedPersonId.Value != Guid.Empty,
            IsMaterializationFailure = sourceClass == OperatorAlertSourceClasses.MaterializationFailure,
            IsStateTransitionOnly = false
        });
    }

    private static string ResolveAlertSourceClass(ResolutionItemSummary item)
    {
        if (string.Equals(item.ItemType, ResolutionItemTypes.MissingData, StringComparison.Ordinal))
        {
            return OperatorAlertSourceClasses.MaterializationFailure;
        }

        if (string.Equals(item.ItemType, ResolutionItemTypes.Review, StringComparison.Ordinal))
        {
            if (string.Equals(item.AffectedFamily, "runtime_control", StringComparison.Ordinal)
                || item.Title.StartsWith("Runtime operating in ", StringComparison.Ordinal))
            {
                return OperatorAlertSourceClasses.RuntimeControlState;
            }

            if (string.Equals(item.AffectedFamily, RuntimeDefectClasses.ControlPlane, StringComparison.OrdinalIgnoreCase)
                || item.Title.Contains("runtime", StringComparison.OrdinalIgnoreCase))
            {
                return OperatorAlertSourceClasses.RuntimeDefect;
            }
        }

        return OperatorAlertSourceClasses.ResolutionBlocker;
    }

    private static string? ResolveRuntimeDefectClass(ResolutionItemSummary item)
    {
        if (string.Equals(item.AffectedFamily, RuntimeDefectClasses.ControlPlane, StringComparison.OrdinalIgnoreCase))
        {
            return RuntimeDefectClasses.ControlPlane;
        }

        if (item.Title.Contains("control plane", StringComparison.OrdinalIgnoreCase))
        {
            return RuntimeDefectClasses.ControlPlane;
        }

        return RuntimeDefectClasses.Data;
    }

    private static bool IsBlockingAlertItem(ResolutionItemSummary item)
    {
        if (string.Equals(item.Priority, ResolutionItemPriorities.Critical, StringComparison.Ordinal))
        {
            return true;
        }

        return item.Status switch
        {
            ResolutionItemStatuses.Blocked => true,
            ResolutionItemStatuses.AttentionRequired => true,
            ResolutionItemStatuses.Degraded => true,
            _ => string.Equals(item.ItemType, ResolutionItemTypes.Clarification, StringComparison.Ordinal)
                 || string.Equals(item.ItemType, ResolutionItemTypes.BlockedBranch, StringComparison.Ordinal)
        };
    }

    private static string BuildResolutionCardText(ResolutionItemSummary item)
    {
        var lines = new List<string>
        {
            item.Title,
            $"Type: {item.ItemType}",
            $"Summary: {item.Summary}",
            $"Why: {item.WhyItMatters}",
            $"Trust: {FormatTrust(item.TrustFactor)}",
            $"Status: {item.Status}",
            $"Evidence: {item.EvidenceCount}",
            $"Updated: {FormatUtc(item.UpdatedAtUtc)}"
        };

        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildAlertCardText(ResolutionItemSummary item, TelegramAlertCardBinding binding, bool acknowledged)
    {
        var lines = new List<string>
        {
            item.Title,
            $"Alert rule: {binding.AlertRuleId}",
            $"Type: {item.ItemType}",
            $"Priority: {item.Priority}",
            $"Status: {item.Status}",
            $"Summary: {item.Summary}",
            $"Why: {item.WhyItMatters}",
            $"Scope: {item.ScopeItemKey}",
            $"Trust: {FormatTrust(item.TrustFactor)}",
            $"Acknowledged: {(acknowledged ? "yes" : "no")}"
        };
        if (!string.IsNullOrWhiteSpace(binding.AlertReason))
        {
            lines.Add($"Reason: {binding.AlertReason}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static List<List<TelegramOperatorButton>> BuildResolutionCardButtons(TelegramResolutionCardBinding binding)
    {
        var buttons = new List<List<TelegramOperatorButton>>();
        var available = binding.AvailableActions.ToHashSet(StringComparer.Ordinal);

        var primaryRow = BuildActionRow(binding.Token, available, ResolutionActionTypes.Approve, ResolutionActionTypes.Reject, ResolutionActionTypes.Defer);
        if (primaryRow.Count > 0)
        {
            buttons.Add(primaryRow);
        }

        var secondaryRow = BuildActionRow(binding.Token, available, ResolutionActionTypes.Clarify, ResolutionActionTypes.Evidence, ResolutionActionTypes.OpenWeb);
        if (secondaryRow.Count > 0)
        {
            buttons.Add(secondaryRow);
        }

        return buttons;
    }

    private static List<List<TelegramOperatorButton>> BuildAlertCardButtons(TelegramAlertCardBinding binding, bool acknowledged)
    {
        var primaryRow = new List<TelegramOperatorButton>();
        if (acknowledged)
        {
            primaryRow.Add(new TelegramOperatorButton { Text = "Acknowledged" });
        }
        else
        {
            primaryRow.Add(new TelegramOperatorButton
            {
                Text = "Acknowledge",
                CallbackData = $"{AlertAcknowledgeCallbackPrefix}{binding.Token}"
            });
        }

        if (!string.IsNullOrWhiteSpace(binding.OpenWebUrl))
        {
            primaryRow.Add(new TelegramOperatorButton
            {
                Text = "Open in Web",
                Url = binding.OpenWebUrl
            });
        }

        return [primaryRow];
    }

    private static List<TelegramOperatorButton> BuildActionRow(
        string token,
        IReadOnlySet<string> available,
        params string[] actionTypes)
    {
        return actionTypes
            .Where(available.Contains)
            .Select(actionType => new TelegramOperatorButton
            {
                Text = FormatActionLabel(actionType),
                CallbackData = $"{ResolutionActionCallbackPrefix}{actionType}:{token}"
            })
            .ToList();
    }

    private static string FormatActionLabel(string actionType)
    {
        return ResolutionActionTypes.Normalize(actionType) switch
        {
            ResolutionActionTypes.OpenWeb => "Open Web",
            ResolutionActionTypes.Approve => "Approve",
            ResolutionActionTypes.Reject => "Reject",
            ResolutionActionTypes.Defer => "Defer",
            ResolutionActionTypes.Clarify => "Clarify",
            ResolutionActionTypes.Evidence => "Evidence",
            _ => actionType
        };
    }

    private static string FormatRecomputeLifecycleLabel(string? lifecycleStatus)
    {
        return lifecycleStatus switch
        {
            ResolutionRecomputeLifecycleStatuses.Running => "running",
            ResolutionRecomputeLifecycleStatuses.Done => "done",
            ResolutionRecomputeLifecycleStatuses.Failed => "failed",
            ResolutionRecomputeLifecycleStatuses.ClarificationBlocked => "clarification_blocked",
            _ => string.IsNullOrWhiteSpace(lifecycleStatus) ? "unknown" : lifecycleStatus.Trim()
        };
    }

    private static ResolutionQueueDeltaSnapshot CaptureQueueDeltaSnapshot(ResolutionQueueResult queue)
    {
        var relatedConflictKeys = queue.Items
            .Where(item => string.Equals(item.ItemType, ResolutionItemTypes.Contradiction, StringComparison.Ordinal))
            .Select(item => item.ScopeItemKey)
            .Where(scopeItemKey => !string.IsNullOrWhiteSpace(scopeItemKey))
            .ToHashSet(StringComparer.Ordinal);
        return new ResolutionQueueDeltaSnapshot
        {
            RelatedConflictKeys = relatedConflictKeys
        };
    }

    private static string FormatTrust(float trustFactor)
    {
        var percent = Math.Clamp((int)Math.Round(trustFactor * 100f, MidpointRounding.AwayFromZero), 0, 100);
        return $"{percent}%";
    }

    private static string FormatUtc(DateTime value)
        => value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'");

    private static string BuildActionRequestId(long chatId, string actionType)
        => $"telegram:{chatId}:{ResolutionActionTypes.Normalize(actionType)}:{Guid.NewGuid():N}";

    private static string TrimForInline(string? value, int maxLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value)
            ? "no summary"
            : value.Trim().ReplaceLineEndings(" ");
        if (normalized.Length <= maxLength)
        {
            return normalized;
        }

        return $"{normalized[..(maxLength - 3)]}...";
    }

    private static string NormalizeAssistantFailureReason(string? value)
    {
        var normalized = NormalizeOptional(value) ?? "assistant_error";
        var separatorIndex = normalized.IndexOf(':', StringComparison.Ordinal);
        return separatorIndex <= 0 ? normalized : normalized[..separatorIndex];
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private string BuildOpenWebHandoffToken(OperatorSessionContext session, ResolutionItemDetail item, DateTime nowUtc)
    {
        var signingSecret = OperatorHandoffTokenCodec.ResolveSigningSecret(_webSettings);
        if (string.IsNullOrWhiteSpace(signingSecret))
        {
            return string.Empty;
        }

        return OperatorHandoffTokenCodec.CreateToken(
            OperatorHandoffTokenCodec.TelegramResolutionContext,
            session.ActiveTrackedPersonId,
            item.ScopeItemKey.Trim(),
            session.OperatorSessionId.Trim(),
            signingSecret,
            nowUtc);
    }

    private string? BuildResolutionOpenWebUrl(
        OperatorSessionContext session,
        string scopeItemKey,
        DateTime nowUtc)
    {
        var trackedPersonId = session.ActiveTrackedPersonId;
        var normalizedScopeItemKey = NormalizeOptional(scopeItemKey);
        var operatorSessionId = NormalizeOptional(session.OperatorSessionId);
        if (trackedPersonId == Guid.Empty
            || string.IsNullOrWhiteSpace(normalizedScopeItemKey)
            || string.IsNullOrWhiteSpace(operatorSessionId))
        {
            return null;
        }

        var webUrl = NormalizeOptional(_webSettings.Url);
        if (!Uri.TryCreate(webUrl, UriKind.Absolute, out var baseUri))
        {
            return null;
        }

        var handoffToken = BuildResolutionOpenWebHandoffToken(trackedPersonId, normalizedScopeItemKey, operatorSessionId, nowUtc);
        var queryParts = new List<string>
        {
            $"tracked_person_id={Uri.EscapeDataString(trackedPersonId.ToString("D"))}",
            $"scope_item_key={Uri.EscapeDataString(normalizedScopeItemKey)}",
            $"operator_session_id={Uri.EscapeDataString(operatorSessionId)}",
            $"active_mode={Uri.EscapeDataString(OperatorModeTypes.ResolutionDetail)}",
            $"handoff_token={Uri.EscapeDataString(handoffToken)}",
            $"target_api={Uri.EscapeDataString("/api/operator/resolution/detail/query")}"
        };

        var builder = new UriBuilder(baseUri)
        {
            Path = "/operator/resolution",
            Query = string.Join("&", queryParts)
        };
        return builder.Uri.ToString();
    }

    private string BuildResolutionOpenWebHandoffToken(
        Guid trackedPersonId,
        string scopeItemKey,
        string operatorSessionId,
        DateTime nowUtc)
    {
        var signingSecret = OperatorHandoffTokenCodec.ResolveSigningSecret(_webSettings);
        if (string.IsNullOrWhiteSpace(signingSecret))
        {
            return string.Empty;
        }

        return OperatorHandoffTokenCodec.CreateToken(
            OperatorHandoffTokenCodec.TelegramResolutionContext,
            trackedPersonId,
            scopeItemKey,
            operatorSessionId,
            signingSecret,
            nowUtc);
    }

    private string? BuildAssistantOpenWebUrl(OperatorAssistantResponseEnvelope response)
    {
        if (!response.OpenInWeb.Enabled)
        {
            return null;
        }

        var webUrl = NormalizeOptional(_webSettings.Url);
        if (!Uri.TryCreate(webUrl, UriKind.Absolute, out var baseUri))
        {
            return null;
        }

        var queryParts = new List<string>
        {
            $"tracked_person_id={Uri.EscapeDataString(response.OpenInWeb.TrackedPersonId.ToString("D"))}",
            $"scope_item_key={Uri.EscapeDataString(response.OpenInWeb.ScopeItemKey ?? string.Empty)}",
            $"operator_session_id={Uri.EscapeDataString(response.OperatorSessionId ?? string.Empty)}",
            $"active_mode={Uri.EscapeDataString(response.OpenInWeb.ActiveMode ?? string.Empty)}",
            $"handoff_token={Uri.EscapeDataString(response.OpenInWeb.HandoffToken ?? string.Empty)}",
            $"target_api={Uri.EscapeDataString(response.OpenInWeb.TargetApi ?? string.Empty)}"
        };

        var builder = new UriBuilder(baseUri)
        {
            Path = "/operator/resolution",
            Query = string.Join("&", queryParts)
        };
        return builder.Uri.ToString();
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
                    CallbackData = $"{TrackedPersonCallbackPrefix}{person.TrackedPersonId:D}"
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

    private TelegramOperatorResponse BuildAssistantTrackedPersonPickerResponse(
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

        lines.Add("Assistant Mode");
        lines.Add($"Active tracked person: {state.ActiveTrackedPersonDisplayName ?? "none"}");
        lines.Add("Select the tracked person for this bounded assistant session.");

        var buttons = new List<List<TelegramOperatorButton>>();
        foreach (var person in trackedPersons.Take(12))
        {
            buttons.Add(
            [
                new TelegramOperatorButton
                {
                    Text = person.DisplayName,
                    CallbackData = $"{AssistantTrackedPersonCallbackPrefix}{person.TrackedPersonId:D}"
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

    private TelegramOperatorResponse BuildAlertsTrackedPersonPickerResponse(
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

        lines.Add("Alerts Mode");
        lines.Add($"Active tracked person: {state.ActiveTrackedPersonDisplayName ?? "none"}");
        lines.Add("Select the tracked person for this bounded alerts session.");

        var buttons = new List<List<TelegramOperatorButton>>();
        foreach (var person in trackedPersons.Take(12))
        {
            buttons.Add(
            [
                new TelegramOperatorButton
                {
                    Text = person.DisplayName,
                    CallbackData = $"{AlertsTrackedPersonCallbackPrefix}{person.TrackedPersonId:D}"
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

    private TelegramOperatorResponse BuildOfflineTrackedPersonPickerResponse(
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

        lines.Add("Offline Event Mode");
        lines.Add($"Active tracked person: {state.ActiveTrackedPersonDisplayName ?? "none"}");
        lines.Add("Select the tracked person for this bounded offline-event session.");

        var buttons = new List<List<TelegramOperatorButton>>();
        foreach (var person in trackedPersons.Take(12))
        {
            buttons.Add(
            [
                new TelegramOperatorButton
                {
                    Text = person.DisplayName,
                    CallbackData = $"{OfflineTrackedPersonCallbackPrefix}{person.TrackedPersonId:D}"
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

    private static TelegramOperatorResponse CreatePendingOfflineEventInputPrompt(
        TelegramOperatorConversationState state,
        string note)
    {
        var pending = state.PendingOfflineEventInput;
        if (pending == null)
        {
            return new TelegramOperatorResponse
            {
                CallbackNotificationText = "No pending input",
                Messages =
                [
                    CreateMessage("No pending offline-event input.")
                ]
            };
        }

        var fieldName = string.Equals(pending.InputKind, OfflineEventInputKindSummary, StringComparison.Ordinal)
            ? "Summary"
            : string.Equals(pending.InputKind, OfflineEventInputKindRecordingReference, StringComparison.Ordinal)
                ? "Recording reference"
                : "Clarification answer";
        var lines = new List<string>
        {
            "Offline Event Input",
            $"Field: {fieldName}",
            note
        };

        return new TelegramOperatorResponse
        {
            CallbackNotificationText = "Input required",
            Messages =
            [
                new TelegramOperatorMessage
                {
                    Text = string.Join(Environment.NewLine, lines),
                    Buttons =
                    [
                        [
                            new TelegramOperatorButton
                            {
                                Text = "Cancel",
                                CallbackData = OfflineCancelInputCallback
                            }
                        ]
                    ]
                }
            ]
        };
    }

    private void EnsureOfflineDraftContext(TelegramOperatorConversationState state, DateTime nowUtc)
    {
        if (state.OfflineEventDraft != null)
        {
            if (state.OfflineEventDraft.BoundTrackedPersonId != state.Session.ActiveTrackedPersonId
                || !string.Equals(state.OfflineEventDraft.ScopeKey, state.ActiveTrackedPersonScopeKey, StringComparison.Ordinal))
            {
                state.OfflineEventDraft = null;
            }
        }

        if (state.OfflineEventDraft == null)
        {
            state.OfflineEventDraft = new TelegramOfflineEventDraft
            {
                StartedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc,
                BoundTrackedPersonId = state.Session.ActiveTrackedPersonId,
                ScopeKey = state.ActiveTrackedPersonScopeKey ?? string.Empty,
                ClarificationState = _offlineEventClarificationPolicy.CreateInitialState(null, null, nowUtc)
            };
        }
    }

    private static string BuildOfflineEventCapturePayloadJson(
        TelegramOperatorConversationState state,
        TelegramOperatorInteraction interaction,
        DateTime nowUtc)
    {
        var draft = state.OfflineEventDraft;
        var payload = new
        {
            source = "telegram_offline_event_mode",
            summary = NormalizeOptional(draft?.Summary),
            recording_reference = NormalizeOptional(draft?.RecordingReference),
            captured_at_utc = nowUtc,
            draft_started_at_utc = draft?.StartedAtUtc,
            draft_updated_at_utc = draft?.UpdatedAtUtc,
            operator_chat_id = interaction.ChatId,
            operator_user_id = interaction.UserId
        };
        return System.Text.Json.JsonSerializer.Serialize(payload);
    }

    private string BuildOfflineEventClarificationStateJson(
        TelegramOperatorConversationState state,
        DateTime nowUtc)
    {
        var draft = state.OfflineEventDraft;
        var clarificationState = draft?.ClarificationState
            ?? _offlineEventClarificationPolicy.CreateInitialState(
                draft?.Summary,
                draft?.RecordingReference,
                nowUtc);
        return JsonSerializer.Serialize(clarificationState);
    }

    private static string FormatOfflineStopReason(string? stopReason)
    {
        return stopReason switch
        {
            OfflineEventClarificationStopReasons.Repetition => "repetition",
            OfflineEventClarificationStopReasons.NoNewInformation => "no_new_information",
            OfflineEventClarificationStopReasons.UnknownPattern => "unknown_pattern",
            OfflineEventClarificationStopReasons.LowGain => "low_gain",
            OfflineEventClarificationStopReasons.Exhausted => "question_pool_exhausted",
            _ => "none"
        };
    }

    private TelegramOperatorResponse CreateAssistantReadyCard(
        TelegramOperatorConversationState state,
        string? note,
        string? callbackNotice)
    {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(note))
        {
            lines.Add(note.Trim());
            lines.Add(string.Empty);
        }

        lines.Add("Assistant Mode");
        lines.Add($"Active tracked person: {state.ActiveTrackedPersonDisplayName ?? "none"}");
        lines.Add("Ask a bounded question about this tracked person.");
        lines.Add("Response contract: Short Answer, What Is Known, What It Means, Recommendation, Trust.");

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
                            new TelegramOperatorButton { Text = "Switch Person", CallbackData = AssistantSwitchPersonCallback },
                            new TelegramOperatorButton { Text = "Modes", CallbackData = "mode:menu" }
                        ]
                    ]
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

    private TelegramOperatorResponse CreateAlertsPolicyCard(
        TelegramOperatorConversationState? state,
        string? callbackNotice = null)
    {
        var rules = _operatorAlertPolicyService.GetRules();
        var pushRuleCount = rules.Count(rule =>
            string.Equals(rule.EscalationBoundary, OperatorAlertEscalationBoundaries.TelegramPushAcknowledge, StringComparison.Ordinal));
        var webOnlyRuleCount = rules.Count(rule =>
            string.Equals(rule.EscalationBoundary, OperatorAlertEscalationBoundaries.WebOnly, StringComparison.Ordinal));
        var suppressedRuleCount = rules.Count(rule =>
            string.Equals(rule.EscalationBoundary, OperatorAlertEscalationBoundaries.Suppressed, StringComparison.Ordinal));

        var lines = new List<string>
        {
            "Alerts Policy (OPINT-009-A)",
            $"Active tracked person: {state?.ActiveTrackedPersonDisplayName ?? "none"}",
            string.Empty,
            $"Telegram push + acknowledgement: {pushRuleCount} critical rules",
            $"Web-only critical context: {webOnlyRuleCount} rule",
            $"Suppressed-by-default churn/non-critical: {suppressedRuleCount} rules",
            string.Empty,
            "Default policy suppresses non-critical transitions.",
            "Telegram push remains workflow-critical only."
        };

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
                            new TelegramOperatorButton { Text = "Resolution", CallbackData = "mode:resolution" },
                            new TelegramOperatorButton { Text = "Modes", CallbackData = "mode:menu" }
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
                state.ActiveTrackedPersonScopeKey = null;
            }

            return;
        }

        state.ActiveTrackedPersonDisplayName = activeTrackedPerson.DisplayName;
        state.ActiveTrackedPersonScopeKey = activeTrackedPerson.ScopeKey;
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

    private sealed class ResolutionActionFeedbackContext
    {
        public string ActionType { get; init; } = string.Empty;
        public string ItemTitle { get; init; } = string.Empty;
        public bool Accepted { get; init; }
        public ResolutionRecomputeContract? Recompute { get; init; }
        public ResolutionQueueDeltaSnapshot? BaselineQueueSnapshot { get; init; }
    }

    private sealed class ResolutionQueueDeltaSnapshot
    {
        public HashSet<string> RelatedConflictKeys { get; init; } = new(StringComparer.Ordinal);
    }
}
