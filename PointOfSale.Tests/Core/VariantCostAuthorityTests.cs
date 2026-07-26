using PointOfSale.Core.Inventory;

namespace PointOfSale.Tests.Core;

public sealed class VariantCostAuthorityTests
{
    [Fact]
    public void ApplyPurchaseReceipt_UsesMovingWeightedAverage()
    {
        var cost = VariantCostAuthority.ApplyPurchaseReceipt(
            currentQuantity: 10m,
            currentCostPrice: 5m,
            receivedQuantity: 5m,
            receivedUnitCost: 8m);

        Assert.Equal(6m, cost);
    }

    [Fact]
    public void ApplyPurchaseReceipt_UsesReceiptCostWhenNoStockExists()
    {
        var cost = VariantCostAuthority.ApplyPurchaseReceipt(
            currentQuantity: 0m,
            currentCostPrice: 0m,
            receivedQuantity: 5m,
            receivedUnitCost: 8m);

        Assert.Equal(8m, cost);
    }

    [Fact]
    public void ApplyPurchaseReturn_ReversesOriginalReceiptCostFromWeightedAverage()
    {
        var cost = VariantCostAuthority.ApplyPurchaseReturn(
            currentQuantity: 15m,
            currentCostPrice: 6m,
            returnedQuantity: 5m,
            returnedUnitCost: 8m);

        Assert.Equal(5m, cost);
    }

    [Fact]
    public void ApplyPurchaseReturn_ClearsCostWhenReturnRemovesAllStock()
    {
        var cost = VariantCostAuthority.ApplyPurchaseReturn(
            currentQuantity: 5m,
            currentCostPrice: 8m,
            returnedQuantity: 5m,
            returnedUnitCost: 8m);

        Assert.Equal(0m, cost);
    }

    [Fact]
    public void ApplyPurchaseReturn_NeverCreatesNegativeCost()
    {
        var cost = VariantCostAuthority.ApplyPurchaseReturn(
            currentQuantity: 5m,
            currentCostPrice: 1m,
            returnedQuantity: 1m,
            returnedUnitCost: 10m);

        Assert.Equal(0m, cost);
    }
}
