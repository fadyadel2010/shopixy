using System.Text.Json;
using PointOfSale.Core.Entities;
using PointOfSale.Infrastructure.Shipping;

namespace PointOfSale.Tests.Shipping;

public sealed class ParcelMeasurementTests
{
    private static (Shipment, Order) Context()
    {
        var tenant = Guid.NewGuid(); var order = new Order { Id = Guid.NewGuid(), TenantId = tenant };
        order.Items.Add(new OrderItem { Id = Guid.NewGuid(), ProductId = Guid.NewGuid(), Quantity = 2 });
        return (new Shipment { Id = Guid.NewGuid(), TenantId = tenant, OrderId = order.Id }, order);
    }
    [Theory]
    [InlineData("cm", "kg", 1, 1)]
    [InlineData("mm", "g", 0.1, 0.001)]
    [InlineData("in", "lb", 2.54, 0.454)]
    [InlineData("cm", "oz", 1, 0.028)]
    public void ExplicitUnits_ConvertWithDecimalRounding(string dimension, string weight, decimal cm, decimal kg)
    {
        var (s, o) = Context(); var e = ParcelMeasurements.Admit(new(1, 1, 1, dimension, 1, weight, 1, "BOX-1"), s, o, Guid.NewGuid());
        Assert.Equal(cm, e.LengthCm); Assert.Equal(kg, e.GrossWeightKg); Assert.Equal(2, e.Contents.Single().Quantity);
    }
    [Theory]
    [InlineData(0)] [InlineData(-1)] [InlineData(0.00001)] [InlineData(10000000)]
    public void InvalidDimensions_FailClosed(decimal length)
    {
        var (s, o) = Context(); Assert.Throws<ArgumentException>(() => ParcelMeasurements.Admit(new(length, 10, 10, "cm", 1, "kg", 1, "BOX"), s, o, Guid.NewGuid()));
    }
    [Theory]
    [InlineData(0)] [InlineData(-1)] [InlineData(0.0001)] [InlineData(1000000000000)]
    public void InvalidWeights_FailClosed(decimal weight)
    {
        var (s, o) = Context(); Assert.Throws<ArgumentException>(() => ParcelMeasurements.Admit(new(10, 10, 10, "cm", weight, "kg", 1, "BOX"), s, o, Guid.NewGuid()));
    }
    [Fact]
    public void MinimumMaximumAndMidpoint_AreDeterministic()
    {
        var (s, o) = Context();
        var e = ParcelMeasurements.Admit(new(0.001m, 9999999m, 1.2345m, "cm", 999999999999m, "kg", 1, "BOX"), s, o, Guid.NewGuid());
        Assert.Equal(1.235m, e.HeightCm); Assert.Equal(999999999999m, e.GrossWeightKg);
    }
    [Theory]
    [InlineData("m", "kg", 1)] [InlineData("cm", "stone", 1)] [InlineData("cm", "kg", 0)] [InlineData("cm", "kg", 2)]
    public void UnsupportedUnitsAndCounts_CannotInventSupport(string dim, string weight, int count)
    {
        var (s, o) = Context(); Assert.Throws<ArgumentException>(() => ParcelMeasurements.Admit(new(10, 10, 10, dim, 1, weight, count, "BOX"), s, o, Guid.NewGuid()));
    }
    [Fact]
    public void CatalogOrLegacyWeight_IsNotMeasurementEvidence_AndChangedContentsAreRejected()
    {
        var (s, o) = Context(); s.WeightKg = 22;
        Assert.Throws<ArgumentException>(() => ParcelMeasurements.Restore(s, o));
        var e = ParcelMeasurements.Admit(new(10, 11, 12, "cm", 2.3m, "kg", 1, "BOX"), s, o, Guid.NewGuid());
        s.ParcelEvidenceJson = JsonSerializer.Serialize(e);
        Assert.Equal(2.3m, ParcelMeasurements.Restore(s, o).GrossWeightKg);
        o.Items.Single().Quantity++;
        Assert.Throws<ArgumentException>(() => ParcelMeasurements.Restore(s, o));
    }
    [Theory]
    [InlineData("NaN")] [InlineData("Infinity")] [InlineData("1e100")]
    public void NonFiniteOrOverflowJson_CannotBecomeDecimalMeasurement(string value) =>
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ParcelMeasurement>($"{{\"Length\":{value}}}"));
}
