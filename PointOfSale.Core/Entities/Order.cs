using System.ComponentModel.DataAnnotations.Schema;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Financial;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

/// <summary>
/// E-commerce order entity (separate from POS SalesInvoice)
/// Supports multi-channel orders from E-commerce, Amazon, and other marketplaces
/// </summary>
public class Order : BaseEntity, ITenantOwned, IProjectionVersioned
{
    /// <summary>
    /// The tenant that owns this order.
    /// CRITICAL: NOT NULL - enforced at database level.
    /// </summary>
    public Guid TenantId { get; set; }
    
    /// <summary>
    /// Unique order number (e.g., ORD-2024-00001)
    /// </summary>
    public string OrderNumber { get; set; } = string.Empty;
    
    /// <summary>
    /// POS registered customer (null for storefront/guest checkout)
    /// FK references Customers table (POS customers)
    /// </summary>
    public Guid? CustomerId { get; set; }
    public Customer? Customer { get; set; }
    
    /// <summary>
    /// Storefront online customer (null for POS/guest checkout)
    /// FK references OnlineCustomers table (Storefront customers)
    /// 
    /// PHASE 10.4: Added to support proper order association with storefront customers.
    /// When an OnlineCustomer places an order, this field is set, NOT CustomerId.
    /// </summary>
    public Guid? OnlineCustomerId { get; set; }
    public OnlineCustomer? OnlineCustomer { get; set; }

    /// <summary>
    /// Canonical analytics session reference captured from storefront checkout.
    /// This is a reference only; journey data remains in the analytics domain.
    /// </summary>
    public string? SessionId { get; set; }

    /// <summary>
    /// Storefront cart that produced this order.
    /// Used as the durable duplicate-order guard for active checkout attempts.
    /// Historical non-storefront orders may not have a cart id.
    /// </summary>
    public Guid? CartId { get; set; }
    
    #region Guest Checkout Info
    
    public string? GuestEmail { get; set; }
    public string? GuestPhone { get; set; }
    public string? GuestName { get; set; }
    public string? IpAddress { get; set; }
    
    /// <summary>
    /// AF-13B: True when the order was placed without customer login.
    /// Affiliate guest checkout sets this to true.
    /// Historical orders default to false.
    /// </summary>
    public bool IsGuestCheckout { get; set; }
    
    #endregion
    
    #region Affiliate Attribution (AF-11)
    
    /// <summary>
    /// AF-11: The affiliate account that referred this order.
    /// Stamped at checkout from HttpContext (AffiliateModeResolver).
    /// NULL for non-affiliate orders.
    /// 
    /// CRITICAL: This is the SERVER-AUTHORITATIVE affiliate link.
    /// Never trust client payloads — only AffiliateModeResolver sets this.
    /// </summary>
    public Guid? AffiliateAccountId { get; set; }
    
    /// <summary>
    /// AF-11: Snapshot of the referral code used at checkout time.
    /// Immutable after order creation (code may be reassigned later).
    /// </summary>
    public string? AffiliateReferralCode { get; set; }
    
    /// <summary>
    /// AF-15: The promotion link that generated this order's affiliate attribution.
    /// NULL for legacy orders or orders using v1 (AffiliateAccountId-only) flow.
    /// When present, this is the authoritative source for affiliate identity.
    /// </summary>
    public Guid? PromotionLinkId { get; set; }
    
    #endregion
    
    #region Order Status
    
    public OrderStatus Status { get; set; } = OrderStatus.Pending;

    /// <summary>
    /// Explicit checkout lifecycle state for this order.
    /// New orders start in PendingPaymentIntent and only advance through validated transitions.
    /// </summary>
    public CheckoutState CheckoutState { get; private set; } = CheckoutState.PendingPaymentIntent;
    
    // ═══════════════════════════════════════════════════════════════════════════
    // PHASE COD-LIFECYCLE: Payment Status Encapsulation
    // 
    // PaymentStatus can ONLY be changed via IPaymentLifecycleService.
    // The backing field allows SaveChanges interceptor to detect unauthorized changes.
    // ═══════════════════════════════════════════════════════════════════════════
    
    private PaymentStatus _paymentStatus = PaymentStatus.Pending;
    
    /// <summary>
    /// Current payment status. PROTECTED - should only be modified by IPaymentLifecycleService.
    /// Direct assignment is blocked at runtime by PaymentStatusSaveChangesInterceptor.
    /// </summary>
    public PaymentStatus PaymentStatus 
    { 
        get => _paymentStatus;
        set
        {
            if (_paymentStatus != value)
            {
                _originalPaymentStatus ??= _paymentStatus;
                _paymentStatus = value;
            }
        }
    }
    
