using PointOfSale.Core.Analytics;

namespace PointOfSale.Infrastructure.Analytics.Alerts;

/// <summary>
/// Service for managing active alerts and alert audit trail.
/// 
/// STORAGE STRATEGY:
/// - Active alerts: Redis (fast access for dashboard)
/// - Alert history: MongoDB (audit trail)
/// 
/// RULES:
/// - Alerts are IDEMPOTENT by RuleId (only one active alert per rule)
/// - Alerts auto-resolve when condition clears
/// - Cooldown prevents alert spam
/// </summary>
public interface IAnalyticsAlertStore
{
    #region Active Alerts (Redis)

    /// <summary>
    /// Gets all active alerts for a tenant.
    /// </summary>
    Task<List<AnalyticsAlert>> GetActiveAlertsAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if an alert is currently active for a rule.
    /// </summary>
    Task<bool> IsAlertActiveAsync(
        Guid tenantId,
        Guid ruleId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the active alert for a specific rule (if any).
    /// </summary>
    Task<AnalyticsAlert?> GetActiveAlertForRuleAsync(
        Guid tenantId,
        Guid ruleId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Activates an alert (stores in Redis and MongoDB).
    /// </summary>
    Task TriggerAlertAsync(
        AnalyticsAlert alert,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves an active alert.
    /// </summary>
    Task ResolveAlertAsync(
        Guid tenantId,
        Guid ruleId,
        decimal currentValue,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a rule is in cooldown period.
    /// </summary>
    Task<bool> IsInCooldownAsync(
        Guid tenantId,
        Guid ruleId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the cooldown for a rule.
    /// </summary>
    Task SetCooldownAsync(
        Guid tenantId,
        Guid ruleId,
        TimeSpan duration,
        CancellationToken cancellationToken = default);

    #endregion

    #region Alert History (MongoDB)

    /// <summary>
    /// Gets alert history for a tenant.
    /// </summary>
    Task<List<AnalyticsAlert>> GetAlertHistoryAsync(
        Guid tenantId,
        int limit = 100,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets alert history for a specific rule.
    /// </summary>
    Task<List<AnalyticsAlert>> GetAlertHistoryByRuleAsync(
        Guid tenantId,
        Guid ruleId,
        int limit = 50,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a specific alert by ID.
    /// </summary>
    Task<AnalyticsAlert?> GetAlertByIdAsync(
        Guid tenantId,
        Guid alertId,
        CancellationToken cancellationToken = default);

    #endregion
}
