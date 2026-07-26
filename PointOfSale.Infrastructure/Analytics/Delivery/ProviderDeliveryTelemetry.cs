using System.Diagnostics;

namespace PointOfSale.Infrastructure.Analytics.Delivery;

public static class ProviderDeliveryTelemetry
{
    public const string ActivitySourceName = "PointOfSale.ProviderDelivery";
    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
}
