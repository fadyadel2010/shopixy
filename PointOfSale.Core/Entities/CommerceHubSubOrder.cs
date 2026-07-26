using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

public class CommerceHubSubOrder : BaseEntity, IProjectionVersioned
{
    public Guid MarketplaceOrderId { get; set; }
    public CommerceHubMarketplaceOrder MarketplaceOrder { get; set; } = null!;
    public string OrderNumber { get; set; } = string.Empty;
    public Guid StoreId { get; set; }
    public string StoreName { get; set; } = string.Empty;
    public string Currency { get; set; } = "EGP";
    public decimal Subtotal { get; set; }
    public decimal Shipping { get; set; }
    public Guid? ShippingMethodId { get; set; }
    public string? ShippingMethodName { get; set; }
    public string? ShippingSnapshotJson { get; set; }
    public CommerceHubPaymentMethod PaymentMethod { get; set; } = CommerceHubPaymentMethod.Online;
    public string PaymentMethodCode { get; set; } = string.Empty;
    public string PaymentMethodName { get; set; } = string.Empty;
    public string? PaymentSnapshotJson { get; set; }
    public string? CommissionSnapshotJson { get; set; }
    public bool IsEarningCreated { get; set; }
    public DateTime? EarningCreatedAtUtc { get; set; }
    public decimal PlatformCommissionAmount { get; set; }
    public decimal FinalPlatformCommissionAmount { get; set; }
    public decimal PromoDiscountAmount { get; set; }
    public decimal VendorEarningAmount { get; set; }
    public decimal PlatformCommissionRate { get; set; }
    public decimal Total { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Pending;
    public string? PaymentReferenceId { get; set; }
    public DateTime? PaidAt { get; set; }
    public long ProjectionMutationVersion { get; private set; }

    public ICollection<CommerceHubSubOrderItem> Items { get; set; } = new List<CommerceHubSubOrderItem>();

    public void AdvanceProjectionMutationVersion(long newVersion)
    {
        if (newVersion <= ProjectionMutationVersion)
        {
            throw new InvalidOperationException(
                $"Cannot regress ProjectionMutationVersion from {ProjectionMutationVersion} to {newVersion}. " +
                "CommerceHub sub-order projection versions must be strictly monotonic.");
        }

        ProjectionMutationVersion = newVersion;
    }

    public void TouchProjectionMutationVersion()
    {
        AdvanceProjectionMutationVersion(ProjectionMutationVersion <= 0 ? 1L : ProjectionMutationVersion + 1L);
    }
}
