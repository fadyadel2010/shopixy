using PointOfSale.Core.Analytics;

namespace PointOfSale.Infrastructure.Analytics.Alerts;

/// <summary>
/// Service for managing analytics alert rules.
/// 
/// STORAGE:
/// - PostgreSQL (source of truth)
/// - Redis (cache for fast evaluation)
/// </summary>
public interface IAnalyticsAlertRuleService
{
    /// <summary>
    /// Gets all enabled alert rules for a tenant.
    /// </summary>
    Task<List<AnalyticsAlertRule>> GetEnabledRulesAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all alert rules for a tenant (including disabled).
    /// </summary>
    Task<List<AnalyticsAlertRule>> GetAllRulesAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a specific alert rule by ID.
    /// </summary>
    Task<AnalyticsAlertRule?> GetRuleByIdAsync(
        Guid tenantId,
        Guid ruleId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new alert rule.
    /// </summary>
    Task<AnalyticsAlertRule> CreateRuleAsync(
        AnalyticsAlertRule rule,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing alert rule.
    /// </summary>
    Task<AnalyticsAlertRule?> UpdateRuleAsync(
        AnalyticsAlertRule rule,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes (soft-delete) an alert rule.
    /// </summary>
    Task<bool> DeleteRuleAsync(
        Guid tenantId,
        Guid ruleId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Toggles an alert rule on/off.
    /// </summary>
    Task<bool> ToggleRuleAsync(
        Guid tenantId,
        Guid ruleId,
        bool enabled,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all tenants that have enabled alert rules.
    /// Used by the background evaluator.
    /// </summary>
    Task<List<Guid>> GetTenantsWithEnabledRulesAsync(
        CancellationToken cancellationToken = default);
}
