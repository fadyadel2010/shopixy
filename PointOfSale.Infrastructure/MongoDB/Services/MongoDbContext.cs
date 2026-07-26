using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using PointOfSale.Infrastructure.MongoDB.Configuration;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.MongoDB.Documents.Affiliate;

namespace PointOfSale.Infrastructure.MongoDB.Services;

/// <summary>
/// MongoDB context providing access to collections and database.
/// This is registered as a singleton to share the connection pool.
/// 
/// IMPORTANT:
/// - MongoClient is thread-safe and should be reused (singleton)
/// - IMongoDatabase and IMongoCollection are also thread-safe
/// - Index creation is idempotent (safe to run multiple times)
/// </summary>
public class MongoDbContext
{
    private readonly IMongoDatabase _database;
    private readonly MongoDbSettings _settings;
    private readonly ILogger<MongoDbContext> _logger;
    private bool _indexesCreated = false;
    private readonly object _indexLock = new();

    public MongoDbContext(
        IOptions<MongoDbSettings> settings,
        ILogger<MongoDbContext> logger)
    {
        _settings = settings.Value;
        _logger = logger;

        if (string.IsNullOrWhiteSpace(_settings.ConnectionString))
        {
            throw new ArgumentException("MongoDB ConnectionString is not configured");
        }

        var client = new MongoClient(_settings.ConnectionString);
        _database = client.GetDatabase(_settings.DatabaseName);

        _logger.LogInformation(
            "MongoDB context initialized | Database: {DatabaseName} | Server: {ConnectionString}",
            _settings.DatabaseName,
            MaskConnectionString(_settings.ConnectionString));
    }

    /// <summary>
    /// Get the underlying IMongoDatabase for direct access
    /// </summary>
    public IMongoDatabase Database => _database;

    /// <summary>
    /// Get the Products collection
    /// </summary>
    public IMongoCollection<ProductDocument> Products => 
        _database.GetCollection<ProductDocument>(_settings.ProductsCollectionName);

    /// <summary>
    /// Get the Categories collection
    /// </summary>
    public IMongoCollection<CategoryDocument> Categories => 
        _database.GetCollection<CategoryDocument>(_settings.CategoriesCollectionName);

    /// <summary>
    /// Get the Coupons collection.
    /// </summary>
    public IMongoCollection<CouponDocument> Coupons =>
        _database.GetCollection<CouponDocument>("coupons");

    /// <summary>
    /// Get the Promotions collection.
    /// </summary>
    public IMongoCollection<PromotionDocument> Promotions =>
        _database.GetCollection<PromotionDocument>("promotions");

    /// <summary>
    /// Get the Shipping Methods collection
    /// </summary>
    public IMongoCollection<ShippingMethodDocument> ShippingMethods => 
        _database.GetCollection<ShippingMethodDocument>("shipping_methods");

    /// <summary>
    /// Get the CMS Pages collection (Phase 9.11)
    /// </summary>
    public IMongoCollection<StorefrontPageDocument> StorefrontPages =>
        _database.GetCollection<StorefrontPageDocument>("storefront_pages");

    /// <summary>
    /// Get the Navigation collection (Phase 9.11)
    /// </summary>
    public IMongoCollection<StorefrontNavigationDocument> StorefrontNavigation =>
        _database.GetCollection<StorefrontNavigationDocument>("storefront_navigation");

    /// <summary>
    /// Get the Storefront Config collection (AF-23 Readiness Gate).
    /// Single document per tenant — contains storefront status, theme, behavior config.
    /// </summary>
    public IMongoCollection<StorefrontConfigDocument> StorefrontConfigs =>
        _database.GetCollection<StorefrontConfigDocument>("storefront_configs");

    /// <summary>
    /// Get the Payment Methods collection (Phase 10.4.D1)
    /// </summary>
    public IMongoCollection<PaymentMethodDocument> PaymentMethods =>
        _database.GetCollection<PaymentMethodDocument>("payment_methods");

    /// <summary>
    /// Get the Provider Trust Snapshots collection (Services Marketplace)
    /// </summary>
    public IMongoCollection<ProviderTrustSnapshotDocument> ProviderTrustSnapshots =>
        _database.GetCollection<ProviderTrustSnapshotDocument>("provider_trust_snapshots");

    // ═══════════════════════════════════════════════════════════════════
    // AFFILIATE SYSTEM (Phase AF-3)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Get the Affiliate Dashboards collection (AF-3)
    /// </summary>
    public IMongoCollection<AffiliateDashboardDocument> AffiliateDashboards =>
        _database.GetCollection<AffiliateDashboardDocument>("affiliate_dashboards");

    /// <summary>
    /// Get the Affiliate Orders collection (AF-3)
    /// </summary>
    public IMongoCollection<AffiliateOrderDocument> AffiliateOrders =>
        _database.GetCollection<AffiliateOrderDocument>("affiliate_orders");

    /// <summary>
    /// Get the Affiliate Earnings Summaries collection (AF-3)
    /// </summary>
    public IMongoCollection<AffiliateEarningsSummaryDocument> AffiliateEarningsSummaries =>
        _database.GetCollection<AffiliateEarningsSummaryDocument>("affiliate_earnings_summaries");

    // ═══════════════════════════════════════════════════════════════════
    // AFFILIATE ANALYTICS (Phase AF-8)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Get the Affiliate Clicks collection (AF-8)
    /// </summary>
    public IMongoCollection<AffiliateClickDocument> AffiliateClicks =>
        _database.GetCollection<AffiliateClickDocument>("affiliate_clicks");

    /// <summary>
    /// Get the Affiliate Analytics Summaries collection (AF-8)
    /// </summary>
    public IMongoCollection<AffiliateAnalyticsSummaryDocument> AffiliateAnalyticsSummaries =>
        _database.GetCollection<AffiliateAnalyticsSummaryDocument>("affiliate_analytics_summaries");

    /// <summary>
    /// AF-24: Daily analytics projection collection.
    /// Key: (AffiliateAccountId, TenantId, Date)
    /// </summary>
    public IMongoCollection<AffiliateAnalyticsDailyDocument> AffiliateAnalyticsDaily =>
        _database.GetCollection<AffiliateAnalyticsDailyDocument>("affiliate_analytics_daily");

    public IMongoCollection<AffiliateProjectionEventReceiptDocument> AffiliateProjectionEventReceipts =>
        _database.GetCollection<AffiliateProjectionEventReceiptDocument>("affiliate_projection_event_receipts");

    // ═══════════════════════════════════════════════════════════════════
    // PUBLIC AFFILIATE STORE DIRECTORY (Phase AF-10B)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Get the Public Affiliate Stores collection (AF-10B)
    /// Cross-tenant — used for public store discovery without authentication.
    /// </summary>
    public IMongoCollection<PublicAffiliateStoreDocument> PublicAffiliateStores =>
        _database.GetCollection<PublicAffiliateStoreDocument>("public_affiliate_stores");

