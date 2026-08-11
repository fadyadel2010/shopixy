namespace PointOfSale.Core.Enums;

/// <summary>
/// Classification of a custom domain's role within the storefront platform.
/// Stored as <c>smallint</c> at the persistence layer (DOMAIN-1.5 Section 6).
/// </summary>
public enum DomainType
{
    /// <summary>
    /// The primary, canonical storefront domain for a tenant.
    /// The StorefrontUrlResolver uses this as the authoritative base URL.
    /// </summary>
    Primary = 0,

    /// <summary>
    /// An alias domain — serves the same content as the Primary but under a
    /// different hostname. Redirects to Primary per CanonicalPolicy.
    /// </summary>
    Alias = 1,

    /// <summary>
    /// A redirect-only domain. All requests are 301/307-redirected to the
    /// redirect target. No content is served from this hostname.
    /// </summary>
    Redirect = 2,

    /// <summary>
    /// A preview domain — serves draft theme/customization content.
    /// Used by the storefront builder for content preview before publish.
    /// </summary>
    Preview = 3,

    /// <summary>
    /// A temporary domain — short-lived, used during onboarding or
    /// migration. Not for production traffic.
    /// </summary>
    Temporary = 4,

    /// <summary>
    /// A development domain — used for local/staging environment
    /// previews. Belongs to the creating tenant.
    /// </summary>
    Development = 5,

    /// <summary>
    /// A system domain — platform-managed, belongs to the system tenant.
    /// Exempt from tenant isolation query filters.
    /// </summary>
    System = 6,
}
