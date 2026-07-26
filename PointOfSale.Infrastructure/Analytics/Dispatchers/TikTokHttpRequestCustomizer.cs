using System.Net.Http.Headers;
using PointOfSale.Core.Analytics;
using PointOfSale.Core.Entities;
using PointOfSale.Infrastructure.Analytics.Delivery;

namespace PointOfSale.Infrastructure.Analytics.Dispatchers;

public sealed class TikTokHttpRequestCustomizer : IProviderDeliveryHttpRequestCustomizer
{
    private readonly IAnalyticsSettingsService _settingsService;

    public TikTokHttpRequestCustomizer(IAnalyticsSettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public string ProviderName => AnalyticsProvider.TikTok.ToString();

    public async Task CustomizeAsync(
        ProviderDeliveryOutboxMessage message,
        HttpRequestMessage request,
        CancellationToken cancellationToken = default)
    {
        var settings = await _settingsService.GetSettingsAsync(message.TenantId, cancellationToken);
        if (settings?.TikTok == null ||
            !settings.TikTok.Enabled ||
            string.IsNullOrWhiteSpace(settings.TikTok.PixelId) ||
            string.IsNullOrWhiteSpace(settings.TikTok.AccessToken))
        {
            throw new InvalidOperationException("TikTok credentials are not configured for provider delivery.");
        }

        if (!string.IsNullOrWhiteSpace(settings.TikTok.Endpoint))
        {
            request.RequestUri = new Uri(settings.TikTok.Endpoint.Trim());
        }

        request.Headers.TryAddWithoutValidation("Access-Token", settings.TikTok.AccessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (request.Content != null)
        {
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }
    }
}