    /// <summary>
    /// Get the Public Affiliate Products collection (AF-11)
    /// Cross-tenant — used for public product discovery without authentication.
    /// </summary>
    public IMongoCollection<PublicAffiliateProductDocument> PublicAffiliateProducts =>
        _database.GetCollection<PublicAffiliateProductDocument>("public_affiliate_products");

    // ═══════════════════════════════════════════════════════════════════
    // AFFILIATE PRODUCT INTELLIGENCE (Phase AF-22)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Get the Affiliate Product Intelligence collection (AF-22)
    /// Event-driven product intelligence for Discover & Landing pages.
    /// Enriches product discovery with full details, inventory, variants, store context, and commission intelligence.
    /// </summary>
    public IMongoCollection<AffiliateProductIntelligenceDocument> AffiliateProductIntelligence =>
        _database.GetCollection<AffiliateProductIntelligenceDocument>("affiliate_product_intelligence");

    // ═══════════════════════════════════════════════════════════════════
    // ABANDONED CHECKOUT (Phase: Lost Orders)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Get the Abandoned Checkouts collection (Lost Orders dashboard)
    /// </summary>
    public IMongoCollection<AbandonedCheckoutDocument> AbandonedCheckouts =>
        _database.GetCollection<AbandonedCheckoutDocument>("abandoned_checkouts");

    /// <summary>
    /// Global CommerceHub listing projection.
    /// Cross-tenant read model for marketplace discovery.
    /// </summary>
    public IMongoCollection<CommerceHubProductDocument> CommerceHubProducts =>
        _database.GetCollection<CommerceHubProductDocument>("commercehub_products");

    /// <summary>
    /// Approved marketplace product reviews used for public read APIs.
    /// </summary>
    public IMongoCollection<MarketplaceProductReviewDocument> MarketplaceProductReviews =>
        _database.GetCollection<MarketplaceProductReviewDocument>("marketplace_product_reviews");

    /// <summary>
    /// Aggregate marketplace rating summary per product.
    /// </summary>
    public IMongoCollection<ProductReviewSummaryDocument> ProductReviewSummaries =>
        _database.GetCollection<ProductReviewSummaryDocument>("product_review_summaries");

    /// <summary>
    /// Idempotency inbox for bulk/fan-out events.
    /// Prevents duplicate processing of expensive operations like tenant-wide updates.
    /// </summary>
    public IMongoCollection<ProcessedEventDocument> ProcessedEvents =>
        _database.GetCollection<ProcessedEventDocument>("processed_events");

