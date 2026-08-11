using PointOfSale.API.DTOs.Admin;

namespace PointOfSale.API.Services.Admin;

public interface ICheckoutFieldDefinitionService
{
    Task<CheckoutFieldDefinitionsResponse> GetFieldsAsync(Guid tenantId, CancellationToken cancellationToken = default);

    Task<CheckoutFieldDefinitionsResponse> InitializeSystemFieldsAsync(
        Guid tenantId,
        Guid? actorUserId,
        CancellationToken cancellationToken = default);

    Task<CheckoutFieldDefinitionDto> CreateCustomFieldAsync(
        Guid tenantId,
        CreateCheckoutFieldDefinitionRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken = default);

    Task<CheckoutFieldDefinitionDto> UpdateFieldAsync(
        Guid tenantId,
        Guid fieldId,
        UpdateCheckoutFieldDefinitionRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken = default);

    Task ArchiveCustomFieldAsync(
        Guid tenantId,
        Guid fieldId,
        Guid? actorUserId,
        CancellationToken cancellationToken = default);

    Task<CheckoutFieldDefinitionsResponse> ReorderFieldsAsync(
        Guid tenantId,
        ReorderCheckoutFieldDefinitionsRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken = default);
}
