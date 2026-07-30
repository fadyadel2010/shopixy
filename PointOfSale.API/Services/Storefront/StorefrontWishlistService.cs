using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Events;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Elasticsearch.Services;
using PointOfSale.Infrastructure.MongoDB.Documents;
using StackExchange.Redis;
using System.Text.Json;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.API.Services.Storefront;

/// <summary>
/// Service interface for wishlist operations.
/// </summary>
public interface IStorefrontWishlistService
{
    /// <summary>
    /// Add a product to customer's wishlist.
    /// Idempotent - adding same product twice is OK.
    /// </summary>
    Task<WishlistResult> AddToWishlistAsync(Guid tenantId, Guid customerId, Guid productId, CancellationToken cancellationToken);

    /// <summary>
    /// Remove a product from customer's wishlist.
    /// </summary>
    Task<WishlistResult> RemoveFromWishlistAsync(Guid tenantId, Guid customerId, Guid productId, CancellationToken cancellationToken);

    /// <summary>
    /// Clear all items from customer's wishlist.
    /// </summary>
    Task<WishlistResult> ClearWishlistAsync(Guid tenantId, Guid customerId, CancellationToken cancellationToken);

    /// <summary>
    /// Get customer's wishlist (from MongoDB with Redis cache).
    /// </summary>
    Task<WishlistDto> GetWishlistAsync(Guid tenantId, Guid customerId, CancellationToken cancellationToken);

    /// <summary>
    /// Get wishlist count (for header badge).
    /// </summary>
    Task<int> GetWishlistCountAsync(Guid tenantId, Guid customerId, CancellationToken cancellationToken);

    /// <summary>
    /// Check if a product is in customer's wishlist.
    /// </summary>
    Task<bool> IsInWishlistAsync(Guid tenantId, Guid customerId, Guid productId, CancellationToken cancellationToken);

    /// <summary>
    /// Get wishlist for admin view (read-only).
    /// </summary>
    Task<AdminWishlistDto> GetWishlistForAdminAsync(Guid tenantId, Guid customerId, CancellationToken cancellationToken);

    /// <summary>
    /// Get wishlist analytics for admin dashboard.
    /// Phase 10.3.6.2 - Wishlist Analytics
    /// </summary>
    Task<WishlistAnalyticsDto> GetWishlistAnalyticsAsync(Guid tenantId, CancellationToken cancellationToken);
}


/// <summary>
/// Result object for wishlist operations.
/// </summary>
public class WishlistResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? ErrorCode { get; set; }
    public int NewCount { get; set; }
}

/// <summary>
/// DTO for wishlist items (includes product IDs only).
/// Product details should be fetched separately from catalog.
/// </summary>
public class WishlistDto
{
    public List<WishlistItemDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
}

/// <summary>
/// DTO for a single wishlist item.
/// </summary>
public class WishlistItemDto
{
    public Guid ProductId { get; set; }
    public DateTime AddedAtUtc { get; set; }
    
    // Product details (optional - populated for admin view)
    public string? ProductName { get; set; }
    public string? ProductImage { get; set; }
    public string? ProductSku { get; set; }
    public decimal? ProductPrice { get; set; }
    public bool ProductDeleted { get; set; } = false;
}

/// <summary>
/// Admin view of wishlist (read-only).
/// </summary>
public class AdminWishlistDto
{
    public Guid CustomerId { get; set; }
    public List<WishlistItemDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public DateTime? LastUpdatedAtUtc { get; set; }
}

/// <summary>
/// Wishlist analytics for admin dashboard.
/// Phase 10.3.6.2 - Wishlist Analytics
/// </summary>
public class WishlistAnalyticsDto
{
    /// <summary>Total customers with at least one wishlist item.</summary>
    public int TotalCustomersWithWishlists { get; set; }
    
    /// <summary>Total wishlist items across all customers.</summary>
    public int TotalWishlistItems { get; set; }
    
    /// <summary>Average items per wishlist.</summary>
    public double AverageItemsPerWishlist { get; set; }
    
    /// <summary>Top wishlisted products (most popular).</summary>
    public List<PopularWishlistProductDto> TopProducts { get; set; } = new();
    
    /// <summary>Recent wishlist activity (last 7 days).</summary>
    public WishlistActivityDto RecentActivity { get; set; } = new();
    
