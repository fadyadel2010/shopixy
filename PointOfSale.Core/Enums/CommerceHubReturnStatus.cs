namespace PointOfSale.Core.Enums;

public enum CommerceHubReturnStatus
{
    Pending = 0,
    PickupScheduled = 1,
    PickedUp = 2,
    InTransit = 3,
    DeliveredToVendor = 4,
    Confirmed = 5,
    Failed = 6
}
