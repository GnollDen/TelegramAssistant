namespace TgAssistant.Intelligence.Stage6;

public interface IBotChatService
{
    Task<string> GenerateReplyAsync(
        string userMessage,
        long? transportChatId = null,
        long? sourceMessageId = null,
        long? senderId = null,
        CancellationToken ct = default);
    Task<string> TriggerSessionResummaryAsync(long chatId, int sessionIndex, CancellationToken ct);
}
