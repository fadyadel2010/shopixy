using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

public class CommerceHubMarketplaceOrder : BaseEntity, IProjectionVersioned
{
    public Guid CustomerId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string? IdempotencyKey { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string AddressLine { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string? Area { get; set; }
    public string? Notes { get; set; }
    public string Currency { get; set; } = "EGP";
    public string PaymentMethodCode { get; set; } = string.Empty;
    public string PaymentMethodName { get; set; } = string.Empty;
    public string? PaymentReference { get; set; }
    public string? PaymentReferenceId { get; set; }
    public string? PaymentGateway { get; set; }
    public decimal Subtotal { get; set; }
    public decimal ShippingTotal { get; set; }
    public decimal TotalAmount { get; set; }
    public string? PromoCode { get; set; }
    public decimal PromoDiscountTotal { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Pending;
    public DateTime? PaidAt { get; set; }
    public Guid? OnlineCustomerId { get; set; }
    public string? OrderAccessToken { get; set; }
    public bool IsProjected { get; set; }
    public DateTime? ProjectedAtUtc { get; set; }
    public long ProjectionMutationVersion { get; private set; }
    
    // Projection retry tracking fields
    public DateTime? LastProjectionAttemptAt { get; set; }
    public int ProjectionAttemptCount { get; set; }
    public bool IsProjectionTerminalFailure { get; set; }

    public CommerceHubCustomer Customer { get; set; } = null!;
    public ICollection<CommerceHubSubOrder> SubOrders { get; set; } = new List<CommerceHubSubOrder>();
    public ICollection<CommerceHubPayment> Payments { get; set; } = new List<CommerceHubPayment>();

    public void AdvanceProjectionMutationVersion(long newVersion)
    {
        if (newVersion <= ProjectionMutationVersion)
        {
            throw new InvalidOperationException(
                $"Cannot regress ProjectionMutationVersion from {ProjectionMutationVersion} to {newVersion}. " +
                "CommerceHub marketplace order projection versions must be strictly monotonic.");
        }

        ProjectionMutationVersion = newVersion;
    }

    public void TouchProjectionMutationVersion()
    {
        AdvanceProjectionMutationVersion(ProjectionMutationVersion <= 0 ? 1L : ProjectionMutationVersion + 1L);
    }

    public List<Guid> GetAffectedStoreIds()
    {
        return SubOrders
            .Select(subOrder => subOrder.StoreId)
            .Where(storeId => storeId != Guid.Empty)
            .Distinct()
            .OrderBy(storeId => storeId)
            .ToList();
    }
}
