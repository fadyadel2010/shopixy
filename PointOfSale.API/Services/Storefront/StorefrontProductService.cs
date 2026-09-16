using System.Text.Json;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Elasticsearch.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.MongoDB.Services;
using PointOfSale.Infrastructure.Redis;
using PointOfSale.Infrastructure.Redis.Configuration;
using PointOfSale.Infrastructure.Redis.Models;
using PointOfSale.Infrastructure.Redis.Services;

namespace PointOfSale.API.Services.Storefront;

/// <summary>
/// Implementation of storefront product service.
/// 
/// Read Flow (strict order):
/// 1. Build Redis cache key
/// 2. Try Redis cache (graceful on miss/error)
/// 3. Query MongoDB as a candidate read model
/// 4. Recompose current SQL product/media/variant authority
/// 5. Populate Redis cache only with SQL-accepted DTOs
/// 6. Return mapped DTO
/// 
/// Design Principles:
/// - MongoDB is a read-model candidate; SQL remains public visibility authority
/// - Redis is optional cache layer
/// - Cache failures never break the request
/// - All operations are tenant-scoped
/// </summary>
public class StorefrontProductService : IStorefrontProductService
{
    private readonly IProductReadRepository _repository;
    private readonly ApplicationDbContext _dbContext;
    private readonly IRedisJsonCacheService _cacheService;
    private readonly RedisSettings _redisSettings;
    private readonly ILogger<StorefrontProductService> _logger;

