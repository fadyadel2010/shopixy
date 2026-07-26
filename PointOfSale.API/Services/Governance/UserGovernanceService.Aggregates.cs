using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Enums.CommerceHubFinance;

namespace PointOfSale.API.Services.Governance;

public sealed partial class UserGovernanceService
{
    private async Task<ILookup<UserGovernanceBlacklistType, string>> LoadActiveBlacklistLookupAsync(
        CancellationToken cancellationToken)
    {
        var entries = await _dbContext.UserGovernanceBlacklistEntries
            .AsNoTracking()
            .Where(entry => entry.IsActive)
            .Select(entry => new
            {
                entry.Type,
                entry.ValueNormalized
            })
            .ToListAsync(cancellationToken);

        return entries.ToLookup(entry => entry.Type, entry => entry.ValueNormalized);
    }

    private async Task<Dictionary<Guid, OrderAggregate>> LoadCustomerOrderAggregatesAsync(
        IReadOnlyCollection<Guid> customerIds,
        CancellationToken cancellationToken)
    {
        if (customerIds.Count == 0)
        {
            return new Dictionary<Guid, OrderAggregate>();
        }

        return await _dbContext.CommerceHubMarketplaceOrders
            .AsNoTracking()
            .Where(order => customerIds.Contains(order.CustomerId) && !order.IsDeleted)
            .GroupBy(order => order.CustomerId)
            .Select(group => new
            {
                group.Key,
                Count = group.Count(),
                FailedPayments = group.Count(order =>
                    order.PaymentStatus == PaymentStatus.Failed ||
                    order.PaymentStatus == PaymentStatus.Rejected),
                TotalAmount = group.Sum(order => order.TotalAmount)
            })
            .ToDictionaryAsync(
                item => item.Key,
                item => new OrderAggregate(item.Count, item.FailedPayments, item.TotalAmount),
                cancellationToken);
    }

    private async Task<Dictionary<Guid, RefundAggregate>> LoadCustomerRefundAggregatesAsync(
        IReadOnlyCollection<Guid> customerIds,
        CancellationToken cancellationToken)
    {
        if (customerIds.Count == 0)
        {
            return new Dictionary<Guid, RefundAggregate>();
        }

        var rapidRefundWindow = DateTime.UtcNow.AddDays(-7);
        return await _dbContext.CommerceHubRefundRequests
            .AsNoTracking()
            .Where(refund => customerIds.Contains(refund.CustomerId) && !refund.IsDeleted)
            .GroupBy(refund => refund.CustomerId)
            .Select(group => new
            {
                group.Key,
                Count = group.Count(),
                TotalAmount = group.Sum(refund => refund.TotalAmount),
                RecentCount = group.Count(refund => refund.CreatedAt >= rapidRefundWindow)
            })
            .ToDictionaryAsync(
                item => item.Key,
                item => new RefundAggregate(item.Count, item.TotalAmount, item.RecentCount),
                cancellationToken);
    }

    private async Task<Dictionary<Guid, ReturnAggregate>> LoadCustomerReturnAggregatesAsync(
        IReadOnlyCollection<Guid> customerIds,
        CancellationToken cancellationToken)
    {
        if (customerIds.Count == 0)
        {
            return new Dictionary<Guid, ReturnAggregate>();
        }

        return await _dbContext.CommerceHubReturnRequests
            .AsNoTracking()
            .Where(returnRequest => customerIds.Contains(returnRequest.CustomerId) && !returnRequest.IsDeleted)
            .GroupBy(returnRequest => returnRequest.CustomerId)
            .Select(group => new
            {
                group.Key,
                Count = group.Count()
            })
            .ToDictionaryAsync(
                item => item.Key,
                item => new ReturnAggregate(item.Count),
                cancellationToken);
    }

    private async Task<Dictionary<Guid, OrderAggregate>> LoadVendorOrderAggregatesAsync(
        IReadOnlyCollection<Guid> vendorIds,
        CancellationToken cancellationToken)
    {
        if (vendorIds.Count == 0)
        {
            return new Dictionary<Guid, OrderAggregate>();
        }

        return await _dbContext.CommerceHubSubOrders
            .AsNoTracking()
            .Where(order => vendorIds.Contains(order.StoreId) && !order.IsDeleted)
            .GroupBy(order => order.StoreId)
            .Select(group => new
            {
                group.Key,
                Count = group.Count(),
                FailedPayments = group.Count(order =>
                    order.PaymentStatus == PaymentStatus.Failed ||
                    order.PaymentStatus == PaymentStatus.Rejected),
                TotalAmount = group.Sum(order => order.Total)
            })
            .ToDictionaryAsync(
                item => item.Key,
                item => new OrderAggregate(item.Count, item.FailedPayments, item.TotalAmount),
                cancellationToken);
    }

