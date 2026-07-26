namespace PointOfSale.API.DTOs.Checkout;

/// <summary>
/// Request to create or resume a checkout session.
/// Sent when customer enters the checkout page.
/// </summary>
public class CreateCheckoutSessionRequest
{
    /// <summary>
    /// Cart identifier from storefront session
    /// </summary>
    public Guid CartId { get; set; }

    /// <summary>
    /// JSON snapshot of cart items for display in admin dashboard.
    /// Structure: [{ variantId, productName, quantity, price, imageUrl }]
    /// </summary>
    public string? ItemsSnapshotJson { get; set; }

    /// <summary>
    /// Cart subtotal
    /// </summary>
    public decimal? CartTotal { get; set; }

    /// <summary>
    /// Currency code (e.g., "EGP")
    /// </summary>
    public string? Currency { get; set; }

    /// <summary>
    /// Device type: Mobile, Desktop, Tablet (from User-Agent).
    /// </summary>
    public string? DeviceType { get; set; }

    /// <summary>
    /// Traffic source: Direct, Instagram, GoogleAds, etc. (from utm_source).
    /// </summary>
    public string? Source { get; set; }
}

/// <summary>
/// Request to update checkout session fields (progressive autosave).
/// Only non-null fields are updated.
/// </summary>
public class UpdateCheckoutSessionRequest
{
    public string? CustomerName { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? City { get; set; }
    public string? Area { get; set; }
    public string? Address { get; set; }
    public string? Country { get; set; }
    public string? Notes { get; set; }
    public Guid? ShippingMethodId { get; set; }
    public string? PaymentMethodCode { get; set; }
    public string? WalletType { get; set; }
    public string? WalletPhoneNumber { get; set; }
    public string? ItemsSnapshotJson { get; set; }
    public decimal? CartTotal { get; set; }
    public string? Currency { get; set; }
}

/// <summary>
/// Response after creating or resuming a checkout session.
/// </summary>
public class CheckoutSessionResponse
{
    public Guid SessionId { get; set; }
    public string Status { get; set; } = "Active";
    public string? Recovery { get; set; }
    public string RecoveryToken { get; set; } = string.Empty;
    public string ManualWalletUploadToken { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime LastActivityAtUtc { get; set; }
}

public class CheckoutSessionMutationResponse
{
    private CheckoutSessionMutationResponse() { }

    public bool Success { get; private set; }
    public string Status { get; private set; } = string.Empty;
    public string? Recovery { get; private set; }

    /// <summary>
    /// Creates a successful response.
    /// </summary>
    public static CheckoutSessionMutationResponse CreateSuccess(string? recovery = null)
    {
        return new CheckoutSessionMutationResponse
        {
            Success = true,
            Status = "success",
            Recovery = recovery
        };
    }

    /// <summary>
    /// Creates a partial success response with recovery information.
    /// </summary>
    public static CheckoutSessionMutationResponse CreatePartialSuccess(string recovery)
    {
        return new CheckoutSessionMutationResponse
        {
            Success = true,
            Status = "partial_success",
            Recovery = recovery
        };
    }

