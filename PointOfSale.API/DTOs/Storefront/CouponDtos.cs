namespace PointOfSale.API.DTOs.Storefront;

/// <summary>
/// Coupon error codes for standardized error handling.
/// These codes are returned to the frontend for localized message display.
/// </summary>
public static class CouponErrorCodes
{
    /// <summary>Customer must be authenticated before coupon validation</summary>
    public const string AuthenticationRequired = "COUPON_AUTH_REQUIRED";

    /// <summary>Invalid or non-existent coupon code</summary>
    public const string Invalid = "COUPON_INVALID";
    
    /// <summary>Coupon has an invalid discount configuration</summary>
    public const string InvalidConfig = "COUPON_INVALID_CONFIG";
    
    /// <summary>Coupon not found in database</summary>
    public const string NotFound = "COUPON_NOT_FOUND";
    
    /// <summary>Coupon code does not match the stored code</summary>
    public const string CodeMismatch = "COUPON_CODE_MISMATCH";
    
    /// <summary>Coupon has expired (past end date)</summary>
    public const string Expired = "COUPON_EXPIRED";
    
    /// <summary>Coupon has not started yet (before start date)</summary>
    public const string NotYetValid = "COUPON_NOT_YET_VALID";
    
    /// <summary>Coupon is disabled by admin</summary>
    public const string Inactive = "COUPON_INACTIVE";
    
    /// <summary>Cart total below minimum order amount</summary>
    public const string MinimumNotMet = "COUPON_MIN_NOT_MET";
    
    /// <summary>Global usage limit exceeded</summary>
    public const string UsageExceeded = "COUPON_USAGE_EXCEEDED";
    
    /// <summary>Global max usage limit exceeded</summary>
    public const string MaxUsageExceeded = "COUPON_MAX_USAGE_EXCEEDED";
    
    /// <summary>Per-customer usage limit exceeded</summary>
    public const string CustomerUsageExceeded = "COUPON_CUSTOMER_USAGE_EXCEEDED";
    
    /// <summary>Per-customer max usage limit exceeded</summary>
    public const string CustomerMaxExceeded = "COUPON_CUSTOMER_MAX_EXCEEDED";
    
    /// <summary>First order only - customer has previous orders</summary>
    public const string FirstOrderOnly = "COUPON_FIRST_ORDER_ONLY";
    
    /// <summary>Product or category restrictions not met</summary>
    public const string NotApplicable = "COUPON_NOT_APPLICABLE";
    
    /// <summary>Coupon was auto-removed due to cart changes</summary>
    public const string RemovedAutomatically = "COUPON_REMOVED_AUTOMATICALLY";
    
    /// <summary>No coupon is applied to remove</summary>
    public const string NotApplied = "COUPON_NOT_APPLIED";
    
    /// <summary>Cart is empty - cannot apply coupon</summary>
    public const string CartEmpty = "COUPON_CART_EMPTY";
    
    /// <summary>Invalid order total (negative or invalid value)</summary>
    public const string InvalidOrderTotal = "INVALID_ORDER_TOTAL";
}

/// <summary>
/// Request to apply a coupon code to the cart
/// </summary>
public class ApplyCouponRequest
{
    /// <summary>
    /// Coupon code to apply (will be normalized: trimmed + uppercased)
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Short-lived server-issued smart-offer redemption token.
    /// When present, the server resolves the internal coupon without trusting a raw coupon code.
    /// </summary>
    public string? SmartOfferRedeemToken { get; set; }
    
    /// <summary>
    /// Validates the request
    /// </summary>
    public (string? ErrorCode, string? ErrorMessage) Validate()
    {
        if (string.IsNullOrWhiteSpace(Code) && string.IsNullOrWhiteSpace(SmartOfferRedeemToken))
        {
            return (CouponErrorCodes.Invalid, "Coupon code or smart offer token is required");
        }

        if (!string.IsNullOrWhiteSpace(Code) && 
            string.IsNullOrWhiteSpace(SmartOfferRedeemToken) && 
            Code.Length > 50)
        {
            return (CouponErrorCodes.Invalid, "Coupon code is too long");
        }

        if (!string.IsNullOrWhiteSpace(SmartOfferRedeemToken) && SmartOfferRedeemToken.Length > 4096)
        {
            return (SmartOfferErrorCodes.SmartOfferTokenInvalid, "Smart offer token is invalid");
        }
        
        return (null, null);
    }
    
