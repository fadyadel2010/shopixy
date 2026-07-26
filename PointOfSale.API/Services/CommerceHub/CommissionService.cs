using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PointOfSale.API.Configuration;
using PointOfSale.API.Instrumentation;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events.CommerceHub;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services.CommerceHub;

public sealed class CommissionAuthorityException : Exception
{
    public CommissionAuthorityException(string code, string message, object? details = null)
        : base(message)
    {
        Code = string.IsNullOrWhiteSpace(code) ? "COMMISSION_AUTHORITY_INVALID" : code.Trim();
        Details = details;
    }

    public string Code { get; }

    public object? Details { get; }
}

public readonly record struct CommissionResolutionResult(
    decimal Rate,
    Guid? RuleId,
    CommissionRuleScopeType? ScopeType,
    Guid? ScopeId,
    int? Priority,
    bool UsedLegacyFallback,
    string Source);

public interface ICommissionService
{
    decimal GetCommissionRate(Guid? vendorId, Guid? categoryId);

    CommissionResolutionResult ResolveCommission(Guid? vendorId, Guid? categoryId);

    void InvalidateRulesCache();

    void InvalidateLocalRulesCache();
}

/// <summary>
/// Immutable snapshot of commission rules cache.
/// Ensures atomic reads of rules and expiry without race conditions.
/// </summary>
sealed record CommissionRulesSnapshot(
    IReadOnlyList<CommissionRule> Rules,
    DateTime Expiry);

