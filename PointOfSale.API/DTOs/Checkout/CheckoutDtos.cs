using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using PointOfSale.API.DTOs;
using PointOfSale.Core.Helpers;

namespace PointOfSale.API.DTOs.Checkout;

/// <summary>
/// Checkout request DTO.
/// Converts a validated cart into a pending order.
/// 
/// Required Headers:
/// - X-Tenant-Id: GUID (required)
/// - X-Session-Id: string (for guest checkout)
/// - X-Customer-Id: GUID (optional, for registered customers)
/// - Idempotency-Key: optional compatibility header only
/// - X-Request-Id: optional compatibility header; must match Idempotency-Key when both are provided
/// </summary>
public class CheckoutRequestDto
{
    private static readonly Regex ManualWalletPhoneRegex = new(@"^(?=(?:.*\d){8,20}$)[\d\s\-+()]+$", RegexOptions.Compiled);

    /// <summary>
    /// Cart ID to checkout. Must exist and belong to the tenant/owner.
    /// </summary>
    public Guid CartId { get; set; }

    /// <summary>
    /// Server-owned checkout session that scoped warehouse resolution.
    /// </summary>
    public Guid? CheckoutSessionId { get; set; }

    /// <summary>
    /// Server-owned warehouse resolution returned by shipping-method preview.
    /// </summary>
    public Guid? WarehouseResolutionId { get; set; }

    /// <summary>
    /// Warehouse resolution version returned by shipping-method preview.
    /// </summary>
    public int? WarehouseResolutionVersion { get; set; }

    /// <summary>
    /// Selected shipping method ID.
    /// </summary>
    public Guid ShippingMethodId { get; set; }

    /// <summary>
    /// Shipping address.
    /// </summary>
    [Required]
    public AddressDto ShippingAddress { get; set; } = new();

    /// <summary>
    /// Billing address. If null, same as shipping.
    /// </summary>
    public AddressDto? BillingAddress { get; set; }

    /// <summary>
    /// Payment method identifier (e.g., "card", "cash_on_delivery", "bank_transfer").
    /// Payment is NOT executed - only intent is captured.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    [StringLength(64)]
    public string PaymentMethod { get; set; } = "card";

    /// <summary>
    /// Optional coupon code to apply.
    /// </summary>
    [StringLength(64)]
    public string? CouponCode { get; set; }

    /// <summary>
    /// Manual Wallet: Wallet Type (VodafoneCash, Instapay, OrangeCash)
    /// </summary>
    [StringLength(32)]
    public string? WalletType { get; set; }

    /// <summary>
    /// Manual Wallet: Customer Phone Number
    /// </summary>
    [StringLength(32)]
    public string? WalletPhoneNumber { get; set; }

    /// <summary>
    /// Manual Wallet: Server-issued proof file key
    /// </summary>
    [StringLength(512)]
    public string? ProofFileKey { get; set; }

    /// <summary>
    /// Customer notes for the order.
    /// </summary>
    [StringLength(1000)]
    public string? CustomerNotes { get; set; }

    /// <summary>
    /// Authoritative pricing preview echoed back by the client at submission time.
    /// Checkout is rejected when these totals do not match the latest backend calculation.
    /// </summary>
    [Required]
    public CheckoutExpectedPricingDto ExpectedPricing { get; set; } = null!;

    /// <summary>
    /// AF-18: Affiliate referral code for attribution.
    /// Frontend should capture ?ref= from URL and include it in checkout request.
    /// This ensures affiliate attribution works even when checkout happens in a separate API call.
    /// </summary>
    [StringLength(64)]
    public string? AffiliateReferralCode { get; set; }

    /// <summary>
    /// Merchant-defined checkout field values. The server resolves field definitions from tenant context.
    /// Values are validated authoritatively and persisted as immutable order snapshots.
    /// </summary>
    public List<CheckoutCustomFieldValueDto>? CustomFields { get; set; }