    /// <summary>
    /// Returns normalized coupon code (trimmed + uppercased)
    /// </summary>
    public string NormalizedCode => Code?.Trim().ToUpperInvariant() ?? string.Empty;

    /// <summary>
    /// Returns normalized smart-offer token.
    /// </summary>
    public string? NormalizedSmartOfferRedeemToken => string.IsNullOrWhiteSpace(SmartOfferRedeemToken)
        ? null
        : SmartOfferRedeemToken.Trim();
}

/// <summary>
/// Coupon validation result from the service layer
/// </summary>
public class CouponValidationResult
{
    /// <summary>
    /// Whether the coupon is valid and can be applied
    /// </summary>
    public bool IsValid { get; set; }
    
    /// <summary>
    /// Error code if validation failed
    /// </summary>
    public string? ErrorCode { get; set; }
    
    /// <summary>
    /// Human-readable error message
    /// </summary>
    public string? ErrorMessage { get; set; }
    
    /// <summary>
    /// Coupon ID if valid
    /// </summary>
    public Guid? CouponId { get; set; }
    
    /// <summary>
    /// Validated coupon code (normalized)
    /// </summary>
    public string? Code { get; set; }
    
    /// <summary>
    /// Discount type (percentage or fixed)
    /// </summary>
    public string? DiscountType { get; set; }
    
    /// <summary>
    /// Discount value (percentage or fixed amount)
    /// </summary>
    public decimal? DiscountValue { get; set; }
    
    /// <summary>
    /// Calculated discount amount for the current cart
    /// </summary>
    public decimal? DiscountAmount { get; set; }
    
    /// <summary>
    /// Creates a successful validation result
    /// </summary>
    public static CouponValidationResult Success(
        Guid couponId,
        string code,
        string discountType,
        decimal discountValue,
        decimal discountAmount) => new()
    {
        IsValid = true,
        CouponId = couponId,
        Code = code,
        DiscountType = discountType,
        DiscountValue = discountValue,
        DiscountAmount = discountAmount
    };
    
    /// <summary>
    /// Creates a failed validation result
    /// </summary>
    public static CouponValidationResult Failure(string errorCode, string errorMessage) => new()
    {
        IsValid = false,
        ErrorCode = errorCode,
        ErrorMessage = errorMessage
    };
}

/// <summary>
/// Applied coupon details included in cart response
/// </summary>
public class AppliedCouponDto
{
    /// <summary>
    /// Applied coupon code
    /// </summary>
    public string Code { get; set; } = string.Empty;
    
    /// <summary>
    /// Discount type: "percentage" or "fixed"
    /// </summary>
    public string Type { get; set; } = string.Empty;
    
    /// <summary>
    /// Discount value (percentage or fixed amount)
    /// </summary>
    public decimal Value { get; set; }
    
    /// <summary>
    /// Calculated discount amount
    /// </summary>
    public decimal Amount { get; set; }
}

/// <summary>
/// Response for coupon apply/remove operations
/// </summary>
public class CouponOperationResponse
{
    /// <summary>
    /// Whether the operation succeeded
    /// </summary>
    public bool Success { get; set; }
    
    /// <summary>
    /// Error code if operation failed
    /// </summary>
    public string? ErrorCode { get; set; }
    
    /// <summary>
    /// Error message if operation failed
    /// </summary>
    public string? ErrorMessage { get; set; }
    
    /// <summary>
    /// Applied coupon details (null if removed or failed)
    /// </summary>
    public AppliedCouponDto? Coupon { get; set; }
}
