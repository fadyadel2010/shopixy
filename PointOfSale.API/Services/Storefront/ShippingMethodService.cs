using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Exceptions;
using PointOfSale.API.Services.Pricing;
using PointOfSale.Core.Constants;
using PointOfSale.Core.Entities.ShippingAdministration;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.MongoDB.Services;
using PointOfSale.Infrastructure.Redis;
using PointOfSale.Infrastructure.Redis.Configuration;
using PointOfSale.Infrastructure.Redis.Services;

namespace PointOfSale.API.Services.Storefront;

/// <summary>
/// Implementation of Shipping Methods read service.
/// 
/// ARCHITECTURE CLOSURE PHASE: CQRS Purity
/// 
/// ARCHITECTURAL BOUNDARY (STRICT):
/// - Shipping Methods are managed ONLY in POS Admin (PostgreSQL)
/// - This API is READ-ONLY
/// - Reads from Redis (cache) → MongoDB (read model) ONLY
/// - ❌ NO SQL ACCESS - PostgreSQL is forbidden here
/// - Tenant isolation is mandatory
/// </summary>
public class ShippingMethodService : IShippingMethodService
{
    private readonly IShippingMethodReadRepository _repository;
    private readonly ApplicationDbContext _dbContext;
    private readonly RedisConnectionFactory _redisFactory;
    private readonly RedisSettings _redisSettings;
    private readonly IShippingCalculationService _shippingCalculationService;
    private readonly CommerceReliabilityTelemetry _reliabilityTelemetry;
    private readonly ILogger<ShippingMethodService> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>
    /// Cache TTL for shipping methods (30 minutes - rarely changes)
    /// </summary>
    private const int CacheTtlMinutes = 30;

    public ShippingMethodService(
        IShippingMethodReadRepository repository,
        ApplicationDbContext dbContext,
        RedisConnectionFactory redisFactory,
        IOptions<RedisSettings> redisSettings,
        IShippingCalculationService shippingCalculationService,
        CommerceReliabilityTelemetry reliabilityTelemetry,
        ILogger<ShippingMethodService> logger)
    {
        _repository = repository;
        _dbContext = dbContext;
        _redisFactory = redisFactory;
        _redisSettings = redisSettings.Value;
        _shippingCalculationService = shippingCalculationService;
        _reliabilityTelemetry = reliabilityTelemetry;
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
    }

