using System.Net.Http;
using PointOfSale.Core.Analytics;
using PointOfSale.Core.Entities;
using PointOfSale.Infrastructure.Analytics.Delivery;

namespace PointOfSale.Infrastructure.Analytics.Dispatchers;

public sealed class GoogleAnalyticsDeliveryRequestCustomizer : IProviderDeliveryHttpRequestCustomizer
{
    private readonly IAnalyticsSettingsService _settingsService;

    public GoogleAnalyticsDeliveryRequestCustomizer(IAnalyticsSettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public string ProviderName => AnalyticsProvider.GoogleAnalytics.ToString();

    public async Task CustomizeAsync(
        ProviderDeliveryOutboxMessage message,
        HttpRequestMessage request,
        CancellationToken cancellationToken = default)
    {
        var settings = await _settingsService.GetSettingsAsync(message.TenantId, cancellationToken);
        if (settings.Google == null ||
            !settings.Google.Enabled ||
            string.IsNullOrWhiteSpace(settings.Google.MeasurementId) ||
            string.IsNullOrWhiteSpace(settings.Google.ApiSecret))
        {
            throw new InvalidOperationException("GA4 credentials are not configured for provider delivery.");
        }

        var separator = request.RequestUri?.Query.Length > 0 ? "&" : "?";
        request.RequestUri = new Uri(
            $"{request.RequestUri}{separator}measurement_id={Uri.EscapeDataString(settings.Google.MeasurementId)}&api_secret={Uri.EscapeDataString(settings.Google.ApiSecret)}");
    }
}
