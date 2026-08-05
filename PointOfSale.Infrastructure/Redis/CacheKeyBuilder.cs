namespace PointOfSale.Infrastructure.Redis;

/// <summary>
/// Centralized cache key builder for consistent, tenant-isolated cache keys.
/// 
/// Key Convention: {instance}:tenant:{tenantId}:{entity}:{identifier}
/// 
/// Examples:
/// - pos:tenant:abc123:product:prod456
/// - pos:tenant:abc123:products:all
/// 
/// IMPORTANT:
/// - All keys MUST include TenantId for multi-tenant isolation
/// - Never create keys without tenant scope
/// - Keys are case-insensitive in Redis, but we use lowercase for consistency
/// </summary>
public static class CacheKeyBuilder
{
    public const string CurrentVersionSuffix = "v1";

    private static string Versioned(string key)
    {
        return $"{key}:{CurrentVersionSuffix}";
    }

    private static string ScopeToken(Guid? tenantId)
    {
        return tenantId?.ToString("N") ?? "global";
    }

    /// <summary>
    /// Builds a pattern for all product-related keys for a tenant (for bulk invalidation)
    /// Pattern: {instance}:tenant:{tenantId}:product*
    /// </summary>
    public static string ProductsPatternKey(string instanceName, Guid tenantId)
    {
        return $"{instanceName}:tenant:{tenantId:N}:product*";
    }

    /// <summary>
    /// Monotonic invalidation generation for all live Storefront product and
    /// homepage-product cache entries owned by the tenant.
    /// </summary>
    public static string StorefrontProductsInvalidationGenerationKey(string instanceName, Guid tenantId)
    {
        return $"{instanceName}:tenant:{tenantId:N}:cache-generation:storefront-products";
    }

    /// <summary>
    /// Builds a cache key for a product by slug
    /// Pattern: {instance}:tenant:{tenantId}:product:slug:{slug}
    /// </summary>
    public static string ProductBySlugKey(string instanceName, Guid tenantId, string slug)
    {
        return Versioned($"{instanceName}:tenant:{tenantId:N}:product:slug:{slug.ToLowerInvariant()}");
    }

    /// <summary>
    /// Builds a cache key for a product by ID
    /// Pattern: {instance}:tenant:{tenantId}:product:id:{productId}
    /// </summary>
    public static string ProductByIdKey(string instanceName, Guid tenantId, Guid productId)
    {
        return Versioned($"{instanceName}:tenant:{tenantId:N}:product:id:{productId:N}");
    }

    /// <summary>
    /// Builds a cache key for product list with query parameters
    /// Pattern: {instance}:tenant:{tenantId}:products:list:{queryHash}
    /// </summary>
    /// <param name="instanceName">Redis instance name</param>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="queryHash">Deterministic hash of query parameters</param>
    public static string ProductsListKey(string instanceName, Guid tenantId, string queryHash)
    {
        return Versioned($"{instanceName}:tenant:{tenantId:N}:products:list:{queryHash}");
    }

    // =============================================================================
    // HOMEPAGE SECTION CACHE KEYS (Phase 2.5)
    // These keys are for homepage-specific cached data.
    // All homepage sections use the tenant:{tenantId}:home:{section} pattern.
    // =============================================================================

    /// <summary>
    /// Builds a cache key for featured products on homepage.
    /// Pattern: {instance}:tenant:{tenantId}:home:featured-products
    /// TTL: 30 minutes (featured products are curated, low volatility)
    /// </summary>
    public static string HomeFeaturedProductsKey(string instanceName, Guid tenantId)
    {
        return Versioned($"{instanceName}:tenant:{tenantId:N}:home:featured-products");
    }

    /// <summary>
    /// Builds a cache key for new arrivals on homepage.
    /// Pattern: {instance}:tenant:{tenantId}:home:new-arrivals
    /// TTL: 15 minutes (new arrivals update more frequently)
    /// </summary>
    public static string HomeNewArrivalsKey(string instanceName, Guid tenantId)
    {
        return Versioned($"{instanceName}:tenant:{tenantId:N}:home:new-arrivals");
    }

    /// <summary>
    /// Builds a cache key for best sellers on homepage.
    /// Pattern: {instance}:tenant:{tenantId}:home:best-sellers
    /// TTL: 15 minutes (best sellers based on sales data)
    /// </summary>
    public static string HomeBestSellersKey(string instanceName, Guid tenantId)
    {
        return Versioned($"{instanceName}:tenant:{tenantId:N}:home:best-sellers");
    }

    /// <summary>
    /// Builds a pattern for all homepage-related keys for a tenant (for bulk invalidation).
    /// Pattern: {instance}:tenant:{tenantId}:home:*
    /// 
    /// RESERVED FOR FUTURE USE:
    /// - Intended for cache invalidation when product/category data changes
    /// </summary>
    public static string HomePatternKey(string instanceName, Guid tenantId)
    {
        return $"{instanceName}:tenant:{tenantId:N}:home:*";
    }