    public StorefrontProductService(
        IProductReadRepository repository,
        ApplicationDbContext dbContext,
        IRedisJsonCacheService cacheService,
        IOptions<RedisSettings> redisSettings,
        ILogger<StorefrontProductService> logger)
    {
        _repository = repository;
        _dbContext = dbContext;
        _cacheService = cacheService;
        _redisSettings = redisSettings.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ProductReadDto?> GetProductBySlugAsync(
        Guid tenantId, 
        string slug,
        bool affiliateModeOnly = false,
        CancellationToken cancellationToken = default)
    {
        // Normalize slug
        slug = slug.ToLowerInvariant().Trim();

        // AF-12 FIX: Include affiliate mode in cache key to prevent cache poisoning
        var cacheKeySuffix = affiliateModeOnly ? $"{slug}:affiliate" : slug;
        var cacheKey = CacheKeyBuilder.ProductBySlugKey(_redisSettings.InstanceName, tenantId, cacheKeySuffix);

        _logger.LogDebug(
            "GetProductBySlug starting | TenantId: {TenantId} | Slug: {Slug} | AffiliateMode: {AffiliateMode} | CacheKey: {CacheKey}",
            tenantId, slug, affiliateModeOnly, cacheKey);

        var product = await _cacheService.GetOrCreateOwnedAsync(
            cacheKey,
            "storefront-product",
            CacheKeyBuilder.StorefrontProductsInvalidationGenerationKey(_redisSettings.InstanceName, tenantId),
            TimeSpan.FromMinutes(_redisSettings.ProductCacheTtlMinutes),
            async ct =>
            {
                ProductDocument? document;
                try
                {
                    document = await _repository.FindBySlugAsync(tenantId, slug, true, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "MongoDB query failed | TenantId: {TenantId} | Slug: {Slug}",
                        tenantId,
                        slug);
                    throw;
                }

                document = await ApplyCurrentSqlProductDetailAuthorityAsync(
                    tenantId,
                    document,
                    ct);

                if (document == null)
                {
                    return new CachePopulationResult<ProductReadDto>();
                }

                if (affiliateModeOnly && !document.IsAffiliateEnabled)
                {
                    return new CachePopulationResult<ProductReadDto>();
                }

                return CachePopulationResult<ProductReadDto>.FromValue(
                    MapToDto(document),
                    document.SyncedAtUtc,
                    document.LastEventOccurredAtUtc,
                    skipCache: !document.LastEventOccurredAtUtc.HasValue);
            },
            maxProjectionLag: TimeSpan.FromSeconds(_redisSettings.MaxProjectionLagSeconds),
            cancellationToken: cancellationToken);

        if (product != null)
        {
            return await RecomposeCachedProductDetailFromSqlAuthorityAsync(
                tenantId,
                product,
                expectedSlug: slug,
                affiliateModeOnly,
                cancellationToken);
        }

        _logger.LogInformation(
            "Product not found | TenantId: {TenantId} | Slug: {Slug}",
            tenantId,
            slug);
        return null;
    }

    /// <inheritdoc />
    public async Task<ProductReadDto?> GetProductByIdAsync(
        Guid tenantId, 
        Guid productId,
        bool affiliateModeOnly = false,
        CancellationToken cancellationToken = default)
    {
        // AF-12 FIX: Include affiliate mode in cache key to prevent cache poisoning
        // Build cache key manually since we need to append :affiliate suffix
        var cacheKey = affiliateModeOnly
            ? $"{CacheKeyBuilder.ProductByIdKey(_redisSettings.InstanceName, tenantId, productId)}:affiliate"
            : CacheKeyBuilder.ProductByIdKey(_redisSettings.InstanceName, tenantId, productId);

        _logger.LogDebug(
            "GetProductById starting | TenantId: {TenantId} | ProductId: {ProductId} | AffiliateMode: {AffiliateMode} | CacheKey: {CacheKey}",
            tenantId, productId, affiliateModeOnly, cacheKey);

        var product = await _cacheService.GetOrCreateOwnedAsync(
            cacheKey,
            "storefront-product",
            CacheKeyBuilder.StorefrontProductsInvalidationGenerationKey(_redisSettings.InstanceName, tenantId),
            TimeSpan.FromMinutes(_redisSettings.ProductCacheTtlMinutes),
            async ct =>
            {
                ProductDocument? document;
                try
                {
                    document = await _repository.FindByProductIdAsync(tenantId, productId, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "MongoDB query failed | TenantId: {TenantId} | ProductId: {ProductId}",
                        tenantId,
                        productId);
                    throw;
                }

                document = await ApplyCurrentSqlProductDetailAuthorityAsync(
                    tenantId,
                    document,
                    ct);

                if (document == null)
                {
                    return new CachePopulationResult<ProductReadDto>();
                }

                if (affiliateModeOnly && !document.IsAffiliateEnabled)
                {
                    return new CachePopulationResult<ProductReadDto>();
                }

                return CachePopulationResult<ProductReadDto>.FromValue(
                    MapToDto(document),
                    document.SyncedAtUtc,
                    document.LastEventOccurredAtUtc,
                    skipCache: !document.LastEventOccurredAtUtc.HasValue);
            },
            maxProjectionLag: TimeSpan.FromSeconds(_redisSettings.MaxProjectionLagSeconds),
            cancellationToken: cancellationToken);

        if (product != null)
        {
            return await RecomposeCachedProductDetailFromSqlAuthorityAsync(
                tenantId,
                product,
                expectedSlug: null,
                affiliateModeOnly,
                cancellationToken);
        }

        _logger.LogInformation(
            "Product not found | TenantId: {TenantId} | ProductId: {ProductId}",
            tenantId,
            productId);
        return null;
    }

    public async Task<StorefrontVariantResolutionResponse?> ResolveProductVariantAsync(
        Guid tenantId,
        Guid productId,
        StorefrontVariantResolutionRequest request,
        bool affiliateModeOnly = false,
        CancellationToken cancellationToken = default)
    {
        var product = await GetProductByIdAsync(
            tenantId,
            productId,
            affiliateModeOnly,
            cancellationToken);

        if (product == null || product.Variants.Count == 0)
        {
            return null;
        }

        var selectedOptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var option in request.SelectedOptions ?? new Dictionary<string, string>())
        {
            if (string.IsNullOrWhiteSpace(option.Key) || string.IsNullOrWhiteSpace(option.Value))
            {
                continue;
            }

            var key = option.Key.Trim();
            if (selectedOptions.ContainsKey(key))
            {
                return null;
            }

            selectedOptions[key] = option.Value.Trim();
        }

        var resolvedVariant = selectedOptions.Count == 0
            ? ResolveDefaultVariant(product)
            : ResolveExactOptionCombination(product.Variants, selectedOptions);

        if (resolvedVariant == null)
        {
            return null;
        }

        return new StorefrontVariantResolutionResponse
        {
            ProductId = product.Id,
            ResolvedVariantId = resolvedVariant.Id,
            Variant = resolvedVariant
        };
    }

