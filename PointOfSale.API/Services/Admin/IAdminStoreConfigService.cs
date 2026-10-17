using PointOfSale.API.DTOs.Admin;

namespace PointOfSale.API.Services.Admin;

/// <summary>
/// Admin Store Config Service Interface.
/// Phase 7.1: Theme & Store Config System
/// 
/// ARCHITECTURAL RULES:
/// - POS Admin is the ONLY source of truth for store config
/// - Full config replacement (no partial patching)
/// - Live-facing mutations must stage integration events durably
/// - Storefront projection consumers own cache invalidation after successful sync
/// </summary>
public interface IAdminStoreConfigService
{
    Task<AdminStoreConfigResponse> UpdateCommerceAiAsync(Guid tenantId, AdminCommerceAiConfigDto request,
        string traceId, CancellationToken cancellationToken = default, uint? expectedRowVersion = null);
    /// <summary>
    /// Gets the store config for a tenant.
    /// Creates a default config if none exists.
    /// </summary>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="traceId">Correlation ID for logging</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Store configuration</returns>
    Task<AdminStoreConfigResponse> GetStoreConfigAsync(
        Guid tenantId,
        string traceId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the store config for a tenant.
    /// Creates if not exists, replaces if exists.
    /// </summary>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="request">Store config request</param>
    /// <param name="traceId">Correlation ID for logging</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Updated store configuration</returns>
    Task<AdminStoreConfigResponse> UpdateStoreConfigAsync(
        Guid tenantId,
        AdminStoreConfigRequest request,
        string traceId,
        CancellationToken cancellationToken = default,
        uint? expectedRowVersion = null);

    /// <summary>
    /// Updates only the storefront operational status for a tenant.
    /// </summary>
    Task<AdminStoreConfigResponse> UpdateStorefrontStatusAsync(
        Guid tenantId,
        AdminStorefrontStatusRequest request,
        string traceId,
        CancellationToken cancellationToken = default,
        uint? expectedRowVersion = null);

    /// <summary>
    /// Update a specific page layout (Draft mode).
    /// </summary>
    Task<AdminStoreConfigResponse> UpdatePageLayoutAsync(
        Guid tenantId,
        string pageId,
        Dictionary<string, object> pageConfig,
        string traceId,
        CancellationToken cancellationToken = default,
        uint? expectedRowVersion = null);

    /// <summary>
    /// Publish a page from Draft to Live.
    /// </summary>
    Task<AdminStoreConfigResponse> PublishPageAsync(
        Guid tenantId,
        string pageId,
        string traceId,
        CancellationToken cancellationToken = default,
        uint? expectedRowVersion = null);

    /// <summary>
    /// Deletes a product template from customization storage.
    /// </summary>
    Task<AdminStoreConfigResponse> DeleteProductTemplateAsync(
        Guid tenantId,
        string templateId,
        string traceId,
        CancellationToken cancellationToken = default,
        uint? expectedRowVersion = null);

    // ===================================
    // PHASE 9.3: Theme Presets & Switching
    // ===================================

    Task<List<ThemePresetDto>> GetThemePresetsAsync(
        string traceId, 
        CancellationToken cancellationToken = default);

    Task<AdminStoreConfigResponse> SelectThemePresetAsync(
        Guid tenantId,
        string presetKey,
        string traceId,
        CancellationToken cancellationToken = default,
        uint? expectedRowVersion = null);

    Task<AdminStoreConfigResponse> UpdateThemeOverridesAsync(
        Guid tenantId,
        string overridesJson,
        string traceId,
        CancellationToken cancellationToken = default,
        uint? expectedRowVersion = null);       

    Task<AdminStoreConfigResponse> PublishThemeAsync(
        Guid tenantId,
        string traceId,
        CancellationToken cancellationToken = default,
        uint? expectedRowVersion = null);

    // ===================================
    // PHASE 9.6: Versioning & Rollback
    // ===================================
    
    Task<AdminStoreConfigResponse> RollbackStoreConfigAsync(
        Guid tenantId,
        int versionNumber,
        bool confirm,
        string? reason,
        string traceId,
        CancellationToken cancellationToken = default,
        uint? expectedRowVersion = null);

    Task<object> GetStoreConfigPreviewAsync(Guid tenantId, string traceId, CancellationToken cancellationToken, string themeId);

    Task<object> GetStoreConfigPreviewAsync(
        Guid tenantId,
        string traceId,
        CancellationToken cancellationToken = default);

    // ===================================
    // PHASE: Storefront Admin Hardening - P1
    // ===================================

    /// <summary>
    /// Gets the version history for a store configuration.
    /// Returns all stored versions with metadata for display and rollback selection.
    /// </summary>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="traceId">Correlation ID for logging</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Version history response</returns>
    Task<VersionHistoryResponse> GetVersionHistoryAsync(
        Guid tenantId,
        string traceId,
        CancellationToken cancellationToken = default);
}
