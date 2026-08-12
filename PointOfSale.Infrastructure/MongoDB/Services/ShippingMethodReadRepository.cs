using System.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using PointOfSale.Core.Entities;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.Utilities;

namespace PointOfSale.Infrastructure.MongoDB.Services;

/// <summary>
/// MongoDB repository implementation for shipping method read operations.
/// 
/// ARCHITECTURE:
/// - MongoDB is the read-only projection source for Storefront
/// - All queries MUST filter by TenantId
/// - Upserts are idempotent by (TenantId, ShippingMethodId)
/// - No business logic - pure data access
/// </summary>
public class ShippingMethodReadRepository : IShippingMethodReadRepository
{
    private readonly IMongoCollection<ShippingMethodDocument> _collection;
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<ShippingMethodReadRepository> _logger;
    private readonly IMongoProjectionHealthCoordinator _healthCoordinator;

    private const string CollectionName = "shipping_methods";

    public ShippingMethodReadRepository(
        IMongoDatabase database,
        ApplicationDbContext dbContext,
        ILogger<ShippingMethodReadRepository> logger,
        IMongoProjectionHealthCoordinator healthCoordinator)
    {
        _collection = database.GetCollection<ShippingMethodDocument>(CollectionName);
        _dbContext = dbContext;
        _logger = logger;
        _healthCoordinator = healthCoordinator;
        
        // Attempt index creation but don't block DI resolution on failure
        // Repository can still function using SQL fallback if MongoDB indices fail
        try
        {
            EnsureIndexes();
            // S40: Mark healthy on successful index creation to reinstate previously-unhealthy collections
            _healthCoordinator.MarkHealthy(CollectionName);
        }
        catch (Exception ex)
        {
            // S40: Set health flag to unhealthy (0) via coordinator
            // to prevent race conditions under concurrent access. Recovery to healthy (1) is performed by
            // the recovery probe (TryRecoverMongoHealthAsync) via its EnsureIndexesAsync() call when
            // subsequent read operations detect the unhealthy state. Repository uses SQL fallback until
            // MongoDB indices are successfully recreated.
            _healthCoordinator.MarkUnhealthy(CollectionName);
            _logger.LogWarning(
                ex,
                "Failed to create MongoDB indices for shipping methods - repository will use SQL fallback until MongoDB recovers");
        }
    }

    /// <inheritdoc />
    public async Task<List<ShippingMethodDocument>> GetActiveShippingMethodsAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        List<ShippingMethodDocument>? result = null;
        bool mongoSucceeded = false;

        // S40: Attempt MongoDB read even if unhealthy to allow self-healing
        // If unhealthy, try a lightweight health probe first
        await TryRecoverMongoHealthAsync(tenantId, cancellationToken: cancellationToken);

        // Attempt MongoDB read if healthy or recovery succeeded
        if (_healthCoordinator.GetHealthState(CollectionName) == 1)
        {
            try
            {
                var filter = Builders<ShippingMethodDocument>.Filter.And(
                    Builders<ShippingMethodDocument>.Filter.Eq(x => x.TenantId, tenantId),
                    Builders<ShippingMethodDocument>.Filter.Eq(x => x.IsActive, true),
                    Builders<ShippingMethodDocument>.Filter.Ne(x => x.IsDeleted, true)
                );

                var sort = Builders<ShippingMethodDocument>.Sort
                    .Ascending(x => x.SortOrder)
                    .Ascending(x => x.Name);

                result = await _collection
                    .Find(filter)
                    .Sort(sort)
                    .ToListAsync(cancellationToken);

                mongoSucceeded = true;

                if (result.Count > 0)
                {
                    _logger.LogDebug(
                        "GetActiveShippingMethods | TenantId: {TenantId} | ResultCount: {Count}",
                        tenantId, result.Count);

                    return result;
                }

                _logger.LogDebug(
                    "Shipping method projection empty; falling back to SQL | TenantId: {TenantId}",
                    tenantId);
            }
            catch (Exception ex) when (IsMongoReadFailure(ex))
            {
                // S40: Mark unhealthy on read failure via coordinator
                _healthCoordinator.MarkUnhealthy(CollectionName);
                _logger.LogError(
                    ex,
                    "Shipping method read model unavailable; falling back to SQL | TenantId: {TenantId}",
                    tenantId);
            }
        }

