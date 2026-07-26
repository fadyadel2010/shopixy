using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

/// <summary>
/// Immutable mapping of orders included in a settlement batch.
/// The unique order constraint prevents an order from being settled twice.
/// </summary>
public class PaymentSettlementOrder : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }

    public Guid SettlementBatchId { get; set; }

    public PaymentSettlementBatch SettlementBatch { get; set; } = null!;

    public Guid OrderId { get; set; }

    public Order Order { get; set; } = null!;

    public Guid? PaymentIntentId { get; set; }

    public PaymentIntent? PaymentIntent { get; set; }

    public decimal OrderTotal { get; set; }

    public string Currency { get; set; } = string.Empty;
}
