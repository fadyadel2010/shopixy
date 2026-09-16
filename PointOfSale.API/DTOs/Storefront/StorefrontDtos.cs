using System.Text.Json.Serialization;
using PointOfSale.Core.Enums;

namespace PointOfSale.API.DTOs.Storefront;

/// <summary>
/// Product unit DTO for storefront pricing.
/// Contains the selling price for a specific unit of measure.
/// </summary>
public class ProductUnitDto
{
    /// <summary>
    /// Unit ID reference
    /// </summary>
    public Guid UnitId { get; set; }

    /// <summary>
    /// Unit name (e.g., "Piece", "Carton", "Box")
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Unit symbol (e.g., "pc", "ctn", "bx")
    /// </summary>
    public string? Symbol { get; set; }

    /// <summary>
    /// Selling price for this unit.
    /// THIS IS THE CORRECT PRICE SOURCE FOR STOREFRONT.
    /// </summary>
    public decimal SalePrice { get; set; }

    /// <summary>
    /// Conversion factor relative to base unit.
    /// </summary>
    public decimal ConversionFactor { get; set; } = 1;
}

/// <summary>
/// Variant attribute DTO for storefront variant selection.
/// 
/// PHASE 11.3 — STOREFRONT VARIANT SELECTION
/// </summary>
public class VariantAttributeDto
{
    /// <summary>
    /// Attribute name (e.g., "Color", "Size")
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Attribute value (e.g., "Red", "XL")
    /// </summary>
    public string Value { get; set; } = string.Empty;
}

/// <summary>
/// Product variant DTO for storefront variant selection.
/// 
/// PHASE 11.3 — STOREFRONT VARIANT SELECTION
/// 
/// This is returned in ProductReadDto.Variants[] for product pages.
/// Cart and Checkout use VariantId (not ProductId) for pricing.
/// </summary>
public class ProductVariantDto
{
    /// <summary>
    /// Variant unique identifier.
    /// This is what gets added to cart, not ProductId.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Variant SKU
    /// </summary>
    public string? SKU { get; set; }

    /// <summary>
    /// POS sale price
    /// </summary>
    public decimal Price { get; set; }

    /// <summary>
    /// Online/Storefront price. If null or 0, use Price.
    /// </summary>
    public decimal? OnlinePrice { get; set; }

    /// <summary>
    /// Computed effective price: OnlinePrice if set, otherwise Price.
    /// Frontend should use this for display.
    /// </summary>
    public decimal EffectivePrice => (OnlinePrice.HasValue && OnlinePrice.Value > 0) ? OnlinePrice.Value : Price;

    /// <summary>
    /// Backend-composed storefront pricing presentation for this variant.
    /// Frontend may format these values, but must not calculate discounts.
    /// </summary>
    public ProductPricingPresentationDto Pricing { get; set; } = new();

    /// <summary>
    /// Backend-composed availability and purchasability presentation for this variant.
    /// Frontend may render this evidence, but must not decide purchase authority.
    /// </summary>
    public ProductAvailabilityPresentationDto Availability { get; set; } = new();

    /// <summary>
    /// Variant-specific product media reference.
    /// Preferred over legacy ImageUrl when present.
    /// </summary>
    public Guid? MediaId { get; set; }

    /// <summary>
    /// Variant-specific image URL.
    /// Falls back to product images if null.
    /// </summary>
    public string? ImageUrl { get; set; }

    /// <summary>
    /// Total stock quantity for this variant.
    /// </summary>
    public int TotalStock { get; set; }

    /// <summary>
    /// Whether this variant is in stock.
    /// </summary>
    public bool InStock => TotalStock > 0;

    /// <summary>
    /// Whether this is the default variant.
    /// Auto-selected when page loads or no selection made.
    /// </summary>
    public bool IsDefault { get; set; }

    /// <summary>
    /// Whether this variant is active and available for purchase.
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// Variant attributes (Color, Size, etc.)
    /// </summary>
    public List<VariantAttributeDto> Attributes { get; set; } = new();
}

public class StorefrontVariantResolutionRequest
{
    public Dictionary<string, string> SelectedOptions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public class StorefrontVariantResolutionResponse
{
    public Guid ProductId { get; set; }
    public Guid ResolvedVariantId { get; set; }
    public ProductVariantDto Variant { get; set; } = new();
}

public class ProductPricingPresentationDto
{
    /// <summary>
    /// Backend-owned current display price after any admitted promotion.
    /// </summary>
    public decimal CurrentPrice { get; set; }

    /// <summary>
    /// Backend-owned compare-at/original price when a promotion changes display price.
    /// </summary>
    public decimal? CompareAtPrice { get; set; }

    /// <summary>
    /// Backend-owned discounted price evidence when a promotion applies.
    /// </summary>
    public decimal? DiscountedPrice { get; set; }

    /// <summary>
    /// Backend-owned discount badge text admitted for presentation.
    /// </summary>
    public string? DiscountBadge { get; set; }
}

public class ProductAvailabilityPresentationDto
{
    /// <summary>
    /// Backend-owned availability presentation after Storefront read admission.
    /// </summary>
    public bool IsAvailable { get; set; }

    /// <summary>
    /// Backend-owned purchasability decision for the admitted product or resolved variant.
    /// </summary>
    public bool CanPurchase { get; set; }

    /// <summary>
    /// Backend-owned Add To Cart permission for the admitted product or resolved variant.
    /// </summary>
    public bool CanAddToCart { get; set; }

    /// <summary>
    /// Backend-owned Buy Now permission for the admitted product or resolved variant.
    /// </summary>
    public bool CanBuyNow { get; set; }

    /// <summary>
    /// Backend-owned stock badge text for Storefront presentation.
    /// </summary>
    public string StockLabel { get; set; } = "Unavailable";

    /// <summary>
    /// Backend-owned maximum quantity presentation. Null means no frontend-enforced maximum.
    /// </summary>
    public int? MaximumQuantity { get; set; }

