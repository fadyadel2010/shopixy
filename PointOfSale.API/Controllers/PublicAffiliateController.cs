using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using PointOfSale.Infrastructure.MongoDB.Documents.Affiliate;
using PointOfSale.Infrastructure.MongoDB.Services;

namespace PointOfSale.API.Controllers;

/// <summary>
/// AF-10B: Public affiliate store discovery.
///
/// RULES:
/// 1. [AllowAnonymous] — completely public, no authentication required
/// 2. Read-only — only GET endpoints
/// 3. No tenantId in responses — only storeSlug, storeName, logoUrl, productCount
/// 4. No commission data — ever
/// 5. No DbContext mutations
/// 6. Data sourced exclusively from MongoDB (public_affiliate_stores read model)
///
/// KILL LIST:
/// - ❌ No tenantId in any response DTO
/// - ❌ No commission rates
/// - ❌ No customer data
/// - ❌ No write operations
/// </summary>
[AllowAnonymous]
[ApiController]
[Route("api/public/affiliate")]
public class PublicAffiliateController : ControllerBase
{
    private readonly MongoDbContext _mongoDbContext;
    private readonly ILogger<PublicAffiliateController> _logger;

    public PublicAffiliateController(
        MongoDbContext mongoDbContext,
        ILogger<PublicAffiliateController> logger)
    {
        _mongoDbContext = mongoDbContext;
        _logger = logger;
    }

    /// <summary>
    /// GET /api/public/affiliate/stores
    ///
    /// Returns all stores that have affiliate-enabled products.
    /// No authentication required. No tenant concepts exposed.
    /// </summary>
    [HttpGet("stores")]
    public async Task<ActionResult<List<PublicAffiliateStoreDto>>> GetStores(CancellationToken ct)
    {
        var stores = await _mongoDbContext.PublicAffiliateStores
            .Find(Builders<PublicAffiliateStoreDocument>.Filter.Gt(d => d.AffiliateProductCount, 0))
            .SortByDescending(d => d.AffiliateProductCount)
            .ToListAsync(ct);

        var result = stores.Select(s => new PublicAffiliateStoreDto
        {
            StoreSlug = s.StoreSlug,
            StoreName = s.StoreName,
            LogoUrl = s.LogoUrl,
            AffiliateProductCount = s.AffiliateProductCount
        }).ToList();

        _logger.LogInformation("[PublicAffiliate:GetStores] Returning {Count} stores from MongoDB public_affiliate_stores", result.Count);

        return Ok(result);
    }

    /// <summary>
    /// GET /api/public/affiliate/stores/{storeSlug}
    ///
    /// Returns a single store by its slug.
    /// Used by the registration page to validate the store exists.
    /// </summary>
    [HttpGet("stores/{storeSlug}")]
    public async Task<ActionResult<PublicAffiliateStoreDto>> GetStore(string storeSlug, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(storeSlug))
            return BadRequest(new { message = "Store slug is required" });

        var store = await _mongoDbContext.PublicAffiliateStores
            .Find(Builders<PublicAffiliateStoreDocument>.Filter.Eq(d => d.StoreSlug, storeSlug.ToLowerInvariant()))
            .FirstOrDefaultAsync(ct);

        if (store == null)
            return NotFound(new { message = "Store not found" });

        return Ok(new PublicAffiliateStoreDto
        {
            StoreSlug = store.StoreSlug,
            StoreName = store.StoreName,
            LogoUrl = store.LogoUrl,
            AffiliateProductCount = store.AffiliateProductCount
        });
    }
}

// ═══════════════════════════════════════════════════════════════════
// DTOs — No tenantId, no commission data
// ═══════════════════════════════════════════════════════════════════

public class PublicAffiliateStoreDto
{
    public string StoreSlug { get; set; } = string.Empty;
    public string StoreName { get; set; } = string.Empty;
    public string? LogoUrl { get; set; }
    public int AffiliateProductCount { get; set; }
}
