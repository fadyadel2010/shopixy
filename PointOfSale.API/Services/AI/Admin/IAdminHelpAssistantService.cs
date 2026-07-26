using PointOfSale.API.DTOs.Admin;

namespace PointOfSale.API.Services.AI.Admin;

public interface IAdminHelpAssistantService
{
    Task<AdminHelpAssistantResponse> AskAsync(
        Guid tenantId,
        AdminHelpAssistantRequest request,
        CancellationToken cancellationToken = default);
}
