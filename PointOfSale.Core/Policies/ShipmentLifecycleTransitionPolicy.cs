using PointOfSale.Core.Enums;

namespace PointOfSale.Core.Policies;

/// <summary>
/// The single canonical shipment lifecycle transition table.  Every runtime
/// writer (admin intent, carrier webhook/sync, and recovery) must validate
/// against this policy before changing the persisted shipment state.
/// </summary>
public static class ShipmentLifecycleTransitionPolicy
{
    public static bool CanTransition(ShipmentStatus current, ShipmentStatus requested)
    {
        if (current == requested)
        {
            return true;
        }

        return (current, requested) switch
        {
            (ShipmentStatus.PendingExternalRegistration, ShipmentStatus.ReadyForPickup) => true,
            (ShipmentStatus.PendingExternalRegistration, ShipmentStatus.Cancelled) => true,
            (ShipmentStatus.PendingExternalRegistration, ShipmentStatus.Failed) => true,
            (ShipmentStatus.Pending, ShipmentStatus.ReadyForPickup) => true,
            (ShipmentStatus.Pending, ShipmentStatus.Cancelled) => true,
            (ShipmentStatus.ReadyForPickup, ShipmentStatus.PickedUp) => true,
            (ShipmentStatus.ReadyForPickup, ShipmentStatus.Cancelled) => true,
            (ShipmentStatus.PickedUp, ShipmentStatus.InTransit) => true,
            (ShipmentStatus.InTransit, ShipmentStatus.OutForDelivery) => true,
            (ShipmentStatus.InTransit, ShipmentStatus.Delivered) => true,
            (ShipmentStatus.InTransit, ShipmentStatus.Failed) => true,
            (ShipmentStatus.InTransit, ShipmentStatus.Returned) => true,
            (ShipmentStatus.OutForDelivery, ShipmentStatus.Delivered) => true,
            (ShipmentStatus.OutForDelivery, ShipmentStatus.Failed) => true,
            (ShipmentStatus.OutForDelivery, ShipmentStatus.Returned) => true,
            (ShipmentStatus.Delivered, ShipmentStatus.Returned) => true,
            _ => false
        };
    }
}
