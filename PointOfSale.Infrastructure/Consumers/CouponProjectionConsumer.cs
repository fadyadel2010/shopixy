using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.MongoDB.Services;
using PointOfSale.Infrastructure.Reliability;
using System.Text.Json;

namespace PointOfSale.Infrastructure.Consumers;

/// <summary>
/// MassTransit consumer for Coupon domain events.
/// 
/// ARCHITECTURE (CQRS COMPLIANCE):
/// - Projects Coupon data from PostgreSQL to MongoDB
/// - PostgreSQL remains the source of truth for coupon identity, lifecycle, eligibility, and usage
/// - MongoDB is derivative visibility/read-model evidence only and cannot accept coupon authority
/// - All projection writes are idempotent by (TenantId, CouponId)
/// 
/// Events handled:
/// - CouponCreatedEvent: Upsert coupon document
/// - CouponUpdatedEvent: Upsert coupon document (same as create)
/// - CouponDeletedEvent: Remove coupon document
/// - CouponUsageChangedEvent: Update CurrentUsage field only (LEGACY)
/// - CouponConsumedEvent: Update CurrentUsage field (RECOMMENDED - uses CouponUsages table)
/// </summary>
public class CouponProjectionConsumer :
    IConsumer<CouponCreatedEvent>,
    IConsumer<CouponUpdatedEvent>,
    IConsumer<CouponDeletedEvent>,
    IConsumer<CouponUsageChangedEvent>,
    IConsumer<CouponConsumedEvent>
{
    private readonly IMongoCollection<CouponDocument> _collection;
    private readonly ApplicationDbContext _dbContext;
    private readonly CommerceReliabilityTelemetry _telemetry;
    private readonly ILogger<CouponProjectionConsumer> _logger;

    private const string CollectionName = "coupons";
    private const string ConsumerName = nameof(CouponProjectionConsumer);

    public CouponProjectionConsumer(
        IMongoDatabase database,
        ApplicationDbContext dbContext,
        CommerceReliabilityTelemetry telemetry,
        ILogger<CouponProjectionConsumer> logger)
    {
        _collection = database.GetCollection<CouponDocument>(CollectionName);
        _dbContext = dbContext;
        _telemetry = telemetry;
        _logger = logger;
        // Index creation moved to MongoDbContext.EnsureIndexesCreatedAsync for centralized startup initialization
    }

    /// <summary>
    /// Handle CouponCreatedEvent - upsert coupon document to MongoDB
    /// </summary>
    public async Task Consume(ConsumeContext<CouponCreatedEvent> context)
    {
        var @event = context.Message;
        var correlationId = @event.CorrelationId ?? context.CorrelationId?.ToString() ?? "N/A";

        _logger.LogInformation(
            "Processing CouponCreatedEvent | EventId: {EventId} | CouponId: {CouponId} | Code: {Code} | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
            @event.EventId, @event.CouponId, @event.Code, @event.TenantId, correlationId);

        // Validate TenantId
        if (!@event.TenantId.HasValue)
        {
            _logger.LogError(
                "CouponCreatedEvent rejected: TenantId is missing | EventId: {EventId} | CouponId: {CouponId}",
                @event.EventId, @event.CouponId);
            return;
        }

        try
        {
            var applied = await ProjectCouponFromSqlAuthorityAsync(
                @event,
                nameof(CouponCreatedEvent),
                context.CorrelationId?.ToString() ?? @event.CorrelationId ?? @event.EventId.ToString(),
                allowMissingSqlAuthority: false,
                context.CancellationToken);

            _logger.LogInformation(
                "CouponCreatedEvent processed | CouponId: {CouponId} | Code: {Code} | Applied: {Applied} | TenantId: {TenantId}",
                @event.CouponId, @event.Code, applied, @event.TenantId);
        }
        catch (MongoException ex)
        {
            _logger.LogError(ex,
                "MongoDB error processing CouponCreatedEvent | EventId: {EventId} | CouponId: {CouponId}",
                @event.EventId, @event.CouponId);
            
            // Best-effort telemetry - don't let it mask the original exception
            try
            {
                _telemetry.RecordCouponEventPersistFailure(@event.TenantId!.Value, "CouponCreatedEvent MongoDB error");
            }
            catch (Exception telemetryEx)
            {
                _logger.LogWarning(telemetryEx,
                    "Telemetry recording failed for CouponCreatedEvent | EventId: {EventId} | CouponId: {CouponId}",
                    @event.EventId, @event.CouponId);
            }
            
            throw;
        }
    }

    /// <summary>
    /// Handle CouponUpdatedEvent - upsert coupon document to MongoDB
    /// </summary>
    public async Task Consume(ConsumeContext<CouponUpdatedEvent> context)
    {
        var @event = context.Message;
        var correlationId = @event.CorrelationId ?? context.CorrelationId?.ToString() ?? "N/A";

        _logger.LogInformation(
            "Processing CouponUpdatedEvent | EventId: {EventId} | CouponId: {CouponId} | Code: {Code} | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
            @event.EventId, @event.CouponId, @event.Code, @event.TenantId, correlationId);

        if (!@event.TenantId.HasValue)
        {
            _logger.LogError(
                "CouponUpdatedEvent rejected: TenantId is missing | EventId: {EventId} | CouponId: {CouponId}",
                @event.EventId, @event.CouponId);
            return;
        }

        try
        {
            var applied = await ProjectCouponFromSqlAuthorityAsync(
                @event,
                nameof(CouponUpdatedEvent),
                context.CorrelationId?.ToString() ?? @event.CorrelationId ?? @event.EventId.ToString(),
                allowMissingSqlAuthority: false,
                context.CancellationToken);

            _logger.LogInformation(
                "CouponUpdatedEvent processed | CouponId: {CouponId} | Code: {Code} | Applied: {Applied} | TenantId: {TenantId}",
                @event.CouponId, @event.Code, applied, @event.TenantId);
        }
        catch (MongoException ex)
        {
            _logger.LogError(ex,
                "MongoDB error processing CouponUpdatedEvent | EventId: {EventId} | CouponId: {CouponId}",
                @event.EventId, @event.CouponId);
            
            // Best-effort telemetry - don't let it mask the original exception
            try
            {
                _telemetry.RecordCouponEventPersistFailure(@event.TenantId!.Value, "CouponUpdatedEvent MongoDB error");
            }
            catch (Exception telemetryEx)
            {
                _logger.LogWarning(telemetryEx,
                    "Telemetry recording failed for CouponUpdatedEvent | EventId: {EventId} | CouponId: {CouponId}",
                    @event.EventId, @event.CouponId);
            }
            
            throw;
        }
    }

    /// <summary>
    /// Handle CouponDeletedEvent - soft delete coupon document (preserve CreatedAtUtc, set DeletedAtUtc)
    /// </summary>
    public async Task Consume(ConsumeContext<CouponDeletedEvent> context)
    {
        var @event = context.Message;
        var correlationId = @event.CorrelationId ?? context.CorrelationId?.ToString() ?? "N/A";

        _logger.LogInformation(
            "Processing CouponDeletedEvent | EventId: {EventId} | CouponId: {CouponId} | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
            @event.EventId, @event.CouponId, @event.TenantId, correlationId);

        if (!@event.TenantId.HasValue)
        {
            _logger.LogError(
                "CouponDeletedEvent rejected: TenantId is missing | EventId: {EventId} | CouponId: {CouponId}",
                @event.EventId, @event.CouponId);
            return;
        }

        try
        {
            var result = await ProjectCouponFromSqlAuthorityAsync(
                @event,
                nameof(CouponDeletedEvent),
                context.CorrelationId?.ToString() ?? @event.CorrelationId ?? @event.EventId.ToString(),
                allowMissingSqlAuthority: true,
                context.CancellationToken);

            _logger.LogInformation(
                "CouponDeletedEvent processed | CouponId: {CouponId} | Applied: {Applied} | TenantId: {TenantId}",
                @event.CouponId, result, @event.TenantId);
        }
        catch (MongoException ex)
        {
            _logger.LogError(ex,
                "MongoDB error processing CouponDeletedEvent | EventId: {EventId} | CouponId: {CouponId}",
                @event.EventId, @event.CouponId);
            
            // Best-effort telemetry - don't let it mask the original exception
            try
            {
                _telemetry.RecordCouponEventPersistFailure(@event.TenantId!.Value, "CouponDeletedEvent MongoDB error");
            }
            catch (Exception telemetryEx)
            {
                _logger.LogWarning(telemetryEx,
                    "Telemetry recording failed for CouponDeletedEvent | EventId: {EventId} | CouponId: {CouponId}",
                    @event.EventId, @event.CouponId);
            }
            
            throw;
        }
    }

    /// <summary>
    /// Handle CouponUsageChangedEvent - update CurrentUsage field only
    /// CRITICAL: This is the ONLY way CurrentUsage is synchronized with MongoDB.
    /// </summary>
    public async Task Consume(ConsumeContext<CouponUsageChangedEvent> context)
    {
        var @event = context.Message;
        var correlationId = @event.CorrelationId ?? context.CorrelationId?.ToString() ?? "N/A";

        _logger.LogInformation(
            "Processing CouponUsageChangedEvent | EventId: {EventId} | CouponId: {CouponId} | CurrentUsage: {CurrentUsage} | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
            @event.EventId, @event.CouponId, @event.CurrentUsage, @event.TenantId, correlationId);

        if (!@event.TenantId.HasValue)
        {
            _logger.LogError(
                "CouponUsageChangedEvent rejected: TenantId is missing | EventId: {EventId} | CouponId: {CouponId}",
                @event.EventId, @event.CouponId);
            return;
        }

        try
        {
            var modified = await ProjectCouponUsageFromSqlAuthorityAsync(
                @event,
                nameof(CouponUsageChangedEvent),
                context.CorrelationId?.ToString() ?? @event.CorrelationId ?? @event.EventId.ToString(),
                context.CancellationToken);

            _logger.LogInformation(
                "CouponUsageChangedEvent processed | CouponId: {CouponId} | CurrentUsage: {CurrentUsage} | Modified: {Modified} | TenantId: {TenantId}",
                @event.CouponId, @event.CurrentUsage, modified, @event.TenantId);
        }
        catch (MongoException ex)
        {
            _logger.LogError(ex,
                "MongoDB error processing CouponUsageChangedEvent | EventId: {EventId} | CouponId: {CouponId}",
                @event.EventId, @event.CouponId);
            
            // Best-effort telemetry - don't let it mask the original exception
            try
            {
                _telemetry.RecordCouponEventPersistFailure(@event.TenantId!.Value, "CouponUsageChangedEvent MongoDB error");
            }
            catch (Exception telemetryEx)
            {
                _logger.LogWarning(telemetryEx,
                    "Telemetry recording failed for CouponUsageChangedEvent | EventId: {EventId} | CouponId: {CouponId}",
                    @event.EventId, @event.CouponId);
            }
            
            throw;
        }
    }

    /// <summary>
    /// Handle CouponConsumedEvent - update CurrentUsage from CouponUsages table count.
    /// RECOMMENDED: This event uses CouponUsages table as source of truth.
    /// </summary>
    public async Task Consume(ConsumeContext<CouponConsumedEvent> context)
    {
        var @event = context.Message;
        var correlationId = @event.CorrelationId ?? context.CorrelationId?.ToString() ?? "N/A";

        _logger.LogInformation(
            "Processing CouponConsumedEvent | EventId: {EventId} | CouponId: {CouponId} | NewUsage: {NewUsage} | OrderId: {OrderId} | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
            @event.EventId, @event.CouponId, @event.NewTotalUsageCount, @event.OrderId, @event.TenantId, correlationId);

        if (!@event.TenantId.HasValue)
        {
            _logger.LogError(
                "CouponConsumedEvent rejected: TenantId is missing | EventId: {EventId} | CouponId: {CouponId}",
                @event.EventId, @event.CouponId);
            return;
        }

        try
        {
            var modified = await ProjectCouponUsageFromSqlAuthorityAsync(
                @event,
                nameof(CouponConsumedEvent),
                context.CorrelationId?.ToString() ?? @event.CorrelationId ?? @event.EventId.ToString(),
                context.CancellationToken);

            _logger.LogInformation(
                "CouponConsumedEvent processed | CouponId: {CouponId} | NewUsage: {NewUsage} | OrderId: {OrderId} | Modified: {Modified} | TenantId: {TenantId}",
                @event.CouponId, @event.NewTotalUsageCount, @event.OrderId, modified, @event.TenantId);
        }
        catch (MongoException ex)
        {
            _logger.LogError(ex,
                "MongoDB error processing CouponConsumedEvent | EventId: {EventId} | CouponId: {CouponId} | OrderId: {OrderId}",
                @event.EventId, @event.CouponId, @event.OrderId);
            
            // Best-effort telemetry - don't let it mask the original exception
            try
            {
                _telemetry.RecordCouponEventPersistFailure(@event.TenantId!.Value, "CouponConsumedEvent MongoDB error");
            }
            catch (Exception telemetryEx)
            {
                _logger.LogWarning(telemetryEx,
                    "Telemetry recording failed for CouponConsumedEvent | EventId: {EventId} | CouponId: {CouponId} | OrderId: {OrderId}",
                    @event.EventId, @event.CouponId, @event.OrderId);
            }
            
            throw;
        }
    }

    private async Task<bool> ProjectCouponFromSqlAuthorityAsync(
        IntegrationEvent @event,
        string eventType,
        string correlationId,
        bool allowMissingSqlAuthority,
        CancellationToken cancellationToken)
    {
        var (tenantId, couponId) = ResolveCouponProjectionIdentity(@event);
        var key = new ProjectionEventFenceKey(ConsumerName, eventType, couponId);
        await using var fence = await ProjectionEventFence.TryAcquireAsync(
            _dbContext,
            @event,
            key,
            _logger,
            cancellationToken);

        if (fence == null)
        {
            _logger.LogInformation(
                "Coupon projection duplicate or active replay collapsed before MongoDB writes | EventType: {EventType} | TenantId: {TenantId} | CouponId: {CouponId} | EventId: {EventId}",
                eventType,
                tenantId,
                couponId,
                @event.EventId);
            return false;
        }

        var sqlCoupon = await LoadSqlCouponAuthorityAsync(tenantId, couponId, cancellationToken);
        CouponDocument document;

        if (sqlCoupon == null && !allowMissingSqlAuthority)
        {
            _logger.LogWarning(
                "Coupon projection found no SQL authority; writing an inactive tombstone so stale visibility cannot survive | EventType: {EventType} | TenantId: {TenantId} | CouponId: {CouponId} | EventId: {EventId}",
                eventType,
                tenantId,
                couponId,
                @event.EventId);
        }

        if (sqlCoupon is null || sqlCoupon.IsDeleted)
        {
            document = await BuildTombstoneFromSqlAuthorityAsync(
                tenantId,
                couponId,
                @event,
                sqlCoupon,
                cancellationToken);
        }
        else
        {
            document = MapSqlAuthorityToDocument(sqlCoupon, @event);
        }

        var applied = await UpsertDocumentAsync(document, @event, cancellationToken);
        await fence.CompleteAsync(@event.EventId, tenantId, correlationId, key, cancellationToken);
        return applied;
    }

    private async Task<bool> ProjectCouponUsageFromSqlAuthorityAsync(
        IntegrationEvent @event,
        string eventType,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var (tenantId, couponId) = ResolveCouponProjectionIdentity(@event);
        var key = new ProjectionEventFenceKey(ConsumerName, eventType, couponId);
        await using var fence = await ProjectionEventFence.TryAcquireAsync(
            _dbContext,
            @event,
            key,
            _logger,
            cancellationToken);

        if (fence == null)
        {
            _logger.LogInformation(
                "Coupon usage projection duplicate or active replay collapsed before MongoDB writes | EventType: {EventType} | TenantId: {TenantId} | CouponId: {CouponId} | EventId: {EventId}",
                eventType,
                tenantId,
                couponId,
                @event.EventId);
            return false;
        }

        var sqlCoupon = await LoadSqlCouponAuthorityAsync(tenantId, couponId, cancellationToken);
        if (sqlCoupon is null || sqlCoupon.IsDeleted)
        {
            var tombstone = await BuildTombstoneFromSqlAuthorityAsync(
                tenantId,
                couponId,
                @event,
                sqlCoupon,
                cancellationToken);
            var tombstoneApplied = await UpsertDocumentAsync(tombstone, @event, cancellationToken);
            await fence.CompleteAsync(@event.EventId, tenantId, correlationId, key, cancellationToken);
            return tombstoneApplied;
        }

        var currentUsage = await _dbContext.CouponUsages
            .AsNoTracking()
            .IgnoreQueryFilters()
            .CountAsync(
                usage => usage.TenantId == tenantId &&
                         usage.CouponId == couponId &&
                         !usage.IsDeleted,
                cancellationToken);

        var filter = Builders<CouponDocument>.Filter.And(
            Builders<CouponDocument>.Filter.Eq(x => x.TenantId, tenantId),
            Builders<CouponDocument>.Filter.Eq(x => x.CouponId, couponId),
            BuildFreshnessFilter(@event));

        var update = Builders<CouponDocument>.Update
            .Set(x => x.CurrentUsage, currentUsage)
            .Set(x => x.SyncedAtUtc, DateTime.UtcNow)
            .Set(x => x.LastEventId, @event.EventId)
            .Set(x => x.LastEventOccurredAtUtc, @event.OccurredAtUtc)
            .Set(x => x.LastEventVersion, @event.Version);

        var result = await _collection.UpdateOneAsync(filter, update, cancellationToken: cancellationToken);
        await fence.CompleteAsync(@event.EventId, tenantId, correlationId, key, cancellationToken);
        return result.IsAcknowledged
            ? result.ModifiedCount > 0 || result.MatchedCount > 0 || result.UpsertedId != null
            : true;
    }

    private async Task<Coupon?> LoadSqlCouponAuthorityAsync(
        Guid tenantId,
        Guid couponId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.Coupons
            .AsNoTracking()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                coupon => coupon.TenantId == tenantId && coupon.Id == couponId,
                cancellationToken);
    }

    private static (Guid TenantId, Guid CouponId) ResolveCouponProjectionIdentity(IntegrationEvent @event)
    {
        if (!@event.TenantId.HasValue)
        {
            throw new InvalidOperationException($"Coupon projection event '{@event.EventType}' is missing TenantId.");
        }

        var couponId = @event switch
        {
            CouponCreatedEvent created => created.CouponId,
            CouponUpdatedEvent updated => updated.CouponId,
            CouponDeletedEvent deleted => deleted.CouponId,
            CouponUsageChangedEvent usageChanged => usageChanged.CouponId,
            CouponConsumedEvent consumed => consumed.CouponId,
            _ => throw new InvalidOperationException($"Unsupported coupon projection event type '{@event.EventType}'.")
        };

        if (couponId == Guid.Empty)
        {
            throw new InvalidOperationException($"Coupon projection event '{@event.EventType}' has an empty CouponId.");
        }

        return (@event.TenantId.Value, couponId);
    }

    private async Task<CouponDocument> BuildTombstoneFromSqlAuthorityAsync(
        Guid tenantId,
        Guid couponId,
        IntegrationEvent @event,
        Coupon? sqlCoupon,
        CancellationToken cancellationToken)
    {
        var existingFilter = Builders<CouponDocument>.Filter.And(
            Builders<CouponDocument>.Filter.Eq(x => x.TenantId, tenantId),
            Builders<CouponDocument>.Filter.Eq(x => x.CouponId, couponId));
        var existingDocument = await _collection
            .Find(existingFilter)
            .FirstOrDefaultAsync(cancellationToken);

        if (existingDocument?.LastEventOccurredAtUtc is { } existingOccurredAtUtc &&
            ProjectionTombstoneGuard.IsTombstoneNewerThanOrEqualToEvent(
                existingOccurredAtUtc,
                existingDocument.LastEventVersion,
                @event.OccurredAtUtc,
                @event.Version))
        {
            return existingDocument;
        }

        var tombstone = existingDocument ?? new CouponDocument
        {
            TenantId = tenantId,
            CouponId = couponId,
            Code = NormalizeCode(sqlCoupon?.Code ?? ResolveCouponCode(@event)),
            IsActive = false,
            CreatedAtUtc = sqlCoupon?.CreatedAt ?? @event.OccurredAtUtc
        };

        // Always write a tombstone, even when the prior create/update projection is missing.
        // Otherwise a delayed older create/update replay can recreate checkout coupon visibility.
        tombstone.Code = NormalizeCode(sqlCoupon?.Code ?? tombstone.Code);
        tombstone.IsActive = false;
        tombstone.DeletedAtUtc = sqlCoupon?.UpdatedAt ?? @event.OccurredAtUtc;
        tombstone.SyncedAtUtc = DateTime.UtcNow;
        tombstone.LastEventId = @event.EventId;
        tombstone.LastEventOccurredAtUtc = @event.OccurredAtUtc;
        tombstone.LastEventVersion = @event.Version;
        return tombstone;
    }

    private static CouponDocument MapSqlAuthorityToDocument(Coupon coupon, IntegrationEvent @event)
    {
        return new CouponDocument
        {
            TenantId = coupon.TenantId,
            CouponId = coupon.Id,
            Code = NormalizeCode(coupon.Code),
            DiscountType = coupon.DiscountType == DiscountType.Percentage ? "percentage" : "fixed",
            DiscountValue = coupon.DiscountValue,
            MaxDiscountAmount = coupon.MaxDiscountAmount,
            MinOrderAmount = coupon.MinOrderAmount,
            MaxUsage = coupon.MaxUsage,
            CurrentUsage = coupon.CurrentUsage,
            MaxUsagePerCustomer = coupon.MaxUsagePerCustomer,
            FirstOrderOnly = coupon.FirstOrderOnly,
            StartDate = coupon.StartDate,
            EndDate = coupon.EndDate,
            IsActive = coupon.IsActive && !coupon.IsDeleted,
            Description = coupon.Description,
            ApplicableProductIds = ParseGuidList(coupon.ApplicableProductIds),
            ApplicableCategoryIds = ParseGuidList(coupon.ApplicableCategoryIds),
            ExcludedProductIds = ParseGuidList(coupon.ExcludedProductIds),
            CreatedAtUtc = coupon.CreatedAt == default ? @event.OccurredAtUtc : coupon.CreatedAt,
            SyncedAtUtc = DateTime.UtcNow,
            LastEventId = @event.EventId,
            LastEventOccurredAtUtc = @event.OccurredAtUtc,
            LastEventVersion = @event.Version
        };
    }

    private static List<Guid>? ParseGuidList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        return JsonSerializer.Deserialize<List<Guid>>(json);
    }

    private static string ResolveCouponCode(IntegrationEvent @event) =>
        @event switch
        {
            CouponCreatedEvent created => created.Code,
            CouponUpdatedEvent updated => updated.Code,
            CouponDeletedEvent deleted => deleted.Code,
            CouponUsageChangedEvent usageChanged => usageChanged.Code,
            CouponConsumedEvent consumed => consumed.CouponCode,
            _ => string.Empty
        };

    private static string NormalizeCode(string? code) =>
        code?.Trim().ToUpperInvariant() ?? string.Empty;

    private async Task<bool> UpsertDocumentAsync(CouponDocument document, IntegrationEvent @event, CancellationToken cancellationToken)
    {
        var filter = Builders<CouponDocument>.Filter.And(
            Builders<CouponDocument>.Filter.Eq(x => x.TenantId, document.TenantId),
            Builders<CouponDocument>.Filter.Eq(x => x.CouponId, document.CouponId),
            BuildFreshnessFilter(@event)
        );

        var options = new ReplaceOptions { IsUpsert = true };

        try
        {
            var result = await _collection.ReplaceOneAsync(filter, document, options, cancellationToken);
            return DidReplaceApply(result);
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            var retry = await _collection.ReplaceOneAsync(
                filter,
                document,
                new ReplaceOptions { IsUpsert = false },
                cancellationToken);

            return DidReplaceApply(retry);
        }
    }

    private static bool DidReplaceApply(ReplaceOneResult result)
    {
        if (!result.IsAcknowledged)
        {
            return true;
        }

        return result.UpsertedId != null ||
               (result.IsModifiedCountAvailable && result.ModifiedCount > 0) ||
               result.MatchedCount > 0;
    }

    private static FilterDefinition<CouponDocument> BuildFreshnessFilter(IntegrationEvent @event)
    {
        return Builders<CouponDocument>.Filter.And(
            Builders<CouponDocument>.Filter.Ne(x => x.LastEventId, @event.EventId),
            Builders<CouponDocument>.Filter.Or(
                Builders<CouponDocument>.Filter.Exists(x => x.LastEventOccurredAtUtc, false),
                Builders<CouponDocument>.Filter.Eq(x => x.LastEventOccurredAtUtc, null),
                Builders<CouponDocument>.Filter.Lt(x => x.LastEventOccurredAtUtc, @event.OccurredAtUtc),
                Builders<CouponDocument>.Filter.And(
                    Builders<CouponDocument>.Filter.Eq(x => x.LastEventOccurredAtUtc, @event.OccurredAtUtc),
                    Builders<CouponDocument>.Filter.Lt(x => x.LastEventVersion, @event.Version))));
    }
}

/// <summary>
/// Consumer definition for CouponProjectionConsumer.
/// Configures retry policy and error handling.
/// </summary>
public class CouponProjectionConsumerDefinition : ConsumerDefinition<CouponProjectionConsumer>
{
    public CouponProjectionConsumerDefinition()
    {
        EndpointName = "coupon-projection";
    }

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<CouponProjectionConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        // Configure retry policy for transient failures
        endpointConfigurator.UseMessageRetry(r => r.Intervals(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(15)));
    }
}