    /// <summary>
    /// Original PaymentStatus before any changes in current unit of work.
    /// Used by SaveChanges interceptor to detect unauthorized direct writes.
    /// </summary>
    private PaymentStatus? _originalPaymentStatus;
    
    /// <summary>
    /// Whether PaymentStatus was changed in current unit of work.
    /// Used by SaveChanges interceptor.
    /// </summary>
    public bool PaymentStatusChanged => _originalPaymentStatus.HasValue && _originalPaymentStatus != _paymentStatus;
    
    /// <summary>
    /// Clears the PaymentStatus change tracking. Called by authorized services only.
    /// </summary>
    public void AcceptPaymentStatusChange() => _originalPaymentStatus = null;
    
    /// <summary>
    /// Whether this order has been financially recognized in the ledger.
    /// True when money has been received (Collected), settled to bank (Settled), or refunded.
    /// 
    /// PHASE COD-LIFECYCLE: Useful for reports and filters to identify orders with financial impact.
    /// </summary>
    public bool IsFinanciallyRecognized => 
        PaymentStatus == PaymentStatus.Collected ||
        PaymentStatus == PaymentStatus.Settled ||
        PaymentStatus == PaymentStatus.Refunded;

    /// <summary>
    /// Monotonic source version used by downstream read models to reject stale order projections.
    /// </summary>
    public long ProjectionMutationVersion { get; private set; }
    
    /// <summary>
    /// Advances the projection mutation version. Used by the persistence pipeline only.
    /// Enforces monotonicity - new version must be greater than current version.
    /// </summary>
    public void AdvanceProjectionMutationVersion(long newVersion)
    {
        if (newVersion <= ProjectionMutationVersion)
        {
            throw new InvalidOperationException(
                $"Cannot regress ProjectionMutationVersion from {ProjectionMutationVersion} to {newVersion}. " +
                "Projection mutation versions must be monotonically increasing.");
        }
        ProjectionMutationVersion = newVersion;
    }
    
    // ═══════════════════════════════════════════════════════════════════════════
    // PHASE OFL: Fulfillment Status Encapsulation
    // 
    // FulfillmentStatus can ONLY be changed via IOrderFulfillmentService.
    // The backing field allows SaveChanges interceptor to detect unauthorized changes.
    // ═══════════════════════════════════════════════════════════════════════════
    
    private FulfillmentStatus _fulfillmentStatus = FulfillmentStatus.Created;
    
    /// <summary>
    /// Current fulfillment status. PROTECTED - should only be modified by IOrderFulfillmentService.
    /// Direct assignment is blocked at runtime by FulfillmentStatusSaveChangesInterceptor.
    /// </summary>
    public FulfillmentStatus FulfillmentStatus 
    { 
        get => _fulfillmentStatus;
        set
        {
            if (_fulfillmentStatus != value)
            {
                _originalFulfillmentStatus ??= _fulfillmentStatus;
                _fulfillmentStatus = value;
            }
        }
    }
    
    /// <summary>
    /// Original FulfillmentStatus before any changes in current unit of work.
    /// Used by SaveChanges interceptor to detect unauthorized direct writes.
    /// </summary>
    private FulfillmentStatus? _originalFulfillmentStatus;
    
    /// <summary>
    /// Whether FulfillmentStatus was changed in current unit of work.
    /// Used by SaveChanges interceptor.
    /// </summary>
    public bool FulfillmentStatusChanged => _originalFulfillmentStatus.HasValue && _originalFulfillmentStatus != _fulfillmentStatus;
    
    /// <summary>
    /// Clears the FulfillmentStatus change tracking. Called by authorized services only.
    /// </summary>
    public void AcceptFulfillmentStatusChange() => _originalFulfillmentStatus = null;
    
    /// <summary>
    /// Whether this order has been physically delivered.
    /// True when FulfillmentStatus is Delivered, Completed, or Returned.
    /// 
    /// PHASE OFL: Useful for reports and filters.
    /// </summary>
    public bool IsPhysicallyDelivered => 
        FulfillmentStatus == FulfillmentStatus.Delivered ||
        FulfillmentStatus == FulfillmentStatus.Completed ||
        FulfillmentStatus == FulfillmentStatus.Returned;
    
