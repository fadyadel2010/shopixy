using PointOfSale.API.DTOs.Storefront;

namespace PointOfSale.API.Services.Storefront;

public interface IStorefrontCheckoutFieldDefinitionService
{
    Task<StorefrontCheckoutFieldDefinitionsResponse> GetFieldsAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);
}
