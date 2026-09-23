using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.Services;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.Affiliate;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MultiTenancy;
using PointOfSale.Infrastructure.Reliability;

namespace PointOfSale.API.Controllers;

/// <summary>
/// AF-15: Affiliate Promotion Link Management.
///
/// Allows affiliates to create store-scoped promotion links for specific products.
/// Each link generates a unique referral code that maps to (AffiliateUser, Store, Product).
///
/// SECURITY:
/// - AffiliateOnly policy: requires valid affiliate JWT
/// - AF-19B: Links are global but scoped to specific stores via TenantId in request
/// - One link per (AffiliateUser, Store, Product) — enforced DB unique constraint
///
/// ENDPOINTS:
/// - POST   /api/affiliate/promotion-links         — Create a promotion link
/// - GET    /api/affiliate/promotion-links         — List my promotion links (all stores)
/// - GET    /api/affiliate/promotion-links/{id}    — Get a specific link
/// - DELETE /api/affiliate/promotion-links/{id}    — Deactivate a link (soft-delete)
/// </summary>
[ApiController]
[Route("api/affiliate/promotion-links")]
[Authorize(Policy = "AffiliateOnly")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class AffiliatePromotionLinksController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly StorefrontUrlResolver _urlResolver;
    private readonly ILogger<AffiliatePromotionLinksController> _logger;

    public AffiliatePromotionLinksController(
        ApplicationDbContext dbContext,
        StorefrontUrlResolver urlResolver,
        ILogger<AffiliatePromotionLinksController> logger)
    {
        _dbContext = dbContext;
        _urlResolver = urlResolver;
        _logger = logger;
    }

    /// <summary>
    /// Create a new promotion link for a product in a specific store.
    /// AF-19B: TenantId must be provided in request body (no tenant context).
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreatePromotionLinkRequest request, CancellationToken ct)
    {
        // AF-19B: TenantId must be provided in request
        if (request.TenantId == Guid.Empty)
            return BadRequest(new { error = "TenantId (store) is required" });
        var tenantId = request.TenantId;

        // Resolve AffiliateUserId from JWT claims
        // NOTE: .NET's JwtSecurityTokenHandler maps 'sub' → ClaimTypes.NameIdentifier
        var affiliateUserIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                                  ?? User.FindFirst("sub")?.Value
                                  ?? User.FindFirst("affiliate_user_id")?.Value;
        if (!Guid.TryParse(affiliateUserIdClaim, out var affiliateUserId))
            return Unauthorized(new { error = "Invalid affiliate identity" });

        // Selection Admission: current backend SQL eligibility is required before
        // an affiliate/product relationship can be created or reactivated.
        var product = await _dbContext.Products
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == request.ProductId && p.TenantId == tenantId && !p.IsDeleted, ct);
        if (product == null)
            return NotFound(new { error = "Product not found in this store" });

        if (!await IsProductCurrentlyAffiliateSelectableAsync(product, ct))
            return BadRequest(new { error = "Product is not enabled for affiliate promotion" });

        // Check uniqueness: one link per (AffiliateUser, Store, Product)
        var existingLink = await _dbContext.AffiliatePromotionLinks
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(l => l.AffiliateUserId == affiliateUserId
                                   && l.TenantId == tenantId
                                   && l.ProductId == request.ProductId
                                   && !l.IsDeleted, ct);
        if (existingLink != null)
        {
            // Idempotent: return existing link
            if (!existingLink.IsActive)
            {
                existingLink.Activate();
                await _dbContext.SaveChangesAsync(ct);
            }
            return Ok(await MapToResponseAsync(existingLink, requireCurrentSelectionAdmission: true, ct));
        }

        // Generate unique referral code
        var referralCode = await GenerateUniqueReferralCodeAsync(ct);

        // Resolve AffiliateAccountId for dual-write bridge
        var affiliateAccountId = await _dbContext.AffiliateUsers
            .IgnoreQueryFilters()
            .Where(u => u.Id == affiliateUserId && !u.IsDeleted)
            .Select(u => (Guid?)u.AffiliateAccountId)
            .FirstOrDefaultAsync(ct);

        var link = AffiliatePromotionLink.Create(
            tenantId, affiliateUserId, request.ProductId,
            referralCode, affiliateAccountId);

        _dbContext.AffiliatePromotionLinks.Add(link);
        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation(
            "[AF-15] Promotion link created | LinkId: {LinkId} | AffiliateUserId: {AffiliateUserId} | " +
            "ProductId: {ProductId} | StoreId: {StoreId} | ReferralCode: {ReferralCode}",
            link.Id, affiliateUserId, request.ProductId, tenantId, referralCode);

        return CreatedAtAction(nameof(GetById), new { id = link.Id }, await MapToResponseAsync(link, requireCurrentSelectionAdmission: true, ct));
    }

    /// <summary>
    /// List all promotion links for the current affiliate across all stores.
    /// AF-19B: Global affiliates - returns links from ALL stores.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var affiliateUserIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                                  ?? User.FindFirst("sub")?.Value
                                  ?? User.FindFirst("affiliate_user_id")?.Value;
        if (!Guid.TryParse(affiliateUserIdClaim, out var affiliateUserId))
            return Unauthorized(new { error = "Invalid affiliate identity" });

        // AF-19B: Query globally (no tenant filter)
        var links = await _dbContext.AffiliatePromotionLinks
            .IgnoreQueryFilters()
            .Where(l => l.AffiliateUserId == affiliateUserId && !l.IsDeleted)
            .OrderByDescending(l => l.CreatedAt)
            .ToListAsync(ct);

        var response = new List<PromotionLinkResponse>(links.Count);
        foreach (var link in links)
        {
            var admitted = await MapToResponseAsync(link, requireCurrentSelectionAdmission: true, ct);
            if (admitted != null)
            {
                response.Add(admitted);
            }
        }

        return Ok(response);
    }

    /// <summary>
    /// Get a specific promotion link by ID.
    /// AF-19B: Global affiliates - can access links from any store.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var affiliateUserIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                                  ?? User.FindFirst("sub")?.Value
                                  ?? User.FindFirst("affiliate_user_id")?.Value;
        if (!Guid.TryParse(affiliateUserIdClaim, out var affiliateUserId))
            return Unauthorized(new { error = "Invalid affiliate identity" });

        var link = await _dbContext.AffiliatePromotionLinks
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(l => l.Id == id
                                   && l.AffiliateUserId == affiliateUserId
                                   && !l.IsDeleted, ct);

        if (link == null)
            return NotFound(new { error = "Promotion link not found" });

        var response = await MapToResponseAsync(link, requireCurrentSelectionAdmission: true, ct);
        if (response == null)
            return NotFound(new { error = "Promotion link not found" });

        return Ok(response);
    }

    /// <summary>
    /// Deactivate a promotion link (soft deactivate, not hard delete).
    /// AF-19B: Global affiliates - can deactivate links from any store.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        var affiliateUserIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                                  ?? User.FindFirst("sub")?.Value
                                  ?? User.FindFirst("affiliate_user_id")?.Value;
        if (!Guid.TryParse(affiliateUserIdClaim, out var affiliateUserId))
            return Unauthorized(new { error = "Invalid affiliate identity" });

        var link = await _dbContext.AffiliatePromotionLinks
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(l => l.Id == id
                                   && l.AffiliateUserId == affiliateUserId
                                   && !l.IsDeleted, ct);

        if (link == null)
            return NotFound(new { error = "Promotion link not found" });

        link.Deactivate();
        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation(
            "[AF-15] Promotion link deactivated | LinkId: {LinkId} | AffiliateUserId: {AffiliateUserId}",
            id, affiliateUserId);

        return NoContent();
    }

    // ═══════════════════════════════════════════════════════════════════
    // HELPERS
    // ═══════════════════════════════════════════════════════════════════

    private async Task<string> GenerateUniqueReferralCodeAsync(CancellationToken ct)
    {
        const int maxAttempts = 10;
        for (int i = 0; i < maxAttempts; i++)
        {
            // Generate 10-char alphanumeric code
            var code = GenerateCode(10);
            
            // Check BOTH tables to prevent v1/v2 collisions
            var existsInLinks = await _dbContext.AffiliatePromotionLinks
                .IgnoreQueryFilters()
                .AnyAsync(l => l.ReferralCode == code, ct);

            if (existsInLinks) continue;

            var existsInAccounts = await _dbContext.AffiliateAccounts
                .IgnoreQueryFilters()
                .AnyAsync(a => a.ReferralCode == code, ct);

            if (!existsInAccounts)
                return code;
        }

        // Fallback: longer code to reduce collision probability
        return GenerateCode(14);
    }

    private static string GenerateCode(int length)
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var result = new char[length];
        for (int i = 0; i < length; i++)
        {
            result[i] = chars[Random.Shared.Next(chars.Length)];
        }
        return new string(result);
    }

    private async Task<bool> IsProductCurrentlyAffiliateSelectableAsync(Product product, CancellationToken ct)
    {
        if (product.IsDeleted ||
            !product.IsActive ||
            product.PublishStatus != PublishStatus.Published ||
            !product.IsOnlineVisible)
        {
            return false;
        }

        var policyWithActiveCategory = await _dbContext.AffiliateProductPolicies
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Join(
                _dbContext.AffiliateCategories.AsNoTracking().IgnoreQueryFilters(),
                policy => policy.AffiliateCategoryId,
                category => category.Id,
                (policy, category) => new { policy, category })
            .AnyAsync(item =>
                item.policy.ProductId == product.Id &&
                item.policy.TenantId == product.TenantId &&
                item.policy.IsEnabled &&
                !item.policy.IsDeleted &&
                item.category.IsActive &&
                !item.category.IsDeleted &&
                ((item.category.Level == 1 && item.category.ParentId == null) ||
                 (item.category.Level == 2 &&
                  item.category.ParentId != null &&
                  _dbContext.AffiliateCategories
                      .IgnoreQueryFilters()
                      .Any(parent =>
                          parent.Id == item.category.ParentId.Value &&
                          parent.Level == 1 &&
                          parent.IsActive &&
                          !parent.IsDeleted))),
                ct);
        if (!policyWithActiveCategory)
        {
            return false;
        }

        if (!await HasPositiveAffiliatePriceAsync(product, ct))
        {
            return false;
        }

        return await HasAvailableAffiliateInventoryAsync(product, ct);
    }

    private async Task<bool> HasPositiveAffiliatePriceAsync(Product product, CancellationToken ct)
    {
        if (product.HasVariants)
        {
            return await _dbContext.ProductVariants
                .AsNoTracking()
                .IgnoreQueryFilters()
                .WhereCurrentSemanticOwner()
                .AnyAsync(variant =>
                    variant.TenantId == product.TenantId &&
                    variant.ProductId == product.Id &&
                    !variant.IsDeleted &&
                    variant.IsActive &&
                    ((variant.OnlinePrice.HasValue && variant.OnlinePrice.Value > 0m) ||
                     (!variant.OnlinePrice.HasValue && variant.Price > 0m) ||
                     (variant.OnlinePrice.HasValue && variant.OnlinePrice.Value == 0m && variant.Price > 0m)),
                    ct);
        }

        return await _dbContext.ProductUnits
            .AsNoTracking()
            .IgnoreQueryFilters()
            .AnyAsync(unit =>
                unit.ProductId == product.Id &&
                !unit.IsDeleted &&
                unit.IsDefault &&
                unit.SellingPrice.HasValue &&
                unit.SellingPrice.Value > 0m,
                ct);
    }

    private async Task<bool> HasAvailableAffiliateInventoryAsync(Product product, CancellationToken ct)
    {
        if (!product.TrackStock || product.AllowBackorder)
        {
            return true;
        }

        return await _dbContext.ProductVariantStocks
            .AsNoTracking()
            .IgnoreQueryFilters()
            .AnyAsync(stock =>
                stock.TenantId == product.TenantId &&
                !stock.IsDeleted &&
                stock.Variant != null &&
                stock.Variant.TenantId == product.TenantId &&
                stock.Variant.ProductId == product.Id &&
                !stock.Variant.IsDeleted &&
                stock.Variant.IsActive &&
                stock.Variant.SemanticLifecycleState == ProductVariantSemanticLifecycleState.Current &&
                stock.Variant.SemanticSupersededAtUtc == null &&
                stock.Variant.SemanticReplacementVariantId == null &&
                (product.HasVariants || stock.Variant.IsDefault) &&
                stock.Warehouse != null &&
                stock.Warehouse.TenantId == product.TenantId &&
                !stock.Warehouse.IsDeleted &&
                stock.Warehouse.IsActive &&
                stock.Quantity - stock.ReservedQuantity > 0m,
                ct);
    }

    private async Task<PromotionLinkResponse?> MapToResponseAsync(
        AffiliatePromotionLink link,
        bool requireCurrentSelectionAdmission,
        CancellationToken ct)
    {
        var product = await _dbContext.Products
            .AsNoTracking()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == link.ProductId && p.TenantId == link.TenantId && !p.IsDeleted, ct);

        if (product == null ||
            (requireCurrentSelectionAdmission && !await IsProductCurrentlyAffiliateSelectableAsync(product, ct)))
        {
            return null;
        }

        var tenant = await _dbContext.Tenants
            .AsNoTracking()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == link.TenantId && t.IsActive && t.DeletedAtUtc == null, ct);

        string? storefrontBaseUrl = null;
        if (tenant != null)
        {
            try
            {
                storefrontBaseUrl = _urlResolver.Resolve(tenant).TrimEnd('/');
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "[AF-F3] Could not resolve backend promotion URL for tenant {TenantId}", tenant.Id);
            }
        }

        var productSlug = product.Slug;

        var productPathSegment = string.IsNullOrWhiteSpace(productSlug)
            ? link.ProductId.ToString()
            : productSlug;
        var promotionUrl = string.IsNullOrWhiteSpace(storefrontBaseUrl)
            ? null
            : _urlResolver.BuildPromotionLink(tenant!, productPathSegment, link.ReferralCode);

        return new PromotionLinkResponse
        {
            Id = link.Id,
            ProductId = link.ProductId,
            ProductSlug = productSlug,
            StoreId = link.TenantId,
            ReferralCode = link.ReferralCode,
            IsActive = link.IsActive,
            CreatedAt = link.CreatedAt,
            StorefrontBaseUrl = storefrontBaseUrl,
            PromotionUrl = promotionUrl
        };
    }
}

// ═══════════════════════════════════════════════════════════════════
// DTOs
// ═══════════════════════════════════════════════════════════════════

public class CreatePromotionLinkRequest
{
    /// <summary>AF-19B: TenantId (store) must be provided in request.</summary>
    public Guid TenantId { get; set; }
    public Guid ProductId { get; set; }
}

public class PromotionLinkResponse
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    /// <summary>AF-16D: Product slug for URL path construction.</summary>
    public string? ProductSlug { get; set; }
    public Guid StoreId { get; set; }
    public string ReferralCode { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    /// <summary>AF-16D: Backend-authoritative storefront base URL for this tenant.</summary>
    public string? StorefrontBaseUrl { get; set; }
    /// <summary>PASS F3: Backend-built promotion URL for copy/display; frontend must not synthesize authority.</summary>
    public string? PromotionUrl { get; set; }
}
