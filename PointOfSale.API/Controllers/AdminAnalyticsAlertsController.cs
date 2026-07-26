using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.Core.Analytics;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Analytics.Alerts;

namespace PointOfSale.API.Controllers;

/// <summary>
/// Admin controller for analytics alerts management.
/// 
/// ENDPOINTS:
/// - GET  /api/admin/analytics/alerts/active         - Get active alerts
/// - GET  /api/admin/analytics/alerts/history        - Get alert history
/// - GET  /api/admin/analytics/alerts/rules          - Get all rules
/// - GET  /api/admin/analytics/alerts/rules/{id}     - Get specific rule
/// - POST /api/admin/analytics/alerts/rules          - Create rule
/// - PUT  /api/admin/analytics/alerts/rules/{id}     - Update rule
/// - DELETE /api/admin/analytics/alerts/rules/{id}   - Delete rule
/// - PUT  /api/admin/analytics/alerts/rules/{id}/toggle - Toggle rule
/// 
/// SECURITY:
/// - All endpoints require Admin authentication
/// - Tenant-isolated (uses ITenantContext)
/// </summary>
[ApiController]
[Route("api/admin/analytics/alerts")]
[Authorize(Roles = "Owner,Admin,Manager")]
public class AdminAnalyticsAlertsController : ControllerBase
{
    private readonly IAnalyticsAlertRuleService _ruleService;
    private readonly IAnalyticsAlertStore _alertStore;
    private readonly IAnalyticsMetricReader _metricReader;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<AdminAnalyticsAlertsController> _logger;