    private async Task<Dictionary<Guid, RefundAggregate>> LoadVendorRefundAggregatesAsync(
        IReadOnlyCollection<Guid> vendorIds,
        CancellationToken cancellationToken)
    {
        if (vendorIds.Count == 0)
        {
            return new Dictionary<Guid, RefundAggregate>();
        }

        var rapidRefundWindow = DateTime.UtcNow.AddDays(-7);
        return await _dbContext.CommerceHubRefundRequests
            .AsNoTracking()
            .Where(refund => vendorIds.Contains(refund.VendorId) && !refund.IsDeleted)
            .GroupBy(refund => refund.VendorId)
            .Select(group => new
            {
                group.Key,
                Count = group.Count(),
                TotalAmount = group.Sum(refund => refund.TotalAmount),
                RecentCount = group.Count(refund => refund.CreatedAt >= rapidRefundWindow)
            })
            .ToDictionaryAsync(
                item => item.Key,
                item => new RefundAggregate(item.Count, item.TotalAmount, item.RecentCount),
                cancellationToken);
    }

    private async Task<Dictionary<Guid, ReturnAggregate>> LoadVendorReturnAggregatesAsync(
        IReadOnlyCollection<Guid> vendorIds,
        CancellationToken cancellationToken)
    {
        if (vendorIds.Count == 0)
        {
            return new Dictionary<Guid, ReturnAggregate>();
        }

        return await _dbContext.CommerceHubReturnRequests
            .AsNoTracking()
            .Where(returnRequest => vendorIds.Contains(returnRequest.VendorId) && !returnRequest.IsDeleted)
            .GroupBy(returnRequest => returnRequest.VendorId)
            .Select(group => new
            {
                group.Key,
                Count = group.Count()
            })
            .ToDictionaryAsync(
                item => item.Key,
                item => new ReturnAggregate(item.Count),
                cancellationToken);
    }

    private async Task<Dictionary<Guid, decimal>> LoadVendorPayablesAsync(
        IReadOnlyCollection<Guid> vendorIds,
        CancellationToken cancellationToken)
    {
        if (vendorIds.Count == 0)
        {
            return new Dictionary<Guid, decimal>();
        }

        return await _dbContext.CommerceHubFinancialTransactions
            .AsNoTracking()
            .Where(transaction =>
                transaction.EntityType == CommerceHubFinancialEntityType.Vendor &&
                transaction.EntityId.HasValue &&
                vendorIds.Contains(transaction.EntityId.Value) &&
                transaction.FinancialBucket == CommerceHubFinancialBucket.Payable)
            .GroupBy(transaction => transaction.EntityId!.Value)
            .Select(group => new
            {
                group.Key,
                Balance = group.Sum(transaction => (decimal?)(
                    transaction.Direction == CommerceHubFinancialTransactionDirection.Credit
                        ? transaction.Amount
                        : -transaction.Amount)) ?? 0m
            })
            .ToDictionaryAsync(
                item => item.Key,
                item => RoundCurrency(item.Balance),
                cancellationToken);
    }

    private async Task<Dictionary<Guid, DateTime?>> LoadVendorLastLoginsAsync(
        IReadOnlyCollection<Guid> vendorIds,
        CancellationToken cancellationToken)
    {
        if (vendorIds.Count == 0)
        {
            return new Dictionary<Guid, DateTime?>();
        }

        return await _dbContext.Users
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(user => vendorIds.Contains(user.TenantId) && !user.IsDeleted)
            .GroupBy(user => user.TenantId)
            .Select(group => new
            {
                group.Key,
                LastLoginAtUtc = group.Max(user => user.LastLoginAt)
            })
            .ToDictionaryAsync(
                item => item.Key,
                item => item.LastLoginAtUtc,
                cancellationToken);
    }
}
