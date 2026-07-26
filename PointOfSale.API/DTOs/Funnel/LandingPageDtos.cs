namespace PointOfSale.API.DTOs.Funnel;

/// <summary>
/// AF-27A/27B: Landing page data model returned by the landing page endpoint.
/// Contains all the information needed to render an affiliate landing page.
/// 
/// This is the MARKETING LAYER — no commerce logic, no checkout.
///
/// AF-27B §7: Rendering model includes:
/// - TemplateType (backend-driven, not hardcoded)
/// - ProductData
/// - StoreBranding (via Tenant)
/// - AffiliateInfo
/// - CTAConfig (backend-driven CTA behavior)
/// </summary>
public class LandingPageDto
{
    /// <summary>
    /// Affiliate information for the landing page.
    /// </summary>
    public LandingPageAffiliateDto Affiliate { get; set; } = new();

    /// <summary>
    /// Product information for the landing page.
    /// </summary>
    public LandingPageProductDto Product { get; set; } = new();

    /// <summary>
    /// AF-28: Template type for the landing page renderer.
    /// Backend-driven — frontend uses TemplateRegistry to resolve component.
    /// Values: "Classic:v1", "Storytelling:v1", "MinimalFastCheckout:v1"
    /// </summary>
    public string TemplateType { get; set; } = "Classic:v1";

    /// <summary>
    /// Tenant information for branding.
    /// </summary>
    public LandingPageTenantDto Tenant { get; set; } = new();

    /// <summary>
    /// AF-27B §7: CTA button configuration (backend-driven).
    /// Controls CTA text, behavior, and funnel mode.
    /// </summary>
    public LandingPageCTAConfig CTAConfig { get; set; } = new();
}

/// <summary>
/// AF-27B §7: Backend-driven CTA configuration.
/// Controls button text, funnel behavior, and variant enforcement.
/// </summary>
public class LandingPageCTAConfig
{
    /// <summary>
    /// CTA button text (e.g., "Buy Now", "Get It Now", "Shop Now")
    /// </summary>
    public string ButtonText { get; set; } = "Buy Now";

    /// <summary>
    /// Whether to use SingleProductMode (qty=1, no accumulation)
    /// </summary>
    public bool SingleProductMode { get; set; } = true;

    /// <summary>
    /// Whether the CTA should include affiliate attribution.
    /// False when affiliate is invalid/suspended (AF-27B §2).
    /// </summary>
    public bool IncludeAffiliateAttribution { get; set; } = true;

    /// <summary>
    /// Default quantity for the CTA (usually 1 for landing pages)
    /// </summary>
    public int DefaultQuantity { get; set; } = 1;
}

public class LandingPageAffiliateDto
{
    public string ReferralCode { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsValid { get; set; }
}

public class LandingPageProductDto
{
    public Guid ProductId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? NameAr { get; set; }
    public string? Description { get; set; }
    public string? DescriptionAr { get; set; }
    public string Slug { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public decimal? CompareAtPrice { get; set; }
    public string Currency { get; set; } = "USD";
    public string? MainImage { get; set; }
    public List<string> Images { get; set; } = new();
    public bool? InStock { get; set; }
    public bool HasVariants { get; set; }
    public List<LandingPageVariantDto> Variants { get; set; } = new();

    // Promotion info
    public bool HasActivePromotion { get; set; }
    public string? PromotionBadge { get; set; }
    public decimal? PromotionDiscountValue { get; set; }
    public string? PromotionDiscountType { get; set; }

    // Brand/Category
    public string? Brand { get; set; }
    public string? CategoryName { get; set; }
}

public class LandingPageVariantDto
{
    public Guid VariantId { get; set; }
    public string? SKU { get; set; }
    public decimal Price { get; set; }
    public decimal? CompareAtPrice { get; set; }
    public bool InStock { get; set; }
    public string? ImageUrl { get; set; }
    public List<LandingPageVariantAttributeDto> Attributes { get; set; } = new();
}

public class LandingPageVariantAttributeDto
{
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public class LandingPageTenantDto
{
    public string StoreName { get; set; } = string.Empty;
    public string? LogoUrl { get; set; }
    public string Currency { get; set; } = "USD";
}
