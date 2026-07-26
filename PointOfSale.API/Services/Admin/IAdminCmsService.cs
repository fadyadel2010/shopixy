using PointOfSale.API.DTOs.Admin;

namespace PointOfSale.API.Services.Admin;

public interface IAdminCmsService
{
    Task<AdminCmsPageResponse?> GetCmsPageAsync(
        Guid tenantId,
        Guid pageId,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<AdminCmsPageResponse?> GetCmsPageBySlugAsync(
        Guid tenantId,
        string slug,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AdminCmsPageListItemResponse>> ListCmsPagesAsync(
        Guid tenantId,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<AdminCmsPageVersionHistoryResponse?> GetCmsPageVersionHistoryAsync(
        Guid tenantId,
        Guid pageId,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<AdminCmsPageResponse> CreateCmsPageAsync(
        Guid tenantId,
        AdminCmsPageRequest request,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<AdminCmsPageResponse> UpdateCmsPageAsync(
        Guid tenantId,
        Guid pageId,
        AdminCmsPageRequest request,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<AdminCmsPageResponse> PublishCmsPageAsync(
        Guid tenantId,
        Guid pageId,
        int expectedVersion,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<AdminCmsPageResponse> RollbackCmsPageAsync(
        Guid tenantId,
        Guid pageId,
        int rollbackVersion,
        int expectedVersion,
        string? reason,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<AdminCmsPageResponse?> GetCmsPagePreviewAsync(
        Guid tenantId,
        Guid pageId,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<AdminCmsPreviewAccessResponse?> CreateCmsPagePreviewAccessAsync(
        Guid tenantId,
        Guid pageId,
        string traceId,
        CancellationToken cancellationToken = default);

    Task DeleteCmsPageAsync(
        Guid tenantId,
        Guid pageId,
        int expectedVersion,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<AdminNavigationResponse> GetNavigationAsync(
        Guid tenantId,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<AdminNavigationResponse> UpdateNavigationAsync(
        Guid tenantId,
        AdminNavigationRequest request,
        uint expectedRowVersion,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<AdminNavigationResponse> PublishNavigationAsync(
        Guid tenantId,
        uint expectedRowVersion,
        string traceId,
        CancellationToken cancellationToken = default);
}