public sealed class CommissionService : ICommissionService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly CommerceHubPaymentsOptions _options;
    private readonly CommissionServiceTelemetry _telemetry;
    private readonly ILogger<CommissionService> _logger;
    private readonly IEventPublisher _eventPublisher;
    
    // In-memory cache for commission rules - single atomic reference
    private volatile CommissionRulesSnapshot? _cachedSnapshot;
    private readonly object _cacheLock = new object();

    public CommissionService(
        ApplicationDbContext dbContext,
        IOptions<CommerceHubPaymentsOptions> options,
        CommissionServiceTelemetry telemetry,
        ILogger<CommissionService> logger,
        IEventPublisher eventPublisher)
    {
        _dbContext = dbContext;
        _options = options.Value;
        _telemetry = telemetry;
        _logger = logger;
        _eventPublisher = eventPublisher;
    }

    public decimal GetCommissionRate(Guid? vendorId, Guid? categoryId)
    {
        return ResolveCommission(vendorId, categoryId).Rate;
    }

    public CommissionResolutionResult ResolveCommission(Guid? vendorId, Guid? categoryId)
    {
        var activeRules = GetActiveRules();
        var matchedRule =
            ResolveWithinScope(activeRules, CommissionRuleScopeType.Vendor, vendorId) ??
            ResolveWithinScope(activeRules, CommissionRuleScopeType.Category, categoryId) ??
            ResolveGlobalRule(activeRules);

        if (matchedRule == null)
        {
            throw new CommissionAuthorityException(
                "COMMISSION_AUTHORITY_MISSING",
                "CommerceHub checkout is blocked because no active SQL commission rule matches this vendor/category authority.",
                new
                {
                    VendorId = vendorId,
                    CategoryId = categoryId
                });
        }

        var resolution = new CommissionResolutionResult(
            Rate: NormalizeCommissionRate(matchedRule.Rate),
            RuleId: matchedRule.Id,
            ScopeType: matchedRule.ScopeType,
            ScopeId: matchedRule.ScopeId,
            Priority: matchedRule.Priority,
            UsedLegacyFallback: false,
            Source: CommissionCalculator.ResolveSource(matchedRule.ScopeType));

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "CommerceHub commission resolved | VendorId: {VendorId} | CategoryId: {CategoryId} | Rate: {Rate} | Source: {Source} | RuleId: {RuleId} | Priority: {Priority} | Authority: SQL",
                vendorId,
                categoryId,
                resolution.Rate,
                resolution.Source,
                resolution.RuleId,
                resolution.Priority);
        }

        return resolution;
    }

    public void InvalidateRulesCache()
    {
        InvalidateLocalRulesCache();
        PublishRulesCacheInvalidationAsync();
    }

    public void InvalidateLocalRulesCache()
    {
        lock (_cacheLock)
        {
            _cachedSnapshot = null;
            _logger.LogDebug("CommerceHub commission rules cache invalidated.");
        }
    }

    private void PublishRulesCacheInvalidationAsync()
    {
        // Publish event for cross-node invalidation (fire-and-forget, non-blocking)
        _ = Task.Run(async () =>
        {
            try
            {
                await _eventPublisher.PublishAsync(new CommissionRulesCacheInvalidatedEvent
                {
                    Reason = "ManualInvalidation",
                    TenantId = null // Global cache invalidation
                });
            }
            catch (Exception ex)
            {
                // Log but don't throw - local cache is already cleared
                _logger.LogWarning(ex, "Failed to publish CommissionRulesCacheInvalidatedEvent");
            }
        });
    }

    private IReadOnlyList<CommissionRule> GetActiveRules()
    {
        var now = DateTime.UtcNow;
        
        // Fast path: check if cache is valid without locking
        // Read snapshot once atomically to avoid race conditions
        var snapshot = _cachedSnapshot;
        if (snapshot != null && now < snapshot.Expiry)
        {
            _telemetry.RecordCacheHit();
            return snapshot.Rules;
        }
        
        // Slow path: acquire lock and refresh cache if still stale
        lock (_cacheLock)
        {
            // Double-check: another thread may have refreshed the cache
            snapshot = _cachedSnapshot;
            if (snapshot != null && now < snapshot.Expiry)
            {
                _telemetry.RecordCacheHit();
                return snapshot.Rules;
            }
            
            // Cache is stale - query database
            _telemetry.RecordCacheMiss();
            IReadOnlyList<CommissionRule> rules;
            using (_telemetry.MeasureDbQuery())
            {
                rules = _dbContext.CommissionRules
                    .AsNoTracking()
                    .Where(rule => rule.IsActive && !rule.IsDeleted)
                    .OrderByDescending(rule => rule.Priority)
                    .ThenByDescending(rule => rule.CreatedAt)
                    .ThenByDescending(rule => rule.Id)
                    .ToList();
            }

            EnsureNoAmbiguousRules(rules);

            _logger.LogDebug(
                "CommerceHub commission rules loaded from SQL authority | Count: {Count} | CacheDurationSeconds: {CacheDurationSeconds}",
                rules.Count,
                _options.CommissionRulesCacheDurationSeconds);

            // Update cache atomically with new snapshot
            var expiry = now.AddSeconds(_options.CommissionRulesCacheDurationSeconds);
            _cachedSnapshot = new CommissionRulesSnapshot(rules, expiry);
            
            return rules;
        }
    }

    private static CommissionRule? ResolveWithinScope(
        IReadOnlyList<CommissionRule> activeRules,
        CommissionRuleScopeType scopeType,
        Guid? scopeId)
    {
        if (!scopeId.HasValue || scopeId.Value == Guid.Empty)
        {
            return null;
        }

        return activeRules
            .Where(rule =>
                rule.ScopeType == scopeType &&
                rule.ScopeId == scopeId.Value)
            .OrderByDescending(rule => rule.Priority)
            .ThenByDescending(rule => rule.CreatedAt)
            .ThenByDescending(rule => rule.Id)
            .FirstOrDefault();
    }

    private static CommissionRule? ResolveGlobalRule(IReadOnlyList<CommissionRule> activeRules)
    {
        return activeRules
            .Where(rule => rule.ScopeType == CommissionRuleScopeType.Global)
            .OrderByDescending(rule => rule.Priority)
            .ThenByDescending(rule => rule.CreatedAt)
            .ThenByDescending(rule => rule.Id)
            .FirstOrDefault();
    }

    private void EnsureNoAmbiguousRules(IReadOnlyList<CommissionRule> rules)
    {
        var activeGlobalCount = rules.Count(rule => rule.ScopeType == CommissionRuleScopeType.Global);
        if (activeGlobalCount > 1)
        {
            throw new CommissionAuthorityException(
                "COMMISSION_AUTHORITY_AMBIGUOUS",
                "CommerceHub checkout is blocked because multiple active global commission rules exist.",
                new
                {
                    ActiveGlobalRuleCount = activeGlobalCount
                });
        }

        var ambiguousGroups = rules
            .GroupBy(rule => new { rule.ScopeType, rule.ScopeId, rule.Priority })
            .Where(group => group.Count() > 1)
            .ToList();

        if (ambiguousGroups.Count > 0)
        {
            var ambiguousGroup = ambiguousGroups[0];
            throw new CommissionAuthorityException(
                "COMMISSION_AUTHORITY_AMBIGUOUS",
                "CommerceHub checkout is blocked because multiple active commission rules share the same authority scope and priority.",
                new
                {
                    ambiguousGroup.Key.ScopeType,
                    ambiguousGroup.Key.ScopeId,
                    ambiguousGroup.Key.Priority,
                    Count = ambiguousGroup.Count()
                });
        }
    }

    private decimal NormalizeCommissionRate(decimal configuredRate)
    {
        if (configuredRate < 0m)
        {
            _logger.LogWarning(
                "Commission rate was negative and has been clamped to zero | ConfiguredRate: {ConfiguredRate}",
                configuredRate);

            return 0m;
        }

        if (configuredRate > 1m && configuredRate <= 100m)
        {
            return CommissionCalculator.RoundRate(configuredRate / 100m);
        }

        if (configuredRate > 1m)
        {
            _logger.LogWarning(
                "Commission rate exceeded 100%% and has been clamped to one | ConfiguredRate: {ConfiguredRate}",
                configuredRate);

            return 1m;
        }

        return CommissionCalculator.RoundRate(configuredRate);
    }
}