    public AdminAnalyticsAlertsController(
        IAnalyticsAlertRuleService ruleService,
        IAnalyticsAlertStore alertStore,
        IAnalyticsMetricReader metricReader,
        ITenantContext tenantContext,
        ILogger<AdminAnalyticsAlertsController> logger)
    {
        _ruleService = ruleService;
        _alertStore = alertStore;
        _metricReader = metricReader;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    #region Active Alerts

    /// <summary>
    /// Gets all active (unresolved) alerts for the tenant.
    /// </summary>
    [HttpGet("active")]
    [ProducesResponseType(typeof(List<AlertResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetActiveAlerts(CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.RequiredTenantId;
        var alerts = await _alertStore.GetActiveAlertsAsync(tenantId, cancellationToken);
        
        return Ok(alerts.Select(MapToResponse).ToList());
    }

    /// <summary>
    /// Gets alert history for the tenant.
    /// </summary>
    [HttpGet("history")]
    [ProducesResponseType(typeof(List<AlertResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAlertHistory(
        [FromQuery] int limit = 100,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.RequiredTenantId;
        var alerts = await _alertStore.GetAlertHistoryAsync(tenantId, limit, from, to, cancellationToken);
        
        return Ok(alerts.Select(MapToResponse).ToList());
    }

    /// <summary>
    /// Gets a summary of active alert counts by severity.
    /// </summary>
    [HttpGet("summary")]
    [ProducesResponseType(typeof(AlertSummaryResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAlertSummary(CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.RequiredTenantId;
        var alerts = await _alertStore.GetActiveAlertsAsync(tenantId, cancellationToken);

        var summary = new AlertSummaryResponse
        {
            TotalActive = alerts.Count,
            CriticalCount = alerts.Count(a => a.Severity == AnalyticsAlertSeverity.Critical),
            WarningCount = alerts.Count(a => a.Severity == AnalyticsAlertSeverity.Warning),
            InfoCount = alerts.Count(a => a.Severity == AnalyticsAlertSeverity.Info),
            OldestActiveAlert = alerts.MinBy(a => a.TriggeredAtUtc)?.TriggeredAtUtc
        };

        return Ok(summary);
    }

    #endregion

    #region Alert Rules

    /// <summary>
    /// Gets all alert rules for the tenant.
    /// </summary>
    [HttpGet("rules")]
    [ProducesResponseType(typeof(List<AlertRuleResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAllRules(CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.RequiredTenantId;
        var rules = await _ruleService.GetAllRulesAsync(tenantId, cancellationToken);
        
        return Ok(rules.Select(MapToResponse).ToList());
    }

    /// <summary>
    /// Gets a specific alert rule by ID.
    /// </summary>
    [HttpGet("rules/{ruleId:guid}")]
    [ProducesResponseType(typeof(AlertRuleResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetRuleById(
        [FromRoute] Guid ruleId,
        CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.RequiredTenantId;
        var rule = await _ruleService.GetRuleByIdAsync(tenantId, ruleId, cancellationToken);

        if (rule == null)
        {
            return NotFound(new { error = "Alert rule not found" });
        }

        return Ok(MapToResponse(rule));
    }

    /// <summary>
    /// Creates a new alert rule.
    /// </summary>
    [HttpPost("rules")]
    [ProducesResponseType(typeof(AlertRuleResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateRule(
        [FromBody] CreateAlertRuleRequest request,
        CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.RequiredTenantId;

        // Validate
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { error = "Name is required" });
        }

        if (request.WindowMinutes < 1 || request.WindowMinutes > 1440)
        {
            return BadRequest(new { error = "WindowMinutes must be between 1 and 1440 (24 hours)" });
        }

        var rule = new AnalyticsAlertRule
        {
            TenantId = tenantId,
            Name = request.Name,
            Description = request.Description,
            Metric = request.Metric,
            Operator = request.Operator,
            Threshold = request.Threshold,
            WindowMinutes = request.WindowMinutes,
            Severity = request.Severity,
            CooldownMinutes = request.CooldownMinutes ?? 15,
            IsEnabled = request.IsEnabled ?? true
        };

        var created = await _ruleService.CreateRuleAsync(rule, cancellationToken);

        _logger.LogInformation(
            "[Admin Alerts] Rule created | TenantId: {TenantId} | RuleId: {RuleId} | Name: {Name}",
            tenantId,
            created.RuleId,
            created.Name);

        return CreatedAtAction(
            nameof(GetRuleById),
            new { ruleId = created.RuleId },
            MapToResponse(created));
    }

    /// <summary>
    /// Updates an existing alert rule.
    /// </summary>
    [HttpPut("rules/{ruleId:guid}")]
    [ProducesResponseType(typeof(AlertRuleResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateRule(
        [FromRoute] Guid ruleId,
        [FromBody] UpdateAlertRuleRequest request,
        CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.RequiredTenantId;

        var existing = await _ruleService.GetRuleByIdAsync(tenantId, ruleId, cancellationToken);
        if (existing == null)
        {
            return NotFound(new { error = "Alert rule not found" });
        }

        var rule = new AnalyticsAlertRule
        {
            RuleId = ruleId,
            TenantId = tenantId,
            Name = request.Name ?? existing.Name,
            Description = request.Description ?? existing.Description,
            Metric = request.Metric ?? existing.Metric,
            Operator = request.Operator ?? existing.Operator,
            Threshold = request.Threshold ?? existing.Threshold,
            WindowMinutes = request.WindowMinutes ?? existing.WindowMinutes,
            Severity = request.Severity ?? existing.Severity,
            CooldownMinutes = request.CooldownMinutes ?? existing.CooldownMinutes,
            IsEnabled = request.IsEnabled ?? existing.IsEnabled
        };

        var updated = await _ruleService.UpdateRuleAsync(rule, cancellationToken);

        return Ok(MapToResponse(updated!));
    }

    /// <summary>
    /// Deletes an alert rule.
    /// </summary>
    [HttpDelete("rules/{ruleId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteRule(
        [FromRoute] Guid ruleId,
        CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.RequiredTenantId;

        var success = await _ruleService.DeleteRuleAsync(tenantId, ruleId, cancellationToken);

        if (!success)
        {
            return NotFound(new { error = "Alert rule not found" });
        }

        _logger.LogInformation(
            "[Admin Alerts] Rule deleted | TenantId: {TenantId} | RuleId: {RuleId}",
            tenantId,
            ruleId);

        return Ok(new { success = true });
    }

    /// <summary>
    /// Toggles an alert rule on/off.
    /// </summary>
    [HttpPut("rules/{ruleId:guid}/toggle")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ToggleRule(
        [FromRoute] Guid ruleId,
        [FromBody] ToggleAlertRuleRequest request,
        CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.RequiredTenantId;

        var success = await _ruleService.ToggleRuleAsync(tenantId, ruleId, request.Enabled, cancellationToken);

        if (!success)
        {
            return NotFound(new { error = "Alert rule not found" });
        }

        return Ok(new { success = true, enabled = request.Enabled });
    }

    #endregion

    #region Metrics (for testing/preview)

    /// <summary>
    /// Gets current metric values for the tenant (for rule preview/testing).
    /// </summary>
    [HttpGet("metrics")]
    [ProducesResponseType(typeof(MetricsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCurrentMetrics(
        [FromQuery] int windowMinutes = 60,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.RequiredTenantId;
        var metrics = Enum.GetValues<AnalyticsMetric>();
        var values = await _metricReader.GetMetricValuesAsync(tenantId, metrics, windowMinutes, cancellationToken);

        return Ok(new MetricsResponse
        {
            WindowMinutes = windowMinutes,
            Metrics = values.ToDictionary(
                kv => kv.Key.ToString(),
                kv => kv.Value)
        });
    }

    #endregion

    #region Mapping

    private static AlertResponse MapToResponse(AnalyticsAlert alert)
    {
        return new AlertResponse
        {
            AlertId = alert.AlertId,
            RuleId = alert.RuleId,
            RuleName = alert.RuleName,
            Metric = alert.Metric.ToString(),
            ActualValue = alert.ActualValue,
            Threshold = alert.Threshold,
            Operator = alert.Operator.ToString(),
            Severity = alert.Severity.ToString(),
            ConditionDescription = alert.ConditionDescription,
            TriggeredAtUtc = alert.TriggeredAtUtc,
            ResolvedAtUtc = alert.ResolvedAtUtc,
            IsResolved = alert.IsResolved,
            IsActive = alert.IsActive,
            DurationSeconds = alert.Duration?.TotalSeconds
        };
    }

    private static AlertRuleResponse MapToResponse(AnalyticsAlertRule rule)
    {
        return new AlertRuleResponse
        {
            RuleId = rule.RuleId,
            Name = rule.Name,
            Description = rule.Description,
            Metric = rule.Metric.ToString(),
            Operator = rule.Operator.ToString(),
            Threshold = rule.Threshold,
            WindowMinutes = rule.WindowMinutes,
            Severity = rule.Severity.ToString(),
            CooldownMinutes = rule.CooldownMinutes,
            IsEnabled = rule.IsEnabled,
            ConditionDescription = rule.GetConditionDescription(),
            CreatedAtUtc = rule.CreatedAtUtc,
            UpdatedAtUtc = rule.UpdatedAtUtc
        };
    }

    #endregion
}

#region DTOs

public class AlertResponse
{
    public Guid AlertId { get; set; }
    public Guid RuleId { get; set; }
    public string RuleName { get; set; } = string.Empty;
    public string Metric { get; set; } = string.Empty;
    public decimal ActualValue { get; set; }
    public decimal Threshold { get; set; }
    public string Operator { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string ConditionDescription { get; set; } = string.Empty;
    public DateTime TriggeredAtUtc { get; set; }
    public DateTime? ResolvedAtUtc { get; set; }
    public bool IsResolved { get; set; }
    public bool IsActive { get; set; }
    public double? DurationSeconds { get; set; }
}

public class AlertSummaryResponse
{
    public int TotalActive { get; set; }
    public int CriticalCount { get; set; }
    public int WarningCount { get; set; }
    public int InfoCount { get; set; }
    public DateTime? OldestActiveAlert { get; set; }
}

public class AlertRuleResponse
{
    public Guid RuleId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Metric { get; set; } = string.Empty;
    public string Operator { get; set; } = string.Empty;
    public decimal Threshold { get; set; }
    public int WindowMinutes { get; set; }
    public string Severity { get; set; } = string.Empty;
    public int CooldownMinutes { get; set; }
    public bool IsEnabled { get; set; }
    public string ConditionDescription { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}

public class CreateAlertRuleRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public AnalyticsMetric Metric { get; set; }
    public AnalyticsComparisonOperator Operator { get; set; }
    public decimal Threshold { get; set; }
    public int WindowMinutes { get; set; }
    public AnalyticsAlertSeverity Severity { get; set; } = AnalyticsAlertSeverity.Warning;
    public int? CooldownMinutes { get; set; }
    public bool? IsEnabled { get; set; }
}

public class UpdateAlertRuleRequest
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public AnalyticsMetric? Metric { get; set; }
    public AnalyticsComparisonOperator? Operator { get; set; }
    public decimal? Threshold { get; set; }
    public int? WindowMinutes { get; set; }
    public AnalyticsAlertSeverity? Severity { get; set; }
    public int? CooldownMinutes { get; set; }
    public bool? IsEnabled { get; set; }
}

public class ToggleAlertRuleRequest
{
    public bool Enabled { get; set; }
}

public class MetricsResponse
{
    public int WindowMinutes { get; set; }
    public Dictionary<string, decimal> Metrics { get; set; } = new();
}

#endregion
