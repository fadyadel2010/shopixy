namespace PointOfSale.Core.Enums;

/// <summary>
/// DNS/HTTP verification method used to prove domain ownership.
/// Stored as <c>smallint</c> at the persistence layer (DOMAIN-1.5 Section 6).
/// </summary>
public enum VerificationMethod
{
    /// <summary>
    /// DNS TXT record verification — merchant adds a TXT record to their
    /// DNS zone with the verification token.
    /// </summary>
    DnsTxt = 0,

    /// <summary>
    /// DNS CNAME record verification — merchant adds a CNAME record
    /// pointing to the platform's verification endpoint.
    /// </summary>
    DnsCname = 1,

    /// <summary>
    /// HTTP file verification — merchant serves a specific file at
    /// `/.well-known/shopixy-verification/{token}` on their domain.
    /// </summary>
    HttpFile = 2,
}
