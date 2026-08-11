using PointOfSale.Core.Enums;

namespace PointOfSale.API.Services.Storefront;

public interface ICertificateRevocationService
{
    Task<CertificateRevocationServiceResult> RevokeAsync(
        Guid domainId,
        CertificateRevocationRequest request,
        Guid tenantId,
        Guid? actorUserId,
        string correlationId,
        CancellationToken cancellationToken = default);
}

public sealed record CertificateRevocationRequest(
    Guid CertificateId,
    string ReasonCode,
    string IdempotencyKey,
    bool ImmediateRevocation = false);

public sealed record CertificateRevocationServiceResult(
    Guid TenantId,
    Guid DomainId,
    Guid CertificateId,
    Guid OperationId,
    ProviderType ProviderType,
    int Generation,
    CertificateOperationStatus OperationStatus,
    CertificateOperationPhase Phase,
    CertificateStatus CertificateStatus,
    string ReasonCode,
    string Outcome,
    bool Succeeded,
    bool Retryable,
    string? FailureCode,
    DateTime? RetryAfterUtc);
