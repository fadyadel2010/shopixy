using System.Text.Json;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using PointOfSale.Core.Events;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.MongoDB.Services;

namespace PointOfSale.Infrastructure.Consumers;

/// <summary>
/// Projects CheckoutSession events from PostgreSQL to MongoDB.
/// 
/// Phase: Abandoned Checkout / Lost Orders
/// 
/// Consumed Events:
/// - CheckoutSessionStartedEvent → Insert new AbandonedCheckoutDocument
/// - CheckoutSessionUpdatedEvent → Update fields from autosave
/// - CheckoutSessionAbandonedEvent → Set status to Abandoned
/// - CheckoutSessionConvertedEvent → Set status to ConvertedToOrder
/// 
/// ARCHITECTURAL RULES:
/// - Idempotent projections (upsert by TenantId + CheckoutSessionId)
/// - Tenant isolation enforced — Guid.Empty rejected
/// - No Redis cache invalidation needed (admin dashboard only, no hot path)
/// </summary>
public class CheckoutSessionProjectionConsumer :
    IConsumer<CheckoutSessionStartedEvent>,
    IConsumer<CheckoutSessionUpdatedEvent>,
    IConsumer<CheckoutSessionProofEvent>,
    IConsumer<CheckoutSessionProofSyncRequestedEvent>,
    IConsumer<CheckoutSessionAbandonedEvent>,
    IConsumer<CheckoutSessionConvertedEvent>
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IMongoCollection<AbandonedCheckoutDocument> _collection;
    private readonly ILogger<CheckoutSessionProjectionConsumer> _logger;

    public CheckoutSessionProjectionConsumer(
        ApplicationDbContext dbContext,
        MongoDbContext mongoContext,
        ILogger<CheckoutSessionProjectionConsumer> logger)
    {
        _dbContext = dbContext;
        _collection = mongoContext.AbandonedCheckouts;
        _logger = logger;
    }

    // ============================================================================
    // CheckoutSessionStartedEvent → Insert
    // ============================================================================

    public async Task Consume(ConsumeContext<CheckoutSessionStartedEvent> context)
    {
        var @event = context.Message;
        var ct = context.CancellationToken;

        if (!ValidateTenantId(@event.TenantId, @event.CheckoutSessionId))
            return;

        _logger.LogInformation(
            "[AbandonedCheckout] SESSION_STARTED | TenantId: {TenantId} | SessionId: {SessionId} | CartId: {CartId}",
            @event.TenantId, @event.CheckoutSessionId, @event.CartId);

        try
        {
            // Step 1: Build base key filter (TenantId + CheckoutSessionId)
            var baseFilter = Builders<AbandonedCheckoutDocument>.Filter.And(
                Builders<AbandonedCheckoutDocument>.Filter.Eq(x => x.TenantId, @event.TenantId!.Value),
                Builders<AbandonedCheckoutDocument>.Filter.Eq(x => x.CheckoutSessionId, @event.CheckoutSessionId));

            // Step 2: Build filter with lifecycle guard for conditional update
            var filterWithLifecycleGuard = baseFilter &
                BuildFreshLifecycleFilter(@event.EventId, @event.OccurredAtUtc, @event.EventVersion);

            var items = DeserializeItems(@event.ItemsSnapshotJson);

            var update = Builders<AbandonedCheckoutDocument>.Update
                .SetOnInsert(x => x.TenantId, @event.TenantId!.Value)
                .SetOnInsert(x => x.CheckoutSessionId, @event.CheckoutSessionId)
                .Set(x => x.CartId, @event.CartId)
                .Set(x => x.SessionId, @event.SessionId)
                .Set(x => x.OnlineCustomerId, @event.OnlineCustomerId)
                .Set(x => x.CreatedAtUtc, @event.CreatedAtUtc)
                .Set(x => x.CustomerName, @event.CustomerName)
                .Set(x => x.Phone, @event.Phone)
                .Set(x => x.City, @event.City)
                .Set(x => x.Items, items)
                .Set(x => x.CartTotal, @event.CartTotal)
                .Set(x => x.Currency, @event.Currency)
                .Set(x => x.RecoveryToken, @event.RecoveryToken)
                .Set(x => x.DeviceType, @event.DeviceType)
                .Set(x => x.Source, @event.Source)
                .SetOnInsert(x => x.Status, "Active")
                .Set(x => x.LastActivityAtUtc, @event.CreatedAtUtc)
                .Set(x => x.LastEventId, @event.EventId)
                .Set(x => x.LastEventOccurredAtUtc, @event.OccurredAtUtc)
                .Set(x => x.LastEventVersion, @event.EventVersion);

            // Step 3: Attempt conditional update with lifecycle guard (no upsert)
            var result = await _collection.UpdateOneAsync(
                filterWithLifecycleGuard,
                update,
                new UpdateOptions { IsUpsert = false },
                ct);

            // Step 4: If no document matched, perform atomic guarded upsert
            // PRODUCTION HARDENING: Replace separate existence check + upsert with single atomic operation
            // to prevent race conditions where concurrent inserts could overwrite each other
            if (result.MatchedCount == 0)
            {
                try
                {
                    // Atomic guarded upsert: only inserts if document doesn't exist OR lifecycle guard passes
                    // The lifecycle guard in the filter prevents overwriting newer state
                    await _collection.UpdateOneAsync(
                        filterWithLifecycleGuard,
                        update,
                        new UpdateOptions { IsUpsert = true },
                        ct);
                }
                catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
                {
                    // PRODUCTION HARDENING: Benign race condition - another process inserted first
                    // This is expected in concurrent scenarios and should be treated as a no-op
                    _logger.LogDebug(
                        "[AbandonedCheckout] SESSION_STARTED concurrent insert race detected (benign) | SessionId: {SessionId} | EventId: {EventId}",
                        @event.CheckoutSessionId, @event.EventId);
                }
                catch (MongoWriteException ex) when (ex.WriteError?.Code == 11000)
                {
                    // PRODUCTION HARDENING: Alternative duplicate key error code
                    _logger.LogDebug(
                        "[AbandonedCheckout] SESSION_STARTED concurrent insert race detected (benign, code 11000) | SessionId: {SessionId} | EventId: {EventId}",
                        @event.CheckoutSessionId, @event.EventId);
                }
            }

            _logger.LogInformation(
                "[AbandonedCheckout] SESSION_STARTED projected | SessionId: {SessionId}",
                @event.CheckoutSessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[AbandonedCheckout] Failed to project SESSION_STARTED | SessionId: {SessionId}",
                @event.CheckoutSessionId);
            throw;
        }
    }

    // ============================================================================
    // CheckoutSessionUpdatedEvent → Update fields
    // ============================================================================

    public async Task Consume(ConsumeContext<CheckoutSessionUpdatedEvent> context)
    {
        var @event = context.Message;
        var ct = context.CancellationToken;

        if (!ValidateTenantId(@event.TenantId, @event.CheckoutSessionId))
            return;

        try
        {
            var filter = Builders<AbandonedCheckoutDocument>.Filter.And(
                Builders<AbandonedCheckoutDocument>.Filter.Eq(x => x.TenantId, @event.TenantId!.Value),
                Builders<AbandonedCheckoutDocument>.Filter.Eq(x => x.CheckoutSessionId, @event.CheckoutSessionId),
                Builders<AbandonedCheckoutDocument>.Filter.Ne(x => x.Status, "ConvertedToOrder"),
                BuildFreshLifecycleFilter(@event.EventId, @event.OccurredAtUtc, @event.EventVersion));

            var updateBuilder = Builders<AbandonedCheckoutDocument>.Update
                .Set(x => x.LastActivityAtUtc, @event.LastActivityAtUtc)
                .Set(x => x.LastEventId, @event.EventId)
                .Set(x => x.LastEventOccurredAtUtc, @event.OccurredAtUtc)
                .Set(x => x.LastEventVersion, @event.EventVersion);

            // Only update non-null fields (progressive autosave)
            if (@event.CustomerName != null)
                updateBuilder = updateBuilder.Set(x => x.CustomerName, @event.CustomerName);
            if (@event.Phone != null)
                updateBuilder = updateBuilder.Set(x => x.Phone, @event.Phone);
            if (@event.Email != null)
                updateBuilder = updateBuilder.Set(x => x.Email, @event.Email);
            if (@event.City != null)
                updateBuilder = updateBuilder.Set(x => x.City, @event.City);
            if (@event.Area != null)
                updateBuilder = updateBuilder.Set(x => x.Area, @event.Area);
            if (@event.Address != null)
                updateBuilder = updateBuilder.Set(x => x.Address, @event.Address);
            if (@event.Country != null)
                updateBuilder = updateBuilder.Set(x => x.Country, @event.Country);
            if (@event.Notes != null)
                updateBuilder = updateBuilder.Set(x => x.Notes, @event.Notes);
            if (@event.ItemsSnapshotJson != null)
                updateBuilder = updateBuilder.Set(x => x.Items, DeserializeItems(@event.ItemsSnapshotJson));
            if (@event.CartTotal.HasValue)
                updateBuilder = updateBuilder.Set(x => x.CartTotal, @event.CartTotal);
            if (@event.Currency != null)
                updateBuilder = updateBuilder.Set(x => x.Currency, @event.Currency);
            if (@event.ShippingMethodId.HasValue)
                updateBuilder = updateBuilder.Set(x => x.ShippingMethodId, @event.ShippingMethodId);
            if (@event.PaymentMethodCode != null)
                updateBuilder = updateBuilder.Set(x => x.PaymentMethodCode, @event.PaymentMethodCode);

            var result = await _collection.UpdateOneAsync(filter, updateBuilder, cancellationToken: ct);

            // Project FirstActivityAtUtc only once (first update)
            // Only proceed if the guarded update above succeeded (not stale)
            if (@event.FirstActivityAtUtc.HasValue && result.MatchedCount > 0)
            {
                var firstActivityFilter = Builders<AbandonedCheckoutDocument>.Filter.And(
                    Builders<AbandonedCheckoutDocument>.Filter.Eq(x => x.TenantId, @event.TenantId!.Value),
                    Builders<AbandonedCheckoutDocument>.Filter.Eq(x => x.CheckoutSessionId, @event.CheckoutSessionId),
                    Builders<AbandonedCheckoutDocument>.Filter.Ne(x => x.Status, "ConvertedToOrder"),
                    Builders<AbandonedCheckoutDocument>.Filter.Eq(x => x.FirstActivityAtUtc, null));
                var firstActivityUpdate = Builders<AbandonedCheckoutDocument>.Update
                    .Set(x => x.FirstActivityAtUtc, @event.FirstActivityAtUtc);
                await _collection.UpdateOneAsync(firstActivityFilter, firstActivityUpdate, cancellationToken: ct);
            }

            if (result.MatchedCount == 0)
            {
                _logger.LogDebug(
                    "[AbandonedCheckout] Session not found for update (may not be projected yet) | SessionId: {SessionId}",
                    @event.CheckoutSessionId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[AbandonedCheckout] Failed to project SESSION_UPDATED | SessionId: {SessionId}",
                @event.CheckoutSessionId);
            throw;
        }
    }

    // ============================================================================
    // CheckoutSessionProofEvent → Update payment proof fields (internal event)
    // ============================================================================

    public async Task Consume(ConsumeContext<CheckoutSessionProofEvent> context)
    {
        var @event = context.Message;
        var ct = context.CancellationToken;

        if (!ValidateTenantId(@event.TenantId, @event.CheckoutSessionId))
            return;

        try
        {
            await UpsertProofFieldsAsync(
                @event.TenantId!.Value,
                @event.CheckoutSessionId,
                @event.WalletType,
                @event.WalletPhoneNumber,
                @event.ProofImageUrl,
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[AbandonedCheckout] Failed to project SESSION_PROOF | SessionId: {SessionId}",
                @event.CheckoutSessionId);
            throw;
        }
    }

    // ============================================================================
    // CheckoutSessionAbandonedEvent → Mark Abandoned
    // ============================================================================

    public async Task Consume(ConsumeContext<CheckoutSessionProofSyncRequestedEvent> context)
    {
        var @event = context.Message;
        var ct = context.CancellationToken;

        if (!ValidateTenantId(@event.TenantId, @event.CheckoutSessionId))
            return;

        var tenantId = @event.TenantId!.Value;

        try
        {
            var session = await _dbContext.CheckoutSessions
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(existing =>
                    existing.TenantId == tenantId &&
                    existing.Id == @event.CheckoutSessionId)
                .Select(existing => new
                {
                    existing.WalletType,
                    existing.WalletPhoneNumber,
                    existing.ProofImageUrl
                })
                .SingleOrDefaultAsync(ct);

            if (session == null)
            {
                _logger.LogWarning(
                    "[AbandonedCheckout] SESSION_PROOF_SYNC skipped because SQL session was not found | SessionId: {SessionId}",
                    @event.CheckoutSessionId);
                return;
            }

            await UpsertProofFieldsAsync(
                tenantId,
                @event.CheckoutSessionId,
                session.WalletType,
                session.WalletPhoneNumber,
                session.ProofImageUrl,
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[AbandonedCheckout] Failed to project SESSION_PROOF_SYNC | SessionId: {SessionId}",
                @event.CheckoutSessionId);
            throw;
        }
    }

    public async Task Consume(ConsumeContext<CheckoutSessionAbandonedEvent> context)
    {
        var @event = context.Message;
        var ct = context.CancellationToken;

        if (!ValidateTenantId(@event.TenantId, @event.CheckoutSessionId))
            return;

        _logger.LogInformation(
            "[AbandonedCheckout] SESSION_ABANDONED | SessionId: {SessionId} | CartTotal: {CartTotal}",
            @event.CheckoutSessionId, @event.CartTotal);

        try
        {
            var filter = Builders<AbandonedCheckoutDocument>.Filter.And(
                Builders<AbandonedCheckoutDocument>.Filter.Eq(x => x.TenantId, @event.TenantId!.Value),
                Builders<AbandonedCheckoutDocument>.Filter.Eq(x => x.CheckoutSessionId, @event.CheckoutSessionId),
                Builders<AbandonedCheckoutDocument>.Filter.Ne(x => x.Status, "ConvertedToOrder"),
                BuildFreshLifecycleFilter(@event.EventId, @event.OccurredAtUtc, @event.EventVersion));

            var update = Builders<AbandonedCheckoutDocument>.Update
                .Set(x => x.Status, "Abandoned")
                .Set(x => x.AbandonedAtUtc, @event.AbandonedAtUtc)
                .Set(x => x.RecoveryToken, @event.RecoveryToken)
                .Set(x => x.LastEventId, @event.EventId)
                .Set(x => x.LastEventOccurredAtUtc, @event.OccurredAtUtc)
                .Set(x => x.LastEventVersion, @event.EventVersion);

            await _collection.UpdateOneAsync(filter, update, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[AbandonedCheckout] Failed to project SESSION_ABANDONED | SessionId: {SessionId}",
                @event.CheckoutSessionId);
            throw;
        }
    }

    // ============================================================================
    // CheckoutSessionConvertedEvent → Mark ConvertedToOrder
    // ============================================================================

    public async Task Consume(ConsumeContext<CheckoutSessionConvertedEvent> context)
    {
        var @event = context.Message;
        var ct = context.CancellationToken;

        if (!ValidateTenantId(@event.TenantId, @event.CheckoutSessionId))
            return;

        _logger.LogInformation(
            "[AbandonedCheckout] SESSION_CONVERTED | SessionId: {SessionId} | OrderNumber: {OrderNumber}",
            @event.CheckoutSessionId, @event.OrderNumber);

        try
        {
            var filter = Builders<AbandonedCheckoutDocument>.Filter.And(
                Builders<AbandonedCheckoutDocument>.Filter.Eq(x => x.TenantId, @event.TenantId!.Value),
                Builders<AbandonedCheckoutDocument>.Filter.Eq(x => x.CheckoutSessionId, @event.CheckoutSessionId),
                BuildFreshLifecycleFilter(@event.EventId, @event.OccurredAtUtc, @event.EventVersion));

            var update = Builders<AbandonedCheckoutDocument>.Update
                .Set(x => x.Status, "ConvertedToOrder")
                .Set(x => x.ConvertedAtUtc, @event.ConvertedAtUtc)
                .Set(x => x.ConvertedOrderId, @event.OrderId)
                .Set(x => x.ConvertedOrderNumber, @event.OrderNumber)
                .Set(x => x.LastEventId, @event.EventId)
                .Set(x => x.LastEventOccurredAtUtc, @event.OccurredAtUtc)
                .Set(x => x.LastEventVersion, @event.EventVersion);

            await _collection.UpdateOneAsync(filter, update, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[AbandonedCheckout] Failed to project SESSION_CONVERTED | SessionId: {SessionId}",
                @event.CheckoutSessionId);
            throw;
        }
    }

    // ============================================================================
    // Helpers
    // ============================================================================

    private async Task UpsertProofFieldsAsync(
        Guid tenantId,
        Guid checkoutSessionId,
        string? walletType,
        string? walletPhoneNumber,
        string? proofImageUrl,
        CancellationToken ct)
    {
        var filter = Builders<AbandonedCheckoutDocument>.Filter.And(
            Builders<AbandonedCheckoutDocument>.Filter.Eq(x => x.TenantId, tenantId),
            Builders<AbandonedCheckoutDocument>.Filter.Eq(x => x.CheckoutSessionId, checkoutSessionId));

        var update = Builders<AbandonedCheckoutDocument>.Update
            .Set(x => x.WalletType, walletType)
            .Set(x => x.WalletPhoneNumber, walletPhoneNumber)
            .Set(x => x.ProofImageUrl, proofImageUrl);

        var result = await _collection.UpdateOneAsync(filter, update, new UpdateOptions { IsUpsert = false }, ct);
        
        if (result.MatchedCount == 0)
        {
            _logger.LogWarning(
                "[AbandonedCheckout] Cannot update proof fields - document not found. CheckoutSessionStartedEvent may not have been projected yet | TenantId: {TenantId} | SessionId: {SessionId}",
                tenantId,
                checkoutSessionId);
        }
    }

    private static FilterDefinition<AbandonedCheckoutDocument> BuildFreshLifecycleFilter(
        Guid eventId,
        DateTime occurredAtUtc,
        int eventVersion)
    {
        return Builders<AbandonedCheckoutDocument>.Filter.And(
            Builders<AbandonedCheckoutDocument>.Filter.Ne(x => x.LastEventId, eventId),
            Builders<AbandonedCheckoutDocument>.Filter.Or(
                Builders<AbandonedCheckoutDocument>.Filter.Exists(x => x.LastEventOccurredAtUtc, false),
                Builders<AbandonedCheckoutDocument>.Filter.Eq(x => x.LastEventOccurredAtUtc, null),
                Builders<AbandonedCheckoutDocument>.Filter.Lt(x => x.LastEventOccurredAtUtc, occurredAtUtc),
                Builders<AbandonedCheckoutDocument>.Filter.And(
                    Builders<AbandonedCheckoutDocument>.Filter.Eq(x => x.LastEventOccurredAtUtc, occurredAtUtc),
                    Builders<AbandonedCheckoutDocument>.Filter.Lt(x => x.LastEventVersion, eventVersion))));
    }

    private bool ValidateTenantId(Guid? tenantId, Guid sessionId)
    {
        if (!tenantId.HasValue || tenantId.Value == Guid.Empty)
        {
            _logger.LogWarning(
                "[AbandonedCheckout] Event missing TenantId | SessionId: {SessionId}", sessionId);
            return false;
        }
        return true;
    }

    private static List<AbandonedCheckoutItemSnapshot> DeserializeItems(string? json)
    {
        if (string.IsNullOrEmpty(json) || json == "[]")
            return new List<AbandonedCheckoutItemSnapshot>();

        try
        {
            return JsonSerializer.Deserialize<List<AbandonedCheckoutItemSnapshot>>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? new List<AbandonedCheckoutItemSnapshot>();
        }
        catch
        {
            return new List<AbandonedCheckoutItemSnapshot>();
        }
    }
}
