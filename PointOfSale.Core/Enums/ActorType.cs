namespace PointOfSale.Core.Enums;

/// <summary>
/// The role of the actor who performed an action recorded in a
/// <see cref="DomainAuditEntry"/>.
/// Stored as <c>smallint</c> at the persistence layer (DOMAIN-1.5 Section 6).
/// </summary>
public enum ActorType
{
    /// <summary>
    /// A merchant/user action — recorded with the acting user's identity.
    /// </summary>
    Merchant = 0,

    /// <summary>
    /// A system/background job action — DNS verification cron, SSL
    /// provisioning job, GC worker, etc. No user identity associated.
    /// </summary>
    System = 1,

    /// <summary>
    /// A platform admin action — elevated-privilege operator.
    /// Recorded with the platform admin's user ID.
    /// </summary>
    PlatformAdmin = 2,
}
