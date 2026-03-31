namespace TgAssistant.Core.Models;

public static class DirectChatSenderResolver
{
    public static long Resolve(
        long explicitSenderId,
        long chatId,
        bool isDirectUserChat,
        bool isOutgoing,
        long selfUserId)
    {
        if (explicitSenderId > 0)
        {
            return explicitSenderId;
        }

        if (!isDirectUserChat || chatId <= 0)
        {
            return 0;
        }

        if (!isOutgoing)
        {
            // In 1:1 incoming message without explicit from_id the only reliable peer is dialog user.
            return chatId;
        }

        // Outgoing fallback is safe only when self id is known from active authenticated client.
        return selfUserId > 0 ? selfUserId : 0;
    }
}
