using MassTransit;
using MassTransit.Transports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.DTOs.Affiliate;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Affiliate;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Controllers;

/// <summary>
/// AF-4C: Admin Product Affiliate Enablement API.
///
/// ARCHITECTURE RULES:
/// 1. Policy = product-level (not per-variant). Commission applies to any variant sold.
/// 2. All writes go through IAffiliateService — controller NEVER touches DB directly
/// 3. AffiliateProductPolicyChangedEvent is published atomically by IAffiliateService
/// 4. CommissionRate and MaxCommissionPerItem are ADMIN-ONLY — never in public APIs
/// 5. Elastic only indexes IsAffiliateEnabled (boolean) — never the rate
/// 6. Disable = soft disable (IsEnabled = false), NOT delete
/// 7. Return 404 for cross-tenant (never 403)
///
/// ENDPOINTS:
/// GET    /api/admin/products/{productId}/affiliate  → Get affiliate settings
/// POST   /api/admin/products/{productId}/affiliate/enable   → Enable/update affiliate
/// POST   /api/admin/products/{productId}/affiliate/disable  → Disable affiliate
/// </summary>
[Authorize(Roles = "Owner,Admin")]
[ApiController]
[Route("api/admin/products/{productId:guid}/affiliate")]
public class AdminProductAffiliateController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IAffiliateService _affiliateService;
    private readonly IAffiliateReadinessService _affiliateReadiness;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<AdminProductAffiliateController> _logger;

    public AdminProductAffiliateController(
        ApplicationDbContext dbContext,
        IAffiliateService affiliateService,
        IAffiliateReadinessService affiliateReadiness,
        ITenantContext tenantContext,
        ILogger<AdminProductAffiliateController> logger)
    {
        _dbContext = dbContext;
        _affiliateService = affiliateService;
        _affiliateReadiness = affiliateReadiness;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    // ═══════════════════════════════════════════════════════════════════
    // GET AFFILIATE SETTINGS
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// GET /api/admin/products/{productId}/affiliate
    /// Returns current affiliate policy for a product.
    /// If no policy exists, returns { isEnabled: false, commissionRate: null, maxCommissionPerItem: null }.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ProductAffiliateSettingsResponse>> GetSettings(
        Guid productId, CancellationToken ct)
    {
        var tenantId = _tenantContext.RequiredTenantId;

        // Verify product belongs to tenant
        var productExists = await _dbContext.Products
            .Where(p => p.Id == productId && p.TenantId == tenantId)
            .AnyAsync(ct);

        if (!productExists)
            return NotFound(new { message = "Product not found" });

        // Fetch policy from Postgres
        var policy = await _dbContext.AffiliateProductPolicies
            .Where(p => p.ProductId == productId && p.TenantId == tenantId)
            .Select(p => new
            {
                p.IsEnabled,
                p.CommissionRate,
                p.MaxCommissionPerItem,
                p.TemplateType,
                p.AffiliateCategoryId
            })
            .FirstOrDefaultAsync(ct);

        if (policy == null)
        {
            return Ok(new ProductAffiliateSettingsResponse
            {
                ProductId = productId,
                IsEnabled = false,
                CommissionRate = null,
                MaxCommissionPerItem = null,
                TemplateType = PointOfSale.Core.Constants.LandingTemplates.ClassicV1,
                AffiliateCategoryId = null
            });
        }

        var isEffectivelyEnabled = policy.IsEnabled &&
            await IsProductEffectivelyAffiliateEligibleAsync(
                productId,
                tenantId,
                policy.AffiliateCategoryId,
                ct);

        return Ok(new ProductAffiliateSettingsResponse
        {
            ProductId = productId,
            IsEnabled = isEffectivelyEnabled,
            CommissionRate = policy.CommissionRate,
            MaxCommissionPerItem = policy.MaxCommissionPerItem,
            TemplateType = policy.TemplateType,
            AffiliateCategoryId = policy.AffiliateCategoryId
        });
    }

    // ═══════════════════════════════════════════════════════════════════
    // ENABLE AFFILIATE
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// POST /api/admin/products/{productId}/affiliate/enable
    /// AF-30A: DEPRECATED - Use /enable-with-category instead.
    /// Enables/updates affiliate commission for a product.
    /// Requires category to be assigned first.
    /// </summary>
    [HttpPost("enable")]
    public async Task<ActionResult<ProductAffiliateSettingsResponse>> EnableAffiliate(
        Guid productId,
        [FromBody] EnableAffiliateRequest request,
        CancellationToken ct)
    {
        var tenantId = _tenantContext.RequiredTenantId;

        // ─── AF-23: READINESS GATE ──────────────────────────────────────
        var readiness = await _affiliateReadiness.CheckAsync(tenantId, ct);
        if (!readiness.IsReady)
        {
            return BadRequest(new
            {
                code = "AFFILIATE_NOT_READY",
                message = "Affiliate system is not ready",
                missing = readiness.MissingRequirements
            });
        }

        // Verify product belongs to tenant
        var productExists = await _dbContext.Products
            .Where(p => p.Id == productId && p.TenantId == tenantId)
            .AnyAsync(ct);

        if (!productExists)
            return NotFound(new { message = "Product not found" });

        // Validate rate range
        if (request.CommissionRate < 0.0001m || request.CommissionRate > 0.50m)
            return BadRequest(new { message = "CommissionRate must be between 0.0001 (0.01%) and 0.50 (50%)" });

        // Validate max cap
        if (request.MaxCommissionPerItem.HasValue && request.MaxCommissionPerItem.Value <= 0)
            return BadRequest(new { message = "MaxCommissionPerItem must be positive when set" });

        try
        {
            var policy = await _affiliateService.EnableAffiliateForProductAsync(
                productId, request.CommissionRate, request.MaxCommissionPerItem, ct);

            _logger.LogInformation(
                "Admin EnableAffiliate | TenantId: {TenantId} | ProductId: {ProductId} | IsEnabled: true | Rate: {Rate}",
                tenantId, productId, policy.CommissionRate);

            return Ok(new ProductAffiliateSettingsResponse
            {
                ProductId = productId,
                IsEnabled = true,
                CommissionRate = policy.CommissionRate,
                MaxCommissionPerItem = policy.MaxCommissionPerItem,
                TemplateType = policy.TemplateType,
                AffiliateCategoryId = policy.AffiliateCategoryId
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// POST /api/admin/products/{productId}/affiliate/enable-with-category
    /// AF-30A: Atomically enables affiliate with category assignment.
    /// Preferred method for enabling affiliate marketing.
    /// </summary>
    [HttpPost("enable-with-category")]
    public async Task<ActionResult<ProductAffiliateSettingsResponse>> EnableAffiliateWithCategory(
        Guid productId,
        [FromBody] EnableAffiliateWithCategoryRequest request,
        CancellationToken ct)
    {
        var tenantId = _tenantContext.RequiredTenantId;

        // ─── AF-23: READINESS GATE ──────────────────────────────────────
        var readiness = await _affiliateReadiness.CheckAsync(tenantId, ct);
        if (!readiness.IsReady)
        {
            return BadRequest(new
            {
                code = "AFFILIATE_NOT_READY",
                message = "Affiliate system is not ready",
                missing = readiness.MissingRequirements
            });
        }

        // Verify product belongs to tenant
        var productExists = await _dbContext.Products
            .Where(p => p.Id == productId && p.TenantId == tenantId)
            .AnyAsync(ct);

        if (!productExists)
            return NotFound(new { message = "Product not found" });

        // AF-30A: Validate category is provided
        if (request.CategoryId == Guid.Empty)
            return BadRequest(new { message = "Affiliate category is required" });

        // Validate rate range
        if (request.CommissionRate < 0.0001m || request.CommissionRate > 0.50m)
            return BadRequest(new { message = "CommissionRate must be between 0.0001 (0.01%) and 0.50 (50%)" });

        // Validate max cap
        if (request.MaxCommissionPerItem.HasValue && request.MaxCommissionPerItem.Value <= 0)
            return BadRequest(new { message = "MaxCommissionPerItem must be positive when set" });

        try
        {
            var result = await _affiliateService.EnableAffiliateWithCategoryAsync(
                productId, request.CommissionRate, request.CategoryId, request.MaxCommissionPerItem, ct);

            _logger.LogInformation(
                "[AF-30A] Affiliate enabled with category | TenantId: {TenantId} | ProductId: {ProductId} | CategoryId: {CategoryId}",
                tenantId, productId, result.CategoryDetails?.CategoryId);

            return Ok(new ProductAffiliateSettingsResponse
            {
                ProductId = productId,
                IsEnabled = true,
                CommissionRate = result.Policy.CommissionRate,
                MaxCommissionPerItem = result.Policy.MaxCommissionPerItem,
                TemplateType = result.Policy.TemplateType,
                AffiliateCategoryId = result.Policy.AffiliateCategoryId
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // DISABLE AFFILIATE
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// POST /api/admin/products/{productId}/affiliate/disable
    /// Disables affiliate for a product (soft-disable, not delete).
    /// </summary>
    [HttpPost("disable")]
    public async Task<ActionResult<ProductAffiliateSettingsResponse>> DisableAffiliate(
        Guid productId, CancellationToken ct)
    {
        var tenantId = _tenantContext.RequiredTenantId;

        // Verify product belongs to tenant
        var productExists = await _dbContext.Products
            .Where(p => p.Id == productId && p.TenantId == tenantId)
            .AnyAsync(ct);

        if (!productExists)
            return NotFound(new { message = "Product not found" });

        var policy = await _affiliateService.DisableAffiliateForProductAsync(productId, ct);

        _logger.LogInformation(
            "Admin DisableAffiliate | TenantId: {TenantId} | ProductId: {ProductId}",
            tenantId, productId);

        return Ok(new ProductAffiliateSettingsResponse
        {
            ProductId = productId,
            IsEnabled = false,
            // AF-30B GAP-4 FIX: Return actual values (not null) so UI shows
            // what's saved and what will be restored on re-enable.
            CommissionRate = policy?.CommissionRate,
            MaxCommissionPerItem = policy?.MaxCommissionPerItem,
            TemplateType = policy?.TemplateType ?? PointOfSale.Core.Constants.LandingTemplates.ClassicV1,
            AffiliateCategoryId = policy?.AffiliateCategoryId
        });
    }

    // ═══════════════════════════════════════════════════════════════════
    // UPDATE TEMPLATE
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// POST /api/admin/products/{productId}/affiliate/template
    /// Updates the assigned landing page template for this product.
    /// </summary>
    [HttpPost("template")]
    public async Task<ActionResult<ProductAffiliateSettingsResponse>> UpdateTemplate(
        Guid productId,
        [FromBody] UpdateTemplateRequest request,
        CancellationToken ct)
    {
        var tenantId = _tenantContext.RequiredTenantId;

        if (!PointOfSale.Core.Constants.LandingTemplates.Allowed.Contains(request.TemplateType))
            return BadRequest(new { message = $"Invalid TemplateType. Allowed values: {string.Join(", ", PointOfSale.Core.Constants.LandingTemplates.Allowed)}" });

        try
        {
            var policy = await _affiliateService.UpdateTemplateForProductAsync(productId, request.TemplateType, ct);

            _logger.LogInformation(
                "Admin UpdateTemplate | TenantId: {TenantId} | ProductId: {ProductId} | TemplateType: {TemplateType}",
                tenantId, productId, policy.TemplateType);

            return Ok(new ProductAffiliateSettingsResponse
            {
                ProductId = productId,
                IsEnabled = policy.IsEnabled,
                CommissionRate = policy.CommissionRate,
                MaxCommissionPerItem = policy.MaxCommissionPerItem,
                TemplateType = policy.TemplateType
            });
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // UPDATE CATEGORY (AF-30)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// POST /api/admin/products/{productId}/affiliate/category
    /// AF-30: Updates the marketplace category assigned to this product's affiliate policy.
    /// </summary>
    [HttpPost("category")]
    public async Task<ActionResult<ProductAffiliateSettingsResponse>> UpdateCategory(
        Guid productId,
        [FromBody] UpdateAffiliateCategoryRequest request,
        CancellationToken ct)
    {
        var tenantId = _tenantContext.RequiredTenantId;

        try
        {
            // AF-30 FIX: Service now returns enriched response with category details
            var result = await _affiliateService.UpdateCategoryForProductAsync(productId, request.CategoryId, ct);

            _logger.LogInformation(
                "[AF-30] Admin UpdateCategory | TenantId: {TenantId} | ProductId: {ProductId} | CategoryId: {CategoryId}",
                tenantId, productId, result.Policy.AffiliateCategoryId);

            return Ok(new ProductAffiliateSettingsResponse
            {
                ProductId = productId,
                IsEnabled = result.Policy.IsEnabled,
                CommissionRate = result.Policy.CommissionRate,
                MaxCommissionPerItem = result.Policy.MaxCommissionPerItem,
                TemplateType = result.Policy.TemplateType,
                AffiliateCategoryId = result.Policy.AffiliateCategoryId
            });
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    private async Task<bool> IsProductEffectivelyAffiliateEligibleAsync(
        Guid productId,
        Guid tenantId,
        Guid? affiliateCategoryId,
        CancellationToken ct)
    {
        if (!affiliateCategoryId.HasValue)
        {
            return false;
        }

        var categoryActive = await _dbContext.AffiliateCategories
            .AsNoTracking()
            .AnyAsync(category =>
                category.Id == affiliateCategoryId.Value &&
                category.IsActive,
                ct);

        if (!categoryActive)
        {
            return false;
        }

        var product = await _dbContext.Products
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(item =>
                item.Id == productId &&
                item.TenantId == tenantId &&
                !item.IsDeleted)
            .Select(item => new
            {
                item.Id,
                item.TenantId,
                item.IsActive,
                item.IsOnlineVisible,
                item.PublishStatus,
                item.HasVariants,
                item.TrackStock,
                item.AllowBackorder,
                item.OnlinePrice,
                item.SalePrice
            })
            .FirstOrDefaultAsync(ct);

        if (product is not { IsActive: true, IsOnlineVisible: true, PublishStatus: PublishStatus.Published })
        {
            return false;
        }

        var hasPositivePrice = product.HasVariants
            ? await _dbContext.ProductVariants
                .AsNoTracking()
                .IgnoreQueryFilters()
                .AnyAsync(variant =>
                    variant.TenantId == tenantId &&
                    variant.ProductId == productId &&
                    !variant.IsDeleted &&
                    variant.IsActive &&
                    variant.SemanticLifecycleState == ProductVariantSemanticLifecycleState.Current &&
                    variant.SemanticSupersededAtUtc == null &&
                    variant.SemanticReplacementVariantId == null &&
                    ((variant.OnlinePrice.HasValue && variant.OnlinePrice.Value > 0m) ||
                     (!variant.OnlinePrice.HasValue && variant.Price > 0m) ||
                     (variant.OnlinePrice.HasValue && variant.OnlinePrice.Value == 0m && variant.Price > 0m)),
                    ct)
            : (product.OnlinePrice.HasValue && product.OnlinePrice.Value > 0m) ||
              (!product.OnlinePrice.HasValue && product.SalePrice > 0m) ||
              (product.OnlinePrice.HasValue && product.OnlinePrice.Value == 0m && product.SalePrice > 0m) ||
              await _dbContext.ProductUnits
                  .AsNoTracking()
                  .IgnoreQueryFilters()
                  .AnyAsync(unit =>
                      unit.ProductId == productId &&
                      !unit.IsDeleted &&
                      unit.SellingPrice.HasValue &&
                      unit.SellingPrice.Value > 0m,
                      ct);

        if (!hasPositivePrice)
        {
            return false;
        }

        if (!product.TrackStock || product.AllowBackorder)
        {
            return true;
        }

        return await _dbContext.ProductVariantStocks
            .AsNoTracking()
            .IgnoreQueryFilters()
            .AnyAsync(stock =>
                stock.TenantId == tenantId &&
                !stock.IsDeleted &&
                stock.Variant != null &&
                stock.Variant.TenantId == tenantId &&
                stock.Variant.ProductId == productId &&
                !stock.Variant.IsDeleted &&
                stock.Variant.IsActive &&
                stock.Variant.SemanticLifecycleState == ProductVariantSemanticLifecycleState.Current &&
                stock.Variant.SemanticSupersededAtUtc == null &&
                stock.Variant.SemanticReplacementVariantId == null &&
                (product.HasVariants || stock.Variant.IsDefault) &&
                stock.Warehouse != null &&
                stock.Warehouse.TenantId == tenantId &&
                !stock.Warehouse.IsDeleted &&
                stock.Warehouse.IsActive &&
                stock.Quantity - stock.ReservedQuantity > 0m,
                ct);
    }
}



// ═══════════════════════════════════════════════════════════════════
// DTOs — Product Affiliate Settings
// ═══════════════════════════════════════════════════════════════════

public class ProductAffiliateSettingsResponse
{
    public Guid ProductId { get; set; }
    public bool IsEnabled { get; set; }
    public decimal? CommissionRate { get; set; }
    public decimal? MaxCommissionPerItem { get; set; }
    public string TemplateType { get; set; } = string.Empty;
    public Guid? AffiliateCategoryId { get; set; } // AF-30
}

public class EnableAffiliateRequest
{
    /// <summary>
    /// Commission rate as a decimal fraction.
    /// 0.10 = 10%. Range: [0.0001, 0.50].
    /// </summary>
    public decimal CommissionRate { get; set; }

    /// <summary>
    /// Optional cap on commission per order-item. Null = no cap.
    /// </summary>
    public decimal? MaxCommissionPerItem { get; set; }
}

public class EnableAffiliateWithCategoryRequest
{
    /// <summary>
    /// Commission rate as a decimal fraction.
    /// 0.10 = 10%. Range: [0.0001, 0.50].
    /// </summary>
    public decimal CommissionRate { get; set; }

    /// <summary>
    /// Optional cap on commission per order-item. Null = no cap.
    /// </summary>
    public decimal? MaxCommissionPerItem { get; set; }

    /// <summary>
    /// AF-30A: Marketplace category ID (REQUIRED).
    /// </summary>
    public Guid CategoryId { get; set; }
}

public class UpdateTemplateRequest
{
    public string TemplateType { get; set; } = string.Empty;
}

public class UpdateAffiliateCategoryRequest
{
    /// <summary>
    /// AF-30: Marketplace category ID. Null to unassign category.
    /// </summary>
    public Guid? CategoryId { get; set; }
}
