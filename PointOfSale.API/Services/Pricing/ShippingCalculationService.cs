using PointOfSale.Core.Entities;
using PointOfSale.Infrastructure.MongoDB.Documents;

namespace PointOfSale.API.Services.Pricing;

public interface IShippingCalculationService
{
    bool TryCalculate(
        decimal baseRate,
        decimal perKgRate,
        decimal? freeShippingThreshold,
        decimal subtotal,
        out decimal shippingCost,
        decimal totalWeight = 0m,
        bool rejectThresholdOnlyZeroRateBeforeThreshold = false);

    decimal Calculate(
        decimal baseRate,
        decimal perKgRate,
        decimal? freeShippingThreshold,
        decimal subtotal,
        decimal totalWeight = 0m);

    decimal Calculate(ShippingMethodDocument method, decimal subtotal, decimal totalWeight = 0m);

    decimal Calculate(ShippingMethod method, decimal subtotal, decimal totalWeight = 0m);
}

public sealed class ShippingCalculationService : IShippingCalculationService
{
    private const int CurrencyScale = 2;

    public bool TryCalculate(
        decimal baseRate,
        decimal perKgRate,
        decimal? freeShippingThreshold,
        decimal subtotal,
        out decimal shippingCost,
        decimal totalWeight = 0m,
        bool rejectThresholdOnlyZeroRateBeforeThreshold = false)
    {
        if (rejectThresholdOnlyZeroRateBeforeThreshold &&
            freeShippingThreshold.HasValue &&
            subtotal < freeShippingThreshold.Value &&
            baseRate <= 0m &&
            perKgRate <= 0m)
        {
            shippingCost = 0m;
            return false;
        }

        shippingCost = Calculate(baseRate, perKgRate, freeShippingThreshold, subtotal, totalWeight);
        return true;
    }

    public decimal Calculate(ShippingMethodDocument method, decimal subtotal, decimal totalWeight = 0m)
    {
        ArgumentNullException.ThrowIfNull(method);

        return Calculate(
            method.BaseRate,
            method.PerKgRate,
            method.FreeShippingThreshold,
            subtotal,
            totalWeight);
    }

    public decimal Calculate(ShippingMethod method, decimal subtotal, decimal totalWeight = 0m)
    {
        ArgumentNullException.ThrowIfNull(method);

        return Calculate(
            method.BaseRate,
            method.PerKgRate,
            method.FreeShippingThreshold,
            subtotal,
            totalWeight);
    }

    public decimal Calculate(
        decimal baseRate,
        decimal perKgRate,
        decimal? freeShippingThreshold,
        decimal subtotal,
        decimal totalWeight)
    {
        if (freeShippingThreshold.HasValue && subtotal >= freeShippingThreshold.Value)
        {
            return 0m;
        }

        var calculatedCost = Math.Max(0m, baseRate + (perKgRate * Math.Max(0m, totalWeight)));
        return RoundCurrency(calculatedCost);
    }

    private static decimal RoundCurrency(decimal value)
    {
        return Math.Round(value, CurrencyScale, MidpointRounding.AwayFromZero);
    }
}