    // ═══════════════════════════════════════════════════════════════════════════
    // FULFILLMENT OWNERSHIP ARCHITECTURE
    // 
    // FulfillmentMode determines WHO controls the fulfillment lifecycle.
    // This is snapshotted at Order creation and is IMMUTABLE.
    // 
    // CRITICAL INVARIANTS:
    // 1. FulfillmentMode is decided at checkout based on ShippingOption.ProviderType
    // 2. FulfillmentMode NEVER changes after order creation (enforced by DB trigger)
    // 3. FulfillmentMode NEVER inferred from ShippingOption after creation
    // 4. UI/API must rely ONLY on Order.FulfillmentMode (never ShippingOption)
    // 
    // ARCHITECTURAL RULES:
    // - Manual orders: Admin controls all fulfillment transitions via IOrderFulfillmentService
    // - Carrier orders: ONLY carrier webhooks can update fulfillment status
    // - Manual orders CANNOT accept carrier webhook updates
    // - Carrier orders CANNOT accept manual admin transitions
    // ═══════════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Fulfillment ownership mode - determines who controls fulfillment lifecycle.
    /// IMMUTABLE after order creation (enforced by DB trigger).
    /// 
    /// Values:
    /// - Manual (0): Admin-controlled via IOrderFulfillmentService
    /// - Carrier (1): Carrier webhook-controlled
    /// 
    /// CRITICAL: This field is the SINGLE SOURCE OF TRUTH for fulfillment ownership.
    /// Never infer from ShippingMethod or any other field.
    /// </summary>
    public FulfillmentMode FulfillmentMode { get; set; } = FulfillmentMode.Manual;
    
    /// <summary>
    /// Carrier code for carrier-managed orders (e.g., "bosta", "aramex", "dhl").
    /// NULL for manual fulfillment orders.
    /// IMMUTABLE after order creation (enforced by DB trigger).
    /// 
    /// INVARIANT:
    /// - FulfillmentMode = Manual → FulfillmentCarrierCode MUST be NULL
    /// - FulfillmentMode = Carrier → FulfillmentCarrierCode MUST NOT be NULL
    /// </summary>
    public string? FulfillmentCarrierCode { get; set; }
    
    /// <summary>
    /// Computed property: true if this order allows manual admin fulfillment transitions.
    /// Use this to determine if IOrderFulfillmentService calls are allowed.
    /// </summary>
    public bool AllowsManualFulfillment => FulfillmentMode == FulfillmentMode.Manual;
    
    /// <summary>
    /// Computed property: true if this order accepts carrier webhook updates.
    /// Use this to determine if carrier webhook handlers should process updates.
    /// </summary>
    public bool AllowsCarrierWebhooks => FulfillmentMode == FulfillmentMode.Carrier;
    
    #endregion
    
    #region Shipping Address
    
    public string ShippingAddress { get; set; } = string.Empty;
    public string? ShippingCity { get; set; }
    public string? ShippingState { get; set; }
    public string? ShippingZipCode { get; set; }
    public string? ShippingCountry { get; set; }

    public Guid? ShippingAddressAdmissionEvidenceId { get; set; }
    public Guid? ShippingGeographicCatalogReleaseId { get; set; }
    public Guid? ShippingCountryPlaceId { get; set; }
    public Guid? ShippingAdministrativeAreaPlaceId { get; set; }
    public Guid? ShippingLocalityPlaceId { get; set; }
    public Guid? ShippingDistrictPlaceId { get; set; }
    public Guid? ShippingZonePlaceId { get; set; }
    public string? ShippingCountryDisplayName { get; set; }
    public string? ShippingAdministrativeAreaDisplayName { get; set; }
    public string? ShippingLocalityDisplayName { get; set; }
    public string? ShippingDistrictDisplayName { get; set; }
    public string? ShippingZoneDisplayName { get; set; }
    public string? ShippingStreet { get; set; }
    public string? ShippingStreet2 { get; set; }
    public string? ShippingBuilding { get; set; }
    public string? ShippingFloor { get; set; }
    public string? ShippingApartment { get; set; }
    public string? ShippingLandmark { get; set; }
    public DateTime? ShippingGeographicSnapshotCreatedAtUtc { get; set; }
    public int? ShippingGeographicSnapshotVersion { get; set; }
    
    #endregion
    
    #region Billing Address
    
    public string? BillingAddress { get; set; }
    public string? BillingCity { get; set; }
    public string? BillingState { get; set; }
    public string? BillingZipCode { get; set; }
    public string? BillingCountry { get; set; }

