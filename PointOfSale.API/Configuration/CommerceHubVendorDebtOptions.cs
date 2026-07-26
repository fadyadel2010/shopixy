namespace PointOfSale.API.Configuration;

public sealed class CommerceHubVendorDebtOptions
{
    public const decimal DefaultSuspensionThresholdAmount = 1000m;
    public const int DefaultGracePeriodDays = 3;
    public const decimal DefaultNearThresholdRatio = 0.8m;

    public decimal SuspensionThresholdAmount { get; set; } = DefaultSuspensionThresholdAmount;
    public int GracePeriodDays { get; set; } = DefaultGracePeriodDays;
    public decimal NearThresholdRatio { get; set; } = DefaultNearThresholdRatio;
}
