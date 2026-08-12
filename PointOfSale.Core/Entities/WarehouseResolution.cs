using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

/// <summary>
/// Server-owned warehouse decision for a Storefront checkout session.
/// This row is the warehouse authority for shipping eligibility, reservation,
/// order warehouse stamping, order item warehouse stamping, and shipment origin.
/// </summary>
public sealed class WarehouseResolution : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }

    public Guid CheckoutSessionId { get; set; }

    public Guid CartId { get; set; }

    public Guid? WarehouseId { get; set; }

    public WarehouseResolutionStatus Status { get; set; } = WarehouseResolutionStatus.Unresolved;

    public WarehouseResolutionSource ResolutionSource { get; set; } = WarehouseResolutionSource.StorefrontCheckout;

    public int ResolutionVersion { get; set; }

    public string AlgorithmCode { get; set; } = string.Empty;

    public string InputsSnapshotHash { get; set; } = string.Empty;

    public string? InputsSnapshotJson { get; set; }

    public string? FailureCode { get; set; }

    public string? InvalidationReason { get; set; }

    public DateTime? ResolvedAtUtc { get; set; }

    public DateTime? ExpiresAtUtc { get; set; }

    public DateTime? LockedAtUtc { get; set; }

    public DateTime? InvalidatedAtUtc { get; set; }

    public Guid? ConvertedOrderId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? UpdatedAtUtc { get; set; }

    public CheckoutSession CheckoutSession { get; set; } = null!;

    public Warehouse? Warehouse { get; set; }

    public Order? ConvertedOrder { get; set; }
}
