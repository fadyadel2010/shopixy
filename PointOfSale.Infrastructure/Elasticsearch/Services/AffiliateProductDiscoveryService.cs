using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nest;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Elasticsearch.Configuration;
using PointOfSale.Infrastructure.Elasticsearch.Documents;
using PointOfSale.Infrastructure.MongoDB.Documents;

namespace PointOfSale.Infrastructure.Elasticsearch.Services;

/// <summary>
/// AF-5: Affiliate Product Discovery Service.
///
/// ARCHITECTURE RULES:
/// 1. Elasticsearch is the ONLY data source — no SQL, no MongoDB
/// 2. Always filters: isAffiliateEnabled=true, isActive=true, isInStock=true
/// 3. Commission rate is NEVER included in results
/// 4. Supports text search + category filter + pagination
/// 5. Sorted by relevance then affiliateEnabledAtUtc desc
/// </summary>
public interface IAffiliateProductDiscoveryService
{
    Task<AffiliateDiscoveryResult> DiscoverAsync(
        Guid tenantId,
        AffiliateDiscoveryQuery query,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Query parameters for affiliate product discovery.
/// </summary>
public class AffiliateDiscoveryQuery
{
    public string? Q { get; set; }
    public Guid? CategoryId { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

/// <summary>
/// Result DTO for affiliate discovery.
/// CRITICAL: No commission rate, no sensitive fields.
/// </summary>
public class AffiliateDiscoveryResult
{
    public int Page { get; set; }
    public int PageSize { get; set; }
    public long Total { get; set; }
    public List<AffiliateDiscoveryItem> Items { get; set; } = new();
}

/// <summary>
/// Individual product item in affiliate discovery.
/// Only safe, public-facing fields exposed.
/// 
/// AF-17A: INVENTORY & PRICING TRUTH ALIGNMENT
/// - Fields mirror Storefront's exact data structure
/// - No frontend calculations allowed
/// </summary>
public class AffiliateDiscoveryItem
{
    public Guid ProductId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Slug { get; set; }
    public string? ImageUrl { get; set; }
    
    /// <summary>
    /// AF-17A: Display price for simple products (no variants).
    /// Null if HasVariants = true.
    /// </summary>
    public decimal? Price { get; set; }
    
    /// <summary>
    /// AF-17A: Minimum variant price (for products with variants).
    /// Null if HasVariants = false.
    /// </summary>
    public decimal? MinVariantPrice { get; set; }
    
    /// <summary>
    /// AF-17A: Maximum variant price (for products with variants).
    /// Null if HasVariants = false.
    /// </summary>
    public decimal? MaxVariantPrice { get; set; }
    
    /// <summary>
    /// DEPRECATED: Use Price or MinVariantPrice/MaxVariantPrice instead.
    /// Kept for backward compatibility.
    /// </summary>
    [Obsolete("Use Price or MinVariantPrice/MaxVariantPrice based on HasVariants")]
    public decimal? PriceFrom => Price ?? MinVariantPrice;
    
    public string? Currency { get; set; }
    
    /// <summary>
    /// AF-17A: Whether product has variants requiring selection.
    /// </summary>
    public bool HasVariants { get; set; }
    
    /// <summary>
    /// AF-17A: Total available stock across all variants/warehouses.
    /// Computed using Storefront's exact logic.
    /// </summary>
    public int TotalAvailableStock { get; set; }
    
    /// <summary>
    /// AF-17A: Whether product is in stock.
    /// Computed using Storefront's exact logic.
    /// </summary>
    public bool InStock { get; set; }
    
    public string? CategoryName { get; set; }
    
    /// <summary>
    /// AF-16E: Authoritative storefront base URL for this product's tenant.
    /// CRITICAL: Each product carries its own tenant's domain.
    /// </summary>
    public string StorefrontBaseUrl { get; set; } = string.Empty;
    
    /// <summary>
    /// AF-16E: TenantId for fail-fast validation.
    /// </summary>
    public Guid TenantId { get; set; }
}

public class AffiliateProductDiscoveryService : IAffiliateProductDiscoveryService
{
    private const int CandidateBatchSize = 500;
    private static readonly TimeSpan CandidateScrollTimeout = TimeSpan.FromMinutes(2);

    private readonly IElasticClient _client;
    private readonly ApplicationDbContext _dbContext;
    private readonly IConfiguration _configuration;
    private readonly ElasticsearchSettings _settings;
    private readonly ILogger<AffiliateProductDiscoveryService> _logger;

    public AffiliateProductDiscoveryService(
        IElasticClient client,
        ApplicationDbContext dbContext,
        IConfiguration configuration,
        IOptions<ElasticsearchSettings> settings,
        ILogger<AffiliateProductDiscoveryService> logger)
    {
        _client = client;
        _dbContext = dbContext;
        _configuration = configuration;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<AffiliateDiscoveryResult> DiscoverAsync(
        Guid tenantId,
        AffiliateDiscoveryQuery query,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        if (!_settings.Enabled)
        {
            _logger.LogWarning("Elasticsearch disabled — returning empty affiliate discovery result");
            return new AffiliateDiscoveryResult
            {
                Page = query.Page,
                PageSize = query.PageSize
            };
        }

        try
        {
            // PATCH F: Clamp page size to prevent abuse (e.g. pageSize=10000)
            query.PageSize = Math.Clamp(query.PageSize, 1, 100);
            if (query.Page < 1) query.Page = 1;

            _logger.LogInformation(
                "Affiliate discovery | TenantId: {TenantId} | Query: {Query} | CategoryId: {CategoryId} | Page: {Page}",
                tenantId, query.Q ?? "(browse)", query.CategoryId, query.Page);

            // ═══════════════════════════════════════════════════════════════════
            // BUILD FILTERS — always include affiliate + active + stock
            // ═══════════════════════════════════════════════════════════════════

            var filters = new List<Func<QueryContainerDescriptor<PublicAffiliateProductSearchDocument>, QueryContainer>>
            {
                // Tenant isolation is NOT applied per tenant because discovery is across all tenants
                // However, if we need to filter by tenant in discovery specifically, we could
                
                // CRITICAL: Only affiliate-enabled products
                f => f.Term(t => t.Field(p => p.IsAffiliateEnabled).Value(true)),
                // Must be active
                f => f.Term(t => t.Field(p => p.IsActive).Value(true)),
                // Must remain storefront-purchasable on the public affiliate surface
                f => f.Term(t => t.Field(p => p.InStock).Value(true))
            };

            // ═══════════════════════════════════════════════════════════════════
            // BUILD QUERY
            // ═══════════════════════════════════════════════════════════════════

            ISearchResponse<PublicAffiliateProductSearchDocument> searchResponse;

            if (!string.IsNullOrWhiteSpace(query.Q))
            {
                // Text search mode: multi_match + filters
                searchResponse = await _client.SearchAsync<PublicAffiliateProductSearchDocument>(s => s
                    .Index(_settings.PublicAffiliateProductsIndexName)
                    .Size(CandidateBatchSize)
                    .Scroll(CandidateScrollTimeout)
                    .Query(q => q
                        .Bool(b => b
                            .Must(m => m.MultiMatch(mm => mm
                                .Query(query.Q)
                                .Fields(f => f
                                    .Field(p => p.ProductName, boost: 4)
                                    .Field(p => p.ProductSlug, boost: 3)
                                    .Field(p => p.Description, boost: 2)
                                    .Field(p => p.CategoryName)
                                )
                                .Type(TextQueryType.BestFields)
                                .Fuzziness(Fuzziness.Auto)
                            ))
                            .Filter(filters.ToArray())
                        )
                    )
                    .Sort(ss => ss
                        .Descending(SortSpecialField.Score)
                        .Descending(p => p.AffiliateEnabledAtUtc)
                    )
                    .TrackTotalHits(true),
                    cancellationToken);
            }
            else
            {
                // Browse mode: no text query, just filters
                searchResponse = await _client.SearchAsync<PublicAffiliateProductSearchDocument>(s => s
                    .Index(_settings.PublicAffiliateProductsIndexName)
                    .Size(CandidateBatchSize)
                    .Scroll(CandidateScrollTimeout)
                    .Query(q => q
                        .Bool(b => b
                            .Filter(filters.ToArray())
                        )
                    )
                    .Sort(ss => ss
                        .Descending(p => p.AffiliateEnabledAtUtc)
                    )
                    .TrackTotalHits(true),
                    cancellationToken);
            }

            if (!searchResponse.IsValid)
            {
                _logger.LogError(
                    "Affiliate discovery failed | TenantId: {TenantId} | Error: {Error}",
                    tenantId, searchResponse.ServerError?.Error?.Reason ?? "Unknown");

                return new AffiliateDiscoveryResult
                {
                    Page = query.Page,
                    PageSize = query.PageSize
                };
            }

            // ═══════════════════════════════════════════════════════════════════
            // MAP RESULTS — no commission rate, only safe fields
            // AF-17A: Map using Storefront-aligned inventory and pricing fields
            // ═══════════════════════════════════════════════════════════════════

            var candidates = await ReadAllSearchCandidatesAsync(searchResponse, cancellationToken);
            var deliverableItems = await RecomposeDiscoveryItemsFromSqlAuthorityAsync(
                candidates,
                query.CategoryId,
                cancellationToken);
            
            // AF-16F: STRICT MODE — Fail-fast if any product lacks StorefrontBaseUrl
            // This prevents silent data corruption and ensures data integrity
            var invalidProducts = deliverableItems.Where(p => string.IsNullOrWhiteSpace(p.StorefrontBaseUrl)).ToList();
            if (invalidProducts.Any())
            {
                _logger.LogError(
                    "[AF-16F] CRITICAL: Affiliate discovery data integrity violation | " +
                    "Count: {Count} | ProductIds: {ProductIds} | " +
                    "This indicates projection failure or incomplete reindex.",
                    invalidProducts.Count,
                    string.Join(", ", invalidProducts.Select(p => p.ProductId)));
                
                throw new InvalidOperationException(
                    $"Affiliate discovery data integrity violation: {invalidProducts.Count} products missing StorefrontBaseUrl. " +
                    "This indicates index corruption or incomplete projection. Products must be re-indexed. " +
                    "Run: af16e-trigger-reindex-via-events.sql or af16e-reindex-products.ps1");
            }

            var total = deliverableItems.Count;
            var skip = (long)(query.Page - 1) * query.PageSize;
            var pageItems = skip >= total
                ? []
                : deliverableItems.Skip((int)skip).Take(query.PageSize).ToList();

            stopwatch.Stop();

            _logger.LogInformation(
                "Affiliate discovery success | TenantId: {TenantId} | Results: {Count}/{Total} | TimeMs: {TimeMs}",
                tenantId, pageItems.Count, total, stopwatch.ElapsedMilliseconds);

            return new AffiliateDiscoveryResult
            {
                Page = query.Page,
                PageSize = query.PageSize,
                Total = total,
                Items = pageItems
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex,
                "Affiliate discovery exception | TenantId: {TenantId} | Query: {Query}",
                tenantId, query.Q ?? "(browse)");

            return new AffiliateDiscoveryResult
            {
                Page = query.Page,
                PageSize = query.PageSize
            };
        }
    }

    private async Task<List<PublicAffiliateProductSearchDocument>> ReadAllSearchCandidatesAsync(
        ISearchResponse<PublicAffiliateProductSearchDocument> initialResponse,
        CancellationToken cancellationToken)
    {
        var candidates = new List<PublicAffiliateProductSearchDocument>();
        var response = initialResponse;
        var scrollId = response.ScrollId;

        try
        {
            while (response.Documents.Count > 0)
            {
                candidates.AddRange(response.Documents);

                if (string.IsNullOrWhiteSpace(scrollId))
                {
                    break;
                }

                response = await _client.ScrollAsync<PublicAffiliateProductSearchDocument>(
                    CandidateScrollTimeout,
                    scrollId,
                    ct: cancellationToken);

                if (!response.IsValid)
                {
                    _logger.LogError(
                        "Affiliate discovery candidate scroll failed | Error: {Error}",
                        response.ServerError?.Error?.Reason ?? "Unknown");

                    throw new InvalidOperationException("Affiliate discovery candidate scan failed.");
                }

                scrollId = response.ScrollId;
            }
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(scrollId))
            {
                await _client.ClearScrollAsync(
                    clear => clear.ScrollId(scrollId),
                    cancellationToken);
            }
        }

        return candidates;
    }

    private async Task<List<AffiliateDiscoveryItem>> RecomposeDiscoveryItemsFromSqlAuthorityAsync(
        IReadOnlyList<PublicAffiliateProductSearchDocument> candidates,
        Guid? categoryId,
        CancellationToken cancellationToken)
    {
        var recomposed = new List<AffiliateDiscoveryItem>(candidates.Count);

        foreach (var candidate in candidates)
        {
            if (candidate.TenantId == Guid.Empty || candidate.ProductId == Guid.Empty)
            {
                continue;
            }

            var policyExists = await _dbContext.AffiliateProductPolicies
                .AsNoTracking()
                .IgnoreQueryFilters()
                .Join(
                    _dbContext.AffiliateCategories.AsNoTracking().IgnoreQueryFilters(),
                    policy => policy.AffiliateCategoryId,
                    category => (Guid?)category.Id,
                    (policy, category) => new { Policy = policy, Category = category })
                .AnyAsync(item =>
                    item.Policy.TenantId == candidate.TenantId &&
                    item.Policy.ProductId == candidate.ProductId &&
                    item.Policy.IsEnabled &&
                    !item.Policy.IsDeleted &&
                    item.Category.IsActive &&
                    !item.Category.IsDeleted &&
                    ((item.Category.Level == 1 && item.Category.ParentId == null) ||
                     (item.Category.Level == 2 &&
                      item.Category.ParentId != null &&
                      _dbContext.AffiliateCategories
                          .IgnoreQueryFilters()
                          .Any(parent =>
                              parent.Id == item.Category.ParentId.Value &&
                              parent.Level == 1 &&
                              parent.IsActive &&
                              !parent.IsDeleted))),
                    cancellationToken);

            if (!policyExists)
            {
                continue;
            }

            var product = await ProductSearchSqlAuthorityOverlay.ApplyAsync(
                _dbContext,
                new ProductDocument
                {
                    TenantId = candidate.TenantId,
                    ProductId = candidate.ProductId
                },
                candidate.TenantId,
                cancellationToken);

            if (product is not { IsActive: true, IsOnlineVisible: true, IsSearchable: true } ||
                (categoryId.HasValue && product.CategoryId != categoryId.Value) ||
                !product.TryResolveAffiliatePublicPricing(out var pricing) ||
                !pricing.InStock)
            {
                continue;
            }

            var tenant = await _dbContext.Tenants
                .AsNoTracking()
                .IgnoreQueryFilters()
                .Where(item =>
                    item.Id == candidate.TenantId &&
                    item.IsActive &&
                    item.DeletedAtUtc == null)
                .Select(item => new
                {
                    item.Name,
                    item.Slug,
                    item.StorefrontBaseUrl,
                    item.Currency
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (tenant == null ||
                !TryResolveStorefrontBaseUrl(tenant.StorefrontBaseUrl, tenant.Slug, out var storefrontBaseUrl))
            {
                continue;
            }

            recomposed.Add(new AffiliateDiscoveryItem
            {
                ProductId = product.ProductId,
                Name = product.Name,
                Slug = product.Slug,
                ImageUrl = ResolvePrimaryProductImage(product),
                Price = pricing.Price,
                MinVariantPrice = pricing.MinVariantPrice,
                MaxVariantPrice = pricing.MaxVariantPrice,
                Currency = tenant.Currency,
                HasVariants = product.HasVariants,
                TotalAvailableStock = pricing.TotalAvailableStock,
                InStock = pricing.InStock,
                CategoryName = product.CategoryName,
                StorefrontBaseUrl = storefrontBaseUrl,
                TenantId = candidate.TenantId
            });
        }

        if (recomposed.Count != candidates.Count)
        {
            _logger.LogWarning(
                "Affiliate discovery converged from SQL authority | CandidateCount: {CandidateCount} | SqlVisibleCount: {SqlVisibleCount}",
                candidates.Count,
                recomposed.Count);
        }

        return recomposed;
    }

    private bool TryResolveStorefrontBaseUrl(
        string? storefrontBaseUrl,
        string? tenantSlug,
        out string resolved)
    {
        if (!string.IsNullOrWhiteSpace(storefrontBaseUrl))
        {
            resolved = storefrontBaseUrl.TrimEnd('/');
            return true;
        }

        var urlTemplate = _configuration["Storefront:UrlTemplate"];
        if (!string.IsNullOrWhiteSpace(urlTemplate) && !string.IsNullOrWhiteSpace(tenantSlug))
        {
            resolved = urlTemplate.Replace("{slug}", tenantSlug).TrimEnd('/');
            return true;
        }

        resolved = string.Empty;
        return false;
    }

    private static string? ResolvePrimaryProductImage(ProductDocument product)
    {
        return product.Media
            .Where(media => string.Equals(media.Type, "image", StringComparison.OrdinalIgnoreCase))
            .OrderBy(media => media.Position)
            .Select(media => media.Url)
            .FirstOrDefault(url => !string.IsNullOrWhiteSpace(url));
    }
}
