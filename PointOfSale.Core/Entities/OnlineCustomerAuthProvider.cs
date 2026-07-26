using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

/// <summary>
/// Links an OnlineCustomer to an external authentication provider (Google, Facebook, etc.)
/// 
/// ARCHITECTURAL RULES:
/// - One customer can have multiple providers
/// - One provider account (ProviderUserId) links to exactly one customer per tenant
/// - Email is the primary identity - providers attach to email
/// - Provider tokens are NEVER stored - only verified once then discarded
/// - PostgreSQL is the source of truth
/// 
/// SECURITY:
/// - ProviderUserId uniqueness prevents provider account takeover
/// - TenantId isolation prevents cross-tenant linking
/// - EmailVerified tracking helps identify trusted providers
/// 
/// Phase: 10.3.3.1 - Social Identity Model
/// </summary>
public class OnlineCustomerAuthProvider : ITenantOwned
{
    /// <summary>
    /// Unique identifier for this provider link.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Tenant this provider link belongs to.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// The OnlineCustomer this provider is linked to.
    /// </summary>
    public Guid OnlineCustomerId { get; set; }

    /// <summary>
    /// The authentication provider.
    /// </summary>
    public AuthProvider Provider { get; set; }

    /// <summary>
    /// The user ID from the provider (e.g., Google 'sub', Facebook 'id').
    /// Unique per provider per tenant.
    /// </summary>
    public string ProviderUserId { get; set; } = null!;

    /// <summary>
    /// Email from the provider (normalized).
    /// May differ from OnlineCustomer.Email if user changed it.
    /// </summary>
    public string ProviderEmail { get; set; } = null!;

    /// <summary>
    /// Whether email was verified by the provider at link time.
    /// </summary>
    public bool EmailVerifiedByProvider { get; set; }

    /// <summary>
    /// Display name from the provider (for reference only).
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Profile picture URL from the provider (optional).
    /// </summary>
    public string? PictureUrl { get; set; }

    /// <summary>
    /// When this provider was first linked.
    /// </summary>
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>
    /// Last successful login using this provider.
    /// </summary>
    public DateTime? LastUsedAtUtc { get; set; }

    // Navigation property
    public OnlineCustomer? OnlineCustomer { get; set; }
}

// Note: AuthProvider enum is defined in OnlineCustomer.cs
