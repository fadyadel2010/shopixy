namespace PointOfSale.Core.Enums;

public enum InventoryReservationStatus
{
    Active = 0,
    PartiallyReleased = 1,
    Released = 2,
    PartiallyCommitted = 3,
    Committed = 4,
    PartiallyReturned = 5,
    Returned = 6,
    Expired = 7
}
