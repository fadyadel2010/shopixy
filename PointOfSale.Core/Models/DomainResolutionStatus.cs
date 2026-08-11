namespace PointOfSale.Core.Models;

/// <summary>
/// Outcome of a host → domain resolution attempt (DOMAIN-2C).
///
/// This is a runtime-only abstraction — it does not map to any persistence
/// enum or database value. It represents the state of the resolution attempt
/// at the Host Resolution Middleware layer, before any tenant resolution.
///
/// Per DOMAIN-2C scope: the resolver is persistence-backed — queries the
/// Domains table by NormalizedHostName. NotFound is returned when no servable
/// domain matches. Found is returned when a servable domain is located.
/// </summary>
public enum DomainResolutionStatus
{
    /// <summary>
    /// No domain matched the normalized host.
    /// The resolver queried the Domains table and found no servable domain
    /// for the given host. Downstream middleware (TenantResolutionMiddleware)
    /// falls through to JWT-based or slug-based resolution as fallback.
    /// </summary>
    NotFound = 0,

    /// <summary>
    /// A domain was found in the registry for the normalized host.
    /// The persistence-backed resolver (DOMAIN-2C) located a servable
    /// Domain row in the Domains table by NormalizedHostName.
    /// </summary>
    Found = 1,

    /// <summary>
    /// An error occurred during resolution (e.g., database failure).
    /// Per DOMAIN-1.6 Section 3.9, the Host Resolution Middleware does
    /// NOT fail on miss — it falls through. Error state allows logging
    /// without blocking the pipeline.
    /// </summary>
    Error = 2,
}
