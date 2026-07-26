using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using PointOfSale.Core.Analytics;
using PointOfSale.Infrastructure.Redis.Configuration;
using StackExchange.Redis;

namespace PointOfSale.Infrastructure.Analytics.Alerts;

/// <summary>
/// Implementation of IAnalyticsAlertStore using Redis + MongoDB.
/// </summary>
public class AnalyticsAlertStore : IAnalyticsAlertStore
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IMongoDatabase _mongoDb;
    private readonly ILogger<AnalyticsAlertStore> _logger;
    private readonly string _instanceName;

    private const string ActiveAlertKeyPrefix = "analytics:alerts:active:tenant:";
    private const string CooldownKeyPrefix = "analytics:alerts:cooldown:tenant:";
    private const string AlertsCollectionName = "analytics_alerts";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public AnalyticsAlertStore(
        IConnectionMultiplexer redis,
        IMongoDatabase mongoDatabase,
        IOptions<RedisSettings> redisSettings,
        ILogger<AnalyticsAlertStore> logger)
    {
        _redis = redis;
        _mongoDb = mongoDatabase;
        _instanceName = redisSettings.Value.InstanceName;
        _logger = logger;
    }

    #region Active Alerts (Redis)

    public async Task<List<AnalyticsAlert>> GetActiveAlertsAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var alerts = new List<AnalyticsAlert>();

        try
        {
            var db = _redis.GetDatabase();
            var pattern = $"{_instanceName}:{ActiveAlertKeyPrefix}{tenantId:N}:*";
            var server = _redis.GetServer(_redis.GetEndPoints().First());
            
            await foreach (var key in server.KeysAsync(pattern: pattern))
            {
                var value = await db.StringGetAsync(key);
                if (value.HasValue)
                {
                    var alert = JsonSerializer.Deserialize<AnalyticsAlert>(value!, JsonOptions);
                    if (alert != null)
                    {
                        alerts.Add(alert);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[AlertStore] Failed to get active alerts | TenantId: {TenantId}",
                tenantId);
        }

        return alerts.OrderByDescending(a => a.TriggeredAtUtc).ToList();
    }

    public async Task<bool> IsAlertActiveAsync(
        Guid tenantId,
        Guid ruleId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            var key = BuildActiveAlertKey(tenantId, ruleId);
            return await db.KeyExistsAsync(key);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[AlertStore] Failed to check active alert | TenantId: {TenantId} | RuleId: {RuleId}",
                tenantId, ruleId);
            return false;
        }
    }

    public async Task<AnalyticsAlert?> GetActiveAlertForRuleAsync(
        Guid tenantId,
        Guid ruleId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            var key = BuildActiveAlertKey(tenantId, ruleId);
            var value = await db.StringGetAsync(key);

            if (value.HasValue)
            {
                return JsonSerializer.Deserialize<AnalyticsAlert>(value!, JsonOptions);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[AlertStore] Failed to get active alert | TenantId: {TenantId} | RuleId: {RuleId}",
                tenantId, ruleId);
        }

        return null;
    }

    public async Task TriggerAlertAsync(
        AnalyticsAlert alert,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Store in Redis (active)
            var db = _redis.GetDatabase();
            var key = BuildActiveAlertKey(alert.TenantId, alert.RuleId);
            var json = JsonSerializer.Serialize(alert, JsonOptions);
            await db.StringSetAsync(key, json, TimeSpan.FromDays(7)); // TTL for safety

            // Store in MongoDB (audit)
            var collection = GetAlertsCollection();
            var document = MapToDocument(alert);
            await collection.InsertOneAsync(document, cancellationToken: cancellationToken);

            _logger.LogInformation(
                "[AlertStore] Alert TRIGGERED | TenantId: {TenantId} | RuleId: {RuleId} | Metric: {Metric} | Value: {Value} | Threshold: {Threshold}",
                alert.TenantId,
                alert.RuleId,
                alert.Metric,
                alert.ActualValue,
                alert.Threshold);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[AlertStore] Failed to trigger alert | TenantId: {TenantId} | RuleId: {RuleId}",
                alert.TenantId, alert.RuleId);
        }
    }

    public async Task ResolveAlertAsync(
        Guid tenantId,
        Guid ruleId,
        decimal currentValue,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Get the active alert
            var activeAlert = await GetActiveAlertForRuleAsync(tenantId, ruleId, cancellationToken);
            if (activeAlert == null) return;

            // Resolve it
            activeAlert.Resolve(currentValue);

            // Remove from Redis
            var db = _redis.GetDatabase();
            var key = BuildActiveAlertKey(tenantId, ruleId);
            await db.KeyDeleteAsync(key);

            // Update in MongoDB
            var collection = GetAlertsCollection();
            var filter = Builders<AnalyticsAlertDocument>.Filter.And(
                Builders<AnalyticsAlertDocument>.Filter.Eq(x => x.TenantId, tenantId.ToString()),
                Builders<AnalyticsAlertDocument>.Filter.Eq(x => x.AlertId, activeAlert.AlertId.ToString())
            );
            var update = Builders<AnalyticsAlertDocument>.Update
                .Set(x => x.IsResolved, true)
                .Set(x => x.ResolvedAtUtc, activeAlert.ResolvedAtUtc)
                .Set(x => x.ResolvedValue, activeAlert.ResolvedValue);

            await collection.UpdateOneAsync(filter, update, cancellationToken: cancellationToken);

            _logger.LogInformation(
                "[AlertStore] Alert RESOLVED | TenantId: {TenantId} | RuleId: {RuleId} | Duration: {Duration}",
                tenantId,
                ruleId,
                activeAlert.Duration);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[AlertStore] Failed to resolve alert | TenantId: {TenantId} | RuleId: {RuleId}",
                tenantId, ruleId);
        }
    }

    public async Task<bool> IsInCooldownAsync(
        Guid tenantId,
        Guid ruleId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            var key = BuildCooldownKey(tenantId, ruleId);
            return await db.KeyExistsAsync(key);
        }
        catch
        {
            return false;
        }
    }

    public async Task SetCooldownAsync(
        Guid tenantId,
        Guid ruleId,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            var key = BuildCooldownKey(tenantId, ruleId);
            await db.StringSetAsync(key, "1", duration);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[AlertStore] Failed to set cooldown | TenantId: {TenantId} | RuleId: {RuleId}",
                tenantId, ruleId);
        }
    }

    #endregion

    #region Alert History (MongoDB)

    public async Task<List<AnalyticsAlert>> GetAlertHistoryAsync(
        Guid tenantId,
        int limit = 100,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var collection = GetAlertsCollection();
            var filterBuilder = Builders<AnalyticsAlertDocument>.Filter;
            var filter = filterBuilder.Eq(x => x.TenantId, tenantId.ToString());

            if (fromDate.HasValue)
            {
                filter &= filterBuilder.Gte(x => x.TriggeredAtUtc, fromDate.Value);
            }

            if (toDate.HasValue)
            {
                filter &= filterBuilder.Lte(x => x.TriggeredAtUtc, toDate.Value);
            }

            var documents = await collection
                .Find(filter)
                .SortByDescending(x => x.TriggeredAtUtc)
                .Limit(limit)
                .ToListAsync(cancellationToken);

            return documents.Select(MapFromDocument).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[AlertStore] Failed to get alert history | TenantId: {TenantId}",
                tenantId);
            return new List<AnalyticsAlert>();
        }
    }

    public async Task<List<AnalyticsAlert>> GetAlertHistoryByRuleAsync(
        Guid tenantId,
        Guid ruleId,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var collection = GetAlertsCollection();
            var filter = Builders<AnalyticsAlertDocument>.Filter.And(
                Builders<AnalyticsAlertDocument>.Filter.Eq(x => x.TenantId, tenantId.ToString()),
                Builders<AnalyticsAlertDocument>.Filter.Eq(x => x.RuleId, ruleId.ToString())
            );

            var documents = await collection
                .Find(filter)
                .SortByDescending(x => x.TriggeredAtUtc)
                .Limit(limit)
                .ToListAsync(cancellationToken);

            return documents.Select(MapFromDocument).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[AlertStore] Failed to get rule alert history | TenantId: {TenantId} | RuleId: {RuleId}",
                tenantId, ruleId);
            return new List<AnalyticsAlert>();
        }
    }

    public async Task<AnalyticsAlert?> GetAlertByIdAsync(
        Guid tenantId,
        Guid alertId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var collection = GetAlertsCollection();
            var filter = Builders<AnalyticsAlertDocument>.Filter.And(
                Builders<AnalyticsAlertDocument>.Filter.Eq(x => x.TenantId, tenantId.ToString()),
                Builders<AnalyticsAlertDocument>.Filter.Eq(x => x.AlertId, alertId.ToString())
            );

            var document = await collection.Find(filter).FirstOrDefaultAsync(cancellationToken);
            return document != null ? MapFromDocument(document) : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[AlertStore] Failed to get alert | TenantId: {TenantId} | AlertId: {AlertId}",
                tenantId, alertId);
            return null;
        }
    }

    #endregion

    #region Helpers

    private string BuildActiveAlertKey(Guid tenantId, Guid ruleId)
    {
        return $"{_instanceName}:{ActiveAlertKeyPrefix}{tenantId:N}:{ruleId:N}";
    }

    private string BuildCooldownKey(Guid tenantId, Guid ruleId)
    {
        return $"{_instanceName}:{CooldownKeyPrefix}{tenantId:N}:{ruleId:N}";
    }

    private IMongoCollection<AnalyticsAlertDocument> GetAlertsCollection()
    {
        return _mongoDb.GetCollection<AnalyticsAlertDocument>(AlertsCollectionName);
    }

    private static AnalyticsAlertDocument MapToDocument(AnalyticsAlert alert)
    {
        return new AnalyticsAlertDocument
        {
            AlertId = alert.AlertId.ToString(),
            TenantId = alert.TenantId.ToString(),
            RuleId = alert.RuleId.ToString(),
            RuleName = alert.RuleName,
            Metric = alert.Metric.ToString(),
            ActualValue = alert.ActualValue,
            Threshold = alert.Threshold,
            Operator = alert.Operator.ToString(),
            Severity = alert.Severity.ToString(),
            ConditionDescription = alert.ConditionDescription,
            TriggeredAtUtc = alert.TriggeredAtUtc,
            ResolvedAtUtc = alert.ResolvedAtUtc,
            IsResolved = alert.IsResolved,
            ResolvedValue = alert.ResolvedValue
        };
    }

    private static AnalyticsAlert MapFromDocument(AnalyticsAlertDocument doc)
    {
        return new AnalyticsAlert
        {
            AlertId = Guid.Parse(doc.AlertId),
            TenantId = Guid.Parse(doc.TenantId),
            RuleId = Guid.Parse(doc.RuleId),
            RuleName = doc.RuleName,
            Metric = Enum.Parse<AnalyticsMetric>(doc.Metric),
            ActualValue = doc.ActualValue,
            Threshold = doc.Threshold,
            Operator = Enum.Parse<AnalyticsComparisonOperator>(doc.Operator),
            Severity = Enum.Parse<AnalyticsAlertSeverity>(doc.Severity),
            ConditionDescription = doc.ConditionDescription,
            TriggeredAtUtc = doc.TriggeredAtUtc,
            ResolvedAtUtc = doc.ResolvedAtUtc,
            IsResolved = doc.IsResolved,
            ResolvedValue = doc.ResolvedValue
        };
    }

    #endregion
}

/// <summary>
/// MongoDB document for analytics alerts.
/// </summary>
internal class AnalyticsAlertDocument
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    public string AlertId { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string RuleId { get; set; } = string.Empty;
    public string RuleName { get; set; } = string.Empty;
    public string Metric { get; set; } = string.Empty;
    public decimal ActualValue { get; set; }
    public decimal Threshold { get; set; }
    public string Operator { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string ConditionDescription { get; set; } = string.Empty;
    public DateTime TriggeredAtUtc { get; set; }
    public DateTime? ResolvedAtUtc { get; set; }
    public bool IsResolved { get; set; }
    public decimal? ResolvedValue { get; set; }
}
