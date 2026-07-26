using MongoDB.Driver;
using MongoDB.Driver.Linq;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.WhatsApp.Interfaces;

namespace PointOfSale.Infrastructure.WhatsApp.Services;

public class WhatsAppMessageLogService : IWhatsAppMessageLogService
{
    private readonly IMongoCollection<WhatsAppMessageLogDocument> _collection;

    public WhatsAppMessageLogService(IMongoDatabase database)
    {
        _collection = database.GetCollection<WhatsAppMessageLogDocument>("WhatsAppMessageLogs");
    }

    public async Task<WhatsAppMessageLogDocument> CreateLogAsync(WhatsAppMessageLogDocument log)
    {
        if (log.CreatedAtUtc == default) log.CreatedAtUtc = DateTime.UtcNow;
        if (string.IsNullOrWhiteSpace(log.IdempotencyKey))
        {
            throw new InvalidOperationException("WhatsApp message idempotency key is required.");
        }

        try
        {
            await _collection.InsertOneAsync(log);
            return log;
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            var existing = await _collection
                .Find(x => x.IdempotencyKey == log.IdempotencyKey)
                .FirstOrDefaultAsync();

            if (existing == null)
            {
                throw;
            }

            return existing;
        }
    }

    public async Task UpdateStatusAsync(string id, string status, string? failureReason = null)
    {
        var update = Builders<WhatsAppMessageLogDocument>.Update
            .Set(x => x.Status, status);

        if (failureReason != null)
        {
            update = update.Set(x => x.FailureReason, failureReason);
        }

        if (status == "SENT")
        {
            update = update.Set(x => x.SentAtUtc, DateTime.UtcNow);
        }

        await _collection.UpdateOneAsync(x => x.Id == id, update);
    }
    
    public async Task<WhatsAppMessageLogDocument?> GetByIdAsync(string id)
    {
        return await _collection.Find(x => x.Id == id).FirstOrDefaultAsync();
    }

    public async Task<WhatsAppMessageLogDocument?> TryClaimDispatchAsync(
        string id,
        string leaseId,
        DateTime leaseExpiresAtUtc,
        int maxAttempts,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var filter = Builders<WhatsAppMessageLogDocument>.Filter.And(
            Builders<WhatsAppMessageLogDocument>.Filter.Eq(x => x.Id, id),
            Builders<WhatsAppMessageLogDocument>.Filter.Lt(x => x.SendAttempts, maxAttempts),
            Builders<WhatsAppMessageLogDocument>.Filter.Or(
                Builders<WhatsAppMessageLogDocument>.Filter.In(
                    x => x.Status,
                    new[] { "PENDING", "QUEUED", "FAILED" }),
                Builders<WhatsAppMessageLogDocument>.Filter.And(
                    Builders<WhatsAppMessageLogDocument>.Filter.Eq(x => x.Status, "DISPATCHING"),
                    Builders<WhatsAppMessageLogDocument>.Filter.Lt(x => x.DispatchLeaseExpiresAtUtc, now))));

        var update = Builders<WhatsAppMessageLogDocument>.Update
            .Set(x => x.Status, "DISPATCHING")
            .Set(x => x.DispatchLeaseId, leaseId)
            .Set(x => x.DispatchLeaseExpiresAtUtc, leaseExpiresAtUtc)
            .Set(x => x.LastAttemptAtUtc, now)
            .Set(x => x.FailureReason, null)
            .Inc(x => x.SendAttempts, 1);

        return await _collection.FindOneAndUpdateAsync(
            filter,
            update,
            new FindOneAndUpdateOptions<WhatsAppMessageLogDocument>
            {
                ReturnDocument = ReturnDocument.After
            },
            ct);
    }

    public async Task<bool> CompleteDispatchAsync(
        string id,
        string tenantId,
        string accountId,
        string leaseId,
        string status,
        string? failureReason = null,
        string? providerMessageId = null,
        CancellationToken ct = default)
    {
        var filter = Builders<WhatsAppMessageLogDocument>.Filter.And(
            Builders<WhatsAppMessageLogDocument>.Filter.Eq(x => x.Id, id),
            Builders<WhatsAppMessageLogDocument>.Filter.Eq(x => x.TenantId, tenantId),
            Builders<WhatsAppMessageLogDocument>.Filter.Eq(x => x.AccountId, accountId),
            Builders<WhatsAppMessageLogDocument>.Filter.Eq(x => x.DispatchLeaseId, leaseId),
            Builders<WhatsAppMessageLogDocument>.Filter.Eq(x => x.Status, "DISPATCHING"));

        var update = Builders<WhatsAppMessageLogDocument>.Update
            .Set(x => x.Status, status)
            .Set(x => x.FailureReason, failureReason)
            .Set(x => x.ProviderMessageId, providerMessageId)
            .Set(x => x.DispatchLeaseId, null)
            .Set(x => x.DispatchLeaseExpiresAtUtc, null);

        if (status == "SENT")
        {
            update = update.Set(x => x.SentAtUtc, DateTime.UtcNow);
        }

        var result = await _collection.UpdateOneAsync(filter, update, cancellationToken: ct);
        return result.ModifiedCount == 1;
    }

