using Microsoft.Extensions.Caching.Memory;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.Services.Pricing;
using PointOfSale.Core.Constants;
using PointOfSale.Core.Entities;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Shipping;
using StatusCodes = Microsoft.AspNetCore.Http.StatusCodes;

namespace PointOfSale.API.Services.CommerceHub;

public interface ICommerceHubShippingService
{
    Task<CommerceHubShippingCalculationResult> CalculateAsync(
        IReadOnlyCollection<CommerceHubShippingStoreRequest> stores,
        IReadOnlyDictionary<Guid, Guid>? requestedSelections = null,
        CancellationToken cancellationToken = default,
        bool bypassCache = false);
}

public sealed class CommerceHubShippingCalculationResult
{
    public decimal TotalShipping { get; set; }
    public List<CommerceHubShippingStoreSelection> StoreSelections { get; set; } = new();
}

public sealed class CommerceHubShippingStoreSelection
{
    public Guid StoreId { get; init; }
    public Guid ShippingOptionId { get; init; }
    public decimal ShippingPrice { get; init; }
    public string ShippingName { get; init; } = string.Empty;
    public decimal? FreeShippingThreshold { get; init; }
}

public sealed class CommerceHubShippingStoreRequest
{
    public Guid StoreId { get; init; }
    public decimal Subtotal { get; init; }
    public decimal TotalWeightKg { get; init; }
}

public class CommerceHubShippingService : ICommerceHubShippingService
{
    private const int CurrencyScale = 2;
    private static readonly TimeSpan ShippingOptionsCacheTtl = TimeSpan.FromMinutes(5);
    private readonly ApplicationDbContext _dbContext;
    private readonly IMemoryCache _cache;
    private readonly IShippingCalculationService _shippingCalculationService;
    private readonly ICarrierIntegrationAuthority _carrierIntegrationAuthority;
    private readonly ILogger<CommerceHubShippingService> _logger;

    public CommerceHubShippingService(
        ApplicationDbContext dbContext,
        IMemoryCache cache,
        IShippingCalculationService shippingCalculationService,
        ILogger<CommerceHubShippingService> logger,
        ICarrierIntegrationAuthority? carrierIntegrationAuthority = null)
    {
        _dbContext = dbContext;
        _cache = cache;
        _shippingCalculationService = shippingCalculationService;
        _carrierIntegrationAuthority = carrierIntegrationAuthority ?? NullCarrierIntegrationAuthority.Instance;
        _logger = logger;
    }

    public async Task<CommerceHubShippingCalculationResult> CalculateAsync(
        IReadOnlyCollection<CommerceHubShippingStoreRequest> stores,
        IReadOnlyDictionary<Guid, Guid>? requestedSelections = null,
        CancellationToken cancellationToken = default,
        bool bypassCache = false)
    {
        var distinctStores = stores
            .GroupBy(store => store.StoreId)
            .Select(group => new CommerceHubShippingStoreRequest
            {
                StoreId = group.Key,
                Subtotal = group.Sum(item => item.Subtotal),
                TotalWeightKg = group.Sum(item => item.TotalWeightKg)
            })
            .ToList();

        if (distinctStores.Count == 0)
        {
            return new CommerceHubShippingCalculationResult();
        }
        var result = new CommerceHubShippingCalculationResult();

        foreach (var store in distinctStores)
        {
            var storeMethods = await GetStoreShippingOptionsAsync(store.StoreId, cancellationToken, bypassCache);
            if (storeMethods.Count == 0)
            {
                throw new CommerceHubCheckoutException(
                    "STORE_HAS_NO_SHIPPING",
                    $"Store {store.StoreId} does not have any active shipping options.",
                    StatusCodes.Status400BadRequest,
                    new { storeId = store.StoreId });
            }

            var evaluatedOptions = storeMethods
                .Select(method =>
                {
                    var isEligible = _shippingCalculationService.TryCalculate(
                        method.BaseRate,
                        method.PerKgRate ?? 0m,
                        method.FreeShippingThreshold,
                        store.Subtotal,
                        out var effectivePrice,
                        store.TotalWeightKg,
                        rejectThresholdOnlyZeroRateBeforeThreshold: true);

                    return new
                    {
                        Method = method,
                        IsEligible = isEligible,
                        EffectivePrice = effectivePrice
                    };
                })
                .Where(candidate => candidate.IsEligible)
                .ToList();

            if (evaluatedOptions.Count == 0)
            {
                throw new CommerceHubCheckoutException(
                    "STORE_HAS_NO_SHIPPING",
                    $"Store {store.StoreId} does not have any valid shipping options for the current subtotal and weight.",
                    StatusCodes.Status400BadRequest,
                    new { storeId = store.StoreId, subtotal = RoundCurrency(store.Subtotal), totalWeightKg = store.TotalWeightKg });
            }

            var cheapest = evaluatedOptions
                .OrderBy(candidate => candidate.EffectivePrice)
                .ThenBy(candidate => candidate.Method.SortOrder)
                .ThenBy(candidate => candidate.Method.Name, StringComparer.OrdinalIgnoreCase)
                .First();

            var selectedShippingPrice = RoundCurrency(cheapest.EffectivePrice);

            if (requestedSelections != null &&
                requestedSelections.TryGetValue(store.StoreId, out var requestedShippingOptionId) &&
                requestedShippingOptionId != Guid.Empty)
            {
                var requestedMethod = storeMethods.FirstOrDefault(method => method.Id == requestedShippingOptionId);
                if (requestedMethod == null)
                {
                    _logger.LogInformation(
                        "CommerceHub shipping selection refreshed because the requested option is no longer active | StoreId: {StoreId} | RequestedShippingOptionId: {RequestedShippingOptionId} | SelectedShippingOptionId: {SelectedShippingOptionId}",
                        store.StoreId,
                        requestedShippingOptionId,
                        cheapest.Method.Id);
                }
                else
                {
                    var requestedIsEligible = _shippingCalculationService.TryCalculate(
                        requestedMethod.BaseRate,
                        requestedMethod.PerKgRate ?? 0m,
                        requestedMethod.FreeShippingThreshold,
                        store.Subtotal,
                        out var requestedEffectivePrice,
                        store.TotalWeightKg,
                        rejectThresholdOnlyZeroRateBeforeThreshold: true);
                    var requestedPrice = RoundCurrency(requestedEffectivePrice);
                    if (!requestedIsEligible || requestedShippingOptionId != cheapest.Method.Id || requestedPrice != selectedShippingPrice)
                    {
                        _logger.LogInformation(
                            "CommerceHub shipping selection refreshed | StoreId: {StoreId} | RequestedShippingOptionId: {RequestedShippingOptionId} | RequestedEligible: {RequestedEligible} | RequestedPrice: {RequestedPrice} | SelectedShippingOptionId: {SelectedShippingOptionId} | SelectedPrice: {SelectedPrice}",
                            store.StoreId,
                            requestedShippingOptionId,
                            requestedIsEligible,
                            requestedPrice,
                            cheapest.Method.Id,
                            selectedShippingPrice);
                    }
                }
            }

            result.StoreSelections.Add(new CommerceHubShippingStoreSelection
            {
                StoreId = store.StoreId,
                ShippingOptionId = cheapest.Method.Id,
                ShippingPrice = selectedShippingPrice,
                ShippingName = cheapest.Method.Name,
                FreeShippingThreshold = cheapest.Method.FreeShippingThreshold
            });

            result.TotalShipping = RoundCurrency(result.TotalShipping + selectedShippingPrice);
        }

        result.TotalShipping = RoundCurrency(result.TotalShipping);
        return result;
    }