    /// <inheritdoc />
    public async Task<List<ShippingMethodDto>> GetShippingMethodsAsync(
        Guid tenantId,
        decimal cartSubtotal,
        decimal totalWeight,
        string correlationId,
        Guid? warehouseId = null,
        CancellationToken cancellationToken = default)
    {
        if (warehouseId.HasValue && warehouseId.Value == Guid.Empty)
        {
            _logger.LogWarning(
                "Shipping methods warehouse filter rejected empty warehouse | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
                tenantId,
                correlationId);
            return new List<ShippingMethodDto>();
        }

        // Step 1: Try Redis cache first
        var cacheKey = CacheKeyBuilder.ShippingMethodsKey(_redisSettings.InstanceName, tenantId);
        var cached = warehouseId.HasValue
            ? null
            : await TryGetFromCacheAsync<List<ShippingMethodDto>>(cacheKey, correlationId);

        if (cached != null)
        {
            string? cacheScopeFailureReason;
            string cacheCurrencyFailureReason = "not_evaluated";

            if (TryValidateCachedTenantScope(tenantId, cached, correlationId, out cacheScopeFailureReason) &&
                TryResolveProjectedCurrency(
                    cached.Select(method => method.Currency),
                    "shipping_methods_cache",
                    correlationId,
                    out var cachedCurrency,
                    out cacheCurrencyFailureReason))
            {
                foreach (var method in cached)
                {
                    method.Currency = cachedCurrency;
                }

                ApplyCalculatedPrices(cached, cartSubtotal, totalWeight);

                _logger.LogDebug(
                    "Shipping methods cache HIT | TenantId: {TenantId} | Count: {Count} | CacheHit: true | CorrelationId: {CorrelationId}",
                    tenantId, cached.Count, correlationId);
                return cached;
            }

            // Cache invalidated due to scope or currency resolution failure
            var cacheFailureReason = cacheScopeFailureReason ?? cacheCurrencyFailureReason;
            _reliabilityTelemetry.RecordCurrencyProjectionMismatch(projection: "shipping_methods_cache", reason: cacheFailureReason);
            var cachedCurrencies = cached
                .Select(method => string.IsNullOrWhiteSpace(method.Currency) 
                    ? "<missing>" 
                    : method.Currency.Trim().ToUpperInvariant())
                .Distinct()
                .ToList();
            _logger.LogWarning(
                "Shipping methods cache INVALIDATED due to authority validation failure | TenantId: {TenantId} | CachedCount: {Count} | Reason: {Reason} | ProjectedCurrencies: {Currencies} | CorrelationId: {CorrelationId}",
                tenantId, cached.Count, cacheFailureReason, string.Join(", ", cachedCurrencies), correlationId);
        }

        // Step 2: Query MongoDB (Read Model) - NO SQL ACCESS
        var documents = await _repository.GetActiveShippingMethodsAsync(tenantId, cancellationToken);
        documents = await ApplyWarehouseAssignmentFilterAsync(
            tenantId,
            warehouseId,
            documents,
            correlationId,
            cancellationToken);
        var projectedCurrency = ResolveProjectedCurrency(tenantId, documents, correlationId);

        var shippingMethods = documents.Select(doc => new ShippingMethodDto
        {
            TenantId = doc.TenantId,
            Id = doc.ShippingMethodId,
            Name = doc.Name,
            NameAr = doc.NameAr,
            Description = doc.Description,
            BaseRate = doc.BaseRate,
            Price = 0m,
            Currency = projectedCurrency,
            RatePerKg = doc.PerKgRate,
            FreeShippingThreshold = doc.FreeShippingThreshold,
            DeliveryDays = FormatDeliveryDays(doc.EstimatedDeliveryDaysMin, doc.EstimatedDeliveryDaysMax),
            EstimatedDays = doc.EstimatedDeliveryDaysMax,
            CarrierName = doc.CarrierName,
            SortOrder = doc.SortOrder
        }).ToList();

        ApplyCalculatedPrices(shippingMethods, cartSubtotal, totalWeight);

        _logger.LogInformation(
            "Shipping methods loaded from MongoDB | TenantId: {TenantId} | ResultCount: {ResultCount} | CacheHit: false | CorrelationId: {CorrelationId}",
            tenantId, shippingMethods.Count, correlationId);

        // Step 3: Populate Redis cache (fire-and-forget). Warehouse-filtered reads
        // are not cached under the tenant-wide key because assignment authority is
        // warehouse-scoped.
        if (!warehouseId.HasValue)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await SetCacheAsync(cacheKey, shippingMethods, CacheTtlMinutes, correlationId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to cache shipping methods (non-fatal) | CacheKey: {CacheKey} | CorrelationId: {CorrelationId}",
                        cacheKey, correlationId);
                }
            }, cancellationToken);
        }

        return shippingMethods;
    }

    #region Helper Methods

    private async Task<List<ShippingMethodDocument>> ApplyWarehouseAssignmentFilterAsync(
        Guid tenantId,
        Guid? warehouseId,
        List<ShippingMethodDocument> documents,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (!warehouseId.HasValue)
        {
            return documents;
        }

        var warehouseExists = await _dbContext.Warehouses
            .AsNoTracking()
            .IgnoreQueryFilters()
            .AnyAsync(
                warehouse => warehouse.TenantId == tenantId &&
                             warehouse.Id == warehouseId.Value &&
                             warehouse.IsActive &&
                             !warehouse.IsDeleted,
                cancellationToken);
        if (!warehouseExists)
        {
            _logger.LogWarning(
                "Shipping methods warehouse filter failed closed because warehouse is unavailable | TenantId: {TenantId} | WarehouseId: {WarehouseId} | CorrelationId: {CorrelationId}",
                tenantId,
                warehouseId.Value,
                correlationId);
            return new List<ShippingMethodDocument>();
        }

        var allowedCarrierCodes = await (
                from assignment in _dbContext.Set<CarrierWarehouseAssignment>().AsNoTracking().IgnoreQueryFilters()
                join account in _dbContext.Set<CarrierAccount>().AsNoTracking().IgnoreQueryFilters()
                    on assignment.CarrierAccountId equals account.Id
                where assignment.TenantId == tenantId &&
                      account.TenantId == tenantId &&
                      assignment.WarehouseId == warehouseId.Value &&
                      assignment.IsEnabled &&
                      assignment.OriginReadinessPublished &&
                      !assignment.IsDeleted &&
                      !account.IsDeleted &&
                      account.State == CarrierAccountState.Active &&
                      account.OperationalState == CarrierOperationalState.Ready
                select account.CarrierCode)
            .ToListAsync(cancellationToken);

        var allowed = allowedCarrierCodes
            .Select(CarrierCodes.Normalize)
            .Where(code => !CarrierCodes.IsManual(code))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (allowed.Count == 0)
        {
            _logger.LogWarning(
                "Shipping methods warehouse filter failed closed because no enabled carrier assignments exist | TenantId: {TenantId} | WarehouseId: {WarehouseId} | CorrelationId: {CorrelationId}",
                tenantId,
                warehouseId.Value,
                correlationId);
            return new List<ShippingMethodDocument>();
        }

        return documents
            .Where(document =>
            {
                var normalizedCarrierCode = CarrierCodes.Normalize(document.CarrierCode);
                return !CarrierCodes.IsManual(normalizedCarrierCode) &&
                       allowed.Contains(normalizedCarrierCode);
            })
            .ToList();
    }

    private static string FormatDeliveryDays(int min, int max)
    {
        if (min == max)
        {
            return min == 1 ? "1 day" : $"{min} days";
        }
        return $"{min}-{max} days";
    }

    private async Task<T?> TryGetFromCacheAsync<T>(string key, string correlationId) where T : class
    {
        try
        {
            var db = _redisFactory.GetDatabase();
            if (db == null) return null;

            var json = await db.StringGetAsync(key);
            if (json.IsNullOrEmpty) return null;

            return JsonSerializer.Deserialize<T>(json!, _jsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Cache read error (non-fatal) | Key: {Key} | CorrelationId: {CorrelationId}",
                key, correlationId);
            return null;
        }
    }

    private async Task SetCacheAsync<T>(string key, T value, int ttlMinutes, string correlationId)
    {
        try
        {
            var db = _redisFactory.GetDatabase();
            if (db == null) return;

            var json = JsonSerializer.Serialize(value, _jsonOptions);
            await db.StringSetAsync(key, json, TimeSpan.FromMinutes(ttlMinutes));

            _logger.LogDebug(
                "Cache set | Key: {Key} | TTL: {TTL}min | CorrelationId: {CorrelationId}",
                key, ttlMinutes, correlationId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Cache write error (non-fatal) | Key: {Key} | CorrelationId: {CorrelationId}",
                key, correlationId);
        }
    }

    private string ResolveProjectedCurrency(
        Guid tenantId,
        IReadOnlyCollection<PointOfSale.Infrastructure.MongoDB.Documents.ShippingMethodDocument> documents,
        string correlationId)
    {
        if (documents.Count == 0)
        {
            return string.Empty;
        }

        if (TryResolveProjectedCurrency(
                documents.Select(document => document.Currency),
                "shipping_methods_projection",
                correlationId,
                out var projectedCurrency,
                out var projectionCurrencyFailureReason))
        {
            return projectedCurrency;
        }

        _reliabilityTelemetry.RecordCurrencyProjectionMismatch(projection: "shipping_methods_projection", reason: projectionCurrencyFailureReason);
        throw new StorefrontConfigOutOfSyncException(
            $"Projected shipping currency is inconsistent for tenant '{tenantId}'.",
            field: "ShippingCurrency");
    }

    private bool TryResolveProjectedCurrency(
        IEnumerable<string?> currencies,
        string projection,
        string correlationId,
        out string authoritativeCurrency,
        out string failureReason)
    {
        authoritativeCurrency = string.Empty;
        failureReason = "missing_currency";

        foreach (var currency in currencies)
        {
            var normalizedCurrency = NormalizeCurrency(currency);
            if (normalizedCurrency == null)
            {
                failureReason = "missing_currency";
                _logger.LogWarning(
                    "Projected shipping currency missing | Projection: {Projection} | CorrelationId: {CorrelationId}",
                    projection,
                    correlationId);
                return false;
            }

            if (string.IsNullOrEmpty(authoritativeCurrency))
            {
                authoritativeCurrency = normalizedCurrency;
                continue;
            }

            if (!string.Equals(authoritativeCurrency, normalizedCurrency, StringComparison.Ordinal))
            {
                failureReason = "mixed_currency";
                _logger.LogWarning(
                    "Projected shipping currency drift detected | Projection: {Projection} | ExpectedCurrency: {ExpectedCurrency} | ActualCurrency: {ActualCurrency} | CorrelationId: {CorrelationId}",
                    projection,
                    authoritativeCurrency,
                    normalizedCurrency,
                    correlationId);
                authoritativeCurrency = string.Empty;
                return false;
            }
        }

        return !string.IsNullOrEmpty(authoritativeCurrency);
    }

    private bool TryValidateCachedTenantScope(
        Guid tenantId,
        IReadOnlyCollection<ShippingMethodDto> cached,
        string correlationId,
        out string? failureReason)
    {
        failureReason = null;

        foreach (var method in cached)
        {
            if (method.TenantId == tenantId)
            {
                continue;
            }

            failureReason = method.TenantId == Guid.Empty
                ? "missing_tenant_scope"
                : "tenant_scope_mismatch";

            _logger.LogWarning(
                "Shipping methods cache scope rejected | ExpectedTenantId: {TenantId} | CachedTenantId: {CachedTenantId} | ShippingMethodId: {ShippingMethodId} | CorrelationId: {CorrelationId}",
                tenantId,
                method.TenantId,
                method.Id,
                correlationId);

            return false;
        }

        return true;
    }

    private static string? NormalizeCurrency(string? currency)
    {
        if (string.IsNullOrWhiteSpace(currency))
        {
            return null;
        }

        return currency.Trim().ToUpperInvariant();
    }

    private void ApplyCalculatedPrices(List<ShippingMethodDto> shippingMethods, decimal cartSubtotal, decimal totalWeight)
    {
        foreach (var shippingMethod in shippingMethods)
        {
            shippingMethod.Price = _shippingCalculationService.Calculate(
                shippingMethod.BaseRate,
                shippingMethod.RatePerKg,
                shippingMethod.FreeShippingThreshold,
                cartSubtotal,
                totalWeight);
        }
    }

    #endregion
}