    public Guid? BillingAddressAdmissionEvidenceId { get; set; }
    public Guid? BillingGeographicCatalogReleaseId { get; set; }
    public Guid? BillingCountryPlaceId { get; set; }
    public Guid? BillingAdministrativeAreaPlaceId { get; set; }
    public Guid? BillingLocalityPlaceId { get; set; }
    public Guid? BillingDistrictPlaceId { get; set; }
    public Guid? BillingZonePlaceId { get; set; }
    public string? BillingCountryDisplayName { get; set; }
    public string? BillingAdministrativeAreaDisplayName { get; set; }
    public string? BillingLocalityDisplayName { get; set; }
    public string? BillingDistrictDisplayName { get; set; }
    public string? BillingZoneDisplayName { get; set; }
    public string? BillingStreet { get; set; }
    public string? BillingStreet2 { get; set; }
    public string? BillingBuilding { get; set; }
    public string? BillingFloor { get; set; }
    public string? BillingApartment { get; set; }
    public string? BillingLandmark { get; set; }
    public DateTime? BillingGeographicSnapshotCreatedAtUtc { get; set; }
    public int? BillingGeographicSnapshotVersion { get; set; }
    
    #endregion
    
    #region Shipping Details
    
    public Guid? ShippingMethodId { get; set; }
    public ShippingMethod? ShippingMethod { get; set; }
    
    /// <summary>
    /// Shipping method name (snapshot at order creation).
    /// This is immutable after order creation - does not change if POS updates the method.
    /// </summary>
    public string? ShippingMethodName { get; set; }

    /// <summary>
    /// Immutable carrier service-level snapshot purchased at checkout.
    /// Shipment execution must not reload this value from the mutable shipping method.
    /// </summary>
    public string? ShippingServiceLevel { get; set; }
    
    public decimal ShippingCost { get; set; }
    
    /// <summary>
    /// Estimated delivery days (snapshot at order creation).
    /// Formatted as "3-5 days" or "1 day".
    /// </summary>
    public string? EstimatedDeliveryDays { get; set; }
    
    public string? TrackingNumber { get; set; }
    public string? Carrier { get; set; }
    
    #endregion
    
    #region Financials
    
    /// <summary>
    /// Sum of all items before discount and tax
    /// </summary>
    public decimal Subtotal { get; set; }
    
    /// <summary>
    /// Aggregate promotion discount persisted separately from coupon discounts.
    /// Includes all automatic offer savings that were accepted by the pricing engine.
    /// </summary>
    public decimal PromotionDiscountAmount { get; set; }
    
    /// <summary>
    /// Coupon discount amount.
    /// This legacy field remains mapped for backwards compatibility and is the source of truth for coupon savings.
    /// </summary>
    public decimal DiscountAmount { get; set; }

    /// <summary>
    /// Manual or operator-entered discount persisted separately from coupon/promotion discounts.
    /// </summary>
    public decimal ManualDiscountAmount { get; set; }

    /// <summary>
    /// Auditable total discount persisted exactly as accepted by checkout pricing.
    /// </summary>
    public decimal TotalDiscountAmount { get; set; }
    
    /// <summary>
    /// Total tax amount
    /// </summary>
    public decimal TaxAmount { get; set; }
    
    /// <summary>
    /// Final order total (Subtotal - Discount + Tax + Shipping)
    /// </summary>
    public decimal Total { get; set; }
    
    /// <summary>
    /// Currency code (e.g., USD, EUR, SAR)
    /// </summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>
    /// Serialized pricing snapshot captured at checkout commit for audit reconstruction.
    /// </summary>
    public string? PricingSnapshotJson { get; set; }

    [NotMapped]
    public decimal CouponDiscountAmount
    {
        get => DiscountAmount;
        set => DiscountAmount = value;
    }

    [NotMapped]
    public DiscountBreakdown DiscountBreakdown => new(
        CouponDiscount: DiscountAmount,
        PromotionDiscount: PromotionDiscountAmount,
        ManualDiscount: ManualDiscountAmount);
    
    #endregion
    
    #region Coupon
    
    public string? CouponCode { get; set; }
    public Guid? CouponId { get; set; }
    public Coupon? Coupon { get; set; }
    // Removed CouponReservation navigation property to support multiple reservations per order
    // (only one active at a time, enforced by filtered unique index on TenantId, OrderId)
    // Use CouponReservations DbSet with filtering to get the active reservation
    
    #endregion
    