    /// <summary>
    /// Validates the checkout request.
    /// </summary>
    public (string? ErrorCode, string? ErrorMessage) Validate()
    {
        if (CartId == Guid.Empty)
        {
            return ("INVALID_CART_ID", "Cart ID is required");
        }

        if (!CheckoutSessionId.HasValue || CheckoutSessionId.Value == Guid.Empty)
        {
            return ("CHECKOUT_SESSION_REQUIRED", "Checkout session is required.");
        }

        if (!WarehouseResolutionId.HasValue || WarehouseResolutionId.Value == Guid.Empty)
        {
            return ("WAREHOUSE_RESOLUTION_REQUIRED", "Shipping methods must be refreshed before checkout.");
        }

        if (!WarehouseResolutionVersion.HasValue || WarehouseResolutionVersion.Value <= 0)
        {
            return ("WAREHOUSE_RESOLUTION_VERSION_REQUIRED", "Shipping methods must be refreshed before checkout.");
        }

        if (ShippingMethodId == Guid.Empty)
        {
            return ("INVALID_SHIPPING_METHOD", "Shipping method is required");
        }

        if (ShippingAddress == null)
        {
            return ("INVALID_SHIPPING_ADDRESS", "Shipping address is required");
        }

        var shippingValidation = ShippingAddress.Validate();
        if (shippingValidation.ErrorCode != null)
        {
            return shippingValidation;
        }

        if (BillingAddress != null)
        {
            var billingValidation = BillingAddress.Validate();
            if (billingValidation.ErrorCode != null)
            {
                return billingValidation;
            }
        }

        var shippingGeographyValidation = ShippingAddress.Geography?.Validate() ?? (null, null);
        if (shippingGeographyValidation.Item1 != null)
        {
            return shippingGeographyValidation;
        }

        var billingGeographyValidation = BillingAddress?.Geography?.Validate() ?? (null, null);
        if (billingGeographyValidation.Item1 != null)
        {
            return billingGeographyValidation;
        }

        if (string.IsNullOrWhiteSpace(PaymentMethod))
        {
            return ("INVALID_PAYMENT_METHOD", "Payment method is required");
        }

        if (IsManualWalletPaymentMethod(PaymentMethod))
        {
            if (string.IsNullOrWhiteSpace(WalletType))
            {
                return ("MANUAL_WALLET_TYPE_REQUIRED", "Wallet type is required for manual wallet payments.");
            }

            if (!IsSupportedManualWalletType(WalletType))
            {
                return ("MANUAL_WALLET_TYPE_INVALID", "Unsupported wallet type for manual wallet payments.");
            }

            if (string.IsNullOrWhiteSpace(WalletPhoneNumber))
            {
                return ("MANUAL_WALLET_PHONE_REQUIRED", "Wallet phone number is required for manual wallet payments.");
            }

            if (!ManualWalletPhoneRegex.IsMatch(WalletPhoneNumber.Trim()))
            {
                return ("INVALID_MANUAL_WALLET_PHONE", "Wallet phone number format is invalid.");
            }

            if (string.IsNullOrWhiteSpace(ProofFileKey))
            {
                return ("MANUAL_WALLET_PROOF_REQUIRED", "Proof of payment is required for manual wallet payments.");
            }

            // Normalize manual wallet fields for downstream processing
            WalletType = WalletType.Trim().ToUpperInvariant();
            WalletPhoneNumber = WalletPhoneNumber.Trim();
            ProofFileKey = ProofFileKey.Trim();
        }

        if (ExpectedPricing == null)
        {
            return ("EXPECTED_PRICING_REQUIRED", "Checkout pricing must be refreshed before placing the order.");
        }

        var expectedPricingValidation = ExpectedPricing.Validate();
        if (expectedPricingValidation.ErrorCode != null)
        {
            return expectedPricingValidation;
        }

        return (null, null);
    }

    private static bool IsManualWalletPaymentMethod(string paymentMethod) =>
        string.Equals(paymentMethod?.Trim(), "MANUALWALLET", StringComparison.OrdinalIgnoreCase);

