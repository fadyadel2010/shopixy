using System;
using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Nest;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Elasticsearch.Configuration;
using PointOfSale.Infrastructure.Elasticsearch.Documents;
using PointOfSale.Infrastructure.Elasticsearch.Services;
using PointOfSale.Infrastructure.MongoDB.Documents;
using Microsoft.Extensions.Options;
using System.Linq;

namespace PointOfSale.API.Controllers;

/// <summary>
/// AF-11: Public Affiliate Products Controller
/// Enables public marketplace discovery of affiliate-enabled products across all stores.
/// 
/// KILL LIST COMPLIANCE:
/// - ❌ AllowAnonymous: NO auth required
/// - ❌ TenantId: NO tenant queries or responses
/// - ❌ Commission: NO commission margins exposed
/// - ❌ MongoDB: Not used here (Elasticsearch only for search)
/// </summary>
[ApiController]
[Route("api/public/affiliate/products")]
[AllowAnonymous]
public class PublicAffiliateProductsController : ControllerBase
{
    private readonly IElasticClient _elasticClient;
    private readonly ApplicationDbContext _dbContext;
    private readonly IConfiguration _configuration;
    private readonly ElasticsearchSettings _elasticSettings;
    private readonly ILogger<PublicAffiliateProductsController> _logger;