    #region Promotion (Offers - PHASE: Promotions Engine)
    
    /// <summary>
    /// ID of the auto-applied promotion (offer) if any.
    /// Null if only a coupon was applied or no promotion.
    /// </summary>
    public Guid? AppliedPromotionId { get; set; }
    public Promotion? AppliedPromotion { get; set; }
    
    /// <summary>
    /// Type of promotion applied: "offer" or "coupon"
    /// Used to distinguish source of discount.
    /// </summary>
    public string? AppliedPromotionType { get; set; }
    
    /// <summary>
    /// Name of the applied promotion (snapshot at order creation)
    /// Stored for historical reference even if promotion is deleted.
    /// </summary>
    public string? AppliedPromotionName { get; set; }
    
    /// <summary>
    /// Description of the discount for order display (e.g., "Summer Sale: 20% off")
    /// </summary>
    public string? AppliedPromotionDescription { get; set; }
    
    /// <summary>
    /// S19C: Indicates whether promotion calculation failed during checkout.
    /// When true, the order was created with fallback pricing (no promotion discounts).
    /// Used to restore the promotion warning banner on duplicate order recovery.
    /// </summary>
    public bool PromotionCalculationFailed { get; set; }
    
    /// <summary>
    /// S19C: Warning message to display when promotion calculation failed.
    /// Preserved for duplicate order recovery to show consistent warnings.
    /// </summary>
    public string? PromotionWarningMessage { get; set; }
    
    #endregion
    
    #region Payment
    
    /// <summary>
    /// Legacy payment method enum (for backwards compatibility with POS).
    /// </summary>
    public PaymentMethod PaymentMethod { get; set; }
    
    /// <summary>
    /// Payment method code from TenantPaymentMethod (snapshot at order creation).
    /// Examples: "COD", "STRIPE", "PAYMOB"
    /// IMMUTABLE after order creation - does not change if POS updates the method.
    /// </summary>
    public string PaymentMethodCode { get; set; } = string.Empty;
    
    /// <summary>
    /// Display name of payment method (snapshot at order creation).
    /// Examples: "Cash on Delivery", "Credit/Debit Card"
    /// IMMUTABLE after order creation.
    /// </summary>
    public string? PaymentMethodName { get; set; }
    
    /// <summary>
    /// Payment method fee (snapshot at order creation).
    /// Already included in order Total.
    /// </summary>
    public decimal PaymentFee { get; set; }
    
    /// <summary>
    /// Whether the order has been paid (legacy field - kept for backward compatibility).
    /// 
    /// PHASE 11.4.2.A: Use IsFullyPaidComputed for accurate status derived from OrderItems.
    /// This field may be out of sync with OrderItem-level payments.
    /// 
    /// COD orders: false until manually marked as paid
    /// Online payments: true after successful payment capture
    /// </summary>
    public bool IsPaid { get; set; }
    
    public string? PaymentReference { get; set; }
    public string? PaymentGateway { get; set; }
    public DateTime? PaidAt { get; set; }

    /// <summary>
    /// Durable accounting lifecycle for collected storefront money.
    /// Payment status can move to Collected before the async accounting outbox posts,
    /// but settlement and commission flows must treat this field as the ledger truth gate.
    /// </summary>
    public FinancialPostingStatus FinancialPostingStatus { get; set; } = FinancialPostingStatus.NotRequired;
    public DateTime? FinancialPostingPendingAtUtc { get; set; }
    public DateTime? FinancialPostedAtUtc { get; set; }
    public Guid? FinancialPostingJournalId { get; set; }
    public string? FinancialPostingFailureReason { get; set; }
    
    #endregion
    
    // =============================================================================
    // PHASE 11.4.2.A — DERIVED PAYMENT STATUS FROM ORDER ITEMS
    // =============================================================================
    // MONEY IS TRACKED ON ORDER ITEMS (VARIANTS), NOT ON ORDERS
    // These computed properties derive order-level payment status from OrderItems.
    // =============================================================================
    
    #region Derived Payment Status (Phase 11.4.2.A)
    
    /// <summary>
    /// Total amount paid across all order items.
    /// PHASE 11.4.2.A: Sum of all OrderItem.PaidAmount.
    /// </summary>
    public decimal TotalPaidAmount => Items?.Sum(i => i.PaidAmount) ?? 0;
    
    /// <summary>
    /// Total amount refunded across all order items.
    /// PHASE 11.4.2.A: Sum of all OrderItem.RefundedAmount.
    /// </summary>
    public decimal TotalRefundedAmount => Items?.Sum(i => i.RefundedAmount) ?? 0;
    
