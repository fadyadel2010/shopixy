using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.DTOs.Funnel;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Helpers; // AF-27E
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.MongoDB.Services;
using PointOfSale.Infrastructure.MultiTenancy;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Elasticsearch.Services;

namespace PointOfSale.API.Controllers;

/// <summary>
/// AF-27A: Affiliate Landing Page Controller — Marketing Layer Only.
///
/// Route: GET /api/storefront/landing/{referralCode}/{slug}
///
/// PURPOSE:
/// Provides data for rendering affiliate landing pages.
/// This is NOT checkout logic. This is MARKETING LAYER.
///
/// FLOW:
/// 1. Resolve affiliate by code (global AF-19)
/// 2. Resolve product by slug (tenant-scoped)
/// 3. Load product intelligence from MongoDB
/// 4. Return landing page data for frontend rendering
///
/// ARCHITECTURAL BOUNDARIES:
/// - READ-ONLY: No database writes
/// - No cart operations
/// - No order creation
/// - No commission calculations
/// - No checkout logic
///
/// SECURITY:
/// - Affiliate code validated (returns 404 if invalid)
/// - Product validated against tenant
/// - Only published/active products returned
/// - No unpublished content exposed
/// </summary>
[ApiController]
[Route("api/storefront/landing")]
[AllowAnonymous]
public class StorefrontLandingPageController : ControllerBase
{
    private readonly IProductReadRepository _productRepository;
    private readonly TenantContext _tenantContext;
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<StorefrontLandingPageController> _logger;