    /// <summary>
    /// Ensures all required indexes are created.
    /// This method is idempotent - safe to call multiple times.
    /// Should be called during application startup.
    /// </summary>
    public async Task EnsureIndexesCreatedAsync(CancellationToken cancellationToken = default)
    {
        // Double-check locking for thread safety
        if (_indexesCreated) return;

        lock (_indexLock)
        {
            if (_indexesCreated) return;
            _indexesCreated = true;
        }

        _logger.LogInformation("Creating MongoDB indexes for Products collection...");

        try
        {
            var productsCollection = Products;

            // Index 1: Unique compound index on (TenantId, ProductId)
            // This ensures no duplicate products per tenant and enables efficient lookups
            var tenantProductIndex = new CreateIndexModel<ProductDocument>(
                Builders<ProductDocument>.IndexKeys
                    .Ascending(p => p.TenantId)
                    .Ascending(p => p.ProductId),
                new CreateIndexOptions
                {
                    Unique = true,
                    Name = "idx_tenant_product_unique",
                    Background = true
                });

            // Index 2: TenantId alone for tenant-scoped queries
            var tenantIndex = new CreateIndexModel<ProductDocument>(
                Builders<ProductDocument>.IndexKeys.Ascending(p => p.TenantId),
                new CreateIndexOptions
                {
                    Name = "idx_tenant",
                    Background = true
                });

            // Index 3: SKU within tenant for quick lookups
            var tenantSkuIndex = new CreateIndexModel<ProductDocument>(
                Builders<ProductDocument>.IndexKeys
                    .Ascending(p => p.TenantId)
                    .Ascending(p => p.SKU),
                new CreateIndexOptions
                {
                    Name = "idx_tenant_sku",
                    Background = true,
                    Sparse = true // SKU can be null
                });

            // Index 4: Slug within tenant for SEO URLs
            var tenantSlugIndex = new CreateIndexModel<ProductDocument>(
                Builders<ProductDocument>.IndexKeys
                    .Ascending(p => p.TenantId)
                    .Ascending(p => p.Slug),
                new CreateIndexOptions
                {
                    Name = "idx_tenant_slug",
                    Background = true,
                    Sparse = true // Slug can be null
                });

            // =============================================================================
            // PRODUCTION HARDENING (Phase 12) — STOREFRONT HOT PATH INDEXES
            // =============================================================================

            // Index 5: Product listing query (CRITICAL HOT PATH)
            // Query pattern: GetProductListAsync filters by TenantId, IsActive, IsOnlineVisible,
            // optionally CategoryId, and sorts by CreatedAtUtc DESC
            var productListingIndex = new CreateIndexModel<ProductDocument>(
                Builders<ProductDocument>.IndexKeys
                    .Ascending(p => p.TenantId)
                    .Ascending(p => p.IsActive)
                    .Ascending(p => p.IsOnlineVisible)
                    .Ascending(p => p.CategoryId)
                    .Descending(p => p.CreatedAtUtc),
                new CreateIndexOptions
                {
                    Name = "idx_tenant_active_visible_category_created",
                    Background = true
                });

            // Index 6: Featured products query
            // Query pattern: GetFeaturedProductsAsync filters on IsFeatured=true, sorts by Name
            var featuredProductsIndex = new CreateIndexModel<ProductDocument>(
                Builders<ProductDocument>.IndexKeys
                    .Ascending(p => p.TenantId)
                    .Ascending(p => p.IsActive)
                    .Ascending(p => p.IsOnlineVisible)
                    .Ascending(p => p.IsFeatured)
                    .Ascending(p => p.Name),
                new CreateIndexOptions
                {
                    Name = "idx_tenant_featured_products",
                    Background = true
                });

            // Index 7: Embedded variant lookup (CRITICAL for Cart/Checkout)
            // Query pattern: ElemMatch on Variants.VariantId for variant-centric operations
            // This index enables efficient lookups of products containing specific variants
            var variantIdIndex = new CreateIndexModel<ProductDocument>(
                Builders<ProductDocument>.IndexKeys
                    .Ascending(p => p.TenantId)
                    .Ascending("Variants.VariantId"),
                new CreateIndexOptions
                {
                    Name = "idx_tenant_variant_id",
                    Background = true
                });

            // Index 8: New arrivals (sorted by CreatedAtUtc DESC, limited to visible products)
            var newArrivalsIndex = new CreateIndexModel<ProductDocument>(
                Builders<ProductDocument>.IndexKeys
                    .Ascending(p => p.TenantId)
                    .Ascending(p => p.IsActive)
                    .Ascending(p => p.IsOnlineVisible)
                    .Descending(p => p.CreatedAtUtc),
                new CreateIndexOptions
                {
                    Name = "idx_tenant_new_arrivals",
                    Background = true
                });

            // Create all indexes
            await productsCollection.Indexes.CreateManyAsync(
                new[] { 
                    tenantProductIndex, 
                    tenantIndex, 
                    tenantSkuIndex, 
                    tenantSlugIndex,
                    productListingIndex,
                    featuredProductsIndex,
                    variantIdIndex,
                    newArrivalsIndex
                },
                cancellationToken);

            _logger.LogInformation(
                "MongoDB indexes created successfully | Collection: {Collection} | IndexCount: 8",
                _settings.ProductsCollectionName);

            // --- Categories Collection Indexes ---
            _logger.LogInformation("Creating MongoDB indexes for Categories collection...");

            var categoriesCollection = Categories;

            // Index 1: Unique compound index on (TenantId, CategoryId)
            var tenantCategoryIndex = new CreateIndexModel<CategoryDocument>(
                Builders<CategoryDocument>.IndexKeys
                    .Ascending(c => c.TenantId)
                    .Ascending(c => c.CategoryId),
                new CreateIndexOptions
                {
                    Unique = true,
                    Name = "idx_tenant_category_unique",
                    Background = true
                });

            // Index 2: TenantId + IsActive + IsOnlineVisible for listing queries
            var tenantVisibleIndex = new CreateIndexModel<CategoryDocument>(
                Builders<CategoryDocument>.IndexKeys
                    .Ascending(c => c.TenantId)
                    .Ascending(c => c.IsActive)
                    .Ascending(c => c.IsOnlineVisible),
                new CreateIndexOptions
                {
                    Name = "idx_tenant_visible",
                    Background = true
                });

            // Index 3: Slug within tenant for SEO URLs
            var tenantCategorySlugIndex = new CreateIndexModel<CategoryDocument>(
                Builders<CategoryDocument>.IndexKeys
                    .Ascending(c => c.TenantId)
                    .Ascending(c => c.Slug),
                new CreateIndexOptions
                {
                    Name = "idx_tenant_category_slug",
                    Background = true
                });

            // Create category indexes
            await categoriesCollection.Indexes.CreateManyAsync(
                new[] { tenantCategoryIndex, tenantVisibleIndex, tenantCategorySlugIndex },
                cancellationToken);

            _logger.LogInformation(
                "MongoDB indexes created successfully | Collection: {Collection}",
                _settings.CategoriesCollectionName);

            // --- Shipping Methods Collection Indexes ---
            _logger.LogInformation("Creating MongoDB indexes for Shipping Methods collection...");

            var shippingMethodsCollection = ShippingMethods;

            // Index 1: Unique compound index on (TenantId, ShippingMethodId)
            var tenantShippingMethodIndex = new CreateIndexModel<ShippingMethodDocument>(
                Builders<ShippingMethodDocument>.IndexKeys
                    .Ascending(s => s.TenantId)
                    .Ascending(s => s.ShippingMethodId),
                new CreateIndexOptions
                {
                    Unique = true,
                    Name = "idx_tenant_shipping_method_unique",
                    Background = true
                });

            // Index 2: TenantId + IsActive + SortOrder for listing queries
            var tenantActiveShippingIndex = new CreateIndexModel<ShippingMethodDocument>(
                Builders<ShippingMethodDocument>.IndexKeys
                    .Ascending(s => s.TenantId)
                    .Ascending(s => s.IsActive)
                    .Ascending(s => s.SortOrder),
                new CreateIndexOptions
                {
                    Name = "idx_tenant_active_sort",
                    Background = true
                });

            // Create shipping method indexes
            await shippingMethodsCollection.Indexes.CreateManyAsync(
                new[] { tenantShippingMethodIndex, tenantActiveShippingIndex },
                cancellationToken);

            _logger.LogInformation(
                "MongoDB indexes created successfully | Collection: shipping_methods");

            // --- Payment Methods Collection Indexes (Phase 10.4.D1) ---
            _logger.LogInformation("Creating MongoDB indexes for Payment Methods collection...");

            var paymentMethodsCollection = PaymentMethods;

            // Index 1: Unique compound index on (TenantId, PaymentMethodId)
            var tenantPaymentMethodIndex = new CreateIndexModel<PaymentMethodDocument>(
                Builders<PaymentMethodDocument>.IndexKeys
                    .Ascending(p => p.TenantId)
                    .Ascending(p => p.PaymentMethodId),
                new CreateIndexOptions
                {
                    Unique = true,
                    Name = "idx_tenant_payment_method_unique",
                    Background = true
                });

            // Create payment method indexes
            // S40: Defensive pattern to handle duplicate index creation (matching coupons pattern)
            try
            {
                await paymentMethodsCollection.Indexes.CreateManyAsync(
                    new[] { tenantPaymentMethodIndex },
                    cancellationToken);

                _logger.LogInformation(
                    "MongoDB indexes created successfully | Collection: payment_methods");
            }
            catch (MongoCommandException mex) when (
                mex.Message.Contains("already exists") ||
                mex.Code == 85 || // IndexOptionsConflict
                mex.CodeName == "IndexOptionsConflict")
            {
                _logger.LogDebug("Payment Methods indexes already exist");
            }

            // --- CMS Pages Collection Indexes (Phase 9.11) ---
            _logger.LogInformation("Creating MongoDB indexes for CMS Pages collection...");

            var pagesCollection = StorefrontPages;

            // Index 1: Unique compound index on (TenantId, Slug)
            var tenantPageSlugIndex = new CreateIndexModel<StorefrontPageDocument>(
                Builders<StorefrontPageDocument>.IndexKeys
                    .Ascending(p => p.TenantId)
                    .Ascending(p => p.Slug),
                new CreateIndexOptions
                {
                    Unique = true,
                    Name = "idx_tenant_page_slug_unique",
                    Background = true
                });

            // Index 2: TenantId for listing queries
            var tenantPagesIndex = new CreateIndexModel<StorefrontPageDocument>(
                Builders<StorefrontPageDocument>.IndexKeys.Ascending(p => p.TenantId),
                new CreateIndexOptions
                {
                    Name = "idx_tenant_pages",
                    Background = true
                });

            // Index 3: Published pages query optimization (LoadPublishedPagesAsync)
            // Query pattern: Find(TenantId, IsPublished).SortBy(DisplayOrder).ThenBy(Title)
            // This compound index covers the filter + sort to avoid collection scans
            var publishedPagesIndex = new CreateIndexModel<StorefrontPageDocument>(
                Builders<StorefrontPageDocument>.IndexKeys
                    .Ascending(p => p.TenantId)
                    .Ascending(p => p.IsPublished)
                    .Ascending(p => p.DisplayOrder)
                    .Ascending(p => p.Title),
                new CreateIndexOptions
                {
                    Name = "idx_tenant_published_display_title",
                    Background = true
                });

            // Create CMS page indexes
            await pagesCollection.Indexes.CreateManyAsync(
                new[] { tenantPageSlugIndex, tenantPagesIndex, publishedPagesIndex },
                cancellationToken);

            _logger.LogInformation(
                "MongoDB indexes created successfully | Collection: storefront_pages");

            // --- Navigation Collection Indexes (Phase 9.11) ---
            _logger.LogInformation("Creating MongoDB indexes for Navigation collection...");

            var navCollection = StorefrontNavigation;

            // Index 1: Unique index on TenantId (one navigation doc per tenant)
            var tenantNavIndex = new CreateIndexModel<StorefrontNavigationDocument>(
                Builders<StorefrontNavigationDocument>.IndexKeys.Ascending(n => n.TenantId),
                new CreateIndexOptions
                {
                    Unique = true,
                    Name = "idx_tenant_navigation_unique",
                    Background = true
                });

            // Create navigation index
            await navCollection.Indexes.CreateManyAsync(
                new[] { tenantNavIndex },
                cancellationToken);

            _logger.LogInformation(
                "MongoDB indexes created successfully | Collection: storefront_navigation");

            // --- Storefront Config Collection Indexes (AF-23 Readiness Gate) ---
            _logger.LogInformation("Creating MongoDB indexes for Storefront Config collection...");

            var storefrontConfigCollection = StorefrontConfigs;

            // CRITICAL: Deduplicate TenantId before creating unique index
            // If duplicates exist, the unique index creation will fail
            await DeduplicateStorefrontConfigsAsync(storefrontConfigCollection, cancellationToken);

            // Index 1: Unique index on TenantId (one config doc per tenant)
            // This index is CRITICAL for StorefrontChatController query performance
            var tenantConfigIndex = new CreateIndexModel<StorefrontConfigDocument>(
                Builders<StorefrontConfigDocument>.IndexKeys.Ascending(c => c.TenantId),
                new CreateIndexOptions
                {
                    Unique = true,
                    Name = "idx_tenant_config_unique",
                    Background = true
                });

            // Create storefront config index
            await storefrontConfigCollection.Indexes.CreateManyAsync(
                new[] { tenantConfigIndex },
                cancellationToken);

            _logger.LogInformation(
                "MongoDB indexes created successfully | Collection: storefront_configs");

            // --- WhatsApp Message Logs Collection Indexes ---
            // CRITICAL: TTL index prevents unbounded collection growth
            _logger.LogInformation("Creating MongoDB indexes for WhatsApp Message Logs collection...");

            var whatsappLogsCollection = _database.GetCollection<WhatsAppMessageLogDocument>("WhatsAppMessageLogs");
            var whatsappIndexBuilder = Builders<WhatsAppMessageLogDocument>.IndexKeys;

            // Index 1: TTL Index (CRITICAL) - Auto-delete documents after 90 days
            // This prevents unbounded growth. At 1000 messages/day = 90,000 max per tenant
            var ttlIndexModel = new CreateIndexModel<WhatsAppMessageLogDocument>(
                whatsappIndexBuilder.Ascending(x => x.CreatedAtUtc),
                new CreateIndexOptions
                {
                    ExpireAfter = TimeSpan.FromDays(90), // 90-day retention
                    Name = "TTL_CreatedAtUtc_90days",
                    Background = true
                }
            );

            // Index 2: Retry job query optimization
            // Query pattern: GetPendingMessagesAsync filters by IsRetryable, Status, SendAttempts
            var retryJobIndexModel = new CreateIndexModel<WhatsAppMessageLogDocument>(
                whatsappIndexBuilder
                    .Ascending(x => x.IsRetryable)
                    .Ascending(x => x.Status)
                    .Ascending(x => x.SendAttempts)
                    .Ascending(x => x.CreatedAtUtc),
                new CreateIndexOptions
                {
                    Name = "idx_retry_job_query",
                    Background = true
                }
            );

            // Index 3: Tenant + Status for analytics
            var tenantAnalyticsIndexModel = new CreateIndexModel<WhatsAppMessageLogDocument>(
                whatsappIndexBuilder
                    .Ascending(x => x.TenantId)
                    .Ascending(x => x.Status)
                    .Descending(x => x.CreatedAtUtc),
                new CreateIndexOptions
                {
                    Name = "idx_tenant_status_created",
                    Background = true
                }
            );

            // Index 4: Tenant + MessageType for filtering
            var tenantMessageTypeIndexModel = new CreateIndexModel<WhatsAppMessageLogDocument>(
                whatsappIndexBuilder
                    .Ascending(x => x.TenantId)
                    .Ascending(x => x.MessageType)
                    .Descending(x => x.CreatedAtUtc),
                new CreateIndexOptions
                {
                    Name = "idx_tenant_messagetype_created",
                    Background = true
                }
            );

            // Index 5: Idempotency key for duplicate detection
            var idempotencyIndexModel = new CreateIndexModel<WhatsAppMessageLogDocument>(
                whatsappIndexBuilder.Ascending(x => x.IdempotencyKey),
                new CreateIndexOptions
                {
                    Name = "idx_idempotency_key",
                    Background = true,
                    Unique = true,
                    Sparse = true // Only index documents where field exists and is non-empty
                }
            );

            try
            {
                await whatsappLogsCollection.Indexes.CreateManyAsync(
                    new[]
                    {
                        ttlIndexModel,
                        retryJobIndexModel,
                        tenantAnalyticsIndexModel,
                        tenantMessageTypeIndexModel,
                        idempotencyIndexModel
                    },
                    cancellationToken);

                _logger.LogInformation(
                    "MongoDB indexes created successfully | Collection: WhatsAppMessageLogs | TTL: 90 days");
            }
            catch (MongoCommandException mex) when (mex.Message.Contains("already exists"))
            {
                // Index already exists with same name, that's fine
                _logger.LogDebug("WhatsAppMessageLogs indexes already exist");
            }

            // --- Provider Trust Snapshots Collection Indexes (P2-15: TTL) ---
            _logger.LogInformation("Creating MongoDB indexes for Provider Trust Snapshots collection...");

            var trustSnapshotsCollection = ProviderTrustSnapshots;
            var trustIndexBuilder = Builders<ProviderTrustSnapshotDocument>.IndexKeys;

            // Index 1: Unique compound index on (TenantId, ProviderProfileId)
            // One snapshot per provider per tenant
            var trustTenantProviderIndex = new CreateIndexModel<ProviderTrustSnapshotDocument>(
                trustIndexBuilder
                    .Ascending(t => t.TenantId)
                    .Ascending(t => t.ProviderProfileId),
                new CreateIndexOptions
                {
                    Unique = true,
                    Name = "idx_tenant_provider_unique",
                    Background = true
                });

            // Index 2: TTL index on LastUpdatedAt (P2-15)
            // Snapshots that haven't been updated in 365 days are stale
            // (provider left the platform or never received reviews).
            // TTL prevents unbounded collection growth.
            var trustTtlIndex = new CreateIndexModel<ProviderTrustSnapshotDocument>(
                trustIndexBuilder.Ascending(t => t.LastUpdatedAt),
                new CreateIndexOptions
                {
                    ExpireAfter = TimeSpan.FromDays(365),
                    Name = "TTL_LastUpdatedAt_365days",
                    Background = true
                });

            try
            {
                await trustSnapshotsCollection.Indexes.CreateManyAsync(
                    new[] { trustTenantProviderIndex, trustTtlIndex },
                    cancellationToken);

                _logger.LogInformation(
                    "MongoDB indexes created successfully | Collection: provider_trust_snapshots | TTL: 365 days");
            }
            catch (MongoCommandException mex) when (mex.Message.Contains("already exists"))
            {
                _logger.LogDebug("ProviderTrustSnapshots indexes already exist");
            }

            // --- Affiliate System Indexes (Phase AF-3) ---
            _logger.LogInformation("Creating MongoDB indexes for Affiliate collections...");

            // 1. Affiliate Dashboards: unique (TenantId, AffiliateAccountId)
            var dashboardCollection = AffiliateDashboards;
            var dashboardTenantAffiliateIndex = new CreateIndexModel<AffiliateDashboardDocument>(
                Builders<AffiliateDashboardDocument>.IndexKeys
                    .Ascending(d => d.TenantId)
                    .Ascending(d => d.AffiliateAccountId),
                new CreateIndexOptions
                {
                    Unique = true,
                    Name = "idx_tenant_affiliate_unique",
                    Background = true
                });

            await dashboardCollection.Indexes.CreateManyAsync(
                new[] { dashboardTenantAffiliateIndex },
                cancellationToken);

            // 2. Affiliate Orders: unique (TenantId, OrderId)
            var affiliateOrderCollection = AffiliateOrders;
            var orderTenantOrderIndex = new CreateIndexModel<AffiliateOrderDocument>(
                Builders<AffiliateOrderDocument>.IndexKeys
                    .Ascending(d => d.TenantId)
                    .Ascending(d => d.OrderId),
                new CreateIndexOptions
                {
                    Unique = true,
                    Name = "idx_tenant_order_unique",
                    Background = true
                });

            // Secondary index: affiliate's orders for dashboard listing
            var orderAffiliateIndex = new CreateIndexModel<AffiliateOrderDocument>(
                Builders<AffiliateOrderDocument>.IndexKeys
                    .Ascending(d => d.TenantId)
                    .Ascending(d => d.AffiliateAccountId)
                    .Descending(d => d.CreatedAtUtc),
                new CreateIndexOptions
                {
                    Name = "idx_tenant_affiliate_created",
                    Background = true
                });

            await affiliateOrderCollection.Indexes.CreateManyAsync(
                new[] { orderTenantOrderIndex, orderAffiliateIndex },
                cancellationToken);

            // 3. Affiliate Earnings Summaries: unique (TenantId, AffiliateAccountId, Currency)
            var summaryCollection = AffiliateEarningsSummaries;
            var summaryUniqueIndex = new CreateIndexModel<AffiliateEarningsSummaryDocument>(
                Builders<AffiliateEarningsSummaryDocument>.IndexKeys
                    .Ascending(d => d.TenantId)
                    .Ascending(d => d.AffiliateAccountId)
                    .Ascending(d => d.Currency),
                new CreateIndexOptions
                {
                    Unique = true,
                    Name = "idx_tenant_affiliate_currency_unique",
                    Background = true
                });

            await summaryCollection.Indexes.CreateManyAsync(
                new[] { summaryUniqueIndex },
                cancellationToken);

            // ── AF-8: Affiliate Clicks indexes ──
            _logger.LogInformation("Creating MongoDB indexes for Affiliate Analytics (AF-8)...");

            var clickCollection = AffiliateClicks;

            // Idempotency: unique (TenantId, SessionId, ProductId)
            var clickIdempotencyIndex = new CreateIndexModel<AffiliateClickDocument>(
                Builders<AffiliateClickDocument>.IndexKeys
                    .Ascending(d => d.TenantId)
                    .Ascending(d => d.SessionId)
                    .Ascending(d => d.ProductId),
                new CreateIndexOptions
                {
                    Unique = true,
                    Name = "idx_click_idempotency",
                    Background = true
                });

            // Timeline: (TenantId, AffiliateAccountId, ClickedAtUtc)
            var clickTimelineIndex = new CreateIndexModel<AffiliateClickDocument>(
                Builders<AffiliateClickDocument>.IndexKeys
                    .Ascending(d => d.TenantId)
                    .Ascending(d => d.AffiliateAccountId)
                    .Descending(d => d.ClickedAtUtc),
                new CreateIndexOptions
                {
                    Name = "idx_click_affiliate_timeline",
                    Background = true
                });

            // Product aggregation: (TenantId, ProductId)
            var clickProductIndex = new CreateIndexModel<AffiliateClickDocument>(
                Builders<AffiliateClickDocument>.IndexKeys
                    .Ascending(d => d.TenantId)
                    .Ascending(d => d.ProductId),
                new CreateIndexOptions
                {
                    Name = "idx_click_product",
                    Background = true
                });

            // Session lookup for conversion bridge: (TenantId, SessionId)
            var clickSessionIndex = new CreateIndexModel<AffiliateClickDocument>(
                Builders<AffiliateClickDocument>.IndexKeys
                    .Ascending(d => d.TenantId)
                    .Ascending(d => d.SessionId),
                new CreateIndexOptions
                {
                    Name = "idx_click_session",
                    Background = true
                });

            // PATCH H: TTL index on ClickedAtUtc — auto-delete clicks after 365 days
            // Prevents unbounded collection growth (scalability finding R3)
            var clickTtlIndex = new CreateIndexModel<AffiliateClickDocument>(
                Builders<AffiliateClickDocument>.IndexKeys.Ascending(d => d.ClickedAtUtc),
                new CreateIndexOptions
                {
                    ExpireAfter = TimeSpan.FromDays(365),
                    Name = "TTL_ClickedAtUtc_365days",
                    Background = true
                });

            await clickCollection.Indexes.CreateManyAsync(
                new[] { clickIdempotencyIndex, clickTimelineIndex, clickProductIndex, clickSessionIndex, clickTtlIndex },
                cancellationToken);

            // AF-8: Analytics Summary: unique (TenantId, AffiliateAccountId)
            var analyticsSummaryCollection = AffiliateAnalyticsSummaries;
            var analyticsSummaryUniqueIndex = new CreateIndexModel<AffiliateAnalyticsSummaryDocument>(
                Builders<AffiliateAnalyticsSummaryDocument>.IndexKeys
                    .Ascending(d => d.TenantId)
                    .Ascending(d => d.AffiliateAccountId),
                new CreateIndexOptions
                {
                    Unique = true,
                    Name = "idx_analytics_tenant_affiliate_unique",
                    Background = true
                });

            await analyticsSummaryCollection.Indexes.CreateManyAsync(
                new[] { analyticsSummaryUniqueIndex },
                cancellationToken);

            // ─── AF-24: Daily Analytics ──────────────────────────────────

            var dailyAnalyticsCollection = AffiliateAnalyticsDaily;

            // Trend query index: (affiliateAccountId, date)
            var dailyTrendIndex = new CreateIndexModel<AffiliateAnalyticsDailyDocument>(
                Builders<AffiliateAnalyticsDailyDocument>.IndexKeys
                    .Ascending(d => d.AffiliateAccountId)
                    .Descending(d => d.Date),
                new CreateIndexOptions
                {
                    Name = "idx_daily_affiliate_date",
                    Background = true
                });

            // Unique compound key: (affiliateAccountId, tenantId, date)
            var dailyUniqueIndex = new CreateIndexModel<AffiliateAnalyticsDailyDocument>(
                Builders<AffiliateAnalyticsDailyDocument>.IndexKeys
                    .Ascending(d => d.AffiliateAccountId)
                    .Ascending(d => d.TenantId)
                    .Ascending(d => d.Date),
                new CreateIndexOptions
                {
                    Unique = true,
                    Name = "idx_daily_affiliate_tenant_date_unique",
                    Background = true
                });

            // AF-24 HARDENING: TTL on daily analytics (keep ~13 months)
            var dailyTtlIndex = new CreateIndexModel<AffiliateAnalyticsDailyDocument>(
                Builders<AffiliateAnalyticsDailyDocument>.IndexKeys
                    .Ascending(d => d.UpdatedAtUtc),
                new CreateIndexOptions
                {
                    ExpireAfter = TimeSpan.FromDays(400),
                    Name = "TTL_AffiliateAnalyticsDaily_400days",
                    Background = true
                });

            await dailyAnalyticsCollection.Indexes.CreateManyAsync(
                new[] { dailyTrendIndex, dailyUniqueIndex, dailyTtlIndex },
                cancellationToken);

            // ─── AF-10B: Public Affiliate Stores ─────────────────────────

            var affiliateProjectionReceipts = AffiliateProjectionEventReceipts;
            var affiliateProjectionReceiptConsumerIndex = new CreateIndexModel<AffiliateProjectionEventReceiptDocument>(
                Builders<AffiliateProjectionEventReceiptDocument>.IndexKeys
                    .Ascending(d => d.ConsumerName)
                    .Ascending(d => d.DocumentId)
                    .Ascending(d => d.EventId),
                new CreateIndexOptions
                {
                    Unique = true,
                    Name = "idx_affiliate_projection_receipt_consumer_document_event_unique",
                    Background = true
                });

            var affiliateProjectionReceiptTtlIndex = new CreateIndexModel<AffiliateProjectionEventReceiptDocument>(
                Builders<AffiliateProjectionEventReceiptDocument>.IndexKeys
                    .Ascending(d => d.UpdatedAtUtc),
                new CreateIndexOptions
                {
                    ExpireAfter = TimeSpan.FromDays(400),
                    Name = "TTL_AffiliateProjectionEventReceipts_400days",
                    Background = true
                });

            await affiliateProjectionReceipts.Indexes.CreateManyAsync(
                new[] { affiliateProjectionReceiptConsumerIndex, affiliateProjectionReceiptTtlIndex },
                cancellationToken);

            var publicStoresCollection = PublicAffiliateStores;

            var publicStoresSlugIndex = new CreateIndexModel<PublicAffiliateStoreDocument>(
                Builders<PublicAffiliateStoreDocument>.IndexKeys
                    .Ascending(d => d.StoreSlug),
                new CreateIndexOptions
                {
                    Unique = true,
                    Name = "idx_public_stores_slug_unique",
                    Background = true
                });

            var publicStoresTenantIndex = new CreateIndexModel<PublicAffiliateStoreDocument>(
                Builders<PublicAffiliateStoreDocument>.IndexKeys
                    .Ascending(d => d.TenantId),
                new CreateIndexOptions
                {
                    Name = "idx_public_stores_tenant",
                    Background = true
                });

            await publicStoresCollection.Indexes.CreateManyAsync(
                new[] { publicStoresSlugIndex, publicStoresTenantIndex },
                cancellationToken);

            // ─── AF-11: Public Affiliate Products ─────────────────────────

            var publicProductsCollection = PublicAffiliateProducts;

            var publicProductsSlugIndex = new CreateIndexModel<PublicAffiliateProductDocument>(
                Builders<PublicAffiliateProductDocument>.IndexKeys
                    .Ascending(d => d.ProductSlug),
                new CreateIndexOptions
                {
                    Unique = true,
                    Name = "idx_public_products_slug_unique",
                    Background = true
                });

            var publicProductsCategoryIndex = new CreateIndexModel<PublicAffiliateProductDocument>(
                Builders<PublicAffiliateProductDocument>.IndexKeys
                    .Ascending(d => d.CategoryName),
                new CreateIndexOptions
                {
                    Name = "idx_public_products_category",
                    Background = true
                });

            var publicProductsActiveAuthIndex = new CreateIndexModel<PublicAffiliateProductDocument>(
                Builders<PublicAffiliateProductDocument>.IndexKeys
                    .Ascending(d => d.IsAffiliateEnabled)
                    .Ascending(d => d.IsActive),
                new CreateIndexOptions
                {
                    Name = "idx_public_products_flags",
                    Background = true
                });

            await publicProductsCollection.Indexes.CreateManyAsync(
                new[] { publicProductsSlugIndex, publicProductsCategoryIndex, publicProductsActiveAuthIndex },
                cancellationToken);

            // ─── AF-22: Affiliate Product Intelligence ─────────────────────────

            var productIntelligenceCollection = AffiliateProductIntelligence;

            // Index 1: Unique compound index on (ProductId, TenantId)
            var productIntelligenceUniqueIndex = new CreateIndexModel<AffiliateProductIntelligenceDocument>(
                Builders<AffiliateProductIntelligenceDocument>.IndexKeys
                    .Ascending(d => d.ProductId)
                    .Ascending(d => d.TenantId),
                new CreateIndexOptions
                {
                    Unique = true,
                    Name = "idx_product_tenant_unique",
                    Background = true
                });

            // Index 2: TenantId for tenant-scoped queries
            var productIntelligenceTenantIndex = new CreateIndexModel<AffiliateProductIntelligenceDocument>(
                Builders<AffiliateProductIntelligenceDocument>.IndexKeys
                    .Ascending(d => d.TenantId),
                new CreateIndexOptions
                {
                    Name = "idx_tenant",
                    Background = true
                });

            // Index 3: IsOutOfStock for filtering
            var productIntelligenceStockIndex = new CreateIndexModel<AffiliateProductIntelligenceDocument>(
                Builders<AffiliateProductIntelligenceDocument>.IndexKeys
                    .Ascending(d => d.IsOutOfStock),
                new CreateIndexOptions
                {
                    Name = "idx_out_of_stock",
                    Background = true
                });

            // Index 4: CommissionRate for filtering
            var productIntelligenceCommissionIndex = new CreateIndexModel<AffiliateProductIntelligenceDocument>(
                Builders<AffiliateProductIntelligenceDocument>.IndexKeys
                    .Ascending(d => d.CommissionRate),
                new CreateIndexOptions
                {
                    Name = "idx_commission_rate",
                    Background = true
                });

            // Index 5: Text index on ProductName for search
            var productIntelligenceTextIndex = new CreateIndexModel<AffiliateProductIntelligenceDocument>(
                Builders<AffiliateProductIntelligenceDocument>.IndexKeys
                    .Text(d => d.ProductName),
                new CreateIndexOptions
                {
                    Name = "idx_product_name_text",
                    Background = true
                });

            // AF-22 HARDENING: TTL index to prevent stale abandoned products (90 days)
            var productIntelligenceTtlIndex = new CreateIndexModel<AffiliateProductIntelligenceDocument>(
                Builders<AffiliateProductIntelligenceDocument>.IndexKeys
                    .Ascending(d => d.LastUpdatedUtc),
                new CreateIndexOptions
                {
                    ExpireAfter = TimeSpan.FromDays(90),
                    Name = "TTL_LastUpdatedUtc_90days",
                    Background = true
                });

            // AF-30A FIX P-3: Index on AffiliateCategoryId for category rename UpdateMany
            // and category-based product discovery queries.
            var productIntelligenceCategoryIdIndex = new CreateIndexModel<AffiliateProductIntelligenceDocument>(
                Builders<AffiliateProductIntelligenceDocument>.IndexKeys
                    .Ascending(d => d.AffiliateCategoryId),
                new CreateIndexOptions
                {
                    Name = "idx_affiliate_category_id",
                    Background = true,
                    Sparse = true // Only index documents with a category assigned
                });

            // AF-30A FIX P-3: Index on AffiliateCategorySlug for slug-based discovery queries.
            var productIntelligenceCategorySlugIndex = new CreateIndexModel<AffiliateProductIntelligenceDocument>(
                Builders<AffiliateProductIntelligenceDocument>.IndexKeys
                    .Ascending(d => d.AffiliateCategorySlug),
                new CreateIndexOptions
                {
                    Name = "idx_affiliate_category_slug",
                    Background = true,
                    Sparse = true
                });

            await productIntelligenceCollection.Indexes.CreateManyAsync(
                new[] { 
                    productIntelligenceUniqueIndex, 
                    productIntelligenceTenantIndex, 
                    productIntelligenceStockIndex, 
                    productIntelligenceCommissionIndex, 
                    productIntelligenceTextIndex,
                    productIntelligenceTtlIndex,
                    productIntelligenceCategoryIdIndex,
                    productIntelligenceCategorySlugIndex
                },
                cancellationToken);

            _logger.LogInformation(
                "MongoDB indexes created successfully | Collections: affiliate_dashboards, affiliate_orders, affiliate_earnings_summaries, affiliate_clicks, affiliate_analytics_summaries, public_affiliate_stores, public_affiliate_products, affiliate_product_intelligence");

            // ─── Abandoned Checkouts (Lost Orders) ──────────────────────
            _logger.LogInformation("Creating MongoDB indexes for Abandoned Checkouts collection...");

            var abandonedCheckoutCollection = AbandonedCheckouts;

            // Index 1: Unique (TenantId, CheckoutSessionId) — one document per session
            var acTenantSessionIndex = new CreateIndexModel<AbandonedCheckoutDocument>(
                Builders<AbandonedCheckoutDocument>.IndexKeys
                    .Ascending(d => d.TenantId)
                    .Ascending(d => d.CheckoutSessionId),
                new CreateIndexOptions
                {
                    Unique = true,
                    Name = "idx_tenant_session_unique",
                    Background = true
                });

            // Index 2: Admin dashboard listing (TenantId + Status + CreatedAtUtc DESC)
            var acDashboardIndex = new CreateIndexModel<AbandonedCheckoutDocument>(
                Builders<AbandonedCheckoutDocument>.IndexKeys
                    .Ascending(d => d.TenantId)
                    .Ascending(d => d.Status)
                    .Descending(d => d.CreatedAtUtc),
                new CreateIndexOptions
                {
                    Name = "idx_tenant_status_created",
                    Background = true
                });

            // Index 3: Cart lookup for conversion (TenantId + CartId)
            var acCartIndex = new CreateIndexModel<AbandonedCheckoutDocument>(
                Builders<AbandonedCheckoutDocument>.IndexKeys
                    .Ascending(d => d.TenantId)
                    .Ascending(d => d.CartId),
                new CreateIndexOptions
                {
                    Name = "idx_tenant_cart",
                    Background = true
                });

            // Index 4: TTL — auto-delete after 90 days to prevent unbounded growth
            var acTtlIndex = new CreateIndexModel<AbandonedCheckoutDocument>(
                Builders<AbandonedCheckoutDocument>.IndexKeys
                    .Ascending(d => d.CreatedAtUtc),
                new CreateIndexOptions
                {
                    ExpireAfter = TimeSpan.FromDays(90),
                    Name = "TTL_CreatedAtUtc_90days",
                    Background = true
                });

            try
            {
                await abandonedCheckoutCollection.Indexes.CreateManyAsync(
                    new[] { acTenantSessionIndex, acDashboardIndex, acCartIndex, acTtlIndex },
                    cancellationToken);

                _logger.LogInformation(
                    "MongoDB indexes created successfully | Collection: abandoned_checkouts | TTL: 90 days");
            }
            catch (MongoCommandException mex) when (mex.Message.Contains("already exists"))
            {
                _logger.LogDebug("AbandonedCheckouts indexes already exist");
            }

            // --- Processed Events Collection Indexes (Idempotency Inbox) ---
            _logger.LogInformation("Creating MongoDB indexes for Processed Events collection...");

            var processedEventsCollection = ProcessedEvents;
            var processedEventsIndexBuilder = Builders<ProcessedEventDocument>.IndexKeys;

            // Index 1: Composite Id ({ConsumerName}:{EventId}) is the primary key (unique)
            // Already handled by [BsonId] attribute on the Id property

            // Index 2: TTL Index (CRITICAL) - Auto-delete events after 30 days
            // Prevents unbounded growth of the idempotency inbox
            var processedEventsTtlIndex = new CreateIndexModel<ProcessedEventDocument>(
                processedEventsIndexBuilder.Ascending(x => x.ProcessedAtUtc),
                new CreateIndexOptions
                {
                    ExpireAfter = TimeSpan.FromDays(30), // 30-day retention
                    Name = "TTL_ProcessedAtUtc_30days",
                    Background = true
                }
            );

            // Index 3: Consumer + TenantId for debugging/monitoring
            var processedEventsConsumerIndex = new CreateIndexModel<ProcessedEventDocument>(
                processedEventsIndexBuilder
                    .Ascending(x => x.ConsumerName)
                    .Ascending(x => x.TenantId)
                    .Descending(x => x.ProcessedAtUtc),
                new CreateIndexOptions
                {
                    Name = "idx_consumer_tenant_processed",
                    Background = true
                }
            );

            try
            {
                await processedEventsCollection.Indexes.CreateManyAsync(
                    new[] { processedEventsTtlIndex, processedEventsConsumerIndex },
                    cancellationToken);

                _logger.LogInformation(
                    "MongoDB indexes created successfully | Collection: processed_events | TTL: 30 days");
            }
            catch (MongoCommandException mex) when (mex.Message.Contains("already exists"))
            {
                _logger.LogDebug("ProcessedEvents indexes already exist");
            }

            // --- Coupons Collection Indexes ---
            _logger.LogInformation("Creating MongoDB indexes for Coupons collection...");

            var couponsCollection = _database.GetCollection<CouponDocument>("coupons");

            // Index 1: Unique compound index on (TenantId, CouponId)
            // Ensures no duplicate coupons per tenant and enables efficient lookups
            var couponTenantCouponIndex = new CreateIndexModel<CouponDocument>(
                Builders<CouponDocument>.IndexKeys
                    .Ascending(c => c.TenantId)
                    .Ascending(c => c.CouponId),
                new CreateIndexOptions
                {
                    Unique = true,
                    Name = "idx_tenant_coupon_unique",
                    Background = true
                });

            try
            {
                await couponsCollection.Indexes.CreateManyAsync(
                    new[] { couponTenantCouponIndex },
                    cancellationToken);

                _logger.LogInformation(
                    "MongoDB indexes created successfully | Collection: coupons");
            }
            catch (MongoCommandException mex) when (
                mex.Message.Contains("already exists") ||
                mex.Code == 85 || // IndexOptionsConflict
                mex.CodeName == "IndexOptionsConflict")
            {
                _logger.LogDebug("Coupons indexes already exist");
            }

            // --- Promotions Collection Indexes ---
            _logger.LogInformation("Creating MongoDB indexes for Promotions collection...");

            var promotionsCollection = _database.GetCollection<PromotionDocument>("promotions");

            // Index 1: Unique compound index on (TenantId, PromotionId)
            // Ensures no duplicate promotions per tenant and prevents duplicate projection entries
            var promotionTenantPromotionIndex = new CreateIndexModel<PromotionDocument>(
                Builders<PromotionDocument>.IndexKeys
                    .Ascending(p => p.TenantId)
                    .Ascending(p => p.PromotionId),
                new CreateIndexOptions
                {
                    Unique = true,
                    Name = "idx_tenant_promotion_unique",
                    Background = true
                });

            try
            {
                await promotionsCollection.Indexes.CreateManyAsync(
                    new[] { promotionTenantPromotionIndex },
                    cancellationToken);

                _logger.LogInformation(
                    "MongoDB indexes created successfully | Collection: promotions");
            }
            catch (MongoCommandException mex) when (
                mex.Message.Contains("already exists") ||
                mex.Code == 85 || // IndexOptionsConflict
                mex.CodeName == "IndexOptionsConflict")
            {
                _logger.LogDebug("Promotions indexes already exist");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, 
                "Failed to create MongoDB indexes");
            
            // Reset flag so we can retry on next startup
            _indexesCreated = false;
            throw;
        }
    }

