using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Infrastructure.Analytics;

public interface IStorefrontAnalyticsAuthorityReader
{
    Task<StorefrontCommerceAnalyticsSnapshot> GetCommerceSnapshotAsync(
        Guid tenantId,
        DateTime startUtc,
        DateTime endUtc,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<Guid, decimal>> GetNetRevenueByOrderAsync(
        Guid tenantId,
        IEnumerable<Guid> orderIds,
        DateTime asOfUtc,
        CancellationToken cancellationToken = default);
}

public sealed record StorefrontCommerceAnalyticsSnapshot(
    IReadOnlyList<Guid> PurchaseOrderIds,
    IReadOnlyDictionary<Guid, decimal> NetRevenueByOrder)
{
    public long PurchaseCount => PurchaseOrderIds.Count;
    public decimal NetRevenue => NetRevenueByOrder.Values.Sum();
}

/// <summary>
/// Reads storefront purchase and revenue truth from PostgreSQL commerce and accounting domains.
/// Redis and MongoDB analytics projections must never replace this reader for financial metrics.
/// </summary>
public sealed class StorefrontAnalyticsAuthorityReader : IStorefrontAnalyticsAuthorityReader
{
    private static readonly string[] StorefrontRevenueSourceTypes =
    [
        "Order_Payment",
        "Order_COD",
        "Order_Wallet",
        "Order_UnresolvedPaymentClearance"
    ];

    private readonly ApplicationDbContext _dbContext;

    public StorefrontAnalyticsAuthorityReader(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<StorefrontCommerceAnalyticsSnapshot> GetCommerceSnapshotAsync(
        Guid tenantId,
        DateTime startUtc,
        DateTime endUtc,
        CancellationToken cancellationToken = default)
    {
        ValidateRange(tenantId, startUtc, endUtc);

        var postedPurchaseIds = await _dbContext.LedgerEntries
            .AsNoTracking()
            .Where(entry =>
                entry.TenantId == tenantId &&
                (entry.JournalEntry.Status == JournalStatus.Posted ||
                 entry.JournalEntry.Status == JournalStatus.Reversed) &&
                entry.JournalEntry.PostedAtUtc >= startUtc &&
                entry.JournalEntry.PostedAtUtc < endUtc &&
                StorefrontRevenueSourceTypes.Contains(entry.JournalEntry.SourceType) &&
                entry.Account.AccountCode == AccountCodes.EcommerceSales &&
                entry.CreditAmount > 0m)
            .Select(entry => entry.JournalEntry.SourceId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var orderIds = await _dbContext.Orders
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(order =>
                order.TenantId == tenantId &&
                order.Source == OrderSource.Ecommerce &&
                postedPurchaseIds.Contains(order.Id))
            .Select(order => order.Id)
            .ToListAsync(cancellationToken);

        var revenue = await GetNetRevenueByOrderAsync(
            tenantId,
            orderIds,
            endUtc,
            cancellationToken);

        return new StorefrontCommerceAnalyticsSnapshot(orderIds, revenue);
    }

    public async Task<IReadOnlyDictionary<Guid, decimal>> GetNetRevenueByOrderAsync(
        Guid tenantId,
        IEnumerable<Guid> orderIds,
        DateTime asOfUtc,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("TenantId is required.", nameof(tenantId));
        }

        var distinctOrderIds = orderIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToArray();

        if (distinctOrderIds.Length == 0)
        {
            return new Dictionary<Guid, decimal>();
        }

        var revenue = await _dbContext.LedgerEntries
            .AsNoTracking()
            .Where(entry =>
                entry.TenantId == tenantId &&
                distinctOrderIds.Contains(entry.JournalEntry.SourceId) &&
                (entry.JournalEntry.Status == JournalStatus.Posted ||
                 entry.JournalEntry.Status == JournalStatus.Reversed) &&
                entry.JournalEntry.PostedAtUtc < asOfUtc &&
                entry.Account.AccountCode == AccountCodes.EcommerceSales)
            .GroupBy(entry => entry.JournalEntry.SourceId)
            .Select(group => new
            {
                OrderId = group.Key,
                NetRevenue = group.Sum(entry => entry.CreditAmount - entry.DebitAmount)
            })
            .ToDictionaryAsync(
                item => item.OrderId,
                item => item.NetRevenue,
                cancellationToken);

        foreach (var orderId in distinctOrderIds)
        {
            revenue.TryAdd(orderId, 0m);
        }

        return revenue;
    }

    private static void ValidateRange(Guid tenantId, DateTime startUtc, DateTime endUtc)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("TenantId is required.", nameof(tenantId));
        }

        if (startUtc.Kind != DateTimeKind.Utc || endUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Analytics ranges must use UTC timestamps.");
        }

        if (endUtc <= startUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(endUtc), "EndUtc must be later than StartUtc.");
        }
    }
}