    public async Task<List<WhatsAppMessageLogDocument>> GetPendingMessagesAsync(int maxRetries, int limit, CancellationToken ct = default)
    {
        var filterBuilder = Builders<WhatsAppMessageLogDocument>.Filter;
        
        // Only get messages that are marked as retryable
        // OTP messages are NOT retryable because their content is not persisted
        var retryableFilter = filterBuilder.Eq(x => x.IsRetryable, true);
        
        var statusFilter = filterBuilder.Or(
            filterBuilder.Eq(x => x.Status, "QUEUED"),
            filterBuilder.Eq(x => x.Status, "PENDING"),
            filterBuilder.And(
                filterBuilder.Eq(x => x.Status, "FAILED"),
                filterBuilder.Lt(x => x.SendAttempts, maxRetries)
            ),
            filterBuilder.And(
                filterBuilder.Eq(x => x.Status, "DISPATCHING"),
                filterBuilder.Lt(x => x.DispatchLeaseExpiresAtUtc, DateTime.UtcNow),
                filterBuilder.Lt(x => x.SendAttempts, maxRetries)
            )
        );

        var filter = filterBuilder.And(retryableFilter, statusFilter);

        return await _collection.Find(filter)
            .SortBy(x => x.CreatedAtUtc)
            .Limit(limit)
            .ToListAsync(ct);
    }

    public async Task<(List<WhatsAppMessageLogDocument> Items, long TotalCount)> GetLogsAsync(
        Guid tenantId,
        int page,
        int pageSize,
        string? status = null,
        string? messageType = null,
        CancellationToken ct = default)
    {
        var tenantIdStr = tenantId.ToString();
        var filterBuilder = Builders<WhatsAppMessageLogDocument>.Filter;
        var filter = filterBuilder.Eq(x => x.TenantId, tenantIdStr);

        if (!string.IsNullOrEmpty(status))
        {
            filter &= filterBuilder.Eq(x => x.Status, status);
        }

        if (!string.IsNullOrEmpty(messageType))
        {
            filter &= filterBuilder.Eq(x => x.MessageType, messageType);
        }

        var totalCount = await _collection.CountDocumentsAsync(filter, cancellationToken: ct);
        
        var items = await _collection.Find(filter)
            .SortByDescending(x => x.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync(ct);

        return (items, totalCount);
    }

    public async Task<WhatsAppMessageAnalytics> GetAnalyticsAsync(
        Guid tenantId,
        DateTime? from = null,
        DateTime? to = null,
        CancellationToken ct = default)
    {
        var tenantIdStr = tenantId.ToString();
        var filterBuilder = Builders<WhatsAppMessageLogDocument>.Filter;
        var filter = filterBuilder.Eq(x => x.TenantId, tenantIdStr);

        if (from.HasValue)
        {
            filter &= filterBuilder.Gte(x => x.CreatedAtUtc, from.Value);
        }

        if (to.HasValue)
        {
            filter &= filterBuilder.Lte(x => x.CreatedAtUtc, to.Value);
        }

        var allDocs = await _collection.Find(filter).ToListAsync(ct);

        var analytics = new WhatsAppMessageAnalytics
        {
            TotalMessagesSent = allDocs.Count(d => d.Status == "SENT"),
            TotalMessagesFailed = allDocs.Count(d => d.Status == "FAILED"),
            TotalMessagesPending = allDocs.Count(d => d.Status == "PENDING" || d.Status == "QUEUED"),
            MessagesByType = allDocs
                .GroupBy(d => d.MessageType ?? "Unknown")
                .ToDictionary(g => g.Key, g => (long)g.Count()),
            MessagesByStatus = allDocs
                .GroupBy(d => d.Status ?? "Unknown")
                .ToDictionary(g => g.Key, g => (long)g.Count())
        };

        var total = analytics.TotalMessagesSent + analytics.TotalMessagesFailed;
        analytics.SuccessRate = total > 0 ? (double)analytics.TotalMessagesSent / total * 100 : 0;

        return analytics;
    }
}

