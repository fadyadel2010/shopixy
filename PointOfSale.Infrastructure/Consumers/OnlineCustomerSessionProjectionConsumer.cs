using MassTransit;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using PointOfSale.Core.Events;
using PointOfSale.Infrastructure.MongoDB.Documents;
using StackExchange.Redis;

namespace PointOfSale.Infrastructure.Consumers;

/// <summary>
/// Projects Online Customer Session events from PostgreSQL (Write Model) to MongoDB (Read Model).
/// This consumer handles refresh token lifecycle events to build the admin session view.
/// 
/// ARCHITECTURAL RULES:
/// - Session = Refresh Token (one-to-one mapping)
/// - PostgreSQL (OnlineCustomerRefreshToken) is the ONLY source of truth
/// - Idempotent projections (safe to replay)
/// - Tenant isolation enforced - Guid.Empty rejected
/// - NO sensitive data in MongoDB (no token hash)
/// - Invalidates Redis session list cache on changes
/// - MIRRORS data from events - NEVER generates source-of-truth values
/// 
/// EVENTS CONSUMED:
/// - OnlineCustomerRefreshTokenIssuedEvent → Create session
/// - OnlineCustomerRefreshTokenRevokedEvent → Mark session revoked
/// - OnlineCustomerAllTokensRevokedEvent → Revoke all sessions for customer
/// - OnlineCustomerTokenReuseDetectedEvent → Mark sessions suspicious
/// 
/// SECURITY NOTES:
/// - IP addresses are masked for privacy
/// - User agents are sanitized/truncated
/// - Session count per customer cached for quick lookup
/// 
/// Phase: Admin Online Session Management (Production-Grade, Tenant-Safe)
/// </summary>
public class OnlineCustomerSessionProjectionConsumer :
    IConsumer<OnlineCustomerRefreshTokenIssuedEvent>,
    IConsumer<OnlineCustomerRefreshTokenRevokedEvent>,
    IConsumer<OnlineCustomerAllTokensRevokedEvent>,
    IConsumer<OnlineCustomerTokenReuseDetectedEvent>
{
    private readonly IMongoCollection<OnlineCustomerSessionDocument> _collection;
    private readonly IMongoCollection<OnlineCustomerDocument> _customerCollection;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<OnlineCustomerSessionProjectionConsumer> _logger;

    private const string SessionCacheKeyPrefix = "storefront:admin:sessions:{0}:{1}"; // tenantId:customerId
    private const string CollectionName = "online_customer_sessions";

    public OnlineCustomerSessionProjectionConsumer(
        IMongoDatabase mongoDatabase,
        IConnectionMultiplexer redis,
        ILogger<OnlineCustomerSessionProjectionConsumer> logger)
    {
        _collection = mongoDatabase.GetCollection<OnlineCustomerSessionDocument>(CollectionName);
        _customerCollection = mongoDatabase.GetCollection<OnlineCustomerDocument>("online_customers");
        _redis = redis;
        _logger = logger;

        // Ensure indexes exist
        EnsureIndexes();
    }

    private void EnsureIndexes()
    {
        try
        {
            var indexKeys = Builders<OnlineCustomerSessionDocument>.IndexKeys;

            // Unique composite index on (TenantId, SessionId)
            var uniqueIndex = new CreateIndexModel<OnlineCustomerSessionDocument>(
                indexKeys.Ascending(x => x.TenantId).Ascending(x => x.SessionId),
                new CreateIndexOptions { Unique = true, Name = "IX_TenantId_SessionId" });

            // Index on (TenantId, CustomerId) for listing sessions per customer
            var customerIndex = new CreateIndexModel<OnlineCustomerSessionDocument>(
                indexKeys.Ascending(x => x.TenantId).Ascending(x => x.CustomerId),
                new CreateIndexOptions { Name = "IX_TenantId_CustomerId" });

            // Index on (TenantId, IsRevoked) for filtering active/revoked
            var revokedIndex = new CreateIndexModel<OnlineCustomerSessionDocument>(
                indexKeys.Ascending(x => x.TenantId).Ascending(x => x.IsRevoked),
                new CreateIndexOptions { Name = "IX_TenantId_IsRevoked" });

            // Index on (TenantId, CreatedAtUtc) for sorting
            var createdIndex = new CreateIndexModel<OnlineCustomerSessionDocument>(
                indexKeys.Ascending(x => x.TenantId).Descending(x => x.CreatedAtUtc),
                new CreateIndexOptions { Name = "IX_TenantId_CreatedAtUtc" });

            // Index on (TenantId, IsSuspicious) for security monitoring
            var suspiciousIndex = new CreateIndexModel<OnlineCustomerSessionDocument>(
                indexKeys.Ascending(x => x.TenantId).Ascending(x => x.IsSuspicious),
                new CreateIndexOptions { Name = "IX_TenantId_IsSuspicious" });

            var eventOrderingIndex = new CreateIndexModel<OnlineCustomerSessionDocument>(
                indexKeys
                    .Ascending(x => x.TenantId)
                    .Ascending(x => x.SessionId)
                    .Descending(x => x.LastEventOccurredAtUtc)
                    .Descending(x => x.LastEventVersion),
                new CreateIndexOptions { Name = "IX_OnlineCustomerSessions_EventOrdering" });

            _collection.Indexes.CreateMany(new[] { uniqueIndex, customerIndex, revokedIndex, createdIndex, suspiciousIndex, eventOrderingIndex });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[OnlineCustomerSession] Failed to ensure MongoDB indexes for {Collection} collection",
                CollectionName);
        }
    }

    /// <summary>
    /// Handles new refresh token issuance - creates a new session document.
    /// </summary>
    public async Task Consume(ConsumeContext<OnlineCustomerRefreshTokenIssuedEvent> context)
    {
        var @event = context.Message;
        var cancellationToken = context.CancellationToken;

        if (!ValidateTenantId(@event.TenantId, @event.CustomerId, nameof(OnlineCustomerRefreshTokenIssuedEvent)))
            return;

        _logger.LogInformation(
            "[OnlineCustomerSession] Processing {Action} | TenantId: {TenantId} | CustomerId: {CustomerId} | SessionId: {SessionId} | TraceId: {TraceId}",
            "SESSION_CREATED",
            @event.TenantId,
            @event.CustomerId,
            @event.TokenId,
            context.MessageId);

        try
        {
            var customerState = await GetCustomerSessionProjectionStateAsync(
                @event.TenantId!.Value,
                @event.CustomerId,
                cancellationToken);

            var revokedByCustomerWatermark =
                customerState.LastSessionRevokedAtUtc.HasValue &&
                @event.IssuedAtUtc <= customerState.LastSessionRevokedAtUtc.Value;

            var update = Builders<OnlineCustomerSessionDocument>.Update
                .Set(x => x.CustomerId, @event.CustomerId)
                .Set(x => x.Email, MaskEmail(customerState.Email))
                .Set(x => x.Ip, MaskIp(@event.CreatedByIp))
                .Set(x => x.UserAgent, ParseUserAgent(@event.CreatedByUserAgent))
                .Set(x => x.CreatedAtUtc, @event.IssuedAtUtc)
                .Set(x => x.ExpiresAtUtc, @event.ExpiresAtUtc)
                .Set(x => x.LastSeenAtUtc, (DateTime?)null)
                .Set(x => x.IsRevoked, revokedByCustomerWatermark)
                .Set(x => x.RevokedAtUtc, revokedByCustomerWatermark ? customerState.LastSessionRevokedAtUtc : null)
                .Set(x => x.RevokedReason, revokedByCustomerWatermark ? customerState.LastSessionRevocationReason : null)
                .Set(x => x.IsSuspicious, false)
                .Set(x => x.SuspiciousReason, (string?)null)
                .Set(x => x.UpdatedAtUtc, @event.IssuedAtUtc)
                .SetOnInsert(x => x.TenantId, @event.TenantId!.Value)
                .SetOnInsert(x => x.SessionId, @event.TokenId)
                .SetOnInsert(x => x.SchemaVersion, 1);

            var applied = await ApplySessionProjectionUpdateAsync(
                @event.TenantId!.Value,
                @event.TokenId,
                @event,
                update,
                isUpsert: true,
                cancellationToken);

            if (!applied)
            {
                _logger.LogInformation(
                    "[OnlineCustomerSession] {Action} ignored as stale or duplicate | TenantId: {TenantId} | CustomerId: {CustomerId} | SessionId: {SessionId} | EventId: {EventId}",
                    "SESSION_CREATED",
                    @event.TenantId,
                    @event.CustomerId,
                    @event.TokenId,
                    @event.EventId);
                return;
            }

            await InvalidateSessionCacheAsync(@event.TenantId!.Value, @event.CustomerId);

            _logger.LogInformation(
                "[OnlineCustomerSession] {Action} projected successfully | TenantId: {TenantId} | CustomerId: {CustomerId} | SessionId: {SessionId}",
                "SESSION_CREATED",
                @event.TenantId,
                @event.CustomerId,
                @event.TokenId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[OnlineCustomerSession] Failed to project {Action} | TenantId: {TenantId} | CustomerId: {CustomerId} | SessionId: {SessionId}",
                "SESSION_CREATED",
                @event.TenantId,
                @event.CustomerId,
                @event.TokenId);
            throw; // Retry via MassTransit
        }
    }

    /// <summary>
    /// Handles single token revocation - marks session as revoked.
    /// </summary>
    public async Task Consume(ConsumeContext<OnlineCustomerRefreshTokenRevokedEvent> context)
    {
        var @event = context.Message;
        var cancellationToken = context.CancellationToken;

        if (!ValidateTenantId(@event.TenantId, @event.CustomerId, nameof(OnlineCustomerRefreshTokenRevokedEvent)))
            return;

        _logger.LogWarning(
            "[OnlineCustomerSession] Processing {Action} | TenantId: {TenantId} | CustomerId: {CustomerId} | SessionId: {SessionId} | Reason: {Reason} | TraceId: {TraceId}",
            "SESSION_REVOKED",
            @event.TenantId,
            @event.CustomerId,
            @event.TokenId,
            @event.Reason,
            context.MessageId);

        try
        {
            var update = Builders<OnlineCustomerSessionDocument>.Update
                .Set(x => x.CustomerId, @event.CustomerId)
                .Set(x => x.IsRevoked, true)
                .Set(x => x.RevokedAtUtc, @event.RevokedAtUtc)
                .Set(x => x.RevokedReason, @event.Reason)
                .Set(x => x.UpdatedAtUtc, @event.RevokedAtUtc);

            var applied = await ApplySessionProjectionUpdateAsync(
                @event.TenantId!.Value,
                @event.TokenId,
                @event,
                update,
                isUpsert: false,
                cancellationToken);

            if (!applied)
            {
                _logger.LogInformation(
                    "[OnlineCustomerSession] {Action} ignored as stale or duplicate | TenantId: {TenantId} | CustomerId: {CustomerId} | SessionId: {SessionId} | EventId: {EventId}",
                    "SESSION_REVOKED",
                    @event.TenantId,
                    @event.CustomerId,
                    @event.TokenId,
                    @event.EventId);
                return;
            }

            await InvalidateSessionCacheAsync(@event.TenantId!.Value, @event.CustomerId);

            _logger.LogWarning(
                "[OnlineCustomerSession] {Action} projected successfully | TenantId: {TenantId} | CustomerId: {CustomerId} | SessionId: {SessionId}",
                "SESSION_REVOKED",
                @event.TenantId,
                @event.CustomerId,
                @event.TokenId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[OnlineCustomerSession] Failed to project {Action} | TenantId: {TenantId} | CustomerId: {CustomerId} | SessionId: {SessionId}",
                "SESSION_REVOKED",
                @event.TenantId,
                @event.CustomerId,
                @event.TokenId);
            throw;
        }
    }

    /// <summary>
    /// Handles bulk revocation - marks ALL sessions for a customer as revoked.
    /// </summary>
    public async Task Consume(ConsumeContext<OnlineCustomerAllTokensRevokedEvent> context)
    {
        var @event = context.Message;
        var cancellationToken = context.CancellationToken;

        if (!ValidateTenantId(@event.TenantId, @event.CustomerId, nameof(OnlineCustomerAllTokensRevokedEvent)))
            return;

        _logger.LogWarning(
            "[OnlineCustomerSession] Processing {Action} | TenantId: {TenantId} | CustomerId: {CustomerId} | TokensRevoked: {Count} | Reason: {Reason} | TraceId: {TraceId}",
            "ALL_SESSIONS_REVOKED",
            @event.TenantId,
            @event.CustomerId,
            @event.TokensRevoked,
            @event.Reason,
            context.MessageId);

        try
        {
            var watermarkApplied = await ApplyCustomerSessionRevocationWatermarkAsync(@event, cancellationToken);
            
            if (!watermarkApplied)
            {
                _logger.LogInformation(
                    "[OnlineCustomerSession] {Action} ignored as stale or duplicate | TenantId: {TenantId} | CustomerId: {CustomerId} | EventId: {EventId}",
                    "ALL_SESSIONS_REVOKED",
                    @event.TenantId,
                    @event.CustomerId,
                    @event.EventId);
                return;
            }

            // Revoke only sessions that existed at the source revocation time. A token issued after
            // this event should remain active even if its event is delivered later.
            var filter = BuildFreshCustomerSessionEventFilter(
                @event.TenantId!.Value,
                @event.CustomerId,
                @event.EventId,
                @event.OccurredAtUtc,
                @event.EventVersion,
                includeOnlyExistingAtUtc: @event.RevokedAtUtc);

            var update = Builders<OnlineCustomerSessionDocument>.Update
                .Set(x => x.IsRevoked, true)
                .Set(x => x.RevokedAtUtc, @event.RevokedAtUtc)
                .Set(x => x.RevokedReason, @event.Reason)
                .Set(x => x.UpdatedAtUtc, @event.RevokedAtUtc)
                .Set(x => x.LastEventId, @event.EventId)
                .Set(x => x.LastEventOccurredAtUtc, @event.OccurredAtUtc)
                .Set(x => x.LastEventVersion, @event.EventVersion);

            var result = await _collection.UpdateManyAsync(filter, update, cancellationToken: cancellationToken);

            await InvalidateSessionCacheAsync(@event.TenantId!.Value, @event.CustomerId);

            _logger.LogWarning(
                "[OnlineCustomerSession] {Action} projected successfully | TenantId: {TenantId} | CustomerId: {CustomerId} | DocumentsUpdated: {Count}",
                "ALL_SESSIONS_REVOKED",
                @event.TenantId,
                @event.CustomerId,
                result.ModifiedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[OnlineCustomerSession] Failed to project {Action} | TenantId: {TenantId} | CustomerId: {CustomerId}",
                "ALL_SESSIONS_REVOKED",
                @event.TenantId,
                @event.CustomerId);
            throw;
        }
    }

    /// <summary>
    /// Handles token reuse detection - marks sessions as suspicious (security incident).
    /// </summary>
    public async Task Consume(ConsumeContext<OnlineCustomerTokenReuseDetectedEvent> context)
    {
        var @event = context.Message;
        var cancellationToken = context.CancellationToken;

        if (!ValidateTenantId(@event.TenantId, @event.CustomerId, nameof(OnlineCustomerTokenReuseDetectedEvent)))
            return;

        // CRITICAL: This is a security incident - log at highest level
        _logger.LogCritical(
            "[OnlineCustomerSession] SECURITY_INCIDENT: {Action} | TenantId: {TenantId} | CustomerId: {CustomerId} | RevokedTokenId: {TokenId} | AttemptedFromIp: {Ip} | TokensRevoked: {Count} | TraceId: {TraceId}",
            "TOKEN_REUSE_DETECTED",
            @event.TenantId,
            @event.CustomerId,
            @event.RevokedTokenId,
            MaskIp(@event.AttemptedFromIp),
            @event.TokensRevoked,
            context.MessageId);

        try
        {
            // Mark all current sessions for this customer as suspicious if this security event is
            // fresher than the session's last projected lifecycle/security event.
            var filter = BuildFreshCustomerSessionEventFilter(
                @event.TenantId!.Value,
                @event.CustomerId,
                @event.EventId,
                @event.OccurredAtUtc,
                @event.EventVersion);

            var suspiciousReason = $"Token reuse detected from IP: {MaskIp(@event.AttemptedFromIp)} at {@event.DetectedAtUtc:u}";

            var update = Builders<OnlineCustomerSessionDocument>.Update
                .Set(x => x.IsSuspicious, true)
                .Set(x => x.SuspiciousReason, suspiciousReason)
                .Set(x => x.UpdatedAtUtc, @event.DetectedAtUtc)
                .Set(x => x.LastEventId, @event.EventId)
                .Set(x => x.LastEventOccurredAtUtc, @event.OccurredAtUtc)
                .Set(x => x.LastEventVersion, @event.EventVersion);

            var result = await _collection.UpdateManyAsync(filter, update, cancellationToken: cancellationToken);

            await InvalidateSessionCacheAsync(@event.TenantId!.Value, @event.CustomerId);

            _logger.LogCritical(
                "[OnlineCustomerSession] {Action} - All sessions marked suspicious | TenantId: {TenantId} | CustomerId: {CustomerId} | DocumentsUpdated: {Count}",
                "TOKEN_REUSE_DETECTED",
                @event.TenantId,
                @event.CustomerId,
                result.ModifiedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[OnlineCustomerSession] Failed to project {Action} | TenantId: {TenantId} | CustomerId: {CustomerId}",
                "TOKEN_REUSE_DETECTED",
                @event.TenantId,
                @event.CustomerId);
            throw;
        }
    }

    #region Helper Methods

    private bool ValidateTenantId(Guid? tenantId, Guid customerId, string eventName)
    {
        if (!tenantId.HasValue || tenantId.Value == Guid.Empty)
        {
            _logger.LogError(
                "[OnlineCustomerSession] SECURITY: {EventName} REJECTED - Missing or empty TenantId | CustomerId: {CustomerId}",
                eventName, customerId);
            return false;
        }
        return true;
    }

    private async Task<CustomerSessionProjectionState> GetCustomerSessionProjectionStateAsync(
        Guid tenantId,
        Guid customerId,
        CancellationToken cancellationToken)
    {
        try
        {
            var customer = await _customerCollection
                .Find(c => c.TenantId == tenantId && c.CustomerId == customerId)
                .Project(c => new CustomerSessionProjectionState(
                    c.Email,
                    c.LastSessionRevokedAtUtc,
                    c.LastSessionRevocationReason))
                .FirstOrDefaultAsync(cancellationToken);

            return customer ?? new CustomerSessionProjectionState(null, null, null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[OnlineCustomerSession] Failed to fetch customer session projection state (non-critical) | TenantId: {TenantId} | CustomerId: {CustomerId}",
                tenantId, customerId);
            return new CustomerSessionProjectionState(null, null, null);
        }
    }

    private async Task<bool> ApplySessionProjectionUpdateAsync(
        Guid tenantId,
        Guid sessionId,
        IntegrationEvent @event,
        UpdateDefinition<OnlineCustomerSessionDocument> update,
        bool isUpsert,
        CancellationToken cancellationToken)
    {
        var filter = BuildFreshSessionEventFilter(
            tenantId,
            sessionId,
            @event.EventId,
            @event.OccurredAtUtc,
            @event.EventVersion);

        var updateWithMetadata = update
            .Set(x => x.LastEventId, @event.EventId)
            .Set(x => x.LastEventOccurredAtUtc, @event.OccurredAtUtc)
            .Set(x => x.LastEventVersion, @event.EventVersion);

        try
        {
            var result = await _collection.UpdateOneAsync(
                filter,
                updateWithMetadata,
                new UpdateOptions { IsUpsert = isUpsert },
                cancellationToken);

            return result.MatchedCount > 0 || result.ModifiedCount > 0 || result.UpsertedId != null;
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            return false;
        }
    }

    private async Task<bool> ApplyCustomerSessionRevocationWatermarkAsync(
        OnlineCustomerAllTokensRevokedEvent @event,
        CancellationToken cancellationToken)
    {
        var filterBuilder = Builders<OnlineCustomerDocument>.Filter;
        var filter = filterBuilder.And(
            filterBuilder.Eq(x => x.TenantId, @event.TenantId!.Value),
            filterBuilder.Eq(x => x.CustomerId, @event.CustomerId),
            filterBuilder.Ne(x => x.LastSessionRevocationEventId, @event.EventId),
            filterBuilder.Or(
                filterBuilder.Exists(x => x.LastSessionRevocationEventOccurredAtUtc, false),
                filterBuilder.Eq(x => x.LastSessionRevocationEventOccurredAtUtc, null),
                filterBuilder.Lt(x => x.LastSessionRevocationEventOccurredAtUtc, @event.OccurredAtUtc),
                filterBuilder.And(
                    filterBuilder.Eq(x => x.LastSessionRevocationEventOccurredAtUtc, @event.OccurredAtUtc),
                    filterBuilder.Lt(x => x.LastSessionRevocationEventVersion, @event.EventVersion))));

        var update = Builders<OnlineCustomerDocument>.Update
            .Set(x => x.LastSessionRevocationEventId, @event.EventId)
            .Set(x => x.LastSessionRevocationEventOccurredAtUtc, @event.OccurredAtUtc)
            .Set(x => x.LastSessionRevocationEventVersion, @event.EventVersion)
            .Set(x => x.LastSessionRevokedAtUtc, @event.RevokedAtUtc)
            .Set(x => x.LastSessionRevocationReason, @event.Reason)
            .Set(x => x.UpdatedAtUtc, @event.RevokedAtUtc)
            .SetOnInsert(x => x.TenantId, @event.TenantId!.Value)
            .SetOnInsert(x => x.CustomerId, @event.CustomerId)
            .SetOnInsert(x => x.SchemaVersion, 2);

        try
        {
            var result = await _customerCollection.UpdateOneAsync(
                filter,
                update,
                new UpdateOptions { IsUpsert = true },
                cancellationToken);

            // Return true if document was matched/modified OR if it was inserted (upserted)
            // This ensures session revocation proceeds even if customer document didn't exist
            return result.MatchedCount > 0 || result.ModifiedCount > 0 || result.UpsertedId != null;
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            // Duplicate key means another event created the document concurrently
            // Check if this event is stale by querying the existing document
            var existing = await _customerCollection
                .Find(filterBuilder.And(
                    filterBuilder.Eq(x => x.TenantId, @event.TenantId!.Value),
                    filterBuilder.Eq(x => x.CustomerId, @event.CustomerId)))
                .FirstOrDefaultAsync(cancellationToken);

            if (existing == null)
            {
                // Document disappeared, allow session revocation to proceed
                return true;
            }

            // Check if this event is stale compared to what's stored
            if (existing.LastSessionRevocationEventId == @event.EventId)
            {
                // Same event, already processed
                return false;
            }

            if (existing.LastSessionRevocationEventOccurredAtUtc.HasValue)
            {
                if (existing.LastSessionRevocationEventOccurredAtUtc.Value > @event.OccurredAtUtc)
                {
                    // Stored event is newer by timestamp
                    return false;
                }

                if (existing.LastSessionRevocationEventOccurredAtUtc.Value == @event.OccurredAtUtc &&
                    existing.LastSessionRevocationEventVersion >= @event.EventVersion)
                {
                    // Same timestamp but stored version is equal or newer
                    return false;
                }
            }

            // This event is fresher, allow session revocation to proceed
            return true;
        }
    }

    private static FilterDefinition<OnlineCustomerSessionDocument> BuildFreshSessionEventFilter(
        Guid tenantId,
        Guid sessionId,
        Guid eventId,
        DateTime occurredAtUtc,
        int eventVersion)
    {
        var filter = Builders<OnlineCustomerSessionDocument>.Filter;

        return filter.And(
            filter.Eq(x => x.TenantId, tenantId),
            filter.Eq(x => x.SessionId, sessionId),
            filter.Ne(x => x.LastEventId, eventId),
            BuildFreshSessionEventMetadataFilter(eventId, occurredAtUtc, eventVersion));
    }

    private static FilterDefinition<OnlineCustomerSessionDocument> BuildFreshCustomerSessionEventFilter(
        Guid tenantId,
        Guid customerId,
        Guid eventId,
        DateTime occurredAtUtc,
        int eventVersion,
        DateTime? includeOnlyExistingAtUtc = null)
    {
        var filter = Builders<OnlineCustomerSessionDocument>.Filter;
        var filters = new List<FilterDefinition<OnlineCustomerSessionDocument>>
        {
            filter.Eq(x => x.TenantId, tenantId),
            filter.Eq(x => x.CustomerId, customerId),
            filter.Ne(x => x.LastEventId, eventId),
            BuildFreshSessionEventMetadataFilter(eventId, occurredAtUtc, eventVersion)
        };

        if (includeOnlyExistingAtUtc.HasValue)
        {
            filters.Add(filter.Lte(x => x.CreatedAtUtc, includeOnlyExistingAtUtc.Value));
        }

        return filter.And(filters);
    }

    private static FilterDefinition<OnlineCustomerSessionDocument> BuildFreshSessionEventMetadataFilter(
        Guid eventId,
        DateTime occurredAtUtc,
        int eventVersion)
    {
        var filter = Builders<OnlineCustomerSessionDocument>.Filter;

        return filter.Or(
            filter.Exists(x => x.LastEventOccurredAtUtc, false),
            filter.Eq(x => x.LastEventOccurredAtUtc, null),
            filter.Lt(x => x.LastEventOccurredAtUtc, occurredAtUtc),
            filter.And(
                filter.Eq(x => x.LastEventOccurredAtUtc, occurredAtUtc),
                filter.Lt(x => x.LastEventVersion, eventVersion)));
    }

    private sealed record CustomerSessionProjectionState(
        string? Email,
        DateTime? LastSessionRevokedAtUtc,
        string? LastSessionRevocationReason);

    private async Task InvalidateSessionCacheAsync(Guid tenantId, Guid customerId)
    {
        try
        {
            var db = _redis.GetDatabase();
            var cacheKey = string.Format(SessionCacheKeyPrefix, tenantId, customerId);
            await db.KeyDeleteAsync(cacheKey);

            _logger.LogDebug(
                "[OnlineCustomerSession] Session cache invalidated | TenantId: {TenantId} | CustomerId: {CustomerId}",
                tenantId, customerId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[OnlineCustomerSession] Cache invalidation failed (non-critical) | TenantId: {TenantId} | CustomerId: {CustomerId}",
                tenantId, customerId);
            // Don't throw - cache invalidation failure shouldn't fail the projection
        }
    }

    /// <summary>
    /// Masks IP address for privacy (last octet for IPv4, last segment for IPv6).
    /// </summary>
    private static string MaskIp(string? ip)
    {
        if (string.IsNullOrWhiteSpace(ip))
            return "[unknown]";

        var lastDot = ip.LastIndexOf('.');
        if (lastDot > 0)
            return ip[..(lastDot + 1)] + "xxx";

        var lastColon = ip.LastIndexOf(':');
        if (lastColon > 0)
            return ip[..(lastColon + 1)] + "xxxx";

        return ip[..Math.Min(ip.Length, 3)] + "xxx";
    }

    /// <summary>
    /// Masks email for privacy (u***@example.com format).
    /// </summary>
    private static string? MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return null;

        var atIndex = email.IndexOf('@');
        if (atIndex <= 1)
            return "***@" + (atIndex >= 0 ? email[(atIndex + 1)..] : "***");

        return email[0] + "***" + email[(atIndex - 1)..];
    }

    /// <summary>
    /// Parses and simplifies user agent for display (e.g., "Chrome / Windows").
    /// Truncates to 512 chars max for storage.
    /// </summary>
    private static string? ParseUserAgent(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
            return null;

        // Truncate if too long
        if (userAgent.Length > 512)
            userAgent = userAgent[..512];

        // Try to extract browser and OS for simplified display
        // This is a basic implementation - could be enhanced with a proper UA parser
        var browser = "Unknown";
        var os = "Unknown";

        var ua = userAgent.ToLowerInvariant();

        // Detect browser
        if (ua.Contains("edg/") || ua.Contains("edge/"))
            browser = "Edge";
        else if (ua.Contains("chrome/") && !ua.Contains("chromium/"))
            browser = "Chrome";
        else if (ua.Contains("firefox/"))
            browser = "Firefox";
        else if (ua.Contains("safari/") && !ua.Contains("chrome/"))
            browser = "Safari";
        else if (ua.Contains("opera/") || ua.Contains("opr/"))
            browser = "Opera";
        else if (ua.Contains("msie") || ua.Contains("trident/"))
            browser = "IE";

        // Detect OS
        if (ua.Contains("windows"))
            os = "Windows";
        else if (ua.Contains("mac os") || ua.Contains("macos"))
            os = "macOS";
        else if (ua.Contains("linux") && !ua.Contains("android"))
            os = "Linux";
        else if (ua.Contains("android"))
            os = "Android";
        else if (ua.Contains("iphone") || ua.Contains("ipad") || ua.Contains("ipod"))
            os = "iOS";

        if (browser == "Unknown" && os == "Unknown")
            return userAgent.Length > 50 ? userAgent[..50] + "..." : userAgent;

        return $"{browser} / {os}";
    }

    #endregion
}

/// <summary>
/// Consumer definition for OnlineCustomerSessionProjectionConsumer.
/// Configures endpoint name and retry policy.
/// </summary>
public class OnlineCustomerSessionProjectionConsumerDefinition : ConsumerDefinition<OnlineCustomerSessionProjectionConsumer>
{
    public OnlineCustomerSessionProjectionConsumerDefinition()
    {
        EndpointName = "online-customer-session-projection";
    }

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<OnlineCustomerSessionProjectionConsumer> consumerConfigurator)
    {
        endpointConfigurator.UseMessageRetry(r => r.Intervals(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(15)));
    }
}
