using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.DTOs.Feeds;
using PointOfSale.API.Instrumentation;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Promotions;
using PointOfSale.Infrastructure.Reliability;

namespace PointOfSale.API.Services.Feeds;

/// <summary>
/// FEED-1A: Outcome of a public feed generation attempt.
/// TokenNotFound and TenantInactive are publicly indistinguishable (both 404);
/// the distinction exists only for internal logging.
/// </summary>
public enum MetaFeedGenerationOutcome
{
    Success,
    TokenNotFound,
    TenantInactive,
    GenerationFailed
}

/// <summary>
/// FEED-1A: Result envelope for feed generation. Never carries tenant
/// diagnostics or raw tokens — only the serialized XML on success.
///
/// FEED-1B additions (all optional, contract-compatible):
/// - <see cref="CacheHit"/> — served from the rendered-XML cache.
/// - <see cref="Truncated"/> — deterministic output hit the item/size cap.
/// - <see cref="CacheMaxAgeSeconds"/> — source for the Cache-Control header.
/// </summary>
public sealed record MetaProductFeedGenerationResult(
    MetaFeedGenerationOutcome Outcome,
    string? Xml,
    Guid? TenantId,
    Guid? TokenId,
    int ItemCount,
    int ExcludedItemCount,
    bool CacheHit = false,
    bool Truncated = false,
    int CacheMaxAgeSeconds = 300)
{
    public static MetaProductFeedGenerationResult Failure(MetaFeedGenerationOutcome outcome) =>
        new(outcome, null, null, null, 0, 0);
}

/// <summary>
/// FEED-1A: Generates the Meta product feed for a hashed feed token.
/// </summary>
public interface IMetaProductFeedService
{
    /// <summary>
    /// Full pipeline: raw token -> hash lookup -> tenant authority -> set-based
    /// projection -> RSS serialization. No per-product round trips.
    /// </summary>
    Task<MetaProductFeedGenerationResult> GenerateAsync(string rawToken, CancellationToken cancellationToken = default);
}

/// <summary>
/// FEED-1A: Set-based feed projection replicating the SQL authority semantics of
/// ProductSearchSqlAuthorityOverlay (eligibility fold, live category lineage with
/// cycle detection, current-semantic-owner variant admission, active-warehouse
/// variant stock, default-unit simple-product pricing, product-scope promotion
/// winner) — but bounded: a fixed number of queries regardless of catalog size.
///
/// AUTHORITY RULES (Feed-0 decisions, authoritative):
/// - Simple product price = default ProductUnit.SellingPrice (legacy Product
///   SalePrice/OnlinePrice never used). Zero/missing price -> product excluded.
/// - Variant admission = IsCurrentSemanticOwner + IsActive + EffectivePrice > 0
///   (EffectivePrice = OnlinePrice > 0 ? OnlinePrice : Price).
/// - Zero-stock items are RETAINED with mapped availability, never excluded.
/// - IsSearchable/IsFeatured never gate feed eligibility.
/// </summary>
public sealed partial class MetaProductFeedService : IMetaProductFeedService, IFeedProductProjection
{
    private const int TitleMaxLength = 150;
    private const int DescriptionMaxLength = 5000;
    private const string ConditionNew = "new";
    private const int UncappedCandidateBatchSize = 10_000;
    private static readonly int[] ValidGtinLengths = { 8, 12, 13, 14 };

    private readonly ApplicationDbContext _dbContext;
    private readonly IMetaFeedXmlSerializer _xmlSerializer;
    private readonly StorefrontUrlResolver _urlResolver;
    private readonly IFeedStorefrontReadinessService _readiness;
    private readonly ILogger<MetaProductFeedService> _logger;
    private readonly IMetaFeedCache _feedCache;
    private readonly MetaFeedTelemetry _telemetry;
    private readonly MetaFeedSafeguards _safeguards;

    public MetaProductFeedService(
        ApplicationDbContext dbContext,
        IMetaFeedXmlSerializer xmlSerializer,
        StorefrontUrlResolver urlResolver,
        ILogger<MetaProductFeedService> logger,
        IMetaFeedCache feedCache,
        MetaFeedTelemetry telemetry,
        MetaFeedSafeguards? safeguards = null,
        IFeedStorefrontReadinessService? readiness = null)
    {
        _dbContext = dbContext;
        _xmlSerializer = xmlSerializer;
        _urlResolver = urlResolver;
        _readiness = readiness ?? StrictFeedStorefrontReadinessService.Instance;
        _logger = logger;
        _feedCache = feedCache;
        _telemetry = telemetry;
        _safeguards = safeguards ?? MetaFeedSafeguards.Default;
    }

    public async Task<MetaProductFeedGenerationResult> GenerateAsync(
        string rawToken,
        CancellationToken cancellationToken = default)
    {
        var requestStopwatch = Stopwatch.StartNew();
        var maxAgeSeconds = (int)_feedCache.GetTtl().TotalSeconds;

        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return MetaProductFeedGenerationResult.Failure(MetaFeedGenerationOutcome.TokenNotFound);
        }

        // Query 1: hashed-token lookup. Raw tokens are never stored or compared.
        // SECURITY (Feed-1B): token validation ALWAYS happens before any cache
        // lookup, so a revoked token can never be satisfied from cache.
        // SECURITY (Feed-2): the lookup is channel-scoped — a token of another
        // channel (e.g. google) can never authorize the Meta feed route.
        var tokenHash = ProductFeedToken.HashToken(rawToken.Trim());
        var token = await _dbContext.ProductFeedTokens
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(item => item.TokenHash == tokenHash && !item.IsDeleted && item.Channel == ProductFeedToken.MetaChannel)
            .Select(item => new { item.Id, item.TenantId, item.RevokedAtUtc })
            .SingleOrDefaultAsync(cancellationToken);

        if (token == null || token.RevokedAtUtc != null)
        {
            return MetaProductFeedGenerationResult.Failure(MetaFeedGenerationOutcome.TokenNotFound);
        }

        // Query 2: tenant authority — same active-tenant gate as the search overlay.
        if (!await TenantLineageAuthority.IsActiveTenantAsync(_dbContext, token.TenantId, cancellationToken))
        {
            return MetaProductFeedGenerationResult.Failure(MetaFeedGenerationOutcome.TenantInactive);
        }

