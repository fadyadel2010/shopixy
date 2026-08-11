namespace PointOfSale.Core.Certificates;

public static class CertificateRevocationReasonCodes
{
    public const string AdminRequested = "ADMIN_REQUESTED";
    public const string SecurityCompromise = "SECURITY_COMPROMISE";
    public const string CertificateCompromised = "CERTIFICATE_COMPROMISED";
    public const string ProviderRevoked = "PROVIDER_REVOKED";
    public const string CaRevoked = "CA_REVOKED";
    public const string Expired = "EXPIRED";
    public const string Superseded = "SUPERSEDED";
    public const string EdgeCertificateMismatch = "EDGE_CERTIFICATE_MISMATCH";
    public const string SniProofFailure = "SNI_PROOF_FAILURE";
    public const string ActivationFailure = "ACTIVATION_FAILURE";
    public const string RenewalExhausted = "RENEWAL_EXHAUSTED";
    public const string DomainDeleted = "DOMAIN_DELETED";
    public const string PolicyViolation = "POLICY_VIOLATION";

    private static readonly HashSet<string> KnownCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        AdminRequested,
        SecurityCompromise,
        CertificateCompromised,
        ProviderRevoked,
        CaRevoked,
        Expired,
        Superseded,
        EdgeCertificateMismatch,
        SniProofFailure,
        ActivationFailure,
        RenewalExhausted,
        DomainDeleted,
        PolicyViolation,
    };

    public static bool IsKnown(string? reasonCode) =>
        !string.IsNullOrWhiteSpace(reasonCode) && KnownCodes.Contains(reasonCode.Trim());

    public static string Normalize(string? reasonCode)
    {
        if (string.IsNullOrWhiteSpace(reasonCode))
        {
            throw new ArgumentException("Revocation reason is required.", nameof(reasonCode));
        }

        var normalized = reasonCode.Trim().ToUpperInvariant();
        if (!KnownCodes.Contains(normalized))
        {
            throw new ArgumentException("Revocation reason is not supported.", nameof(reasonCode));
        }

        return normalized;
    }

    public static bool IsCompromise(string reasonCode) =>
        string.Equals(reasonCode, SecurityCompromise, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(reasonCode, CertificateCompromised, StringComparison.OrdinalIgnoreCase);
}
