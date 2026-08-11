using PointOfSale.Core.Enums;

namespace PointOfSale.Core.Events.Domain;

public sealed class CertificateProvisioningStartedEvent : IntegrationEvent
{
    public Guid DomainId { get; init; }
    public Guid CertificateId { get; init; }
    public Guid OperationId { get; init; }
    public CertificateOperationType OperationType { get; init; }
    public int Generation { get; init; }
}

public sealed class CertificateAuthorizationStartedEvent : IntegrationEvent
{
    public Guid DomainId { get; init; }
    public Guid CertificateId { get; init; }
    public Guid OperationId { get; init; }
    public int Generation { get; init; }
}

public sealed class CertificateIssuedEvent : IntegrationEvent
{
    public Guid DomainId { get; init; }
    public Guid CertificateId { get; init; }
    public Guid OperationId { get; init; }
    public int Generation { get; init; }
    public string? Thumbprint { get; init; }
    public DateTime? NotAfterUtc { get; init; }
}

public sealed class CertificatePendingActivationEvent : IntegrationEvent
{
    public Guid DomainId { get; init; }
    public Guid CertificateId { get; init; }
    public Guid OperationId { get; init; }
    public int Generation { get; init; }
}

public sealed class CertificateProvisioningFailedEvent : IntegrationEvent
{
    public Guid DomainId { get; init; }
    public Guid? CertificateId { get; init; }
    public Guid OperationId { get; init; }
    public int Generation { get; init; }
    public string FailureCode { get; init; } = string.Empty;
    public bool Retryable { get; init; }
}

public sealed class CertificateEdgeActivationStartedEvent : IntegrationEvent
{
    public Guid DomainId { get; init; }
    public Guid CertificateId { get; init; }
    public Guid OperationId { get; init; }
    public int Generation { get; init; }
}

public sealed class CertificateEdgeAdmissionSucceededEvent : IntegrationEvent
{
    public Guid DomainId { get; init; }
    public Guid CertificateId { get; init; }
    public Guid OperationId { get; init; }
    public int Generation { get; init; }
}

public sealed class CertificateEdgeActivationFailedEvent : IntegrationEvent
{
    public Guid DomainId { get; init; }
    public Guid CertificateId { get; init; }
    public Guid OperationId { get; init; }
    public int Generation { get; init; }
    public string FailureCode { get; init; } = string.Empty;
    public bool Retryable { get; init; }
}

public sealed class CertificateActivatedEvent : IntegrationEvent
{
    public Guid DomainId { get; init; }
    public Guid CertificateId { get; init; }
    public Guid OperationId { get; init; }
    public int Generation { get; init; }
    public string? Thumbprint { get; init; }
    public DateTime? NotAfterUtc { get; init; }
}

public sealed class DomainSslActivatedEvent : IntegrationEvent
{
    public Guid DomainId { get; init; }
    public Guid CertificateId { get; init; }
    public Guid OperationId { get; init; }
    public string HostName { get; init; } = string.Empty;
    public string NormalizedHostName { get; init; } = string.Empty;
    public DateTime SslActivatedAtUtc { get; init; }
}

public sealed class CertificateRenewalStartedEvent : IntegrationEvent
{
    public Guid DomainId { get; init; }
    public Guid CertificateId { get; init; }
    public Guid OperationId { get; init; }
    public int Generation { get; init; }
}

public sealed class CertificateReplacementIssuedEvent : IntegrationEvent
{
    public Guid DomainId { get; init; }
    public Guid OldCertificateId { get; init; }
    public Guid ReplacementCertificateId { get; init; }
    public Guid OperationId { get; init; }
    public int Generation { get; init; }
    public string? Thumbprint { get; init; }
    public DateTime? NotAfterUtc { get; init; }
}

public sealed class CertificateRenewalFailedEvent : IntegrationEvent
{
    public Guid DomainId { get; init; }
    public Guid CertificateId { get; init; }
    public Guid OperationId { get; init; }
    public int Generation { get; init; }
    public string FailureCode { get; init; } = string.Empty;
    public bool Retryable { get; init; }
    public DateTime? RetryAfterUtc { get; init; }
}

public sealed class CertificateRenewedEvent : IntegrationEvent
{
    public Guid DomainId { get; init; }
    public Guid OldCertificateId { get; init; }
    public Guid ReplacementCertificateId { get; init; }
    public Guid OperationId { get; init; }
    public int Generation { get; init; }
    public string? Thumbprint { get; init; }
    public DateTime? NotAfterUtc { get; init; }
}

public sealed class CertificateGenerationRetiredEvent : IntegrationEvent
{
    public Guid DomainId { get; init; }
    public Guid OldCertificateId { get; init; }
    public Guid ReplacementCertificateId { get; init; }
    public Guid OperationId { get; init; }
    public int OldGeneration { get; init; }
    public int ReplacementGeneration { get; init; }
}

public sealed class CertificateRevocationRequestedEvent : IntegrationEvent
{
    public Guid DomainId { get; init; }
    public Guid CertificateId { get; init; }
    public Guid OperationId { get; init; }
    public int Generation { get; init; }
    public string ReasonCode { get; init; } = string.Empty;
}

public sealed class CertificateRevokedEvent : IntegrationEvent
{
    public Guid DomainId { get; init; }
    public Guid CertificateId { get; init; }
    public Guid OperationId { get; init; }
    public int Generation { get; init; }
    public string ReasonCode { get; init; } = string.Empty;
}

public sealed class CertificateRevocationFailedEvent : IntegrationEvent
{
    public Guid DomainId { get; init; }
    public Guid CertificateId { get; init; }
    public Guid OperationId { get; init; }
    public int Generation { get; init; }
    public string ReasonCode { get; init; } = string.Empty;
    public string FailureCode { get; init; } = string.Empty;
    public bool Retryable { get; init; }
    public DateTime? RetryAfterUtc { get; init; }
}

public sealed class CertificateCompromisedEvent : IntegrationEvent
{
    public Guid DomainId { get; init; }
    public Guid CertificateId { get; init; }
    public Guid OperationId { get; init; }
    public int Generation { get; init; }
    public string ReasonCode { get; init; } = string.Empty;
}

public sealed class CertificateExpiredEvent : IntegrationEvent
{
    public Guid DomainId { get; init; }
    public Guid CertificateId { get; init; }
    public Guid OperationId { get; init; }
    public int Generation { get; init; }
}

public sealed class DomainSslDegradedEvent : IntegrationEvent
{
    public Guid DomainId { get; init; }
    public Guid? CertificateId { get; init; }
    public Guid OperationId { get; init; }
    public string ReasonCode { get; init; } = string.Empty;
}

public sealed class DomainSslRestoredEvent : IntegrationEvent
{
    public Guid DomainId { get; init; }
    public Guid CertificateId { get; init; }
    public Guid OperationId { get; init; }
    public int Generation { get; init; }
}
