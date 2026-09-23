namespace PointOfSale.Core.Tenancy;

public static class StorefrontUrlAuthority
{
    public static string ResolveOrigin(string? storefrontBaseUrl, string? tenantSlug, string? urlTemplate)
    {
        if (!string.IsNullOrWhiteSpace(storefrontBaseUrl))
        {
            return NormalizeOrigin(storefrontBaseUrl);
        }

        if (!string.IsNullOrWhiteSpace(urlTemplate) && !string.IsNullOrWhiteSpace(tenantSlug))
        {
            if (!urlTemplate.Contains("{slug}", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Storefront URL template must contain the {slug} placeholder.");
            }

            return NormalizeOrigin(urlTemplate.Replace("{slug}", tenantSlug, StringComparison.OrdinalIgnoreCase));
        }

        throw new InvalidOperationException(
            "Storefront URL authority requires either a tenant base URL or a slug URL template.");
    }

    public static string NormalizeOrigin(string url)
    {
        var normalized = url.Trim().TrimEnd('/');
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) ||
            string.IsNullOrWhiteSpace(uri.Host) ||
            !string.IsNullOrWhiteSpace(uri.UserInfo) ||
            uri.AbsolutePath != "/" ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new InvalidOperationException("Storefront base URL authority must be an absolute HTTP(S) origin.");
        }

        return uri.GetLeftPart(UriPartial.Authority);
    }

    public static string BuildUrl(string origin, params string[] segments)
    {
        var url = NormalizeOrigin(origin);
        foreach (var segment in segments)
        {
            var value = segment.Trim('/');
            if (!string.IsNullOrEmpty(value))
            {
                url = $"{url}/{Uri.EscapeDataString(value)}";
            }
        }

        return url;
    }
}
