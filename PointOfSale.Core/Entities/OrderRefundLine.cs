using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

public class OrderRefundLine : BaseEntity, ITenantOwned
{
    private decimal _amount;

    public Guid TenantId { get; set; }
    public Guid OrderRefundId { get; set; }
    public OrderRefund OrderRefund { get; set; } = null!;
    
    /// <summary>
    /// Denormalized OrderId for composite FK constraint.
    /// Ensures OrderItemId references an item from the correct order.
    /// </summary>
    public Guid OrderId { get; set; }
    
    public Guid OrderItemId { get; set; }
    public OrderItem OrderItem { get; set; } = null!;
    
    public decimal Amount
    {
        get => _amount;
        set
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(Amount),
                    value,
                    "Refund line amount cannot be negative.");
            }
            _amount = value;
        }
    }
}
