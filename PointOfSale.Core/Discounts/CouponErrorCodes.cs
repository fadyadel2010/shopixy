namespace PointOfSale.Core.Discounts;

public static class CouponErrorCodes
{
    public const string Invalid = "COUPON_INVALID";
    public const string InvalidConfig = "COUPON_INVALID_CONFIG";
    public const string Inactive = "COUPON_INACTIVE";
    public const string NotYetValid = "COUPON_NOT_YET_VALID";
    public const string Expired = "COUPON_EXPIRED";
    public const string MaxUsageExceeded = "COUPON_MAX_USAGE_EXCEEDED";
    public const string CustomerMaxExceeded = "COUPON_CUSTOMER_MAX_EXCEEDED";
    public const string MinimumNotMet = "COUPON_MINIMUM_NOT_MET";
    public const string FirstOrderOnly = "COUPON_FIRST_ORDER_ONLY";
    public const string NotApplicable = "COUPON_NOT_APPLICABLE";
    public const string CartEmpty = "COUPON_CART_EMPTY";
    public const string AuthenticationRequired = "COUPON_AUTH_REQUIRED";
    public const string CodeMismatch = "COUPON_CODE_MISMATCH";
    public const string InvalidOrderTotal = "COUPON_INVALID_ORDER_TOTAL";
    public const string NotFound = "COUPON_NOT_FOUND";
    public const string DiscountMismatch = "COUPON_DISCOUNT_MISMATCH";
    public const string ReservationMissing = "COUPON_RESERVATION_MISSING";
    public const string ReservationExists = "COUPON_RESERVATION_EXISTS";
}