    /// <summary>
    /// Maps MongoDB ProductDocument to ProductReadDto.
    /// DTO is independent from internal document structure.
    /// 
    /// CRITICAL: Maps DefaultUnit for correct unit-based pricing.
    /// PHASE 11.3: Maps Variants for storefront variant selection.
    /// 
    /// PHASE 11.5 — VARIANT-CENTRIC ARCHITECTURE:
    /// - For products WITH variants: Price comes from DEFAULT VARIANT
    /// - For products WITHOUT variants: Legacy price fallback
    /// - InStock is computed from variant data if variants exist
    /// </summary>
    private static ProductReadDto MapToDto(ProductDocument document)
    {
        var media = GetOrderedProductMedia(document.Media);
        var images = media
            .Where(item => string.Equals(item.MediaType, "image", StringComparison.OrdinalIgnoreCase))
            .Select(item => item.Url)
            .ToList();
        
        // Map default unit if available
        DTOs.Storefront.ProductUnitDto? defaultUnit = null;
        if (document.DefaultUnit != null)
        {
            defaultUnit = new DTOs.Storefront.ProductUnitDto
            {
                UnitId = document.DefaultUnit.UnitId,
                Name = document.DefaultUnit.Name,
                Symbol = document.DefaultUnit.Symbol,
                SalePrice = document.DefaultUnit.SalePrice,
                ConversionFactor = document.DefaultUnit.ConversionFactor
            };
        }

        var purchasability = ProductStorefrontVisibilityAuthority.Evaluate(document);

        // PHASE 11.3: Map variants for storefront variant selection
        var variants = MapVariants(
            document,
            document.Variants,
            document.Media,
            images,
            document.HasActivePromotion,
            document.PromotionBadge,
            document.PromotionDiscountType,
            document.PromotionDiscountValue);
        var defaultVariant = variants.FirstOrDefault(v => v.IsDefault) ?? variants.FirstOrDefault();
        
        var effectivePrice = purchasability.Price;

        return new ProductReadDto
        {
            Id = document.ProductId,
            Name = document.Name,
            NameAr = document.NameAr,
            Description = document.Description,
            DescriptionAr = document.DescriptionAr,
            SKU = document.SKU,
            Barcode = document.Barcode,
            Price = effectivePrice, // Use effective price for display
            DefaultUnit = defaultUnit,
            Pricing = StorefrontPricingPresentationMapper.Build(
                effectivePrice,
                document.HasActivePromotion,
                document.PromotionBadge,
                document.PromotionDiscountType,
                document.PromotionDiscountValue),
            Availability = StorefrontAvailabilityPresentationMapper.Build(
                purchasability.IsAvailable,
                purchasability.InStock,
                purchasability.StockQuantity,
                purchasability.RequiresVariantSelection,
                purchasability.CanPurchase,
                purchasability.UnavailableReason),
            CategoryName = document.CategoryName,
            CategoryId = document.CategoryId,
            Slug = document.Slug,
            ProductTemplate = document.ProductTemplate,
            StorefrontLayout = document.ProductTemplate,
            IsFeatured = document.IsFeatured,
            Media = media,
            Images = images,
            IsAvailable = purchasability.IsAvailable,
            InStock = purchasability.InStock,
            StockQuantity = purchasability.StockQuantity,
            CreatedAtUtc = document.CreatedAtUtc,
            // PHASE 11.3: Variant selection fields
            Variants = variants,
            DefaultVariantId = defaultVariant?.Id,
            // PHASE 11.7 — BLOCKER B1 FIX: Map from MongoDB (NOT computed from Variants.Count)
            HasVariants = document.HasVariants,
            // PHASE: Promotions Engine - Badge Display Fields
            HasActivePromotion = document.HasActivePromotion,
            PromotionBadge = document.PromotionBadge,
            PromotionDiscountType = document.PromotionDiscountType,
            PromotionDiscountValue = document.PromotionDiscountValue
        };
    }

