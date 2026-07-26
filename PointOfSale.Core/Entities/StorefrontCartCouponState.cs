namespace PointOfSale.Core.Entities;

/// <summary>
/// Durable source of truth for coupon state on a storefront cart.
/// Redis remains a cache for cart payloads, but coupon mutations are
/// committed here so they can share a transaction boundary with the outbox.
/// </summary>
public sealed class StorefrontCartCouponState : TenantEntity
{
    public Guid CartId { get; private set; }
    public string OwnerType { get; private set; } = string.Empty;
    public string OwnerId { get; private set; } = string.Empty;
    public bool HasAppliedCoupon { get; private set; }
    public Guid? CouponId { get; private set; }
    public string? CouponCode { get; private set; }
    public string? DiscountType { get; private set; }
    public decimal? DiscountValue { get; private set; }
    public decimal? DiscountAmount { get; private set; }
    public decimal CartSubtotal { get; private set; }
    public Guid? CustomerId { get; private set; }
    public int MutationVersion { get; private set; }
    public string? LastRemovalReason { get; private set; }

    private StorefrontCartCouponState()
    {
    }

    public static StorefrontCartCouponState Create(
        Guid tenantId,
        Guid cartId,
        string ownerType,
        string ownerId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant id cannot be empty.", nameof(tenantId));
        }

        if (cartId == Guid.Empty)
        {
            throw new ArgumentException("Cart id cannot be empty.", nameof(cartId));
        }

        if (string.IsNullOrWhiteSpace(ownerType))
        {
            throw new ArgumentException("Owner type is required.", nameof(ownerType));
        }

        if (string.IsNullOrWhiteSpace(ownerId))
        {
            throw new ArgumentException("Owner id is required.", nameof(ownerId));
        }

        var normalizedOwnerType = ownerType.Trim();
        if (normalizedOwnerType != "customer" && normalizedOwnerType != "session")
        {
            throw new ArgumentException("Owner type must be 'customer' or 'session'.", nameof(ownerType));
        }

        return new StorefrontCartCouponState
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CartId = cartId,
            OwnerType = normalizedOwnerType,
            OwnerId = ownerId.Trim(),
            CreatedAt = DateTime.UtcNow,
            CartSubtotal = 0m,
            MutationVersion = 0
        };
    }

    public void ApplyCoupon(
        Guid couponId,
        string couponCode,
        string discountType,
        decimal discountValue,
        decimal discountAmount,
        decimal cartSubtotal,
        Guid? customerId)
    {
        if (couponId == Guid.Empty)
        {
            throw new ArgumentException("Coupon id is required.", nameof(couponId));
        }

        if (string.IsNullOrWhiteSpace(couponCode))
        {
            throw new ArgumentException("Coupon code is required.", nameof(couponCode));
        }

        if (string.IsNullOrWhiteSpace(discountType))
        {
            throw new ArgumentException("Discount type is required.", nameof(discountType));
        }

        if (discountValue < 0)
        {
            throw new ArgumentException("Discount value cannot be negative.", nameof(discountValue));
        }

        if (discountAmount < 0)
        {
            throw new ArgumentException("Discount amount cannot be negative.", nameof(discountAmount));
        }

        if (cartSubtotal < 0)
        {
            throw new ArgumentException("Cart subtotal cannot be negative.", nameof(cartSubtotal));
        }

        HasAppliedCoupon = true;
        CouponId = couponId;
        CouponCode = couponCode.Trim();
        DiscountType = discountType.Trim();
        DiscountValue = discountValue;
        DiscountAmount = discountAmount;
        CartSubtotal = cartSubtotal;
        CustomerId = customerId;
        LastRemovalReason = null;
        MutationVersion++;
        UpdatedAt = DateTime.UtcNow;
    }

    public void ClearCoupon(
        decimal cartSubtotal,
        Guid? customerId,
        string removalReason)
    {
        if (cartSubtotal < 0)
        {
            throw new ArgumentException("Cart subtotal cannot be negative.", nameof(cartSubtotal));
        }

        HasAppliedCoupon = false;
        CouponId = null;
        CouponCode = null;
        DiscountType = null;
        DiscountValue = null;
        DiscountAmount = null;
        CartSubtotal = cartSubtotal;
        CustomerId = customerId;
        LastRemovalReason = string.IsNullOrWhiteSpace(removalReason)
            ? "unknown"
            : removalReason.Trim();
        MutationVersion++;
        UpdatedAt = DateTime.UtcNow;
    }
}
