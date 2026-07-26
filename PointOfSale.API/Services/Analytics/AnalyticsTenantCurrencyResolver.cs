using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Linq;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services.Analytics;

public interface IAnalyticsTenantCurrencyResolver
{
    Task<AnalyticsCurrencyResolution> ResolveAsync(
        Guid tenantId,
        IEnumerable<Guid?> orderIds,
        CancellationToken cancellationToken = default);
}

public sealed record AnalyticsCurrencyResolution(
    string? TenantCurrency,
    IReadOnlyDictionary<Guid, string?> OrderCurrencies);

public sealed class AnalyticsTenantCurrencyResolver : IAnalyticsTenantCurrencyResolver
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<AnalyticsTenantCurrencyResolver> _logger;

    public AnalyticsTenantCurrencyResolver(
        ApplicationDbContext dbContext,
        ILogger<AnalyticsTenantCurrencyResolver> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<AnalyticsCurrencyResolution> ResolveAsync(
        Guid tenantId,
        IEnumerable<Guid?> orderIds,
        CancellationToken cancellationToken = default)
    {
        var tenantCurrency = await _dbContext.Tenants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(tenant => tenant.Id == tenantId)
            .Select(tenant => tenant.Currency)
            .FirstOrDefaultAsync(cancellationToken);

        var resolvedTenantCurrency = NormalizeCurrency(tenantCurrency);
        if (resolvedTenantCurrency == null)
        {
            _logger.LogDebug(
                "[Analytics] Tenant currency not configured | TenantId: {TenantId}",
                tenantId);
        }

        var distinctOrderIds = orderIds
            .Where(orderId => orderId.HasValue && orderId.Value != Guid.Empty)
            .Select(orderId => orderId!.Value)
            .Distinct()
            .ToArray();

        if (distinctOrderIds.Length == 0)
        {
            return new AnalyticsCurrencyResolution(resolvedTenantCurrency, new Dictionary<Guid, string>());
        }

        var orderCurrencies = await _dbContext.Orders
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(order => order.TenantId == tenantId && distinctOrderIds.Contains(order.Id))
            .Select(order => new { order.Id, order.Currency })
            .ToDictionaryAsync(
                item => item.Id,
                item => NormalizeCurrency(item.Currency),
                cancellationToken);

        return new AnalyticsCurrencyResolution(resolvedTenantCurrency, orderCurrencies);
    }

    private static string? NormalizeCurrency(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().ToUpperInvariant();
    }
}
