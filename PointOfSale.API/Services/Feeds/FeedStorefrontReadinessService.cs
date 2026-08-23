using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace PointOfSale.API.Services.Feeds;

/// <summary>
/// Environment-aware feed storefront readiness. Delegates to the production-safe
/// static <see cref="FeedPublicUrlReadiness"/> checks first, then applies a
/// Development-only exception for the configured local storefront template
/// (e.g. <c>http://{slug}.localhost:5174</c>).
///
/// PRODUCTION SAFETY: the dev exception never fires outside the Development
/// environment, and only for URLs that structurally match the
/// <c>Storefront:UrlTemplate</c>. There is no blanket <c>if (Development) return true</c>.
/// <see cref="IsPublicImageUrl"/> remains strict for all environments.
/// </summary>
public interface IFeedStorefrontReadinessService
{
    /// <summary>
    /// Strict for production; allows the configured local storefront base URL
    /// only when running in the Development environment.
    /// </summary>
    bool IsPublicStorefrontBaseUrl(string? baseUrl, out string reasonCode);

    /// <summary>
    /// Strict for production; allows a local product route under the configured
    /// storefront template only when running in the Development environment.
    /// </summary>
    bool IsPublicProductUrl(
        string? productUrl,
        string expectedStorefrontBaseUrl,
        string productSlug,
        out string reasonCode);
}

public sealed class FeedStorefrontReadinessService : IFeedStorefrontReadinessService
{
    // A valid single DNS label substituted into the template to parse scheme/port/host.
    private const string SlugPlaceholder = "shopixyslug";
    private const string SlugLabelPattern = "[a-z0-9](?:[a-z0-9-]*[a-z0-9])?";

    private readonly IWebHostEnvironment _environment;
    private readonly DevStorefrontPattern? _devPattern;

    public FeedStorefrontReadinessService(IWebHostEnvironment environment, IConfiguration configuration)
    {
        _environment = environment;
        _devPattern = ParseDevPattern(configuration?["Storefront:UrlTemplate"]);
    }

    public bool IsPublicStorefrontBaseUrl(string? baseUrl, out string reasonCode)
    {
        // Production-safe strict path first. Unchanged static behavior for HTTPS public URLs.
        if (FeedPublicUrlReadiness.IsPublicStorefrontBaseUrl(baseUrl, out reasonCode))
        {
            return true;
        }

        // Development-only exception: explicit environment gate + template match.
        if (IsDevelopmentLocalStorefrontBase(baseUrl, out _))
        {
            return true;
        }

        reasonCode = FeedPublicUrlReadiness.NonPublicStorefrontUrl;
        return false;
    }

    public bool IsPublicProductUrl(
        string? productUrl,
        string expectedStorefrontBaseUrl,
        string productSlug,
        out string reasonCode)
    {
        // Production-safe strict path first.
        if (FeedPublicUrlReadiness.IsPublicProductUrl(productUrl, expectedStorefrontBaseUrl, productSlug, out reasonCode))
        {
            return true;
        }

        // Development-only exception: base must match the local template AND the
        // product URL must resolve to /products/{slug} under that same origin.
        if (IsDevelopmentLocalProductUrl(productUrl, expectedStorefrontBaseUrl, productSlug))
        {
            return true;
        }

        reasonCode = FeedPublicUrlReadiness.InvalidProductUrl;
        return false;
    }

    private bool IsDevelopmentLocalStorefrontBase(string? baseUrl, out Uri? baseUri)
    {
        baseUri = null;

        if (!_environment.IsDevelopment() || _devPattern is null)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return false;
        }

