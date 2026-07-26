using PointOfSale.Core.Enums;

namespace PointOfSale.Core.Entities;

/// <summary>
/// Represents a tenant (Store/Company/Business) in the multi-tenant SaaS system.
/// This is analogous to a Shopify Store or Odoo Company.
/// Phase 8.0: Extended with Status and lifecycle fields.
/// </summary>
public class Tenant
{
    /// <summary>
    /// Unique identifier for the tenant
    /// </summary>
    public Guid Id { get; set; }
    
    /// <summary>
    /// Display name of the tenant (e.g., "Acme Electronics Store")
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Whether the tenant is active and can perform operations.
    /// Deprecated: Use Status instead. Kept for backwards compatibility.
    /// </summary>
    public bool IsActive { get; set; } = true;
    
    /// <summary>
    /// UTC timestamp when the tenant was created
    /// </summary>
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Unique slug/subdomain identifier for subdomain routing.
    /// Must be globally unique, lowercase, alphanumeric with hyphens.
    /// </summary>
    public string? Slug { get; set; }
    
    /// <summary>
    /// Contact email for the tenant (owner's email)
    /// </summary>
    public string? ContactEmail { get; set; }
    
    // =========================================================================
    // Phase 8.0: Tenant lifecycle fields
    // =========================================================================
    
    /// <summary>
    /// Tenant lifecycle status.
    /// Controls access permissions for POS and Storefront.
    /// </summary>
    public TenantStatus Status { get; set; } = TenantStatus.PendingSetup;
    
    /// <summary>
    /// Country code for the tenant (ISO 3166-1 alpha-2)
    /// </summary>
    public string? Country { get; set; }
    
    /// <summary>
    /// Currency code for the tenant (ISO 4217)
    /// </summary>
    public string Currency { get; set; } = "EGP";
    
    /// <summary>
    /// Reason for suspension (if Status is Suspended)
    /// </summary>
    public string? SuspendedReason { get; set; }
    
    /// <summary>
    /// Timestamp when tenant was soft-deleted
    /// </summary>
    public DateTime? DeletedAtUtc { get; set; }
    
    /// <summary>
    /// Bootstrap schema version - tracks which version was used to create the tenant.
    /// Useful for future migrations.
    /// </summary>
    public int BootstrapVersion { get; set; } = 1;
    
    /// <summary>
    /// Last update timestamp
    /// </summary>
    public DateTime? UpdatedAtUtc { get; set; }
    
    // =========================================================================
    // WhatsApp Baileys Migration Feature Gate
    // =========================================================================
    
    // =========================================================================
    // AF-16D: Storefront URL Authority
    // =========================================================================
    
    /// <summary>
    /// AF-16D: Authoritative storefront base URL for this tenant.
    /// Used to construct promotion links, recovery links, email links, etc.
    /// Example: "http://mena.localhost:5174" or "https://mena.example.com"
    /// If null, computed from Slug + Storefront:UrlTemplate config.
    /// MUST NOT include trailing slash.
    /// </summary>
    public string? StorefrontBaseUrl { get; set; }
    
    /// <summary>
    /// Feature gate for WhatsApp Baileys migration.
    /// When true, tenant uses Baileys transport (lightweight, 20-40 MB RAM per session).
    /// When false, tenant uses Playwright transport (legacy, 300-600 MB RAM per session).
    /// Default: false (Playwright) for backward compatibility.
    /// Phase 4: Backend Integration - Gradual rollout with instant rollback capability.
    /// </summary>
    public bool UseBaileys { get; set; } = false;

    public bool IsOperational => Status == TenantStatus.Active && DeletedAtUtc == null;

    public void SetLifecycleStatus(TenantStatus status, string? suspendedReason = null, DateTime? nowUtc = null)
    {
        var effectiveNow = nowUtc ?? DateTime.UtcNow;

        Status = status;
        IsActive = status == TenantStatus.Active;
        UpdatedAtUtc = effectiveNow;

        if (status == TenantStatus.Suspended)
        {
            SuspendedReason = string.IsNullOrWhiteSpace(suspendedReason)
                ? SuspendedReason
                : suspendedReason.Trim();
        }
        else
        {
            SuspendedReason = null;
        }

        if (status == TenantStatus.Deleted)
        {
            DeletedAtUtc ??= effectiveNow;
        }
        else
        {
            DeletedAtUtc = null;
        }
    }

    public void SyncLifecycleMirror()
    {
        IsActive = Status == TenantStatus.Active && DeletedAtUtc == null;
    }
}

