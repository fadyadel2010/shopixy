using PointOfSale.Core.Enums;

namespace PointOfSale.API.Services.CommerceHub;

public static class CommissionCalculator
{
    public const string CommissionBase = "Subtotal";
    public const string VendorSource = "Vendor";
    public const string CategorySource = "Category";
    public const string GlobalSource = "Global";
    public const string LegacySource = "Legacy";
    public const string MixedSource = "Mixed";
    public const string HistoricalFieldsSource = "HistoricalSnapshotFields";
    public const string ItemSnapshotFallbackSource = "ItemSnapshotFallback";

    public static decimal RoundAmount(decimal value)
    {
        return Math.Round(value, 2, MidpointRounding.AwayFromZero);
    }

    public static decimal RoundRate(decimal value)
    {
        return Math.Round(value, 6, MidpointRounding.AwayFromZero);
    }

    public static string ResolveSource(CommissionRuleScopeType? scopeType, bool usedLegacyFallback = false)
    {
        if (usedLegacyFallback || !scopeType.HasValue)
        {
            return LegacySource;
        }

        return scopeType.Value switch
        {
            CommissionRuleScopeType.Vendor => VendorSource,
            CommissionRuleScopeType.Category => CategorySource,
            CommissionRuleScopeType.Global => GlobalSource,
            _ => LegacySource
        };
    }
}