    /// <summary>
    /// Backend-owned unavailable reason for display/diagnostics.
    /// </summary>
    public string? UnavailableReason { get; set; }
}

public static class StorefrontAvailabilityPresentationMapper
{
    public static ProductAvailabilityPresentationDto Build(
        bool isAvailable,
        bool inStock,
        int stockQuantity,
        bool requiresVariantSelection = false,
        bool? canPurchaseOverride = null,
        string? unavailableReason = null)
    {
        var canPurchase = canPurchaseOverride ?? (isAvailable && inStock && !requiresVariantSelection);
        return new ProductAvailabilityPresentationDto
        {
            IsAvailable = isAvailable,
            CanPurchase = canPurchase,
            CanAddToCart = canPurchase,
            CanBuyNow = canPurchase,
            StockLabel = BuildStockLabel(isAvailable, inStock, stockQuantity, requiresVariantSelection),
            MaximumQuantity = canPurchase && stockQuantity > 0 ? stockQuantity : null,
            UnavailableReason = unavailableReason ?? BuildUnavailableReason(isAvailable, inStock, requiresVariantSelection)
        };
    }

    private static string BuildStockLabel(
        bool isAvailable,
        bool inStock,
        int stockQuantity,
        bool requiresVariantSelection)
    {
        if (requiresVariantSelection)
        {
            return "Select options";
        }

        if (!isAvailable)
        {
            return "Unavailable";
        }

        if (!inStock)
        {
            return "Out of stock";
        }

        return stockQuantity > 0 ? $"In Stock ({stockQuantity})" : "In Stock";
    }

    private static string? BuildUnavailableReason(
        bool isAvailable,
        bool inStock,
        bool requiresVariantSelection)
    {
        if (requiresVariantSelection)
        {
            return "VARIANT_SELECTION_REQUIRED";
        }

        if (!isAvailable)
        {
            return "UNAVAILABLE";
        }

        if (!inStock)
        {
            return "OUT_OF_STOCK";
        }

        return null;
    }
}

public static class StorefrontPricingPresentationMapper
{
    public static ProductPricingPresentationDto Build(
        decimal basePrice,
        bool hasActivePromotion,
        string? promotionBadge,
        string? promotionDiscountType,
        decimal? promotionDiscountValue)
    {
        var currentPrice = RoundCurrency(basePrice);
        if (!hasActivePromotion || !promotionDiscountValue.HasValue || promotionDiscountValue.Value <= 0m)
        {
            return new ProductPricingPresentationDto
            {
                CurrentPrice = currentPrice
            };
        }

        var discountedPrice = promotionDiscountType?.Equals("percentage", StringComparison.OrdinalIgnoreCase) == true
            ? currentPrice * (1m - (promotionDiscountValue.Value / 100m))
            : promotionDiscountType?.Equals("fixed", StringComparison.OrdinalIgnoreCase) == true
                ? currentPrice - promotionDiscountValue.Value
                : currentPrice;

        discountedPrice = RoundCurrency(Math.Max(0m, discountedPrice));
        if (discountedPrice >= currentPrice)
        {
            return new ProductPricingPresentationDto
            {
                CurrentPrice = currentPrice,
                DiscountBadge = string.IsNullOrWhiteSpace(promotionBadge) ? null : promotionBadge.Trim()
            };
        }

        return new ProductPricingPresentationDto
        {
            CurrentPrice = discountedPrice,
            CompareAtPrice = currentPrice,
            DiscountedPrice = discountedPrice,
            DiscountBadge = string.IsNullOrWhiteSpace(promotionBadge) ? null : promotionBadge.Trim()
        };
    }

    private static decimal RoundCurrency(decimal amount) =>
        Math.Round(amount, 2, MidpointRounding.AwayFromZero);
}

/// <summary>
/// Product media DTO for storefront product gallery rendering.
/// Supports images and videos in gallery order.
/// </summary>
public class ProductMediaDto
{
    /// <summary>
    /// Product media unique identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Media type: image or video.
    /// </summary>
    public string MediaType { get; set; } = string.Empty;

    /// <summary>
    /// Public media URL.
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Backend-composed playback URL for embedded providers.
    /// </summary>
    public string? PresentationUrl { get; set; }

    /// <summary>
    /// Backend-composed thumbnail URL for video/media thumbnails.
    /// </summary>
    public string? ThumbnailUrl { get; set; }

    /// <summary>
    /// Optional provider for videos (youtube, vimeo, mp4).
    /// </summary>
    public string? Provider { get; set; }

    /// <summary>
    /// Ordered position in the product gallery.
    /// </summary>
    public int Position { get; set; }
}

/// <summary>
/// Product read DTO for e-commerce storefront.
/// 
/// Design:
/// - Frontend-ready (camelCase serialization)
/// - Independent from MongoDB/Elasticsearch schemas
/// - Contains only fields needed for product display
/// 
/// PHASE 11.3 — VARIANT SELECTION:
/// - Variants[] contains all sellable variants with their prices/stock
/// - DefaultVariantId points to the auto-selected variant
/// - HasVariants indicates if variant selector should be shown
/// 
/// CRITICAL PRICING RULE (PHASE 11.3+):
/// - SelectedVariant.EffectivePrice is the correct price for cart/checkout
/// - Legacy Price field kept for backward compatibility
/// </summary>
public class ProductReadDto
{
    /// <summary>
    /// Product unique identifier
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Product name for display
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Arabic product name (optional)
    /// </summary>
    public string? NameAr { get; set; }

    /// <summary>
    /// Product description HTML/text
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Arabic product description (optional)
    /// </summary>
    public string? DescriptionAr { get; set; }

    /// <summary>
    /// Stock Keeping Unit
    /// </summary>
    public string? SKU { get; set; }

    /// <summary>
    /// Product barcode
    /// </summary>
    public string? Barcode { get; set; }

    /// <summary>
    /// LEGACY: Product sale price from Products table.
    /// Use DefaultUnit.SalePrice for correct unit-based pricing.
    /// Kept for backwards compatibility.
    /// </summary>
    public decimal Price { get; set; }

    /// <summary>
    /// Default selling unit for this product.
    /// THIS IS THE SOURCE OF TRUTH FOR STOREFRONT PRICING.
    /// If null, Price field is used as fallback.
    /// </summary>
    public ProductUnitDto? DefaultUnit { get; set; }

    /// <summary>
    /// Effective storefront price resolved by the server.
    /// Price already contains the channel-authoritative value; DefaultUnit remains
    /// POS/unit metadata and must not override a simple product's online price.
    /// </summary>
    public decimal EffectivePrice => Price;

    /// <summary>
    /// Backend-composed storefront pricing presentation for the current/default product price.
    /// Frontend may format these values, but must not calculate discounts.
    /// </summary>
    public ProductPricingPresentationDto Pricing { get; set; } = new();

