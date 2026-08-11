namespace PointOfSale.Core.Enums;

/// <summary>
/// Optional persisted operation phase for SSL/TLS workflows.
/// This is diagnostic/progress metadata only; <see cref="CertificateStatus"/>
/// remains the single authoritative certificate business lifecycle state.
/// </summary>
public enum CertificateOperationPhase
{
    None = 0,
    Admission = 1,
    ProviderRequest = 2,
    Authorization = 3,
    MetadataSync = 4,
    ActivationProbe = 5,
    RenewalWindow = 6,
    Revocation = 7,
    RetryBackoff = 8,
    EdgeDeactivation = 9,
}

