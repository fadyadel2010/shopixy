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
    private readonly IShippingMethodCacheService _cache;
    private readonly IShippingCalculationService _shippingCalculationService;
    private readonly CommerceReliabilityTelemetry _reliabilityTelemetry;
    private readonly ILogger<ShippingMethodService> _logger;

    public ShippingMethodService(
        IShippingMethodReadRepository repository,
        ApplicationDbContext dbContext,
        IShippingMethodCacheService cache,
        IShippingCalculationService shippingCalculationService,
        CommerceReliabilityTelemetry reliabilityTelemetry,
        ILogger<ShippingMethodService> logger)
    {
        _repository = repository;
        _dbContext = dbContext;
        _cache = cache;
        _shippingCalculationService = shippingCalculationService;
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
                var cachedMethods = MapToDtos(documents, cachedCurrency);
                ApplyCalculatedPrices(cachedMethods, cartSubtotal, totalWeight);

                _logger.LogDebug(
                    "Shipping methods loaded through cache owner | TenantId: {TenantId} | Count: {Count} | CorrelationId: {CorrelationId}",
                    tenantId, cachedMethods.Count, correlationId);
                return cachedMethods;
            }

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
        else
        {
            documents = await _repository.GetActiveShippingMethodsAsync(tenantId, cancellationToken);
            documents = await ApplyWarehouseAssignmentFilterAsync(
                tenantId,
                warehouseId,
                documents,
                correlationId,
                cancellationToken);
        }

        var projectedCurrency = ResolveProjectedCurrency(tenantId, documents, correlationId);

        var shippingMethods = MapToDtos(documents, projectedCurrency);

        ApplyCalculatedPrices(shippingMethods, cartSubtotal, totalWeight);

        _logger.LogInformation(
            "Shipping methods loaded from MongoDB | TenantId: {TenantId} | ResultCount: {ResultCount} | CacheHit: false | CorrelationId: {CorrelationId}",
            tenantId, shippingMethods.Count, correlationId);

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
            SortOrder = doc.SortOrder
        }).ToList();
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
