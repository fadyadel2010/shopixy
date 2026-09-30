namespace PointOfSale.Core.Entities;

/// <summary>
/// Tenant-level automation configuration for e-commerce marketing features.
/// Phase: Smart Abandoned Cart Recovery Engine - Hardening
/// 
/// ARCHITECTURAL PRINCIPLES:
/// - One record per tenant (TenantId is unique)
/// - Admin-controlled via POS Admin UI
/// - Feature-gated: automation only runs if explicitly enabled
/// - Safe defaults: all automation disabled by default
/// - Deterministic: clear rules for when automation triggers
/// - Observable: all settings auditable
/// 
/// ZERO-REGRESSION GUARANTEE:
/// - Does NOT affect existing checkout flow
/// - Does NOT send messages (Phase 1 is foundation only)
/// - Does NOT introduce background jobs yet
/// </summary>
public class TenantAutomationSettings : TenantEntity
{
    /// <summary>
    /// Whether abandoned cart recovery automation is enabled.
    /// CRITICAL: Default is FALSE - must be explicitly enabled by admin.
    /// When disabled, no abandoned cart messages will be sent.
    /// </summary>
    public bool EnableAbandonedRecovery { get; set; } = false;
    
    /// <summary>
    /// Delay in minutes before sending the first abandoned cart message.
    /// Recommended range: 60-1440 minutes (1 hour to 24 hours)
    /// Default: 60 minutes (1 hour)
    /// 
    /// WHY: Gives customer time to complete checkout naturally.
    /// Too short = annoying, too long = lost opportunity.
    /// </summary>
    public int FirstMessageDelayMinutes { get; set; } = 60;
    
    /// <summary>
    /// Delay in minutes before sending the second follow-up message.
    /// Measured from the first message send time.
    /// Recommended range: 1440-4320 minutes (24 hours to 3 days)
    /// Default: 1440 minutes (24 hours)
    /// 
    /// WHY: Gentle reminder without being pushy.
    /// Set to 0 to disable second message.
    /// </summary>
    public int SecondMessageDelayMinutes { get; set; } = 1440;
    
    /// <summary>
    /// Start of working hours (24-hour format, tenant's timezone).
    /// Messages will only be sent during working hours.
    /// Default: 09:00 (9 AM)
    /// 
    /// WHY: Respect customer's time - don't message at 3 AM.
    /// </summary>
    public TimeSpan WorkingHoursFrom { get; set; } = new TimeSpan(9, 0, 0);
    
    /// <summary>
    /// End of working hours (24-hour format, tenant's timezone).
    /// Messages will only be sent during working hours.
    /// Default: 21:00 (9 PM)
    /// 
    /// WHY: Respect customer's time - don't message late at night.
    /// </summary>
    public TimeSpan WorkingHoursTo { get; set; } = new TimeSpan(21, 0, 0);

    /// <summary>
    /// Authoritative IANA time zone used for recovery sending windows.
    /// </summary>
    public string TimeZoneId { get; set; } = "UTC";
    
    /// <summary>
    /// When these settings were last updated.
    /// Used for audit trail and cache invalidation.
    /// </summary>
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// When these settings were created.
    /// </summary>
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    
    // Navigation
    public virtual Tenant Tenant { get; set; } = null!;
}