        if (!Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var uri) ||
            string.IsNullOrWhiteSpace(uri.Host) ||
            !string.IsNullOrWhiteSpace(uri.UserInfo))
        {
            return false;
        }

        // Scheme + port must match the configured template (no arbitrary localhost ports).
        if (!string.Equals(uri.Scheme, _devPattern.Scheme, StringComparison.OrdinalIgnoreCase) ||
            uri.Port != _devPattern.Port)
        {
            return false;
        }

        // Base URL must be origin-only (no path/query/fragment).
        if (uri.AbsolutePath != "/" ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            return false;
        }

        // Host must match the template host pattern: <label>.localhost (rejects
        // 127.0.0.1, ::1, localhost.evil.example, bare localhost, private hosts).
        if (!_devPattern.HostRegex.IsMatch(uri.Host))
        {
            return false;
        }

        baseUri = uri;
        return true;
    }

    private bool IsDevelopmentLocalProductUrl(string? productUrl, string expectedStorefrontBaseUrl, string productSlug)
    {
        // Slug validation mirrors the strict rules exactly.
        if (string.IsNullOrWhiteSpace(productSlug) ||
            productSlug.Any(char.IsControl) ||
            productSlug.Any(char.IsWhiteSpace) ||
            productSlug.Contains('/') ||
            productSlug.Contains('\\') ||
            productSlug.Contains('?') ||
            productSlug.Contains('#'))
        {
            return false;
        }

        // Expected base must be a dev-local storefront matching the configured template.
        if (!IsDevelopmentLocalStorefrontBase(expectedStorefrontBaseUrl, out var baseUri))
        {
            return false;
        }

        // Product URL must be an absolute URI with no user info.
        if (string.IsNullOrWhiteSpace(productUrl) ||
            !Uri.TryCreate(productUrl.Trim(), UriKind.Absolute, out var uri) ||
            string.IsNullOrWhiteSpace(uri.Host) ||
            !string.IsNullOrWhiteSpace(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            return false;
        }

        // Origin must match the dev base origin exactly (scheme + host + port).
        if (!string.Equals(uri.Scheme, baseUri!.Scheme, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(uri.Host, baseUri.Host, StringComparison.OrdinalIgnoreCase) ||
            uri.Port != baseUri.Port)
        {
            return false;
        }

        var decodedPath = Uri.UnescapeDataString(uri.AbsolutePath).TrimEnd('/');
        return string.Equals(decodedPath, $"/products/{productSlug.Trim()}", StringComparison.Ordinal);
    }

    private static DevStorefrontPattern? ParseDevPattern(string? template)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return null;
        }

        if (template.IndexOf("{slug}", StringComparison.OrdinalIgnoreCase) < 0)
        {
            return null;
        }

        var withPlaceholder = template.Replace("{slug}", SlugPlaceholder, StringComparison.OrdinalIgnoreCase);
        if (!Uri.TryCreate(withPlaceholder, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrWhiteSpace(uri.Host) ||
            !string.IsNullOrWhiteSpace(uri.UserInfo))
        {
            return null;
        }

        var host = uri.Host;
        var placeholderIndex = host.IndexOf(SlugPlaceholder, StringComparison.Ordinal);
        if (placeholderIndex < 0)
        {
            return null;
        }

        var prefix = host[..placeholderIndex];
        var suffix = host[(placeholderIndex + SlugPlaceholder.Length)..];
        var pattern = $"^{Regex.Escape(prefix)}{SlugLabelPattern}{Regex.Escape(suffix)}$";

        return new DevStorefrontPattern(
            uri.Scheme,
            new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled),
            uri.Port);
    }

    private sealed record DevStorefrontPattern(string Scheme, Regex HostRegex, int Port);
}

/// <summary>
/// Production-safe default that delegates to the strict static readiness checks.
/// Used when no environment-aware service is injected (e.g. direct unit test
/// construction). Behavior is always strict regardless of environment.
/// </summary>
internal sealed class StrictFeedStorefrontReadinessService : IFeedStorefrontReadinessService
{
    public static readonly StrictFeedStorefrontReadinessService Instance = new();

    public bool IsPublicStorefrontBaseUrl(string? baseUrl, out string reasonCode) =>
        FeedPublicUrlReadiness.IsPublicStorefrontBaseUrl(baseUrl, out reasonCode);

    public bool IsPublicProductUrl(
        string? productUrl,
        string expectedStorefrontBaseUrl,
        string productSlug,
        out string reasonCode) =>
        FeedPublicUrlReadiness.IsPublicProductUrl(productUrl, expectedStorefrontBaseUrl, productSlug, out reasonCode);
}
