using PointOfSale.API.DTOs.Admin;

namespace PointOfSale.API.Services.Admin;

public interface IAdminThemeActivationService
{
    Task<AdminStoreConfigResponse> ActivateThemeAsync(
        Guid tenantId,
        AdminThemeActivationRequest request,
        string traceId,
        CancellationToken cancellationToken = default,
        uint? expectedRowVersion = null);
}
