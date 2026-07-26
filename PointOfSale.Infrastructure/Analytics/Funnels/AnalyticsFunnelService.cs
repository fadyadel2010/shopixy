using Microsoft.Extensions.Logging;
using PointOfSale.Core.Analytics;
using PointOfSale.Infrastructure.Analytics.Alerts;
using PointOfSale.Infrastructure.MongoDB.Services;
using PointOfSale.Infrastructure.Redis.Services;

namespace PointOfSale.Infrastructure.Analytics.Funnels;

/// <summary>
/// Implementation of IAnalyticsFunnelService.
/// 
/// Computes conversion funnels from Redis counters only.
/// 
/// DEFAULT FUNNELS:
/// - Purchase Funnel: PageView → ProductView → AddToCart → BeginCheckout → PurchaseCompleted
/// - Discovery Funnel: PageView → ProductImpression → ProductView → AddToCart
/// - Checkout Funnel: BeginCheckout → PurchaseCompleted
/// </summary>
public class AnalyticsFunnelService : IAnalyticsFunnelService
{
    private readonly IAnalyticsMetricReader _metricReader;
    private readonly IAnalyticsHotStoreService _hotStore;
    private readonly IProductReadRepository _productRepository;
    private readonly ILogger<AnalyticsFunnelService> _logger;

    // Pre-defined system funnels
    private static readonly IReadOnlyList<AnalyticsFunnelDefinition> SystemFunnels = new List<AnalyticsFunnelDefinition>
    {
        // Purchase Funnel - Full journey
        new AnalyticsFunnelDefinition
        {
            FunnelKey = "purchase_funnel",
            Name = "Purchase Funnel",
            Description = "Track the complete journey from page view to purchase completion",
            IsSystem = true,
            Steps = new List<AnalyticsFunnelStep>
            {
                AnalyticsFunnelStep.Create(1, AnalyticsEventType.PageView, "Page View"),
                AnalyticsFunnelStep.Create(2, AnalyticsEventType.ProductView, "View Product"),
                AnalyticsFunnelStep.Create(3, AnalyticsEventType.AddToCart, "Add to Cart"),
                AnalyticsFunnelStep.Create(4, AnalyticsEventType.BeginCheckout, "Begin Checkout"),
                AnalyticsFunnelStep.Create(5, AnalyticsEventType.PurchaseCompleted, "Purchase Completed")
            }
        },

        // Discovery Funnel - Product discovery
        new AnalyticsFunnelDefinition
        {
            FunnelKey = "discovery_funnel",
            Name = "Discovery Funnel",
            Description = "Track how users discover and engage with products",
            IsSystem = true,
            Steps = new List<AnalyticsFunnelStep>
            {
                AnalyticsFunnelStep.Create(1, AnalyticsEventType.PageView, "Page View"),
                AnalyticsFunnelStep.Create(2, AnalyticsEventType.ProductImpression, "Product Impression"),
                AnalyticsFunnelStep.Create(3, AnalyticsEventType.ProductView, "View Product"),
                AnalyticsFunnelStep.Create(4, AnalyticsEventType.AddToCart, "Add to Cart")
            }
        },

        // Checkout Funnel - Checkout completion
        new AnalyticsFunnelDefinition
        {
            FunnelKey = "checkout_funnel",
            Name = "Checkout Funnel",
            Description = "Track checkout completion rate",
            IsSystem = true,
            Steps = new List<AnalyticsFunnelStep>
            {
                AnalyticsFunnelStep.Create(1, AnalyticsEventType.BeginCheckout, "Begin Checkout"),
                AnalyticsFunnelStep.Create(2, AnalyticsEventType.PurchaseCompleted, "Purchase Completed")
            }
        },

        // Add to Cart Funnel - Product engagement
        new AnalyticsFunnelDefinition
        {
            FunnelKey = "cart_funnel",
            Name = "Cart Funnel",
            Description = "Track product views to cart additions",
            IsSystem = true,
            Steps = new List<AnalyticsFunnelStep>
            {
                AnalyticsFunnelStep.Create(1, AnalyticsEventType.ProductView, "View Product"),
                AnalyticsFunnelStep.Create(2, AnalyticsEventType.AddToCart, "Add to Cart"),
                AnalyticsFunnelStep.Create(3, AnalyticsEventType.BeginCheckout, "Begin Checkout")
            }
        },

        // Search to Purchase Funnel
        new AnalyticsFunnelDefinition
        {
            FunnelKey = "search_funnel",
            Name = "Search Funnel",
            Description = "Track search to purchase journey",
            IsSystem = true,
            Steps = new List<AnalyticsFunnelStep>
            {
                AnalyticsFunnelStep.Create(1, AnalyticsEventType.SearchPerformed, "Search"),
                AnalyticsFunnelStep.Create(2, AnalyticsEventType.ProductView, "View Product"),
                AnalyticsFunnelStep.Create(3, AnalyticsEventType.AddToCart, "Add to Cart"),
                AnalyticsFunnelStep.Create(4, AnalyticsEventType.PurchaseCompleted, "Purchase")
            }
        }
    };

