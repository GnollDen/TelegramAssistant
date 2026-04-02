// Legacy Stage6 surface retained for reference and diagnostics only. Not part of the PRD target architecture.

namespace TgAssistant.Intelligence.Stage6;

public sealed class BotChatTurnDiagnostics
{
    public string Reply { get; set; } = string.Empty;
    public string ResolvedModel { get; set; } = string.Empty;
    public int EmbeddingCalls { get; set; }
    public int ChatCompletionCalls { get; set; }
    public int DroppedToolCalls { get; set; }
    public List<string> ToolCallsExecuted { get; set; } = [];
    public bool UsedToolCalls => ToolCallsExecuted.Count > 0;
}