    private static bool IsSupportedManualWalletType(string walletType) =>
        walletType.Trim().ToUpperInvariant() is "VODAFONECASH" or "INSTAPAY" or "ORANGECASH";
}

public class CheckoutPricingPreviewRequestDto
{
    public Guid CartId { get; set; }

    public Guid? ShippingMethodId { get; set; }

    public string? PaymentMethod { get; set; }

    [StringLength(2, MinimumLength = 2)]
    public string? ShippingCountry { get; set; }

    public GeographicAddressCandidateDto? ShippingGeography { get; set; }

    public (string? ErrorCode, string? ErrorMessage) Validate()
    {
        if (CartId == Guid.Empty)
        {
            return ("INVALID_CART_ID", "Cart ID is required");
        }

        if (!string.IsNullOrWhiteSpace(ShippingCountry))
        {
            if (!CountryCodeHelper.TryNormalizeCountryCode(ShippingCountry, out var normalizedCountry))
            {
                return ("INVALID_ADDRESS", "Country must be a valid ISO 3166-1 alpha-2 code");
            }

            ShippingCountry = normalizedCountry;
        }

        var geographyValidation = ShippingGeography?.Validate() ?? (null, null);
        if (geographyValidation.Item1 != null)
        {
            return geographyValidation;
        }

        return (null, null);
    }
}

/// <summary>
/// Address DTO for checkout.
/// </summary>
public class AddressDto
{
    /// <summary>
    /// Recipient name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Phone number.
    /// </summary>
    public string Phone { get; set; } = string.Empty;

    /// <summary>
    /// Email address.
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// Street address line 1.
    /// </summary>
    public string Street { get; set; } = string.Empty;

    /// <summary>
    /// Street address line 2 (optional).
    /// </summary>
    [StringLength(200)]
    public string? Street2 { get; set; }

    /// <summary>
    /// City.
    /// </summary>
    public string City { get; set; } = string.Empty;

    /// <summary>
    /// State/Province.
    /// </summary>
    [StringLength(120)]
    public string? State { get; set; }

    /// <summary>
    /// Postal/ZIP code.
    /// </summary>
    [StringLength(32)]
    public string? ZipCode { get; set; }

    /// <summary>
    /// Country code (ISO 3166-1 alpha-2).
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    [StringLength(2, MinimumLength = 2)]
    public string Country { get; set; } = string.Empty;

    /// <summary>
    /// Canonical Geographic candidate selected by the storefront. Backend admission is the authority.
    /// </summary>
    public GeographicAddressCandidateDto? Geography { get; set; }

    [StringLength(120)]
    public string? Building { get; set; }

    [StringLength(64)]
    public string? Floor { get; set; }

    [StringLength(64)]
    public string? Apartment { get; set; }

    [StringLength(200)]
    public string? Landmark { get; set; }

    /// <summary>
    /// Validates the address.
    /// Note: This method mutates the Country property by normalizing it to ISO 3166-1 alpha-2 uppercase format on success.
    /// </summary>
    public (string? ErrorCode, string? ErrorMessage) Validate()
    {
        if (string.IsNullOrWhiteSpace(Country))
        {
            return ("INVALID_ADDRESS", "Country is required");
        }

        if (!CountryCodeHelper.TryNormalizeCountryCode(Country, out var normalizedCountry))
        {
            return ("INVALID_ADDRESS", "Country must be a valid ISO 3166-1 alpha-2 code");
        }

        Country = normalizedCountry;

        return (null, null);
    }
}

public class GeographicAddressCandidateDto
{
    public Guid? CountryPlaceId { get; set; }

    public Guid? AdministrativeAreaPlaceId { get; set; }

    public Guid? LocalityPlaceId { get; set; }

    public Guid? DistrictPlaceId { get; set; }

    public Guid? ZonePlaceId { get; set; }


    public bool HasAnyPlaceCandidate =>
        CountryPlaceId.HasValue ||
        AdministrativeAreaPlaceId.HasValue ||
        LocalityPlaceId.HasValue ||
        DistrictPlaceId.HasValue ||
        ZonePlaceId.HasValue;