        var tenant = await _dbContext.Tenants
            .AsNoTracking()
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(item => item.Id == token.TenantId, cancellationToken);
        if (tenant == null)
        {
            return MetaProductFeedGenerationResult.Failure(MetaFeedGenerationOutcome.TenantInactive);
        }

        // FEED-1B: cache lookup keyed by tenant + token ROW identity only
        // (never the raw token). Redis failures are isolated inside the cache
        // wrapper and fall through to SQL generation.
        var (cacheOutcome, cached) = await _feedCache.TryGetAsync(
            tenant.Id, token.Id, ProductFeedToken.MetaChannel, cancellationToken);
        if (cacheOutcome == MetaFeedCacheLookupOutcome.Hit && cached != null)
        {
            _telemetry.RecordCacheHit();
            _telemetry.RecordRequest(cached.ItemCount == 0 ? MetaFeedTelemetry.ResultEmpty : MetaFeedTelemetry.ResultSuccess);
            _telemetry.RecordResponseSize(Encoding.UTF8.GetByteCount(cached.Xml));
            requestStopwatch.Stop();

            _logger.LogInformation(
                "Meta feed served from cache | TenantId: {TenantId} | TokenId: {TokenId} | Channel: meta | CacheResult: hit | Items: {ItemCount} | Excluded: {ExcludedCount} | Truncated: {Truncated} | DurationMs: {DurationMs}",
                tenant.Id, token.Id, cached.ItemCount, cached.ExcludedItemCount, cached.Truncated, requestStopwatch.ElapsedMilliseconds);

            return new MetaProductFeedGenerationResult(
                MetaFeedGenerationOutcome.Success,
                cached.Xml,
                tenant.Id,
                token.Id,
                cached.ItemCount,
                cached.ExcludedItemCount,
                CacheHit: true,
                Truncated: cached.Truncated,
                CacheMaxAgeSeconds: maxAgeSeconds);
        }

        // Both miss and error fall through to SQL generation (the cache wrapper
        // records feed_cache_error itself when Redis fails).
        _telemetry.RecordCacheMiss();

        MetaProductFeedGenerationResult coldResult;
        try
        {
            coldResult = await GenerateFromSqlAsync(tenant, token.Id, maxAgeSeconds, requestStopwatch, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _telemetry.RecordGenerationFailure("canceled");
            throw;
        }
        catch (Exception)
        {
            _telemetry.RecordGenerationFailure("projection_error");
            _telemetry.RecordRequest(MetaFeedTelemetry.ResultError);
            throw;
        }

        return coldResult;
    }

    /// <summary>
    /// FEED-1B cold path: storefront URL authority -> set-based projection ->
    /// safeguards (item cap + response size) -> RSS serialization -> cache
    /// population -> telemetry. Cached value is exactly the XML served here,
    /// so cached and uncached output stay byte-identical.
    /// </summary>
    private async Task<MetaProductFeedGenerationResult> GenerateFromSqlAsync(
        Tenant tenant,
        Guid tokenId,
        int maxAgeSeconds,
        Stopwatch requestStopwatch,
        CancellationToken cancellationToken)
    {
        // Storefront base URL must resolve before projecting anything — the feed
        // must never emit invented/localhost/broken product URLs.
        string baseUrl;
        try
        {
            baseUrl = _urlResolver.Resolve(tenant);
            if (!_readiness.IsPublicStorefrontBaseUrl(baseUrl, out var reasonCode))
            {
                _telemetry.RecordGenerationFailure(reasonCode);
                _telemetry.RecordRequest(MetaFeedTelemetry.ResultError);
                _logger.LogError(
                    "Meta feed generation aborted: storefront URL is not feed-public | TenantId: {TenantId} | TokenId: {TokenId} | ReasonCode: {ReasonCode}",
                    tenant.Id, tokenId, reasonCode);
                return MetaProductFeedGenerationResult.Failure(MetaFeedGenerationOutcome.GenerationFailed);
            }
        }
        catch (InvalidOperationException ex)
        {
            _telemetry.RecordGenerationFailure("url_unresolved");
            _telemetry.RecordRequest(MetaFeedTelemetry.ResultError);
            _logger.LogError(
                ex,
                "Meta feed generation aborted: storefront URL unresolved | TenantId: {TenantId} | TokenId: {TokenId}",
                tenant.Id, tokenId);
            return MetaProductFeedGenerationResult.Failure(MetaFeedGenerationOutcome.GenerationFailed);
        }

        var generationStopwatch = Stopwatch.StartNew();
        var projection = await ProjectFeedProductsBoundedAsync(
            tenant,
            _safeguards.MaxItems,
            includeItem: null,
            cancellationToken);

        // FEED-1B/F-09 item cap: deterministic (Product.Id then Variant.Id
        // order), now enforced during bounded feed-item projection so the cold
        // path never materializes the full catalog before capping.
        var truncated = projection.HitItemCap;
        IReadOnlyList<FeedProduct> items = projection.Items;
        var excludedCount = projection.ExcludedItemCount;
        if (projection.HitItemCap)
        {
            _telemetry.RecordItemCapTruncation();
            _logger.LogWarning(
                "Meta feed truncated at item cap | TenantId: {TenantId} | TokenId: {TokenId} | Cap: {Cap}",
                tenant.Id, tokenId, _safeguards.MaxItems);
        }

        var xml = _xmlSerializer.Serialize(tenant.Name, baseUrl, items);

        // FEED-1B response-size backstop: shrink deterministically and
        // re-serialize — the response is always complete valid RSS. The
        // returned item list is the shrunken one, so reported counts match
        // exactly what was serialized.
        (xml, items) = EnforceMaxResponseBytes(xml, items, tenant.Name, baseUrl, ref truncated, tenant.Id, tokenId);
        generationStopwatch.Stop();

        var finalItemCount = items.Count;
        var responseBytes = Encoding.UTF8.GetByteCount(xml);

        await _feedCache.SetAsync(
            tenant.Id,
            tokenId,
            new MetaFeedCachePayload
            {
                Xml = xml,
                ItemCount = finalItemCount,
                ExcludedItemCount = excludedCount,
                Truncated = truncated,
                GeneratedAtUtc = DateTime.UtcNow
            },
            ProductFeedToken.MetaChannel,
            cancellationToken);

        _telemetry.RecordGenerationDuration(generationStopwatch.Elapsed);
        _telemetry.RecordResponseSize(responseBytes);
        _telemetry.RecordRequest(finalItemCount == 0 ? MetaFeedTelemetry.ResultEmpty : MetaFeedTelemetry.ResultSuccess);
        requestStopwatch.Stop();

        _logger.LogInformation(
            "Meta feed generated | TenantId: {TenantId} | TokenId: {TokenId} | Channel: meta | CacheResult: miss | Items: {ItemCount} | Excluded: {ExcludedCount} | Truncated: {Truncated} | GenerationMs: {GenerationMs} | TotalMs: {TotalMs} | ResponseBytes: {ResponseBytes}",
            tenant.Id, tokenId, finalItemCount, excludedCount, truncated,
            generationStopwatch.ElapsedMilliseconds, requestStopwatch.ElapsedMilliseconds, responseBytes);

        return new MetaProductFeedGenerationResult(
            MetaFeedGenerationOutcome.Success,
            xml,
            tenant.Id,
            tokenId,
            finalItemCount,
            excludedCount,
            CacheHit: false,
            Truncated: truncated,
            CacheMaxAgeSeconds: maxAgeSeconds);
    }

