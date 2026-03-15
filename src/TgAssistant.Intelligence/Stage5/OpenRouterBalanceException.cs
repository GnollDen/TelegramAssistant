using System.Net;

namespace TgAssistant.Intelligence.Stage5;

public sealed class OpenRouterBalanceException : HttpRequestException
{
    public OpenRouterBalanceException(string message, HttpStatusCode statusCode)
        : base(message, null, statusCode)
    {
    }
}
