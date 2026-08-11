namespace PointOfSale.Core.Enums;

/// <summary>
/// Status of a DNS/HTTP verification attempt.
/// Stored as <c>smallint</c> at the persistence layer (DOMAIN-1.5 Section 6).
/// </summary>
public enum VerificationStatus
{
    /// <summary>
    /// Verification attempt is pending — waiting for the merchant to
    /// complete the DNS/HTTP configuration.
    /// </summary>
    Pending = 0,

    /// <summary>
    /// Verification attempt succeeded — ownership has been confirmed.
    /// </summary>
    Success = 1,

    /// <summary>
    /// Verification attempt failed — the DNS/HTTP record was not found
    /// or was incorrect.
    /// </summary>
    Failed = 2,
}