    /// <summary>
    /// Backend-composed availability and purchasability presentation for the current product.
    /// Frontend may render this evidence, but must not decide purchase authority.
    /// </summary>
    public ProductAvailabilityPresentationDto Availability { get; set; } = new();

    /// <summary>
    /// Category name for display
    /// </summary>
    public string? CategoryName { get; set; }

    /// <summary>
    /// Category ID for filtering
    /// </summary>
    public Guid? CategoryId { get; set; }

    /// <summary>
    /// SEO-friendly URL slug
    /// </summary>
    public string? Slug { get; set; }

    /// <summary>
    /// Optional storefront product template identifier.
    /// Null means the default product template should be used.
    /// </summary>
    public string? ProductTemplate { get; set; }

    /// <summary>
    /// Product card/layout preference for storefront listing surfaces.
    /// Restores compatibility with older storefront clients that expect a dedicated layout field.
    /// Backed by the existing ProductTemplate value in projections.
    /// </summary>
    public string? StorefrontLayout { get; set; }

    /// <summary>
    /// Whether this product is curated as featured.
    /// </summary>
    public bool IsFeatured { get; set; }

    /// <summary>
    /// Product image URLs
    /// First image is the primary/thumbnail
    /// </summary>
    public List<string> Images { get; set; } = new();

    /// <summary>
    /// Ordered product media gallery.
    /// Images and videos are both included for storefront media rendering.
    /// </summary>
    public List<ProductMediaDto> Media { get; set; } = new();

    /// <summary>
    /// Whether product is currently available for purchase.
    /// False if no valid price (EffectivePrice == 0) or not active/visible.
    /// </summary>
    public bool IsAvailable { get; set; }

    /// <summary>
    /// Whether the product is in stock.
    /// True if: !TrackStock || AllowBackorder || StockQuantity > 0
    /// Used for "Out of Stock" display and disabling Add to Cart.
    /// </summary>
    public bool InStock { get; set; } = true;

    /// <summary>
    /// Available stock quantity.
    /// Used for stock display and cart quantity validation.
    /// </summary>
    public int StockQuantity { get; set; }

    /// <summary>
    /// Product creation date
    /// </summary>
    public DateTime CreatedAtUtc { get; set; }

    // =====================================================================
    // PHASE 11.3 — VARIANT SELECTION FIELDS
    // =====================================================================

    /// <summary>
    /// Product variants for storefront selection.
    /// Contains all active variants with their prices, stock, and attributes.
    /// Empty if product has no variants (legacy products).
    /// </summary>
    public List<ProductVariantDto> Variants { get; set; } = new();

    /// <summary>
    /// Whether this product requires variant selection.
    /// PHASE 11.7 — BLOCKER B1 FIX:
    /// This flag is STORED (from PostgreSQL Product.HasVariants via MongoDB),
    /// NOT COMPUTED from Variants.Count.
    /// 
    /// A product with HasVariants=true but only 1 variant still REQUIRES selection.
    /// Variant behavior is DECLARED, not inferred.
    /// </summary>
    public bool HasVariants { get; set; }

    /// <summary>
    /// The default variant ID to auto-select.
    /// Null only for legacy products without variants.
    /// </summary>
    public Guid? DefaultVariantId { get; set; }
    
    // =====================================================================
    // PHASE: PROMOTIONS ENGINE - BADGE DISPLAY FIELDS
    // =====================================================================
    
    /// <summary>
    /// Whether this product has an active promotion (offer).
    /// Updated by PromotionProjectionConsumer when promotions change.
    /// </summary>
    public bool HasActivePromotion { get; set; }
    
    /// <summary>
    /// Badge text to display (e.g., "20% OFF", "Save $10").
    /// </summary>
    public string? PromotionBadge { get; set; }
    
    /// <summary>
    /// Discount type: "percentage" or "fixed".
    /// </summary>
    public string? PromotionDiscountType { get; set; }
    
    /// <summary>
    /// Discount value for calculating discounted price.
    /// </summary>
    public decimal? PromotionDiscountValue { get; set; }
    
    // =====================================================================
    // AF-12: AFFILIATE MODE SUPPORT
    // =====================================================================
    
    /// <summary>
    /// Whether this product is enabled for affiliate mode.
    /// When true, product can be sold through affiliate referral links.
    /// </summary>
    public bool IsAffiliateEnabled { get; set; }
}


/// <summary>
/// Minimal product DTO for list views and search results.
/// Contains only fields needed for product cards/tiles.
/// </summary>
public class ProductListItemDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? NameAr { get; set; }
    
    /// <summary>
    /// LEGACY: Product sale price from Products table.
    /// Use EffectivePrice for correct pricing.
    /// </summary>
    public decimal Price { get; set; }
    
    /// <summary>
    /// Default selling unit for this product.
    /// </summary>
    public ProductUnitDto? DefaultUnit { get; set; }
    
    /// <summary>
    /// Effective storefront price resolved by the server. Price already contains
    /// the channel-authoritative value for both simple and variant products.
    /// </summary>
    public decimal EffectivePrice => Price;

    /// <summary>
    /// Backend-composed storefront pricing presentation.
    /// Frontend may format these values, but must not calculate discounts.
    /// </summary>
    public ProductPricingPresentationDto Pricing { get; set; } = new();

    /// <summary>
    /// Backend-composed availability and purchasability presentation for product cards.
    /// Frontend may render this evidence, but must not decide purchase authority.
    /// </summary>
    public ProductAvailabilityPresentationDto Availability { get; set; } = new();
    
    public string? CategoryName { get; set; }
    public string? Slug { get; set; }
    public string? ThumbnailImage { get; set; }
    public string? ProductTemplate { get; set; }
    public string? StorefrontLayout { get; set; }
    public bool IsFeatured { get; set; }
    public bool IsAvailable { get; set; }
    
    /// <summary>
    /// Whether the product is in stock.
    /// True if: !TrackStock || AllowBackorder || StockQuantity > 0
    /// </summary>
    public bool InStock { get; set; } = true;
    
    /// <summary>
    /// Available stock quantity.
    /// </summary>
    public int StockQuantity { get; set; }
    
    // =====================================================================
    // PHASE: PROMOTIONS ENGINE - BADGE DISPLAY FIELDS
    // =====================================================================
    
