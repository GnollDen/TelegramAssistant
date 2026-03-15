using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Intelligence.Stage5;

namespace TgAssistant.Intelligence.Stage6;

public class BotChatService : IBotChatService
{
    private const int DefaultFactLimit = 10;
    private const int ReplyMaxTokens = 400;
    private readonly ITextEmbeddingGenerator _embeddingGenerator;
    private readonly IFactRepository _factRepository;
    private readonly OpenRouterAnalysisService _analysisService;
    private readonly EmbeddingSettings _embeddingSettings;
    private readonly AnalysisSettings _analysisSettings;
    private readonly ILogger<BotChatService> _logger;

    public BotChatService(
        ITextEmbeddingGenerator embeddingGenerator,
        IFactRepository factRepository,
        OpenRouterAnalysisService analysisService,
        IOptions<EmbeddingSettings> embeddingSettings,
        IOptions<AnalysisSettings> analysisSettings,
        ILogger<BotChatService> logger)
    {
        _embeddingGenerator = embeddingGenerator;
        _factRepository = factRepository;
        _analysisService = analysisService;
        _embeddingSettings = embeddingSettings.Value;
        _analysisSettings = analysisSettings.Value;
        _logger = logger;
    }

    public async Task<string> GenerateReplyAsync(string userMessage)
    {
        var normalizedMessage = (userMessage ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedMessage))
        {
            return "Please provide a message.";
        }

        var embedding = await _embeddingGenerator.GenerateAsync(
            _embeddingSettings.Model,
            normalizedMessage,
            CancellationToken.None);
        if (embedding.Length == 0)
        {
            _logger.LogWarning("Stage6 chat embedding is empty.");
            return "I cannot answer now because context retrieval failed.";
        }

        var facts = await _factRepository.SearchSimilarFactsAsync(embedding, DefaultFactLimit);
        if (facts.Count == 0)
        {
            return "I do not have relevant context facts yet.";
        }

        var systemPrompt = BotChatPromptBuilder.BuildSystemPrompt(facts);
        var model = ResolveReplyModel();
        var reply = await _analysisService.CompleteTextAsync(
            model,
            systemPrompt,
            normalizedMessage,
            ReplyMaxTokens,
            CancellationToken.None);

        if (string.IsNullOrWhiteSpace(reply))
        {
            return "I cannot answer based on the available context facts.";
        }

        return reply.Trim();
    }

    private string ResolveReplyModel()
    {
        if (!string.IsNullOrWhiteSpace(_analysisSettings.CheapBaselineModel))
        {
            return _analysisSettings.CheapBaselineModel.Trim();
        }

        if (!string.IsNullOrWhiteSpace(_analysisSettings.CheapModel))
        {
            return _analysisSettings.CheapModel.Trim();
        }

        return "openai/gpt-4o-mini";
    }

    private static class BotChatPromptBuilder
    {
        public static string BuildSystemPrompt(IReadOnlyCollection<Fact> facts)
        {
            var factsBlock = BuildFactsBlock(facts);
            return $"You are an AI personal assistant for Rinat. Answer his questions using ONLY the provided context facts. Be concise and direct. Context:\n{factsBlock}";
        }

        private static string BuildFactsBlock(IReadOnlyCollection<Fact> facts)
        {
            if (facts.Count == 0)
            {
                return "- no facts available";
            }

            var sb = new StringBuilder();
            var index = 1;
            foreach (var fact in facts)
            {
                sb.Append(index++)
                    .Append(". ")
                    .Append(fact.Category)
                    .Append('.')
                    .Append(fact.Key)
                    .Append('=')
                    .Append(fact.Value)
                    .Append(" (confidence=")
                    .Append(fact.Confidence.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture))
                    .AppendLine(")");
            }

            return sb.ToString().TrimEnd();
        }
    }
}
