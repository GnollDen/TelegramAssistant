using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Infrastructure.Database;

public class Stage6ArtifactFreshnessService : IStage6ArtifactFreshnessService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IMessageRepository _messageRepository;
    private readonly IClarificationRepository _clarificationRepository;
    private readonly IOfflineEventRepository _offlineEventRepository;
    private readonly IPeriodRepository _periodRepository;
    private readonly IInboxConflictRepository _inboxConflictRepository;

    public Stage6ArtifactFreshnessService(
        IMessageRepository messageRepository,
        IClarificationRepository clarificationRepository,
        IOfflineEventRepository offlineEventRepository,
        IPeriodRepository periodRepository,
        IInboxConflictRepository inboxConflictRepository)
    {
        _messageRepository = messageRepository;
        _clarificationRepository = clarificationRepository;
        _offlineEventRepository = offlineEventRepository;
        _periodRepository = periodRepository;
        _inboxConflictRepository = inboxConflictRepository;
    }

    public async Task<Stage6ArtifactEvidenceStamp> BuildEvidenceStampAsync(
        long caseId,
        long chatId,
        string artifactType,
        CancellationToken ct = default)
    {
        var latestMessage = (await _messageRepository.GetProcessedByChatAsync(chatId, 1, ct))
            .OrderByDescending(x => x.Timestamp)
            .FirstOrDefault()
            ?.Timestamp;

        var questions = (await _clarificationRepository.GetQuestionsAsync(caseId, null, null, ct))
            .Where(x => x.ChatId == null || x.ChatId == chatId)
            .ToList();

        var latestQuestionUpdated = questions.Count == 0
            ? (DateTime?)null
            : questions.Max(x => x.UpdatedAt);

        var latestAnswer = DateTime.MinValue;
        foreach (var question in questions.OrderByDescending(x => x.UpdatedAt).Take(20))
        {
            var answer = (await _clarificationRepository.GetAnswersByQuestionIdAsync(question.Id, ct)).FirstOrDefault();
            if (answer != null && answer.CreatedAt > latestAnswer)
            {
                latestAnswer = answer.CreatedAt;
            }
        }

        var latestOffline = (await _offlineEventRepository.GetOfflineEventsByCaseAsync(caseId, ct))
            .Where(x => x.ChatId == null || x.ChatId == chatId)
            .Select(x => x.UpdatedAt >= x.TimestampStart ? x.UpdatedAt : x.TimestampStart)
            .DefaultIfEmpty(DateTime.MinValue)
            .Max();

        DateTime latestHypothesis = DateTime.MinValue;
        DateTime latestConflict = DateTime.MinValue;
        if (string.Equals(artifactType, Stage6ArtifactTypes.Dossier, StringComparison.OrdinalIgnoreCase))
        {
            latestHypothesis = (await _periodRepository.GetHypothesesByCaseAsync(caseId, null, ct))
                .Where(x => x.ChatId == null || x.ChatId == chatId)
                .Select(x => x.UpdatedAt)
                .DefaultIfEmpty(DateTime.MinValue)
                .Max();

            latestConflict = (await _inboxConflictRepository.GetConflictRecordsAsync(caseId, null, ct))
                .Where(x => x.ChatId == null || x.ChatId == chatId)
                .Select(x => x.UpdatedAt)
                .DefaultIfEmpty(DateTime.MinValue)
                .Max();
        }

        var candidates = new[]
        {
            latestMessage,
            latestQuestionUpdated,
            latestAnswer == DateTime.MinValue ? null : latestAnswer,
            latestOffline == DateTime.MinValue ? null : latestOffline,
            latestHypothesis == DateTime.MinValue ? null : latestHypothesis,
            latestConflict == DateTime.MinValue ? null : latestConflict
        };

        var latestEvidence = candidates
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .DefaultIfEmpty()
            .Max();

        var basis = new
        {
            latest_message_at = latestMessage,
            latest_question_updated_at = latestQuestionUpdated,
            latest_answer_at = latestAnswer == DateTime.MinValue ? (DateTime?)null : latestAnswer,
            latest_offline_event_at = latestOffline == DateTime.MinValue ? (DateTime?)null : latestOffline,
            latest_hypothesis_at = latestHypothesis == DateTime.MinValue ? (DateTime?)null : latestHypothesis,
            latest_conflict_at = latestConflict == DateTime.MinValue ? (DateTime?)null : latestConflict
        };

        var basisJson = JsonSerializer.Serialize(basis, JsonOptions);
        return new Stage6ArtifactEvidenceStamp
        {
            LatestEvidenceAtUtc = latestEvidence == default ? null : latestEvidence,
            BasisJson = basisJson,
            BasisHash = ComputeBasisHash(basisJson)
        };
    }

    public TimeSpan ResolveTtl(string artifactType)
    {
        return artifactType switch
        {
            Stage6ArtifactTypes.CurrentState => TimeSpan.FromHours(6),
            Stage6ArtifactTypes.ClarificationState => TimeSpan.FromHours(3),
            Stage6ArtifactTypes.Dossier => TimeSpan.FromHours(12),
            Stage6ArtifactTypes.Strategy => TimeSpan.FromHours(12),
            Stage6ArtifactTypes.Draft => TimeSpan.FromHours(12),
            Stage6ArtifactTypes.Review => TimeSpan.FromHours(8),
            _ => TimeSpan.FromHours(12)
        };
    }

    private static string ComputeBasisHash(string basisJson)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(basisJson));
        return Convert.ToHexString(bytes);
    }
}