    public (string? ErrorCode, string? ErrorMessage) Validate()
    {
        if (!HasAnyPlaceCandidate)
        {
            return (null, null);
        }

        if (!CountryPlaceId.HasValue || CountryPlaceId.Value == Guid.Empty)
        {
            return ("INVALID_GEOGRAPHIC_CANDIDATE", "Canonical country place is required when geographic identity is submitted.");
        }

        if (AdministrativeAreaPlaceId == Guid.Empty ||
            LocalityPlaceId == Guid.Empty ||
            DistrictPlaceId == Guid.Empty ||
            ZonePlaceId == Guid.Empty)
        {
            return ("INVALID_GEOGRAPHIC_CANDIDATE", "Canonical place identifiers must be non-empty GUIDs.");
        }

        return (null, null);
    }
}

public class CheckoutCustomFieldValueDto
{
    public Guid FieldId { get; set; }

    [StringLength(5000)]
    public string? Value { get; set; }

    public List<Guid>? AttachmentIds { get; set; }
}

public sealed class CheckoutAttachmentUploadLeaseRequestDto
{
    public Guid CartId { get; set; }
    public Guid FieldId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
}

public sealed class CheckoutAttachmentUploadLeaseResponseDto
{
    public Guid AttachmentId { get; set; }
    public string UploadUrl { get; set; } = string.Empty;
    public string UploadMethod { get; set; } = "PUT";
    public string ContentType { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime AttachmentExpiresAtUtc { get; set; }
}

public sealed class CheckoutAttachmentConfirmRequestDto
{
    public Guid CartId { get; set; }
    public Guid FieldId { get; set; }
    public Guid AttachmentId { get; set; }
}

public sealed class CheckoutAttachmentDto
{
    public Guid AttachmentId { get; set; }
    public Guid FieldId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime? UploadedAtUtc { get; set; }
}

public class CheckoutExpectedPricingDto
{
    [Range(typeof(decimal), "0", ValidationConstants.DecimalAmountMax)]
    public decimal Subtotal { get; set; }

    [Range(typeof(decimal), "0", ValidationConstants.DecimalAmountMax)]
    public decimal Shipping { get; set; }

    [Range(typeof(decimal), "0", ValidationConstants.DecimalAmountMax)]
    public decimal Tax { get; set; }

    [Range(typeof(decimal), "0", ValidationConstants.DecimalAmountMax)]
    public decimal Discount { get; set; }

    [Range(typeof(decimal), "0", ValidationConstants.DecimalAmountMax)]
    public decimal Total { get; set; }

    [Range(typeof(decimal), "0", ValidationConstants.DecimalAmountMax)]
    public decimal CouponDiscount { get; set; }

    [Range(typeof(decimal), "0", ValidationConstants.DecimalAmountMax)]
    public decimal PromotionDiscount { get; set; }

    [Range(typeof(decimal), "0", ValidationConstants.DecimalAmountMax)]
    public decimal CartPromotionDiscount { get; set; }

    [Range(typeof(decimal), "0", ValidationConstants.DecimalAmountMax)]
    public decimal PaymentFee { get; set; }

    [Range(typeof(decimal), "0", ValidationConstants.DecimalAmountMax)]
    public decimal ManualDiscount { get; set; }

    public decimal FinalTotal
    {
        get => Total;
        set => Total = value;
    }

