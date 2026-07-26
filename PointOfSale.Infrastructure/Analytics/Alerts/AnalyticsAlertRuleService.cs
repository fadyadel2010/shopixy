using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Analytics;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Infrastructure.Analytics.Alerts;

/// <summary>
/// PostgreSQL-based implementation of IAnalyticsAlertRuleService.
/// </summary>
public class AnalyticsAlertRuleService : IAnalyticsAlertRuleService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<AnalyticsAlertRuleService> _logger;

    public AnalyticsAlertRuleService(
        ApplicationDbContext dbContext,
        ILogger<AnalyticsAlertRuleService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<List<AnalyticsAlertRule>> GetEnabledRulesAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.AnalyticsAlertRules
            .AsNoTracking()
            .Where(r => r.TenantId == tenantId && r.IsEnabled && !r.IsDeleted)
            .OrderBy(r => r.Metric)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<AnalyticsAlertRule>> GetAllRulesAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.AnalyticsAlertRules
            .AsNoTracking()
            .Where(r => r.TenantId == tenantId && !r.IsDeleted)
            .OrderByDescending(r => r.CreatedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<AnalyticsAlertRule?> GetRuleByIdAsync(
        Guid tenantId,
        Guid ruleId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.AnalyticsAlertRules
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.RuleId == ruleId && !r.IsDeleted, cancellationToken);
    }

    public async Task<AnalyticsAlertRule> CreateRuleAsync(
        AnalyticsAlertRule rule,
        CancellationToken cancellationToken = default)
    {
        rule.RuleId = Guid.NewGuid();
        rule.CreatedAtUtc = DateTime.UtcNow;
        rule.IsDeleted = false;

        _dbContext.AnalyticsAlertRules.Add(rule);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "[AlertRules] Rule CREATED | TenantId: {TenantId} | RuleId: {RuleId} | Name: {Name} | Condition: {Condition}",
            rule.TenantId,
            rule.RuleId,
            rule.Name,
            rule.GetConditionDescription());

        return rule;
    }

    public async Task<AnalyticsAlertRule?> UpdateRuleAsync(
        AnalyticsAlertRule rule,
        CancellationToken cancellationToken = default)
    {
        var existing = await _dbContext.AnalyticsAlertRules
            .FirstOrDefaultAsync(r => r.TenantId == rule.TenantId && r.RuleId == rule.RuleId && !r.IsDeleted, cancellationToken);

        if (existing == null) return null;

        existing.Name = rule.Name;
        existing.Description = rule.Description;
        existing.Metric = rule.Metric;
        existing.Operator = rule.Operator;
        existing.Threshold = rule.Threshold;
        existing.WindowMinutes = rule.WindowMinutes;
        existing.Severity = rule.Severity;
        existing.IsEnabled = rule.IsEnabled;
        existing.CooldownMinutes = rule.CooldownMinutes;
        existing.UpdatedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "[AlertRules] Rule UPDATED | TenantId: {TenantId} | RuleId: {RuleId} | Name: {Name}",
            rule.TenantId,
            rule.RuleId,
            rule.Name);

        return existing;
    }

    public async Task<bool> DeleteRuleAsync(
        Guid tenantId,
        Guid ruleId,
        CancellationToken cancellationToken = default)
    {
        var existing = await _dbContext.AnalyticsAlertRules
            .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.RuleId == ruleId && !r.IsDeleted, cancellationToken);

        if (existing == null) return false;

        existing.IsDeleted = true;
        existing.IsEnabled = false;
        existing.UpdatedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "[AlertRules] Rule DELETED | TenantId: {TenantId} | RuleId: {RuleId}",
            tenantId,
            ruleId);

        return true;
    }

    public async Task<bool> ToggleRuleAsync(
        Guid tenantId,
        Guid ruleId,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        var existing = await _dbContext.AnalyticsAlertRules
            .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.RuleId == ruleId && !r.IsDeleted, cancellationToken);

        if (existing == null) return false;

        existing.IsEnabled = enabled;
        existing.UpdatedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "[AlertRules] Rule TOGGLED | TenantId: {TenantId} | RuleId: {RuleId} | Enabled: {Enabled}",
            tenantId,
            ruleId,
            enabled);

        return true;
    }

    public async Task<List<Guid>> GetTenantsWithEnabledRulesAsync(
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.AnalyticsAlertRules
            .AsNoTracking()
            .Where(r => r.IsEnabled && !r.IsDeleted)
            .Select(r => r.TenantId)
            .Distinct()
            .ToListAsync(cancellationToken);
    }
}
