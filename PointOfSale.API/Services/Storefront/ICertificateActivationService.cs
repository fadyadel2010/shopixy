using PointOfSale.API.DTOs.Storefront;

namespace PointOfSale.API.Services.Storefront;

public interface ICertificateActivationService
{
    Task<CertificateActivationResponse> ActivateAsync(
        Guid domainId,
        CertificateActivationRequest? request,
        Guid tenantId,
        Guid? actorUserId,
        string correlationId,
        CancellationToken cancellationToken = default);
}