    /// <summary>
    /// Whether this product has an active promotion (offer).
    /// Updated by PromotionProjectionConsumer when promotions change.
    /// </summary>
    public bool HasActivePromotion { get; set; }
    
    /// <summary>
    /// Badge text to display (e.g., "20% OFF", "Save $10").
    /// </summary>
    public string? PromotionBadge { get; set; }
    
    /// <summary>
    /// Discount type: "percentage" or "fixed".
    /// </summary>
    public string? PromotionDiscountType { get; set; }
    
    /// <summary>
    /// Discount value for calculating discounted price.
    /// </summary>
    public decimal? PromotionDiscountValue { get; set; }
    
    // =====================================================================
    // AF-12: AFFILIATE MODE SUPPORT
    // =====================================================================
    
    /// <summary>
    /// Whether this product is enabled for affiliate mode.
    /// When true, product can be sold through affiliate referral links.
    /// </summary>
    public bool IsAffiliateEnabled { get; set; }
}

/// <summary>
/// Category DTO for storefront category landing pages.
/// Used for single category fetch by slug.
/// </summary>
public class CategoryReadDto
{
    /// <summary>
    /// Category unique identifier
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Category name for display
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Arabic category name (optional)
    /// </summary>
    public string? NameAr { get; set; }

    /// <summary>
    /// SEO-friendly URL slug
    /// </summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>
    /// Parent category ID (null for root categories)
    /// </summary>
    public Guid? ParentId { get; set; }
}

/// <summary>
/// Category DTO for hierarchical tree display.
/// Used for navigation menus, mega menus, footer menus.
/// </summary>
public class CategoryTreeDto
{
    /// <summary>
    /// Category unique identifier
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Category name for display
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Arabic category name (optional)
    /// </summary>
    public string? NameAr { get; set; }

    /// <summary>
    /// SEO-friendly URL slug
    /// </summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>
    /// Child categories (recursively populated)
    /// </summary>
    public List<CategoryTreeDto> Children { get; set; } = new();
    
    // ===========================================
    // PHASE: Promotions Engine - Category Banners
    // ===========================================
    
    /// <summary>
    /// Whether this category has an active promotion
    /// </summary>
    public bool HasActivePromotion { get; set; }
    
    /// <summary>
    /// Banner text to display at top of category page (e.g., "5% OFF")
    /// </summary>
    public string? PromotionBannerText { get; set; }
    
    /// <summary>
    /// End date of promotion for countdown timer (null = no end date)
    /// </summary>
    public DateTime? PromotionEndDate { get; set; }
    