        var methods = await _dbContext.ShippingMethods
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.IsActive && !x.IsDeleted)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Name)
            .ToListAsync(cancellationToken);

        if (mongoSucceeded && methods.Count > 0)
        {
            _logger.LogWarning(
                "Shipping method projection drift detected | TenantId: {TenantId} | SQL found {Count} records that Mongo missed",
                tenantId, methods.Count);
        }

        var tenantCurrency = methods.Count == 0
            ? string.Empty
            : await ResolveTenantCurrencyAsync(tenantId, cancellationToken);

        return methods.Select(method => MapToDocument(method, tenantCurrency)).ToList();
    }

    /// <inheritdoc />
    public async Task<ShippingMethodDocument?> GetByIdAsync(
        Guid tenantId,
        Guid shippingMethodId,
        CancellationToken cancellationToken = default)
    {
        bool mongoSucceeded = false;

        // S40: Attempt MongoDB read even if unhealthy to allow self-healing
        // If unhealthy, try a lightweight health probe first
        await TryRecoverMongoHealthAsync(tenantId, shippingMethodId, cancellationToken);

        // Attempt MongoDB read if healthy or recovery succeeded
        if (_healthCoordinator.GetHealthState(CollectionName) == 1)
        {
            try
            {
                var filter = Builders<ShippingMethodDocument>.Filter.And(
                    Builders<ShippingMethodDocument>.Filter.Eq(x => x.TenantId, tenantId),
                    Builders<ShippingMethodDocument>.Filter.Eq(x => x.ShippingMethodId, shippingMethodId),
                    Builders<ShippingMethodDocument>.Filter.Ne(x => x.IsDeleted, true)
                );

                var document = await _collection.Find(filter).FirstOrDefaultAsync(cancellationToken);
                mongoSucceeded = true;

                if (document != null)
                {
                    return document;
                }

                _logger.LogDebug(
                    "Shipping method projection miss; falling back to SQL | TenantId: {TenantId} | ShippingMethodId: {ShippingMethodId}",
                    tenantId,
                    shippingMethodId);
            }
            catch (Exception ex) when (IsMongoReadFailure(ex))
            {
                // S40: Mark unhealthy on read failure via coordinator
                _healthCoordinator.MarkUnhealthy(CollectionName);
                _logger.LogError(
                    ex,
                    "Shipping method read model unavailable; falling back to SQL | TenantId: {TenantId} | ShippingMethodId: {ShippingMethodId}",
                    tenantId,
                    shippingMethodId);
            }
        }

        var method = await _dbContext.ShippingMethods
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.Id == shippingMethodId && !x.IsDeleted)
            .FirstOrDefaultAsync(cancellationToken);

        if (mongoSucceeded && method != null)
        {
            _logger.LogWarning(
                "Shipping method projection drift detected | TenantId: {TenantId} | ShippingMethodId: {ShippingMethodId} | SQL found record that Mongo missed",
                tenantId,
                shippingMethodId);
        }

        if (method == null)
        {
            return null;
        }

        var tenantCurrency = await ResolveTenantCurrencyAsync(tenantId, cancellationToken);
        return MapToDocument(method, tenantCurrency);
    }

    /// <inheritdoc />
    public async Task<bool> UpsertAsync(
        ShippingMethodDocument document,
        CancellationToken cancellationToken = default)
    {
        var filter = Builders<ShippingMethodDocument>.Filter.And(
            Builders<ShippingMethodDocument>.Filter.Eq(x => x.TenantId, document.TenantId),
            Builders<ShippingMethodDocument>.Filter.Eq(x => x.ShippingMethodId, document.ShippingMethodId)
        );

        var options = new ReplaceOptions { IsUpsert = true };

        var result = await _collection.ReplaceOneAsync(filter, document, options, cancellationToken);

        var success = result.IsAcknowledged && (result.ModifiedCount > 0 || result.UpsertedId != null);

        _logger.LogDebug(
            "Upsert ShippingMethod | TenantId: {TenantId} | ShippingMethodId: {ShippingMethodId} | Success: {Success}",
            document.TenantId, document.ShippingMethodId, success);

        return success;
    }

    /// <inheritdoc />
    public async Task<bool> UpsertIfFreshAsync(
        ShippingMethodDocument document,
        Guid eventId,
        DateTime occurredAtUtc,
        int eventVersion,
        CancellationToken cancellationToken = default)
    {
        document.IsDeleted = false;
        document.LastEventId = eventId;
        document.LastEventOccurredAtUtc = occurredAtUtc;
        document.LastEventVersion = eventVersion;

        var filter = Builders<ShippingMethodDocument>.Filter.And(
            Builders<ShippingMethodDocument>.Filter.Eq(x => x.TenantId, document.TenantId),
            Builders<ShippingMethodDocument>.Filter.Eq(x => x.ShippingMethodId, document.ShippingMethodId),
            BuildFreshnessFilter(eventId, occurredAtUtc, eventVersion));

        try
        {
            var result = await _collection.ReplaceOneAsync(
                filter,
                document,
                new ReplaceOptions { IsUpsert = true },
                cancellationToken);

            var success = result.IsAcknowledged && (result.ModifiedCount > 0 || result.UpsertedId != null);

            _logger.LogDebug(
                "Fresh upsert ShippingMethod | TenantId: {TenantId} | ShippingMethodId: {ShippingMethodId} | EventId: {EventId} | Success: {Success}",
                document.TenantId, document.ShippingMethodId, eventId, success);

            return success;
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            var retry = await _collection.ReplaceOneAsync(
                filter,
                document,
                new ReplaceOptions { IsUpsert = false },
                cancellationToken);

            return retry.IsAcknowledged && retry.ModifiedCount > 0;
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(
        Guid tenantId,
        Guid shippingMethodId,
        CancellationToken cancellationToken = default)
    {
        var filter = Builders<ShippingMethodDocument>.Filter.And(
            Builders<ShippingMethodDocument>.Filter.Eq(x => x.TenantId, tenantId),
            Builders<ShippingMethodDocument>.Filter.Eq(x => x.ShippingMethodId, shippingMethodId)
        );

        var result = await _collection.DeleteOneAsync(filter, cancellationToken);

        var success = result.IsAcknowledged && result.DeletedCount > 0;

        _logger.LogInformation(
            "Delete ShippingMethod | TenantId: {TenantId} | ShippingMethodId: {ShippingMethodId} | Deleted: {Deleted}",
            tenantId, shippingMethodId, success);

        return success;
    }

    /// <inheritdoc />
    public async Task<bool> TombstoneIfFreshAsync(
        Guid tenantId,
        Guid shippingMethodId,
        Guid eventId,
        DateTime occurredAtUtc,
        int eventVersion,
        CancellationToken cancellationToken = default)
    {
        var tombstone = new ShippingMethodDocument
        {
            TenantId = tenantId,
            ShippingMethodId = shippingMethodId,
            IsActive = false,
            IsDeleted = true,
            SyncedAtUtc = DateTime.UtcNow,
            LastEventId = eventId,
            LastEventOccurredAtUtc = occurredAtUtc,
            LastEventVersion = eventVersion
        };

        var filter = Builders<ShippingMethodDocument>.Filter.And(
            Builders<ShippingMethodDocument>.Filter.Eq(x => x.TenantId, tenantId),
            Builders<ShippingMethodDocument>.Filter.Eq(x => x.ShippingMethodId, shippingMethodId),
            BuildFreshnessFilter(eventId, occurredAtUtc, eventVersion));

        try
        {
            var result = await _collection.ReplaceOneAsync(
                filter,
                tombstone,
                new ReplaceOptions { IsUpsert = true },
                cancellationToken);

            var success = result.IsAcknowledged && (result.ModifiedCount > 0 || result.UpsertedId != null);

            _logger.LogInformation(
                "Tombstone ShippingMethod | TenantId: {TenantId} | ShippingMethodId: {ShippingMethodId} | EventId: {EventId} | Applied: {Applied}",
                tenantId, shippingMethodId, eventId, success);

            return success;
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            var retry = await _collection.ReplaceOneAsync(
                filter,
                tombstone,
                new ReplaceOptions { IsUpsert = false },
                cancellationToken);

            return retry.IsAcknowledged && retry.ModifiedCount > 0;
        }
    }

    private async Task<string> ResolveTenantCurrencyAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var tenantCurrency = await _dbContext.Tenants
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(tenant => tenant.Id == tenantId)
            .Select(tenant => tenant.Currency)
            .FirstOrDefaultAsync(cancellationToken);

        return CurrencyNormalizationHelper.NormalizeCurrencyOrThrow(
            tenantCurrency,
            $"Repository: {nameof(ShippingMethodReadRepository)}, Method: {nameof(ResolveTenantCurrencyAsync)}, TenantId: {tenantId}");
    }

    private static ShippingMethodDocument MapToDocument(ShippingMethod method, string tenantCurrency)
    {
        return new ShippingMethodDocument
        {
            TenantId = method.TenantId,
            ShippingMethodId = method.Id,
            Name = method.Name,
            NameAr = method.NameAr,
            Description = method.Description,
            BaseRate = method.BaseRate,
            PerKgRate = method.PerKgRate,
            FreeShippingThreshold = method.FreeShippingThreshold,
            Currency = tenantCurrency,
            EstimatedDeliveryDaysMin = method.EstimatedDeliveryDaysMin,
            EstimatedDeliveryDaysMax = method.EstimatedDeliveryDaysMax,
            IsActive = method.IsActive,
            SortOrder = method.SortOrder,
            CarrierName = method.CarrierName,
            CarrierCode = method.CarrierCode,
            ServiceLevel = method.ServiceLevel,
            FulfillmentWarehouseId = method.FulfillmentWarehouseId,
            TrackingUrlTemplate = method.TrackingUrlTemplate,
            AvailableCountries = method.AvailableCountries,
            IsDeleted = false,
            SyncedAtUtc = method.UpdatedAt ?? method.CreatedAt
        };
    }

    private void EnsureIndexes()
    {
        var index = new CreateIndexModel<ShippingMethodDocument>(
            Builders<ShippingMethodDocument>.IndexKeys
                .Ascending(document => document.TenantId)
                .Ascending(document => document.ShippingMethodId),
            new CreateIndexOptions
            {
                Unique = true,
                Name = "idx_tenant_shipping_method_unique",
                Background = true
            });

        _collection.Indexes.CreateOne(index);
    }

    private async Task EnsureIndexesAsync(CancellationToken cancellationToken = default)
    {
        var index = new CreateIndexModel<ShippingMethodDocument>(
            Builders<ShippingMethodDocument>.IndexKeys
                .Ascending(document => document.TenantId)
                .Ascending(document => document.ShippingMethodId),
            new CreateIndexOptions
            {
                Unique = true,
                Name = "idx_tenant_shipping_method_unique",
                Background = true
            });

        await _collection.Indexes.CreateOneAsync(index, cancellationToken: cancellationToken);
    }

    private static FilterDefinition<ShippingMethodDocument> BuildFreshnessFilter(
        Guid eventId,
        DateTime occurredAtUtc,
        int eventVersion)
    {
        return Builders<ShippingMethodDocument>.Filter.And(
            Builders<ShippingMethodDocument>.Filter.Ne(x => x.LastEventId, eventId),
            Builders<ShippingMethodDocument>.Filter.Or(
                Builders<ShippingMethodDocument>.Filter.Exists(x => x.LastEventOccurredAtUtc, false),
                Builders<ShippingMethodDocument>.Filter.Eq(x => x.LastEventOccurredAtUtc, null),
                Builders<ShippingMethodDocument>.Filter.Lt(x => x.LastEventOccurredAtUtc, occurredAtUtc),
                Builders<ShippingMethodDocument>.Filter.And(
                    Builders<ShippingMethodDocument>.Filter.Eq(x => x.LastEventOccurredAtUtc, occurredAtUtc),
                    Builders<ShippingMethodDocument>.Filter.Lt(x => x.LastEventVersion, eventVersion))));
    }

    private static bool IsMongoReadFailure(Exception ex) =>
        ex is MongoException || ex is TimeoutException;

    /// <summary>
    /// Attempts to recover MongoDB projection health by recreating indices asynchronously.
    /// Uses atomic CompareExchange to ensure only one thread attempts recovery at a time,
    /// preventing thundering herd when multiple concurrent requests detect unhealthy state.
    /// Implements cooldown period to prevent recovery probe storms.
    /// </summary>
    /// <param name="tenantId">Tenant identifier for logging context</param>
    /// <param name="shippingMethodId">Optional shipping method identifier for logging context</param>
    /// <param name="cancellationToken">Cancellation token</param>
    private async Task TryRecoverMongoHealthAsync(Guid tenantId, Guid? shippingMethodId = null, CancellationToken cancellationToken = default)
    {
        // S40: Use coordinator to atomically transition from unhealthy (0) to recovering (2)
        // Only the winning thread proceeds with recovery; others skip and use SQL fallback
        // This prevents thundering herd where all threads would call EnsureIndexesAsync simultaneously
        // Cooldown period prevents recovery probe storms when MongoDB is persistently unavailable
        if (_healthCoordinator.TryBeginRecovery(CollectionName))
        {
            // This thread won the race and is now responsible for recovery
            // Build context string for logging
            var context = shippingMethodId.HasValue
                ? $"TenantId: {tenantId} | ShippingMethodId: {shippingMethodId.Value}"
                : $"TenantId: {tenantId}";

            _logger.LogDebug(
                "MongoDB projection unhealthy; attempting recovery probe | {Context}",
                context);
            
            // Attempt to recover by trying the index creation again (async to avoid blocking)
            try
            {
                await EnsureIndexesAsync(cancellationToken);
                
                // Success: transition from recovering (2) to healthy (1)
                _healthCoordinator.CompleteRecovery(CollectionName);
                
                _logger.LogInformation(
                    "MongoDB projection recovered via health probe - indices recreated | {Context}",
                    context);
            }
            catch (Exception ex)
            {
                // Failure: reset from recovering (2) back to unhealthy (0) so future probes can retry
                _healthCoordinator.FailRecovery(CollectionName);
                
                _logger.LogDebug(
                    ex,
                    "MongoDB recovery probe failed; using SQL fallback | {Context}",
                    context);
                // Caller will use SQL fallback
            }
        }
        else
        {
            // Recovery attempt skipped due to:
            // 1. Another thread is already recovering (state = 2)
            // 2. MongoDB is already healthy (state = 1)
            // 3. Cooldown period hasn't expired yet
            // In all cases, use SQL fallback
            var currentState = _healthCoordinator.GetHealthState(CollectionName);
            if (currentState == 2)
            {
                _logger.LogDebug(
                    "MongoDB recovery already in progress by another thread; using SQL fallback | TenantId: {TenantId}",
                    tenantId);
            }
            else if (currentState == 0)
            {
                _logger.LogDebug(
                    "MongoDB recovery cooldown active; using SQL fallback | TenantId: {TenantId}",
                    tenantId);
            }
            // If currentState == 1 (healthy), no action needed - MongoDB is already available
        }
    }
}
