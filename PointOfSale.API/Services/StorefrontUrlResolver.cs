using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services;

/// <summary>
/// AF-16D: Resolves the authoritative storefront base URL for a tenant.
///
/// RESOLUTION ORDER (first non-null wins):
/// 1. Tenant.StorefrontBaseUrl — custom domain set by admin (e.g., "https://store.example.com")
/// 2. Storefront:UrlTemplate config — slug-based template (e.g., "http://{slug}.localhost:5174")
/// 3. THROW — no silent fallback, no frontend inference
///
/// PRODUCTION HARDENING:
/// - URL normalization: No double-slashes in generated URLs
///
/// ARCHITECTURAL INVARIANTS:
/// - ALL storefront links MUST use this resolver (recovery, product, cart, email, WhatsApp)
/// - Frontend MUST NOT invent or infer storefront URLs
/// - Same slug across tenants → different base URLs (if custom domains differ)
/// - Result never includes trailing slash
///
/// USAGE:
///   var url = resolver.BuildRecoveryUrl(tenant, "abc123");
///   // → "http://mena.localhost:5174/recover-checkout/abc123"
/// </summary>
public class StorefrontUrlResolver
{
    private readonly string? _urlTemplate;
    private readonly ILogger<StorefrontUrlResolver> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    public StorefrontUrlResolver(
        IConfiguration configuration,
        ILogger<StorefrontUrlResolver> logger,
        IServiceScopeFactory scopeFactory)
    {
        _urlTemplate = configuration["Storefront:UrlTemplate"];
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    // =========================================================================
    // Base URL Resolution
    // =========================================================================

    /// <summary>
    /// Resolve the storefront base URL for a tenant entity.
    /// Returns a URL like "http://mena.localhost:5174" (no trailing slash).
    /// </summary>
    public string Resolve(Tenant tenant)
    {
        ArgumentNullException.ThrowIfNull(tenant);

        // Priority 1: Tenant-specific custom domain
        if (!string.IsNullOrWhiteSpace(tenant.StorefrontBaseUrl))
        {
            return ValidateAndNormalizeBaseUrl(tenant.StorefrontBaseUrl);
        }

        // Priority 2: Config template with slug substitution
        if (!string.IsNullOrWhiteSpace(_urlTemplate) && !string.IsNullOrWhiteSpace(tenant.Slug))
        {
            var url = _urlTemplate.Replace("{slug}", tenant.Slug);
            return ValidateAndNormalizeBaseUrl(url);
        }

        _logger.LogError(
            "[AF-16D] Cannot resolve storefront URL | TenantId: {TenantId} | Slug: {Slug} | " +
            "StorefrontBaseUrl: {StorefrontBaseUrl} | UrlTemplate: {UrlTemplate}",
            tenant.Id, tenant.Slug, tenant.StorefrontBaseUrl, _urlTemplate);

        throw new InvalidOperationException(
            $"Cannot resolve storefront URL for tenant {tenant.Id}. " +
            "Either set Tenant.StorefrontBaseUrl or configure Storefront:UrlTemplate with a valid {slug} template.");
    }

    /// <summary>
    /// Resolve the storefront base URL by tenant ID.
    /// Reads the current tenant authority.
    /// </summary>
    public async Task<string> ResolveByTenantIdAsync(Guid tenantId)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var tenant = await dbContext.Tenants
            .AsNoTracking()
            .SingleOrDefaultAsync(t =>
                t.Id == tenantId &&
                t.IsActive &&
                t.Status == TenantStatus.Active &&
                t.DeletedAtUtc == null);

        if (tenant == null)
            throw new InvalidOperationException($"Tenant {tenantId} not found.");

        return Resolve(tenant);
    }

    // =========================================================================
    // URL Builders — centralized, backend-only
    // =========================================================================

    /// <summary>
    /// Build checkout recovery URL.
    /// Example: "http://mena.localhost:5174/recover-checkout/abc123"
    /// </summary>
    public string BuildRecoveryUrl(Tenant tenant, string recoveryToken)
    {
        var baseUrl = Resolve(tenant);
        return CombineUrl(baseUrl, "recover-checkout", recoveryToken);
    }

    /// <summary>
    /// Build checkout recovery URL by tenant ID using the current SQL tenant record.
    /// </summary>
    public async Task<string> BuildRecoveryUrlAsync(Guid tenantId, string recoveryToken)
    {
        var baseUrl = await ResolveByTenantIdAsync(tenantId);
        return CombineUrl(baseUrl, "recover-checkout", recoveryToken);
    }

    /// <summary>
    /// Build a product detail page URL.
    /// Example: "http://mena.localhost:5174/products/blue-sneakers"
    /// </summary>
    public string BuildProductUrl(Tenant tenant, string productSlug)
    {
        var baseUrl = Resolve(tenant);
        return CombineUrl(baseUrl, "products", productSlug);
    }

    /// <summary>
    /// Build a cart page URL.
    /// Example: "http://mena.localhost:5174/cart"
    /// </summary>
    public string BuildCartUrl(Tenant tenant)
    {
        var baseUrl = Resolve(tenant);
        return CombineUrl(baseUrl, "cart");
    }

    /// <summary>
    /// Build a full promotion link URL for a product with affiliate ref.
    /// Example: "http://mena.localhost:5174/products/blue-sneakers?ref=JOHN10"
    /// </summary>
    public string BuildPromotionLink(Tenant tenant, string productSlug, string referralCode)
    {
        var baseUrl = Resolve(tenant);
        return $"{CombineUrl(baseUrl, "products", productSlug)}?ref={Uri.EscapeDataString(referralCode)}";
    }

    // =========================================================================
    // URL Normalization — never produce double slashes
    // =========================================================================

    /// <summary>
    /// Normalize a base URL: trim trailing slashes, validate format.
    /// </summary>
    private static string ValidateAndNormalizeBaseUrl(string url)
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

    /// <summary>
    /// Safely combine base URL with path segments.
    /// Prevents double-slash issues regardless of input format.
    /// Example: CombineUrl("http://foo.com/", "/recover-checkout", "abc") → "http://foo.com/recover-checkout/abc"
    /// </summary>
    private static string CombineUrl(string baseUrl, params string[] segments)
    {
        var normalized = ValidateAndNormalizeBaseUrl(baseUrl);
        foreach (var segment in segments)
        {
            var clean = segment.Trim('/');
            if (!string.IsNullOrEmpty(clean))
                normalized = $"{normalized}/{clean}";
        }
        return normalized;
    }
}