    /// <summary>
    /// Deduplicates StorefrontConfigDocument by TenantId before creating unique index.
    /// Keeps the most recently updated document per tenant and deletes older duplicates.
    /// Phase: Abandoned Cart Hardening - Index Creation Safety
    /// </summary>
    private async Task DeduplicateStorefrontConfigsAsync(
        IMongoCollection<StorefrontConfigDocument> collection,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("[MongoDB] Checking for duplicate TenantId in storefront_configs...");

            // Aggregation pipeline to find TenantIds with multiple documents
            var pipeline = new[]
            {
                new BsonDocument("$group", new BsonDocument
                {
                    { "_id", "$TenantId" },
                    { "count", new BsonDocument("$sum", 1) },
                    { "docs", new BsonDocument("$push", new BsonDocument
                        {
                            { "id", "$_id" },
                            { "updatedAt", "$UpdatedAt" }
                        })
                    }
                }),
                new BsonDocument("$match", new BsonDocument("count", new BsonDocument("$gt", 1)))
            };

            var duplicates = await collection.Aggregate<BsonDocument>(pipeline, cancellationToken: cancellationToken)
                .ToListAsync(cancellationToken);

            if (duplicates.Count == 0)
            {
                _logger.LogInformation("[MongoDB] No duplicate TenantId found in storefront_configs");
                return;
            }

            _logger.LogWarning(
                "[MongoDB] Found {Count} TenantIds with duplicate documents in storefront_configs",
                duplicates.Count);

            var totalDeleted = 0;

            foreach (var duplicate in duplicates)
            {
                var tenantId = duplicate["_id"].AsGuid;
                var docs = duplicate["docs"].AsBsonArray;

                _logger.LogWarning(
                    "[MongoDB] TenantId {TenantId} has {Count} duplicate documents",
                    tenantId, docs.Count);

                // Sort by UpdatedAt descending to keep the most recent
                // Guard against missing/null BSON fields
                var sortedDocs = docs
                    .Select(d =>
                    {
                        var idValue = d.AsBsonDocument.GetValue("id", BsonNull.Value);
                        var updatedAtValue = d.AsBsonDocument.GetValue("updatedAt", BsonNull.Value);
                        
                        return new
                        {
                            Id = idValue.IsBsonNull ? null : idValue.AsString,
                            UpdatedAt = updatedAtValue.IsBsonNull || !updatedAtValue.IsBsonDateTime 
                                ? (DateTime?)null 
                                : updatedAtValue.ToUniversalTime()
                        };
                    })
                    .Where(d => d.Id != null) // Skip documents with missing id
                    .OrderByDescending(d => d.UpdatedAt ?? DateTime.MinValue) // Treat null as oldest
                    .ToList();

                if (sortedDocs.Count == 0)
                {
                    _logger.LogWarning(
                        "[MongoDB] All documents for TenantId {TenantId} have missing/invalid id field, skipping",
                        tenantId);
                    continue;
                }

                // Keep the first (most recent), delete the rest
                var toKeep = sortedDocs.First();
                var toDelete = sortedDocs.Skip(1).ToList();

                _logger.LogInformation(
                    "[MongoDB] Keeping document {Id} (UpdatedAt: {UpdatedAt}) for TenantId {TenantId}",
                    toKeep.Id, toKeep.UpdatedAt, tenantId);

                foreach (var doc in toDelete)
                {
                    var filter = Builders<StorefrontConfigDocument>.Filter.Eq(d => d.Id, doc.Id);
                    var result = await collection.DeleteOneAsync(filter, cancellationToken);

                    if (result.DeletedCount > 0)
                    {
                        totalDeleted++;
                        _logger.LogInformation(
                            "[MongoDB] Deleted duplicate document {Id} (UpdatedAt: {UpdatedAt}) for TenantId {TenantId}",
                            doc.Id, doc.UpdatedAt, tenantId);
                    }
                }
            }

            _logger.LogInformation(
                "[MongoDB] Deduplication complete | Deleted {Count} duplicate documents",
                totalDeleted);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "[MongoDB] Failed to deduplicate storefront_configs | Error: {Message}",
                ex.Message);

            // Don't throw - allow index creation to proceed and fail with clear error if duplicates remain
            // This gives operators visibility into the issue
        }
    }

    /// <summary>
    /// Mask sensitive parts of connection string for logging
    /// </summary>
    private static string MaskConnectionString(string connectionString)
    {
        // Basic masking - in production, use proper URI parsing
        if (connectionString.Contains("@"))
        {
            var atIndex = connectionString.IndexOf("@");
            return "mongodb://***:***" + connectionString[atIndex..];
        }
        return connectionString;
    }
}