    public AnalyticsFunnelService(
        IAnalyticsMetricReader metricReader,
        IAnalyticsHotStoreService hotStore,
        IProductReadRepository productRepository,
        ILogger<AnalyticsFunnelService> logger)
    {
        _metricReader = metricReader;
        _hotStore = hotStore;
        _productRepository = productRepository;
        _logger = logger;
    }

    public IReadOnlyList<AnalyticsFunnelDefinition> GetFunnelDefinitions()
    {
        return SystemFunnels;
    }

    public AnalyticsFunnelDefinition? GetFunnelDefinition(string funnelKey)
    {
        return SystemFunnels.FirstOrDefault(f => 
            f.FunnelKey.Equals(funnelKey, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<AnalyticsFunnelResult?> ComputeFunnelAsync(
        Guid tenantId,
        string funnelKey,
        int windowMinutes = 60,
        CancellationToken cancellationToken = default)
    {
        var definition = GetFunnelDefinition(funnelKey);
        if (definition == null)
        {
            _logger.LogWarning(
                "[Funnels] Unknown funnel key | TenantId: {TenantId} | Key: {Key}",
                tenantId, funnelKey);
            return null;
        }

        return await ComputeCustomFunnelAsync(tenantId, definition, windowMinutes, cancellationToken);
    }

    public async Task<List<AnalyticsFunnelResult>> ComputeAllFunnelsAsync(
        Guid tenantId,
        int windowMinutes = 60,
        CancellationToken cancellationToken = default)
    {
        var results = new List<AnalyticsFunnelResult>();

        foreach (var definition in SystemFunnels)
        {
            var result = await ComputeCustomFunnelAsync(tenantId, definition, windowMinutes, cancellationToken);
            results.Add(result);
        }

        return results;
    }

    public async Task<AnalyticsFunnelResult> ComputeCustomFunnelAsync(
        Guid tenantId,
        AnalyticsFunnelDefinition definition,
        int windowMinutes = 60,
        CancellationToken cancellationToken = default)
    {
        var result = new AnalyticsFunnelResult
        {
            FunnelKey = definition.FunnelKey,
            FunnelName = definition.Name,
            WindowMinutes = windowMinutes,
            TenantId = tenantId,
            ComputedAtUtc = DateTime.UtcNow
        };

        if (!definition.IsValid())
        {
            _logger.LogWarning(
                "[Funnels] Invalid funnel definition | TenantId: {TenantId} | Key: {Key}",
                tenantId, definition.FunnelKey);
            return result;
        }

        try
        {
            // Get counts for each step from Redis
            var stepCounts = new Dictionary<AnalyticsEventType, long>();

            foreach (var step in definition.Steps)
            {
                var metric = EventTypeToMetric(step.EventType);
                var count = await _metricReader.GetCounterValueAsync(
                    tenantId,
                    step.EventType.ToString().ToLowerInvariant(),
                    windowMinutes,
                    cancellationToken);

                stepCounts[step.EventType] = count;
            }

            // Compute step results
            long? previousCount = null;
            long firstCount = 0;

            foreach (var step in definition.Steps.OrderBy(s => s.Order))
            {
                var count = stepCounts.GetValueOrDefault(step.EventType, 0);

                if (step.Order == 1)
                {
                    firstCount = count;
                }

                var stepResult = new AnalyticsFunnelStepResult
                {
                    Order = step.Order,
                    Name = step.Name,
                    EventType = step.EventType,
                    Count = count
                };

                // Calculate conversion and drop-off rates
                if (previousCount.HasValue && previousCount.Value > 0)
                {
                    stepResult.ConversionFromPrevious = Math.Round(
                        (decimal)count / previousCount.Value * 100, 2);
                    stepResult.DropOffFromPrevious = Math.Round(
                        100 - stepResult.ConversionFromPrevious, 2);
                    stepResult.DropOffCount = previousCount.Value - count;

                    // Ensure non-negative (edge case where counts are inconsistent)
                    if (stepResult.DropOffCount < 0) stepResult.DropOffCount = 0;
                }
                else if (step.Order == 1)
                {
                    // First step - 100% entry
                    stepResult.ConversionFromPrevious = 100;
                    stepResult.DropOffFromPrevious = 0;
                    stepResult.DropOffCount = 0;
                }

                // Conversion from entry (first step)
                if (firstCount > 0)
                {
                    stepResult.ConversionFromEntry = Math.Round(
                        (decimal)count / firstCount * 100, 2);
                }

                result.Steps.Add(stepResult);
                previousCount = count;

                // Populate contextual data (Top Pages / Top Products)
                // Limit to 5 for the funnel overview to keep it lightweight
                if (step.EventType == AnalyticsEventType.PageView)
                {
                    stepResult.TopPages = await GetTopPagesAsync(tenantId, windowMinutes, 5, cancellationToken);
                }
                else if (step.EventType == AnalyticsEventType.ProductView)
                {
                    stepResult.TopProducts = await GetTopViewedProductsAsync(tenantId, windowMinutes, 5, cancellationToken);
                }
                else if (step.EventType == AnalyticsEventType.AddToCart)
                {
                    stepResult.TopProducts = await GetTopAddedToCartProductsAsync(tenantId, windowMinutes, 5, cancellationToken);
                }
            }

            // Overall conversion rate (first to last)
            if (firstCount > 0 && result.Steps.Count > 0)
            {
                var lastCount = result.Steps.Last().Count;
                result.OverallConversionRate = Math.Round(
                    (decimal)lastCount / firstCount * 100, 2);
            }

            _logger.LogDebug(
                "[Funnels] Computed | TenantId: {TenantId} | Funnel: {Funnel} | Window: {Window}min | Entry: {Entry} | Completion: {Completion} | Rate: {Rate}%",
                tenantId,
                definition.FunnelKey,
                windowMinutes,
                result.EntryCount,
                result.CompletionCount,
                result.OverallConversionRate);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[Funnels] Computation failed | TenantId: {TenantId} | Funnel: {Funnel}",
                tenantId, definition.FunnelKey);
        }

        return result;
    }

    public async Task<List<TopPageStats>> GetTopPagesAsync(
        Guid tenantId,
        int windowMinutes = 60,
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        return await _hotStore.GetTopPagesAsync(tenantId, windowMinutes, limit, cancellationToken);
    }

    public async Task<List<TopProductStats>> GetTopViewedProductsAsync(
        Guid tenantId,
        int windowMinutes = 60,
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        var products = await _hotStore.GetTopViewedProductsAsync(tenantId, windowMinutes, limit, cancellationToken);
        return await HydrateProductNamesAsync(tenantId, products, cancellationToken);
    }

    public async Task<List<TopProductStats>> GetTopAddedToCartProductsAsync(
        Guid tenantId,
        int windowMinutes = 60,
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        var products = await _hotStore.GetTopAddedToCartProductsAsync(tenantId, windowMinutes, limit, cancellationToken);
        return await HydrateProductNamesAsync(tenantId, products, cancellationToken);
    }

    /// <summary>
    /// Hydrates product names from MongoDB for top product stats.
    /// </summary>
    private async Task<List<TopProductStats>> HydrateProductNamesAsync(
        Guid tenantId,
        List<TopProductStats> products,
        CancellationToken cancellationToken)
    {
        if (products.Count == 0) return products;

        try
        {
            var productIds = products.Select(p => p.ProductId).ToList();
            var productDocs = await _productRepository.GetByProductIdsAsync(tenantId, productIds, false, cancellationToken);
            var productLookup = productDocs.ToDictionary(p => p.ProductId, p => p.Name);

            foreach (var product in products)
            {
                if (productLookup.TryGetValue(product.ProductId, out var name))
                {
                    product.ProductName = name;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[Funnels] Failed to hydrate product names | TenantId: {TenantId}",
                tenantId);
            // Return products with IDs only - graceful degradation
        }

        return products;
    }

    private static AnalyticsMetric EventTypeToMetric(AnalyticsEventType eventType)
    {
        return eventType switch
        {
            AnalyticsEventType.PageView => AnalyticsMetric.PageViews,
            AnalyticsEventType.ProductView => AnalyticsMetric.ProductViews,
            AnalyticsEventType.AddToCart => AnalyticsMetric.AddToCart,
            AnalyticsEventType.BeginCheckout => AnalyticsMetric.BeginCheckout,
            AnalyticsEventType.PurchaseCompleted => AnalyticsMetric.PurchaseCompleted,
            AnalyticsEventType.PaymentFailed => AnalyticsMetric.PaymentFailed,
            _ => AnalyticsMetric.PageViews
        };
    }
}
