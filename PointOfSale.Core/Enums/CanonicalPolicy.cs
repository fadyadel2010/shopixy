namespace PointOfSale.Core.Enums;

/// <summary>
/// Canonical URL policy for alias domains — determines how the platform
/// handles SEO canonicalization for non-Primary domains.
/// Stored as <c>smallint</c> at the persistence layer (DOMAIN-1.5 Section 6).
/// </summary>
public enum CanonicalPolicy
{
    /// <summary>
    /// Use this domain AS the primary canonical URL.
    /// This domain will be the canonical source of truth.
    /// </summary>
    UseAsPrimary = 0,

    /// <summary>
    /// Canonicalize to the tenant's Primary domain.
    /// This domain's pages will have <code>rel="canonical"</code>
    /// pointing to the Primary domain's equivalent URL.
    /// </summary>
    CanonicalizeToPrimary = 1,
}
