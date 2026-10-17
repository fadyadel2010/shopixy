using System.Text.Json.Nodes;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Policies;

namespace PointOfSale.Tests.Shipping;

public sealed class OrderInventoryReservationSnapshotTests
{
    [Fact]
    public void Capture_RetainsExistingPricingAndExactPhysicalLines_IndependentOfLaterCatalog()
    {
        var order = Order(); var physical = order.Items.First();
        order.PricingSnapshotJson = OrderInventoryReservationSnapshot.Capture(order,
            new Dictionary<(Guid, Guid?), decimal> { [(physical.ProductId, physical.VariantId)] = physical.Quantity });
        Assert.Equal(125, JsonNode.Parse(order.PricingSnapshotJson)!["total"]!.GetValue<int>());
        Assert.Equal(physical.Id, Assert.Single(OrderInventoryReservationSnapshot.PhysicalItems(order)).Id);
    }

    [Theory]
    [InlineData("tenantId")][InlineData("orderId")][InlineData("warehouseId")]
    [InlineData("line-quantity")][InlineData("line-warehouse")][InlineData("missing-policy")]
    [InlineData("missing-line")][InlineData("duplicate-line")]
    public void RetainedSnapshot_RefusesForeignIncompleteOrSubstitutedEvidence(string corruption)
    {
        var order = Order();
        var json = JsonNode.Parse(OrderInventoryReservationSnapshot.Capture(order, new Dictionary<(Guid, Guid?), decimal>()))!;
        var snapshot = json["inventoryReservation"]!;
        if (corruption is "tenantId" or "orderId" or "warehouseId") snapshot[corruption] = Guid.NewGuid();
        else if (corruption == "line-quantity") snapshot["lines"]![0]!["quantity"] = 99;
        else if (corruption == "line-warehouse") snapshot["lines"]![0]!["warehouseId"] = Guid.NewGuid();
        else if (corruption == "missing-policy") snapshot["lines"]![0]!.AsObject().Remove("requiresPhysicalReservation");
        else if (corruption == "missing-line") snapshot["lines"]!.AsArray().RemoveAt(0);
        else snapshot["lines"]![1] = snapshot["lines"]![0]!.DeepClone();
        order.PricingSnapshotJson = json.ToJsonString();
        Assert.Throws<InvalidOperationException>(() => OrderInventoryReservationSnapshot.PhysicalItems(order));
    }

    [Fact]
    public void LegacyWithoutPolicy_DoesNotInferExemptionOrRewriteHistory()
    {
        var order = Order(); var before = order.PricingSnapshotJson;
        Assert.Equal(2, OrderInventoryReservationSnapshot.PhysicalItems(order).Count);
        Assert.Equal(before, order.PricingSnapshotJson);
    }

    private static Order Order()
    {
        var order = new Order { Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), WarehouseId = Guid.NewGuid(), PricingSnapshotJson = "{\"total\":125}" };
        for (var i = 0; i < 2; i++) order.Items.Add(new OrderItem { Id = Guid.NewGuid(), OrderId = order.Id, ProductId = Guid.NewGuid(),
            VariantId = Guid.NewGuid(), WarehouseId = order.WarehouseId, Quantity = 2 });
        return order;
    }
}
