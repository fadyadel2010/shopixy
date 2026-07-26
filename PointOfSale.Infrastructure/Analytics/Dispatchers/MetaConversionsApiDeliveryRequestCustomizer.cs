using System.Net.Http.Headers;
using PointOfSale.Core.Analytics;
using PointOfSale.Core.Entities;
using PointOfSale.Infrastructure.Analytics.Delivery;

namespace PointOfSale.Infrastructure.Analytics.Dispatchers;

public sealed class MetaConversionsApiDeliveryRequestCustomizer : IProviderDeliveryHttpRequestCustomizer
{
    private readonly IAnalyticsSettingsService _settingsService;

    public MetaConversionsApiDeliveryRequestCustomizer(IAnalyticsSettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public string ProviderName => AnalyticsProvider.Meta.ToString();

    public async Task CustomizeAsync(
        ProviderDeliveryOutboxMessage message,
        HttpRequestMessage request,
        CancellationToken cancellationToken = default)
    {
        var settings = await _settingsService.GetSettingsAsync(message.TenantId, cancellationToken);
        if (settings.Meta == null ||
            !settings.Meta.Enabled ||
            string.IsNullOrWhiteSpace(settings.Meta.AccessToken))
        {
            throw new InvalidOperationException("Meta CAPI credentials are not configured for provider delivery.");
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.Meta.AccessToken);
    }
}
