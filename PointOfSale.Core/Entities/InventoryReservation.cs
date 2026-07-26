using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

public class InventoryReservation : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid OrderId { get; set; }
    public Guid? SubOrderId { get; set; }
    public Guid OrderItemId { get; set; }
    public Guid ProductId { get; set; }
    public Guid VariantId { get; set; }
    public Guid WarehouseId { get; set; }
    public decimal ReservedQuantity { get; set; }
    public decimal CommittedQuantity { get; set; }
    public decimal ReleasedQuantity { get; set; }
    public decimal ReturnedQuantity { get; set; }
    public decimal DamagedReturnedQuantity { get; set; }
    public InventoryReservationStatus Status { get; set; } = InventoryReservationStatus.Active;
    public Guid ReservationEventId { get; set; }
    public Guid? LastMutationEventId { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }

    public decimal RemainingReservedQuantity => Math.Max(0m, ReservedQuantity - CommittedQuantity - ReleasedQuantity);

    public decimal RemainingDeliveredReturnableQuantity =>
        Math.Max(0m, CommittedQuantity - ReturnedQuantity - DamagedReturnedQuantity);
}
