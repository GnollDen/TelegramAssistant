namespace TgAssistant.Infrastructure.OpenRouter;

public class OpenRouterRequest
{
    public string Model { get; set; } = string.Empty;
    public List<OpenRouterMessage> Messages { get; set; } = new();
    public OpenRouterResponseFormat? ResponseFormat { get; set; }
    public int? MaxTokens { get; set; }
    public float? Temperature { get; set; }
}

public class OpenRouterMessage
{
    public string Role { get; set; } = string.Empty;
    public object Content { get; set; } = string.Empty;
}

public class OpenRouterResponseFormat
{
    public string Type { get; set; } = string.Empty;
}

public class OpenRouterResponse
{
    public List<OpenRouterChoice>? Choices { get; set; }
    public OpenRouterUsage? Usage { get; set; }
}

public class OpenRouterChoice
{
    public OpenRouterResponseMessage? Message { get; set; }
}

public class OpenRouterResponseMessage
{
    public string Content { get; set; } = string.Empty;
}

public class OpenRouterUsage
{
    public int? PromptTokens { get; set; }
    public int? CompletionTokens { get; set; }
    public int? TotalTokens { get; set; }
    public decimal? Cost { get; set; }
}