    public (string? ErrorCode, string? ErrorMessage) Validate()
    {
        if (Subtotal < 0m)
        {
            return ("INVALID_EXPECTED_PRICING", "Expected subtotal cannot be negative.");
        }

        if (Shipping < 0m)
        {
            return ("INVALID_EXPECTED_PRICING", "Expected shipping cannot be negative.");
        }

        if (Tax < 0m)
        {
            return ("INVALID_EXPECTED_PRICING", "Expected tax cannot be negative.");
        }

        if (Discount < 0m ||
            CouponDiscount < 0m ||
            PromotionDiscount < 0m ||
            CartPromotionDiscount < 0m ||
            ManualDiscount < 0m)
        {
            return ("INVALID_EXPECTED_PRICING", "Expected discounts cannot be negative.");
        }

        if (PaymentFee < 0m)
        {
            return ("INVALID_EXPECTED_PRICING", "Expected payment fee cannot be negative.");
        }

        if (Total < 0m)
        {
            return ("INVALID_EXPECTED_PRICING", "Expected total cannot be negative.");
        }

        var legacyComponentDiscount = RoundCurrency(CouponDiscount + PromotionDiscount + CartPromotionDiscount + ManualDiscount);
        var deterministicComponentDiscount = RoundCurrency(CouponDiscount + PromotionDiscount + ManualDiscount);
        var normalizedDiscount = RoundCurrency(Discount);
        if (normalizedDiscount != legacyComponentDiscount &&
            normalizedDiscount != deterministicComponentDiscount)
        {
            return ("INVALID_EXPECTED_PRICING", "Expected pricing discount breakdown is inconsistent.");
        }

        var expectedTotal = RoundCurrency(Subtotal - Discount + Shipping + Tax + PaymentFee);
        if (RoundCurrency(Total) != expectedTotal)
        {
            return ("INVALID_EXPECTED_PRICING", "Expected pricing total is inconsistent.");
        }

        return (null, null);
    }

    private static decimal RoundCurrency(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);
}

/// <summary>
/// Checkout response DTO.
/// Returned after successful order creation.
/// </summary>
public class CheckoutResponseDto
{
    /// <summary>
    /// Created order ID.
    /// </summary>
    public Guid OrderId { get; set; }

    /// <summary>
    /// Order number (human-readable).
    /// </summary>
    public string OrderNumber { get; set; } = string.Empty;

    /// <summary>
    /// Order status (always "PendingPayment" after checkout).
    /// </summary>
    public string Status { get; set; } = "PendingPayment";

    /// <summary>
    /// Total amount to pay.
    /// </summary>
    public decimal TotalAmount { get; set; }

    /// <summary>
    /// Currency code.
    /// </summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>
    /// Payment reference for payment gateway.
    /// </summary>
    public string? PaymentReference { get; set; }

    /// <summary>
    /// Backend-owned payment intent identity for the accepted payment session.
    /// </summary>
    public Guid? PaymentIntentId { get; set; }

    /// <summary>
    /// Current backend-owned payment status for this checkout response.
    /// </summary>
    public string PaymentStatus { get; set; } = string.Empty;

    /// <summary>
    /// Canonical backend-owned payment method code used to create or replay the order.
    /// </summary>
    public string PaymentMethodCode { get; set; } = string.Empty;

    /// <summary>
    /// Redirect URL for payment (if applicable).
    /// </summary>
    public string? RedirectUrl { get; set; }

    /// <summary>
    /// Purpose-scoped checkout token for payment ownership validation.
    /// </summary>
    public string? CheckoutToken { get; set; }

    /// <summary>
    /// Purpose-scoped guest tracking token. This is never placed in URLs.
    /// </summary>
    public string? TrackingToken { get; set; }

    /// <summary>
    /// Cart ID that was checked out.
    /// </summary>
    public Guid CartId { get; set; }

    /// <summary>
    /// Legacy compatibility flag.
    /// S15-B fail-closed checkout blocks promotion calculation failures, so successful responses keep this false.
    /// </summary>
    public bool PromotionCalculationFailed { get; set; }

    /// <summary>
    /// [DEPRECATED] Customer-visible warning when checkout completes without discounts after a promotion calculation failure.
    /// Use PromotionWarningCode and PromotionWarningParams for localization support.
    /// </summary>
    [Obsolete("Use PromotionWarningCode and PromotionWarningParams for localization support")]
    public string? PromotionWarningMessage { get; set; }

    /// <summary>
    /// Localization key for promotion warning message (e.g., "PROMOTION_CALCULATION_FAILED").
    /// Frontend should use this to look up the appropriate translated message.
    /// </summary>
    public string? PromotionWarningCode { get; set; }

