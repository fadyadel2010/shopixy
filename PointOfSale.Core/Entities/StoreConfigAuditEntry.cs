using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

/// <summary>
/// Audit entry for store configuration changes.
/// Phase: Storefront Admin Hardening - P3
/// 
/// Tracks all publish/rollback/destructive operations for:
/// - Compliance and regulatory requirements
/// - Support troubleshooting
/// - Change history visibility
/// </summary>
public class StoreConfigAuditEntry : ITenantOwned
{
    public Guid Id { get; set; }
    
    /// <summary>
    /// Tenant that owns this audit entry.
    /// </summary>
    public Guid TenantId { get; set; }
    
    /// <summary>
    /// User who performed the action (if available).
    /// May be null for system-initiated actions.
    /// </summary>
    public Guid? UserId { get; set; }
    
    /// <summary>
    /// Username or email of the acting user (denormalized for audit readability).
    /// </summary>
    public string? UserName { get; set; }
    
    /// <summary>
    /// Type of action performed.
    /// Values: CMS_PAGE_PUBLISHED, THEME_PUBLISHED, NAVIGATION_PUBLISHED, ROLLBACK, etc.
    /// </summary>
    public string ActionType { get; set; } = string.Empty;
    
    /// <summary>
    /// Target of the action (e.g., page slug, "theme", "navigation").
    /// </summary>
    public string Target { get; set; } = string.Empty;
    
    /// <summary>
    /// JSON snapshot of the state BEFORE the action.
    /// For rollbacks, this is the state being rolled back FROM.
    /// </summary>
    public string? BeforeSnapshotJson { get; set; }
    
    /// <summary>
    /// JSON snapshot of the state AFTER the action.
    /// For rollbacks, this is the state being rolled back TO.
    /// </summary>
    public string? AfterSnapshotJson { get; set; }
    
    /// <summary>
    /// Version number after the action (if applicable).
    /// </summary>
    public int? Version { get; set; }
    
    /// <summary>
    /// Correlation ID for request tracing.
    /// </summary>
    public string? TraceId { get; set; }
    
    /// <summary>
    /// IP address of the request (for security auditing).
    /// </summary>
    public string? IpAddress { get; set; }
    
    /// <summary>
    /// User agent of the request (for debugging).
    /// </summary>
    public string? UserAgent { get; set; }
    
    /// <summary>
    /// When the action was performed (UTC).
    /// </summary>
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Known audit action types for type safety.
/// </summary>
public static class AuditActionTypes
{
    public const string PagePublished = "PAGE_PUBLISHED";
    
    [Obsolete("Use PagePublished instead. This constant is deprecated to unify analytics.")]
    public const string CmsPagePublished = PagePublished;
    
    public const string CmsPageDeleted = "CMS_PAGE_DELETED";
    public const string ThemePublished = "THEME_PUBLISHED";
    public const string NavigationPublished = "NAVIGATION_PUBLISHED";
    public const string Rollback = "ROLLBACK";
    public const string StoreConfigUpdated = "STORE_CONFIG_UPDATED";
}
