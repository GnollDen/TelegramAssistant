using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Interfaces;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Intelligence.Stage5;

public class Neo4jSyncWorkerService : BackgroundService
{
    private const string EntityWatermarkKey = "neo4j:entity_watermark_ms";
    private const string RelationshipWatermarkKey = "neo4j:relationship_watermark_ms";
    private const string FactWatermarkKey = "neo4j:fact_watermark_ms";
    private static readonly TimeSpan BatchThrottleDelay = TimeSpan.FromMilliseconds(25);

    private readonly Neo4jSettings _settings;
    private readonly IDbContextFactory<TgAssistantDbContext> _dbFactory;
    private readonly IAnalysisStateRepository _stateRepository;
    private readonly HttpClient _http;
    private readonly ILogger<Neo4jSyncWorkerService> _logger;

    public Neo4jSyncWorkerService(
        IOptions<Neo4jSettings> settings,
        IDbContextFactory<TgAssistantDbContext> dbFactory,
        IAnalysisStateRepository stateRepository,
        HttpClient http,
        ILogger<Neo4jSyncWorkerService> logger)
    {
        _settings = settings.Value;
        _dbFactory = dbFactory;
        _stateRepository = stateRepository;
        _http = http;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled)
        {
            _logger.LogInformation("Neo4j sync worker is disabled");
            return;
        }

