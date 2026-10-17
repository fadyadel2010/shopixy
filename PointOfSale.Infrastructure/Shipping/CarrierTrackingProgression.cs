using PointOfSale.Core.Enums;
using PointOfSale.Core.Policies;

namespace PointOfSale.Infrastructure.Shipping;

/// <summary>Catch up only through actual admitted observations; never fabricate missing checkpoints.</summary>
public static class CarrierTrackingProgression
{
    public static IReadOnlyList<TrackingEvent>? Resolve(ShipmentStatus current, ShipmentTrackingResult result)
    {
        var events = result.Events.OrderBy(e => e.Timestamp).ToArray();
        if (events.Length == 0 || events.Any(e => e.Timestamp.Kind != DateTimeKind.Utc || e.Timestamp == DateTime.MinValue ||
                e.Timestamp > DateTime.UtcNow.AddMinutes(5)) ||
            events.GroupBy(e => e.Timestamp).Any(g => g.Select(e => e.Status).Distinct().Count() != 1) ||
            events[^1].Status != result.CurrentStatus)
            return null;

        var path = new List<TrackingEvent>();
        var cursor = current;
        foreach (var observation in events)
        {
            if (observation.Status == cursor) continue;
            // Earlier outbound checkpoints in a complete history are not new transitions.
            if (path.Count == 0 && Rank(observation.Status) >= 0 && Rank(cursor) >= 0 && Rank(observation.Status) < Rank(cursor)) continue;
            if (!ShipmentLifecycleTransitionPolicy.CanTransition(cursor, observation.Status)) return null;
            path.Add(observation);
            cursor = observation.Status;
        }
        return cursor == result.CurrentStatus ? path : null;
    }

    private static int Rank(ShipmentStatus status) => status switch
    {
        ShipmentStatus.Pending => 0, ShipmentStatus.ReadyForPickup => 1, ShipmentStatus.PickedUp => 2,
        ShipmentStatus.InTransit => 3, ShipmentStatus.OutForDelivery => 4, ShipmentStatus.Delivered => 5,
        _ => -1
    };
}
