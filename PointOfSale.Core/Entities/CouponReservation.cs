using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

/// <summary>
/// Durable coupon reservation used to hold a pre-payment coupon claim until the
/// order either confirms payment or is released.
/// </summary>
public class CouponReservation : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }

    public Guid CouponId { get; set; }
    public Coupon Coupon { get; set; } = null!;

    public Guid CustomerId { get; set; }
    public OnlineCustomer? Customer { get; set; }

    public Guid OrderId { get; set; }
    public Order Order { get; set; } = null!;

    public CouponReservationStatus Status { get; set; } = CouponReservationStatus.Reserved;

    public string? CorrelationId { get; set; }

    public DateTime ReservedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? ConfirmedAtUtc { get; set; }

    public DateTime? ReleasedAtUtc { get; set; }
}