    /// <summary>
    /// Maps variant documents to DTOs.
    /// Filters only active variants and sorts by SortOrder.
    /// 
    /// PHASE 11.3 — STOREFRONT VARIANT SELECTION
    /// </summary>
    private static List<ProductVariantDto> MapVariants(
        ProductDocument product,
        List<ProductVariantDocument>? variantDocs,
        List<ProductMediaDocument>? productMedia,
        List<string> productImages,
        bool hasActivePromotion,
        string? promotionBadge,
        string? promotionDiscountType,
        decimal? promotionDiscountValue)
    {
        if (variantDocs == null || variantDocs.Count == 0)
        {
            return new List<ProductVariantDto>();
        }

        return variantDocs
            .Where(v => v.IsActive)
            .OrderBy(v => v.SortOrder)
            .ThenBy(v => v.IsDefault ? 0 : 1) // Default first
            .Select(v =>
            {
                var variantPurchasability = ProductStorefrontVisibilityAuthority.EvaluateVariant(product, v);
                return new ProductVariantDto
                {
                    Id = v.VariantId,
                    SKU = v.SKU,
                    Price = v.Price,
                    OnlinePrice = v.OnlinePrice,
                    Pricing = StorefrontPricingPresentationMapper.Build(
                        variantPurchasability.Price,
                        hasActivePromotion,
                        promotionBadge,
                        promotionDiscountType,
                        promotionDiscountValue),
                    Availability = StorefrontAvailabilityPresentationMapper.Build(
                        variantPurchasability.IsAvailable,
                        variantPurchasability.InStock,
                        variantPurchasability.StockQuantity,
                        variantPurchasability.RequiresVariantSelection,
                        variantPurchasability.CanPurchase,
                        variantPurchasability.UnavailableReason),
                    MediaId = v.MediaId,
                    ImageUrl = ResolveVariantImageUrl(v, productMedia, productImages),
                    TotalStock = variantPurchasability.StockQuantity,
                    IsDefault = v.IsDefault,
                    IsActive = v.IsActive,
                    // PHASE 11.5 FIX: Null-check Attributes to prevent NullReferenceException
                    Attributes = (v.Attributes ?? new List<VariantAttributeDocument>())
                        .OrderBy(a => a.SortOrder)
                        .Select(a => new VariantAttributeDto
                        {
                            Name = a.Name,
                            Value = a.Value
                        })
                        .ToList()
                };
            })
            .ToList();
    }

    private static string? ResolveVariantImageUrl(
        ProductVariantDocument variant,
        List<ProductMediaDocument>? productMedia,
        List<string> productImages)
    {
        if (variant.MediaId.HasValue && productMedia != null)
        {
            var mediaMatch = productMedia
                .Where(media => string.Equals(media.Type, "image", StringComparison.OrdinalIgnoreCase))
                .OrderBy(media => media.Position)
                .FirstOrDefault(media => media.Id == variant.MediaId.Value);

            if (!string.IsNullOrWhiteSpace(mediaMatch?.Url))
            {
                return mediaMatch.Url;
            }
        }

        return productImages.FirstOrDefault();
    }