    /// <summary>
    /// FEED-1B: when the rendered XML exceeds the response-size backstop,
    /// keep the deterministic FIRST items (projection order never changes),
    /// re-serializing a complete valid RSS document. Bounded iterations with a
    /// geometric fallback guarantee termination even under pathological
    /// per-item size variance.
    /// </summary>
    private (string Xml, IReadOnlyList<FeedProduct> Items) EnforceMaxResponseBytes(
        string xml,
        IReadOnlyList<FeedProduct> items,
        string tenantName,
        string baseUrl,
        ref bool truncated,
        Guid tenantId,
        Guid tokenId)
    {
        var maxBytes = _safeguards.MaxResponseBytes;
        var current = xml;
        var currentItems = items;

        for (var attempt = 0; attempt < 4; attempt++)
        {
            var byteCount = Encoding.UTF8.GetByteCount(current);
            if (byteCount <= maxBytes || currentItems.Count == 0)
            {
                return (current, currentItems);
            }

            var estimated = (long)((double)maxBytes / byteCount * currentItems.Count * 0.95);
            var target = (int)Math.Max(1, Math.Min(estimated, currentItems.Count - 1));
            currentItems = currentItems.Take(target).ToList();
            current = _xmlSerializer.Serialize(tenantName, baseUrl, currentItems);
            truncated = true;

            _logger.LogWarning(
                "Meta feed truncated at response-size safeguard | TenantId: {TenantId} | TokenId: {TokenId} | Bytes: {ByteCount} | MaxBytes: {MaxBytes} | ItemsKept: {ItemsKept}",
                tenantId, tokenId, byteCount, maxBytes, currentItems.Count);
        }

        return (current, currentItems);
    }

    // =========================================================================
    // Set-based projection — bounded queries only, no per-product round trips
    // =========================================================================

    /// <summary>
    /// FEED-2: shared projection contract — every feed channel serializes the
    /// exact same canonical items; delegates to the certified implementation.
    /// </summary>
    Task<FeedProductProjectionResult> IFeedProductProjection.ProjectAsync(
        Tenant tenant,
        int? maxItems,
        Func<FeedProduct, bool>? includeItem,
        CancellationToken cancellationToken) =>
        ProjectFeedProductsBoundedAsync(tenant, maxItems, includeItem, cancellationToken);

    private async Task<FeedProductProjectionResult> ProjectFeedProductsBoundedAsync(
        Tenant tenant,
        int? maxItems,
        Func<FeedProduct, bool>? includeItem,
        CancellationToken cancellationToken)
    {
        var tenantId = tenant.Id;
        var feedStorefrontBaseUrl = _urlResolver.Resolve(tenant);
        var targetItemCount = maxItems.GetValueOrDefault(int.MaxValue);
        var batchSize = maxItems.HasValue
            ? Math.Max(1, maxItems.Value + 1)
            : UncappedCandidateBatchSize;
        var items = maxItems.HasValue
            ? new List<FeedProduct>(Math.Min(maxItems.Value, 1024))
            : new List<FeedProduct>();
        var excludedCount = 0;
        var offset = 0;

        while (true)
        {
            var candidates = await BuildFeedCandidateQuery(tenantId)
                .Skip(offset)
                .Take(batchSize)
                .ToListAsync(cancellationToken);

            if (candidates.Count == 0)
            {
                break;
            }

            var (batchItems, batchExcludedCount) = await ProjectCandidateBatchAsync(
                tenant,
                candidates,
                feedStorefrontBaseUrl,
                cancellationToken);
            excludedCount += batchExcludedCount;

            foreach (var item in batchItems)
            {
                if (includeItem != null && !includeItem(item))
                {
                    excludedCount++;
                    continue;
                }

                if (items.Count >= targetItemCount)
                {
                    return new FeedProductProjectionResult(items, excludedCount, HitItemCap: true);
                }

                items.Add(item);
            }

            offset += candidates.Count;
            if (candidates.Count < batchSize)
            {
                break;
            }
        }

        return new FeedProductProjectionResult(items, excludedCount, HitItemCap: false);
    }

    private IQueryable<FeedCandidateRow> BuildFeedCandidateQuery(Guid tenantId)
    {
        var eligibleProducts = _dbContext.Products
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(product =>
                product.TenantId == tenantId &&
                !product.IsDeleted &&
                product.IsActive &&
                product.IsOnlineVisible &&
                product.PublishStatus == PublishStatus.Published);

        var simpleCandidates = eligibleProducts
            .Where(product => !product.HasVariants)
            .Select(product => new FeedCandidateRow
            {
                ProductId = product.Id,
                VariantId = null
            });

        var variantCandidates =
            from variant in _dbContext.ProductVariants
                .AsNoTracking()
                .IgnoreQueryFilters()
            join product in eligibleProducts.Where(product => product.HasVariants)
                on variant.ProductId equals product.Id
            where
                variant.TenantId == tenantId &&
                !variant.IsDeleted &&
                variant.IsActive &&
                variant.SemanticLifecycleState == ProductVariantSemanticLifecycleState.Current &&
                variant.SemanticSupersededAtUtc == null &&
                variant.SemanticReplacementVariantId == null &&
                ((variant.OnlinePrice.HasValue && variant.OnlinePrice.Value > 0m) || variant.Price > 0m)
            select new FeedCandidateRow
            {
                ProductId = product.Id,
                VariantId = variant.Id
            };

        return simpleCandidates
            .Concat(variantCandidates)
            .OrderBy(candidate => candidate.ProductId)
            .ThenBy(candidate => candidate.VariantId ?? Guid.Empty);
    }