    /// <summary>
    /// Banner display style: gradient, solid, minimal, festive
    /// </summary>
    public string? PromotionBannerStyle { get; set; }
}
    // =====================================================================
    // DOMAIN Management READ DTOs (DOMAIN-3.1-B: Storefront Domain Management)
    // =====================================================================

    public class DomainSummaryDto
    {
        public Guid Id { get; set; }
        public string HostName { get; set; } = string.Empty;
        public DomainType DomainType { get; set; }
        public DomainLifecycleState LifecycleState { get; set; }
        public RedirectType RedirectType { get; set; }
        public CertificateStatus SslStatus { get; set; }
        public string? SslExpiryDate { get; set; }
        public bool IsVerified { get; set; }
        public bool IsPrimary { get; set; }
        public int Version { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime? UpdatedAtUtc { get; set; }
        public string? CanonicalUrl { get; set; }
    }

    /// <summary>
    /// Detailed view of a single domain including related sub-resources.
    /// </summary>
    public class DomainDetailDto
    {
        public Guid Id { get; set; }
        public string HostName { get; set; } = string.Empty;
        public string NormalizedHostName { get; set; } = string.Empty;
        public DomainType DomainType { get; set; }
        public DomainLifecycleState LifecycleState { get; set; }
        public bool IsPrimary { get; set; }
        public int Version { get; set; }
        public RedirectType RedirectType { get; set; }
        public Guid? RedirectTargetDomainId { get; set; }
        public string? RedirectTargetHost { get; set; }
        public CanonicalPolicy CanonicalPolicy { get; set; }
        public string? SslCertificateThumbprint { get; set; }
        public Guid? SslCertificateId { get; set; }
        public bool IsVerified { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime? VerifiedAtUtc { get; set; }
        public DateTime? SslActivatedAtUtc { get; set; }
        public DateTime? UpdatedAtUtc { get; set; }
        public string? CanonicalUrl { get; set; }
    }

    /// <summary>
    /// DNS/HTTP verification details for a domain.
    /// </summary>
    public class DomainVerificationDto
    {
        public Guid DomainId { get; set; }
        public int Version { get; set; }
        public string HostName { get; set; } = string.Empty;
        public string NormalizedHostName { get; set; } = string.Empty;
        public VerificationStatus Status { get; set; }
        public VerificationMethod Method { get; set; }
        public string? VerificationToken { get; set; }
        public DateTime? VerifiedAtUtc { get; set; }
        public string? DnsRecordType { get; set; }
        public string? DnsRecordName { get; set; }
        public string? DnsRecordValue { get; set; }
        public string? DnsRecordHost { get; set; }
        public string? HttpVerificationUrl { get; set; }
    }

    /// <summary>
    /// SSL certificate details for a domain.
    /// </summary>
    public class DomainSslDto
    {
        public Guid? DomainId { get; set; }
        public Guid? CertificateId { get; set; }
        public ProviderType Provider { get; set; }
        public CertificateStatus Status { get; set; }
        public string? Thumbprint { get; set; }
        public string? NotBefore { get; set; }
        public string? NotAfter { get; set; }
        public DateTime? NotAfterUtc { get; set; }
        public bool AutoRenewEnabled { get; set; }
        public DateTime? NextRenewalUtc { get; set; }
        public DateTime? ActivatedAtUtc { get; set; }
        public List<string> SANs { get; set; } = new();
        public bool ExpiringSoon { get; set; }
        public int DaysUntilExpiry { get; set; }
    }

    /// <summary>
    /// Request body for DOMAIN-5.4 certificate provisioning admission.
    /// </summary>
    public class CertificateProvisioningRequest
    {
        public string? IdempotencyKey { get; set; }
    }

    /// <summary>
    /// Safe provisioning response. It exposes lifecycle state and opaque provider
    /// references only; it never contains certificate material or challenge secrets.
    /// </summary>
    public class CertificateProvisioningResponse
    {
        public Guid DomainId { get; set; }
        public Guid CertificateId { get; set; }
        public Guid OperationId { get; set; }
        public CertificateOperationType OperationType { get; set; }
        public CertificateOperationStatus OperationStatus { get; set; }
        public CertificateOperationPhase OperationPhase { get; set; }
        public CertificateStatus CertificateStatus { get; set; }
        public int Generation { get; set; }
        public string NormalizedHostName { get; set; } = string.Empty;
        public ProviderType Provider { get; set; }
        public string? ProviderEnvironment { get; set; }
        public string? ProviderRequestReference { get; set; }
        public string? OrderReference { get; set; }
        public bool Retryable { get; set; }
        public string? FailureCode { get; set; }
        public string? SafeFailureReason { get; set; }
        public string NextAction { get; set; } = string.Empty;
        public string Outcome { get; set; } = string.Empty;
        public string CorrelationId { get; set; } = string.Empty;
    }

    /// <summary>
    /// Request body for DOMAIN-5.5 certificate edge activation.
    /// </summary>
    public class CertificateActivationRequest
    {
        public string? IdempotencyKey { get; set; }
        public Guid? CertificateId { get; set; }
    }

    /// <summary>
    /// Safe activation response. It reports edge proof state only; it never
    /// contains certificate material, private keys, Caddy credentials, or raw
    /// Caddy configuration.
    /// </summary>
    public class CertificateActivationResponse
    {
        public Guid DomainId { get; set; }
        public Guid CertificateId { get; set; }
        public Guid OperationId { get; set; }
        public CertificateOperationType OperationType { get; set; }
        public CertificateOperationStatus OperationStatus { get; set; }
        public CertificateOperationPhase OperationPhase { get; set; }
        public CertificateStatus CertificateStatus { get; set; }
        public DomainLifecycleState DomainLifecycleState { get; set; }
        public int Generation { get; set; }
        public string NormalizedHostName { get; set; } = string.Empty;
        public string? EdgeIdentifier { get; set; }
        public string? LastObservedThumbprint { get; set; }
        public bool Retryable { get; set; }
        public string? FailureCode { get; set; }
        public string? SafeFailureReason { get; set; }
        public string NextAction { get; set; } = string.Empty;
        public string Outcome { get; set; } = string.Empty;
        public string CorrelationId { get; set; } = string.Empty;
    }

    /// <summary>
    /// Request body for manual DOMAIN-5.8 certificate renewal/reconciliation.
    /// </summary>
    public class CertificateRenewalCommandRequest
    {
        public Guid? CertificateId { get; set; }
        public string? IdempotencyKey { get; set; }
    }

    /// <summary>
    /// Request body for DOMAIN-5.8 certificate revocation.
    /// </summary>
    public class CertificateRevocationCommandRequest
    {
        public Guid CertificateId { get; set; }
        public string Reason { get; set; } = string.Empty;
        public string? IdempotencyKey { get; set; }
    }

    /// <summary>
    /// Request body for retrying a failed certificate operation.
    /// </summary>
    public class CertificateRetryCommandRequest
    {
        public Guid OperationId { get; set; }
        public string? IdempotencyKey { get; set; }
    }

    /// <summary>
    /// Request body for refreshing/reconciling certificate state.
    /// </summary>
    public class CertificateRefreshCommandRequest
    {
        public Guid? CertificateId { get; set; }
        public string? IdempotencyKey { get; set; }
    }

    /// <summary>
    /// Safe DOMAIN-5.8 certificate command response.
    /// </summary>
    public class CertificateCommandResponse
    {
        public Guid DomainId { get; set; }
        public Guid? CertificateId { get; set; }
        public Guid OperationId { get; set; }
        public CertificateOperationType OperationType { get; set; }
        public CertificateOperationStatus OperationStatus { get; set; }
        public CertificateOperationPhase OperationPhase { get; set; }
        public CertificateStatus? CertificateStatus { get; set; }
        public int? Generation { get; set; }
        public string Outcome { get; set; } = string.Empty;
        public bool Retryable { get; set; }
        public string? FailureCode { get; set; }
        public string? SafeFailureReason { get; set; }
        public DateTime? RetryAfterUtc { get; set; }
        public string NextAction { get; set; } = string.Empty;
        public string CorrelationId { get; set; } = string.Empty;
    }

    /// <summary>
    /// Safe metadata-only certificate read model.
    /// </summary>
    public class DomainCertificateDto
    {
        public Guid DomainId { get; set; }
        public Guid? CertificateId { get; set; }
        public int? Generation { get; set; }
        public ProviderType? Provider { get; set; }
        public CertificateStatus Status { get; set; }
        public CertificateOperationPhase OperationPhase { get; set; }
        public string? Issuer { get; set; }
        public string? SerialNumber { get; set; }
        public string? Thumbprint { get; set; }
        public string? Fingerprint { get; set; }
        public DateTime? NotBeforeUtc { get; set; }
        public DateTime? NotAfterUtc { get; set; }
        public DateTime? ActivatedAtUtc { get; set; }
        public DateTime? NextRenewalUtc { get; set; }
        public bool RenewalEligible { get; set; }
        public bool Retryable { get; set; }
        public string? FailureCode { get; set; }
        public string? SafeFailureReason { get; set; }
        public Guid? LastOperationId { get; set; }
        public CertificateOperationStatus? LastOperationStatus { get; set; }
        public CertificateOperationPhase? LastOperationPhase { get; set; }
        public DateTime? LastOperationUpdatedAtUtc { get; set; }
        public bool Degraded { get; set; }
    }

    /// <summary>
    /// Safe certificate operation read model.
    /// </summary>
    public class CertificateOperationDto
    {
        public Guid OperationId { get; set; }
        public Guid DomainId { get; set; }
        public Guid? CertificateId { get; set; }
        public CertificateOperationType OperationType { get; set; }
        public CertificateOperationStatus Status { get; set; }
        public CertificateOperationPhase Phase { get; set; }
        public bool Retryable { get; set; }
        public string? FailureCode { get; set; }
        public string? SafeFailureReason { get; set; }
        public DateTime? RetryAfterUtc { get; set; }
        public int AttemptCount { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime? UpdatedAtUtc { get; set; }
        public DateTime? CompletedAtUtc { get; set; }
        public string? CorrelationId { get; set; }
    }

    /// <summary>
    /// Bounded page of certificate operations.
    /// </summary>
    public class CertificateOperationPageDto
    {
        public List<CertificateOperationDto> Items { get; set; } = new();
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalCount { get; set; }
        public int TotalPages { get; set; }
        public bool HasNextPage { get; set; }
        public bool HasPreviousPage { get; set; }
    }

/// <summary>
/// A single redirect rule for a domain.
/// </summary>
    public class DomainRedirectDto
    {
        public Guid Id { get; set; }
        public Guid DomainId { get; set; }
        public string Source { get; set; } = string.Empty;
        public string Target { get; set; } = string.Empty;
        public RedirectType Type { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }

    /// <summary>
    /// Append-only audit log entry for a domain.
    /// </summary>
    public class DomainHistoryEntryDto
    {
        public Guid Id { get; set; }
        public Guid DomainId { get; set; }
        public string Action { get; set; } = string.Empty;
        public ActorType ActorType { get; set; }
        public Guid? ActorUserId { get; set; }
        public string? BeforeSnapshotJson { get; set; }
        public string AfterSnapshotJson { get; set; } = string.Empty;
        public string? TraceId { get; set; }
        public string? IpAddress { get; set; }
        public DateTime TimestampUtc { get; set; }
    }

    /// <summary>
    /// Dashboard summary for the domain management overview (D01).
    /// </summary>
    public class DomainDashboardSummaryDto
    {
        public DomainSummaryDto? PrimaryDomain { get; set; }
        public int TotalDomains { get; set; }
        public int SslExpiringSoon { get; set; }
        public int PendingVerification { get; set; }
        public int DraftCount { get; set; }
        public List<DomainSummaryDto> RecentDomains { get; set; } = new();
    }

    /// <summary>
    /// Background job activity entry for a domain.
    /// </summary>
    public class DomainActivityEntryDto
    {
        public Guid Id { get; set; }
        public Guid DomainId { get; set; }
        public string HostName { get; set; } = string.Empty;
        public string EventType { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? Details { get; set; }
        public DateTime TimestampUtc { get; set; }
        public DateTime? CompletedAtUtc { get; set; }
    }

    /// <summary>
    /// Activity dashboard summary for the domain management activity page (D09).
    /// </summary>
    public class DomainActivitySummaryDto
    {
        public int JobsRunning { get; set; }
        public int JobsSucceeded { get; set; }
        public int JobsFailed { get; set; }
        public List<DomainActivityEntryDto> RecentActivity { get; set; } = new();
    }

    /// <summary>
    /// Standard API error response format.
    /// </summary>
    public class ApiErrorResponse
{
    /// <summary>
    /// Error code for programmatic handling
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable error message
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Request correlation identifier for debugging/support.
    /// </summary>
    [JsonIgnore]
    public string? CorrelationId { get; set; }

    /// <summary>
    /// Additional structured error details.
    /// </summary>
    [JsonIgnore]
    public object? Details { get; set; }

    [JsonPropertyName("traceId")]
    public string? TraceId
    {
        get => CorrelationId;
        set => CorrelationId = value;
    }

    [JsonPropertyName("data")]
    public object? Data
    {
        get => Details;
        set => Details = value;
    }

    public ApiErrorResponse() { }

    public ApiErrorResponse(string code, string message, string? correlationId = null)
    {
        Code = code;
        Message = message;
        CorrelationId = correlationId;
    }

    // Factory methods for common errors
    public static ApiErrorResponse TenantHeaderMissing(string? correlationId = null) =>
        new("TENANT_HEADER_MISSING", "X-Tenant-Id header is required", correlationId);

    public static ApiErrorResponse TenantHeaderInvalid(string? correlationId = null) =>
        new("TENANT_HEADER_INVALID", "X-Tenant-Id must be a valid GUID", correlationId);

    public static ApiErrorResponse ProductNotFound(string? correlationId = null) =>
        new("PRODUCT_NOT_FOUND", "Product not found", correlationId);

    public static ApiErrorResponse ServiceUnavailable(string? correlationId = null) =>
        new("SERVICE_UNAVAILABLE", "Service temporarily unavailable", correlationId);

    public static ApiErrorResponse SearchUnavailable(string? correlationId = null) =>
        new("SEARCH_UNAVAILABLE", "Search is temporarily unavailable", correlationId);

    public static ApiErrorResponse InvalidPagination(string? correlationId = null) =>
        new("INVALID_PAGINATION", "Page and pageSize must be positive integers. pageSize max is 50.", correlationId);

    public static ApiErrorResponse InvalidSearchQuery(string? correlationId = null) =>
        new("INVALID_SEARCH_QUERY", "Search query is required and must be at least 2 characters.", correlationId);

    public static ApiErrorResponse CategoryNotFound(string? correlationId = null) =>
        new("CATEGORY_NOT_FOUND", "Category not found", correlationId);

    public static ApiErrorResponse InvalidToken(string? correlationId = null) =>
        new("INVALID_TOKEN", "Invalid authentication token", correlationId);

    /// <summary>
    /// Internal server error — for unexpected failures during request processing.
    /// </summary>
    public static ApiErrorResponse InternalError(string? correlationId = null) =>
        new("INTERNAL_ERROR", "An internal error occurred. Please try again later.", correlationId);
}

/// <summary>
/// Paginated result wrapper for list endpoints.
/// Frontend-ready.
/// </summary>
public class PagedResult<T>
{
    public List<T> Items { get; set; } = new();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
    public bool HasNextPage { get; set; }
    public bool HasPreviousPage { get; set; }

    public PagedResult() { }

    public PagedResult(List<T> items, int page, int pageSize, int totalCount)
    {
        Items = items;
        Page = page;
        PageSize = pageSize;
        TotalCount = totalCount;
        TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
        HasNextPage = page < TotalPages;
        HasPreviousPage = page > 1;
    }
}

/// <summary>
/// Page wrapper for the domain list endpoint (D04).
/// Separates pagination metadata from the PagedResult generic type
/// to allow future filtering/sorting context fields.
/// </summary>
public class DomainListPageDto
{
    public List<DomainSummaryDto> Items { get; set; } = new();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
    public bool HasNextPage { get; set; }
    public bool HasPreviousPage { get; set; }
}

/// <summary>
/// Page wrapper for the domain history endpoint (D08).
/// </summary>
public class DomainHistoryPageDto
{
    public List<DomainHistoryEntryDto> Items { get; set; } = new();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
    public bool HasNextPage { get; set; }
    public bool HasPreviousPage { get; set; }
}

// =====================================================================
// DOMAIN Management WRITE DTOs (DOMAIN-3.1-C: Storefront Domain Management)
// =====================================================================

/// <summary>
/// Request body for creating a new domain.
/// POST /api/admin/storefront/domains
/// </summary>
public class CreateDomainRequest
{
    /// <summary>
    /// Original hostname as entered by the merchant (preserves case).
    /// Max 253 characters per RFC 1035.
    /// </summary>
    public string HostName { get; set; } = string.Empty;

    /// <summary>
    /// Domain type (Primary, Alias, Preview, Development).
    /// Redirect and Temporary types must not be set at creation.
    /// </summary>
    public DomainType DomainType { get; set; }

    /// <summary>
    /// Redirect type for redirect domains. None for non-redirect types.
    /// </summary>
    public RedirectType RedirectType { get; set; } = RedirectType.None;

    /// <summary>
    /// Target domain ID for redirect domains. Null for non-redirect types.
    /// </summary>
    public Guid? RedirectTargetDomainId { get; set; }

    /// <summary>
    /// Canonical URL policy for alias domains.
    /// </summary>
    public CanonicalPolicy CanonicalPolicy { get; set; } = CanonicalPolicy.UseAsPrimary;

    /// <summary>
    /// Idempotency key for safe retries. If a domain with this key was
    /// already created, the existing domain is returned.
    /// </summary>
    public string? IdempotencyKey { get; set; }
}

/// <summary>
/// Response body for domain mutation commands (Create, Update, Archive, Restore, Delete, PromotePrimary).
/// </summary>
public class DomainMutationResponse
{
    public Guid DomainId { get; set; }
    public string HostName { get; set; } = string.Empty;
    public string NormalizedHostName { get; set; } = string.Empty;
    public DomainType DomainType { get; set; }
    public DomainLifecycleState LifecycleState { get; set; }
    public DomainLifecycleState? PreviousState { get; set; }
    public int Version { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public string? AuditEntryId { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public bool WasChanged { get; set; } = true;
}

/// <summary>
/// Request body for explicitly starting ownership verification.
/// PUT /api/admin/storefront/domains/{id}/verification/start
/// </summary>
public class StartDomainVerificationRequest
{
    /// <summary>
    /// Optional verification method. Null keeps the domain's existing method
    /// or uses DNS TXT for domains that do not yet have a method.
    /// </summary>
    public VerificationMethod? VerificationMethod { get; set; }

    /// <summary>
    /// Optimistic concurrency token. If provided, must match current domain Version.
    /// </summary>
    public int? ExpectedVersion { get; set; }
}

/// <summary>
/// Request body for executing a manual verification attempt.
/// PUT /api/admin/storefront/domains/{id}/verification/verify
/// </summary>
public class VerifyDomainNowRequest
{
    /// <summary>
    /// Optimistic concurrency token. Reserved for clients that already have
    /// the current domain detail version.
    /// </summary>
    public int? ExpectedVersion { get; set; }
}

/// <summary>
/// Request body for retrying a failed verification attempt.
/// PUT /api/admin/storefront/domains/{id}/verification/retry
/// </summary>
public class RetryDomainVerificationRequest
{
    /// <summary>
    /// Optimistic concurrency token. Reserved for clients that already have
    /// the current domain detail version.
    /// </summary>
    public int? ExpectedVersion { get; set; }
}

/// <summary>
/// Request body for refreshing a verification token before ownership is proven.
/// PUT /api/admin/storefront/domains/{id}/verification/token
/// </summary>
public class RefreshDomainVerificationTokenRequest
{
    /// <summary>
    /// Optional verification method to use with the refreshed token.
    /// Null preserves the current method or defaults to DNS TXT.
    /// </summary>
    public VerificationMethod? VerificationMethod { get; set; }

    /// <summary>
    /// Optimistic concurrency token. If provided, must match current domain Version.
    /// </summary>
    public int? ExpectedVersion { get; set; }
}

/// <summary>
/// Safe, deterministic result for verification commands.
/// Does not expose raw DNS/HTTP responses or expected values.
/// </summary>
public class DomainVerificationCommandResponse
{
    public Guid DomainId { get; set; }
    public string Operation { get; set; } = string.Empty;
    public VerificationStatus Status { get; set; }
    public DomainLifecycleState? LifecycleState { get; set; }
    public VerificationMethod? Method { get; set; }
    public string Outcome { get; set; } = string.Empty;
    public string? FailureReason { get; set; }
    public bool RetryEligible { get; set; }
    public DateTime? RetryAfterUtc { get; set; }
    public int? AttemptNumber { get; set; }
    public string NextAction { get; set; } = string.Empty;
    public DomainVerificationDto? Instructions { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
}

/// <summary>
/// Request body for partial domain update.
/// PATCH /api/admin/storefront/domains/{id}
/// </summary>
public class UpdateDomainRequest
{
    /// <summary>
    /// Hostname to update. Null = no change.
    /// </summary>
    public string? HostName { get; set; }

    /// <summary>
    /// Canonical policy to update. Null = no change.
    /// </summary>
    public CanonicalPolicy? CanonicalPolicy { get; set; }

    /// <summary>
    /// Redirect type to update. Null = no change.
    /// </summary>
    public RedirectType? RedirectType { get; set; }

    /// <summary>
    /// Redirect target domain ID to update. Null = no change.
    /// </summary>
    public Guid? RedirectTargetDomainId { get; set; }

    /// <summary>
    /// Optimistic concurrency token. If provided, must match current domain Version.
    /// </summary>
    public int? ExpectedVersion { get; set; }
}

/// <summary>
/// Request body for promoting a domain to Primary.
/// PUT /api/admin/storefront/domains/{id}/primary
/// </summary>
public class PromotePrimaryDomainRequest
{
    /// <summary>
    /// Optimistic concurrency token for the domain being promoted.
    /// </summary>
    public int? ExpectedVersion { get; set; }

    /// <summary>
    /// Optimistic concurrency token for the current primary domain being demoted.
    /// If null, the server reads the current primary's version.
    /// </summary>
    public int? PreviousPrimaryExpectedVersion { get; set; }
}

/// <summary>
/// Query parameters for restore operation.
/// PUT /api/admin/storefront/domains/{id}/restore
/// </summary>
public class RestoreDomainRequest
{
    public int? ExpectedVersion { get; set; }

    /// <summary>
    /// Optional reason for the restore operation.
    /// Recorded in the audit trail.
    /// </summary>
    public string? Reason { get; set; }
}

/// <summary>
/// Query parameters for archive operation.
/// PUT /api/admin/storefront/domains/{id}/archive
/// </summary>
public class ArchiveDomainRequest
{
    public int? ExpectedVersion { get; set; }

    /// <summary>
    /// Reason for archiving the domain.
    /// Recorded in the audit trail.
    /// </summary>
    public string? Reason { get; set; }
}

/// <summary>
/// Request body for delete operation.
/// DELETE /api/admin/storefront/domains/{id}
/// </summary>
public class DeleteDomainRequest
{
    public int? ExpectedVersion { get; set; }

    /// <summary>
    /// Reason for soft-deleting the domain.
    /// Recorded in the audit trail.
    /// </summary>
    public string? Reason { get; set; }
}

/// <summary>
/// Query parameters for product list endpoint.
/// </summary>
public class ProductListQuery
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public Guid? CategoryId { get; set; }
    public bool IsActive { get; set; } = true;

    public const int MinPage = 1;
    public const int MinPageSize = 1;
    public const int MaxPageSize = 50;
    public const int DefaultPageSize = 20;

    /// <summary>
    /// Validates and normalizes the query parameters.
    /// Returns null if valid, or an error message if invalid.
    /// </summary>
    public string? Validate()
    {
        if (Page < MinPage)
            return $"Page must be at least {MinPage}";
        
        if (PageSize < MinPageSize || PageSize > MaxPageSize)
            return $"PageSize must be between {MinPageSize} and {MaxPageSize}";

        return null;
    }

    /// <summary>
    /// Generates a deterministic hash for cache key.
    /// </summary>
    public string GetQueryHash()
    {
        // Build deterministic string for hashing
        var parts = new[]
        {
            $"p={Page}",
            $"ps={PageSize}",
            $"cat={CategoryId?.ToString("N") ?? "null"}",
            $"active={IsActive}"
        };
        
        var queryString = string.Join("&", parts);
        
        // Use simple hash for cache key (not cryptographic)
        var hash = queryString.GetHashCode();
        return Math.Abs(hash).ToString("X8");
    }
}

/// <summary>
/// Search result wrapper with search-specific metadata.
/// Extends paged results with search timing and query info.
/// </summary>
public class SearchResult<T>
{
    /// <summary>
    /// The search results.
    /// </summary>
    public List<T> Items { get; set; } = new();

    /// <summary>
    /// The original search query.
    /// </summary>
    public string Query { get; set; } = string.Empty;

    /// <summary>
    /// Current page number (1-indexed).
    /// </summary>
    public int Page { get; set; }

    /// <summary>
    /// Number of items per page.
    /// </summary>
    public int PageSize { get; set; }

    /// <summary>
    /// Total number of matching items.
    /// </summary>
    public int TotalCount { get; set; }

    /// <summary>
    /// Total number of pages.
    /// </summary>
    public int TotalPages { get; set; }

    /// <summary>
    /// Time taken for the search in milliseconds.
    /// </summary>
    public long SearchTimeMs { get; set; }

    /// <summary>
    /// Whether there are more pages after this one.
    /// </summary>
    public bool HasNextPage { get; set; }

    /// <summary>
    /// Whether there are pages before this one.
    /// </summary>
    public bool HasPreviousPage { get; set; }

    public SearchResult() { }

    public SearchResult(
        List<T> items, 
        string query,
        int page, 
        int pageSize, 
        int totalCount,
        long searchTimeMs)
    {
        Items = items;
        Query = query;
        Page = page;
        PageSize = pageSize;
        TotalCount = totalCount;
        TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
        SearchTimeMs = searchTimeMs;
        HasNextPage = page < TotalPages;
        HasPreviousPage = page > 1;
    }
}

/// <summary>
/// Query parameters for product search endpoint.
/// </summary>
public class ProductSearchQuery
{
    /// <summary>
    /// Search query string (required, min 2 chars).
    /// </summary>
    public string Q { get; set; } = string.Empty;

    /// <summary>
    /// Page number (1-indexed, default: 1).
    /// </summary>
    public int Page { get; set; } = 1;

    /// <summary>
    /// Items per page (default: 20, max: 50).
    /// </summary>
    public int PageSize { get; set; } = 20;

    /// <summary>
    /// Optional category filter.
    /// </summary>
    public Guid? CategoryId { get; set; }

    /// <summary>
    /// Optional minimum price filter.
    /// </summary>
    public decimal? MinPrice { get; set; }

    /// <summary>
    /// Optional maximum price filter.
    /// </summary>
    public decimal? MaxPrice { get; set; }

    public const int MinQueryLength = 2;
    public const int MinPage = 1;
    public const int MinPageSize = 1;
    public const int MaxPageSize = 50;
    public const int DefaultPageSize = 20;

    /// <summary>
    /// Validates search query parameters.
    /// Returns error code if invalid, null if valid.
    /// </summary>
    public (string? ErrorCode, string? ErrorMessage) Validate()
    {
        // Validate search query
        if (string.IsNullOrWhiteSpace(Q) || Q.Trim().Length < MinQueryLength)
        {
            return ("INVALID_SEARCH_QUERY", $"Search query must be at least {MinQueryLength} characters");
        }

        // Validate pagination
        if (Page < MinPage)
        {
            return ("INVALID_PAGINATION", $"Page must be at least {MinPage}");
        }

        if (PageSize < MinPageSize || PageSize > MaxPageSize)
        {
            return ("INVALID_PAGINATION", $"PageSize must be between {MinPageSize} and {MaxPageSize}");
        }

        // Validate price range
        if (MinPrice.HasValue && MaxPrice.HasValue && MinPrice > MaxPrice)
        {
            return ("INVALID_SEARCH_QUERY", "MinPrice cannot be greater than MaxPrice");
        }

        return (null, null);
    }

    /// <summary>
    /// Normalizes the search query for consistent searching.
    /// Applies: trim whitespace, convert to lowercase.
    /// NOTE: This normalized query is used for ES/MongoDB searches,
    /// but the original Q is preserved in SearchResult.Query for display.
    /// </summary>
    public string GetNormalizedQuery() => Q?.Trim().ToLowerInvariant() ?? string.Empty;
}
