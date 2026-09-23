using System;
using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nest;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Elasticsearch.Configuration;
using PointOfSale.Infrastructure.Elasticsearch.Documents;
using PointOfSale.Infrastructure.Elasticsearch.Services;
using PointOfSale.Infrastructure.MongoDB.Documents;
using Microsoft.Extensions.Options;
using System.Linq;
using PointOfSale.API.Services;
using PointOfSale.Core.Entities;

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
    private const int CandidateBatchSize = 500;
    private static readonly TimeSpan CandidateScrollTimeout = TimeSpan.FromMinutes(2);

    private readonly IElasticClient _elasticClient;
    private readonly ApplicationDbContext _dbContext;
    private readonly StorefrontUrlResolver _storefrontUrlResolver;
    private readonly ElasticsearchSettings _elasticSettings;
    private readonly ILogger<PublicAffiliateProductsController> _logger;

    public PublicAffiliateProductsController(
        IElasticClient elasticClient,
        ApplicationDbContext dbContext,
        StorefrontUrlResolver storefrontUrlResolver,
        IOptions<ElasticsearchSettings> elasticSettings,
        ILogger<PublicAffiliateProductsController> logger)
    {
        _elasticClient = elasticClient;
        _dbContext = dbContext;
        _storefrontUrlResolver = storefrontUrlResolver;
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

            var response = await _elasticClient.SearchAsync<PublicAffiliateProductSearchDocument>(s => s
                .Index(_elasticSettings.PublicAffiliateProductsIndexName)
                .Size(CandidateBatchSize)
                .Scroll(CandidateScrollTimeout)
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
                        // Stable browse order. Authority keys break timestamp ties so
                        // repeated page scans cannot reshuffle or duplicate products.
                        return sortDescriptor
                            .Descending(p => p.AffiliateEnabledAtUtc)
                            .Ascending(p => p.TenantId)
                            .Ascending(p => p.ProductId);
                    }
                    // Relevance first, then stable authority-key tie breakers.
                    return sortDescriptor
                        .Descending(SortSpecialField.Score)
                        .Descending(p => p.AffiliateEnabledAtUtc)
                        .Ascending(p => p.TenantId)
                        .Ascending(p => p.ProductId);
                }), cancellationToken);

            if (!response.IsValid)
            {
                _logger.LogError("PublicAffiliateProductsSearch failed: {Error}", response.ServerError?.Error?.Reason ?? "Unknown Error");
                return StatusCode(500, new { message = "An error occurred while searching products." });
            }

            var candidates = await ReadAllSearchCandidatesAsync(response, cancellationToken);
            var docs = await RecomposePublicAffiliateProductsFromSqlAuthorityAsync(
                candidates,
                cancellationToken);

            // SQL recomposition must happen before totals and page boundaries. A stale
            // or invalid Elasticsearch document can no longer consume a visible slot
            // or make a later valid candidate unreachable.
            var total = docs.Count;
            var skip = (page - 1) * pageSize;
            var pageDocs = docs.Skip(skip).Take(pageSize).ToList();

            var items = pageDocs.Select(d => new
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
                total,
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

                response = await _elasticClient.ScrollAsync<PublicAffiliateProductSearchDocument>(
                    CandidateScrollTimeout,
                    scrollId,
                    ct: cancellationToken);

                if (!response.IsValid)
                {
                    _logger.LogError(
                        "Public affiliate product candidate scroll failed: {Error}",
                        response.ServerError?.Error?.Reason ?? "Unknown");
                    throw new InvalidOperationException("Public affiliate product candidate scan failed.");
                }

                scrollId = response.ScrollId;
            }
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(scrollId))
            {
                await _elasticClient.ClearScrollAsync(
                    clear => clear.ScrollId(scrollId),
                    cancellationToken);
            }
        }

        // Defensive duplicate fencing: a damaged/rebuilt index must not emit the
        // same authority identity twice in totals or across public pages.
        var seen = new HashSet<(Guid TenantId, Guid ProductId)>();
        return candidates
            .Where(candidate => seen.Add((candidate.TenantId, candidate.ProductId)))
            .ToList();
    }

    private async Task<List<PublicAffiliateProductSearchDocument>> RecomposePublicAffiliateProductsFromSqlAuthorityAsync(
        IReadOnlyList<PublicAffiliateProductSearchDocument> candidates,
        CancellationToken cancellationToken)
    {
        var visible = new List<PublicAffiliateProductSearchDocument>(candidates.Count);
        var validCandidates = candidates
            .Where(candidate => candidate.TenantId != Guid.Empty && candidate.ProductId != Guid.Empty)
            .ToList();
        var tenantIds = validCandidates.Select(candidate => candidate.TenantId).Distinct().ToList();
        var productIds = validCandidates.Select(candidate => candidate.ProductId).Distinct().ToList();
        var candidateKeys = validCandidates
            .Select(candidate => (candidate.TenantId, candidate.ProductId))
            .ToHashSet();
        var policyRows = await _dbContext.AffiliateProductPolicies
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Join(
                _dbContext.AffiliateCategories.AsNoTracking().IgnoreQueryFilters(),
                policy => policy.AffiliateCategoryId,
                category => (Guid?)category.Id,
                (policy, category) => new { Policy = policy, Category = category })
            .Where(item =>
                tenantIds.Contains(item.Policy.TenantId) &&
                productIds.Contains(item.Policy.ProductId) &&
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
                          parent.ParentId == null &&
                          parent.IsActive &&
                          !parent.IsDeleted))))
            .Select(item => new { item.Policy.TenantId, item.Policy.ProductId })
            .ToListAsync(cancellationToken);
        var approvedKeys = policyRows
            .Select(row => (row.TenantId, row.ProductId))
            .Where(candidateKeys.Contains)
            .ToHashSet();
        var placeholders = validCandidates
            .Select(candidate => new ProductDocument
            {
                TenantId = candidate.TenantId,
                ProductId = candidate.ProductId
            })
            .ToList();
        var products = await ProductSearchSqlAuthorityOverlay.ApplyBatchByTenantAsync(
            _dbContext,
            placeholders,
            cancellationToken);
        var tenants = await _dbContext.Tenants
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(tenant =>
                tenantIds.Contains(tenant.Id) &&
                tenant.IsActive &&
                tenant.Status == PointOfSale.Core.Enums.TenantStatus.Active &&
                tenant.DeletedAtUtc == null)
            .Select(tenant => new Tenant
            {
                Id = tenant.Id,
                Name = tenant.Name,
                Slug = tenant.Slug,
                StorefrontBaseUrl = tenant.StorefrontBaseUrl,
                Currency = tenant.Currency
            })
            .ToDictionaryAsync(tenant => tenant.Id, cancellationToken);

        for (var index = 0; index < validCandidates.Count; index++)
        {
            var candidate = validCandidates[index];
            var product = products[index];
            if (!approvedKeys.Contains((candidate.TenantId, candidate.ProductId)) ||
                product is not { IsActive: true, IsOnlineVisible: true, IsSearchable: true } ||
                !product.IsAffiliateEnabled ||
                !product.TryResolveAffiliatePublicPricing(out var pricing) ||
                !pricing.InStock ||
                 !tenants.TryGetValue(candidate.TenantId, out var tenant) ||
                 !_storefrontUrlResolver.TryResolve(tenant, out var storefrontBaseUrl))
            {
                continue;
            }

            visible.Add(new PublicAffiliateProductSearchDocument
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
            });
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
                              parent.ParentId == null &&
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
                item.Status == PointOfSale.Core.Enums.TenantStatus.Active &&
                item.DeletedAtUtc == null)
            .Select(item => new Tenant
            {
                Id = item.Id,
                Name = item.Name,
                Slug = item.Slug,
                StorefrontBaseUrl = item.StorefrontBaseUrl,
                Currency = item.Currency
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (tenant == null ||
            !_storefrontUrlResolver.TryResolve(tenant, out var storefrontBaseUrl))
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

    private static string? ResolvePrimaryProductImage(ProductDocument product)
    {
        return product.Media
            .Where(media => string.Equals(media.Type, "image", StringComparison.OrdinalIgnoreCase))
            .OrderBy(media => media.Position)
            .Select(media => media.Url)
            .FirstOrDefault(url => !string.IsNullOrWhiteSpace(url));
    }
}
