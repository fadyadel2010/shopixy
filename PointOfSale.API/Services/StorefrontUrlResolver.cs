using Microsoft.EntityFrameworkCore;
using System.Diagnostics.CodeAnalysis;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Tenancy;
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
            return StorefrontUrlAuthority.NormalizeOrigin(tenant.StorefrontBaseUrl);
        }

        // Priority 2: Config template with slug substitution
        if (!string.IsNullOrWhiteSpace(_urlTemplate) && !string.IsNullOrWhiteSpace(tenant.Slug))
        {
            return StorefrontUrlAuthority.ResolveOrigin(null, tenant.Slug, _urlTemplate);
        }

        _logger.LogError(
            "[AF-16D] Cannot resolve storefront URL | TenantId: {TenantId} | Slug: {Slug} | " +
            "StorefrontBaseUrl: {StorefrontBaseUrl} | UrlTemplate: {UrlTemplate}",
            tenant.Id, tenant.Slug, tenant.StorefrontBaseUrl, _urlTemplate);

        throw new InvalidOperationException(
            $"Cannot resolve storefront URL for tenant {tenant.Id}. " +
            "Either set Tenant.StorefrontBaseUrl or configure Storefront:UrlTemplate with a valid {slug} template.");
    }

    public bool TryResolve(Tenant tenant, [NotNullWhen(true)] out string? storefrontUrl)
    {
        try
        {
            storefrontUrl = Resolve(tenant);
            return true;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(
                ex,
                "Storefront URL authority is unavailable | TenantId: {TenantId} | Slug: {Slug}",
                tenant.Id,
                tenant.Slug);
            storefrontUrl = null;
            return false;
        }
    }

    /// <summary>
    /// Resolve the storefront base URL by tenant ID.
    /// Reads the current tenant authority.
    /// </summary>
    public async Task<string> ResolveByTenantIdAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var tenant = await dbContext.Tenants
            .AsNoTracking()
            .SingleOrDefaultAsync(t =>
                t.Id == tenantId &&
                t.IsActive &&
                t.Status == TenantStatus.Active &&
                t.DeletedAtUtc == null,
                cancellationToken);

        if (tenant == null)
            throw new InvalidOperationException($"Tenant {tenantId} not found.");

        return Resolve(tenant);
    }

    public async Task<string> BuildThemePreviewUrlAsync(
        Guid tenantId,
        string previewToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(previewToken);
        var baseUrl = await ResolveByTenantIdAsync(tenantId, cancellationToken);
        return $"{baseUrl}?themePreviewToken={Uri.EscapeDataString(previewToken)}";
    }

    public async Task<string> BuildCmsPagePreviewUrlAsync(
        Guid tenantId,
        string pageSlug,
        string previewToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pageSlug);
        ArgumentException.ThrowIfNullOrWhiteSpace(previewToken);
        var baseUrl = await ResolveByTenantIdAsync(tenantId, cancellationToken);
        return $"{baseUrl}/{Uri.EscapeDataString(pageSlug.Trim())}" +
               $"?tenant={tenantId:D}&preview=true&previewToken={Uri.EscapeDataString(previewToken)}";
    }

    public async Task<string> BuildEmailVerificationUrlAsync(
        Guid tenantId,
        string verificationToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(verificationToken);
        var baseUrl = await ResolveByTenantIdAsync(tenantId, cancellationToken);
        return $"{baseUrl}/verify-email?token={Uri.EscapeDataString(verificationToken)}&tenant={tenantId:D}";
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
        return StorefrontUrlAuthority.BuildUrl(baseUrl, "recover-checkout", recoveryToken);
    }

    /// <summary>
    /// Build checkout recovery URL by tenant ID using the current SQL tenant record.
    /// </summary>
    public async Task<string> BuildRecoveryUrlAsync(Guid tenantId, string recoveryToken)
    {
        var baseUrl = await ResolveByTenantIdAsync(tenantId);
        return StorefrontUrlAuthority.BuildUrl(baseUrl, "recover-checkout", recoveryToken);
    }

    /// <summary>
    /// Build a product detail page URL.
    /// Example: "http://mena.localhost:5174/products/blue-sneakers"
    /// </summary>
    public string BuildProductUrl(Tenant tenant, string productSlug)
    {
        var baseUrl = Resolve(tenant);
        return StorefrontUrlAuthority.BuildUrl(baseUrl, "products", productSlug);
    }

    /// <summary>
    /// Build a cart page URL.
    /// Example: "http://mena.localhost:5174/cart"
    /// </summary>
    public string BuildCartUrl(Tenant tenant)
    {
        var baseUrl = Resolve(tenant);
        return StorefrontUrlAuthority.BuildUrl(baseUrl, "cart");
    }

    /// <summary>
    /// Build a full promotion link URL for a product with affiliate ref.
    /// Example: "http://mena.localhost:5174/products/blue-sneakers?ref=JOHN10"
    /// </summary>
    public string BuildPromotionLink(Tenant tenant, string productSlug, string referralCode)
    {
        var baseUrl = Resolve(tenant);
        return $"{StorefrontUrlAuthority.BuildUrl(baseUrl, "products", productSlug)}?ref={Uri.EscapeDataString(referralCode)}";
    }
}
