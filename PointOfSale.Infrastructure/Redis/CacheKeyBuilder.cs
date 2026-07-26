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
    /// Builds a cache key for a single product
    /// Pattern: {instance}:tenant:{tenantId}:product:{productId}
    /// </summary>
    public static string ProductKey(string instanceName, Guid tenantId, Guid productId)
    {
        return Versioned($"{instanceName}:tenant:{tenantId:N}:product:{productId:N}");
    }

    /// <summary>
    /// Builds a cache key for all products list for a tenant
    /// Pattern: {instance}:tenant:{tenantId}:products:all
    /// </summary>
    public static string ProductsAllKey(string instanceName, Guid tenantId)
    {
        return Versioned($"{instanceName}:tenant:{tenantId:N}:products:all");
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
    /// Builds a cache key for products by category
    /// Pattern: {instance}:tenant:{tenantId}:products:category:{categoryId}
    /// </summary>
    public static string ProductsByCategoryKey(string instanceName, Guid tenantId, Guid categoryId)
    {
        return Versioned($"{instanceName}:tenant:{tenantId:N}:products:category:{categoryId:N}");
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

    /// <summary>
    /// Builds a cache key for category tree (all categories)
    /// Pattern: {instance}:tenant:{tenantId}:categories:tree
    /// TTL: 30 minutes (categories are structural, low volatility)
    /// </summary>
    public static string CategoriesTreeKey(string instanceName, Guid tenantId)
    {
        return Versioned($"{instanceName}:tenant:{tenantId:N}:categories:tree");
    }

    /// <summary>
    /// Builds a cache key for a single category by slug
    /// Pattern: {instance}:tenant:{tenantId}:category:slug:{slug}
    /// TTL: 30 minutes (categories are structural, low volatility)
    /// </summary>
    public static string CategoryBySlugKey(string instanceName, Guid tenantId, string slug)
    {
        return Versioned($"{instanceName}:tenant:{tenantId:N}:category:slug:{slug.ToLowerInvariant()}");
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
    /// Builds a cache key for homepage navigation categories.
    /// Pattern: {instance}:tenant:{tenantId}:home:categories
    /// TTL: 60 minutes (categories are structural, very low volatility)
    /// </summary>
    public static string HomeCategoriesKey(string instanceName, Guid tenantId)
    {
        return Versioned($"{instanceName}:tenant:{tenantId:N}:home:categories");
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

    /// <summary>
    /// Builds a pattern for all category-related keys for a tenant (for bulk invalidation).
    /// Pattern: {instance}:tenant:{tenantId}:categor*
    /// 
    /// RESERVED FOR FUTURE USE:
    /// - This pattern key is NOT used in Phase 2.4 (Read-Only)
    /// - Intended for future cache invalidation scenarios (e.g., when CategoryUpdatedEvent is implemented)
    /// - Do NOT use in storefront-facing code until cache invalidation feature is implemented
    /// </summary>
    public static string CategoriesPatternKey(string instanceName, Guid tenantId)
    {
        return $"{instanceName}:tenant:{tenantId:N}:categor*";
    }

    // =============================================================================
    // SEO & METADATA CACHE KEYS (Phase 3.0 - SEO APIs)
    // These keys are for SEO-specific cached data.
    // All SEO keys use the tenant:{tenantId}:seo:{type}:{identifier} pattern.
    // =============================================================================

    /// <summary>
    /// Builds a cache key for SEO metadata by type and slug.
    /// Pattern: {instance}:tenant:{tenantId}:seo:meta:{type}:{slug}
    /// TTL: 30 minutes
    /// </summary>
    public static string SeoMetadataKey(string instanceName, Guid tenantId, string type, string slug)
    {
        return $"{instanceName}:tenant:{tenantId:N}:seo:meta:{type.ToLowerInvariant()}:{slug.ToLowerInvariant()}";
    }

    /// <summary>
    /// Builds a cache key for breadcrumbs by type and slug.
    /// Pattern: {instance}:tenant:{tenantId}:seo:breadcrumbs:{type}:{slug}
    /// TTL: 60 minutes
    /// </summary>
    public static string SeoBreadcrumbsKey(string instanceName, Guid tenantId, string type, string slug)
    {
        return $"{instanceName}:tenant:{tenantId:N}:seo:breadcrumbs:{type.ToLowerInvariant()}:{slug.ToLowerInvariant()}";
    }

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
    /// Builds a cache key for sitemap.
    /// Pattern: {instance}:tenant:{tenantId}:seo:sitemap
    /// TTL: 6 hours
    /// </summary>
    public static string SeoSitemapKey(string instanceName, Guid tenantId)
    {
        return $"{instanceName}:tenant:{tenantId:N}:seo:sitemap";
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

    /// <summary>
    /// Builds a pattern for all SEO-related keys for a tenant (for bulk invalidation).
    /// Pattern: {instance}:tenant:{tenantId}:seo:*
    /// </summary>
    public static string SeoPatternKey(string instanceName, Guid tenantId)
    {
        return $"{instanceName}:tenant:{tenantId:N}:seo:*";
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

    // =============================================================================
    // STORE CONFIG KEYS (Phase 7.1 - Theme & Store Config System)
    // These keys are for store configuration cached data.
    // Store config uses the tenant:{tenantId}:store:config pattern.
    // TTL: 30 minutes (config changes are infrequent)
    // =============================================================================

    /// <summary>
    /// Builds a cache key for store configuration.
    /// Pattern: {instance}:tenant:{tenantId}:store:config
    /// TTL: 30 minutes
    /// </summary>
    /// <param name="instanceName">Redis instance name</param>
    /// <param name="tenantId">Tenant identifier</param>
    public static string StoreConfigKey(string instanceName, Guid tenantId)
    {
        return Versioned($"{instanceName}:tenant:{tenantId:N}:store:config");
    }

    /// <summary>
    /// Builds a pattern for all store-related keys for a tenant (for bulk invalidation).
    /// Pattern: {instance}:tenant:{tenantId}:store:*
    /// </summary>
    public static string StorePatternKey(string instanceName, Guid tenantId)
    {
        return $"{instanceName}:tenant:{tenantId:N}:store:*";
    }

    // =============================================================================
    // PROMOTION CACHE KEYS (Promotions Engine)
    // These keys are for caching active promotions.
    // Promotions use the tenant:{tenantId}:promotions:{type} pattern.
    // TTL: 90 seconds (promotions need quick expiration for accuracy)
    // =============================================================================

    /// <summary>
    /// Builds a cache key for all active promotions for a tenant.
    /// Pattern: {instance}:tenant:{tenantId}:promotions:active
    /// TTL: 90 seconds
    /// </summary>
    /// <param name="instanceName">Redis instance name</param>
    /// <param name="tenantId">Tenant identifier</param>
    public static string PromotionsActiveKey(string instanceName, Guid tenantId)
    {
        return Versioned($"{instanceName}:tenant:{tenantId:N}:promotions:active");
    }

    /// <summary>
    /// Builds a cache key for product-specific promotions.
    /// Pattern: {instance}:tenant:{tenantId}:promotions:product:{productId}
    /// TTL: 90 seconds
    /// </summary>
    public static string PromotionsByProductKey(string instanceName, Guid tenantId, Guid productId)
    {
        return Versioned($"{instanceName}:tenant:{tenantId:N}:promotions:product:{productId:N}");
    }

    /// <summary>
    /// Builds a cache key for category-specific promotions.
    /// Pattern: {instance}:tenant:{tenantId}:promotions:category:{categoryId}
    /// TTL: 90 seconds
    /// </summary>
    public static string PromotionsByCategoryKey(string instanceName, Guid tenantId, Guid categoryId)
    {
        return Versioned($"{instanceName}:tenant:{tenantId:N}:promotions:category:{categoryId:N}");
    }

    /// <summary>
    /// Builds a pattern for all promotion-related keys for a tenant (for bulk invalidation).
    /// Pattern: {instance}:tenant:{tenantId}:promotions:*
    /// Used for cache invalidation when promotions are created/updated/deleted.
    /// </summary>
    public static string PromotionsPatternKey(string instanceName, Guid tenantId)
    {
        return $"{instanceName}:tenant:{tenantId:N}:promotions:*";
    }

    public static string HomepagePublishedScopeKey(string instanceName, Guid? tenantId)
    {
        return Versioned($"{instanceName}:homepage:{ScopeToken(tenantId)}:published");
    }

    public static string CmsPagesScopeKey(string instanceName, Guid tenantId)
    {
        return Versioned($"{instanceName}:cms:{tenantId:N}:scope");
    }

    public static string MarketplaceCmsPagesKey(string instanceName)
    {
        return Versioned($"{instanceName}:marketplace:cms:scope");
    }

    public static string StorefrontFooterKey(string instanceName, Guid tenantId)
    {
        return Versioned($"{instanceName}:footer:{tenantId:N}");
    }

    public static string StorefrontFooterPatternKey(string instanceName)
    {
        return $"{instanceName}:footer:*";
    }

    /// <summary>
    /// Builds a tenant-scoped pattern for footer keys (for targeted invalidation).
    /// Pattern: {instance}:footer:{tenantId}*
    /// Prefer this over the global overload to avoid cross-tenant invalidation.
    /// </summary>
    public static string StorefrontFooterPatternKey(string instanceName, Guid tenantId)
    {
        return $"{instanceName}:footer:{tenantId:N}*";
    }


    public static string MarketplaceFooterKey(string instanceName)
    {
        return Versioned($"{instanceName}:marketplace:footer");
    }

    /// <summary>
    /// Builds a cache key for storefront homepage (requires tenantId — compile-time safety).
    /// Pattern: {instance}:homepage:{tenantId}:published:v1
    /// Use this for storefront-scoped homepage requests.
    /// </summary>
    public static string StorefrontHomepageKey(string instanceName, Guid tenantId)
    {
        return Versioned($"{instanceName}:homepage:{tenantId:N}:published");
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


