using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Shipping;

namespace PointOfSale.Tests.Shipping;

public sealed class CarrierTrackingProgressionTests
{
    private static TrackingEvent Observation(ShipmentStatus status, int minute) => new()
    {
        Status = status, Timestamp = DateTime.UtcNow.Date.AddDays(-1).AddMinutes(minute), Description = "Synthetic observed checkpoint"
    };

    [Fact]
    public void CompleteHistoryUsesObservedEdgesNotInventedSteps()
    {
        var result = new ShipmentTrackingResult { CurrentStatus = ShipmentStatus.Delivered, Events = [
            Observation(ShipmentStatus.Delivered, 3), Observation(ShipmentStatus.PickedUp, 1), Observation(ShipmentStatus.InTransit, 2)] };
        var path = CarrierTrackingProgression.Resolve(ShipmentStatus.ReadyForPickup, result);
        Assert.NotNull(path);
        Assert.Equal(new[] { ShipmentStatus.PickedUp, ShipmentStatus.InTransit, ShipmentStatus.Delivered }, path.Select(e => e.Status));
        Assert.DoesNotContain(path, e => e.Status == ShipmentStatus.OutForDelivery);
    }

    [Theory]
    [InlineData(ShipmentStatus.ReadyForPickup, ShipmentStatus.Delivered)]
    [InlineData(ShipmentStatus.Cancelled, ShipmentStatus.InTransit)]
    [InlineData(ShipmentStatus.Failed, ShipmentStatus.Delivered)]
    [InlineData(ShipmentStatus.Returned, ShipmentStatus.Delivered)]
    public void MissingEdgesAndTerminalRegressionRemainRejected(ShipmentStatus from, ShipmentStatus target) =>
        Assert.Null(CarrierTrackingProgression.Resolve(from, new() { CurrentStatus = target, Events = [Observation(target, 1)] }));

    [Fact]
    public void ConflictingSimultaneousOrNonUtcObservationsAreRejected()
    {
        Assert.Null(CarrierTrackingProgression.Resolve(ShipmentStatus.ReadyForPickup, new() { CurrentStatus = ShipmentStatus.InTransit,
            Events = [Observation(ShipmentStatus.PickedUp, 1), Observation(ShipmentStatus.InTransit, 1)] }));
        var local = Observation(ShipmentStatus.PickedUp, 1); local.Timestamp = DateTime.SpecifyKind(local.Timestamp, DateTimeKind.Unspecified);
        Assert.Null(CarrierTrackingProgression.Resolve(ShipmentStatus.ReadyForPickup, new() { CurrentStatus = ShipmentStatus.InTransit,
            Events = [local, Observation(ShipmentStatus.InTransit, 2)] }));
    }

    [Fact]
    public void RegressionInsideNewHistoryCannotBeDiscardedToCreateAValidPath() =>
        Assert.Null(CarrierTrackingProgression.Resolve(ShipmentStatus.ReadyForPickup, new() { CurrentStatus = ShipmentStatus.Delivered,
            Events = [Observation(ShipmentStatus.PickedUp, 1), Observation(ShipmentStatus.InTransit, 2),
                Observation(ShipmentStatus.PickedUp, 3), Observation(ShipmentStatus.Delivered, 4)] }));
}
