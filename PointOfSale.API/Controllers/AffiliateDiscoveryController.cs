using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.Services;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Elasticsearch.Services;

namespace PointOfSale.API.Controllers;

/// <summary>
/// AF-5 / AF-15: Affiliate Product Discovery API.
///
/// ARCHITECTURE RULES:
/// 1. Elasticsearch is the primary data source for product search
/// 2. Commission data is enriched from AffiliateProductPolicy (SQL)
/// 3. Commission is shown ONLY to authenticated affiliates (never to public)
/// 4. Only GET allowed — discovery is read-only
/// 5. Tenant isolation via TenantId in JWT + Elastic query filter
/// 6. No client-side filtering — all filtering done in Elasticsearch
///
/// ENDPOINTS:
/// GET /api/affiliate/discovery — Search/browse affiliate-enabled products
/// </summary>
[Authorize(Policy = "AffiliateOnly")]
[ApiController]
[Route("api/affiliate/discovery")]
public class AffiliateDiscoveryController : ControllerBase
{
    private readonly IAffiliateProductDiscoveryService _discoveryService;
    private readonly ApplicationDbContext _dbContext;
    private readonly StorefrontUrlResolver _urlResolver;
    private readonly ILogger<AffiliateDiscoveryController> _logger;

    public AffiliateDiscoveryController(
        IAffiliateProductDiscoveryService discoveryService,
        ApplicationDbContext dbContext,
        StorefrontUrlResolver urlResolver,
        ILogger<AffiliateDiscoveryController> logger)
    {
        _discoveryService = discoveryService;
        _dbContext = dbContext;
        _urlResolver = urlResolver;
        _logger = logger;
    }

    /// <summary>
    /// Discover products available for affiliate promotion.
    ///
    /// All results are guaranteed to be:
    /// - Affiliate-enabled (IsAffiliateEnabled = true)
    /// - Active (IsActive = true)
    /// - In stock (IsInStock = true)
    /// - Online visible
    ///
    /// AF-15: Commission rate and estimated earnings are included for authenticated affiliates.
    /// AF-19B: Global affiliates can discover products from ALL stores.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Discover(
        [FromQuery] string? q,
        [FromQuery] Guid? categoryId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        // AF-19B: Global affiliates - no tenant filter, search ALL stores
        // Pass Guid.Empty to indicate global search
        var tenantId = Guid.Empty;

        // Clamp page size
        if (pageSize < 1) pageSize = 1;
        if (pageSize > 100) pageSize = 100;
        if (page < 1) page = 1;

        var query = new AffiliateDiscoveryQuery
        {
            Q = q,
            CategoryId = categoryId,
            Page = page,
            PageSize = pageSize
        };

        var result = await _discoveryService.DiscoverAsync(tenantId, query, ct);

        // ═══════════════════════════════════════════════════════════════════
        // AF-15: Enrich with commission data from AffiliateProductPolicy
        // Only for authenticated affiliates viewing the Discover tab
        // AF-19B: Query policies globally (no tenant filter)
        // ═══════════════════════════════════════════════════════════════════

        var productIds = result.Items.Select(i => i.ProductId).ToList();

        var commissionPolicies = productIds.Count > 0
            ? await _dbContext.AffiliateProductPolicies
                .IgnoreQueryFilters() // AF-19B: Global search
                .Join(
                    _dbContext.AffiliateCategories.AsNoTracking().IgnoreQueryFilters(),
                    policy => policy.AffiliateCategoryId,
                    category => (Guid?)category.Id,
                    (policy, category) => new { Policy = policy, Category = category })
                .Where(item =>
                    productIds.Contains(item.Policy.ProductId) &&
                    item.Policy.IsEnabled &&
                    !item.Policy.IsDeleted &&
                    item.Category.IsActive &&
                    !item.Category.IsDeleted)
                .Select(item => new { item.Policy.ProductId, item.Policy.CommissionRate })
                .ToListAsync(ct)
            : [];

        var commissionMap = commissionPolicies.ToDictionary(p => p.ProductId, p => p.CommissionRate);

        // AF-16E: StorefrontBaseUrl is now per-product from Elasticsearch
        // No need to resolve tenant URL here — each product carries its own domain

        return Ok(new
        {
            page = result.Page,
            pageSize = result.PageSize,
            total = result.Total,
            items = result.Items.Select(item =>
            {
                var hasPolicy = commissionMap.TryGetValue(item.ProductId, out var rate);
                
                // AF-17A: Calculate commission based on correct price
                var basePrice = item.Price ?? item.MinVariantPrice ?? 0;
                var estimatedCommission = hasPolicy && basePrice > 0
                    ? Math.Round(basePrice * rate, 2)
                    : (decimal?)null;

                return new
                {
                    productId = item.ProductId,
                    name = item.Name,
                    slug = item.Slug,
                    imageUrl = item.ImageUrl,
                    
                    // AF-17A: Pricing fields (mutually exclusive based on hasVariants)
                    price = item.Price,
                    minVariantPrice = item.MinVariantPrice,
                    maxVariantPrice = item.MaxVariantPrice,
                    priceFrom = item.PriceFrom, // Deprecated but kept for compatibility
                    
                    // AF-17A: Inventory fields
                    hasVariants = item.HasVariants,
                    totalAvailableStock = item.TotalAvailableStock,
                    inStock = item.InStock,
                    
                    categoryName = item.CategoryName,
                    
                    // AF-15: Commission info for affiliates
                    commissionRate = hasPolicy ? rate : (decimal?)null,
                    estimatedCommission,
                    
                    // AF-16E: Per-product storefront URL for multi-tenant correctness
                    storefrontBaseUrl = item.StorefrontBaseUrl
                };
            }).ToList()
        });
    }
}
