namespace PointOfSale.Core.Enums;

/// <summary>
/// Pricing model for a service.
/// Display-only - backend never computes prices.
/// </summary>
public enum PricingType
{
    Fixed = 0,
    Hourly = 1,
    Package = 2
}
