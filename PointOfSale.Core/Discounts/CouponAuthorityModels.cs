using PointOfSale.Core.Entities;

namespace PointOfSale.Core.Discounts;

public readonly record struct CouponScopeValidationItem(Guid ProductId, Guid CategoryId);

public sealed class CouponValidationSnapshot
{
    public decimal OrderSubtotal { get; init; }
    public decimal AppliedDiscountAmount { get; init; }
    public IReadOnlyCollection<CouponScopeValidationItem> CartItems { get; init; } = Array.Empty<CouponScopeValidationItem>();
}

public sealed class CouponValidationDecision
{
    public bool IsValid { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public Guid? CouponId { get; init; }
    public string? CouponCode { get; init; }
    public string? DiscountType { get; init; }
    public decimal? DiscountValue { get; init; }
    public decimal? CalculatedDiscountAmount { get; init; }
    public int ConfirmedUsageCount { get; init; }
    public int CustomerConfirmedUsageCount { get; init; }
    public int ActiveReservationCount { get; init; }
    public int ActiveCustomerReservationCount { get; init; }

    public static CouponValidationDecision Success(
        Guid couponId,
        string couponCode,
        string discountType,
        decimal discountValue,
        decimal calculatedDiscountAmount,
        int confirmedUsageCount,
        int customerConfirmedUsageCount,
        int activeReservationCount,
        int activeCustomerReservationCount)
    {
        return new CouponValidationDecision
        {
            IsValid = true,
            CouponId = couponId,
            CouponCode = couponCode,
            DiscountType = discountType,
            DiscountValue = discountValue,
            CalculatedDiscountAmount = calculatedDiscountAmount,
            ConfirmedUsageCount = confirmedUsageCount,
            CustomerConfirmedUsageCount = customerConfirmedUsageCount,
            ActiveReservationCount = activeReservationCount,
            ActiveCustomerReservationCount = activeCustomerReservationCount
        };
    }

    public static CouponValidationDecision Failure(string errorCode, string errorMessage)
    {
        return new CouponValidationDecision
        {
            IsValid = false,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage
        };
    }
}

public sealed class CouponReservationDecision
{
    public bool HasCoupon { get; init; }
    public Guid? CouponId { get; init; }
    public string? CouponCode { get; init; }
    public Guid? ReservationId { get; init; }
    public decimal AppliedDiscountAmount { get; init; }
    public bool WasCreated { get; init; }
    public bool WasIdempotent { get; init; }
    public CouponReservation? Reservation { get; init; }

    public static CouponReservationDecision None() => new();

    public static CouponReservationDecision Reserved(
        Guid couponId,
        string couponCode,
        Guid reservationId,
        decimal appliedDiscountAmount,
        bool wasCreated,
        bool wasIdempotent,
        CouponReservation reservation)
    {
        return new CouponReservationDecision
        {
            HasCoupon = true,
            CouponId = couponId,
            CouponCode = couponCode,
            ReservationId = reservationId,
            AppliedDiscountAmount = appliedDiscountAmount,
            WasCreated = wasCreated,
            WasIdempotent = wasIdempotent,
            Reservation = reservation
        };
    }
}

public sealed class CouponConfirmationDecision
{
    public bool HasCoupon { get; init; }
    public Guid? CouponId { get; init; }
    public string? CouponCode { get; init; }
    public Guid? CouponUsageId { get; init; }
    public bool WasConfirmed { get; init; }
    public bool WasAlreadyConfirmed { get; init; }
    public bool CreatedLegacyReservation { get; init; }
    public int ConfirmedUsageCount { get; init; }
    public int CustomerUsageCount { get; init; }
    public CouponUsage? CouponUsage { get; init; }

    public static CouponConfirmationDecision None() => new();

    public static CouponConfirmationDecision Confirmed(
        Guid couponId,
        string couponCode,
        Guid couponUsageId,
        bool wasConfirmed,
        bool wasAlreadyConfirmed,
        bool createdLegacyReservation,
        int confirmedUsageCount,
        int customerUsageCount,
        CouponUsage couponUsage)
    {
        return new CouponConfirmationDecision
        {
            HasCoupon = true,
            CouponId = couponId,
            CouponCode = couponCode,
            CouponUsageId = couponUsageId,
            WasConfirmed = wasConfirmed,
            WasAlreadyConfirmed = wasAlreadyConfirmed,
            CreatedLegacyReservation = createdLegacyReservation,
            ConfirmedUsageCount = confirmedUsageCount,
            CustomerUsageCount = customerUsageCount,
            CouponUsage = couponUsage
        };
    }
}

public sealed class CouponReleaseDecision
{
    public bool HasCoupon { get; init; }
    public Guid? CouponId { get; init; }
    public string? CouponCode { get; init; }
    public Guid? ReservationId { get; init; }
    public bool WasReleased { get; init; }
    public bool HadConfirmedUsage { get; init; }

    public static CouponReleaseDecision None() => new();

    public static CouponReleaseDecision Released(
        Guid couponId,
        string couponCode,
        Guid reservationId,
        bool wasReleased,
        bool hadConfirmedUsage)
    {
        return new CouponReleaseDecision
        {
            HasCoupon = true,
            CouponId = couponId,
            CouponCode = couponCode,
            ReservationId = reservationId,
            WasReleased = wasReleased,
            HadConfirmedUsage = hadConfirmedUsage
        };
    }
}