    public StorefrontLandingPageController(
        IProductReadRepository productRepository,
        TenantContext tenantContext,
        ApplicationDbContext dbContext,
        ILogger<StorefrontLandingPageController> logger)
    {
        _productRepository = productRepository;
        _tenantContext = tenantContext;
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// AF-27A: Get landing page data for affiliate + product.
    ///
    /// Returns all information needed to render an affiliate landing page:
    /// - Affiliate identity and validity
    /// - Product intelligence (from MongoDB projection)
    /// - Variant details
    /// - Tenant branding
    /// - Template type
    ///
    /// SECURITY:
    /// - Affiliate code is fully validated (inactive/invalid = 404)
    /// - Product must be active, published, and belong to current tenant
    /// - Only affiliate-enabled products are allowed
    /// </summary>
    /// <param name="referralCode">Affiliate referral code (e.g., "ZEZO")</param>
    /// <param name="slug">Product slug (e.g., "iphone-17-pro")</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Landing page data</returns>
    [HttpGet("{referralCode}/{slug}")]
    [ProducesResponseType(typeof(LandingPageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetLandingPage(
        string referralCode,
        string slug,
        [FromQuery] string? template, // AF-27E: Preview-only, not saved
        CancellationToken cancellationToken)
    {
        var traceId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;

        _logger.LogInformation(
            "[AF-27E:LandingPage] Request | ReferralCode: {ReferralCode} | Slug: {Slug} | TraceId: {TraceId}",
            referralCode, slug, traceId);

        // ═══════════════════════════════════════════════════════════════════
        // STEP 1: Resolve tenant
        // ═══════════════════════════════════════════════════════════════════
        var tenantId = ResolveTenantId();
        if (tenantId == null)
        {
            return BadRequest(ApiErrorResponse.TenantHeaderMissing(traceId));
        }

        // ═══════════════════════════════════════════════════════════════════
        // STEP 2: AF-27E - Read affiliate from middleware context
        // Middleware already resolved and validated the affiliate
        // ═══════════════════════════════════════════════════════════════════
        var affiliateAccountId = AffiliateModeResolver.GetAffiliateAccountId(HttpContext);
        var resolvedReferralCode = AffiliateModeResolver.GetReferralCode(HttpContext);
        var resolvedDisplayName = AffiliateModeResolver.GetDisplayName(HttpContext);
        
        if (affiliateAccountId == null)
        {
            // Middleware didn't resolve affiliate → invalid code
            _logger.LogInformation(
                "[AF-27E:LandingPage] Affiliate not found | Code: {Code} | TraceId: {TraceId}",
                referralCode, traceId);
            return NotFound(new ApiErrorResponse(
                "AFFILIATE_NOT_FOUND",
                "This affiliate link is no longer valid.",
                traceId));
        }

        // VALIDATION CONTRACT: This controller relies on AffiliateReferralMiddleware for affiliate validation.
        // If affiliateAccountId is non-null, middleware has already verified:
        // - V2 flow: Status == AffiliateStatus.Active (via AffiliateAccounts query after PromotionLink resolution)
        // - V1 fallback flow: Status == AffiliateStatus.Active (via direct AffiliateAccounts lookup by ReferralCode)
        // - Both flows: !IsDeleted check on AffiliateAccounts
        // The middleware sets HttpContext.Items["AffiliateAccountId"] only after these checks pass.
        // Therefore, if we reach this point with a non-null affiliateAccountId, the affiliate is valid.
        var affiliateIsValid = true;

        // ═══════════════════════════════════════════════════════════════════
        // STEP 3: Resolve product candidate by slug, then reauthorize against SQL before response shaping.
        // ═══════════════════════════════════════════════════════════════════
        var product = await _productRepository.FindBySlugAsync(tenantId.Value, slug);
        if (product == null)
        {
            _logger.LogInformation(
                "[AF-27A:LandingPage] Product not found | Slug: {Slug} | TenantId: {TenantId} | TraceId: {TraceId}",
                slug, tenantId.Value, traceId);
            return NotFound(new ApiErrorResponse(
                "PRODUCT_NOT_FOUND",
                "This product is no longer available.",
                traceId));
        }

        product = await ProductSearchSqlAuthorityOverlay.ApplyAsync(
            _dbContext,
            product,
            tenantId.Value,
            cancellationToken);

        if (product == null)
        {
            _logger.LogInformation(
                "[AF-27A:LandingPage] Product failed SQL authority overlay | Slug: {Slug} | TenantId: {TenantId} | TraceId: {TraceId}",
                slug, tenantId.Value, traceId);
            return NotFound(new ApiErrorResponse(
                "PRODUCT_NOT_AVAILABLE",
                "This product is currently unavailable.",
                traceId));
        }

        // Validate product is active and affiliate-enabled
        if (!product.IsActive)
        {
            _logger.LogInformation(
                "[AF-27A:LandingPage] Product inactive | Slug: {Slug} | ProductId: {ProductId} | TraceId: {TraceId}",
                slug, product.ProductId, traceId);
            return NotFound(new ApiErrorResponse(
                "PRODUCT_NOT_AVAILABLE",
                "This product is currently unavailable.",
                traceId));
        }

        var affiliatePolicy = await _dbContext.AffiliateProductPolicies
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Join(
                _dbContext.AffiliateCategories.AsNoTracking().IgnoreQueryFilters(),
                policy => policy.AffiliateCategoryId,
                category => (Guid?)category.Id,
                (policy, category) => new { Policy = policy, Category = category })
            .Where(item =>
                item.Policy.TenantId == tenantId.Value &&
                item.Policy.ProductId == product.ProductId &&
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
                          !parent.IsDeleted))))
            .Select(item => new { item.Policy.TemplateType })
            .FirstOrDefaultAsync(cancellationToken);

        if (affiliatePolicy == null)
        {
            _logger.LogInformation(
                "[AF-27A:LandingPage] Product not affiliate-enabled | Slug: {Slug} | ProductId: {ProductId} | TraceId: {TraceId}",
                slug, product.ProductId, traceId);
            return NotFound(new ApiErrorResponse(
                "PRODUCT_NOT_ELIGIBLE",
                "This product is not available through affiliate links.",
                traceId));
        }

        // ═══════════════════════════════════════════════════════════════════
        // STEP 4: Load tenant branding
        // ═══════════════════════════════════════════════════════════════════
        var tenantDto = await LoadTenantInfoAsync(tenantId.Value, cancellationToken);

        // ═══════════════════════════════════════════════════════════════════
        // STEP 5: Build landing page DTO
        // ═══════════════════════════════════════════════════════════════════
        var effectivePrice = GetEffectivePrice(product);
        var images = GetOrderedProductImages(product);
        var isInStock = product.Variants?.Count > 0
            ? product.Variants.Any(v => v.IsActive && v.TotalStock > 0)
            : product.InStock;

        // AF-27E: Template Resolution (with preview support)
        // PRIORITY 1: ?template= query param (PREVIEW ONLY - for affiliate portal testing)
        // PRIORITY 2: Product default from MongoDB projection
        // FALLBACK: Classic:v1
        var resolvedTemplate = affiliatePolicy.TemplateType;

        var canPreviewTemplateOverride = User.Identity?.IsAuthenticated == true &&
            (User.IsInRole("Owner") || User.IsInRole("Admin") || User.IsInRole("Manager"));

        // Allow template override only for authenticated preview users.
        if (!string.IsNullOrWhiteSpace(template) &&
            PointOfSale.Core.Constants.LandingTemplates.Allowed.Contains(template))
        {
            if (canPreviewTemplateOverride)
            {
                resolvedTemplate = template;
                _logger.LogInformation(
                    "[AF-27E:TemplatePreview] Authenticated template override via query param | Template: {Template} | Product: {ProductId}",
                    template,
                    product.ProductId);
            }
            else
            {
                _logger.LogWarning(
                    "[AF-27E:TemplatePreview] Ignored unauthenticated template override | Template: {Template} | Product: {ProductId} | TraceId: {TraceId}",
                    template,
                    product.ProductId,
                    traceId);
            }
        }

        if (string.IsNullOrWhiteSpace(resolvedTemplate) || 
            !PointOfSale.Core.Constants.LandingTemplates.Allowed.Contains(resolvedTemplate))
        {
            if (!string.IsNullOrWhiteSpace(resolvedTemplate))
            {
                _logger.LogWarning(
                    "[AF-27E:TemplateValidator] Unknown template '{UnknownTemplate}' for Product {ProductId}. Falling back to Classic:v1.",
                    resolvedTemplate, product.ProductId);
            }
            resolvedTemplate = PointOfSale.Core.Constants.LandingTemplates.ClassicV1;
        }

        var landingPage = new LandingPageDto
        {
            TemplateType = resolvedTemplate,
            Affiliate = new LandingPageAffiliateDto
            {
                ReferralCode = string.IsNullOrWhiteSpace(resolvedReferralCode) ? referralCode : resolvedReferralCode,
                DisplayName = resolvedDisplayName ?? string.Empty,
                IsValid = affiliateIsValid
            },
            Product = new LandingPageProductDto
            {
                ProductId = product.ProductId,
                Name = product.Name,
                NameAr = product.NameAr,
                Description = product.Description,
                Slug = product.Slug ?? string.Empty,
                Price = effectivePrice,
                Currency = tenantDto.Currency,
                MainImage = images.FirstOrDefault(),
                Images = images,
                InStock = isInStock,
                HasVariants = product.HasVariants,
                Variants = MapVariants(product),
                // Promotion info
                HasActivePromotion = product.HasActivePromotion,
                PromotionBadge = product.PromotionBadge,
                PromotionDiscountValue = product.PromotionDiscountValue,
                PromotionDiscountType = product.PromotionDiscountType,
                // Brand/Category
                Brand = product.Brand,
                CategoryName = product.CategoryName
            },
            Tenant = tenantDto,
            // AF-27B §7: Backend-driven CTA configuration
            CTAConfig = new LandingPageCTAConfig
            {
                ButtonText = isInStock ? "Buy Now" : "Out of Stock",
                SingleProductMode = true,
                IncludeAffiliateAttribution = affiliateIsValid, // AF-27B §2: No attribution when suspended
                DefaultQuantity = 1
            }
        };

        _logger.LogInformation(
            "[AF-27E:Validation] LandingPageRequested | TenantSlug: {TenantSlug} | ReferralCode: {ReferralCode} | ProductSlug: {ProductSlug} | TemplateResolved: {TemplateType} | TraceId: {TraceId}",
            tenantDto.StoreName, referralCode, slug, landingPage.TemplateType, traceId);

        return Ok(landingPage);
    }

    // ═══════════════════════════════════════════════════════════════════
    // HELPER METHODS
    // ═══════════════════════════════════════════════════════════════════

    private Guid? ResolveTenantId()
    {
        return StorefrontTenantContextResolver.GetTenantId(HttpContext) ?? _tenantContext.TenantId;
    }

    private decimal GetEffectivePrice(ProductDocument product)
    {
        // PHASE 11.4: Variant pricing is the authority
        if (product.Variants?.Count > 0)
        {
            var defaultVariant = product.Variants
                .FirstOrDefault(v => v.IsDefault && v.IsActive) ??
                product.Variants.FirstOrDefault(v => v.IsActive);

            if (defaultVariant != null)
                return defaultVariant.OnlinePrice ?? defaultVariant.Price;
        }

        return product.StorefrontVisiblePrice;
    }

    private static List<string> GetOrderedProductImages(ProductDocument product)
    {
        return (product.Media ?? new List<ProductMediaDocument>())
            .Where(media => string.Equals(media.Type, "image", StringComparison.OrdinalIgnoreCase))
            .OrderBy(media => media.Position)
            .Select(media => media.Url)
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .ToList();
    }

    private List<LandingPageVariantDto> MapVariants(ProductDocument product)
    {
        if (product.Variants == null || !product.HasVariants)
            return new List<LandingPageVariantDto>();

        return product.Variants
            .Where(v => v.IsActive)
            .Select(v => new LandingPageVariantDto
            {
                VariantId = v.VariantId,
                SKU = v.SKU,
                Price = v.OnlinePrice ?? v.Price,
                InStock = v.TotalStock > 0,
                ImageUrl = v.ImageUrl,
                Attributes = v.Attributes
                    .Select(a => new LandingPageVariantAttributeDto
                    {
                        Name = a.Name,
                        Value = a.Value
                    })
                    .ToList()
            })
            .ToList();
    }

    private async Task<LandingPageTenantDto> LoadTenantInfoAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        try
        {
            var authority = await _dbContext.StoreConfigs
                .AsNoTracking()
                .Where(config => config.TenantId == tenantId)
                .Join(
                    _dbContext.Tenants.AsNoTracking(),
                    config => config.TenantId,
                    tenant => tenant.Id,
                    (config, tenant) => new { config.StoreName, tenant.Currency })
                .SingleOrDefaultAsync(cancellationToken);

            if (authority != null && !string.IsNullOrWhiteSpace(authority.Currency))
            {
                return new LandingPageTenantDto
                {
                    StoreName = authority.StoreName,
                    LogoUrl = null, // Not available from storefront config
                    Currency = authority.Currency.Trim().ToUpperInvariant()
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[AF-27A:LandingPage] Failed to load storefront config | TenantId: {TenantId}",
                tenantId);
        }

        return new LandingPageTenantDto
        {
            StoreName = "Store",
            Currency = string.Empty
        };
    }
}