    /// <summary>
    /// Optional parameters for the promotion warning message (e.g., discount amounts, product names).
    /// Frontend can use these to interpolate values into the translated message.
    /// </summary>
    public Dictionary<string, string>? PromotionWarningParams { get; set; }

    /// <summary>
    /// Stripe Client Secret for Payment Element.
    /// </summary>
    public string? StripeClientSecret { get; set; }

    /// <summary>
    /// Stripe Publishable Key for initialization.
    /// </summary>
    public string? StripePublishableKey { get; set; }

    /// <summary>
    /// Paymob iFrame URL for payment page redirect.
    /// </summary>
    public string? PaymobIframeUrl { get; set; }

    /// <summary>
    /// Paymob Payment Key for embedding in iframe.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? PaymobPaymentKey { get; set; }

    /// <summary>
    /// Fawry Reference Code (for offline payments).
    /// </summary>
    public string? ReferenceCode { get; set; }

    /// <summary>
    /// When the payment reference/code expires.
    /// </summary>
    public DateTime? ExpiresAtUtc { get; set; }

    // =========================================================================
    // Manual Wallet Payment Fields
    // =========================================================================

    /// <summary>
    /// Indicates this is a manual wallet payment requiring proof upload.
    /// </summary>
    public bool IsManualWallet { get; set; }

    /// <summary>
    /// Merchant's wallet number to transfer to (e.g., 01012345678).
    /// </summary>
    public string? MerchantWalletNumber { get; set; }

    /// <summary>
    /// Wallet type selected (VodafoneCash, Instapay, OrangeCash).
    /// </summary>
    public string? WalletType { get; set; }

    /// <summary>
    /// Instructions for the customer (e.g., "Transfer to this number and upload proof").
    /// </summary>
    public string? WalletInstructions { get; set; }

    /// <summary>
    /// Indicates whether manual wallet configuration was successfully resolved.
    /// False means the merchant wallet details could not be retrieved.
    /// </summary>
    public bool? ManualWalletConfigured { get; set; }

    /// <summary>
    /// Error message when manual wallet configuration fails.
    /// </summary>
    public string? ManualWalletErrorMessage { get; set; }

    /// <summary>
    /// Breakdown of order totals.
    /// </summary>
    public OrderTotalsDto Totals { get; set; } = new();
}

/// <summary>
/// Order totals breakdown.
/// </summary>
public class OrderTotalsDto
{
    /// <summary>
    /// Subtotal before discounts, shipping, and tax.
    /// </summary>
    public decimal Subtotal { get; set; }

    /// <summary>
    /// Total discount applied.
    /// </summary>
    public decimal DiscountAmount { get; set; }

    public decimal Discount
    {
        get => DiscountAmount;
        set => DiscountAmount = value;
    }

    /// <summary>
    /// Shipping cost.
    /// </summary>
    public decimal ShippingCost { get; set; }

    public decimal Shipping
    {
        get => ShippingCost;
        set => ShippingCost = value;
    }

    /// <summary>
    /// Tax amount.
    /// </summary>
    public decimal TaxAmount { get; set; }

    public decimal Tax
    {
        get => TaxAmount;
        set => TaxAmount = value;
    }

    public decimal PaymentFee { get; set; }

    /// <summary>
    /// Grand total (Subtotal - Discount + Shipping + Tax).
    /// </summary>
    public decimal GrandTotal { get; set; }

    public decimal Total
    {
        get => GrandTotal;
        set => GrandTotal = value;
    }

    public decimal CouponDiscountAmount { get; set; }

    public decimal PromotionDiscountAmount { get; set; }

    public decimal ManualDiscountAmount { get; set; }

    public decimal TaxRate { get; set; }

    public string? TaxSource { get; set; }
}

public class CheckoutPricingPreviewResponseDto : CheckoutExpectedPricingDto
{
    public decimal TaxRate { get; set; }

    public string TaxSource { get; set; } = string.Empty;

