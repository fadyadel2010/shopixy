using Microsoft.Extensions.Logging;
using PointOfSale.Core.Analytics;
using PointOfSale.Core.Analytics.Attribution;

namespace PointOfSale.Infrastructure.Attribution;

/// <summary>
/// Attribution Context Resolver - Extracts attribution data from PageView events.
/// 
/// FLOW:
/// 1. PageView event received
/// 2. Extract UTM params from PageUrl
/// 3. If UTM params present OR first PageView for session, create AttributionContext
/// 4. Store in AttributionContextStore
/// 
/// UTM PARAMETER EXTRACTION:
/// - utm_source → Source
/// - utm_medium → Medium
/// - utm_campaign → Campaign
/// - utm_content → Content
/// - utm_term → Term
/// 
/// DEFAULT VALUES:
/// - No UTM + No Referrer → Source: direct, Medium: none
/// - No UTM + Referrer → Source: inferred, Medium: referral
/// </summary>
public interface IAttributionContextResolver
{
    /// <summary>
    /// Resolves attribution context from a PageView event.
    /// Returns null if the event doesn't contain attribution data worth storing.
    /// </summary>
    Task<AttributionContext?> ResolveFromEventAsync(
        AnalyticsEvent analyticsEvent,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Implementation of AttributionContextResolver.
/// </summary>
public class AttributionContextResolver : IAttributionContextResolver
{
    private readonly IAttributionContextStore _store;
    private readonly ILogger<AttributionContextResolver> _logger;

    // Known search engines for organic traffic detection
    private static readonly HashSet<string> SearchEngines = new(StringComparer.OrdinalIgnoreCase)
    {
        "google", "bing", "yahoo", "duckduckgo", "baidu", "yandex", "ecosia"
    };

    // Known social networks for social traffic detection
    private static readonly HashSet<string> SocialNetworks = new(StringComparer.OrdinalIgnoreCase)
    {
        "facebook", "instagram", "twitter", "linkedin", "pinterest", "tiktok", "youtube", "reddit", "snapchat"
    };

    public AttributionContextResolver(
        IAttributionContextStore store,
        ILogger<AttributionContextResolver> logger)
    {
        _store = store;
        _logger = logger;
    }

    public async Task<AttributionContext?> ResolveFromEventAsync(
        AnalyticsEvent analyticsEvent,
        CancellationToken cancellationToken = default)
    {
        // Only process PageView events for attribution
        if (analyticsEvent.Type != AnalyticsEventType.PageView)
        {
            return null;
        }

        try
        {
            var pageUrl = analyticsEvent.Context?.PageUrl;
            if (string.IsNullOrWhiteSpace(pageUrl))
            {
                return null;
            }

            var sessionId = analyticsEvent.Actor?.SessionId;
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                _logger.LogWarning("[Attribution] PageView without SessionId | TenantId: {TenantId}",
                    analyticsEvent.TenantId);
                return null;
            }

            var referrer = analyticsEvent.Context?.Referrer;

            // Determine source and medium
            string source, medium;
            string? campaign = analyticsEvent.Context?.UtmCampaign;
            string? content = analyticsEvent.Context?.UtmContent;
            string? term = analyticsEvent.Context?.UtmTerm;

            if (!string.IsNullOrWhiteSpace(analyticsEvent.Context?.UtmSource))
            {
                source = analyticsEvent.Context.UtmSource;
                medium = analyticsEvent.Context.UtmMedium ?? "none";
            }
            else if (!string.IsNullOrWhiteSpace(referrer))
            {
                // Infer from referrer
                (source, medium) = InferFromReferrer(referrer);
            }
            else
            {
                // Direct traffic
                source = "direct";
                medium = "none";
            }

            // Check if we should store this context
            // Always store if:
            // 1. Has UTM parameters
            // 2. First PageView for session (direct traffic baseline)
            // 3. Source changed (e.g., user came back via different channel)

            var hasUtm = !string.IsNullOrEmpty(campaign) ||
                         (source != "direct" && medium != "none");

            var existingFirst = await _store.GetFirstTouchContextAsync(
                analyticsEvent.TenantId, sessionId, cancellationToken);

            var shouldStore = hasUtm ||
                              existingFirst == null ||
                              (existingFirst.Source != source && source != "direct");

            if (!shouldStore)
            {
                return null;
            }

            // Create and store attribution context
            var context = AttributionContext.FromUtmParams(
                analyticsEvent.TenantId,
                sessionId,
                source,
                medium,
                campaign,
                content,
                term,
                referrer,
                pageUrl);

            context.CustomerId = analyticsEvent.Actor?.CustomerId;
            context.CapturedAtUtc = analyticsEvent.OccurredAtUtc;

            await _store.StoreContextAsync(context, cancellationToken);

            _logger.LogDebug(
                "[Attribution] Context resolved | TenantId: {TenantId} | SessionId: {SessionId} | Source: {Source}/{Medium} | Campaign: {Campaign}",
                analyticsEvent.TenantId, sessionId, source, medium, campaign ?? "(none)");

            return context;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[Attribution] Failed to resolve context | TenantId: {TenantId} | EventId: {EventId}",
                analyticsEvent.TenantId, analyticsEvent.EventId);
            return null;
        }
    }

    /// <summary>
    /// Infers source and medium from referrer URL.
    /// </summary>
    private static (string source, string medium) InferFromReferrer(string referrer)
    {
        try
        {
            var uri = new Uri(referrer);
            var host = uri.Host.ToLowerInvariant();

            // Remove www. prefix
            if (host.StartsWith("www."))
            {
                host = host.Substring(4);
            }

            // Check for search engines
            foreach (var engine in SearchEngines)
            {
                if (host.Contains(engine))
                {
                    return (engine, "organic");
                }
            }

            // Check for social networks
            foreach (var network in SocialNetworks)
            {
                if (host.Contains(network))
                {
                    return (network, "social");
                }
            }

            // Generic referral
            var domain = host.Split('.').FirstOrDefault() ?? host;
            return (domain, "referral");
        }
        catch
        {
            return ("unknown", "referral");
        }
    }
}