        ConfigureHttpClient();
        _logger.LogInformation("Neo4j sync worker started. db={Db}, batch={BatchSize}", _settings.Database, _settings.BatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var synced = await SyncOnceAsync(stoppingToken);
                if (!synced)
                {
                    await Task.Delay(TimeSpan.FromSeconds(_settings.PollIntervalSeconds), stoppingToken);
                }
                else
                {
                    await Task.Delay(BatchThrottleDelay, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Neo4j sync loop failed");
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(5, _settings.PollIntervalSeconds)), stoppingToken);
            }
        }
    }

    private void ConfigureHttpClient()
    {
        _http.BaseAddress = new Uri(_settings.BaseUrl.TrimEnd('/'));
        var authRaw = $"{_settings.Username}:{_settings.Password}";
        var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes(authRaw));
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", auth);
    }

    private async Task<bool> SyncOnceAsync(CancellationToken ct)
    {
        var entitySynced = await SyncEntitiesAsync(ct);
        var relationSynced = await SyncRelationshipsAsync(ct);
        var factSynced = await SyncFactsAsync(ct);
        return entitySynced || relationSynced || factSynced;
    }

    private async Task<bool> SyncEntitiesAsync(CancellationToken ct)
    {
        var watermarkMs = await _stateRepository.GetWatermarkAsync(EntityWatermarkKey, ct);
        var watermark = DateTimeOffset.FromUnixTimeMilliseconds(Math.Max(0, watermarkMs)).UtcDateTime;

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var rows = await db.Entities
            .AsNoTracking()
            .Where(x => x.UpdatedAt >= watermark)
            .OrderBy(x => x.UpdatedAt)
            .Take(_settings.BatchSize)
            .Select(x => new
            {
                x.Id,
                x.Type,
                x.Name,
                x.ActorKey,
                x.TelegramUserId,
                x.UpdatedAt
            })
            .ToListAsync(ct);

        if (rows.Count == 0)
        {
            return false;
        }

        var statements = rows.Select(x => new Neo4jStatement
        {
            Statement = """
                MERGE (e:Entity {id: $id})
                SET e.name = $name,
                    e.type = $type,
                    e.actor_key = $actor_key,
                    e.telegram_user_id = $telegram_user_id,
                    e.updated_at = $updated_at
                """,
            Parameters = new Dictionary<string, object?>
            {
                ["id"] = x.Id.ToString(),
                ["name"] = x.Name,
                ["type"] = x.Type,
                ["actor_key"] = x.ActorKey,
                ["telegram_user_id"] = x.TelegramUserId,
                ["updated_at"] = x.UpdatedAt.ToUniversalTime().ToString("O")
            }
        }).ToList();

        await CommitAsync(statements, ct);
        var nextMs = rows.Max(x => new DateTimeOffset(x.UpdatedAt).ToUnixTimeMilliseconds()) + 1;
        await _stateRepository.SetWatermarkAsync(EntityWatermarkKey, nextMs, ct);
        return true;
    }

    private async Task<bool> SyncRelationshipsAsync(CancellationToken ct)
    {
        var watermarkMs = await _stateRepository.GetWatermarkAsync(RelationshipWatermarkKey, ct);
        var watermark = DateTimeOffset.FromUnixTimeMilliseconds(Math.Max(0, watermarkMs)).UtcDateTime;

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var rows = await db.Relationships
            .AsNoTracking()
            .Where(x => x.UpdatedAt >= watermark)
            .OrderBy(x => x.UpdatedAt)
            .Take(_settings.BatchSize)
            .Select(x => new
            {
                x.Id,
                x.FromEntityId,
                x.ToEntityId,
                x.Type,
                x.Status,
                x.Confidence,
                x.ContextText,
                x.UpdatedAt
            })
            .ToListAsync(ct);

        if (rows.Count == 0)
        {
            return false;
        }

        var statements = rows.Select(x => new Neo4jStatement
        {
            Statement = """
                MERGE (from:Entity {id: $from_id})
                MERGE (to:Entity {id: $to_id})
                MERGE (from)-[r:RELATES_TO {id: $id}]->(to)
                SET r.rel_type = $rel_type,
                    r.status = $status,
                    r.confidence = $confidence,
                    r.context_text = $context_text,
                    r.updated_at = $updated_at
                """,
            Parameters = new Dictionary<string, object?>
            {
                ["id"] = x.Id.ToString(),
                ["from_id"] = x.FromEntityId.ToString(),
                ["to_id"] = x.ToEntityId.ToString(),
                ["rel_type"] = x.Type,
                ["status"] = x.Status,
                ["confidence"] = x.Confidence,
                ["context_text"] = x.ContextText,
                ["updated_at"] = x.UpdatedAt.ToUniversalTime().ToString("O")
            }
        }).ToList();

        await CommitAsync(statements, ct);
        var nextMs = rows.Max(x => new DateTimeOffset(x.UpdatedAt).ToUnixTimeMilliseconds()) + 1;
        await _stateRepository.SetWatermarkAsync(RelationshipWatermarkKey, nextMs, ct);
        return true;
    }

    private async Task<bool> SyncFactsAsync(CancellationToken ct)
    {
        var watermarkMs = await _stateRepository.GetWatermarkAsync(FactWatermarkKey, ct);
        var watermark = DateTimeOffset.FromUnixTimeMilliseconds(Math.Max(0, watermarkMs)).UtcDateTime;

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var rows = await db.Facts
            .AsNoTracking()
            .Where(x => x.UpdatedAt >= watermark)
            .OrderBy(x => x.UpdatedAt)
            .Take(_settings.BatchSize)
            .Select(x => new
            {
                x.Id,
                x.EntityId,
                x.Category,
                x.Key,
                x.Value,
                x.Status,
                x.Confidence,
                x.IsCurrent,
                x.UpdatedAt
            })
            .ToListAsync(ct);

        if (rows.Count == 0)
        {
            return false;
        }

        var statements = rows.Select(x => new Neo4jStatement
        {
            Statement = """
                MERGE (e:Entity {id: $entity_id})
                MERGE (f:Fact {id: $id})
                SET f.category = $category,
                    f.key = $key,
                    f.value = $value,
                    f.status = $status,
                    f.confidence = $confidence,
                    f.is_current = $is_current,
                    f.updated_at = $updated_at
                MERGE (e)-[:HAS_FACT]->(f)
                """,
            Parameters = new Dictionary<string, object?>
            {
                ["id"] = x.Id.ToString(),
                ["entity_id"] = x.EntityId.ToString(),
                ["category"] = x.Category,
                ["key"] = x.Key,
                ["value"] = x.Value,
                ["status"] = x.Status,
                ["confidence"] = x.Confidence,
                ["is_current"] = x.IsCurrent,
                ["updated_at"] = x.UpdatedAt.ToUniversalTime().ToString("O")
            }
        }).ToList();

        await CommitAsync(statements, ct);
        var nextMs = rows.Max(x => new DateTimeOffset(x.UpdatedAt).ToUnixTimeMilliseconds()) + 1;
        await _stateRepository.SetWatermarkAsync(FactWatermarkKey, nextMs, ct);
        return true;
    }

    private async Task CommitAsync(List<Neo4jStatement> statements, CancellationToken ct)
    {
        if (statements.Count == 0)
        {
            return;
        }

        var payload = new Neo4jCommitRequest
        {
            Statements = statements
        };
        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync($"/db/{_settings.Database}/tx/commit", content, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Neo4j sync failed {(int)response.StatusCode}: {body}");
        }

        using var document = JsonDocument.Parse(body);
        if (document.RootElement.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
        {
            throw new InvalidOperationException($"Neo4j returned errors: {errors}");
        }
    }

    private sealed class Neo4jCommitRequest
    {
        public List<Neo4jStatement> Statements { get; set; } = new();
    }

    private sealed class Neo4jStatement
    {
        public string Statement { get; set; } = string.Empty;
        public Dictionary<string, object?> Parameters { get; set; } = new();
    }
}
