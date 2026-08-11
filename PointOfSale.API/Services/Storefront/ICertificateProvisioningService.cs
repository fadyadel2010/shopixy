using PointOfSale.API.DTOs.Storefront;

namespace PointOfSale.API.Services.Storefront;

public interface ICertificateProvisioningService
{
    Task<CertificateProvisioningResponse> ProvisionAsync(
        Guid domainId,
        CertificateProvisioningRequest? request,
        Guid tenantId,
        Guid? actorUserId,
        string correlationId,
        CancellationToken cancellationToken = default);
}
