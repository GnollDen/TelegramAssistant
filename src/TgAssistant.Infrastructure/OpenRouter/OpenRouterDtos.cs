namespace TgAssistant.Infrastructure.OpenRouter;

public class OpenRouterRequest
{
    public string Model { get; set; } = string.Empty;
    public List<OpenRouterMessage> Messages { get; set; } = new();
    public List<OpenRouterTool>? Tools { get; set; }
    public object? ToolChoice { get; set; }
    public OpenRouterResponseFormat? ResponseFormat { get; set; }
    public OpenRouterProviderPreferences? Provider { get; set; }
    public int? MaxTokens { get; set; }
    public float? Temperature { get; set; }
}

public class OpenRouterProviderPreferences
{
    public List<string>? Order { get; set; }
    public bool? AllowFallbacks { get; set; }
}

public class OpenRouterMessage
{
    public string Role { get; set; } = string.Empty;
    public object Content { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? ToolCallId { get; set; }
    public List<OpenRouterToolCall>? ToolCalls { get; set; }
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
    public string Role { get; set; } = string.Empty;
    public object? Content { get; set; } = string.Empty;
    public List<OpenRouterToolCall>? ToolCalls { get; set; }
}

public class OpenRouterTool
{
    public string Type { get; set; } = "function";
    public OpenRouterFunctionDefinition Function { get; set; } = new();
}

public class OpenRouterFunctionDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public object Parameters { get; set; } = new();
}

public class OpenRouterToolCall
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = "function";
    public OpenRouterToolCallFunction Function { get; set; } = new();
}

public class OpenRouterToolCallFunction
{
    public string Name { get; set; } = string.Empty;
    public string Arguments { get; set; } = "{}";
}

public class OpenRouterUsage
{
    public int? PromptTokens { get; set; }
    public int? CompletionTokens { get; set; }
    public int? TotalTokens { get; set; }
    public decimal? Cost { get; set; }
}
