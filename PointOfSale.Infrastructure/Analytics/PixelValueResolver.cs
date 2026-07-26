using Microsoft.Extensions.Logging;
using PointOfSale.Core.Analytics;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Infrastructure.Analytics;

/// <summary>
/// Implementation of IPixelValueResolver.
/// Resolves the value to send to pixel providers based on tenant configuration.
/// 
/// ISOLATION GUARANTEE:
/// This class encapsulates the override logic. Dispatchers just call this and use the result.
/// </summary>
public class PixelValueResolver : IPixelValueResolver
{
    private readonly IPixelTrackingConfigService _configService;
    private readonly IProductPixelOverrideService _productOverrideService;
    private readonly ILogger<PixelValueResolver> _logger;

    public PixelValueResolver(
        IPixelTrackingConfigService configService,
        IProductPixelOverrideService productOverrideService,
        ILogger<PixelValueResolver> logger)
    {
        _configService = configService;
        _productOverrideService = productOverrideService;
        _logger = logger;
    }

    public async Task<decimal> ResolvePixelValueAsync(
        Guid tenantId, 
        decimal actualOrderTotal, 
        PixelProvider provider,
        CancellationToken ct = default)
    {
        try
        {
            var config = await _configService.GetConfigAsync(tenantId, ct);

            // 1. If global disabled, return actual
            if (!config.IsEnabled)
            {
                return actualOrderTotal;
            }

            // 2. If provider not in AppliesTo, return actual
            if (config.AppliesTo == null || !config.AppliesTo.Contains(provider))
            {
                return actualOrderTotal;
            }

            // 3. Apply strategy
            decimal resolvedValue = actualOrderTotal;

            switch (config.Strategy)
            {
                case PixelOverrideStrategy.Percentage:
                    // Value is percentage (e.g. 70 for 70%)
                    // Formula: Actual * (Value / 100)
                    resolvedValue = actualOrderTotal * (config.Value / 100m);
                    break;

                case PixelOverrideStrategy.Fixed:
                    // Value is fixed amount
                    resolvedValue = config.Value;
                    break;

                default:
                    // Fallback
                    resolvedValue = actualOrderTotal;
                    break;
            }

            // Round to 2 decimal places usually required by pixels
            resolvedValue = Math.Round(resolvedValue, 2);

            // Log if overridden (Debug only to avoid noise, but maybe Info for audit?)
            // Keeping it Trace/Debug as this will happen for every purchase
            if (resolvedValue != actualOrderTotal)
            {
                _logger.LogDebug(
                    "Pixel Override Applied | Tenant: {TenantId} | Provider: {Provider} | Actual: {Actual} | Resolved: {Resolved} | Strategy: {Strategy}",
                    tenantId, provider, actualOrderTotal, resolvedValue, config.Strategy);
            }

            return resolvedValue;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resolving pixel value for Tenant {TenantId}. Returning actual value.", tenantId);
            return actualOrderTotal; // Fail safe: send actual value
        }
    }

    public async Task<decimal> ResolvePixelValueAsync(
        Guid tenantId,
        IReadOnlyCollection<OrderItemPixelInfo> orderItems,
        PixelProvider provider,
        CancellationToken ct = default)
    {
        try
        {
            // 1. Get Product Overrides & Global Config
            var productOverrides = await _productOverrideService.GetOverridesForTenantAsync(tenantId, ct);
            var globalConfig = await _configService.GetConfigAsync(tenantId, ct);

            decimal totalPixelValue = 0;
            bool anyProductOverrideApplied = false;

            // 2. Iterate items to apply specific overrides
            foreach (var item in orderItems)
            {
                if (productOverrides.TryGetValue(item.ProductId, out var productOverride) 
                    && productOverride.IsEnabled 
                    && AppliesToProvider(productOverride, provider))
                {
                    // PRODUCT OVERRIDE WINS
                    totalPixelValue += ApplyOverride(productOverride, item.LineItemTotal);
                    anyProductOverrideApplied = true;
                }
                else if (!anyProductOverrideApplied && globalConfig.IsEnabled 
                         && globalConfig.AppliesTo != null 
                         && globalConfig.AppliesTo.Contains(provider))
                {
                    // If NO product override exists for ANY item up to now, 
                    // we treat this item as potential global override.
                    // BUT: The moment we find a product override, we must enforce the rule:
                    // "If mixed, non-overridden items use ACTUAL price."
                    
                    // However, iterating like this makes it hard to know if *future* items have overrides.
                    // The algorithm in implementation plan says:
                    // "If some items have product override, some don't: Overridden items: use product override, Non-overridden: use ACTUAL"
                    
                    // So we should assume "Actual" for now stash it, and at the end checks if we should fallback to global.
                    // Actually, simpler logic:
                    // Accumulate "resolved value" for each item.
                    // For items without product override, use actual.
                    // IF (and only if) `anyProductOverrideApplied` remains false for ALL items, 
                    // THEN we call the global resolver on the total sum.
                    
                    totalPixelValue += item.LineItemTotal;
                }
                else
                {
                    // No product override, and we already know (or will know) that product overrides exist
                    // OR global is disabled.
                    // In any case, add actual.
                    totalPixelValue += item.LineItemTotal;
                }
            }

            // 3. Check if we should fallback to Global (Phase M1)
            // Rule: Global applies ONLY if NO product overrides were applied.
            if (!anyProductOverrideApplied)
            {
                var orderTotal = orderItems.Sum(i => i.LineItemTotal);
                return await ResolvePixelValueAsync(tenantId, orderTotal, provider, ct);
            }

            return Math.Round(totalPixelValue, 2);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resolving multi-item pixel value for Tenant {TenantId}", tenantId);
            return orderItems.Sum(i => i.LineItemTotal); // Fail safe
        }
    }

    private bool AppliesToProvider(ProductPixelOverride overrideEntity, PixelProvider provider)
    {
        return provider switch
        {
            PixelProvider.Google => overrideEntity.ApplyToGoogle,
            PixelProvider.Meta => overrideEntity.ApplyToMeta,
            PixelProvider.TikTok => overrideEntity.ApplyToTikTok,
            _ => false
        };
    }

    private decimal ApplyOverride(ProductPixelOverride overrideEntity, decimal lineTotal)
    {
        return overrideEntity.Strategy switch
        {
            PixelOverrideStrategy.Percentage => lineTotal * (overrideEntity.Value / 100m),
            PixelOverrideStrategy.Fixed => overrideEntity.Value, // Fixed value per line item? Or fixed value usually means "Total value"? 
            // For product override, "Fixed" usually means "This product is reported as $X".
            // If Quantity > 1, should it be X * Qty?
            // The PixelOverrideStrategy doesn't specify per-unit or per-line.
            // Assumption: Value is per unit if it's product price override. 
            // But here we get LineItemTotal.
            // If strategy is "Fixed = 10", does it mean the whole line is 10? Or each unit is 10?
            // For now, let's assume "Fixed" means "The value of this Line Item is X".
            // Why? Because usually fixed pixel events are for "Lead = $10".
            // But for e-commerce, Percentage is 99% of use cases.
            // If user sets Fixed $10 on a $100 product, they expect to report $10.
            // If they buy 2, it's a $200 line. Should it report $10 or $20?
            // "Fixed" usually implies a static value for the event.
            // However, this is Per-Product.
            // Let's stick to: Value is the total for this line occurrence override.
            // CAUTION: Implementation plan said "Value input".
            // Let's stick to exact value replacement for the line.
            _ => lineTotal
        };
    }
}
