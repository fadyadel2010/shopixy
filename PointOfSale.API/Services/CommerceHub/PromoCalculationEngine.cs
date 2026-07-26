using PointOfSale.Core.Entities;

namespace PointOfSale.API.Services.CommerceHub;

public readonly record struct PromoCommissionLine(
    Guid LineId,
    Guid StoreId,
    Guid CategoryId,
    decimal BaseCommissionAmount);

public sealed record PromoApplicationResult(
    decimal EligibleBaseCommissionAmount,
    decimal DiscountAmount,
    IReadOnlyDictionary<Guid, decimal> DiscountByLine)
{
    public static PromoApplicationResult Empty { get; } =
        new(0m, 0m, new Dictionary<Guid, decimal>());
}

internal static class PromoCalculationEngine
{
    public static PromoApplicationResult Apply(
        PromoCode promo,
        Guid storeId,
        IReadOnlyCollection<PromoCommissionLine> lines,
        Func<decimal, PromoCode, decimal> calculateDiscount)
    {
        ArgumentNullException.ThrowIfNull(promo);
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(calculateDiscount);

        if (lines.Count == 0 || !MatchesVendorScope(promo, storeId))
        {
            return PromoApplicationResult.Empty;
        }

        var eligibleLines = lines
            .Where(line => MatchesCategoryScope(promo, line.CategoryId) && line.BaseCommissionAmount > 0m)
            .OrderByDescending(line => line.BaseCommissionAmount)
            .ThenBy(line => line.LineId)
            .ToList();

        if (eligibleLines.Count == 0)
        {
            return PromoApplicationResult.Empty;
        }

        var eligibleBaseCommission = CommissionCalculator.RoundAmount(
            eligibleLines.Sum(line => line.BaseCommissionAmount));

        if (eligibleBaseCommission <= 0m)
        {
            return PromoApplicationResult.Empty;
        }

        var discountAmount = CommissionCalculator.RoundAmount(
            Math.Min(eligibleBaseCommission, calculateDiscount(eligibleBaseCommission, promo)));

        if (discountAmount <= 0m)
        {
            return new PromoApplicationResult(
                eligibleBaseCommission,
                0m,
                new Dictionary<Guid, decimal>());
        }

        return new PromoApplicationResult(
            eligibleBaseCommission,
            discountAmount,
            AllocateDiscount(discountAmount, eligibleLines));
    }

    private static IReadOnlyDictionary<Guid, decimal> AllocateDiscount(
        decimal totalDiscount,
        IReadOnlyCollection<PromoCommissionLine> eligibleLines)
    {
        var allocations = new Dictionary<Guid, decimal>(eligibleLines.Count);
        var remainders = new List<(Guid LineId, decimal FractionalRemainder, decimal Capacity)>(eligibleLines.Count);
        var allocated = 0m;

        foreach (var line in eligibleLines)
        {
            var exactShare = line.BaseCommissionAmount <= 0m
                ? 0m
                : totalDiscount * (line.BaseCommissionAmount / eligibleLines.Sum(item => item.BaseCommissionAmount));
            var roundedDownShare = Math.Floor(exactShare * 100m) / 100m;
            var cappedShare = Math.Min(line.BaseCommissionAmount, roundedDownShare);
            allocations[line.LineId] = CommissionCalculator.RoundAmount(cappedShare);
            allocated += cappedShare;
            remainders.Add((
                line.LineId,
                exactShare - roundedDownShare,
                line.BaseCommissionAmount - cappedShare));
        }

        var remaining = CommissionCalculator.RoundAmount(totalDiscount - allocated);
        if (remaining <= 0m)
        {
            return allocations;
        }

        foreach (var remainder in remainders
                     .Where(item => item.Capacity > 0m)
                     .OrderByDescending(item => item.FractionalRemainder)
                     .ThenBy(item => item.LineId))
        {
            if (remaining <= 0m)
            {
                break;
            }

            var current = allocations[remainder.LineId];
            if (current >= eligibleLines.First(line => line.LineId == remainder.LineId).BaseCommissionAmount)
            {
                continue;
            }

            allocations[remainder.LineId] = CommissionCalculator.RoundAmount(current + 0.01m);
            remaining = CommissionCalculator.RoundAmount(remaining - 0.01m);
        }

        if (remaining > 0m)
        {
            var fallbackLine = eligibleLines
                .First(line => allocations.GetValueOrDefault(line.LineId) < line.BaseCommissionAmount);
            allocations[fallbackLine.LineId] = CommissionCalculator.RoundAmount(
                allocations.GetValueOrDefault(fallbackLine.LineId) + remaining);
        }

        return allocations;
    }

    private static bool MatchesVendorScope(PromoCode promo, Guid storeId) =>
        !promo.VendorId.HasValue || promo.VendorId.Value == storeId;

    private static bool MatchesCategoryScope(PromoCode promo, Guid categoryId) =>
        !promo.CategoryId.HasValue || promo.CategoryId.Value == categoryId;
}
