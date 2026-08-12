using Microsoft.EntityFrameworkCore;
using PointOfSale.API.DTOs.Cart;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Exceptions;
using PointOfSale.API.Services.Pricing;
using PointOfSale.Core.Constants;
using PointOfSale.Core.Entities;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.MongoDB.Services;
using PointOfSale.Infrastructure.Reliability;
using PointOfSale.Infrastructure.Redis.Services;
using PointOfSale.Infrastructure.Shipping;

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
    private readonly IShippingMethodCacheService _cache;
    private readonly IShippingCalculationService _shippingCalculationService;
    private readonly ICarrierIntegrationAuthority _carrierIntegrationAuthority;
    private readonly CommerceReliabilityTelemetry _reliabilityTelemetry;
    private readonly ILogger<ShippingMethodService> _logger;

    public ShippingMethodService(
        IShippingMethodReadRepository repository,
        ApplicationDbContext dbContext,
        IShippingMethodCacheService cache,
        IShippingCalculationService shippingCalculationService,
        CommerceReliabilityTelemetry reliabilityTelemetry,
        ILogger<ShippingMethodService> logger,
        ICarrierIntegrationAuthority? carrierIntegrationAuthority = null)
    {
        _repository = repository;
        _dbContext = dbContext;
        _cache = cache;
        _shippingCalculationService = shippingCalculationService;
        _carrierIntegrationAuthority = carrierIntegrationAuthority ?? NullCarrierIntegrationAuthority.Instance;
        _reliabilityTelemetry = reliabilityTelemetry;
        _logger = logger;
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

        List<ShippingMethodDocument> documents;
        if (!warehouseId.HasValue)
        {
            documents = (await _cache.GetOrCreateAsync(
                tenantId,
                async ct => await _repository.GetActiveShippingMethodsAsync(tenantId, ct),
                cancellationToken)).ToList();

            string? cacheScopeFailureReason;
            string cacheCurrencyFailureReason = "not_evaluated";

            if (TryValidateCachedTenantScope(tenantId, documents, correlationId, out cacheScopeFailureReason) &&
                TryResolveProjectedCurrency(
                    documents.Select(method => method.Currency),
                    "shipping_methods_cache",
                    correlationId,
                    out var cachedCurrency,
                    out cacheCurrencyFailureReason))
            {
                _logger.LogDebug(
                    "Shipping methods loaded through cache owner | TenantId: {TenantId} | Count: {Count} | CorrelationId: {CorrelationId}",
                    tenantId, documents.Count, correlationId);
            }
            else
            {
                // Cache invalidated due to scope or currency resolution failure
                var cacheFailureReason = cacheScopeFailureReason ?? cacheCurrencyFailureReason;
                _reliabilityTelemetry.RecordCurrencyProjectionMismatch(projection: "shipping_methods_cache", reason: cacheFailureReason);
                var cachedCurrencies = documents
                    .Select(method => string.IsNullOrWhiteSpace(method.Currency)
                        ? "<missing>"
                        : method.Currency.Trim().ToUpperInvariant())
                    .Distinct()
                    .ToList();
                _logger.LogWarning(
                    "Shipping methods cache INVALIDATED due to authority validation failure | TenantId: {TenantId} | CachedCount: {Count} | Reason: {Reason} | ProjectedCurrencies: {Currencies} | CorrelationId: {CorrelationId}",
                    tenantId, documents.Count, cacheFailureReason, string.Join(", ", cachedCurrencies), correlationId);

                await _cache.InvalidateAsync(tenantId, cancellationToken);
                documents = (await _cache.GetOrCreateAsync(
                    tenantId,
                    async ct => await _repository.GetActiveShippingMethodsAsync(tenantId, ct),
                    cancellationToken)).ToList();
            }
        }
        else
        {
            documents = await _repository.GetActiveShippingMethodsAsync(tenantId, cancellationToken);
        }

        documents = await ApplyConfiguredWarehouseAuthorityFilterAsync(
            tenantId,
            warehouseId,
            documents,
            correlationId,
            cancellationToken);

        var projectedCurrency = ResolveProjectedCurrency(tenantId, documents, correlationId);

        var shippingMethods = MapToDtos(documents, projectedCurrency);

        ApplyCalculatedPrices(shippingMethods, cartSubtotal, totalWeight);

        _logger.LogInformation(
            "Shipping methods loaded from MongoDB | TenantId: {TenantId} | ResultCount: {ResultCount} | CacheHit: false | CorrelationId: {CorrelationId}",
            tenantId, shippingMethods.Count, correlationId);

        return shippingMethods;
    }

    public async Task<ShippingMethodsListResponseDto> GetShippingMethodsAvailabilityAsync(
        Guid tenantId,
        decimal cartSubtotal,
        decimal totalWeight,
        string correlationId,
        Guid? warehouseId = null,
        CancellationToken cancellationToken = default)
    {
        var configuredCount = await _dbContext.ShippingMethods
            .AsNoTracking()
            .IgnoreQueryFilters()
            .CountAsync(method => method.TenantId == tenantId && !method.IsDeleted, cancellationToken);

        var activeCount = await _dbContext.ShippingMethods
            .AsNoTracking()
            .IgnoreQueryFilters()
            .CountAsync(method => method.TenantId == tenantId && method.IsActive && !method.IsDeleted, cancellationToken);

        var methods = await GetShippingMethodsAsync(
            tenantId,
            cartSubtotal,
            totalWeight,
            correlationId,
            warehouseId,
            cancellationToken);

        var status = ResolveShippingMethodsStatus(configuredCount, activeCount, methods.Count);

        return new ShippingMethodsListResponseDto
        {
            TenantId = tenantId,
            Status = status,
            ShippingMethods = methods,
            TotalCount = methods.Count,
            ConfiguredCount = configuredCount,
            ActiveCount = activeCount,
            ApplicableCount = methods.Count
        };
    }

    public async Task<ShippingMethodsListResponseDto> GetShippingMethodsAvailabilityForConfiguredWarehousesAsync(
        Guid tenantId,
        decimal cartSubtotal,
        decimal totalWeight,
        IReadOnlyCollection<CartItemDto> cartItems,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var configuredCount = await _dbContext.ShippingMethods
            .AsNoTracking()
            .IgnoreQueryFilters()
            .CountAsync(method => method.TenantId == tenantId && !method.IsDeleted, cancellationToken);

        var activeCount = await _dbContext.ShippingMethods
            .AsNoTracking()
            .IgnoreQueryFilters()
            .CountAsync(method => method.TenantId == tenantId && method.IsActive && !method.IsDeleted, cancellationToken);

        var methods = await _dbContext.ShippingMethods
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(method => method.TenantId == tenantId && method.IsActive && !method.IsDeleted)
            .OrderBy(method => method.SortOrder)
            .ThenBy(method => method.Name)
            .ToListAsync(cancellationToken);

        var configuredWarehouseIds = methods
            .Select(method => method.FulfillmentWarehouseId)
            .Where(id => id.HasValue && id.Value != Guid.Empty)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        var activeWarehouseIds = await _dbContext.Warehouses
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(warehouse => warehouse.TenantId == tenantId &&
                                configuredWarehouseIds.Contains(warehouse.Id) &&
                                warehouse.IsActive &&
                                !warehouse.IsDeleted)
            .Select(warehouse => warehouse.Id)
            .ToListAsync(cancellationToken);
        var activeWarehouses = activeWarehouseIds.ToHashSet();

        var stockReadyWarehouses = await GetWarehousesWithCartStockAsync(
            tenantId,
            activeWarehouseIds,
            cartItems,
            cancellationToken);

        var tenantCurrency = methods.Count == 0
            ? string.Empty
            : await ResolveTenantCurrencyAsync(tenantId, cancellationToken);

        var admitted = new List<ShippingMethodDocument>();
        foreach (var method in methods)
        {
            if (!method.FulfillmentWarehouseId.HasValue ||
                method.FulfillmentWarehouseId.Value == Guid.Empty ||
                !activeWarehouses.Contains(method.FulfillmentWarehouseId.Value) ||
                !stockReadyWarehouses.Contains(method.FulfillmentWarehouseId.Value))
            {
                continue;
            }

            var carrierCode = CarrierCodes.Normalize(method.CarrierCode);
            var serviceLevelReadiness = await _carrierIntegrationAuthority.GetServiceLevelReadinessAsync(
                tenantId,
                carrierCode,
                method.ServiceLevel,
                cancellationToken);
            if (!serviceLevelReadiness.CanExecute)
            {
                continue;
            }

            if (!CarrierCodes.IsManual(carrierCode))
            {
                var readiness = await _carrierIntegrationAuthority.GetIntegrationReadinessAsync(
                    tenantId,
                    carrierCode,
                    method.Id,
                    method.FulfillmentWarehouseId.Value,
                    CarrierIntegrationPurpose.StorefrontAdmission,
                    cancellationToken);
                if (!readiness.CanExecute)
                {
                    continue;
                }
            }

            admitted.Add(MapToDocument(method, tenantCurrency));
        }

        var shippingMethods = MapToDtos(admitted, tenantCurrency);
        ApplyCalculatedPrices(shippingMethods, cartSubtotal, totalWeight);

        return new ShippingMethodsListResponseDto
        {
            TenantId = tenantId,
            Status = ResolveShippingMethodsStatus(configuredCount, activeCount, shippingMethods.Count),
            ShippingMethods = shippingMethods,
            TotalCount = shippingMethods.Count,
            ConfiguredCount = configuredCount,
            ActiveCount = activeCount,
            ApplicableCount = shippingMethods.Count
        };
    }

    #region Helper Methods

    private async Task<List<ShippingMethodDocument>> ApplyConfiguredWarehouseAuthorityFilterAsync(
        Guid tenantId,
        Guid? warehouseId,
        List<ShippingMethodDocument> documents,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var configuredWarehouseIds = documents
            .Select(document => document.FulfillmentWarehouseId)
            .Where(id => id.HasValue && id.Value != Guid.Empty)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        if (configuredWarehouseIds.Count == 0)
        {
            return new List<ShippingMethodDocument>();
        }

        var activeWarehouseIds = await _dbContext.Warehouses
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(
                warehouse => warehouse.TenantId == tenantId &&
                             configuredWarehouseIds.Contains(warehouse.Id) &&
                             warehouse.IsActive &&
                             !warehouse.IsDeleted)
            .Select(warehouse => warehouse.Id)
            .ToListAsync(cancellationToken);
        var activeWarehouses = activeWarehouseIds.ToHashSet();

        if (warehouseId.HasValue && !activeWarehouses.Contains(warehouseId.Value))
        {
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
        }

        var admitted = new List<ShippingMethodDocument>(documents.Count);
        foreach (var document in documents)
        {
            if (!document.FulfillmentWarehouseId.HasValue ||
                document.FulfillmentWarehouseId.Value == Guid.Empty ||
                !activeWarehouses.Contains(document.FulfillmentWarehouseId.Value))
            {
                continue;
            }

            if (warehouseId.HasValue && document.FulfillmentWarehouseId.Value != warehouseId.Value)
            {
                continue;
            }

            var normalizedCarrierCode = CarrierCodes.Normalize(document.CarrierCode);
            var serviceLevelReadiness = await _carrierIntegrationAuthority.GetServiceLevelReadinessAsync(
                tenantId,
                normalizedCarrierCode,
                document.ServiceLevel,
                cancellationToken);
            if (!serviceLevelReadiness.CanExecute)
            {
                continue;
            }

            if (!CarrierCodes.IsManual(normalizedCarrierCode))
            {
                var readiness = await _carrierIntegrationAuthority.GetIntegrationReadinessAsync(
                    tenantId,
                    normalizedCarrierCode,
                    document.ShippingMethodId,
                    document.FulfillmentWarehouseId.Value,
                    CarrierIntegrationPurpose.StorefrontAdmission,
                    cancellationToken);
                if (!readiness.CanExecute)
                {
                    continue;
                }
            }

            admitted.Add(document);
        }

        return admitted;
    }

    private static string FormatDeliveryDays(int min, int max)
    {
        if (min == max)
        {
            return min == 1 ? "1 day" : $"{min} days";
        }
        return $"{min}-{max} days";
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
        IReadOnlyCollection<ShippingMethodDocument> cached,
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
                method.ShippingMethodId,
                correlationId);

            return false;
        }

        return true;
    }

    private static List<ShippingMethodDto> MapToDtos(
        IEnumerable<ShippingMethodDocument> documents,
        string currency)
    {
        return documents.Select(doc => new ShippingMethodDto
        {
            TenantId = doc.TenantId,
            Id = doc.ShippingMethodId,
            Name = doc.Name,
            NameAr = doc.NameAr,
            Description = doc.Description,
            BaseRate = doc.BaseRate,
            Price = 0m,
            Currency = currency,
            RatePerKg = doc.PerKgRate,
            FreeShippingThreshold = doc.FreeShippingThreshold,
            DeliveryDays = FormatDeliveryDays(doc.EstimatedDeliveryDaysMin, doc.EstimatedDeliveryDaysMax),
            EstimatedDays = doc.EstimatedDeliveryDaysMax,
            CarrierName = doc.CarrierName,
            CarrierCode = CarrierCodes.Normalize(doc.CarrierCode),
            ServiceLevel = doc.ServiceLevel,
            TrackingUrlTemplate = doc.TrackingUrlTemplate,
            SortOrder = doc.SortOrder
        }).ToList();
    }

    private static string ResolveShippingMethodsStatus(int configuredCount, int activeCount, int applicableCount)
    {
        if (applicableCount > 0)
        {
            return "AVAILABLE";
        }

        if (configuredCount == 0 || activeCount == 0)
        {
            return "EMPTY";
        }

        return "NOT_APPLICABLE";
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

    private async Task<HashSet<Guid>> GetWarehousesWithCartStockAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> warehouseIds,
        IReadOnlyCollection<CartItemDto> cartItems,
        CancellationToken cancellationToken)
    {
        var lines = cartItems
            .Where(item => item.Quantity > 0 && item.VariantId.HasValue && item.VariantId.Value != Guid.Empty)
            .ToList();

        if (warehouseIds.Count == 0)
        {
            return new HashSet<Guid>();
        }

        if (lines.Count == 0)
        {
            return warehouseIds.ToHashSet();
        }

        var variantIds = lines.Select(item => item.VariantId!.Value).Distinct().ToList();
        var variants = await _dbContext.ProductVariants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .WhereInventoryReservableLineage(tenantId)
            .Where(variant => variantIds.Contains(variant.Id))
            .Select(variant => new
            {
                variant.Id,
                variant.ProductId,
                variant.Product.TrackStock,
                variant.Product.AllowBackorder
            })
            .ToListAsync(cancellationToken);
        var variantById = variants.ToDictionary(item => item.Id);

        if (lines.Any(line =>
                !variantById.TryGetValue(line.VariantId!.Value, out var variant) ||
                variant.ProductId != line.ProductId))
        {
            return new HashSet<Guid>();
        }

        var requiredQuantities = lines
            .Where(line => variantById.TryGetValue(line.VariantId!.Value, out var variant) &&
                           variant.ProductId == line.ProductId &&
                           variant.TrackStock &&
                           !variant.AllowBackorder)
            .GroupBy(line => line.VariantId!.Value)
            .Select(group => new { VariantId = group.Key, Quantity = group.Sum(item => item.Quantity) })
            .ToList();

        if (requiredQuantities.Count == 0)
        {
            return warehouseIds.ToHashSet();
        }

        var requiredVariantIds = requiredQuantities.Select(requirement => requirement.VariantId).ToList();
        var stock = await _dbContext.ProductVariantStocks
            .IgnoreQueryFilters()
            .AsNoTracking()
            .WhereCurrentInventoryStockOwner(tenantId)
            .Where(item => warehouseIds.Contains(item.WarehouseId) &&
                           requiredVariantIds.Contains(item.VariantId))
            .GroupBy(item => new { item.WarehouseId, item.VariantId })
            .Select(group => new
            {
                group.Key.WarehouseId,
                group.Key.VariantId,
                Available = group.Sum(item => item.Quantity - item.ReservedQuantity)
            })
            .ToListAsync(cancellationToken);
        var stockByWarehouseVariant = stock.ToDictionary(
            item => (item.WarehouseId, item.VariantId),
            item => item.Available);

        return warehouseIds
            .Where(warehouseId => requiredQuantities.All(requirement =>
                stockByWarehouseVariant.TryGetValue((warehouseId, requirement.VariantId), out var available) &&
                available >= requirement.Quantity))
            .ToHashSet();
    }

    private async Task<string> ResolveTenantCurrencyAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var tenantCurrency = await _dbContext.Tenants
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(tenant => tenant.Id == tenantId)
            .Select(tenant => tenant.Currency)
            .FirstOrDefaultAsync(cancellationToken);

        return NormalizeCurrency(tenantCurrency) ?? string.Empty;
    }

    private static ShippingMethodDocument MapToDocument(ShippingMethod method, string currency) => new()
    {
        TenantId = method.TenantId,
        ShippingMethodId = method.Id,
        Name = method.Name,
        NameAr = method.NameAr,
        Description = method.Description,
        BaseRate = method.BaseRate,
        PerKgRate = method.PerKgRate,
        FreeShippingThreshold = method.FreeShippingThreshold,
        Currency = currency,
        EstimatedDeliveryDaysMin = method.EstimatedDeliveryDaysMin,
        EstimatedDeliveryDaysMax = method.EstimatedDeliveryDaysMax,
        IsActive = method.IsActive,
        IsDeleted = method.IsDeleted,
        SortOrder = method.SortOrder,
        CarrierName = method.CarrierName,
        CarrierCode = method.CarrierCode,
        TrackingUrlTemplate = method.TrackingUrlTemplate,
        AvailableCountries = method.AvailableCountries,
        FulfillmentWarehouseId = method.FulfillmentWarehouseId,
        ServiceLevel = method.ServiceLevel,
        SyncedAtUtc = method.UpdatedAt ?? method.CreatedAt
    };

    #endregion
}
