using PointOfSale.Core.Enums;

namespace PointOfSale.API.Services.Storefront;

public interface ICertificateRenewalService
{
    Task<CertificateRenewalServiceResult> RenewAsync(
        CertificateRenewalSchedulerCandidate candidate,
        string correlationId,
        CancellationToken cancellationToken = default);
}

public sealed record CertificateRenewalServiceResult(
    Guid TenantId,
    Guid DomainId,
    Guid CertificateId,
    Guid? ReplacementCertificateId,
    Guid? OperationId,
    ProviderType ProviderType,
    int Generation,
    CertificateOperationPhase Phase,
    string Outcome,
    bool Succeeded,
    bool Retryable,
    string? FailureCode,
    DateTime? RetryAfterUtc);
