namespace PointOfSale.Core.Enums;

/// <summary>
/// Redirect behavior for redirect-type domains.
/// Stored as <c>smallint</c> at the persistence layer (DOMAIN-1.5 Section 6).
/// </summary>
public enum RedirectType
{
    /// <summary>
    /// No redirect — the domain serves content directly.
    /// </summary>
    None = 0,

    /// <summary>
    /// Permanent redirect (HTTP 301) to the redirect target domain.
    /// Used for domain consolidation and SEO-preserving redirects.
    /// </summary>
    Permanent301 = 1,

    /// <summary>
    /// Temporary redirect (HTTP 307) to the redirect target domain.
    /// Preserves the original HTTP method and is not cached by browsers.
    /// </summary>
    Temporary307 = 2,
}
