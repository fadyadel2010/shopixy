using PointOfSale.Core.Entities;
using PointOfSale.Core.Policies;

namespace PointOfSale.Infrastructure.Shipping;

/// <summary>Operator-declared goods, separate from COD, shipping fees and order totals.</summary>
public static class BostaParcelContents
{
    public static bool IsDescription(string? value) => !string.IsNullOrWhiteSpace(value) &&
        value.Length <= 1000 && value == value.Trim() && !value.Any(char.IsControl);

    public static bool IsValue(decimal? value) => value is > 0 && decimal.Round(value.Value, 2) == value;

    public static int Validate(ParcelGoodsDeclaration? goods, Order order)
    {
        if (goods == null) throw new ArgumentException("BOSTA_GOODS_DECLARATION_REQUIRED");
        if (!IsDescription(goods.Description)) throw new ArgumentException("BOSTA_GOODS_DESCRIPTION_INVALID");
        if (!IsValue(goods.Value)) throw new ArgumentException("BOSTA_GOODS_VALUE_INVALID");
        if (goods.Currency != "EGP" || order.Currency != "EGP") throw new ArgumentException("BOSTA_GOODS_CURRENCY_UNSUPPORTED");
        var count = OrderInventoryReservationSnapshot.PhysicalItems(order).Sum(item => (decimal)item.Quantity);
        if (count is <= 0 or > int.MaxValue || decimal.Truncate(count) != count)
            throw new ArgumentException("BOSTA_GOODS_ITEM_COUNT_INVALID");
        return (int)count;
    }
}
