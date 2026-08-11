namespace PointOfSale.Core.Enums;

/// <summary>
/// Certificate provider — the authority that issued or manages the SSL/TLS
/// certificate.
/// Stored as <c>smallint</c> at the persistence layer (DOMAIN-1.5 Section 6).
/// </summary>
public enum ProviderType
{
    /// <summary>
    /// Let's Encrypt — automated, free certificate authority.
    /// Default provider for standard domains.
    /// </summary>
    LetsEncrypt = 0,

    /// <summary>
    /// Custom upload — merchant-uploaded certificate (enterprise customers).
    /// </summary>
    CustomUpload = 1,

    /// <summary>
    /// System default — platform-managed shared certificate.
    /// Used for system/development domains.
    /// </summary>
    SystemDefault = 2,
}
