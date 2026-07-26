using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PointOfSale.API.Configuration;
using PointOfSale.API.DTOs.CommerceHub;
using PointOfSale.API.DTOs.Customers;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Financial;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services.CommerceHub;

public interface ICommerceHubCustomerOrderService
{
    Task<PagedResult<CustomerOrderSummaryDto>> GetOrdersAsync(
        Guid customerId,
        CustomerOrdersQueryDto query,
        CancellationToken cancellationToken = default);

    Task<CustomerOrderDetailsDto?> GetOrderAsync(
        Guid customerId,
        Guid orderId,
        CancellationToken cancellationToken = default);
}

public class CommerceHubCustomerOrderService : ICommerceHubCustomerOrderService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly RefundSettings _refundSettings;
    private readonly IRefundEvidenceService _refundEvidenceService;
    private readonly ICommerceHubPaymentRecoveryAuthorityService _paymentRecoveryAuthorityService;

    public CommerceHubCustomerOrderService(
        ApplicationDbContext dbContext,
        IOptions<RefundSettings> refundSettings,
        IRefundEvidenceService refundEvidenceService,
        ICommerceHubPaymentRecoveryAuthorityService paymentRecoveryAuthorityService)
    {
        _dbContext = dbContext;
        _refundSettings = refundSettings.Value;
        _refundEvidenceService = refundEvidenceService;
        _paymentRecoveryAuthorityService = paymentRecoveryAuthorityService;
    }

    public async Task<PagedResult<CustomerOrderSummaryDto>> GetOrdersAsync(
        Guid customerId,
        CustomerOrdersQueryDto query,
        CancellationToken cancellationToken = default)
    {
        var baseQuery = _dbContext.CommerceHubMarketplaceOrders
            .AsNoTracking()
            .Where(order =>
                !order.IsDeleted &&
                order.CustomerId == customerId &&
                order.Currency == FinancialCurrencyGuard.PlatformBaseCurrencyCode &&
                !order.SubOrders.Any(subOrder => subOrder.Currency != FinancialCurrencyGuard.PlatformBaseCurrencyCode));

        var totalCount = await baseQuery.CountAsync(cancellationToken);

        var orderIds = await baseQuery
            .OrderByDescending(order => order.CreatedAt)
            .ThenByDescending(order => order.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(order => order.Id)
            .ToListAsync(cancellationToken);

        if (orderIds.Count == 0)
        {
            return new PagedResult<CustomerOrderSummaryDto>(new List<CustomerOrderSummaryDto>(), query.Page, query.PageSize, totalCount);
        }

        var orders = await _dbContext.CommerceHubMarketplaceOrders
            .AsNoTracking()
            .AsSplitQuery()
            .Where(order => orderIds.Contains(order.Id))
            .Include(order => order.SubOrders.OrderBy(subOrder => subOrder.OrderNumber))
            .ThenInclude(subOrder => subOrder.Items)
            .ToListAsync(cancellationToken);

        var refundLookup = await LoadRefundLookupAsync(
            customerId,
            orders.SelectMany(order => order.SubOrders).SelectMany(subOrder => subOrder.Items),
            cancellationToken);

        var evidenceLookup = await _refundEvidenceService.GetEvidenceLookupAsync(
            refundLookup.Values.SelectMany(refunds => refunds).Select(refund => refund.Id),
            cancellationToken);
        var (executionLookup, trackedRefundLookup) = await LoadPayoutConfirmationLookupsAsync(
            refundLookup.Values.SelectMany(refunds => refunds).Select(refund => refund.Id),
            cancellationToken);

        var ordered = orderIds
            .Join(orders, id => id, order => order.Id, (_, order) => order)
            .Select(order => MapSummary(order, refundLookup, evidenceLookup, executionLookup, trackedRefundLookup))
            .ToList();

        return new PagedResult<CustomerOrderSummaryDto>(ordered, query.Page, query.PageSize, totalCount);
    }

    public async Task<CustomerOrderDetailsDto?> GetOrderAsync(
        Guid customerId,
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        var order = await _dbContext.CommerceHubMarketplaceOrders
            .AsNoTracking()
            .AsSplitQuery()
            .Where(current =>
                !current.IsDeleted &&
                current.CustomerId == customerId &&
                current.Id == orderId &&
                current.Currency == FinancialCurrencyGuard.PlatformBaseCurrencyCode &&
                !current.SubOrders.Any(subOrder => subOrder.Currency != FinancialCurrencyGuard.PlatformBaseCurrencyCode))
            .Include(current => current.SubOrders.OrderBy(subOrder => subOrder.OrderNumber))
            .ThenInclude(subOrder => subOrder.Items)
            .FirstOrDefaultAsync(cancellationToken);

        if (order == null)
        {
            return null;
        }

        var subOrderLookup = order.SubOrders.ToDictionary(subOrder => subOrder.Id);
        var histories = await _dbContext.CommerceHubSubOrderStatusHistory
            .AsNoTracking()
            .Where(history => !history.IsDeleted && subOrderLookup.Keys.Contains(history.SubOrderId))
            .OrderBy(history => history.ChangedAtUtc)
            .ToListAsync(cancellationToken);

        var refundLookup = await LoadRefundLookupAsync(
            customerId,
            order.SubOrders.SelectMany(subOrder => subOrder.Items),
            cancellationToken);

        var evidenceLookup = await _refundEvidenceService.GetEvidenceLookupAsync(
            refundLookup.Values.SelectMany(refunds => refunds).Select(refund => refund.Id),
            cancellationToken);
        var (executionLookup, trackedRefundLookup) = await LoadPayoutConfirmationLookupsAsync(
            refundLookup.Values.SelectMany(refunds => refunds).Select(refund => refund.Id),
            cancellationToken);

        var paymentRecoveryAuthority = await _paymentRecoveryAuthorityService.EvaluateAsync(
            order.Id,
            order.PaymentMethodCode,
            order.PaymentStatus,
            order.Status,
            cancellationToken);

        return MapDetails(
            order,
            histories,
            subOrderLookup,
            refundLookup,
            evidenceLookup,
            executionLookup,
            trackedRefundLookup,
            paymentRecoveryAuthority);
    }

    private async Task<Dictionary<Guid, IReadOnlyList<CommerceHubRefundRequest>>> LoadRefundLookupAsync(
        Guid customerId,
        IEnumerable<CommerceHubSubOrderItem> orderItems,
        CancellationToken cancellationToken)
    {
        var orderItemIds = orderItems
            .Select(item => item.Id)
            .Distinct()
            .ToList();

        if (orderItemIds.Count == 0)
        {
            return new Dictionary<Guid, IReadOnlyList<CommerceHubRefundRequest>>();
        }

        var refundRequests = await _dbContext.CommerceHubRefundRequests
            .AsNoTracking()
            .Where(refundRequest =>
                refundRequest.CustomerId == customerId &&
                orderItemIds.Contains(refundRequest.OrderItemId) &&
                !refundRequest.IsDeleted)
            .OrderByDescending(refundRequest => refundRequest.CreatedAt)
            .ThenByDescending(refundRequest => refundRequest.Id)
            .ToListAsync(cancellationToken);

        return refundRequests
            .GroupBy(refundRequest => refundRequest.OrderItemId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<CommerceHubRefundRequest>)group.ToList());
    }

    private CustomerOrderSummaryDto MapSummary(
        CommerceHubMarketplaceOrder order,
        IReadOnlyDictionary<Guid, IReadOnlyList<CommerceHubRefundRequest>> refundLookup,
        IReadOnlyDictionary<Guid, IReadOnlyList<RefundEvidenceMediaDto>> evidenceLookup,
        IReadOnlyDictionary<Guid, CommerceHubRefundExecution> executionLookup,
        IReadOnlyDictionary<Guid, CommerceHubRefund> trackedRefundLookup)
    {
        var nowUtc = DateTime.UtcNow;

        return new CustomerOrderSummaryDto
        {
            OrderId = order.Id,
            OrderNumber = order.OrderNumber,
            CreatedAt = order.CreatedAt,
            TotalAmount = order.TotalAmount,
            Currency = order.Currency,
            Status = order.Status,
            SubOrders = order.SubOrders
                .OrderBy(subOrder => subOrder.OrderNumber, StringComparer.OrdinalIgnoreCase)
                .Select(subOrder => MapVendor(subOrder, refundLookup, evidenceLookup, executionLookup, trackedRefundLookup, nowUtc))
                .ToList()
        };
    }

    private CustomerOrderDetailsDto MapDetails(
        CommerceHubMarketplaceOrder order,
        IReadOnlyCollection<CommerceHubSubOrderStatusHistory> histories,
        IReadOnlyDictionary<Guid, CommerceHubSubOrder> subOrderLookup,
        IReadOnlyDictionary<Guid, IReadOnlyList<CommerceHubRefundRequest>> refundLookup,
        IReadOnlyDictionary<Guid, IReadOnlyList<RefundEvidenceMediaDto>> evidenceLookup,
        IReadOnlyDictionary<Guid, CommerceHubRefundExecution> executionLookup,
        IReadOnlyDictionary<Guid, CommerceHubRefund> trackedRefundLookup,
        CommerceHubPaymentRecoveryAuthorityDecision paymentRecoveryAuthority)
    {
        var nowUtc = DateTime.UtcNow;
        var timeline = new List<CustomerOrderTimelineEntryDto>
        {
            new()
            {
                At = order.CreatedAt,
                Type = "OrderCreated",
                Description = "Order created"
            }
        };

        timeline.AddRange(histories.Select(history =>
        {
            subOrderLookup.TryGetValue(history.SubOrderId, out var subOrder);

            return new CustomerOrderTimelineEntryDto
            {
                At = history.ChangedAtUtc,
                Type = "SubOrderStatusChanged",
                Description = history.Reason ?? $"Vendor order moved from {history.PreviousStatus} to {history.NewStatus}.",
                PreviousStatus = history.PreviousStatus.ToString(),
                NewStatus = history.NewStatus.ToString(),
                VendorName = subOrder?.StoreName
            };
        }));

        return new CustomerOrderDetailsDto
        {
            OrderId = order.Id,
            OrderNumber = order.OrderNumber,
            CreatedAt = order.CreatedAt,
            UpdatedAt = order.UpdatedAt,
            Currency = order.Currency,
            Subtotal = order.Subtotal,
            ShippingTotal = order.ShippingTotal,
            TotalAmount = order.TotalAmount,
            Status = order.Status,
            PaymentStatus = order.PaymentStatus,
            PaymentMethodCode = order.PaymentMethodCode,
            PaymentMethodName = order.PaymentMethodName,
            PaymentReference = order.PaymentReference,
            PaidAt = order.PaidAt,
            CanRetryPayment = paymentRecoveryAuthority.CanRetryPayment,
            PaymentRecoveryMessage = paymentRecoveryAuthority.PaymentRecoveryMessage,
            FullName = order.FullName,
            PhoneNumber = order.PhoneNumber,
            AddressLine = order.AddressLine,
            City = order.City,
            Area = order.Area,
            SubOrders = order.SubOrders
                .OrderBy(subOrder => subOrder.OrderNumber, StringComparer.OrdinalIgnoreCase)
                .Select(subOrder => MapVendor(subOrder, refundLookup, evidenceLookup, executionLookup, trackedRefundLookup, nowUtc))
                .ToList(),
            Timeline = timeline
                .OrderByDescending(entry => entry.At)
                .ToList()
        };
    }

    private CustomerOrderVendorDto MapVendor(
        CommerceHubSubOrder subOrder,
        IReadOnlyDictionary<Guid, IReadOnlyList<CommerceHubRefundRequest>> refundLookup,
        IReadOnlyDictionary<Guid, IReadOnlyList<RefundEvidenceMediaDto>> evidenceLookup,
        IReadOnlyDictionary<Guid, CommerceHubRefundExecution> executionLookup,
        IReadOnlyDictionary<Guid, CommerceHubRefund> trackedRefundLookup,
        DateTime nowUtc)
    {
        return new CustomerOrderVendorDto
        {
            SubOrderId = subOrder.Id,
            VendorName = subOrder.StoreName,
            OrderNumber = subOrder.OrderNumber,
            Status = subOrder.Status,
            PaymentStatus = subOrder.PaymentStatus,
            TotalAmount = subOrder.Total,
            ShippingAmount = subOrder.Shipping,
            Items = subOrder.Items
                .OrderBy(item => item.ProductName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.VariantName, StringComparer.OrdinalIgnoreCase)
                .Select(item =>
                {
                    var refundRequests = refundLookup.TryGetValue(item.Id, out var matchedRefunds)
                        ? matchedRefunds
                        : Array.Empty<CommerceHubRefundRequest>();
                    var eligibility = CustomerRefundRequestPolicy.Evaluate(
                        item.Status,
                        item.DeliveredAt,
                        item.Quantity,
                        refundRequests,
                        _refundSettings,
                        nowUtc);

                    return new CustomerOrderItemDto
                    {
                        OrderItemId = item.Id,
                        ProductName = item.ProductName,
                        Variant = item.VariantName,
                        Quantity = item.Quantity,
                        Price = item.UnitPrice,
                        Total = item.Total,
                        Status = item.Status,
                        CanRefund = eligibility.CanRefund,
                        MaxRefundableQuantity = eligibility.MaxRefundableQuantity,
                        RefundDisabledReason = eligibility.DisabledReason,
                        ExistingRefunds = refundRequests
                            .Select(refundRequest => new CustomerOrderItemExistingRefundDto
                            {
                                RefundId = refundRequest.Id,
                                Quantity = refundRequest.Quantity,
                                Amount = refundRequest.TotalAmount,
                                Status = CommerceHubRefundRequestWorkflowPolicy.NormalizeStatus(refundRequest.Status),
                                Reason = CustomerRefundService.BuildReasonSummary(refundRequest.ReasonCode, refundRequest.ReasonText),
                                ReasonCode = refundRequest.ReasonCode,
                                ReasonText = refundRequest.ReasonText,
                                Payout = CustomerRefundService.BuildPayoutSnapshot(
                                    refundRequest,
                                    executionLookup.GetValueOrDefault(refundRequest.Id),
                                    trackedRefundLookup.GetValueOrDefault(refundRequest.Id)),
                                MediaUrls = evidenceLookup.TryGetValue(refundRequest.Id, out var media)
                                    ? media.Select(item => item.Url).ToList()
                                    : new List<string>(),
                                Media = evidenceLookup.TryGetValue(refundRequest.Id, out var refundMedia)
                                    ? refundMedia.ToList()
                                    : new List<RefundEvidenceMediaDto>(),
                                CreatedAt = refundRequest.CreatedAt,
                                UpdatedAt = refundRequest.UpdatedAt
                            })
                            .ToList(),
                        ImageUrl = item.ImageUrl
                    };
                })
                .ToList()
        };
    }

    private async Task<(Dictionary<Guid, CommerceHubRefundExecution> ExecutionLookup, Dictionary<Guid, CommerceHubRefund> TrackedRefundLookup)>
        LoadPayoutConfirmationLookupsAsync(IEnumerable<Guid> refundIds, CancellationToken cancellationToken)
    {
        var ids = refundIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return (new Dictionary<Guid, CommerceHubRefundExecution>(), new Dictionary<Guid, CommerceHubRefund>());
        }

        var executionLookup = await _dbContext.CommerceHubRefundExecutions
            .AsNoTracking()
            .Where(item => ids.Contains(item.RefundId) && !item.IsDeleted)
            .ToDictionaryAsync(item => item.RefundId, cancellationToken);

        var trackedRefundLookup = await _dbContext.CommerceHubRefunds
            .AsNoTracking()
            .Where(item => item.RefundRequestId.HasValue && ids.Contains(item.RefundRequestId.Value) && !item.IsDeleted)
            .ToDictionaryAsync(item => item.RefundRequestId!.Value, cancellationToken);

        return (executionLookup, trackedRefundLookup);
    }
}
