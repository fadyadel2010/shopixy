namespace PointOfSale.Core.Enums;

/// <summary>
/// Durable SSL/TLS operation idempotency scope.
/// </summary>
public enum CertificateOperationType
{
    Provisioning = 0,
    Renewal = 1,
    Retry = 2,
    Revocation = 3,
    Activation = 4,
    MetadataSync = 5,
}