    public string? TaxCountryCode { get; set; }

    public bool PricesRefreshed { get; set; }

    /// <summary>
    /// Indicates whether backend promotion calculation failed before pricing admission.
    /// Customer-visible checkout pricing must fail closed instead of accepting fabricated discounts.
    /// </summary>
    public bool PromotionCalculationFailed { get; set; }
}

/// <summary>
/// Internal cart snapshot taken at checkout start.
/// Used for price recalculation and order creation.
/// 
/// ARCHITECTURAL RULE: This snapshot captures ALL cart state.
/// - Coupon identity/code candidates are copied from backend cart state.
/// - Checkout revalidates coupon financial authority before pricing and again inside the final SQL order transaction.
/// - Order creation snapshots only the revalidated backend financial totals.
/// </summary>
public class CartSnapshot
{
    public Guid CartId { get; set; }
    public Guid TenantId { get; set; }
    public string OwnerType { get; set; } = string.Empty;
    public string OwnerId { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public List<CartItemSnapshot> Items { get; set; } = new();
    public DateTime SnapshotTakenAtUtc { get; set; } = DateTime.UtcNow;
    /// <remarks>
    /// Must be explicitly set from the cart record (cart.LastUpdatedUtc).
    /// No default — leaving this unset would produce a different hash on every retry,
    /// breaking the snapshot/idempotency contract.
    /// </remarks>
    public required DateTime CartUpdatedAtUtc { get; set; }
    
    // Coupon state from cart (READ-ONLY candidate; checkout revalidates before accepted pricing/order totals)
    /// <summary>
    /// Applied coupon ID from cart. NULL if no coupon.
    /// PHASE: Shopify-grade Coupon Engine - needed for checkout validation.
    /// </summary>
    public Guid? AppliedCouponId { get; set; }
    
    /// <summary>
    /// Applied coupon code from cart. NULL if no coupon.
    /// </summary>
    public string? AppliedCouponCode { get; set; }
    
    /// <summary>
    /// Coupon discount candidate from cart state. Checkout recalculates it through backend coupon validation
    /// before accepted pricing/order totals are emitted or persisted.
    /// </summary>
    public decimal DiscountTotal { get; set; }

    /// <summary>
    /// Explicit coupon discount total used by the deterministic pricing engine.
    /// Legacy callers may still populate DiscountTotal only; checkout normalizes both.
    /// </summary>
    public decimal CouponDiscountTotal { get; set; }
    
    /// <summary>
    /// PHASE: Promotions Engine - Item-level promotion discount total.
    /// Sum of all cart item LineDiscount values from product-level promotions.
    /// This is separate from DiscountTotal (which is coupon-based).
    /// </summary>
    public decimal PromotionDiscountTotal { get; set; }

    /// <summary>
    /// Cart-level promotion discount total from the cart service.
    /// This captures auto-applied cart promotions separately from item-level savings.
    /// </summary>
    public decimal CartPromotionDiscountTotal { get; set; }

    /// <summary>
    /// Manual discount total. Storefront checkout currently keeps this at zero, but the field
    /// exists so financial modeling stays explicit when future write paths add manual adjustments.
    /// </summary>
    public decimal ManualDiscountTotal { get; set; }
    
    // PHASE: Promotions Engine - promotion tracking
    /// <summary>
    /// Applied promotion (offer) ID from promotion calculation.
    /// NULL if no offer applied or coupon won.
    /// </summary>
    public Guid? AppliedPromotionId { get; set; }
    
    /// <summary>
    /// Type of promotion applied: "offer" or "coupon"
    /// </summary>
    public string? AppliedPromotionType { get; set; }
    
    /// <summary>
    /// Name of the applied promotion (for display)
    /// </summary>
    public string? AppliedPromotionName { get; set; }

    // ═══════════════════════════════════════════════════════════════════
    // AF-27A: ANTI-TAMPERING AFFILIATE ATTRIBUTION (from cart metadata)
    //
    // These are populated from the Redis cart at snapshot time.
    // CheckoutService reads these as PRIORITY 0 (trusted, server-stamped)
    // before falling back to HttpContext or request DTO.
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// AF-27A: Server-stamped affiliate account ID from cart metadata.
    /// Set by funnel endpoint, null if not funnel-originated.
    /// </summary>
    public Guid? AffiliateAccountId { get; set; }

    /// <summary>
    /// AF-27A: Server-stamped affiliate referral code from cart metadata.
    /// </summary>
    public string? AffiliateReferralCode { get; set; }

    /// <summary>
    /// AF-27A: Source of affiliate attribution ("Funnel" = trusted).
    /// </summary>
    public string? AffiliateSource { get; set; }
}

/// <summary>
/// Cart item snapshot for checkout processing.
/// 
/// PHASE 11.4.1 — VARIANT-BASED CHECKOUT
/// 
/// CRITICAL RULES:
/// - A Product is NEVER sold. A ProductVariant (SKU) is ALWAYS sold.
/// - VariantId is the source of truth for pricing and stock
/// - ProductId is kept for display grouping only
/// - CurrentPrice comes from Variant.OnlinePrice ?? Variant.Price (NEVER Product.Price)
/// - CostPrice comes from Variant.CostPrice
/// </summary>
public class CartItemSnapshot
{
    /// <summary>
    /// Product ID (for display grouping only - NOT for pricing).
    /// </summary>
    public Guid ProductId { get; set; }
    
