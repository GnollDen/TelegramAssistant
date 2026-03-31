namespace TgAssistant.Core.Models;

public static class ScopeVisibilityPolicy
{
    public const long SyntheticSmokeChatIdMin = 9_000_000_000_000L;

    public static bool IsSyntheticChatId(long chatId) => chatId >= SyntheticSmokeChatIdMin;

    public static bool IsOperatorVisibleScope(long caseId, long chatId)
        => caseId > 0 && chatId > 0 && !IsSyntheticChatId(chatId);
}
