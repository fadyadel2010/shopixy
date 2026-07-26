using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

/// <summary>
/// Stores social login provider settings for a tenant's Storefront.
/// 
/// ARCHITECTURAL RULES:
/// - POS is the ONLY admin - tenants configure this from POS
/// - Disabled provider = login attempts rejected
/// - Client secrets should be encrypted at rest
/// - Changes are live (no restart required)
/// 
/// SECURITY:
/// - ClientSecret is encrypted in database
/// - Secrets never returned to frontend
/// - Only POS admins can modify
/// 
/// Phase: 10.3.3.2 - Provider Configuration (POS Managed)
/// </summary>
public class StorefrontAuthProviderSettings : ITenantOwned
{
    /// <summary>
    /// Unique identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Tenant this configuration belongs to.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// The authentication provider this configuration is for.
    /// </summary>
    public AuthProvider Provider { get; set; }

    /// <summary>
    /// Whether this provider is enabled for the storefront.
    /// Disabled = all login attempts rejected.
    /// </summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// OAuth Client ID from the provider.
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// OAuth Client Secret (encrypted at rest).
    /// SECURITY: Never return this to frontend.
    /// </summary>
    public string? ClientSecretEncrypted { get; set; }

    /// <summary>
    /// Optional: Additional scopes to request.
    /// </summary>
    public string? AdditionalScopes { get; set; }

    /// <summary>
    /// When this configuration was created.
    /// </summary>
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>
    /// When this configuration was last updated.
    /// </summary>
    public DateTime? UpdatedAtUtc { get; set; }

    /// <summary>
    /// Who last updated this configuration.
    /// </summary>
    public Guid? UpdatedByUserId { get; set; }
}
