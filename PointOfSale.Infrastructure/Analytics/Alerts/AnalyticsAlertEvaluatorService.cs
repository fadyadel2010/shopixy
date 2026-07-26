using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Analytics;

namespace PointOfSale.Infrastructure.Analytics.Alerts;

/// <summary>
/// Background service that evaluates alert rules every 60 seconds.
/// 
/// BEHAVIOR:
/// - Runs every 60 seconds
/// - Iterates all tenants with enabled rules
/// - Evaluates each enabled rule
/// - Stateless & idempotent
/// - Redis failure does not crash system
/// - MongoDB failure does not block evaluation
/// 
/// ARCHITECTURE:
/// Analytics Events (Phase 1)
///        ↓
/// Redis Realtime Counters
///        ↓
/// AnalyticsAlertEvaluator (this service)
///        ↓
/// Triggered Alerts
///        ├── Redis (active alerts)
///        └── MongoDB (audit log)
/// </summary>
public class AnalyticsAlertEvaluatorService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AnalyticsAlertEvaluatorService> _logger;

    private static readonly TimeSpan EvaluationInterval = TimeSpan.FromSeconds(60);

    public AnalyticsAlertEvaluatorService(
        IServiceScopeFactory scopeFactory,
        ILogger<AnalyticsAlertEvaluatorService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[AlertEvaluator] Background service STARTED - Evaluating every {Interval} seconds", 
            EvaluationInterval.TotalSeconds);

        // Initial delay to let the system stabilize
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await EvaluateAllAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AlertEvaluator] Evaluation cycle FAILED (will retry next cycle)");
            }

            await Task.Delay(EvaluationInterval, stoppingToken);
        }

        _logger.LogInformation("[AlertEvaluator] Background service STOPPED");
    }

    private async Task EvaluateAllAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();

        var ruleService = scope.ServiceProvider.GetRequiredService<IAnalyticsAlertRuleService>();
        var metricReader = scope.ServiceProvider.GetRequiredService<IAnalyticsMetricReader>();
        var alertStore = scope.ServiceProvider.GetRequiredService<IAnalyticsAlertStore>();

        // Get all tenants with enabled rules
        var tenants = await ruleService.GetTenantsWithEnabledRulesAsync(stoppingToken);

        if (tenants.Count == 0)
        {
            _logger.LogDebug("[AlertEvaluator] No tenants with enabled rules - skipping evaluation");
            return;
        }

        _logger.LogDebug("[AlertEvaluator] Evaluating {TenantCount} tenants", tenants.Count);

        int triggeredCount = 0;
        int resolvedCount = 0;

        foreach (var tenantId in tenants)
        {
            if (stoppingToken.IsCancellationRequested) break;

            try
            {
                var result = await EvaluateTenantAsync(
                    tenantId,
                    ruleService,
                    metricReader,
                    alertStore,
                    stoppingToken);

                triggeredCount += result.Triggered;
                resolvedCount += result.Resolved;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "[AlertEvaluator] Tenant evaluation FAILED | TenantId: {TenantId}",
                    tenantId);
            }
        }

        if (triggeredCount > 0 || resolvedCount > 0)
        {
            _logger.LogInformation(
                "[AlertEvaluator] Evaluation complete | Tenants: {Tenants} | Triggered: {Triggered} | Resolved: {Resolved}",
                tenants.Count,
                triggeredCount,
                resolvedCount);
        }
    }

    private async Task<(int Triggered, int Resolved)> EvaluateTenantAsync(
        Guid tenantId,
        IAnalyticsAlertRuleService ruleService,
        IAnalyticsMetricReader metricReader,
        IAnalyticsAlertStore alertStore,
        CancellationToken stoppingToken)
    {
        int triggered = 0;
        int resolved = 0;

        var rules = await ruleService.GetEnabledRulesAsync(tenantId, stoppingToken);

        foreach (var rule in rules)
        {
            if (stoppingToken.IsCancellationRequested) break;

            try
            {
                // Get current metric value
                var value = await metricReader.GetMetricValueAsync(
                    tenantId,
                    rule.Metric,
                    rule.WindowMinutes,
                    stoppingToken);

                // Evaluate rule condition
                var conditionMet = rule.Evaluate(value);

                if (conditionMet)
                {
                    // Check if alert already active
                    var isActive = await alertStore.IsAlertActiveAsync(tenantId, rule.RuleId, stoppingToken);

                    if (!isActive)
                    {
                        // Check cooldown
                        var inCooldown = await alertStore.IsInCooldownAsync(tenantId, rule.RuleId, stoppingToken);

                        if (!inCooldown)
                        {
                            // Trigger new alert
                            var alert = AnalyticsAlert.FromRule(rule, value);
                            await alertStore.TriggerAlertAsync(alert, stoppingToken);

                            // Set cooldown
                            await alertStore.SetCooldownAsync(tenantId, rule.RuleId, rule.Cooldown, stoppingToken);

                            triggered++;

                            _logger.LogWarning(
                                "[AlertEvaluator] ALERT TRIGGERED | TenantId: {TenantId} | Rule: {RuleName} | {Metric} = {Value} ({Condition})",
                                tenantId,
                                rule.Name,
                                rule.Metric,
                                value,
                                rule.GetConditionDescription());
                        }
                    }
                }
                else
                {
                    // Condition not met - check if we need to resolve an active alert
                    var isActive = await alertStore.IsAlertActiveAsync(tenantId, rule.RuleId, stoppingToken);

                    if (isActive)
                    {
                        await alertStore.ResolveAlertAsync(tenantId, rule.RuleId, value, stoppingToken);
                        resolved++;

                        _logger.LogInformation(
                            "[AlertEvaluator] ALERT RESOLVED | TenantId: {TenantId} | Rule: {RuleName} | {Metric} = {Value}",
                            tenantId,
                            rule.Name,
                            rule.Metric,
                            value);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "[AlertEvaluator] Rule evaluation FAILED | TenantId: {TenantId} | RuleId: {RuleId}",
                    tenantId,
                    rule.RuleId);
            }
        }

        return (triggered, resolved);
    }
}