    /// <summary>
    /// Net amount paid (after refunds).
    /// PHASE 11.4.2.A: TotalPaidAmount - TotalRefundedAmount.
    /// </summary>
    public decimal NetPaidAmount => TotalPaidAmount - TotalRefundedAmount;
    
    /// <summary>
    /// Total amount still owed on this order.
    /// PHASE 11.4.2.A: Total - TotalPaidAmount.
    /// </summary>
    public decimal BalanceDue => Math.Max(0, Total - TotalPaidAmount);
    
    /// <summary>
    /// Whether all order items are fully paid (derived from OrderItems).
    /// PHASE 11.4.2.A: True when ALL OrderItems have PaidAmount >= Subtotal.
    /// </summary>
    public bool IsFullyPaidComputed => Items?.All(i => i.IsFullyPaid) ?? false;
    
    /// <summary>
    /// Whether any payment has been made but order is not fully paid.
    /// PHASE 11.4.2.A: True when SOME OrderItems have PaidAmount > 0 but not all are fully paid.
    /// </summary>
    public bool IsPartiallyPaid => Items?.Any(i => i.PaidAmount > 0) == true && !IsFullyPaidComputed;
    
    /// <summary>
    /// Whether all paid amounts have been refunded.
    /// PHASE 11.4.2.A: True when ALL OrderItems have RefundedAmount >= PaidAmount.
    /// </summary>
    public bool IsFullyRefundedComputed => Items?.All(i => i.IsFullyRefunded) ?? false;
    
    /// <summary>
    /// Whether some (but not all) paid amounts have been refunded.
    /// PHASE 11.4.2.A: True when SOME OrderItems have RefundedAmount > 0.
    /// </summary>
    public bool IsPartiallyRefunded => Items?.Any(i => i.RefundedAmount > 0) == true && !IsFullyRefundedComputed;
    
    #endregion
    
    #region Notes
    
    /// <summary>
    /// Notes from the customer
    /// </summary>
    public string? CustomerNotes { get; set; }
    
    /// <summary>
    /// Internal admin notes
    /// </summary>
    public string? AdminNotes { get; set; }
    
    #endregion
    
    #region Order Source
    
    /// <summary>
    /// Source channel of the order
    /// </summary>
    public OrderSource Source { get; set; } = OrderSource.Ecommerce;
    
    /// <summary>
    /// External order ID from the source (e.g., Amazon Order ID)
    /// </summary>
    public string? SourceOrderId { get; set; }
    
    #endregion
    
    #region Timestamps
    
    public DateTime? ShippedAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancellationReason { get; set; }
    
    #endregion
    
    #region Warehouse
    
    /// <summary>
    /// Default warehouse for order fulfillment
    /// </summary>
    public Guid? WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }
    
    #endregion

    public void TransitionCheckoutState(CheckoutState targetState)
    {
        if (CheckoutState == targetState)
        {
            return;
        }

        if (!IsValidCheckoutTransition(CheckoutState, targetState))
        {
            throw new InvalidOperationException(
                $"Invalid checkout state transition for order {Id}: {CheckoutState} -> {targetState}");
        }

        CheckoutState = targetState;
    }

    private static bool IsValidCheckoutTransition(CheckoutState current, CheckoutState next)
    {
        return current switch
        {
            CheckoutState.Draft => next == CheckoutState.PendingPaymentIntent,
            CheckoutState.PendingPaymentIntent => next == CheckoutState.PaymentPending
                || next == CheckoutState.Failed
                || next == CheckoutState.Cancelled
                || next == CheckoutState.Expired,
            CheckoutState.PaymentPending => next == CheckoutState.Paid
                || next == CheckoutState.Failed
                || next == CheckoutState.Cancelled
                || next == CheckoutState.Expired,
            CheckoutState.Failed => next == CheckoutState.PendingPaymentIntent
                || next == CheckoutState.Cancelled,
            CheckoutState.Cancelled => false,
            CheckoutState.Expired => next == CheckoutState.PendingPaymentIntent,
            CheckoutState.Paid => false,
            _ => false
        };
    }
    
    // Navigation
    public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
    public ICollection<OrderCheckoutFieldSnapshot> CheckoutFieldSnapshots { get; set; } = new List<OrderCheckoutFieldSnapshot>();
    public ICollection<OrderRefund> Refunds { get; set; } = new List<OrderRefund>();
}