    private async Task<(IReadOnlyList<FeedProduct> Items, int ExcludedCount)> ProjectCandidateBatchAsync(
        Tenant tenant,
        IReadOnlyList<FeedCandidateRow> candidates,
        string feedStorefrontBaseUrl,
        CancellationToken cancellationToken)
    {
        if (candidates.Count == 0)
        {
            return (Array.Empty<FeedProduct>(), 0);
        }

        var tenantId = tenant.Id;
        var productIds = candidates.Select(candidate => candidate.ProductId).Distinct().ToList();
        var candidateVariantIds = candidates
            .Where(candidate => candidate.VariantId.HasValue)
            .Select(candidate => candidate.VariantId!.Value)
            .ToHashSet();
        var simpleProductIds = candidates
            .Where(candidate => !candidate.VariantId.HasValue)
            .Select(candidate => candidate.ProductId)
            .ToHashSet();

        var products = await _dbContext.Products
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(product =>
                product.TenantId == tenantId &&
                productIds.Contains(product.Id) &&
                !product.IsDeleted &&
                product.IsActive &&
                product.IsOnlineVisible &&
                product.PublishStatus == PublishStatus.Published)
            .Select(product => new ProductRow(
                product.Id,
                product.Name,
                product.Description,
                product.CategoryId,
                product.Brand,
                product.Manufacturer,
                product.Slug,
                product.SKU,
                product.Barcode,
                product.TrackStock,
                product.AllowBackorder,
                product.HasVariants))
            .ToListAsync(cancellationToken);

        if (products.Count == 0)
        {
            return (Array.Empty<FeedProduct>(), 0);
        }

        var categoryMap = await LoadCategoryMapAsync(
            tenantId,
            products
                .Where(product => product.CategoryId != Guid.Empty)
                .Select(product => product.CategoryId)
                .Distinct()
                .ToList(),
            cancellationToken);

        var variantRows = await _dbContext.ProductVariants
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(variant =>
                variant.TenantId == tenantId &&
                productIds.Contains(variant.ProductId) &&
                !variant.IsDeleted &&
                variant.IsActive &&
                variant.SemanticLifecycleState == ProductVariantSemanticLifecycleState.Current &&
                variant.SemanticSupersededAtUtc == null &&
                variant.SemanticReplacementVariantId == null &&
                (candidateVariantIds.Contains(variant.Id) || simpleProductIds.Contains(variant.ProductId)))
            .Select(variant => new VariantRow(
                variant.Id,
                variant.ProductId,
                variant.SKU,
                variant.Barcode,
                variant.Price,
                variant.OnlinePrice,
                variant.MediaId))
            .ToListAsync(cancellationToken);
        var variantsByProduct = variantRows
            .GroupBy(variant => variant.ProductId)
            .ToDictionary(group => group.Key, group => group.OrderBy(variant => variant.Id).ToList());

        var variantIds = variantRows.Select(variant => variant.Id).ToList();
        var stockByVariant = new Dictionary<Guid, decimal>();
        if (variantIds.Count > 0)
        {
            var stockRows = await _dbContext.ProductVariantStocks
                .AsNoTracking()
                .IgnoreQueryFilters()
                .WhereCurrentInventoryStockOwner(tenantId)
                .Where(stock => variantIds.Contains(stock.VariantId))
                .GroupBy(stock => stock.VariantId)
                .Select(group => new
                {
                    VariantId = group.Key,
                    Available = group.Sum(stock => stock.Quantity - stock.ReservedQuantity)
                })
                .ToListAsync(cancellationToken);

            foreach (var row in stockRows)
            {
                stockByVariant[row.VariantId] = Math.Max(0m, row.Available);
            }
        }

        var unitRows = await _dbContext.ProductUnits
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(unit =>
                simpleProductIds.Contains(unit.ProductId) &&
                !unit.IsDeleted &&
                unit.IsDefault &&
                unit.Unit != null &&
                !unit.Unit.IsDeleted &&
                unit.Unit.TenantId == tenantId)
            .Select(unit => new UnitRow(unit.Id, unit.ProductId, unit.SellingPrice, unit.IsBaseUnit))
            .ToListAsync(cancellationToken);
        var defaultUnitPriceByProduct = unitRows
            .GroupBy(unit => unit.ProductId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(unit => unit.IsBaseUnit)
                    .ThenBy(unit => unit.Id)
                    .First()
                    .SellingPrice);

        var mediaRows = await _dbContext.ProductMedia
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(media =>
                media.TenantId == tenantId &&
                productIds.Contains(media.ProductId) &&
                media.MediaType == "image")
            .OrderBy(media => media.Position)
            .ThenBy(media => media.CreatedAtUtc)
            .Select(media => new { media.Id, media.ProductId, media.Url })
            .ToListAsync(cancellationToken);
        var mediaByProduct = new Dictionary<Guid, List<MediaRef>>();
        foreach (var media in mediaRows)
        {
            if (!FeedPublicUrlReadiness.IsPublicImageUrl(media.Url, out _))
            {
                continue;
            }

            if (!mediaByProduct.TryGetValue(media.ProductId, out var gallery))
            {
                gallery = new List<MediaRef>();
                mediaByProduct[media.ProductId] = gallery;
            }
            gallery.Add(new MediaRef(media.Id, media.Url));
        }

        var promotionRows = await _dbContext.Promotions
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(promotion =>
                promotion.TenantId == tenantId &&
                !promotion.IsDeleted &&
                promotion.Scope == PromotionScope.Product &&
                promotion.AppliesToId != null &&
                productIds.Contains(promotion.AppliesToId.Value))
            .ToListAsync(cancellationToken);
        var promotionsByProduct = promotionRows
            .GroupBy(promotion => promotion.AppliesToId!.Value)
            .ToDictionary(group => group.Key, group => group.ToList());

        var nowUtc = DateTime.UtcNow;
        var items = new List<FeedProduct>();
        var excludedCount = 0;

        foreach (var product in products.OrderBy(product => product.Id))
        {
            string? categoryName = null;
            if (product.CategoryId != Guid.Empty)
            {
                categoryName = ResolveLiveCategoryName(categoryMap, product.CategoryId);
                if (categoryName == null)
                {
                    excludedCount++;
                    _logger.LogInformation(
                        "Feed item excluded: live category unresolved | TenantId: {TenantId} | ProductId: {ProductId} | CategoryId: {CategoryId}",
                        tenantId, product.Id, product.CategoryId);
                    continue;
                }
            }

            if (string.IsNullOrWhiteSpace(product.Slug))
            {
                excludedCount++;
                _logger.LogInformation(
                    "Feed item excluded: missing product slug | TenantId: {TenantId} | ProductId: {ProductId}",
                    tenantId, product.Id);
                continue;
            }

            var productUrl = _urlResolver.BuildProductUrl(tenant, product.Slug);
            var gallery = mediaByProduct.TryGetValue(product.Id, out var productMedia)
                ? productMedia
                : new List<MediaRef>();
            var winningPromotion = promotionsByProduct.TryGetValue(product.Id, out var productPromotions)
                ? PromotionVisibilityProjectionSupport.SelectWinningProductPromotion(productPromotions, nowUtc)
                : null;
            var brand = CleanOptional(product.Brand) ?? CleanOptional(product.Manufacturer);
            var description = SanitizeDescription(product.Description);
            var title = CleanOptional(product.Name);

            if (!product.HasVariants)
            {
                var basePrice = defaultUnitPriceByProduct.TryGetValue(product.Id, out var unitPrice)
                    ? unitPrice
                    : null;
                if (basePrice == null || basePrice <= 0m)
                {
                    excludedCount++;
                    _logger.LogInformation(
                        "Feed item excluded: non-positive default unit price | TenantId: {TenantId} | ProductId: {ProductId}",
                        tenantId, product.Id);
                    continue;
                }

                var productStock = variantsByProduct.TryGetValue(product.Id, out var simpleVariants)
                    ? simpleVariants.Sum(variant => stockByVariant.GetValueOrDefault(variant.Id))
                    : 0m;

                if (!TryCreateItem(
                        feedItemId: product.Id,
                        productId: product.Id,
                        variantId: null,
                        itemGroupId: null,
                        sku: CleanOptional(product.Sku),
                        gtin: TryBuildGtin(product.Barcode),
                        price: basePrice.Value,
                        winningPromotion,
                        availability: MapAvailability(product.TrackStock, product.AllowBackorder, productStock),
                        primaryImageUrl: gallery.FirstOrDefault()?.Url ?? string.Empty,
                        additionalImageUrls: gallery.Skip(1).Select(media => media.Url).ToList(),
                        tenant, title, description, productUrl, brand, categoryName,
                        product.Slug,
                        feedStorefrontBaseUrl,
                        out var item))
                {
                    excludedCount++;
                    continue;
                }
                items.Add(item);
            }
            else
            {
                var admittedVariants = variantsByProduct.TryGetValue(product.Id, out var productVariants)
                    ? productVariants
                        .Where(variant => EffectivePrice(variant.Price, variant.OnlinePrice) > 0m)
                        .OrderBy(variant => variant.Id)
                        .ToList()
                    : new List<VariantRow>();

                foreach (var variant in admittedVariants)
                {
                    var variantPrice = EffectivePrice(variant.Price, variant.OnlinePrice);
                    var variantStock = stockByVariant.GetValueOrDefault(variant.Id);
                    string primaryImage = string.Empty;
                    var additionalImages = new List<string>();
                    var variantMedia = variant.MediaId.HasValue
                        ? gallery.FirstOrDefault(media => media.Id == variant.MediaId.Value)
                        : null;
                    if (variantMedia != null)
                    {
                        primaryImage = variantMedia.Url;
                        additionalImages = gallery
                            .Where(media => media.Id != variant.MediaId!.Value)
                            .Select(media => media.Url)
                            .ToList();
                    }
                    else if (gallery.Count > 0)
                    {
                        primaryImage = gallery[0].Url;
                        additionalImages = gallery.Skip(1).Select(media => media.Url).ToList();
                    }

                    if (!TryCreateItem(
                            feedItemId: variant.Id,
                            productId: product.Id,
                            variantId: variant.Id,
                            itemGroupId: product.Id,
                            sku: CleanOptional(variant.Sku),
                            gtin: TryBuildGtin(variant.Barcode),
                            price: variantPrice,
                            winningPromotion,
                            availability: MapAvailability(product.TrackStock, product.AllowBackorder, variantStock),
                            primaryImageUrl: primaryImage,
                            additionalImageUrls: additionalImages,
                            tenant, title, description, productUrl, brand, categoryName,
                            product.Slug,
                            feedStorefrontBaseUrl,
                            out var item))
                    {
                        excludedCount++;
                        continue;
                    }
                    items.Add(item);
                }
            }
        }

        return (items, excludedCount);
    }

