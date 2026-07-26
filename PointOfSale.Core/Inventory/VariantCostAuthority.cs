namespace PointOfSale.Core.Inventory;

public static class VariantCostAuthority
{
    public static decimal ApplyPurchaseReceipt(
        decimal currentQuantity,
        decimal currentCostPrice,
        decimal receivedQuantity,
        decimal receivedUnitCost)
    {
        if (receivedQuantity <= 0m)
        {
            return currentCostPrice;
        }

        var resultingQuantity = currentQuantity + receivedQuantity;
        if (resultingQuantity <= 0m)
        {
            return receivedUnitCost;
        }

        return ((currentQuantity * currentCostPrice) + (receivedQuantity * receivedUnitCost)) / resultingQuantity;
    }

    public static decimal ApplyPurchaseReturn(
        decimal currentQuantity,
        decimal currentCostPrice,
        decimal returnedQuantity,
        decimal returnedUnitCost)
    {
        if (returnedQuantity <= 0m)
        {
            return currentCostPrice;
        }

        var remainingQuantity = currentQuantity - returnedQuantity;
        if (remainingQuantity <= 0m)
        {
            return 0m;
        }

        var remainingValue = (currentQuantity * currentCostPrice) - (returnedQuantity * returnedUnitCost);
        return Math.Max(0m, remainingValue / remainingQuantity);
    }
}
