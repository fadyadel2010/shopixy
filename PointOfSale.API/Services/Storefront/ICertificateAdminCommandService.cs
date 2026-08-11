using PointOfSale.API.DTOs.Storefront;

namespace PointOfSale.API.Services.Storefront;

public interface ICertificateAdminCommandService
{
    Task<CertificateProvisioningResponse> ProvisionAsync(
        Guid domainId,
        CertificateProvisioningRequest? request,
        Guid tenantId,
        Guid? actorUserId,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<CertificateActivationResponse> ActivateAsync(
        Guid domainId,
        CertificateActivationRequest? request,
        Guid tenantId,
        Guid? actorUserId,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<CertificateCommandResponse> RenewAsync(
        Guid domainId,
        CertificateRenewalCommandRequest? request,
        Guid tenantId,
        Guid? actorUserId,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<CertificateCommandResponse> RevokeAsync(
        Guid domainId,
        CertificateRevocationCommandRequest? request,
        Guid tenantId,
        Guid? actorUserId,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<CertificateCommandResponse> RetryAsync(
        Guid domainId,
        CertificateRetryCommandRequest? request,
        Guid tenantId,
        Guid? actorUserId,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<CertificateCommandResponse> RefreshAsync(
        Guid domainId,
        CertificateRefreshCommandRequest? request,
        Guid tenantId,
        Guid? actorUserId,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<DomainCertificateDto?> GetCertificateAsync(
        Guid domainId,
        Guid tenantId,
        CancellationToken cancellationToken = default);

    Task<CertificateOperationPageDto?> GetOperationsAsync(
        Guid domainId,
        Guid tenantId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<CertificateOperationDto?> GetOperationAsync(
        Guid domainId,
        Guid operationId,
        Guid tenantId,
        CancellationToken cancellationToken = default);
}

public sealed class CertificateCommandRateLimitException : Exception
{
    public CertificateCommandRateLimitException(string message, Guid? operationId = null, DateTime? retryAfterUtc = null)
        : base(message)
    {
        OperationId = operationId;
        RetryAfterUtc = retryAfterUtc;
    }

    public Guid? OperationId { get; }
    public DateTime? RetryAfterUtc { get; }
}