    /// <summary>
    /// Variant ID (SOURCE OF TRUTH for pricing and stock).
    /// PHASE 11.4.1: This is the actual sellable SKU.
    /// Null only for legacy cart items without variant selection.
    /// </summary>
    public Guid? VariantId { get; set; }
    
    /// <summary>
    /// Category ID of the product (for promotion scope matching).
    /// PHASE: Promotions Engine - needed for category-scoped offers.
    /// </summary>
    public Guid? CategoryId { get; set; }
    
    /// <summary>
    /// SKU from Variant (immutable snapshot).
    /// </summary>
    public string SKU { get; set; } = string.Empty;
    
    /// <summary>
    /// Product name (for display).
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Product name in Arabic (for display).
    /// </summary>
    public string? NameAr { get; set; }
    
    /// <summary>
    /// Variant attributes formatted for display (e.g., "Red / XL").
    /// PHASE 11.4.1: Captured from Variant.Attributes.
    /// </summary>
    public string? VariantTitle { get; set; }
    
    /// <summary>
    /// Variant attributes as structured data.
    /// </summary>
    public List<VariantAttributeSnapshot>? VariantAttributes { get; set; }
    
    public int Quantity { get; set; }
    
    /// <summary>
    /// Price from cart (may be stale).
    /// </summary>
    public decimal CartPrice { get; set; }
    
    /// <summary>
    /// Recalculated price from Variant (OnlinePrice ?? Price).
    /// PHASE 11.4.1: This comes from Variant, NEVER from Product.
    /// </summary>
    public decimal CurrentPrice { get; set; }
    
    /// <summary>
    /// Cost price from Variant (for profit calculation).
    /// </summary>
    public decimal CostPrice { get; set; }

    /// <summary>
    /// Authoritative unit weight in kilograms captured from the current
    /// ProductVariant weight, falling back to Product weight.
    /// Revalidated with price/catalog authority before checkout pricing.
    /// </summary>
    public decimal Weight { get; set; }
    
    /// <summary>
    /// Image URL from Variant (or Product fallback).
    /// </summary>
    public string? ThumbnailImage { get; set; }
}

/// <summary>
/// Variant attribute snapshot for order items.
/// PHASE 11.4.1 — Immutable capture of variant selection.
/// </summary>
public class VariantAttributeSnapshot
{
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

/// <summary>
/// Checkout step result for tracking.
/// </summary>
public class CheckoutStepResult
{
    public string StepName { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? FailureReason { get; set; }
    public string? ErrorMessage { get; set; }
    public long DurationMs { get; set; }
}
