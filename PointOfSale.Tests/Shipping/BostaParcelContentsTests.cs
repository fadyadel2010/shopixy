using System.Text.Json;
using PointOfSale.Core.Entities;
using PointOfSale.Infrastructure.Shipping;

namespace PointOfSale.Tests.Shipping;

public sealed class BostaParcelContentsTests
{
    [Fact]
    public void ActualGoodsArePartOfImmutableMeasuredEvidenceAndItemCountIsNotParcelCount()
    {
        var (shipment, order) = Context();
        var input = Measurement() with { Goods = new("Synthetic packed goods", 75m, "EGP") };
        var evidence = ParcelMeasurements.Admit(input, shipment, order, Guid.NewGuid());
        shipment.ParcelEvidenceJson = JsonSerializer.Serialize(evidence);
        Assert.Equal(input, ParcelMeasurements.Restore(shipment, order).Measurement);
        Assert.Equal(2, BostaParcelContents.Validate(input.Goods, order));
        Assert.Equal(1, input.PackageCount);
        Assert.NotEqual(input, input with { Goods = input.Goods with { Value = 76m } });
    }

    [Fact]
    public void BostaCannotSaveMeasurementsWithoutExplicitGoodsDeclaration_OrInferItFromOrderTotal()
    {
        var (shipment, order) = Context(); order.Total = 100m;
        Assert.Equal("BOSTA_GOODS_DECLARATION_REQUIRED", Assert.Throws<ArgumentException>(() =>
            ParcelMeasurements.Admit(Measurement(), shipment, order, Guid.NewGuid())).Message);
    }

    [Theory]
    [InlineData(0)] [InlineData(-1)] [InlineData(12.345)]
    public void InvalidGoodsValueIsRejected(decimal value)
    {
        var (shipment, order) = Context();
        var input = Measurement() with { Goods = new("Synthetic packed goods", value, "EGP") };
        Assert.Equal("BOSTA_GOODS_VALUE_INVALID", Assert.Throws<ArgumentException>(() =>
            ParcelMeasurements.Admit(input, shipment, order, Guid.NewGuid())).Message);
    }

    [Theory]
    [InlineData("")] [InlineData("   ")] [InlineData("Synthetic\r\nheader")]
    public void InvalidDescriptionIsRejected(string description)
    {
        var (shipment, order) = Context();
        var input = Measurement() with { Goods = new(description, 75m, "EGP") };
        Assert.Equal("BOSTA_GOODS_DESCRIPTION_INVALID", Assert.Throws<ArgumentException>(() =>
            ParcelMeasurements.Admit(input, shipment, order, Guid.NewGuid())).Message);
    }

    [Fact]
    public void OtherCarrierExistingMeasurementShapeAndBehaviorRemainCompatible()
    {
        var (shipment, order) = Context(); shipment.CarrierCode = "dhl";
        var input = Measurement();
        Assert.DoesNotContain("Goods", JsonSerializer.Serialize(input));
        Assert.Null(ParcelMeasurements.Admit(input, shipment, order, Guid.NewGuid()).Measurement.Goods);
    }

    [Fact]
    public void LegacyBostaEvidenceRemainsReadableButCannotAuthorizeANewCreateWithoutGoods()
    {
        var (shipment, order) = Context(); shipment.CarrierCode = "dhl";
        var evidence = ParcelMeasurements.Admit(Measurement(), shipment, order, Guid.NewGuid());
        shipment.ParcelEvidenceJson = JsonSerializer.Serialize(evidence); shipment.CarrierCode = "bosta";
        var restored = ParcelMeasurements.Restore(shipment, order);
        Assert.Null(restored.Measurement.Goods);
        Assert.Equal("BOSTA_GOODS_DECLARATION_REQUIRED", Assert.Throws<ArgumentException>(() =>
            BostaParcelContents.Validate(restored.Measurement.Goods, order)).Message);
    }

    private static ParcelMeasurement Measurement() => new(20, 15, 10, "cm", 1.25m, "kg", 1, "SYNTHETIC-BOX");
    private static (Shipment, Order) Context()
    {
        var order = new Order { Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), Currency = "EGP" };
        order.Items.Add(new OrderItem { Id = Guid.NewGuid(), ProductId = Guid.NewGuid(), Quantity = 2 });
        return (new Shipment { Id = Guid.NewGuid(), OrderId = order.Id, TenantId = order.TenantId, CarrierCode = "bosta" }, order);
    }
}