    /// <summary>
    /// Creates a failure response.
    /// </summary>
    public static CheckoutSessionMutationResponse CreateFailure(string? recovery = null)
    {
        return new CheckoutSessionMutationResponse
        {
            Success = false,
            Status = "failure",
            Recovery = recovery
        };
    }
}

/// <summary>
/// Restorable checkout payload returned by the recovery endpoint.
/// Contains enough state for the storefront to rehydrate the checkout form
/// and restore guest carts across devices by reusing the original session id.
/// </summary>
public class CheckoutRecoveryResponse
{
    public Guid SessionId { get; set; }
    public string? GuestSessionId { get; set; }
    public string? CheckoutToken { get; set; }
    public Guid CartId { get; set; }
    public string Status { get; set; } = "Active";
    public List<AbandonedCheckoutItemDto> Cart { get; set; } = new();
    public RecoveredCheckoutAddressDto Address { get; set; } = new();
    public RecoveredCheckoutShippingMethodDto? ShippingMethod { get; set; }
    public RecoveredCheckoutPaymentMethodDto? PaymentMethod { get; set; }
    public string? Notes { get; set; }
    public decimal? CartTotal { get; set; }
    public string? Currency { get; set; }
    public Guid? ShippingMethodId { get; set; }
    public string? PaymentMethodCode { get; set; }
    public string? CouponCode { get; set; }
    public decimal? CouponDiscount { get; set; }
    public string? CouponType { get; set; }
    public decimal? CouponValue { get; set; }
    public Guid? PromotionId { get; set; }
    public decimal? CartPromotionDiscount { get; set; }
    public string? PromotionName { get; set; }
    public string? PromotionDescription { get; set; }
    public string? PromotionDiscountType { get; set; }
    public decimal? PromotionDiscountValue { get; set; }
    public string? PromotionBadge { get; set; }
    public bool CartWasRevalidated { get; set; }
    public bool CartChangedDuringRecovery { get; set; }
    public bool RequiresReview { get; set; }
    public string? RecoveryMessage { get; set; }
    public DateTime? RevalidatedAtUtc { get; set; }
}

public class RecoveredCheckoutAddressDto
{
    public string FullName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string City { get; set; } = string.Empty;
    public string Area { get; set; } = string.Empty;
    public string AddressLine { get; set; } = string.Empty;
    public string? Country { get; set; }
}

public class RecoveredCheckoutShippingMethodDto
{
    public Guid Id { get; set; }
}

public class RecoveredCheckoutPaymentMethodDto
{
    public string Code { get; set; } = string.Empty;
}

/// <summary>
/// Abandoned checkout item for admin dashboard display.
/// </summary>
public class AbandonedCheckoutListItem
{
    public string? MongoId { get; set; }
    public Guid CheckoutSessionId { get; set; }
    public Guid CartId { get; set; }
    public string? CustomerName { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? City { get; set; }
    public decimal? CartTotal { get; set; }
    public string? Currency { get; set; }
    public int ItemCount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? DeviceType { get; set; }
    public string? Source { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? FirstActivityAtUtc { get; set; }
    public DateTime LastActivityAtUtc { get; set; }
    public DateTime? AbandonedAtUtc { get; set; }
    public DateTime? ConvertedAtUtc { get; set; }
    public string? ConvertedOrderNumber { get; set; }
    /// <summary>
    /// Calculated: LastActivityAtUtc - FirstActivityAtUtc (in seconds).
    /// Null if FirstActivityAtUtc is null.
    /// </summary>
    public double? TimeSpentSeconds { get; set; }
    /// <summary>
    /// Backend-generated recovery URL with correct tenant storefront domain.
    /// Example: "http://mena.localhost:5174/recover-checkout/abc123"
    /// </summary>
    public string? RecoveryUrl { get; set; }
}

/// <summary>
/// Detailed view of an abandoned checkout for admin dashboard.
/// </summary>
public class AbandonedCheckoutDetail
{
    public Guid CheckoutSessionId { get; set; }
    public Guid CartId { get; set; }

    // Customer info
    public string? CustomerName { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? City { get; set; }
    public string? Area { get; set; }
    public string? Address { get; set; }
    public string? Notes { get; set; }

    // Cart snapshot
    public List<AbandonedCheckoutItemDto> Items { get; set; } = new();
    public decimal? CartTotal { get; set; }
    public string? Currency { get; set; }

    // Checkout progress
    public Guid? ShippingMethodId { get; set; }
    public string? PaymentMethodCode { get; set; }
    public string? WalletType { get; set; }

    // Status
    public string Status { get; set; } = string.Empty;
    public string RecoveryToken { get; set; } = string.Empty;
    public string? DeviceType { get; set; }
    public string? Source { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? FirstActivityAtUtc { get; set; }
    public DateTime LastActivityAtUtc { get; set; }
    public DateTime? AbandonedAtUtc { get; set; }
    public DateTime? ConvertedAtUtc { get; set; }
    public Guid? ConvertedOrderId { get; set; }
    public string? ConvertedOrderNumber { get; set; }
    public double? TimeSpentSeconds { get; set; }
    /// <summary>
    /// Backend-generated recovery URL with correct tenant storefront domain.
    /// Example: "http://mena.localhost:5174/recover-checkout/abc123"
    /// </summary>
    public string? RecoveryUrl { get; set; }
}

public class AbandonedCheckoutItemDto
{
    public Guid? VariantId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? VariantName { get; set; }
    public int Quantity { get; set; }
    public decimal Price { get; set; }
    public string? ImageUrl { get; set; }
}

/// <summary>
/// Paged response for admin dashboard.
/// </summary>
public class AbandonedCheckoutPagedResponse
{
    public List<AbandonedCheckoutListItem> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
    public bool HasMore { get; set; }
}