    // =============================================================================
    // SEO & METADATA CACHE KEYS (Phase 3.0 - SEO APIs)
    // These keys are for SEO-specific cached data.
    // All SEO keys use the tenant:{tenantId}:seo:{type}:{identifier} pattern.
    // =============================================================================

    /// <summary>
    /// Builds a cache key for slug resolution.
    /// Pattern: {instance}:tenant:{tenantId}:seo:slug:{slug}
    /// TTL: 60 minutes
    /// </summary>
    public static string SeoSlugResolutionKey(string instanceName, Guid tenantId, string slug)
    {
        return $"{instanceName}:tenant:{tenantId:N}:seo:slug:{slug.ToLowerInvariant()}";
    }

    /// <summary>
    /// Builds a cache key for robots.txt.
    /// Pattern: {instance}:tenant:{tenantId}:seo:robots
    /// TTL: 12 hours
    /// </summary>
    public static string SeoRobotsKey(string instanceName, Guid tenantId)
    {
        return $"{instanceName}:tenant:{tenantId:N}:seo:robots";
    }

    // =============================================================================
    // CART CACHE KEYS (Phase 3.1 - Cart API)
    // These keys are for cart data storage in Redis.
    // Cart keys use the tenant:{tenantId}:cart:{ownerType}:{ownerId} pattern.
    // TTL: 24 hours (reset on every write)
    // =============================================================================

    /// <summary>
    /// Builds a cache key for a cart.
    /// Pattern: {instance}:tenant:{tenantId}:cart:{ownerType}:{ownerId}
    /// TTL: 24 hours
    /// </summary>
    /// <param name="instanceName">Redis instance name</param>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="ownerType">Owner type: "session" or "customer"</param>
    /// <param name="ownerId">Owner identifier (session ID or customer ID)</param>
    public static string CartKey(string instanceName, Guid tenantId, string ownerType, string ownerId)
    {
        return $"{instanceName}:tenant:{tenantId:N}:cart:{ownerType.ToLowerInvariant()}:{ownerId.ToLowerInvariant()}";
    }

    /// <summary>
    /// Builds a pattern for all cart-related keys for a tenant (for bulk invalidation).
    /// Pattern: {instance}:tenant:{tenantId}:cart:*
    /// </summary>
    public static string CartPatternKey(string instanceName, Guid tenantId)
    {
        return $"{instanceName}:tenant:{tenantId:N}:cart:*";
    }

    // =============================================================================
    // SHIPPING METHODS CACHE KEYS (Phase 5.1 - Shipping Read API)
    // These keys are for shipping method data from POS.
    // Shipping methods use the tenant:{tenantId}:shipping-methods pattern.
    // TTL: 60 minutes (shipping methods change rarely)
    // =============================================================================

    /// <summary>
    /// Builds a cache key for shipping methods list.
    /// Pattern: {instance}:tenant:{tenantId}:shipping-methods
    /// TTL: 60 minutes
    /// </summary>
    public static string ShippingMethodsKey(string instanceName, Guid tenantId)
    {
        return $"{instanceName}:tenant:{tenantId:N}:shipping-methods";
    }

    /// <summary>
    /// Monotonic invalidation generation for the tenant shipping-method cache.
    /// </summary>
    public static string ShippingMethodsInvalidationGenerationKey(string instanceName, Guid tenantId)
    {
        return $"{instanceName}:tenant:{tenantId:N}:cache-generation:shipping-methods";
    }

    public static string StorefrontWishlistKey(string instanceName, Guid tenantId, Guid customerId)
    {
        return $"{instanceName}:tenant:{tenantId:N}:storefront:wishlist:{customerId:N}";
    }

    public static string StorefrontWishlistCountKey(string instanceName, Guid tenantId, Guid customerId)
    {
        return $"{instanceName}:tenant:{tenantId:N}:storefront:wishlist-count:{customerId:N}";
    }

    public static string StorefrontWishlistInvalidationGenerationKey(
        string instanceName,
        Guid tenantId,
        Guid customerId)
    {
        return $"{instanceName}:tenant:{tenantId:N}:cache-generation:storefront-wishlist:{customerId:N}";
    }

    /// <summary>
    /// Builds a pattern for all shipping-related keys for a tenant (for invalidation).
    /// Pattern: {instance}:tenant:{tenantId}:shipping*
    /// </summary>
    public static string ShippingPatternKey(string instanceName, Guid tenantId)
    {
        return $"{instanceName}:tenant:{tenantId:N}:shipping*";
    }

