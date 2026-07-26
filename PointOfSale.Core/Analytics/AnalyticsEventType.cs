namespace PointOfSale.Core.Analytics;

/// <summary>
/// Business-meaningful analytics event types.
/// 
/// ARCHITECTURAL RULES:
/// - Every event type MUST have business value
/// - NO GA-style events (e.g., "gtm.js", "gtag.config")
/// - NO UI-only events (e.g., "button_hover", "scroll")
/// - POS owns this schema - NOT third-party analytics providers
/// 
/// These events can later be mapped to any analytics provider
/// (Google Analytics, Meta Pixel, Segment, etc.) without refactoring.
/// </summary>
public enum AnalyticsEventType
{
    /// <summary>
    /// User viewed a page on the storefront.
    /// Payload: PageUrl required.
    /// </summary>
    PageView = 1,

    /// <summary>
    /// Product was displayed in a list (search results, category page, homepage).
    /// Payload: ProductId required, position optional.
    /// </summary>
    ProductImpression = 2,

    /// <summary>
    /// User viewed a product detail page.
    /// Payload: ProductId required, VariantId optional.
    /// </summary>
    ProductView = 3,

    /// <summary>
    /// User performed a search.
    /// Payload: SearchQuery required, result count optional.
    /// </summary>
    SearchPerformed = 4,

    /// <summary>
    /// User applied a filter on product listing.
    /// Payload: Filters dictionary required.
    /// </summary>
    FilterApplied = 5,

    /// <summary>
    /// User added a product to cart.
    /// Payload: ProductId, VariantId, Quantity, Value required.
    /// </summary>
    AddToCart = 6,

    /// <summary>
    /// User removed a product from cart.
    /// Payload: ProductId, VariantId, Quantity required.
    /// </summary>
    RemoveFromCart = 7,

    /// <summary>
    /// User started the checkout process.
    /// Payload: Value (cart total) required.
    /// </summary>
    BeginCheckout = 8,

    /// <summary>
    /// User completed a purchase.
    /// Payload: OrderId, Value required.
    /// </summary>
    PurchaseCompleted = 9,

    /// <summary>
    /// Payment failed during checkout.
    /// Payload: OrderId optional, error details in Extra.
    /// </summary>
    PaymentFailed = 10,

    /// <summary>
    /// Backend payment succeeded, but post-success storefront cleanup failed.
    /// Payload: OrderId optional, failure reason in Extra.
    /// </summary>
    CheckoutFinalizeFailure = 11,

    /// <summary>
    /// Storefront auth verification entered a degraded state after a transient failure.
    /// Payload: Extra contains source/reason metadata.
    /// </summary>
    AuthStateDegraded = 12,

    /// <summary>
    /// A persisted checkout selection no longer matched the latest storefront configuration.
    /// Payload: Extra contains selection_type/reason metadata.
    /// </summary>
    CheckoutSelectionInvalidated = 13,

    /// <summary>
    /// A cart response was detected to have inconsistent aggregate state.
    /// Payload: Extra contains surface/reason metadata.
    /// </summary>
    CartStateMismatchDetected = 14,

    /// <summary>
    /// A guest payment recovery flow successfully reloaded payment state after refresh.
    /// Payload: Extra contains recovery source metadata.
    /// </summary>
    GuestPaymentRecoverySuccess = 15,

    /// <summary>
    /// An unsafe cart clear was blocked because the paid order did not match active checkout context.
    /// Payload: Extra contains reason metadata.
    /// </summary>
    CartUnexpectedClearBlocked = 16,

    /// <summary>
    /// A legacy inbound guest tracking token was removed from the storefront URL.
    /// Payload: Extra contains token source metadata, never the token value.
    /// </summary>
    TrackingTokenRemovedFromUrl = 17,

    /// <summary>
    /// Storefront auth recovered from an explicit degraded state.
    /// Payload: Extra contains source/outcome metadata.
    /// </summary>
    AuthDegradedRecovery = 18,

    /// <summary>
    /// Checkout state was cleared to prevent cross-user state reuse.
    /// Payload: Extra contains trigger metadata.
    /// </summary>
    CheckoutStateLeakPrevented = 19
}
