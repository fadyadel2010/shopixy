using PointOfSale.Core.Entities;

namespace PointOfSale.Core.Interfaces;

/// <summary>
/// Service for managing tenant automation settings.
/// Phase: Smart Abandoned Cart Recovery Engine - Foundation
/// 
/// RESPONSIBILITIES:
/// - CRUD operations for TenantAutomationSettings
/// - Validation of settings (working hours, delays)
/// - Cache invalidation on updates
/// - Audit logging
/// 
/// DOES NOT:
/// - Send messages (that's Phase 2)
/// - Trigger automation (that's Phase 2)
/// - Make eligibility decisions (see IAbandonedCartEligibilityService)
/// </summary>
public interface ITenantAutomationSettingsService
{
    /// <summary>
    /// Gets automation settings for a tenant.
    /// Returns default settings if none exist.
    /// NEVER returns null.
    /// </summary>
    Task<TenantAutomationSettings> GetSettingsAsync(Guid tenantId, CancellationToken ct = default);
    
    /// <summary>
    /// Updates automation settings for a tenant.
    /// Creates if not exists.
    /// Validates settings before saving.
    /// Invalidates cache.
    /// </summary>
    /// <exception cref="ArgumentException">If settings are invalid</exception>
    Task<TenantAutomationSettings> UpdateSettingsAsync(
        Guid tenantId, 
        TenantAutomationSettings settings, 
        CancellationToken ct = default);
    
    /// <summary>
    /// Checks if automation settings exist for a tenant.
    /// Used to determine if tenant has configured automation.
    /// </summary>
    Task<bool> ExistsAsync(Guid tenantId, CancellationToken ct = default);
}
