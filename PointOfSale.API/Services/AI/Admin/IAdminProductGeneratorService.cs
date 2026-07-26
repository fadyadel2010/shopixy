using PointOfSale.API.DTOs.Admin;

namespace PointOfSale.API.Services.AI.Admin;

public interface IAdminProductGeneratorService
{
    Task<AdminAiGeneratedProductResponse> GenerateProductAsync(
        Guid tenantId,
        AdminAiProductGenerationRequest request,
        CancellationToken cancellationToken = default);
}
