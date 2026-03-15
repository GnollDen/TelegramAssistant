namespace TgAssistant.Intelligence.Stage6;

public interface IBotChatService
{
    Task<string> GenerateReplyAsync(string userMessage);
}