    private async Task<IReadOnlyDictionary<Guid, CategoryRow>> LoadCategoryMapAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> categoryIds,
        CancellationToken cancellationToken)
    {
        var categoryMap = new Dictionary<Guid, CategoryRow>();
        var frontier = categoryIds.Where(id => id != Guid.Empty).ToHashSet();

        while (frontier.Count > 0)
        {
            var ids = frontier.ToList();
            frontier.Clear();

            var rows = await _dbContext.Categories
                .AsNoTracking()
                .IgnoreQueryFilters()
                .Where(category => category.TenantId == tenantId && ids.Contains(category.Id))
                .Select(category => new CategoryRow(
                    category.Id,
                    category.Name,
                    category.ParentCategoryId,
                    category.IsDeleted))
                .ToListAsync(cancellationToken);

            foreach (var row in rows)
            {
                if (!categoryMap.TryAdd(row.Id, row))
                {
                    continue;
                }

                if (row.ParentCategoryId.HasValue &&
                    row.ParentCategoryId.Value != Guid.Empty &&
                    !categoryMap.ContainsKey(row.ParentCategoryId.Value))
                {
                    frontier.Add(row.ParentCategoryId.Value);
                }
            }
        }

        return categoryMap;
    }

    /// <summary>
    /// Projects the full feed for one tenant using a fixed set of bounded SQL
    /// queries (products, categories, variants, variant stocks, default units,
    /// media, promotions) followed by in-memory composition. Public for direct
    /// contract testing against EF InMemory.
    /// </summary>
    public async Task<(IReadOnlyList<FeedProduct> Items, int ExcludedItemCount)> ProjectFeedProductsAsync(
        Tenant tenant,
        CancellationToken cancellationToken = default)
    {
        var tenantId = tenant.Id;
        var feedStorefrontBaseUrl = _urlResolver.Resolve(tenant);

        // Query 3: eligible products — mirrors the overlay visibility fold
        // (IsOnlineVisible && Published) plus IsActive. IsSearchable/IsFeatured
        // deliberately do not gate feed eligibility.
        var products = await _dbContext.Products
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(product =>
                product.TenantId == tenantId &&
                !product.IsDeleted &&
                product.IsActive &&
                product.IsOnlineVisible &&
                product.PublishStatus == PublishStatus.Published)
            .Select(product => new ProductRow(
                product.Id,
                product.Name,
                product.Description,
                product.CategoryId,
                product.Brand,
                product.Manufacturer,
                product.Slug,
                product.SKU,
                product.Barcode,
                product.TrackStock,
                product.AllowBackorder,
                product.HasVariants))
            .ToListAsync(cancellationToken);

        if (products.Count == 0)
        {
            return (Array.Empty<FeedProduct>(), 0);
        }

        var productIds = products.Select(product => product.Id).ToList();

        // Query 4: all tenant categories in one query; lineage validation and
        // cycle detection are reproduced in memory (ResolveLiveCategoryNameAsync
        // semantics from the overlay).
        var categoryRows = await _dbContext.Categories
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(category => category.TenantId == tenantId)
            .Select(category => new CategoryRow(
                category.Id,
                category.Name,
                category.ParentCategoryId,
                category.IsDeleted))
            .ToListAsync(cancellationToken);
        var categoryMap = categoryRows.ToDictionary(category => category.Id);

        // Query 5: admitted variants for all eligible products in one query.
        // Admission == IsCurrentSemanticOwner + IsActive (overlay semantics).
        var variantRows = await _dbContext.ProductVariants
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(variant =>
                variant.TenantId == tenantId &&
                productIds.Contains(variant.ProductId) &&
                !variant.IsDeleted &&
                variant.IsActive &&
                variant.SemanticLifecycleState == ProductVariantSemanticLifecycleState.Current &&
                variant.SemanticSupersededAtUtc == null &&
                variant.SemanticReplacementVariantId == null)
            .Select(variant => new VariantRow(
                variant.Id,
                variant.ProductId,
                variant.SKU,
                variant.Barcode,
                variant.Price,
                variant.OnlinePrice,
                variant.MediaId))
            .ToListAsync(cancellationToken);
        var variantsByProduct = variantRows
            .GroupBy(variant => variant.ProductId)
            .ToDictionary(group => group.Key, group => group.OrderBy(variant => variant.Id).ToList());

        // Query 6: variant stocks in one grouped query through the existing
        // active-warehouse inventory ownership authority. Formula matches
        // SumCurrentVariantStock: sum(Quantity - ReservedQuantity) clamped >= 0.
        var variantIds = variantRows.Select(variant => variant.Id).ToList();
        var stockByVariant = new Dictionary<Guid, decimal>();
        if (variantIds.Count > 0)
        {
            var stockRows = await _dbContext.ProductVariantStocks
                .AsNoTracking()
                .IgnoreQueryFilters()
                .WhereCurrentInventoryStockOwner(tenantId)
                .Where(stock => variantIds.Contains(stock.VariantId))
                .GroupBy(stock => stock.VariantId)
                .Select(group => new
                {
                    VariantId = group.Key,
                    Available = group.Sum(stock => stock.Quantity - stock.ReservedQuantity)
                })
                .ToListAsync(cancellationToken);

            foreach (var row in stockRows)
            {
                stockByVariant[row.VariantId] = Math.Max(0m, row.Available);
            }
        }

        // Query 7: default units in one query — simple-product price authority.
        var unitRows = await _dbContext.ProductUnits
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(unit =>
                productIds.Contains(unit.ProductId) &&
                !unit.IsDeleted &&
                unit.IsDefault &&
                unit.Unit != null &&
                !unit.Unit.IsDeleted &&
                unit.Unit.TenantId == tenantId)
            .Select(unit => new UnitRow(unit.Id, unit.ProductId, unit.SellingPrice, unit.IsBaseUnit))
            .ToListAsync(cancellationToken);
        var defaultUnitPriceByProduct = unitRows
            .GroupBy(unit => unit.ProductId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(unit => unit.IsBaseUnit)
                    .ThenBy(unit => unit.Id)
                    .First()
                    .SellingPrice);

        // Query 8: image media in one query, ordered Position -> CreatedAtUtc
        // (storefront gallery order).
        var mediaRows = await _dbContext.ProductMedia
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(media =>
                media.TenantId == tenantId &&
                productIds.Contains(media.ProductId) &&
                media.MediaType == "image")
            .OrderBy(media => media.Position)
            .ThenBy(media => media.CreatedAtUtc)
            .Select(media => new { media.Id, media.ProductId, media.Url })
            .ToListAsync(cancellationToken);
        var mediaByProduct = new Dictionary<Guid, List<MediaRef>>();
        foreach (var media in mediaRows)
        {
            // Only feed-public image URLs may ever reach external catalogs.
            // Invalid/private/signed URLs are rejected here, without fetching.
            if (!FeedPublicUrlReadiness.IsPublicImageUrl(media.Url, out _))
            {
                continue;
            }

            if (!mediaByProduct.TryGetValue(media.ProductId, out var gallery))
            {
                gallery = new List<MediaRef>();
                mediaByProduct[media.ProductId] = gallery;
            }
            gallery.Add(new MediaRef(media.Id, media.Url));
        }

        // Query 9: product-scope promotions in one query; winner per product via
        // the existing single-winner overlay support.
        var promotionRows = await _dbContext.Promotions
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(promotion =>
                promotion.TenantId == tenantId &&
                !promotion.IsDeleted &&
                promotion.Scope == PromotionScope.Product &&
                promotion.AppliesToId != null &&
                productIds.Contains(promotion.AppliesToId.Value))
            .ToListAsync(cancellationToken);
        var promotionsByProduct = promotionRows
            .GroupBy(promotion => promotion.AppliesToId!.Value)
            .ToDictionary(group => group.Key, group => group.ToList());

        var nowUtc = DateTime.UtcNow;
        var items = new List<FeedProduct>();
        var excludedCount = 0;

        foreach (var product in products.OrderBy(product => product.Id))
        {
            // Live category lineage: deleted/missing/cycled category excludes the
            // product (only when the product actually points at a category).
            string? categoryName = null;
            if (product.CategoryId != Guid.Empty)
            {
                categoryName = ResolveLiveCategoryName(categoryMap, product.CategoryId);
                if (categoryName == null)
                {
                    excludedCount++;
                    _logger.LogInformation(
                        "Feed item excluded: live category unresolved | TenantId: {TenantId} | ProductId: {ProductId} | CategoryId: {CategoryId}",
                        tenantId, product.Id, product.CategoryId);
                    continue;
                }
            }

            if (string.IsNullOrWhiteSpace(product.Slug))
            {
                excludedCount++;
                _logger.LogInformation(
                    "Feed item excluded: missing product slug | TenantId: {TenantId} | ProductId: {ProductId}",
                    tenantId, product.Id);
                continue;
            }

            var productUrl = _urlResolver.BuildProductUrl(tenant, product.Slug);
            var gallery = mediaByProduct.TryGetValue(product.Id, out var productMedia)
                ? productMedia
                : new List<MediaRef>();
            var winningPromotion = promotionsByProduct.TryGetValue(product.Id, out var productPromotions)
                ? PromotionVisibilityProjectionSupport.SelectWinningProductPromotion(productPromotions, nowUtc)
                : null;
            var brand = CleanOptional(product.Brand) ?? CleanOptional(product.Manufacturer);
            var description = SanitizeDescription(product.Description);
            var title = CleanOptional(product.Name);

            if (!product.HasVariants)
            {
                var basePrice = defaultUnitPriceByProduct.TryGetValue(product.Id, out var unitPrice)
                    ? unitPrice
                    : null;
                if (basePrice == null || basePrice <= 0m)
                {
                    excludedCount++;
                    _logger.LogInformation(
                        "Feed item excluded: non-positive default unit price | TenantId: {TenantId} | ProductId: {ProductId}",
                        tenantId, product.Id);
                    continue;
                }

                // Simple products mirror the overlay: product-level stock is the
                // sum of current variant stocks when any exist (typically zero).
                var productStock = variantsByProduct.TryGetValue(product.Id, out var simpleVariants)
                    ? simpleVariants.Sum(variant => stockByVariant.GetValueOrDefault(variant.Id))
                    : 0m;

                if (!TryCreateItem(
                        feedItemId: product.Id,
                        productId: product.Id,
                        variantId: null,
                        itemGroupId: null,
                        sku: CleanOptional(product.Sku),
                        gtin: TryBuildGtin(product.Barcode),
                        price: basePrice.Value,
                        winningPromotion,
                        availability: MapAvailability(product.TrackStock, product.AllowBackorder, productStock),
                        primaryImageUrl: gallery.FirstOrDefault()?.Url ?? string.Empty,
                        additionalImageUrls: gallery.Skip(1).Select(media => media.Url).ToList(),
                        tenant, title, description, productUrl, brand, categoryName,
                        product.Slug,
                        feedStorefrontBaseUrl,
                        out var item))
                {
                    excludedCount++;
                    continue;
                }
                items.Add(item);
            }
            else
            {
                var admittedVariants = variantsByProduct.TryGetValue(product.Id, out var productVariants)
                    ? productVariants.Where(variant => EffectivePrice(variant.Price, variant.OnlinePrice) > 0m).ToList()
                    : new List<VariantRow>();

                if (admittedVariants.Count == 0)
                {
                    excludedCount++;
                    _logger.LogInformation(
                        "Feed item excluded: no admitted variants | TenantId: {TenantId} | ProductId: {ProductId}",
                        tenantId, product.Id);
                    continue;
                }

                foreach (var variant in admittedVariants)
                {
                    var variantPrice = EffectivePrice(variant.Price, variant.OnlinePrice);
                    var variantStock = stockByVariant.GetValueOrDefault(variant.Id);

                    // Variant image by MediaId when it points into the product
                    // gallery; otherwise fall back to the gallery order.
                    string primaryImage = string.Empty;
                    var additionalImages = new List<string>();
                    var variantMedia = variant.MediaId.HasValue
                        ? gallery.FirstOrDefault(media => media.Id == variant.MediaId.Value)
                        : null;
                    if (variantMedia != null)
                    {
                        primaryImage = variantMedia.Url;
                        additionalImages = gallery
                            .Where(media => media.Id != variant.MediaId!.Value)
                            .Select(media => media.Url)
                            .ToList();
                    }
                    else if (gallery.Count > 0)
                    {
                        primaryImage = gallery[0].Url;
                        additionalImages = gallery.Skip(1).Select(media => media.Url).ToList();
                    }

                    if (!TryCreateItem(
                            feedItemId: variant.Id,
                            productId: product.Id,
                            variantId: variant.Id,
                            itemGroupId: product.Id,
                            sku: CleanOptional(variant.Sku),
                            gtin: TryBuildGtin(variant.Barcode),
                            price: variantPrice,
                            winningPromotion,
                            availability: MapAvailability(product.TrackStock, product.AllowBackorder, variantStock),
                            primaryImageUrl: primaryImage,
                            additionalImageUrls: additionalImages,
                            tenant, title, description, productUrl, brand, categoryName,
                            product.Slug,
                            feedStorefrontBaseUrl,
                            out var item))
                    {
                        excludedCount++;
                        continue;
                    }
                    items.Add(item);
                }
            }
        }

        return (items, excludedCount);
    }

    // =========================================================================
    // Projection helpers
    // =========================================================================

    private bool TryCreateItem(
        Guid feedItemId,
        Guid productId,
        Guid? variantId,
        Guid? itemGroupId,
        string? sku,
        string? gtin,
        decimal price,
        Promotion? winningPromotion,
        string availability,
        string primaryImageUrl,
        IReadOnlyList<string> additionalImageUrls,
        Tenant tenant,
        string? title,
        string description,
        string productUrl,
        string? brand,
        string? categoryName,
        string productSlug,
        string feedStorefrontBaseUrl,
        out FeedProduct item)
    {
        item = null!;

        // Item validation (Feed-0): required = title, product URL, primary image,
        // positive price, availability, condition, feed id. Failure excludes the
        // item only — the feed continues.
        var productUrlReady = _readiness.IsPublicProductUrl(
            productUrl,
            feedStorefrontBaseUrl,
            productSlug,
            out var productUrlReason);
        var primaryImageReady = FeedPublicUrlReadiness.IsPublicImageUrl(primaryImageUrl, out var primaryImageReason);

        if (feedItemId == Guid.Empty ||
            string.IsNullOrWhiteSpace(title) ||
            !productUrlReady ||
            !primaryImageReady ||
            price <= 0m ||
            string.IsNullOrWhiteSpace(availability))
        {
            _logger.LogInformation(
                "Feed item excluded: required field invalid | TenantId: {TenantId} | ProductId: {ProductId} | VariantId: {VariantId} | ProductUrlReason: {ProductUrlReason} | PrimaryImageReason: {PrimaryImageReason}",
                tenant.Id,
                productId,
                variantId,
                productUrlReason,
                primaryImageReason);
            return false;
        }

        var feedReadyAdditionalImages = additionalImageUrls
            .Where(url => FeedPublicUrlReadiness.IsPublicImageUrl(url, out _))
            .ToList();

        var salePrice = winningPromotion == null
            ? (decimal?)null
            : PromotionVisibilityProjectionSupport.ComposeVisiblePrice(price, winningPromotion);
        if (salePrice.HasValue && salePrice.Value >= price)
        {
            salePrice = null;
        }

        item = new FeedProduct
        {
            FeedItemId = feedItemId,
            ProductId = productId,
            VariantId = variantId,
            ItemGroupId = itemGroupId,
            Title = Truncate(title, TitleMaxLength),
            Description = description,
            ProductUrl = productUrl,
            PrimaryImageUrl = primaryImageUrl,
            AdditionalImageUrls = feedReadyAdditionalImages,
            Price = price,
            SalePrice = salePrice,
            Currency = tenant.Currency,
            Availability = availability,
            Condition = ConditionNew,
            Brand = brand,
            Sku = sku,
            Gtin = gtin,
            CategoryName = categoryName
        };
        return true;
    }

    private static decimal EffectivePrice(decimal price, decimal? onlinePrice) =>
        onlinePrice is > 0m ? onlinePrice.Value : price;

    /// <summary>
    /// Feed-0 availability mapping: untracked -> in stock; tracked with stock
    /// -> in stock; tracked zero + backorder -> available for order;
    /// tracked zero no backorder -> out of stock. Zero-stock items are retained.
    /// </summary>
    private static string MapAvailability(bool trackStock, bool allowBackorder, decimal stock)
    {
        if (!trackStock || stock > 0m)
        {
            return FeedAvailability.InStock;
        }

        return allowBackorder ? FeedAvailability.AvailableForOrder : FeedAvailability.OutOfStock;
    }

    /// <summary>
    /// In-memory reproduction of the overlay's ResolveLiveCategoryNameAsync:
    /// walks parent lineage with cycle detection; deleted/missing node or cycle
    /// -> null (caller excludes the product). Leaf category name is returned.
    /// </summary>
    private static string? ResolveLiveCategoryName(
        IReadOnlyDictionary<Guid, CategoryRow> categoryMap,
        Guid categoryId)
    {
        var visited = new HashSet<Guid>();
        var currentId = categoryId;
        string? leafName = null;

        while (currentId != Guid.Empty)
        {
            if (!visited.Add(currentId))
            {
                return null;
            }

            if (!categoryMap.TryGetValue(currentId, out var category))
            {
                return null;
            }

            if (category.IsDeleted)
            {
                return null;
            }

            leafName ??= category.Name;

            if (category.ParentCategoryId == null || category.ParentCategoryId.Value == Guid.Empty)
            {
                return leafName;
            }

            if (category.ParentCategoryId.Value == category.Id)
            {
                return null;
            }

            currentId = category.ParentCategoryId.Value;
        }

        return leafName;
    }

    /// <summary>
    /// GTIN is emitted only when the barcode is all digits with a Meta-accepted
    /// digit length (8/12/13/14); anything else is omitted.
    /// </summary>
    private static string? TryBuildGtin(string? barcode)
    {
        var candidate = barcode?.Trim();
        if (string.IsNullOrEmpty(candidate))
        {
            return null;
        }

        return candidate.All(char.IsDigit) && ValidGtinLengths.Contains(candidate.Length)
            ? candidate
            : null;
    }

    private static string? CleanOptional(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    /// <summary>
    /// Strips HTML tags and collapses whitespace (same tag-strip precedent as
    /// AdminSearchSyncController); truncates to the Meta description limit.
    /// </summary>
    private static string SanitizeDescription(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var withoutTags = HtmlTagRegex().Replace(html, " ");
        var collapsed = WhitespaceRegex().Replace(withoutTags, " ").Trim();
        return Truncate(collapsed, DescriptionMaxLength);
    }

    [GeneratedRegex("<.*?>")]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    // =========================================================================
    // Bounded query row shapes
    // =========================================================================

    private sealed record ProductRow(
        Guid Id,
        string? Name,
        string? Description,
        Guid CategoryId,
        string? Brand,
        string? Manufacturer,
        string? Slug,
        string? Sku,
        string? Barcode,
        bool TrackStock,
        bool AllowBackorder,
        bool HasVariants);

    private sealed record CategoryRow(Guid Id, string Name, Guid? ParentCategoryId, bool IsDeleted);

    private sealed record VariantRow(
        Guid Id,
        Guid ProductId,
        string? Sku,
        string? Barcode,
        decimal Price,
        decimal? OnlinePrice,
        Guid? MediaId);

    private sealed record UnitRow(Guid Id, Guid ProductId, decimal? SellingPrice, bool IsBaseUnit);

    private sealed record MediaRef(Guid Id, string Url);

    private sealed class FeedCandidateRow
    {
        public Guid ProductId { get; init; }
        public Guid? VariantId { get; init; }
    }
}
