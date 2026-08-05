using MassTransit;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using PointOfSale.Core.Events;
using PointOfSale.Core.Helpers;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.Redis.Services;

namespace PointOfSale.Infrastructure.Consumers;

/// <summary>
/// Projects Online Customer events from PostgreSQL (Write Model) to MongoDB (Read Model).
/// 
/// ARCHITECTURAL RULES:
/// - Handles all OnlineCustomer events
/// - Idempotent projections (safe to replay)
/// - Tenant isolation enforced - Guid.Empty rejected
/// - NO sensitive data in MongoDB (no password hash)
/// - Invalidates the owned wishlist cache on deactivation
/// - MIRRORS data from events - NEVER generates source-of-truth values
/// 
/// SECURITY NOTES:
/// - LastLoginAtUtc: Mirrored from event, NOT computed by consumer
/// - PostgreSQL is the ONLY source of truth for all timestamps
/// - Emails are normalized using EmailNormalizer.Normalize()
/// 
/// Phase: 10.3.1 - Online Customer Identity (Core)
/// Phase: 10.3.1 Hardening - Security & Correctness Fixes
/// Phase F2: Account Area - Added OnlineCustomerUpdatedEvent and OnlineCustomerPasswordChangedEvent
/// </summary>
public class OnlineCustomerProjectionConsumer :
    IConsumer<OnlineCustomerRegisteredEvent>,
    IConsumer<OnlineCustomerActivatedEvent>,
    IConsumer<OnlineCustomerDeactivatedEvent>,
    IConsumer<OnlineCustomerEmailVerifiedEvent>,
    IConsumer<OnlineCustomerLoggedInEvent>,
    IConsumer<OnlineCustomerUpdatedEvent>,
    IConsumer<OnlineCustomerPasswordChangedEvent>
{
    private readonly IMongoCollection<OnlineCustomerDocument> _collection;
    private readonly IStorefrontWishlistCacheInvalidator _wishlistCacheInvalidator;
    private readonly ILogger<OnlineCustomerProjectionConsumer> _logger;

    public OnlineCustomerProjectionConsumer(
        IMongoDatabase mongoDatabase,
        IStorefrontWishlistCacheInvalidator wishlistCacheInvalidator,
        ILogger<OnlineCustomerProjectionConsumer> logger)
    {
        _collection = mongoDatabase.GetCollection<OnlineCustomerDocument>("online_customers");
        _wishlistCacheInvalidator = wishlistCacheInvalidator;
        _logger = logger;

        // Ensure indexes exist
        EnsureIndexes();
    }

    private void EnsureIndexes()
    {
        try
        {
            var indexKeys = Builders<OnlineCustomerDocument>.IndexKeys;
            
            // Unique composite index on (TenantId, CustomerId)
            var uniqueIndex = new CreateIndexModel<OnlineCustomerDocument>(
                indexKeys.Ascending(x => x.TenantId).Ascending(x => x.CustomerId),
                new CreateIndexOptions { Unique = true, Name = "IX_TenantId_CustomerId" });

            // Index on (TenantId, Email) for email lookups
            var emailIndex = new CreateIndexModel<OnlineCustomerDocument>(
                indexKeys.Ascending(x => x.TenantId).Ascending(x => x.Email),
                new CreateIndexOptions { Name = "IX_TenantId_Email" });

            // Index on TenantId for tenant-filtered queries
            var tenantIndex = new CreateIndexModel<OnlineCustomerDocument>(
                indexKeys.Ascending(x => x.TenantId),
                new CreateIndexOptions { Name = "IX_TenantId" });

            var eventOrderingIndex = new CreateIndexModel<OnlineCustomerDocument>(
                indexKeys
                    .Ascending(x => x.TenantId)
                    .Ascending(x => x.CustomerId)
                    .Descending(x => x.LastEventOccurredAtUtc)
                    .Descending(x => x.LastEventVersion),
                new CreateIndexOptions { Name = "IX_OnlineCustomers_EventOrdering" });

            var sessionRevocationIndex = new CreateIndexModel<OnlineCustomerDocument>(
                indexKeys
                    .Ascending(x => x.TenantId)
                    .Ascending(x => x.CustomerId)
                    .Descending(x => x.LastSessionRevokedAtUtc),
                new CreateIndexOptions { Name = "IX_OnlineCustomers_SessionRevocationWatermark" });

            _collection.Indexes.CreateMany(new[] { uniqueIndex, emailIndex, tenantIndex, eventOrderingIndex, sessionRevocationIndex });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, 
                "[OnlineCustomer] Failed to ensure MongoDB indexes for online_customers collection");
        }
    }

    public async Task Consume(ConsumeContext<OnlineCustomerRegisteredEvent> context)
    {
        var @event = context.Message;
        var cancellationToken = context.CancellationToken;

        if (!ValidateTenantId(@event.TenantId, @event.CustomerId, nameof(OnlineCustomerRegisteredEvent)))
            return;

        // Structured log: REGISTRATION event
        _logger.LogInformation(
            "[OnlineCustomer] Processing {Action} | TenantId: {TenantId} | CustomerId: {CustomerId} | Email: {Email} | Provider: {Provider} | TraceId: {TraceId}",
            "REGISTRATION",
            @event.TenantId,
            @event.CustomerId,
            MaskEmail(@event.Email),
            @event.Provider,
            context.MessageId);

        try
        {
            // CRITICAL: Normalize email using EmailNormalizer (Trim + ToLowerInvariant)
            var normalizedEmail = EmailNormalizer.Normalize(@event.Email);

            var update = Builders<OnlineCustomerDocument>.Update
                .Set(x => x.Email, normalizedEmail)
                .Set(x => x.Provider, @event.Provider)
                .Set(x => x.DisplayName, @event.DisplayName)
                .Set(x => x.IsActive, @event.IsActive)
                .Set(x => x.EmailVerified, @event.EmailVerified)
                .Set(x => x.CreatedAtUtc, @event.CreatedAtUtc)
                .Set(x => x.UpdatedAtUtc, @event.OccurredAtUtc)
                .SetOnInsert(x => x.Id, ObjectId.GenerateNewId().ToString())
                .SetOnInsert(x => x.TenantId, @event.TenantId!.Value)
                .SetOnInsert(x => x.CustomerId, @event.CustomerId)
                .SetOnInsert(x => x.SchemaVersion, 2);

            var applied = await ApplyCustomerProjectionUpdateAsync(
                @event.TenantId!.Value,
                @event.CustomerId,
                @event,
                update,
                isUpsert: true,
                cancellationToken);

            if (!applied)
            {
                _logger.LogInformation(
                    "[OnlineCustomer] {Action} ignored as stale or duplicate | TenantId: {TenantId} | CustomerId: {CustomerId} | EventId: {EventId} | OccurredAtUtc: {OccurredAtUtc} | Version: {Version}",
                    "REGISTRATION",
                    @event.TenantId,
                    @event.CustomerId,
                    @event.EventId,
                    @event.OccurredAtUtc,
                    @event.EventVersion);
                return;
            }

            _logger.LogInformation(
                "[OnlineCustomer] {Action} projected successfully | TenantId: {TenantId} | CustomerId: {CustomerId}",
                "REGISTRATION",
                @event.TenantId,
                @event.CustomerId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[OnlineCustomer] Failed to project {Action} | TenantId: {TenantId} | CustomerId: {CustomerId}",
                "REGISTRATION",
                @event.TenantId,
                @event.CustomerId);
            throw; // Retry via MassTransit
        }
    }

    public async Task Consume(ConsumeContext<OnlineCustomerActivatedEvent> context)
    {
        var @event = context.Message;
        var cancellationToken = context.CancellationToken;

        if (!ValidateTenantId(@event.TenantId, @event.CustomerId, nameof(OnlineCustomerActivatedEvent)))
            return;

        // Structured log: ACTIVATION event
        _logger.LogInformation(
            "[OnlineCustomer] Processing {Action} | TenantId: {TenantId} | CustomerId: {CustomerId} | Email: {Email} | TraceId: {TraceId}",
            "ACTIVATION",
            @event.TenantId,
            @event.CustomerId,
            MaskEmail(@event.Email),
            context.MessageId);

        try
        {
            var update = Builders<OnlineCustomerDocument>.Update
                .Set(x => x.IsActive, true)
                .Set(x => x.UpdatedAtUtc, @event.ActivatedAtUtc)
                .SetOnInsert(x => x.Id, ObjectId.GenerateNewId().ToString())
                .SetOnInsert(x => x.TenantId, @event.TenantId!.Value)
                .SetOnInsert(x => x.CustomerId, @event.CustomerId)
                .SetOnInsert(x => x.SchemaVersion, 2);

            var applied = await ApplyCustomerProjectionUpdateAsync(
                @event.TenantId!.Value,
                @event.CustomerId,
                @event,
                update,
                isUpsert: false,
                cancellationToken);

            if (!applied)
            {
                _logger.LogInformation(
                    "[OnlineCustomer] {Action} ignored as stale or duplicate (event already processed) | TenantId: {TenantId} | CustomerId: {CustomerId} | EventId: {EventId}",
                    "ACTIVATION",
                    @event.TenantId,
                    @event.CustomerId,
                    @event.EventId);
                return;
            }

            _logger.LogInformation(
                "[OnlineCustomer] {Action} projected successfully | TenantId: {TenantId} | CustomerId: {CustomerId}",
                "ACTIVATION",
                @event.TenantId,
                @event.CustomerId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[OnlineCustomer] Failed to project {Action} | TenantId: {TenantId} | CustomerId: {CustomerId}",
                "ACTIVATION",
                @event.TenantId,
                @event.CustomerId);
            throw;
        }
    }

    public async Task Consume(ConsumeContext<OnlineCustomerDeactivatedEvent> context)
    {
        var @event = context.Message;
        var cancellationToken = context.CancellationToken;

        if (!ValidateTenantId(@event.TenantId, @event.CustomerId, nameof(OnlineCustomerDeactivatedEvent)))
            return;

        // Structured log: DEACTIVATION event (critical security event)
        _logger.LogWarning(
            "[OnlineCustomer] Processing {Action} | TenantId: {TenantId} | CustomerId: {CustomerId} | Email: {Email} | Reason: {Reason} | TraceId: {TraceId}",
            "DEACTIVATION",
            @event.TenantId,
            @event.CustomerId,
            MaskEmail(@event.Email),
            @event.Reason ?? "Not specified",
            context.MessageId);

        try
        {
            var update = Builders<OnlineCustomerDocument>.Update
                .Set(x => x.IsActive, false)
                .Set(x => x.UpdatedAtUtc, @event.DeactivatedAtUtc)
                .SetOnInsert(x => x.Id, ObjectId.GenerateNewId().ToString())
                .SetOnInsert(x => x.TenantId, @event.TenantId!.Value)
                .SetOnInsert(x => x.CustomerId, @event.CustomerId)
                .SetOnInsert(x => x.SchemaVersion, 2);

            var applied = await ApplyCustomerProjectionUpdateAsync(
                @event.TenantId!.Value,
                @event.CustomerId,
                @event,
                update,
                isUpsert: false,
                cancellationToken);

            if (!applied)
            {
                _logger.LogInformation(
                    "[OnlineCustomer] {Action} ignored as stale or duplicate (event already processed) | TenantId: {TenantId} | CustomerId: {CustomerId} | EventId: {EventId}",
                    "DEACTIVATION",
                    @event.TenantId,
                    @event.CustomerId,
                    @event.EventId);
                return;
            }
            
            // Customer profile reads are uncached. The wishlist cache remains live and
            // is invalidated exclusively through its semantic owner.
            await _wishlistCacheInvalidator.InvalidateAsync(
                @event.TenantId!.Value,
                @event.CustomerId,
                context.CancellationToken);

            _logger.LogWarning(
                "[OnlineCustomer] {Action} projected successfully - customer can no longer authenticate | TenantId: {TenantId} | CustomerId: {CustomerId}",
                "DEACTIVATION",
                @event.TenantId,
                @event.CustomerId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[OnlineCustomer] Failed to project {Action} | TenantId: {TenantId} | CustomerId: {CustomerId}",
                "DEACTIVATION",
                @event.TenantId,
                @event.CustomerId);
            throw;
        }
    }

    public async Task Consume(ConsumeContext<OnlineCustomerEmailVerifiedEvent> context)
    {
        var @event = context.Message;
        var cancellationToken = context.CancellationToken;

        if (!ValidateTenantId(@event.TenantId, @event.CustomerId, nameof(OnlineCustomerEmailVerifiedEvent)))
            return;

        // Structured log: EMAIL_VERIFIED event
        _logger.LogInformation(
            "[OnlineCustomer] Processing {Action} | TenantId: {TenantId} | CustomerId: {CustomerId} | Email: {Email} | TraceId: {TraceId}",
            "EMAIL_VERIFIED",
            @event.TenantId,
            @event.CustomerId,
            MaskEmail(@event.Email),
            context.MessageId);

        try
        {
            var update = Builders<OnlineCustomerDocument>.Update
                .Set(x => x.EmailVerified, true)
                .Set(x => x.UpdatedAtUtc, @event.VerifiedAtUtc)
                .SetOnInsert(x => x.Id, ObjectId.GenerateNewId().ToString())
                .SetOnInsert(x => x.TenantId, @event.TenantId!.Value)
                .SetOnInsert(x => x.CustomerId, @event.CustomerId)
                .SetOnInsert(x => x.SchemaVersion, 2);

            var applied = await ApplyCustomerProjectionUpdateAsync(
                @event.TenantId!.Value,
                @event.CustomerId,
                @event,
                update,
                isUpsert: false,
                cancellationToken);

            if (!applied)
            {
                _logger.LogInformation(
                    "[OnlineCustomer] {Action} ignored as stale or duplicate (event already processed) | TenantId: {TenantId} | CustomerId: {CustomerId} | EventId: {EventId}",
                    "EMAIL_VERIFIED",
                    @event.TenantId,
                    @event.CustomerId,
                    @event.EventId);
                return;
            }

            _logger.LogInformation(
                "[OnlineCustomer] {Action} projected successfully | TenantId: {TenantId} | CustomerId: {CustomerId}",
                "EMAIL_VERIFIED",
                @event.TenantId,
                @event.CustomerId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[OnlineCustomer] Failed to project {Action} | TenantId: {TenantId} | CustomerId: {CustomerId}",
                "EMAIL_VERIFIED",
                @event.TenantId,
                @event.CustomerId);
            throw;
        }
    }

    public async Task Consume(ConsumeContext<OnlineCustomerLoggedInEvent> context)
    {
        var @event = context.Message;
        var cancellationToken = context.CancellationToken;

        if (!ValidateTenantId(@event.TenantId, @event.CustomerId, nameof(OnlineCustomerLoggedInEvent)))
            return;

        // Structured log: LOGIN event
        _logger.LogInformation(
            "[OnlineCustomer] Processing {Action} | TenantId: {TenantId} | CustomerId: {CustomerId} | Email: {Email} | Provider: {Provider} | TraceId: {TraceId}",
            "LOGIN",
            @event.TenantId,
            @event.CustomerId,
            MaskEmail(@event.Email),
            @event.Provider,
            context.MessageId);

        try
        {
            // CRITICAL: LastLoginAtUtc is MIRRORED from the event, NOT computed here
            // PostgreSQL is the ONLY source of truth for this timestamp
            // The event.LoginAtUtc was set in the service layer AFTER updating PostgreSQL
            var update = Builders<OnlineCustomerDocument>.Update
                .Set(x => x.LastLoginAtUtc, @event.LoginAtUtc)
                .Set(x => x.UpdatedAtUtc, @event.LoginAtUtc)
                .SetOnInsert(x => x.Id, ObjectId.GenerateNewId().ToString())
                .SetOnInsert(x => x.TenantId, @event.TenantId!.Value)
                .SetOnInsert(x => x.CustomerId, @event.CustomerId)
                .SetOnInsert(x => x.SchemaVersion, 2);

            var applied = await ApplyCustomerProjectionUpdateAsync(
                @event.TenantId!.Value,
                @event.CustomerId,
                @event,
                update,
                isUpsert: false,
                cancellationToken);

            if (!applied)
            {
                _logger.LogInformation(
                    "[OnlineCustomer] {Action} ignored as stale or duplicate (event already processed) | TenantId: {TenantId} | CustomerId: {CustomerId} | EventId: {EventId}",
                    "LOGIN",
                    @event.TenantId,
                    @event.CustomerId,
                    @event.EventId);
                return;
            }

            _logger.LogInformation(
                "[OnlineCustomer] {Action} projected successfully | TenantId: {TenantId} | CustomerId: {CustomerId} | LoginAtUtc: {LoginAtUtc}",
                "LOGIN",
                @event.TenantId,
                @event.CustomerId,
                @event.LoginAtUtc);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[OnlineCustomer] Failed to project {Action} | TenantId: {TenantId} | CustomerId: {CustomerId}",
                "LOGIN",
                @event.TenantId,
                @event.CustomerId);
            throw;
        }
    }

    /// <summary>
    /// Handles profile updates (display name, phone, etc.)
    /// Phase F2: Account Area
    /// </summary>
    public async Task Consume(ConsumeContext<OnlineCustomerUpdatedEvent> context)
    {
        var @event = context.Message;
        var cancellationToken = context.CancellationToken;

        if (!ValidateTenantId(@event.TenantId, @event.CustomerId, nameof(OnlineCustomerUpdatedEvent)))
            return;

        // Structured log: PROFILE_UPDATE event
        _logger.LogInformation(
            "[OnlineCustomer] Processing {Action} | TenantId: {TenantId} | CustomerId: {CustomerId} | Email: {Email} | TraceId: {TraceId}",
            "PROFILE_UPDATE",
            @event.TenantId,
            @event.CustomerId,
            MaskEmail(@event.Email),
            context.MessageId);

        try
        {
            // CRITICAL: Mirror all data from event - PostgreSQL is source of truth
            var update = Builders<OnlineCustomerDocument>.Update
                .Set(x => x.Email, EmailNormalizer.Normalize(@event.Email))
                .Set(x => x.DisplayName, @event.DisplayName)
                .Set(x => x.Phone, @event.Phone)
                .Set(x => x.Provider, @event.Provider)
                .Set(x => x.EmailVerified, @event.EmailVerified)
                .Set(x => x.IsActive, @event.IsActive)
                .Set(x => x.UpdatedAtUtc, @event.UpdatedAt)
                .SetOnInsert(x => x.Id, ObjectId.GenerateNewId().ToString())
                .SetOnInsert(x => x.TenantId, @event.TenantId)
                .SetOnInsert(x => x.CustomerId, @event.CustomerId)
                .SetOnInsert(x => x.SchemaVersion, 2);

            var applied = await ApplyCustomerProjectionUpdateAsync(
                @event.TenantId,
                @event.CustomerId,
                @event,
                update,
                isUpsert: false,
                cancellationToken);

            if (!applied)
            {
                _logger.LogInformation(
                    "[OnlineCustomer] {Action} ignored as stale or duplicate (event already processed) | TenantId: {TenantId} | CustomerId: {CustomerId} | EventId: {EventId}",
                    "PROFILE_UPDATE",
                    @event.TenantId,
                    @event.CustomerId,
                    @event.EventId);
                return;
            }

            _logger.LogInformation(
                "[OnlineCustomer] {Action} projected successfully | TenantId: {TenantId} | CustomerId: {CustomerId}",
                "PROFILE_UPDATE",
                @event.TenantId,
                @event.CustomerId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[OnlineCustomer] Failed to project {Action} | TenantId: {TenantId} | CustomerId: {CustomerId}",
                "PROFILE_UPDATE",
                @event.TenantId,
                @event.CustomerId);
            throw;
        }
    }

    /// <summary>
    /// Handles password change events.
    /// Note: We don't update anything password-related in MongoDB (no password hash stored).
    /// This event is retained for security audit trail processing.
    /// 
    /// Phase F2: Account Area
    /// </summary>
    public async Task Consume(ConsumeContext<OnlineCustomerPasswordChangedEvent> context)
    {
        var @event = context.Message;

        if (!ValidateTenantId(@event.TenantId, @event.CustomerId, nameof(OnlineCustomerPasswordChangedEvent)))
            return;

        // Structured log: PASSWORD_CHANGED event (security-sensitive)
        _logger.LogWarning(
            "[OnlineCustomer] Processing {Action} | TenantId: {TenantId} | CustomerId: {CustomerId} | ChangedAt: {ChangedAt} | TraceId: {TraceId}",
            "PASSWORD_CHANGED",
            @event.TenantId,
            @event.CustomerId,
            @event.ChangedAt,
            context.MessageId);

        try
        {
            var update = Builders<OnlineCustomerDocument>.Update
                .Set(x => x.UpdatedAtUtc, @event.ChangedAt);

            await ApplyCustomerProjectionUpdateAsync(
                @event.TenantId,
                @event.CustomerId,
                @event,
                update,
                isUpsert: false,
                context.CancellationToken);

            _logger.LogWarning(
                "[OnlineCustomer] {Action} processed | TenantId: {TenantId} | CustomerId: {CustomerId}",
                "PASSWORD_CHANGED",
                @event.TenantId,
                @event.CustomerId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[OnlineCustomer] Failed to process {Action} | TenantId: {TenantId} | CustomerId: {CustomerId}",
                "PASSWORD_CHANGED",
                @event.TenantId,
                @event.CustomerId);
            throw;
        }
    }

    /// <summary>
    /// Validates TenantId is present and not Guid.Empty.
    /// Rejects events with missing tenant context.
    /// </summary>
    private bool ValidateTenantId(Guid? tenantId, Guid customerId, string eventName)
    {
        if (!tenantId.HasValue || tenantId.Value == Guid.Empty)
        {
            _logger.LogError(
                "[OnlineCustomer] SECURITY: {EventName} REJECTED - Missing or empty TenantId | CustomerId: {CustomerId}",
                eventName, customerId);
            return false;
        }
        return true;
    }

    private async Task<bool> ApplyCustomerProjectionUpdateAsync(
        Guid tenantId,
        Guid customerId,
        IntegrationEvent @event,
        UpdateDefinition<OnlineCustomerDocument> update,
        bool isUpsert,
        CancellationToken cancellationToken)
    {
        var filter = BuildFreshCustomerEventFilter(
            tenantId,
            customerId,
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

            // If no match and not upserting, check if document exists at all
            if (!isUpsert && result.MatchedCount == 0)
            {
                var documentExists = await _collection.Find(
                    Builders<OnlineCustomerDocument>.Filter.And(
                        Builders<OnlineCustomerDocument>.Filter.Eq(x => x.TenantId, tenantId),
                        Builders<OnlineCustomerDocument>.Filter.Eq(x => x.CustomerId, customerId)))
                    .AnyAsync(cancellationToken);

                if (!documentExists)
                {
                    var eventType = @event?.GetType().Name ?? "null";

                    // Document doesn't exist - this is an error condition that should be retried
                    throw new InvalidOperationException(
                        $"Cannot apply non-upsert update for CustomerId {customerId} in TenantId {tenantId}: " +
                        $"projection document does not exist. Event {eventType} (EventId: {@event.EventId}) " +
                        $"arrived before OnlineCustomerRegisteredEvent. This event will be retried.");
                }

                // The document may have been inserted concurrently after the first update missed it.
                // Retry the same non-upsert update once so we do not drop a valid event due to timing.
                result = await _collection.UpdateOneAsync(
                    filter,
                    updateWithMetadata,
                    new UpdateOptions { IsUpsert = isUpsert },
                    cancellationToken);
            }

            return result.MatchedCount > 0 || result.ModifiedCount > 0 || result.UpsertedId != null;
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            return false;
        }
    }

    private static FilterDefinition<OnlineCustomerDocument> BuildFreshCustomerEventFilter(
        Guid tenantId,
        Guid customerId,
        Guid eventId,
        DateTime occurredAtUtc,
        int eventVersion)
    {
        var filter = Builders<OnlineCustomerDocument>.Filter;

        return filter.And(
            filter.Eq(x => x.TenantId, tenantId),
            filter.Eq(x => x.CustomerId, customerId),
            filter.Ne(x => x.LastEventId, eventId),
            filter.Or(
                filter.Exists(x => x.LastEventOccurredAtUtc, false),
                filter.Eq(x => x.LastEventOccurredAtUtc, null),
                filter.Lt(x => x.LastEventOccurredAtUtc, occurredAtUtc),
                filter.And(
                    filter.Eq(x => x.LastEventOccurredAtUtc, occurredAtUtc),
                    filter.Lt(x => x.LastEventVersion, eventVersion))));
    }

    /// <summary>
    /// Validates TenantId is not Guid.Empty (for non-nullable Guid overload).
    /// </summary>
    private bool ValidateTenantId(Guid tenantId, Guid customerId, string eventName)
    {
        if (tenantId == Guid.Empty)
        {
            _logger.LogError(
                "[OnlineCustomer] SECURITY: {EventName} REJECTED - Empty TenantId | CustomerId: {CustomerId}",
                eventName, customerId);
            return false;
        }
        return true;
    }

    /// <summary>
    /// Masks email for logging to prevent PII leakage.
    /// Example: "user@example.com" -> "u***@example.com"
    /// </summary>
    private static string MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return "[empty]";

        var atIndex = email.IndexOf('@');
        if (atIndex <= 1)
            return "***@" + (atIndex >= 0 ? email[(atIndex + 1)..] : "***");

        return email[0] + "***" + email[(atIndex - 1)..];
    }
}

/// <summary>
/// Consumer definition for OnlineCustomerProjectionConsumer.
/// Configures endpoint name and retry policy.
/// </summary>
public class OnlineCustomerProjectionConsumerDefinition : ConsumerDefinition<OnlineCustomerProjectionConsumer>
{
    public OnlineCustomerProjectionConsumerDefinition()
    {
        EndpointName = "online-customer-projection";
    }

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<OnlineCustomerProjectionConsumer> consumerConfigurator)
    {
        endpointConfigurator.UseMessageRetry(r => r.Intervals(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(15)));
    }
}
