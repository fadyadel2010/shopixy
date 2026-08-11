namespace PointOfSale.Core.Enums;

/// <summary>
/// Durable status for an SSL/TLS operation claim.
/// This does not replace the certificate business lifecycle state.
/// </summary>
public enum CertificateOperationStatus
{
    Pending = 0,
    Running = 1,
    Succeeded = 2,
    Failed = 3,
    Cancelled = 4,
    Superseded = 5,
}
