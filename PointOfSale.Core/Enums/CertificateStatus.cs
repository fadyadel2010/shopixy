namespace PointOfSale.Core.Enums;

/// <summary>
/// SSL/TLS certificate business lifecycle status.
/// Stored as <c>smallint</c> at the persistence layer.
///
/// DOMAIN-5.1 canonical states:
/// Pending, Requested, Authorizing, Issued, Activating, Active, Renewing,
/// Revoked, Expired, Failed.
///
/// Existing numeric values are preserved for migration safety; do not infer
/// lifecycle ordering from the stored integer value.
/// </summary>
public enum CertificateStatus
{
    /// <summary>
    /// Certificate intent is accepted but provider/edge work has not started.
    /// </summary>
    Pending = 0,

    /// <summary>
    /// Certificate is active and serving traffic.
    /// </summary>
    Active = 1,

    /// <summary>
    /// Certificate validity has expired. Terminal for this generation.
    /// </summary>
    Expired = 2,

    /// <summary>
    /// Certificate has been revoked. Terminal for this generation.
    /// </summary>
    Revoked = 3,

    /// <summary>
    /// Certificate operation failed. Retryability is persisted separately.
    /// </summary>
    Failed = 4,

    /// <summary>
    /// Edge/provider certificate management request has been accepted.
    /// </summary>
    Requested = 5,

    /// <summary>
    /// Certificate authorization/challenge is in progress at the edge/provider.
    /// </summary>
    Authorizing = 6,

    /// <summary>
    /// Provider has issued certificate material at the edge; activation proof is pending.
    /// </summary>
    Issued = 7,

    /// <summary>
    /// Shopixy is proving that the edge serves the expected certificate.
    /// </summary>
    Activating = 8,

    /// <summary>
    /// Existing active certificate is being replaced or refreshed.
    /// </summary>
    Renewing = 9,

    /// <summary>
    /// A superseded generation retained as non-authoritative lifecycle evidence.
    /// Terminal for this generation.
    /// </summary>
    Retired = 10,
}