    private async Task<IReadOnlyList<ShippingMethodCacheEntry>> GetStoreShippingOptionsAsync(
        Guid storeId,
        CancellationToken cancellationToken,
        bool bypassCache)
    {
        var cacheKey = GetShippingOptionsCacheKey(storeId);
        if (bypassCache)
        {
            var freshMethods = await LoadStoreShippingOptionsAsync(storeId, cancellationToken);
            _cache.Set(cacheKey, freshMethods, ShippingOptionsCacheTtl);
            return freshMethods;
        }

        var cachedMethods = await _cache.GetOrCreateAsync(
            cacheKey,
            async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = ShippingOptionsCacheTtl;
                return await LoadStoreShippingOptionsAsync(storeId, cancellationToken);
            });

        return cachedMethods ?? Array.Empty<ShippingMethodCacheEntry>();
    }

    private async Task<IReadOnlyList<ShippingMethodCacheEntry>> LoadStoreShippingOptionsAsync(
        Guid storeId,
        CancellationToken cancellationToken)
    {
        var shippingMethods = await _dbContext.ShippingMethods
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(method => !method.IsDeleted && method.IsActive && method.TenantId == storeId)
            .OrderBy(method => method.SortOrder)
            .ThenBy(method => method.Name)
            .ToListAsync(cancellationToken);

        var admitted = new List<ShippingMethodCacheEntry>(shippingMethods.Count);
        foreach (var method in shippingMethods)
        {
            var carrierCode = CarrierCodes.Normalize(method.CarrierCode);
            if (!CarrierCodes.IsManual(carrierCode))
            {
                var readiness = await _carrierIntegrationAuthority.GetIntegrationReadinessAsync(
                    method.TenantId,
                    carrierCode,
                    method.Id,
                    method.FulfillmentWarehouseId,
                    CarrierIntegrationPurpose.CommerceHubAdmission,
                    cancellationToken);
                if (!readiness.CanExecute)
                {
                    continue;
                }
            }

            admitted.Add(new ShippingMethodCacheEntry(
                method.Id,
                method.Name,
                method.BaseRate,
                method.PerKgRate,
                method.FreeShippingThreshold,
                method.SortOrder));
        }

        return admitted;
    }

    private static string GetShippingOptionsCacheKey(Guid storeId)
    {
        return $"shipping_options:{storeId:N}";
    }

    private static decimal RoundCurrency(decimal value)
    {
        return Math.Round(value, CurrencyScale, MidpointRounding.AwayFromZero);
    }

    private sealed record ShippingMethodCacheEntry(
        Guid Id,
        string Name,
        decimal BaseRate,
        decimal? PerKgRate,
        decimal? FreeShippingThreshold,
        int SortOrder);
}