    // =============================================================================
    // NOTIFICATION IDEMPOTENCY KEYS (Phase 5.3 - Tracking & Notifications)
    // These keys are used to track processed notification events for idempotency.
    // Notification keys use the tenant:{tenantId}:notification:{eventId} pattern.
    // TTL: 24 hours (sufficient to deduplicate retries)
    // =============================================================================

    /// <summary>
    /// Builds a cache key for notification idempotency tracking.
    /// Pattern: {instance}:tenant:{tenantId}:notification:{eventId}
    /// TTL: 24 hours
    /// 
    /// Usage: Set this key when a notification is successfully processed.
    /// If key exists, skip processing (already handled).
    /// </summary>
    /// <param name="instanceName">Redis instance name</param>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="eventId">The unique event ID being processed</param>
    public static string NotificationIdempotencyKey(string instanceName, Guid tenantId, Guid eventId)
    {
        return $"{instanceName}:tenant:{tenantId:N}:notification:{eventId:N}";
    }

    /// <summary>
    /// Builds a pattern for all notification-related keys for a tenant (for bulk invalidation).
    /// Pattern: {instance}:tenant:{tenantId}:notification:*
    /// 
    /// RESERVED FOR FUTURE USE:
    /// - Intended for cleanup/maintenance operations
    /// </summary>
    public static string NotificationPatternKey(string instanceName, Guid tenantId)
    {
        return $"{instanceName}:tenant:{tenantId:N}:notification:*";
    }

    public static string MarketplaceCmsPagesKey(string instanceName)
    {
        return Versioned($"{instanceName}:marketplace:cms:scope");
    }

    public static string MarketplaceFooterKey(string instanceName)
    {
        return Versioned($"{instanceName}:marketplace:footer");
    }

    public static string MarketplaceProductsListKey(string instanceName, Guid? tenantId, string filtersHash)
    {
        return Versioned($"{instanceName}:marketplace:products:list:{ScopeToken(tenantId)}:{filtersHash}");
    }

    public static string MarketplaceProductsPatternKey(string instanceName, Guid? tenantId)
    {
        return $"{instanceName}:marketplace:products:list:{ScopeToken(tenantId)}:*";
    }

    public static string MarketplaceOrdersListKey(string instanceName, Guid tenantId, string filtersHash)
    {
        return Versioned($"{instanceName}:marketplace:orders:list:{tenantId:N}:{filtersHash}");
    }

    public static string MarketplaceOrdersPatternKey(string instanceName, Guid tenantId)
    {
        return $"{instanceName}:marketplace:orders:list:{tenantId:N}:*";
    }

    public static string MarketplaceProductReviewsKey(string instanceName, Guid productId, string filtersHash)
    {
        return Versioned($"{instanceName}:mp:product:{productId:N}:reviews:{filtersHash}");
    }

    public static string MarketplaceProductReviewsPatternKey(string instanceName, Guid productId)
    {
        return $"{instanceName}:mp:product:{productId:N}:reviews*";
    }

    public static string MarketplaceProductRatingSummaryKey(string instanceName, Guid productId)
    {
        return Versioned($"{instanceName}:mp:product:{productId:N}:rating-summary");
    }

    public static string MarketplaceHomepageKey(string instanceName, Guid? tenantId, string? language = null)
    {
        var normalizedLanguage = string.IsNullOrWhiteSpace(language) ? "en" : language.ToLowerInvariant();
        return Versioned($"{instanceName}:marketplace:homepage:{ScopeToken(tenantId)}:{normalizedLanguage}");
    }

    public static string MarketplaceCategoryTranslationKey(string instanceName, Guid categoryId, string language)
    {
        var normalizedLanguage = string.IsNullOrWhiteSpace(language) ? "en" : language.ToLowerInvariant();
        return Versioned($"{instanceName}:marketplace:category:{categoryId:N}:{normalizedLanguage}");
    }

    public static string MarketplaceProductTranslationKey(string instanceName, Guid productId, string language)
    {
        var normalizedLanguage = string.IsNullOrWhiteSpace(language) ? "en" : language.ToLowerInvariant();
        return Versioned($"{instanceName}:marketplace:product:{productId:N}:{normalizedLanguage}");
    }

    public static string MarketplaceProductFieldTranslationKey(string instanceName, Guid productId, string field, string language)
    {
        var normalizedLanguage = string.IsNullOrWhiteSpace(language) ? "en" : language.ToLowerInvariant();
        var normalizedField = string.IsNullOrWhiteSpace(field) ? "name" : field.Trim().ToLowerInvariant();
        return Versioned($"{instanceName}:marketplace:product:{productId:N}:{normalizedField}:{normalizedLanguage}");
    }

    public static string CacheRefreshLockKey(string instanceName, string cacheKeyHash)
    {
        return $"{instanceName}:lock:cache-refresh:{cacheKeyHash}";
    }
}