    private static List<ProductMediaDto> GetOrderedProductMedia(List<ProductMediaDocument>? media)
    {
        return (media ?? new List<ProductMediaDocument>())
            .OrderBy(item => item.Position)
            .ThenBy(item => item.Id)
            .Where(item => !string.IsNullOrWhiteSpace(item.Url))
            .Select(item => new ProductMediaDto
            {
                Id = item.Id,
                MediaType = item.Type,
                Url = item.Url,
                PresentationUrl = BuildProductMediaPresentationUrl(item),
                ThumbnailUrl = BuildProductMediaThumbnailUrl(item),
                Provider = item.Provider,
                Position = item.Position
            })
            .ToList();
    }

    private static string? BuildProductMediaPresentationUrl(ProductMediaDocument item)
    {
        if (!IsYouTubeMedia(item))
        {
            return null;
        }

        var videoId = ExtractYouTubeVideoId(item.Url);
        return string.IsNullOrWhiteSpace(videoId)
            ? null
            : $"https://www.youtube.com/embed/{videoId}";
    }

    private static string? BuildProductMediaThumbnailUrl(ProductMediaDocument item)
    {
        if (!IsYouTubeMedia(item))
        {
            return null;
        }

        var videoId = ExtractYouTubeVideoId(item.Url);
        return string.IsNullOrWhiteSpace(videoId)
            ? null
            : $"https://img.youtube.com/vi/{videoId}/hqdefault.jpg";
    }

    private static bool IsYouTubeMedia(ProductMediaDocument item) =>
        string.Equals(item.Provider, "youtube", StringComparison.OrdinalIgnoreCase);