    /// <summary>When the analytics were generated.</summary>
    public DateTime GeneratedAtUtc { get; set; }
}

/// <summary>
/// Popular product in wishlists.
/// </summary>
public class PopularWishlistProductDto
{
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public string? ProductNameAr { get; set; }
    public decimal Price { get; set; }
    public string Currency { get; set; } = "";
    public string? ThumbnailImage { get; set; }
    public int WishlistCount { get; set; }
    public int Rank { get; set; }
}

/// <summary>
/// Wishlist activity stats.
/// </summary>
public class WishlistActivityDto
{
    public int ItemsAddedLast7Days { get; set; }
    public int ItemsRemovedLast7Days { get; set; }
    public int NewWishlistsLast7Days { get; set; }
}

/// <summary>
/// Storefront Wishlist Service Implementation.
/// 
/// ARCHITECTURAL RULES:
/// - Writes go to PostgreSQL (source of truth)
/// - Emits domain events for MongoDB projection
/// - Reads from MongoDB with Redis cache
/// - Idempotent operations (safe to retry)
/// - Product existence validated via catalog
/// 
/// Phase: 10.3.6 - Wishlist (Production-grade)
/// </summary>
public class StorefrontWishlistService : IStorefrontWishlistService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IMongoCollection<OnlineCustomerWishlistDocument> _wishlistCollection;
    private readonly IMongoCollection<ProductDocument> _productCollection;
    private readonly IConnectionMultiplexer _redis;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<StorefrontWishlistService> _logger;

    private const string CacheKeyPrefix = "storefront:tenant:{0}:wishlist:{1}";
    private const string CountCacheKeyPrefix = "storefront:tenant:{0}:wishlist:count:{1}";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);

    public StorefrontWishlistService(
        ApplicationDbContext dbContext,
        IMongoDatabase mongoDatabase,
        IConnectionMultiplexer redis,
        IEventPublisher eventPublisher,
        ILogger<StorefrontWishlistService> logger)
    {
        _dbContext = dbContext;
        _wishlistCollection = mongoDatabase.GetCollection<OnlineCustomerWishlistDocument>("online_customer_wishlists");
        _productCollection = mongoDatabase.GetCollection<ProductDocument>("products");
        _redis = redis;
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    /// <summary>
    /// Maximum number of items allowed in a wishlist per customer.
    /// Prevents document bloat and ensures scalable cart operations.
    /// </summary>
    private const int MaxWishlistItems = 500;

    public async Task<WishlistResult> AddToWishlistAsync(
        Guid tenantId, 
        Guid customerId, 
        Guid productId, 
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "[Wishlist] Adding to wishlist | TenantId: {TenantId} | CustomerId: {CustomerId} | ProductId: {ProductId}",
            tenantId, customerId, productId);

        // Validate product through the same storefront purchasability authority used by catalog/PDP/cart.
        var product = await LoadCurrentStorefrontProductAuthorityAsync(
            tenantId,
            productId,
            cancellationToken);
        var productPurchasability = product == null
            ? null
            : ProductStorefrontVisibilityAuthority.Evaluate(product);

        if (productPurchasability is not { IsAvailable: true })
        {
            _logger.LogWarning(
                "[Wishlist] Product not admitted by storefront authority | TenantId: {TenantId} | ProductId: {ProductId} | Reason: {Reason}",
                tenantId, productId, productPurchasability?.UnavailableReason ?? "PRODUCT_NOT_FOUND");
            return new WishlistResult
            {
                Success = false,
                Error = "Product not found or not available",
                ErrorCode = "PRODUCT_NOT_FOUND"
            };
        }

        // SECURITY: IgnoreQueryFilters is intentional so soft-deleted wishlist
        // rows can be reactivated safely, while TenantId and OnlineCustomerId
        // are explicitly reapplied.
        var existingItem = await _dbContext.OnlineCustomerWishlistItems
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(w =>
                w.TenantId == tenantId &&
                w.OnlineCustomerId == customerId &&
                w.ProductId == productId &&
                !w.IsRemoved,
            cancellationToken);

        if (existingItem != null)
        {
            // Already in wishlist - idempotent success
            var currentCount = await GetWishlistCountFromDbAsync(tenantId, customerId, cancellationToken);
            return new WishlistResult
            {
                Success = true,
                NewCount = currentCount
            };
        }

        // SECURITY: IgnoreQueryFilters is intentional so previously removed
        // wishlist rows remain visible for reactivation, while tenant/customer
        // scoping is reapplied in the predicate.
        var removedItem = await _dbContext.OnlineCustomerWishlistItems
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(w =>
                w.TenantId == tenantId &&
                w.OnlineCustomerId == customerId &&
                w.ProductId == productId &&
                w.IsRemoved,
            cancellationToken);

        // Track the actual entity ID for the domain event
        Guid wishlistItemId;

        if (removedItem != null)
        {
            // Reactivate the removed item
            removedItem.IsRemoved = false;
            removedItem.RemovedAtUtc = null;
            removedItem.CreatedAtUtc = DateTime.UtcNow; // Reset timestamp
            wishlistItemId = removedItem.Id;
        }
        else
        {
            // HARDENING: Check wishlist size limit before adding new item
            var currentItemCount = await GetWishlistCountFromDbAsync(tenantId, customerId, cancellationToken);
            if (currentItemCount >= MaxWishlistItems)
            {
                _logger.LogWarning(
                    "[Wishlist] Wishlist full | TenantId: {TenantId} | CustomerId: {CustomerId} | CurrentCount: {CurrentCount} | MaxItems: {MaxItems}",
                    tenantId, customerId, currentItemCount, MaxWishlistItems);
                return new WishlistResult
                {
                    Success = false,
                    Error = $"Wishlist is full. Maximum {MaxWishlistItems} items allowed.",
                    ErrorCode = "WISHLIST_FULL",
                    NewCount = currentItemCount
                };
            }

            // Create new wishlist item
            var newItem = new OnlineCustomerWishlistItem
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                OnlineCustomerId = customerId,
                ProductId = productId,
                CreatedAtUtc = DateTime.UtcNow,
                IsRemoved = false
            };

            _dbContext.OnlineCustomerWishlistItems.Add(newItem);
            wishlistItemId = newItem.Id;
        }

        // PHASE 10.3.6 FIX: Event-Driven Projection Integrity
        // CRITICAL: Publish event BEFORE SaveChangesAsync to use EF Core Outbox
        // This ensures events are stored in the outbox table within the same transaction
        // and delivered reliably to RabbitMQ after commit
        var addedAtUtc = DateTime.UtcNow;
        await _eventPublisher.PublishAsync(new OnlineCustomerWishlistItemAddedEvent
        {
            TenantId = tenantId,
            WishlistItemId = wishlistItemId,
            CustomerId = customerId,
            ProductId = productId,
            AddedAtUtc = addedAtUtc
        }, cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);

        // Invalidate cache after successful write
        // The projection consumer will update MongoDB, but we invalidate cache proactively
        await InvalidateWishlistCacheAsync(tenantId, customerId);

        var newCount = await GetWishlistCountFromDbAsync(tenantId, customerId, cancellationToken);

        _logger.LogInformation(
            "[Wishlist] Added to wishlist | TenantId: {TenantId} | CustomerId: {CustomerId} | ProductId: {ProductId} | WishlistItemId: {WishlistItemId} | NewCount: {NewCount}",
            tenantId, customerId, productId, wishlistItemId, newCount);

        return new WishlistResult
        {
            Success = true,
            NewCount = newCount
        };
    }

    public async Task<WishlistResult> RemoveFromWishlistAsync(
        Guid tenantId,
        Guid customerId,
        Guid productId,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "[Wishlist] Removing from wishlist | TenantId: {TenantId} | CustomerId: {CustomerId} | ProductId: {ProductId}",
            tenantId, customerId, productId);

        // SECURITY: IgnoreQueryFilters is intentional so soft-deleted rows can
        // be found for idempotent removal, while tenant/customer predicates are
        // explicitly reapplied.
        var item = await _dbContext.OnlineCustomerWishlistItems
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(w =>
                w.TenantId == tenantId &&
                w.OnlineCustomerId == customerId &&
                w.ProductId == productId &&
                !w.IsRemoved,
            cancellationToken);

        if (item == null)
        {
            // Not in wishlist - idempotent success
            var currentCount = await GetWishlistCountFromDbAsync(tenantId, customerId, cancellationToken);
            return new WishlistResult
            {
                Success = true,
                NewCount = currentCount
            };
        }

        // Soft delete
        item.IsRemoved = true;
        var removedAtUtc = DateTime.UtcNow;
        item.RemovedAtUtc = removedAtUtc;

        // PHASE 10.3.6 FIX: Event-Driven Projection Integrity
        // CRITICAL: Publish event BEFORE SaveChangesAsync to use EF Core Outbox
        await _eventPublisher.PublishAsync(new OnlineCustomerWishlistItemRemovedEvent
        {
            TenantId = tenantId,
            CustomerId = customerId,
            ProductId = productId,
            RemovedAtUtc = removedAtUtc
        }, cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);

        // Invalidate cache after successful write
        await InvalidateWishlistCacheAsync(tenantId, customerId);

        var newCount = await GetWishlistCountFromDbAsync(tenantId, customerId, cancellationToken);

        _logger.LogInformation(
            "[Wishlist] Removed from wishlist | TenantId: {TenantId} | CustomerId: {CustomerId} | ProductId: {ProductId} | NewCount: {NewCount}",
            tenantId, customerId, productId, newCount);

        return new WishlistResult
        {
            Success = true,
            NewCount = newCount
        };
    }

    public async Task<WishlistResult> ClearWishlistAsync(
        Guid tenantId,
        Guid customerId,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "[Wishlist] Clearing wishlist | TenantId: {TenantId} | CustomerId: {CustomerId}",
            tenantId, customerId);

        // SECURITY: IgnoreQueryFilters is intentional so clear operations see
        // the full wishlist row set, while tenant/customer scoping remains
        // explicit in the filter.
        var items = await _dbContext.OnlineCustomerWishlistItems
            .IgnoreQueryFilters()
            .Where(w =>
                w.TenantId == tenantId &&
                w.OnlineCustomerId == customerId &&
                !w.IsRemoved)
            .ToListAsync(cancellationToken);

        var itemsCount = items.Count;

        if (itemsCount == 0)
        {
            return new WishlistResult
            {
                Success = true,
                NewCount = 0
            };
        }

        // Soft delete all items
        var now = DateTime.UtcNow;
        foreach (var item in items)
        {
            item.IsRemoved = true;
            item.RemovedAtUtc = now;
        }

        // PHASE 10.3.6 FIX: Event-Driven Projection Integrity
        // CRITICAL: Publish event BEFORE SaveChangesAsync to use EF Core Outbox
        await _eventPublisher.PublishAsync(new OnlineCustomerWishlistClearedEvent
        {
            TenantId = tenantId,
            CustomerId = customerId,
            ItemsCleared = itemsCount,
            ClearedAtUtc = now
        }, cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);

        // Invalidate cache after successful write
        await InvalidateWishlistCacheAsync(tenantId, customerId);

        _logger.LogInformation(
            "[Wishlist] Cleared wishlist | TenantId: {TenantId} | CustomerId: {CustomerId} | ItemsCleared: {ItemsCleared}",
            tenantId, customerId, itemsCount);

        return new WishlistResult
        {
            Success = true,
            NewCount = 0
        };
    }

    public async Task<WishlistDto> GetWishlistAsync(
        Guid tenantId,
        Guid customerId,
        CancellationToken cancellationToken)
    {
        // Try Redis cache first
        var db = _redis.GetDatabase();
        var cacheKey = string.Format(CacheKeyPrefix, tenantId, customerId);

        try
        {
            var cached = await db.StringGetAsync(cacheKey);
            if (cached.HasValue)
            {
                var cachedDto = JsonSerializer.Deserialize<WishlistDto>(cached!);
                if (cachedDto != null)
                {
                    _logger.LogDebug("[Wishlist] Cache hit | TenantId: {TenantId} | CustomerId: {CustomerId}", tenantId, customerId);
                    return cachedDto;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Wishlist] Cache read failed | TenantId: {TenantId} | CustomerId: {CustomerId}", tenantId, customerId);
        }

        // Check MongoDB first
        var doc = await _wishlistCollection
            .Find(w => w.TenantId == tenantId && w.CustomerId == customerId)
            .FirstOrDefaultAsync(cancellationToken);

        WishlistDto result;

        if (doc != null)
        {
            // MongoDB hit
            result = new WishlistDto
            {
                Items = doc.Items
                    .Where(i => !i.IsRemoved)
                    .Select(i => new WishlistItemDto
                    {
                        ProductId = i.ProductId,
                        AddedAtUtc = i.AddedAtUtc
                    })
                    .ToList(),
                TotalCount = doc.Items.Count(i => !i.IsRemoved)
            };
        }
        else
        {
            // MongoDB miss - fallback to SQL (Write Model)
            // This handles projection lag and ensures "Read Your Own Writes"
            _logger.LogInformation("[Wishlist] MongoDB miss - falling back to SQL | TenantId: {TenantId} | CustomerId: {CustomerId}", tenantId, customerId);

            // SECURITY: IgnoreQueryFilters is intentional so SQL fallback reads
            // can tolerate soft-deleted rows, while TenantId and
            // OnlineCustomerId are explicitly reapplied.
            var sqlItems = await _dbContext.OnlineCustomerWishlistItems
                .IgnoreQueryFilters()
                .Where(w => w.TenantId == tenantId && w.OnlineCustomerId == customerId && !w.IsRemoved)
                .OrderByDescending(w => w.CreatedAtUtc)
                .Select(w => new WishlistItemDto 
                { 
                    ProductId = w.ProductId, 
                    AddedAtUtc = w.CreatedAtUtc 
                })
                .ToListAsync(cancellationToken);

            result = new WishlistDto
            {
                Items = sqlItems,
                TotalCount = sqlItems.Count
            };
        }

        // Cache the result
        try
        {
            var json = JsonSerializer.Serialize(result);
            await db.StringSetAsync(cacheKey, json, CacheTtl);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Wishlist] Cache write failed | TenantId: {TenantId} | CustomerId: {CustomerId}", tenantId, customerId);
        }

        return result;
    }

    public async Task<int> GetWishlistCountAsync(
        Guid tenantId,
        Guid customerId,
        CancellationToken cancellationToken)
    {
        // Try Redis cache first
        var db = _redis.GetDatabase();
        var cacheKey = string.Format(CountCacheKeyPrefix, tenantId, customerId);

        try
        {
            var cached = await db.StringGetAsync(cacheKey);
            if (cached.HasValue && int.TryParse(cached, out var cachedCount))
            {
                return cachedCount;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Wishlist] Count cache read failed | TenantId: {TenantId} | CustomerId: {CustomerId}", tenantId, customerId);
        }

        // Fallback to MongoDB
        var doc = await _wishlistCollection
            .Find(w => w.TenantId == tenantId && w.CustomerId == customerId)
            .FirstOrDefaultAsync(cancellationToken);

        int count;
        if (doc != null)
        {
            // PHASE 10.3.6 FIX: Recalculate count from active items only
            // This ensures consistency with GetWishlistAsync and prevents stale counts
            // when removed items haven't been filtered from the projection
            count = doc.Items.Count(i => !i.IsRemoved);
        }
        else
        {
            // MongoDB miss - fallback to SQL
            count = await GetWishlistCountFromDbAsync(tenantId, customerId, cancellationToken);
        }

        // Cache the result
        try
        {
            await db.StringSetAsync(cacheKey, count.ToString(), CacheTtl);
        }
        catch { /* Non-critical */ }

        return count;
    }

    public async Task<bool> IsInWishlistAsync(
        Guid tenantId,
        Guid customerId,
        Guid productId,
        CancellationToken cancellationToken)
    {
        var doc = await _wishlistCollection
            .Find(w => w.TenantId == tenantId && w.CustomerId == customerId)
            .FirstOrDefaultAsync(cancellationToken);

        return doc?.Items.Any(i => i.ProductId == productId && !i.IsRemoved) ?? false;
    }

    public async Task<AdminWishlistDto> GetWishlistForAdminAsync(
        Guid tenantId,
        Guid customerId,
        CancellationToken cancellationToken)
    {
        var doc = await _wishlistCollection
            .Find(w => w.TenantId == tenantId && w.CustomerId == customerId)
            .FirstOrDefaultAsync(cancellationToken);

        // If MongoDB is empty, fallback to PostgreSQL (source of truth)
        if (doc == null || doc.Items.All(i => i.IsRemoved))
        {
            _logger.LogInformation(
                "[Wishlist] Admin view - MongoDB empty, falling back to SQL | TenantId: {TenantId} | CustomerId: {CustomerId}",
                tenantId, customerId);
            
            return await GetWishlistFromSqlAsync(tenantId, customerId, cancellationToken);
        }

        // Fetch product details from catalog (MongoDB)
        var activeItems = doc.Items.Where(i => !i.IsRemoved).ToList();
        var productIds = activeItems.Select(i => i.ProductId).ToList();
        var products = await _productCollection
            .Find(p => p.TenantId == tenantId && productIds.Contains(p.ProductId))
            .ToListAsync(cancellationToken);

        var productMap = products.ToDictionary(p => p.ProductId);

        // Map wishlist items with product details
        var items = activeItems.Select(i =>
        {
            productMap.TryGetValue(i.ProductId, out var product);
            return new WishlistItemDto
            {
                ProductId = i.ProductId,
                AddedAtUtc = i.AddedAtUtc,
                ProductName = product?.Name ?? "Unknown Product",
                ProductImage = ExtractThumbnailImage(product),
                ProductSku = product?.SKU,
                ProductPrice = ResolveWishlistProductPrice(product),
                ProductDeleted = IsWishlistProductUnavailable(product)
            };
        }).ToList();

        return new AdminWishlistDto
        {
            CustomerId = customerId,
            Items = items,
            TotalCount = activeItems.Count,
            LastUpdatedAtUtc = doc.UpdatedAtUtc
        };
    }

    /// <summary>
    /// Fallback method to get wishlist from PostgreSQL when MongoDB is empty.
    /// Used for admin view when projections haven't synced.
    /// </summary>
    private async Task<AdminWishlistDto> GetWishlistFromSqlAsync(
        Guid tenantId,
        Guid customerId,
        CancellationToken cancellationToken)
    {
        // Get wishlist items from PostgreSQL
        // SECURITY: IgnoreQueryFilters is intentional so admin fallback reads
        // can see the authoritative wishlist rows, while TenantId and
        // OnlineCustomerId are explicitly reapplied.
        var wishlistItems = await _dbContext.OnlineCustomerWishlistItems
            .IgnoreQueryFilters()
            .Where(w => w.TenantId == tenantId && 
                        w.OnlineCustomerId == customerId && 
                        !w.IsRemoved)
            .OrderByDescending(w => w.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        if (!wishlistItems.Any())
        {
            return new AdminWishlistDto
            {
                CustomerId = customerId,
                Items = new List<WishlistItemDto>(),
                TotalCount = 0,
                LastUpdatedAtUtc = null
            };
        }

        // Fetch product details from MongoDB catalog
        var productIds = wishlistItems.Select(w => w.ProductId).ToList();
        var products = await _productCollection
            .Find(p => p.TenantId == tenantId && productIds.Contains(p.ProductId))
            .ToListAsync(cancellationToken);

        var productMap = products.ToDictionary(p => p.ProductId);

        // Map wishlist items with product details
        var items = wishlistItems.Select(w =>
        {
            productMap.TryGetValue(w.ProductId, out var product);
            return new WishlistItemDto
            {
                ProductId = w.ProductId,
                AddedAtUtc = w.CreatedAtUtc,
                ProductName = product?.Name ?? "Unknown Product",
                ProductImage = ExtractThumbnailImage(product),
                ProductSku = product?.SKU,
                ProductPrice = ResolveWishlistProductPrice(product),
                ProductDeleted = IsWishlistProductUnavailable(product)
            };
        }).ToList();

        return new AdminWishlistDto
        {
            CustomerId = customerId,
            Items = items,
            TotalCount = wishlistItems.Count,
            LastUpdatedAtUtc = wishlistItems.Max(w => w.CreatedAtUtc)
        };
    }

    private async Task<int> GetWishlistCountFromDbAsync(
        Guid tenantId,
        Guid customerId,
        CancellationToken cancellationToken)
    {
        // SECURITY: IgnoreQueryFilters is intentional so the source-of-truth
        // wishlist count includes rows hidden by global filters, while
        // TenantId and OnlineCustomerId are explicitly reapplied.
        return await _dbContext.OnlineCustomerWishlistItems
            .IgnoreQueryFilters()
            .CountAsync(w =>
                w.TenantId == tenantId &&
                w.OnlineCustomerId == customerId &&
                !w.IsRemoved,
            cancellationToken);
    }

    /// <summary>
    /// Get wishlist analytics for admin dashboard.
    /// Queries PostgreSQL for accurate analytics data.
    /// </summary>
    public async Task<WishlistAnalyticsDto> GetWishlistAnalyticsAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("[Wishlist Analytics] Generating analytics | TenantId: {TenantId}", tenantId);

        var sevenDaysAgo = DateTime.UtcNow.AddDays(-7);

        // SECURITY: IgnoreQueryFilters is intentional so analytics reads can use
        // the full row set, while the tenant boundary is explicitly reapplied.
        var activeItemsQuery = _dbContext.OnlineCustomerWishlistItems
            .IgnoreQueryFilters()
            .Where(w => w.TenantId == tenantId && !w.IsRemoved);

        // Keep analytics in SQL so a large tenant cannot force the server to
        // materialize the entire wishlist table into memory.
        var totalItems = await activeItemsQuery.CountAsync(cancellationToken);
        var uniqueCustomers = await activeItemsQuery
            .Select(w => w.OnlineCustomerId)
            .Distinct()
            .CountAsync(cancellationToken);
        var avgItemsPerWishlist = uniqueCustomers > 0 ? (double)totalItems / uniqueCustomers : 0;

        // Top wishlisted products (aggregate by product ID)
        var productCounts = await activeItemsQuery
            .GroupBy(w => w.ProductId)
            .Select(g => new { ProductId = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(10)
            .ToListAsync(cancellationToken);

        // Fetch product details from MongoDB for top products
        var topProductIds = productCounts.Select(p => p.ProductId).ToList();
        var productDetails = new Dictionary<Guid, ProductDocument>();
        
        if (topProductIds.Any())
        {
            var products = await _productCollection
                .Find(p => p.TenantId == tenantId && topProductIds.Contains(p.ProductId))
                .ToListAsync(cancellationToken);
            
            productDetails = products.ToDictionary(p => p.ProductId);
        }

        var tenantCurrency = await _dbContext.Tenants
            .IgnoreQueryFilters()
            .Where(t => t.Id == tenantId)
            .Select(t => t.Currency ?? "")
            .FirstOrDefaultAsync(cancellationToken) ?? "";

        var topProducts = productCounts.Select((p, index) =>
        {
            productDetails.TryGetValue(p.ProductId, out var product);
            var thumbnailImage = ExtractThumbnailImage(product);
            return new PopularWishlistProductDto
            {
                ProductId = p.ProductId,
                ProductName = product?.Name ?? "Unknown Product",
                ProductNameAr = product?.NameAr,
                Price = ResolveWishlistProductPrice(product) ?? 0,
                Currency = tenantCurrency,
                ThumbnailImage = thumbnailImage,
                WishlistCount = p.Count,
                Rank = index + 1
            };
        }).ToList();

        // Recent activity (last 7 days)
        // SECURITY: IgnoreQueryFilters is intentional so analytics includes the
        // full wishlist history, while TenantId is explicitly reapplied.
        var itemsAddedLast7Days = await _dbContext.OnlineCustomerWishlistItems
            .IgnoreQueryFilters()
            .CountAsync(w =>
                w.TenantId == tenantId &&
                !w.IsRemoved &&
                w.CreatedAtUtc >= sevenDaysAgo,
            cancellationToken);

        // SECURITY: IgnoreQueryFilters is intentional so removal analytics can
        // include soft-deleted rows, while TenantId is explicitly reapplied.
        var itemsRemovedLast7Days = await _dbContext.OnlineCustomerWishlistItems
            .IgnoreQueryFilters()
            .CountAsync(w =>
                w.TenantId == tenantId &&
                w.IsRemoved &&
                w.RemovedAtUtc >= sevenDaysAgo,
            cancellationToken);

        // New wishlists = customers who added their first item in last 7 days
        // Simplified approach: count distinct customers with items added in last 7 days
        // that don't have any older items
        // SECURITY: IgnoreQueryFilters is intentional so first-item analytics
        // can use the full wishlist history, while TenantId is explicitly
        // reapplied.
        var recentCustomerIds = await _dbContext.OnlineCustomerWishlistItems
            .IgnoreQueryFilters()
            .Where(w => w.TenantId == tenantId && !w.IsRemoved && w.CreatedAtUtc >= sevenDaysAgo)
            .Select(w => w.OnlineCustomerId)
            .Distinct()
            .ToListAsync(cancellationToken);

        // SECURITY: IgnoreQueryFilters is intentional so the "older items"
        // comparison sees the full history, while TenantId is explicitly
        // reapplied.
        var customersWithOlderItems = await _dbContext.OnlineCustomerWishlistItems
            .IgnoreQueryFilters()
            .Where(w => w.TenantId == tenantId && !w.IsRemoved && w.CreatedAtUtc < sevenDaysAgo)
            .Select(w => w.OnlineCustomerId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var customersWithRecentFirstItem = recentCustomerIds.Except(customersWithOlderItems).Count();


        var analytics = new WishlistAnalyticsDto
        {
            TotalCustomersWithWishlists = uniqueCustomers,
            TotalWishlistItems = totalItems,
            AverageItemsPerWishlist = Math.Round(avgItemsPerWishlist, 1),
            TopProducts = topProducts,
            RecentActivity = new WishlistActivityDto
            {
                ItemsAddedLast7Days = itemsAddedLast7Days,
                ItemsRemovedLast7Days = itemsRemovedLast7Days,
                NewWishlistsLast7Days = customersWithRecentFirstItem
            },
            GeneratedAtUtc = DateTime.UtcNow
        };

        _logger.LogInformation(
            "[Wishlist Analytics] Generated | TenantId: {TenantId} | Customers: {Customers} | Items: {Items} | TopProducts: {TopProducts}",
            tenantId, uniqueCustomers, totalItems, topProducts.Count);

        return analytics;
    }

    private static string? ExtractThumbnailImage(ProductDocument? product)
    {
        return (product?.Media ?? new List<ProductMediaDocument>())
            .Where(media => string.Equals(media.Type, "image", StringComparison.OrdinalIgnoreCase))
            .OrderBy(media => media.Position)
            .Select(media => media.Url)
            .FirstOrDefault(url => !string.IsNullOrWhiteSpace(url));
    }

    private static decimal? ResolveWishlistProductPrice(ProductDocument? product)
    {
        return product == null
            ? null
            : ProductStorefrontVisibilityAuthority.Evaluate(product).Price;
    }

    private static bool IsWishlistProductUnavailable(ProductDocument? product)
    {
        return product == null ||
               !ProductStorefrontVisibilityAuthority.Evaluate(product).IsAvailable;
    }

    private async Task<ProductDocument?> LoadCurrentStorefrontProductAuthorityAsync(
        Guid tenantId,
        Guid productId,
        CancellationToken cancellationToken)
    {
        var projectedProduct = await _productCollection
            .Find(p => p.TenantId == tenantId && p.ProductId == productId)
            .FirstOrDefaultAsync(cancellationToken);

        return await ProductSearchSqlAuthorityOverlay.ApplyAsync(
            _dbContext,
            projectedProduct ?? new ProductDocument
            {
                TenantId = tenantId,
                ProductId = productId
            },
            tenantId,
            cancellationToken);
    }

    #region Cache Helpers

    /// <summary>
    /// Invalidate Redis cache for wishlist.
    /// 
    /// PHASE 10.3.6 - ARCHITECTURAL NOTE:
    /// Direct MongoDB writes are FORBIDDEN in this service.
    /// MongoDB is updated ONLY via event-driven projection (OnlineCustomerWishlistProjectionConsumer).
    /// This cache invalidation is safe because it only clears Redis, not MongoDB.
    /// </summary>
    private async Task InvalidateWishlistCacheAsync(Guid tenantId, Guid customerId)
    {
        try
        {
            var db = _redis.GetDatabase();
            var cacheKey = string.Format(CacheKeyPrefix, tenantId, customerId);
            var countCacheKey = string.Format(CountCacheKeyPrefix, tenantId, customerId);
            await db.KeyDeleteAsync(new RedisKey[] { cacheKey, countCacheKey });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Wishlist] Redis cache invalidation failed | TenantId: {TenantId} | CustomerId: {CustomerId}",
                tenantId, customerId);
        }
    }

    #endregion
}