    public PublicAffiliateProductsController(
        IElasticClient elasticClient,
        ApplicationDbContext dbContext,
        IConfiguration configuration,
        IOptions<ElasticsearchSettings> elasticSettings,
        ILogger<PublicAffiliateProductsController> logger)
    {
        _elasticClient = elasticClient;
        _dbContext = dbContext;
        _configuration = configuration;
        _elasticSettings = elasticSettings.Value;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetPublicAffiliateProducts(
        [FromQuery] string? q = null,
        [FromQuery] string? category = null,
        [FromQuery] decimal? minPrice = null,
        [FromQuery] decimal? maxPrice = null,
        [FromQuery] [Range(1, 1000)] int page = 1,
        [FromQuery] [Range(1, 100)] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!_elasticSettings.Enabled)
            {
                return StatusCode(503, new { message = "Search is currently unavailable." });
            }

            var skip = (page - 1) * pageSize;

            var response = await _elasticClient.SearchAsync<PublicAffiliateProductSearchDocument>(s => s
                .Index(_elasticSettings.PublicAffiliateProductsIndexName)
                .From(skip)
                .Size(pageSize)
                .Query(queryDescriptor => queryDescriptor
                    .Bool(b =>
                    {
                        var boolQuery = b
                            .Filter(
                                // Hard Filter: Must be active and affiliate enabled
                                f => f.Term(t => t.Field("isAffiliateEnabled").Value(true)),
                                f => f.Term(t => t.Field("isActive").Value(true)),
                                f => f.Term(t => t.Field("inStock").Value(true))
                            );

                        if (!string.IsNullOrWhiteSpace(q))
                        {
                            boolQuery = boolQuery.Must(m => m
                                .MultiMatch(mm => mm
                                    .Query(q)
                                    .Fields(f => f
                                        .Field(p => p.ProductName, 3)
                                        .Field(p => p.Description)
                                        .Field(p => p.CategoryName))
                                    .Fuzziness(Fuzziness.Auto)
                                )
                            );
                        }

                        if (!string.IsNullOrWhiteSpace(category))
                        {
                            boolQuery = boolQuery.Filter(f => f
                                .Term(t => t.Field(p => p.CategoryName).Value(category))
                            );
                        }

                        if (minPrice.HasValue || maxPrice.HasValue)
                        {
                            boolQuery = boolQuery.Filter(f => f
                                .Bool(priceFilter =>
                                {
                                    var priceShoulds = new List<Func<QueryContainerDescriptor<PublicAffiliateProductSearchDocument>, QueryContainer>>
                                    {
                                        should => should.Bool(simple => simple
                                            .Must(
                                                must => must.Term(term => term.Field(p => p.HasVariants).Value(false)),
                                                must => must.Range(r => r
                                                    .Field(p => p.Price)
                                                    .GreaterThanOrEquals(minPrice.HasValue ? (double)minPrice.Value : null)
                                                    .LessThanOrEquals(maxPrice.HasValue ? (double)maxPrice.Value : null)))),
                                        should => should.Bool(variant => variant
                                            .Must(
                                                must => must.Term(term => term.Field(p => p.HasVariants).Value(true)),
                                                must => must.Range(r => r
                                                    .Field(p => p.MaxVariantPrice)
                                                    .GreaterThanOrEquals(minPrice.HasValue ? (double)minPrice.Value : null)),
                                                must => must.Range(r => r
                                                    .Field(p => p.MinVariantPrice)
                                                    .LessThanOrEquals(maxPrice.HasValue ? (double)maxPrice.Value : null))))
                                    };

                                    var priceBool = priceFilter.Should(priceShoulds.ToArray());

                                    return priceBool.MinimumShouldMatch(1);
                                })
                            );
                        }

                        return boolQuery;
                    })
                )
                .Sort(sortDescriptor => 
                {
                    if (string.IsNullOrWhiteSpace(q))
                    {
                        // Default search sort
                        return sortDescriptor.Descending(p => p.AffiliateEnabledAtUtc);
                    }
                    // Relavance sorting takes over if q is provided
                    return sortDescriptor.Descending(SortSpecialField.Score);
                }), cancellationToken);

            if (!response.IsValid)
            {
                _logger.LogError("PublicAffiliateProductsSearch failed: {Error}", response.ServerError?.Error?.Reason ?? "Unknown Error");
                return StatusCode(500, new { message = "An error occurred while searching products." });
            }

            var docs = await RecomposePublicAffiliateProductsFromSqlAuthorityAsync(
                response.Documents.ToList(),
                cancellationToken);

            var items = docs.Select(d => new
            {
                productId = d.ProductId,
                name = d.ProductName,
                thumbnail = d.ThumbnailUrl,
                
                // AF-17A: Return all pricing fields
                price = d.Price,
                minVariantPrice = d.MinVariantPrice,
                maxVariantPrice = d.MaxVariantPrice,
                
                currency = d.Currency,
                storeName = d.StoreName,
                storeSlug = d.StoreSlug,
                category = d.CategoryName,
                slug = d.ProductSlug,
                
                // AF-17A: Inventory fields
                hasVariants = d.HasVariants,
                totalAvailableStock = d.TotalAvailableStock,
                inStock = d.InStock
            }).ToList();

            return Ok(new
            {
                page,
                pageSize,
                total = docs.Count,
                items
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PublicAffiliateProductsSearch error.");
            return StatusCode(500, new { message = "An unexpected error occurred." });
        }
    }

    [HttpGet("{slug}")]
    public async Task<IActionResult> GetProductBySlug(string slug, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!_elasticSettings.Enabled)
            {
                return StatusCode(503, new { message = "Search is currently unavailable." });
            }

            var response = await _elasticClient.SearchAsync<PublicAffiliateProductSearchDocument>(s => s
                .Index(_elasticSettings.PublicAffiliateProductsIndexName)
                .Size(1)
                .Query(q => q
                    .Bool(b => b
                        .Filter(
                            f => f.Term(t => t.Field(p => p.ProductSlug).Value(slug)),
                            f => f.Term(t => t.Field("isAffiliateEnabled").Value(true)),
                            f => f.Term(t => t.Field("isActive").Value(true)),
                            f => f.Term(t => t.Field("inStock").Value(true))
                        )
                    )
                ), cancellationToken);

            var document = response.Documents.FirstOrDefault();
            if (document == null) return NotFound(new { message = "Product not found" });
            document = await RecomposePublicAffiliateProductFromSqlAuthorityAsync(
                document,
                cancellationToken);
            if (document == null) return NotFound(new { message = "Product not found" });
            if (!IsCurrentCanonicalProductSlug(slug, document.ProductSlug))
            {
                _logger.LogInformation(
                    "Public affiliate product stale slug suppressed by SQL authority | RequestedSlug: {RequestedSlug} | CurrentSlug: {CurrentSlug} | ProductId: {ProductId}",
                    slug,
                    document.ProductSlug,
                    document.ProductId);
                return NotFound(new { message = "Product not found" });
            }

            return Ok(new
            {
                productId = document.ProductId,
                name = document.ProductName,
                description = document.Description,
                thumbnail = document.ThumbnailUrl,
                price = document.Price,
                minVariantPrice = document.MinVariantPrice,
                maxVariantPrice = document.MaxVariantPrice,
                currency = document.Currency,
                storeName = document.StoreName,
                storeSlug = document.StoreSlug,
                category = document.CategoryName,
                slug = document.ProductSlug,
                hasVariants = document.HasVariants,
                totalAvailableStock = document.TotalAvailableStock,
                inStock = document.InStock
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PublicAffiliateProductFetch error.");
            return StatusCode(500, new { message = "An unexpected error occurred." });
        }
    }

    private async Task<List<PublicAffiliateProductSearchDocument>> RecomposePublicAffiliateProductsFromSqlAuthorityAsync(
        IReadOnlyList<PublicAffiliateProductSearchDocument> candidates,
        CancellationToken cancellationToken)
    {
        var visible = new List<PublicAffiliateProductSearchDocument>(candidates.Count);

        foreach (var candidate in candidates)
        {
            var recomposed = await RecomposePublicAffiliateProductFromSqlAuthorityAsync(
                candidate,
                cancellationToken);
            if (recomposed != null)
            {
                visible.Add(recomposed);
            }
        }

        if (visible.Count != candidates.Count)
        {
            _logger.LogWarning(
                "Public affiliate product search converged from SQL authority | CandidateCount: {CandidateCount} | SqlVisibleCount: {SqlVisibleCount}",
                candidates.Count,
                visible.Count);
        }

        return visible;
    }

    private async Task<PublicAffiliateProductSearchDocument?> RecomposePublicAffiliateProductFromSqlAuthorityAsync(
        PublicAffiliateProductSearchDocument candidate,
        CancellationToken cancellationToken)
    {
        if (candidate.TenantId == Guid.Empty || candidate.ProductId == Guid.Empty)
        {
            return null;
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
            return null;
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
            !product.TryResolveAffiliatePublicPricing(out var pricing) ||
            !pricing.InStock)
        {
            return null;
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
            return null;
        }

        return new PublicAffiliateProductSearchDocument
        {
            ProductId = product.ProductId,
            ProductName = product.Name,
            Description = product.Description ?? string.Empty,
            CategoryName = product.CategoryName ?? string.Empty,
            StoreName = tenant.Name,
            StoreSlug = tenant.Slug ?? string.Empty,
            ProductSlug = product.Slug ?? string.Empty,
            ThumbnailUrl = ResolvePrimaryProductImage(product) ?? string.Empty,
            Price = pricing.Price.HasValue ? (double?)pricing.Price.Value : null,
            MinVariantPrice = pricing.MinVariantPrice.HasValue ? (double?)pricing.MinVariantPrice.Value : null,
            MaxVariantPrice = pricing.MaxVariantPrice.HasValue ? (double?)pricing.MaxVariantPrice.Value : null,
            Currency = tenant.Currency ?? string.Empty,
            HasVariants = product.HasVariants,
            TotalAvailableStock = pricing.TotalAvailableStock,
            InStock = pricing.InStock,
            IsActive = product.IsActive,
            IsAffiliateEnabled = true,
            AffiliateEnabledAtUtc = candidate.AffiliateEnabledAtUtc,
            TenantId = candidate.TenantId,
            StorefrontBaseUrl = storefrontBaseUrl
        };
    }

    private static bool IsCurrentCanonicalProductSlug(string requestedSlug, string? currentSlug)
    {
        return !string.IsNullOrWhiteSpace(requestedSlug) &&
               !string.IsNullOrWhiteSpace(currentSlug) &&
               string.Equals(
                   requestedSlug.Trim(),
                   currentSlug.Trim(),
                   StringComparison.OrdinalIgnoreCase);
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
