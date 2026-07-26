namespace PointOfSale.API.DTOs.Funnel;

/// <summary>
/// AF-27: Affiliate Funnel Start Request.
/// 
/// Allows affiliate landing pages to server-side inject a product into a cart
/// and redirect directly to checkout, reducing funnel friction.
/// 
/// SECURITY:
/// - productId is server-validated against tenant
/// - referralCode is server-validated globally (AF-19 global marketplace)
/// - Tenant is resolved from request context (NOT from body)
/// - No client-side trust
/// </summary>
public class FunnelStartRequest
{
    /// <summary>
    /// Product ID to add to cart.
    /// Must belong to the resolved tenant and be active, in stock, and affiliate-enabled.
    /// </summary>
    public Guid ProductId { get; set; }

    /// <summary>
    /// Quantity to add (default: 1, max: 10 for funnel).
    /// </summary>
    public int Quantity { get; set; } = 1;

    /// <summary>
    /// Affiliate referral code (e.g., "ZEZO").
    /// Optional — if invalid or missing, checkout proceeds without attribution.
    /// AF-27E: Renamed from AffiliateCode for consistency.
    /// </summary>
    public string? ReferralCode { get; set; }

    /// <summary>
    /// Optional Variant ID for variant products.
    /// If the product has variants, this must be provided.
    /// </summary>
    public Guid? VariantId { get; set; }

    /// <summary>
    /// AF-27A: Single product funnel mode.
    /// When true:
    /// - Quantity is forced to 1 (ignores Quantity field)
    /// - If the product is already in cart, quantity stays at 1 (no accumulation)
    /// - Prevents infinite duplication from repeated CTA clicks
    /// Default: false (normal mode, quantity accumulates)
    /// </summary>
    public bool SingleProductMode { get; set; } = false;

    /// <summary>
    /// AF-28 Preparation: The template version the client is rendering from.
    /// Included for observability and to detect template rendering trends.
    /// Default: "Classic:v1"
    /// </summary>
    public string TemplateType { get; set; } = "Classic:v1";

    /// <summary>
    /// Observability: Storefront Tenant Slug
    /// </summary>
    public string TenantSlug { get; set; } = string.Empty;

    /// <summary>
    /// Observability: Product Slug
    /// </summary>
    public string ProductSlug { get; set; } = string.Empty;


    /// <summary>
    /// Validates the funnel start request.
    /// </summary>
    public (string? ErrorCode, string? ErrorMessage) Validate()
    {
        if (ProductId == Guid.Empty)
        {
            return ("INVALID_PRODUCT_ID", "Product ID is required");
        }

        if (Quantity < 1)
        {
            return ("INVALID_QUANTITY", "Quantity must be at least 1");
        }

        if (Quantity > 10)
        {
            return ("INVALID_QUANTITY", "Funnel quantity cannot exceed 10");
        }

        // ReferralCode is optional — silent fallback if invalid
        if (!string.IsNullOrWhiteSpace(ReferralCode) && ReferralCode.Trim().Length > 20)
        {
            return ("INVALID_REFERRAL_CODE", "Referral code is too long");
        }

        return (null, null);
    }
}

/// <summary>
/// AF-27: Affiliate Funnel Start Response.
/// Returns the checkout URL for client-side redirect.
/// </summary>
public class FunnelStartResponse
{
    /// <summary>
    /// Checkout URL to redirect the user to.
    /// Format: /checkout?cartId={cartId}
    /// </summary>
    public string CheckoutUrl { get; set; } = string.Empty;

    /// <summary>
    /// Cart ID created/used for this funnel session.
    /// </summary>
    public Guid CartId { get; set; }

    /// <summary>
    /// Whether affiliate attribution was successfully attached.
    /// false if affiliate code was invalid/missing (checkout still proceeds).
    /// </summary>
    public bool AffiliateAttached { get; set; }

    /// <summary>
    /// Number of items in the cart after funnel injection.
    /// </summary>
    public int ItemCount { get; set; }

    /// <summary>
    /// Product name (for confirmation display if needed).
    /// </summary>
    public string? ProductName { get; set; }
}