    private static string? ExtractYouTubeVideoId(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) ||
            !Uri.TryCreate(url, UriKind.Absolute, out var parsed))
        {
            return null;
        }

        if (parsed.Host.Contains("youtu.be", StringComparison.OrdinalIgnoreCase))
        {
            return parsed.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        }

        if (!parsed.Host.Contains("youtube.com", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (parsed.AbsolutePath.Equals("/watch", StringComparison.OrdinalIgnoreCase))
        {
            return ExtractQueryParameter(parsed.Query, "v");
        }

        var segments = parsed.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var embedIndex = Array.FindIndex(segments, segment =>
            segment.Equals("embed", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("shorts", StringComparison.OrdinalIgnoreCase));

        return embedIndex >= 0 && embedIndex + 1 < segments.Length
            ? segments[embedIndex + 1]
            : null;
    }

    private static string? ExtractQueryParameter(string query, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        var pairs = query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
        foreach (var pair in pairs)
        {
            var separatorIndex = pair.IndexOf('=');
            var key = separatorIndex >= 0 ? pair[..separatorIndex] : pair;
            if (!string.Equals(Uri.UnescapeDataString(key), parameterName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = separatorIndex >= 0 ? pair[(separatorIndex + 1)..] : string.Empty;
            return string.IsNullOrWhiteSpace(value) ? null : Uri.UnescapeDataString(value);
        }

        return null;
    }

    private static List<string> GetOrderedProductImageUrls(List<ProductMediaDocument>? media)
    {
        return GetOrderedProductMedia(media)
            .Where(item => string.Equals(item.MediaType, "image", StringComparison.OrdinalIgnoreCase))
            .Select(item => item.Url)
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .ToList();
    }

    #region Product List

    /// <inheritdoc />
    public async Task<PagedResult<ProductListItemDto>> GetProductListAsync(
        Guid tenantId,
        ProductListQuery query,
        bool affiliateModeOnly = false,
        CancellationToken cancellationToken = default)
    {
        // AF-12 FIX: Include affiliate mode in cache key to prevent cache poisoning
        // Affiliate and non-affiliate requests must have separate cache entries
        var queryHash = query.GetQueryHash();
        var cacheKeySuffix = affiliateModeOnly ? $"{queryHash}:affiliate" : queryHash;
        var cacheKey = CacheKeyBuilder.ProductsListKey(_redisSettings.InstanceName, tenantId, cacheKeySuffix);

        _logger.LogDebug(
            "GetProductList starting | TenantId: {TenantId} | Page: {Page} | PageSize: {PageSize} | CategoryId: {CategoryId} | AffiliateMode: {AffiliateMode} | CacheKey: {CacheKey}",
            tenantId, query.Page, query.PageSize, query.CategoryId, affiliateModeOnly, cacheKey);

        var result = await _cacheService.GetOrCreateOwnedAsync(
            cacheKey,
            "storefront-product-list",
            CacheKeyBuilder.StorefrontProductsInvalidationGenerationKey(_redisSettings.InstanceName, tenantId),
            TimeSpan.FromMinutes(_redisSettings.ProductListCacheTtlMinutes),
            async ct =>
            {
                try
                {
                    var (items, totalCount) = await _repository.GetProductListAsync(
                        tenantId,
                        query.Page,
                        query.PageSize,
                        query.CategoryId,
                        query.IsActive,
                        onlineVisibleOnly: true,
                        affiliateModeOnly: affiliateModeOnly,
                        ct);

                    var visibleItems = await RecomposeCurrentSqlProductListAuthorityAsync(
                        tenantId,
                        items,
                        ct);

                    var dtoItems = visibleItems.Select(MapToListItemDto).ToList();
                    if (affiliateModeOnly && dtoItems.Any(p => !p.IsAffiliateEnabled))
                    {
                        var leakedCount = dtoItems.Count(p => !p.IsAffiliateEnabled);
                        _logger.LogCritical(
                            "[AF-12 SECURITY BREACH] Affiliate Mode filter bypassed | TenantId: {TenantId} | LeakedProducts: {Count} | TotalProducts: {Total}",
                            tenantId,
                            leakedCount,
                            dtoItems.Count);

                        throw new InvalidOperationException(
                            $"AF-12 Security breach: {leakedCount} non-affiliate products returned in Affiliate Mode");
                    }

                    var latestDocument = visibleItems
                        .OrderByDescending(item => item.SyncedAtUtc)
                        .FirstOrDefault();

                    return CachePopulationResult<PagedResult<ProductListItemDto>>.FromValue(
                        new PagedResult<ProductListItemDto>(dtoItems, query.Page, query.PageSize, (int)totalCount),
                        latestDocument?.SyncedAtUtc,
                        latestDocument?.LastEventOccurredAtUtc,
                        skipCache: latestDocument?.LastEventOccurredAtUtc == null);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "MongoDB query failed (list) | TenantId: {TenantId} | Page: {Page} | PageSize: {PageSize}",
                        tenantId,
                        query.Page,
                        query.PageSize);
                    throw;
                }
            },
            maxProjectionLag: TimeSpan.FromSeconds(_redisSettings.MaxProjectionLagSeconds),
            cancellationToken: cancellationToken)
            ?? new PagedResult<ProductListItemDto>([], query.Page, query.PageSize, 0);

        return await RecomposeCachedProductListFromSqlAuthorityAsync(
            tenantId,
            result,
            affiliateModeOnly,
            cancellationToken);
    }

    private async Task<ProductReadDto?> RecomposeCachedProductDetailFromSqlAuthorityAsync(
        Guid tenantId,
        ProductReadDto cached,
        string? expectedSlug,
        bool affiliateModeOnly,
        CancellationToken cancellationToken)
    {
        if (cached.Id == Guid.Empty)
        {
            return null;
        }

        var current = await ApplyCurrentSqlProductDetailAuthorityAsync(
            tenantId,
            new ProductDocument
            {
                TenantId = tenantId,
                ProductId = cached.Id
            },
            cancellationToken);

        if (current == null)
        {
            _logger.LogWarning(
                "Cached product detail suppressed by current SQL authority | TenantId: {TenantId} | ProductId: {ProductId}",
                tenantId,
                cached.Id);
            return null;
        }

        if (!string.IsNullOrWhiteSpace(expectedSlug) &&
            !string.Equals(current.Slug, expectedSlug, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Cached product detail slug suppressed by current SQL authority | TenantId: {TenantId} | ProductId: {ProductId} | CachedSlug: {CachedSlug} | CurrentSlug: {CurrentSlug}",
                tenantId,
                cached.Id,
                expectedSlug,
                current.Slug);
            return null;
        }

        if (affiliateModeOnly && !current.IsAffiliateEnabled)
        {
            return null;
        }

        return MapToDto(current);
    }

    private async Task<PagedResult<ProductListItemDto>> RecomposeCachedProductListFromSqlAuthorityAsync(
        Guid tenantId,
        PagedResult<ProductListItemDto> cached,
        bool affiliateModeOnly,
        CancellationToken cancellationToken)
    {
        if (cached.Items.Count == 0)
        {
            return cached;
        }

        var recomposed = new List<ProductListItemDto>(cached.Items.Count);
        foreach (var item in cached.Items)
        {
            if (item.Id == Guid.Empty)
            {
                continue;
            }

            var current = await ApplyCurrentSqlProductDetailAuthorityAsync(
                tenantId,
                new ProductDocument
                {
                    TenantId = tenantId,
                    ProductId = item.Id
                },
                cancellationToken);

            if (current == null)
            {
                continue;
            }

            if (affiliateModeOnly && !current.IsAffiliateEnabled)
            {
                continue;
            }

            recomposed.Add(MapToListItemDto(current));
        }

        if (recomposed.Count != cached.Items.Count)
        {
            _logger.LogWarning(
                "Cached product list converged from current SQL authority | TenantId: {TenantId} | CachedCount: {CachedCount} | CurrentVisibleCount: {CurrentVisibleCount}",
                tenantId,
                cached.Items.Count,
                recomposed.Count);
        }

        return new PagedResult<ProductListItemDto>(
            recomposed,
            cached.Page,
            cached.PageSize,
            recomposed.Count);
    }

    private async Task<ProductDocument?> ApplyCurrentSqlProductDetailAuthorityAsync(
        Guid tenantId,
        ProductDocument? document,
        CancellationToken cancellationToken)
    {
        if (document == null)
        {
            return null;
        }

        var sqlProjection = await ProductSearchSqlAuthorityOverlay.ApplyAsync(
            _dbContext,
            document,
            tenantId,
            cancellationToken);

        return IsPubliclyVisibleProduct(sqlProjection)
            ? sqlProjection
            : null;
    }

    private async Task<List<ProductDocument>> RecomposeCurrentSqlProductListAuthorityAsync(
        Guid tenantId,
        IReadOnlyCollection<ProductDocument> candidates,
        CancellationToken cancellationToken)
    {
        var visible = new List<ProductDocument>(candidates.Count);

        foreach (var candidate in candidates)
        {
            var sqlProjection = await ProductSearchSqlAuthorityOverlay.ApplyAsync(
                _dbContext,
                candidate,
                tenantId,
                cancellationToken);

            if (sqlProjection is { IsActive: true, IsOnlineVisible: true })
            {
                visible.Add(sqlProjection);
            }
        }

        return visible;
    }

    private static bool IsPubliclyVisibleProduct(ProductDocument? document)
    {
        return document is { IsActive: true, IsOnlineVisible: true };
    }

    private static ProductVariantDto? ResolveDefaultVariant(ProductReadDto product) =>
        product.Variants.FirstOrDefault(variant => variant.Id == product.DefaultVariantId && variant.IsActive)
        ?? product.Variants.FirstOrDefault(variant => variant.IsDefault && variant.IsActive)
        ?? product.Variants.FirstOrDefault(variant => variant.IsActive);

    private static ProductVariantDto? ResolveExactOptionCombination(
        IReadOnlyCollection<ProductVariantDto> variants,
        IReadOnlyDictionary<string, string> selectedOptions)
    {
        if (selectedOptions.Count == 0)
        {
            return null;
        }

        var matches = variants
            .Where(variant => variant.IsActive)
            .Where(variant => HasExactOptionCombination(variant, selectedOptions))
            .Take(2)
            .ToList();

        return matches.Count == 1 ? matches[0] : null;
    }

    private static bool HasExactOptionCombination(
        ProductVariantDto variant,
        IReadOnlyDictionary<string, string> selectedOptions)
    {
        if (variant.Attributes.Count != selectedOptions.Count)
        {
            return false;
        }

        foreach (var attribute in variant.Attributes)
        {
            if (!selectedOptions.TryGetValue(attribute.Name, out var selectedValue) ||
                !string.Equals(attribute.Value, selectedValue, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Maps MongoDB ProductDocument to ProductListItemDto.
    /// Minimal fields for list/card views.
    /// 
    /// CRITICAL: Maps DefaultUnit for correct unit-based pricing.
    /// 
    /// PHASE 11.5 — VARIANT-CENTRIC ARCHITECTURE:
    /// - For products WITH variants: Price comes from DEFAULT VARIANT
    /// - For products WITHOUT variants: Legacy price fallback
    /// - InStock is computed from variant data if variants exist
    /// </summary>
    private static ProductListItemDto MapToListItemDto(ProductDocument document)
    {
        // Get first image as thumbnail
        var images = GetOrderedProductImageUrls(document.Media);
        var thumbnailImage = images.FirstOrDefault();
        
        // Map default unit if available
        DTOs.Storefront.ProductUnitDto? defaultUnit = null;
        if (document.DefaultUnit != null)
        {
            defaultUnit = new DTOs.Storefront.ProductUnitDto
            {
                UnitId = document.DefaultUnit.UnitId,
                Name = document.DefaultUnit.Name,
                Symbol = document.DefaultUnit.Symbol,
                SalePrice = document.DefaultUnit.SalePrice,
                ConversionFactor = document.DefaultUnit.ConversionFactor
            };
        }
        
        var purchasability = ProductStorefrontVisibilityAuthority.Evaluate(document);
        var effectivePrice = purchasability.Price;

        return new ProductListItemDto
        {
            Id = document.ProductId,
            Name = document.Name,
            NameAr = document.NameAr,
            Price = effectivePrice, // Use effective price for display
            DefaultUnit = defaultUnit,
            Pricing = StorefrontPricingPresentationMapper.Build(
                effectivePrice,
                document.HasActivePromotion,
                document.PromotionBadge,
                document.PromotionDiscountType,
                document.PromotionDiscountValue),
            Availability = StorefrontAvailabilityPresentationMapper.Build(
                purchasability.IsAvailable,
                purchasability.InStock,
                purchasability.StockQuantity,
                purchasability.RequiresVariantSelection,
                purchasability.CanPurchase,
                purchasability.UnavailableReason),
            CategoryName = document.CategoryName,
            Slug = document.Slug,
            ThumbnailImage = thumbnailImage,
            ProductTemplate = document.ProductTemplate,
            StorefrontLayout = document.ProductTemplate,
            IsFeatured = document.IsFeatured,
            IsAvailable = purchasability.IsAvailable,
            InStock = purchasability.InStock,
            StockQuantity = purchasability.StockQuantity,
            // PHASE: Promotions Engine - Badge Display Fields
            HasActivePromotion = document.HasActivePromotion,
            PromotionBadge = document.PromotionBadge,
            PromotionDiscountType = document.PromotionDiscountType,
            PromotionDiscountValue = document.PromotionDiscountValue
        };
    }

    #endregion
}
