namespace PointOfSale.Core.Enums;

/// <summary>
/// Lifecycle state of a custom domain.
/// Stored as <c>smallint</c> at the persistence layer (DOMAIN-1.5 Section 6).
/// Defines the domain's current position in the lifecycle state machine
/// (DOMAIN-1 Section 5 — 9 states, 15 transitions).
/// </summary>
public enum DomainLifecycleState
{
    /// <summary>
    /// Initial state — domain record created, no verification attempted.
    /// Merchant has submitted the hostname but verification has not started.
    /// </summary>
    Draft = 0,

    /// <summary>
    /// Verification has been initiated — DNS/HTTP validation is in progress.
    /// The domain's <see cref="VerificationToken"/> has been set and the
    /// merchant has been instructed to add the DNS record.
    /// </summary>
    PendingVerification = 1,

    /// <summary>
    /// Verification successful — the merchant has proven ownership of the
    /// hostname. The domain is eligible for SSL provisioning.
    /// </summary>
    Verified = 2,

    /// <summary>
    /// SSL certificate provisioning is in progress.
    /// The domain has been verified but the certificate is not yet active.
    /// </summary>
    SslPending = 3,

    /// <summary>
    /// SSL certificate is active and the domain is serving traffic securely.
    /// </summary>
    SslActive = 4,

    /// <summary>
    /// The domain is the Primary canonical domain for the tenant.
    /// Exactly one Primary domain per tenant is enforced by
    /// <c>UQ_Domains_TenantId_Primary</c> filtered unique index.
    /// </summary>
    Primary = 5,

    /// <summary>
    /// The domain is disabled by a merchant or system action.
    /// Not serving traffic. Can be restored.
    /// </summary>
    Disabled = 6,

    /// <summary>
    /// The domain is archived — soft-deleted.
    /// Can be restored within the 90-day retention window.
    /// </summary>
    Archived = 7,

    /// 
    Deleted = 8,
}
