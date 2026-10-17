using Amazon.S3;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text.Json;
using PointOfSale.API.DTOs.CommerceHub;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.API.Services;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Payments;
using PointOfSale.Core.Pagination;
using PointOfSale.Core.Policies;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Data.Repositories;

namespace PointOfSale.API.Services.Admin;

/// <summary>
/// Admin Order Service Implementation.
/// 
/// PHASE 6.1: ADMIN ORDER MANAGEMENT
/// 
/// ARCHITECTURAL RULES (STRICT):
/// - PostgreSQL is the ONLY source of truth for orders
/// - MongoDB is NOT used for admin operations
/// - Events are reused from existing order/payment/shipment events
/// - Prefer rejecting invalid actions over auto-fixing
/// 
/// TENANT ISOLATION - CRITICAL:
/// Order.TenantId is the canonical seller/store authority for standard orders.
/// Customer, shipment, and payment records are optional related evidence and must
/// never determine whether the owning tenant can see its order.
/// </summary>
public class AdminOrderService : IAdminOrderService
{
    private readonly ApplicationDbContext _context;
    private readonly IEventPublisher _eventPublisher;
    private readonly IRefundEvidenceService _refundEvidenceService;
    private readonly ICouponValidationService _couponValidationService;
    private readonly IPaymentLifecycleService _paymentLifecycleService;
    private readonly IProductVariantStockRepository _stockRepository;
    private readonly IR2StorageService _storageService;
    private readonly ILogger<AdminOrderService> _logger;

    public AdminOrderService(
        ApplicationDbContext context,
        IEventPublisher eventPublisher,
        IRefundEvidenceService refundEvidenceService,
        ICouponValidationService couponValidationService,
        IPaymentLifecycleService paymentLifecycleService,
        IProductVariantStockRepository stockRepository,
        IR2StorageService storageService,
        ILogger<AdminOrderService> logger)
    {
        _context = context;
        _eventPublisher = eventPublisher;
        _refundEvidenceService = refundEvidenceService;
        _couponValidationService = couponValidationService;
        _paymentLifecycleService = paymentLifecycleService;
        _stockRepository = stockRepository;
        _storageService = storageService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<AdminPagedResult<AdminOrderListItemDto>> ListOrdersAsync(
        Guid tenantId,
        AdminOrderListQuery query,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "ListOrders | TenantId: {TenantId} | Page: {Page} | PageSize: {PageSize} | Status: {Status} | PaymentStatus: {PaymentStatus} | CorrelationId: {CorrelationId} | Action: LIST",
            tenantId, query.Page, query.PageSize, query.Status, query.PaymentStatus, correlationId);

        query.Page = PaginationGuard.NormalizePage(query.Page);
        query.PageSize = PaginationGuard.NormalizePageSize(query.PageSize);
        query.Source ??= OrderSource.Ecommerce;
        if (query.Source is not OrderSource.Ecommerce and not OrderSource.Marketplace)
        {
            throw new ArgumentOutOfRangeException(
                nameof(query.Source),
                query.Source,
                "Admin order listing admits only Ecommerce or Marketplace scope.");
        }

        var standardOrdersQuery = _context.Orders
            .Include(o => o.Customer)
            .Where(o =>
                o.TenantId == tenantId &&
                o.Source == OrderSource.Ecommerce)
            .AsNoTracking();

        if (query.Source == OrderSource.Marketplace)
        {
            standardOrdersQuery = standardOrdersQuery.Where(_ => false);
        }

        if (query.Status.HasValue)
        {
            standardOrdersQuery = standardOrdersQuery.Where(o => o.Status == query.Status.Value);
        }

        if (query.PaymentStatus.HasValue)
        {
            standardOrdersQuery = standardOrdersQuery.Where(o => o.PaymentStatus == query.PaymentStatus.Value);
        }

        if (query.FromDate.HasValue)
        {
            var fromDateUtc = DateTime.SpecifyKind(query.FromDate.Value.Date, DateTimeKind.Utc);
            standardOrdersQuery = standardOrdersQuery.Where(o => o.CreatedAt >= fromDateUtc);
        }

        if (query.ToDate.HasValue)
        {
            var toDateUtc = DateTime.SpecifyKind(query.ToDate.Value.Date.AddDays(1), DateTimeKind.Utc);
            standardOrdersQuery = standardOrdersQuery.Where(o => o.CreatedAt < toDateUtc);
        }

        if (query.CustomerId.HasValue)
        {
            standardOrdersQuery = standardOrdersQuery.Where(o => o.CustomerId == query.CustomerId.Value);
        }

        var search = query.Search?.Trim();
        if (!string.IsNullOrWhiteSpace(search))
        {
            standardOrdersQuery = standardOrdersQuery.Where(o =>
                o.OrderNumber.Contains(search) ||
                (o.Customer != null && o.Customer.Name != null && o.Customer.Name.Contains(search)) ||
                (o.Customer != null && o.Customer.Email != null && o.Customer.Email.Contains(search)) ||
                (o.GuestName != null && o.GuestName.Contains(search)) ||
                (o.GuestEmail != null && o.GuestEmail.Contains(search)) ||
                (o.GuestPhone != null && o.GuestPhone.Contains(search)));
        }

        var standardRowsQuery = standardOrdersQuery
            .Select(o => new AdminOrderListQueryRow
            {
                TenantId = o.TenantId,
                OrderId = o.Id,
                OrderNumber = o.OrderNumber,
                CustomerId = o.CustomerId,
                CustomerName = o.Customer != null ? o.Customer.Name : o.GuestName,
                TotalAmount = o.Total,
                SubtotalAmount = o.Subtotal,
                ShippingAmount = o.ShippingCost,
                ItemCount = o.Items.Count,
                TotalQuantity = o.Items.Sum(item => item.Quantity),
                ProductSummary = o.Items.OrderBy(item => item.Id).Select(item => item.ProductName).FirstOrDefault(),
                Currency = o.Currency,
                Status = o.Status,
                PaymentStatus = o.PaymentStatus,
                Source = o.Source,
                SourceOrderId = o.SourceOrderId,
                MarketplaceOrderId = null,
                SourceLabel = o.Source == OrderSource.Marketplace ? "Marketplace" : null,
                IsMarketplaceOrder = false,
                CreatedAtUtc = o.CreatedAt
            });

        IQueryable<CommerceHubSubOrder> marketplaceOrdersQuery = _context.CommerceHubSubOrders
            .AsNoTracking()
            .Where(subOrder =>
                !subOrder.IsDeleted &&
                subOrder.StoreId == tenantId &&
                !subOrder.MarketplaceOrder.IsDeleted);

        if (query.Source == OrderSource.Ecommerce)
        {
            marketplaceOrdersQuery = marketplaceOrdersQuery.Where(_ => false);
        }

        if (query.CustomerId.HasValue)
        {
            marketplaceOrdersQuery = marketplaceOrdersQuery.Where(_ => false);
        }

        if (query.Status.HasValue)
        {
            marketplaceOrdersQuery = marketplaceOrdersQuery.Where(subOrder => subOrder.Status == query.Status.Value);
        }

        if (query.PaymentStatus.HasValue)
        {
            marketplaceOrdersQuery = marketplaceOrdersQuery.Where(subOrder => subOrder.PaymentStatus == query.PaymentStatus.Value);
        }

        if (query.FromDate.HasValue)
        {
            var fromDateUtc = DateTime.SpecifyKind(query.FromDate.Value.Date, DateTimeKind.Utc);
            marketplaceOrdersQuery = marketplaceOrdersQuery.Where(subOrder => subOrder.CreatedAt >= fromDateUtc);
        }

        if (query.ToDate.HasValue)
        {
            var toDateUtc = DateTime.SpecifyKind(query.ToDate.Value.Date.AddDays(1), DateTimeKind.Utc);
            marketplaceOrdersQuery = marketplaceOrdersQuery.Where(subOrder => subOrder.CreatedAt < toDateUtc);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            marketplaceOrdersQuery = marketplaceOrdersQuery.Where(subOrder =>
                subOrder.OrderNumber.Contains(search) ||
                subOrder.MarketplaceOrder.FullName.Contains(search) ||
                subOrder.MarketplaceOrder.PhoneNumber.Contains(search));
        }

        var marketplaceRowsQuery = marketplaceOrdersQuery
            .Select(subOrder => new AdminOrderListQueryRow
            {
                TenantId = subOrder.StoreId,
                OrderId = subOrder.Id,
                OrderNumber = subOrder.OrderNumber,
                CustomerId = null,
                CustomerName = subOrder.MarketplaceOrder.FullName,
                TotalAmount = subOrder.Total,
                SubtotalAmount = subOrder.Subtotal,
                ShippingAmount = subOrder.Shipping,
                ItemCount = subOrder.Items.Count,
                TotalQuantity = subOrder.Items.Sum(item => item.Quantity),
                ProductSummary = subOrder.Items.OrderBy(item => item.Id).Select(item => item.ProductName).FirstOrDefault(),
                Currency = subOrder.Currency,
                Status = subOrder.Status,
                PaymentStatus = subOrder.PaymentStatus,
                Source = OrderSource.Marketplace,
                SourceOrderId = null,
                MarketplaceOrderId = subOrder.MarketplaceOrderId,
                SourceLabel = "Marketplace",
                IsMarketplaceOrder = true,
                CreatedAtUtc = subOrder.CreatedAt
            });

        var combinedQuery = standardRowsQuery.Concat(marketplaceRowsQuery);
        var totalCount = await combinedQuery.CountAsync(cancellationToken);
        var totalPages = (int)Math.Ceiling(totalCount / (double)query.PageSize);
        query.Page = totalPages == 0 ? 1 : Math.Min(query.Page, totalPages);
        var skip = PaginationGuard.GetSkip(query.Page, query.PageSize);
        var orderedQuery = ApplyOrderListSorting(combinedQuery, query.SortBy, query.SortDirection);
        var rows = await orderedQuery
            .Skip(skip)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);
        var orders = rows.Select(MapOrderListRow).ToList();

        await AttachAffiliateAttributionEvidenceAsync(orders, tenantId, cancellationToken);
        await AttachMarketplaceLineageEvidenceAsync(orders, tenantId, cancellationToken);

        _logger.LogInformation(
            "ListOrders | TenantId: {TenantId} | TotalCount: {TotalCount} | ReturnedCount: {ReturnedCount} | CorrelationId: {CorrelationId}",
            tenantId, totalCount, orders.Count, correlationId);

        return new AdminPagedResult<AdminOrderListItemDto>(orders, query.Page, query.PageSize, totalCount);
    }

    private static IOrderedQueryable<AdminOrderListQueryRow> ApplyOrderListSorting(
        IQueryable<AdminOrderListQueryRow> query,
        string sortBy,
        string sortDirection)
    {
        var descending = string.Equals(sortDirection, "desc", StringComparison.OrdinalIgnoreCase);
        return sortBy.ToLowerInvariant() switch
        {
            "ordernumber" => descending
                ? query.OrderByDescending(order => order.OrderNumber).ThenBy(order => order.OrderId)
                : query.OrderBy(order => order.OrderNumber).ThenBy(order => order.OrderId),
            "customername" => descending
                ? query.OrderByDescending(order => order.CustomerName).ThenBy(order => order.OrderId)
                : query.OrderBy(order => order.CustomerName).ThenBy(order => order.OrderId),
            "totalamount" => descending
                ? query.OrderByDescending(order => order.TotalAmount).ThenBy(order => order.OrderId)
                : query.OrderBy(order => order.TotalAmount).ThenBy(order => order.OrderId),
            "status" => descending
                ? query.OrderByDescending(order => order.Status).ThenBy(order => order.OrderId)
                : query.OrderBy(order => order.Status).ThenBy(order => order.OrderId),
            "paymentstatus" => descending
                ? query.OrderByDescending(order => order.PaymentStatus).ThenBy(order => order.OrderId)
                : query.OrderBy(order => order.PaymentStatus).ThenBy(order => order.OrderId),
            _ => descending
                ? query.OrderByDescending(order => order.CreatedAtUtc).ThenBy(order => order.OrderId)
                : query.OrderBy(order => order.CreatedAtUtc).ThenBy(order => order.OrderId),
        };
    }

    private static AdminOrderListItemDto MapOrderListRow(AdminOrderListQueryRow row) => new()
    {
        TenantId = row.TenantId,
        OrderId = row.OrderId,
        OrderNumber = row.OrderNumber,
        CustomerId = row.CustomerId,
        CustomerName = row.CustomerName,
        TotalAmount = row.TotalAmount,
        SubtotalAmount = row.SubtotalAmount,
        ShippingAmount = row.ShippingAmount,
        ItemCount = row.ItemCount,
        TotalQuantity = row.TotalQuantity,
        ProductSummary = row.ProductSummary,
        Currency = row.Currency,
        Status = row.Status,
        PaymentStatus = row.PaymentStatus,
        Source = row.Source,
        SourceOrderId = row.SourceOrderId ?? row.MarketplaceOrderId?.ToString(),
        SourceLabel = row.SourceLabel,
        IsMarketplaceOrder = row.IsMarketplaceOrder,
        MarketplaceLineage = null,
        CreatedAtUtc = row.CreatedAtUtc
    };

    private sealed class AdminOrderListQueryRow
    {
        public Guid TenantId { get; init; }
        public Guid OrderId { get; init; }
        public string OrderNumber { get; init; } = string.Empty;
        public Guid? CustomerId { get; init; }
        public string? CustomerName { get; init; }
        public decimal TotalAmount { get; init; }
        public decimal SubtotalAmount { get; init; }
        public decimal ShippingAmount { get; init; }
        public int ItemCount { get; init; }
        public int TotalQuantity { get; init; }
        public string? ProductSummary { get; init; }
        public string Currency { get; init; } = string.Empty;
        public OrderStatus Status { get; init; }
        public PaymentStatus PaymentStatus { get; init; }
        public OrderSource Source { get; init; }
        public string? SourceOrderId { get; init; }
        public Guid? MarketplaceOrderId { get; init; }
        public string? SourceLabel { get; init; }
        public bool IsMarketplaceOrder { get; init; }
        public DateTime CreatedAtUtc { get; init; }
    }

    /// <inheritdoc />
    public async Task<AdminOrderDetailsDto?> GetOrderDetailsAsync(
        Guid tenantId,
        Guid orderId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "GetOrderDetails | TenantId: {TenantId} | OrderId: {OrderId} | CorrelationId: {CorrelationId} | Action: VIEW",
            tenantId, orderId, correlationId);

        // First, verify ownership
        var orderExists = await VerifyOrderOwnershipAsync(tenantId, orderId, cancellationToken);
        if (!orderExists)
        {
            _logger.LogWarning(
                "GetOrderDetails | TenantId: {TenantId} | OrderId: {OrderId} | NOT_FOUND_OR_NOT_OWNED | CorrelationId: {CorrelationId}",
                tenantId, orderId, correlationId);
            return null;
        }

        var order = await _context.Orders
            .Where(o =>
                o.Id == orderId &&
                o.TenantId == tenantId &&
                o.Source == OrderSource.Ecommerce)
            // xmin is a shadow property. Detached entities discard its loaded
            // value, producing RowVersion="0" and unusable If-Match evidence.
            .AsTracking()
            .AsSplitQuery()
            .Include(o => o.Customer)
            .Include(o => o.OnlineCustomer)
            .Include(o => o.Items)
            .Include(o => o.CheckoutFieldSnapshots)
            .Include(o => o.Coupon)
            .FirstOrDefaultAsync(cancellationToken);

        if (order == null)
        {
            return null;
        }

        // Load payment intents (already tenant-filtered by ownership)
        var paymentIntents = await _context.Set<PaymentIntent>()
            .Where(pi => pi.OrderId == orderId && pi.TenantId == tenantId)
            .AsNoTracking()
            .OrderBy(pi => pi.CreatedAt)
            .ThenBy(pi => pi.Id)
            .ToListAsync(cancellationToken);

        // Load shipment (already tenant-filtered by ownership)
        var shipment = await _context.Shipments
            .Where(s => s.OrderId == orderId && s.TenantId == tenantId)
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);

        var affiliateAttribution = await LoadAffiliateAttributionEvidenceAsync(
            tenantId,
            order,
            cancellationToken);

        var orderDetails = MapToDetailsDto(order, paymentIntents, shipment, affiliateAttribution);

        _logger.LogInformation(
            "GetOrderDetails | TenantId: {TenantId} | OrderId: {OrderId} | OrderNumber: {OrderNumber} | Status: {Status} | CorrelationId: {CorrelationId}",
            tenantId, orderId, order.OrderNumber, order.Status, correlationId);

        return orderDetails;
    }

    public async Task<AdminOrderDetailsDto?> GetCommerceHubVendorOrderDetailsAsync(
        Guid tenantId,
        Guid subOrderId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "GetCommerceHubVendorOrderDetails | TenantId: {TenantId} | SubOrderId: {SubOrderId} | CorrelationId: {CorrelationId} | Action: VIEW",
            tenantId, subOrderId, correlationId);

        var marketplaceSubOrder = await _context.CommerceHubSubOrders
            .AsNoTracking()
            .Include(subOrder => subOrder.MarketplaceOrder)
            .Include(subOrder => subOrder.Items)
            .FirstOrDefaultAsync(
                subOrder =>
                    subOrder.Id == subOrderId &&
                    !subOrder.IsDeleted &&
                    subOrder.StoreId == tenantId &&
                    !subOrder.MarketplaceOrder.IsDeleted,
                cancellationToken);

        return marketplaceSubOrder == null
            ? null
            : await MapMarketplaceSubOrderToDetailsDtoAsync(marketplaceSubOrder, cancellationToken);
    }

    public async Task<AdminOrderAttachmentDownloadResult> OpenCheckoutAttachmentDownloadAsync(
        Guid tenantId,
        Guid orderId,
        Guid attachmentId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var orderExists = await VerifyOrderOwnershipAsync(tenantId, orderId, cancellationToken);
        if (!orderExists)
        {
            _logger.LogWarning(
                "OpenCheckoutAttachmentDownload | TenantId: {TenantId} | OrderId: {OrderId} | AttachmentId: {AttachmentId} | ErrorCode: ORDER_NOT_FOUND | ErrorCategory: authorization | CorrelationId: {CorrelationId}",
                tenantId,
                orderId,
                attachmentId,
                correlationId);
            throw new AdminOrderNotFoundException(orderId);
        }

        var attachment = await _context.CheckoutAttachments
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(item =>
                item.Id == attachmentId &&
                item.OrderId == orderId &&
                item.TenantId == tenantId &&
                !item.IsDeleted,
                cancellationToken);

        if (attachment == null)
        {
            _logger.LogWarning(
                "OpenCheckoutAttachmentDownload | TenantId: {TenantId} | OrderId: {OrderId} | AttachmentId: {AttachmentId} | ErrorCode: CHECKOUT_ATTACHMENT_NOT_FOUND | ErrorCategory: authorization | CorrelationId: {CorrelationId}",
                tenantId,
                orderId,
                attachmentId,
                correlationId);
            throw new AdminOrderAttachmentNotFoundException(attachmentId);
        }

        if (attachment.Status != CheckoutAttachmentStatus.Attached ||
            string.IsNullOrWhiteSpace(attachment.FinalObjectKey))
        {
            _logger.LogWarning(
                "OpenCheckoutAttachmentDownload | TenantId: {TenantId} | OrderId: {OrderId} | AttachmentId: {AttachmentId} | Status: {Status} | ErrorCode: CHECKOUT_ATTACHMENT_NOT_DOWNLOADABLE | ErrorCategory: invalid_state | CorrelationId: {CorrelationId}",
                tenantId,
                orderId,
                attachmentId,
                attachment.Status,
                correlationId);
            throw new AdminOrderAttachmentNotDownloadableException(attachmentId);
        }

        try
        {
            var download = await _storageService.DownloadObjectAsync(
                attachment.FinalObjectKey,
                attachment.FinalETag,
                cancellationToken);

            var downloadedContentType = NormalizeAttachmentContentType(download.ContentType);
            var expectedContentType = NormalizeAttachmentContentType(attachment.ContentType);
            if (download.ContentLength != attachment.SizeBytes ||
                !string.Equals(downloadedContentType, expectedContentType, StringComparison.Ordinal))
            {
                await download.Content.DisposeAsync();
                throw new AdminOrderAttachmentIntegrityException(
                    attachmentId,
                    new InvalidDataException("Stored attachment metadata does not match its authoritative order association."));
            }

            return new AdminOrderAttachmentDownloadResult(
                download.Content,
                attachment.OriginalFileName,
                expectedContentType,
                attachment.SizeBytes);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.PreconditionFailed)
        {
            _logger.LogWarning(
                ex,
                "OpenCheckoutAttachmentDownload | TenantId: {TenantId} | OrderId: {OrderId} | AttachmentId: {AttachmentId} | INTEGRITY_CHECK_FAILED | CorrelationId: {CorrelationId}",
                tenantId,
                orderId,
                attachmentId,
                correlationId);
            throw new AdminOrderAttachmentIntegrityException(attachmentId, ex);
        }
    }

    /// <inheritdoc />
    public async Task<AdminOrderStatusUpdateResponse> UpdateOrderStatusAsync(
        Guid tenantId,
        Guid orderId,
        AdminUpdateOrderStatusRequest request,
        uint expectedRowVersion,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "UpdateOrderStatus | TenantId: {TenantId} | OrderId: {OrderId} | NewStatus: {NewStatus} | Reason: {Reason} | CorrelationId: {CorrelationId} | Action: STATUS_CHANGE",
            tenantId, orderId, request.NewStatus, request.Reason ?? "(none)", correlationId);

        // Verify ownership first
        var ownershipVerified = await VerifyOrderOwnershipAsync(tenantId, orderId, cancellationToken);
        if (!ownershipVerified)
        {
            _logger.LogWarning(
                "UpdateOrderStatus | TenantId: {TenantId} | OrderId: {OrderId} | NOT_FOUND_OR_NOT_OWNED | CorrelationId: {CorrelationId}",
                tenantId, orderId, correlationId);
            throw new AdminOrderNotFoundException(orderId);
        }

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        // Get order with a write lock so admin, payment, and shipment status writers
        // cannot validate against stale state and publish competing versions.
        var order = await LoadOrderForAdminStatusMutationAsync(
            tenantId,
            orderId,
            cancellationToken);

        if (order == null)
        {
            throw new AdminOrderNotFoundException(orderId);
        }

        var oldStatus = order.Status;
        var currentRowVersion = GetOrderRowVersion(order);

        if (currentRowVersion != expectedRowVersion)
        {
            _logger.LogWarning(
                "UpdateOrderStatus | TenantId: {TenantId} | OrderId: {OrderId} | ExpectedRowVersion: {ExpectedRowVersion} | CurrentRowVersion: {CurrentRowVersion} | VERSION_CONFLICT | CorrelationId: {CorrelationId}",
                tenantId, orderId, expectedRowVersion, currentRowVersion, correlationId);
            throw new AdminOrderConcurrencyException(orderId);
        }

        // Validate: Delivered orders are immutable
        if (order.Status == OrderStatus.Delivered || order.Status == OrderStatus.Completed)
        {
            _logger.LogWarning(
                "UpdateOrderStatus | TenantId: {TenantId} | OrderId: {OrderId} | OldStatus: {OldStatus} | NewStatus: {NewStatus} | ORDER_IMMUTABLE | CorrelationId: {CorrelationId}",
                tenantId, orderId, oldStatus, request.NewStatus, correlationId);
            throw new AdminOrderImmutableException(orderId, order.Status);
        }

        // PRODUCTION HARDENING: Lifecycle transitions must use dedicated endpoints
        // This guard runs BEFORE generic transition validation to ensure consistent messaging
        if (request.NewStatus == OrderStatus.Cancelled ||
            request.NewStatus == OrderStatus.Shipped ||
            request.NewStatus == OrderStatus.Delivered ||
            request.NewStatus == OrderStatus.Completed ||
            request.NewStatus == OrderStatus.Refunded)
        {
            _logger.LogWarning(
                "UpdateOrderStatus | TenantId: {TenantId} | OrderId: {OrderId} | OldStatus: {OldStatus} | NewStatus: {NewStatus} | LIFECYCLE_BYPASS_ATTEMPT | CorrelationId: {CorrelationId}",
                tenantId, orderId, oldStatus, request.NewStatus, correlationId);
            throw new InvalidAdminOrderStateException(
                orderId, 
                oldStatus, 
                request.NewStatus, 
                $"Direct status update to {request.NewStatus} is prohibited. Use dedicated lifecycle endpoints to ensure cross-system integrity.");
        }

        // Validate status transition
        if (!IsValidStatusTransition(oldStatus, request.NewStatus))
        {
            _logger.LogWarning(
                "UpdateOrderStatus | TenantId: {TenantId} | OrderId: {OrderId} | OldStatus: {OldStatus} | NewStatus: {NewStatus} | INVALID_TRANSITION | CorrelationId: {CorrelationId}",
                tenantId, orderId, oldStatus, request.NewStatus, correlationId);
            throw new InvalidAdminOrderStateException(orderId, oldStatus, request.NewStatus);
        }

        EnsureAdminStatusOverrideDoesNotAdvanceOperationalLifecycle(order, request.NewStatus, correlationId);

        var nowUtc = DateTime.UtcNow;

        // Update order status
        using var orderStatusScope = OrderStatusSaveChangesInterceptor.BeginAuthorizedContext();
        _context.Entry(order).Property<uint>("xmin").OriginalValue = expectedRowVersion;
        order.Status = request.NewStatus;
        order.UpdatedAt = nowUtc;

        // No status-specific timestamp side-effects here — only non-lifecycle statuses
        // (Confirmed, Processing, OnHold, etc.) reach this point. Lifecycle statuses
        // (Cancelled, Shipped, Delivered) are blocked above by the LIFECYCLE_BYPASS_ATTEMPT guard.

        // Append admin notes
        if (!string.IsNullOrWhiteSpace(request.Reason))
        {
            var noteEntry = $"{nowUtc:yyyy-MM-dd HH:mm}: Status changed from {oldStatus} to {request.NewStatus}. Reason: {request.Reason}";
            order.AdminNotes = string.IsNullOrEmpty(order.AdminNotes)
                ? noteEntry
                : $"{order.AdminNotes}\n{noteEntry}";
        }

        // Emit inside the same transaction and before SaveChanges. The publisher
        // writes to the EF/MassTransit outbox, making the status mutation and
        // downstream projection event a single commit boundary.
        await _eventPublisher.PublishAsync(new OrderStatusChangedEvent
        {
            TenantId = tenantId,
            OrderId = order.Id,
            OrderNumber = order.OrderNumber,
            OldStatus = oldStatus,
            NewStatus = order.Status,
            Reason = request.Reason,
            CorrelationId = correlationId
        });

        await _context.SaveChangesAsync(cancellationToken);
        var acceptedRowVersion = GetOrderRowVersion(order);
        await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "UpdateOrderStatus | TenantId: {TenantId} | OrderId: {OrderId} | OldStatus: {OldStatus} | NewStatus: {NewStatus} | SUCCESS | CorrelationId: {CorrelationId}",
            tenantId, orderId, oldStatus, order.Status, correlationId);

        return new AdminOrderStatusUpdateResponse
        {
            OrderId = order.Id,
            OrderNumber = order.OrderNumber,
            OldStatus = oldStatus,
            NewStatus = order.Status,
            RowVersion = acceptedRowVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            UpdatedAtUtc = nowUtc
        };
    }

    /// <inheritdoc />
    public async Task<AdminOrderCancellationResponse> CancelOrderAsync(
        Guid tenantId,
        Guid orderId,
        AdminCancelOrderRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "CancelOrder | TenantId: {TenantId} | OrderId: {OrderId} | Reason: {Reason} | CorrelationId: {CorrelationId} | Action: CANCEL",
            tenantId, orderId, request.Reason, correlationId);

        // Validate reason
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            _logger.LogWarning(
                "CancelOrder | TenantId: {TenantId} | OrderId: {OrderId} | CANCELLATION_REASON_REQUIRED | CorrelationId: {CorrelationId}",
                tenantId, orderId, correlationId);
            throw new CancellationReasonRequiredException();
        }

        // Verify ownership first
        var ownershipVerified = await VerifyOrderOwnershipAsync(tenantId, orderId, cancellationToken);
        if (!ownershipVerified)
        {
            _logger.LogWarning(
                "CancelOrder | TenantId: {TenantId} | OrderId: {OrderId} | NOT_FOUND_OR_NOT_OWNED | CorrelationId: {CorrelationId}",
                tenantId, orderId, correlationId);
            throw new AdminOrderNotFoundException(orderId);
        }

        // Get order
        var order = await _context.Orders
            .Where(o =>
                o.Id == orderId &&
                o.TenantId == tenantId &&
                o.Source == OrderSource.Ecommerce)
            .Include(o => o.Items)
            .FirstOrDefaultAsync(cancellationToken);

        if (order == null)
        {
            throw new AdminOrderNotFoundException(orderId);
        }

        // Validate: Cannot cancel delivered orders
        if (order.Status == OrderStatus.Delivered || order.Status == OrderStatus.Completed)
        {
            _logger.LogWarning(
                "CancelOrder | TenantId: {TenantId} | OrderId: {OrderId} | Status: {Status} | ORDER_IMMUTABLE | CorrelationId: {CorrelationId}",
                tenantId, orderId, order.Status, correlationId);
            throw new AdminOrderImmutableException(orderId, order.Status);
        }

        await EnsureCancellationDoesNotBypassRecognizedPaymentAsync(
            tenantId,
            order,
            correlationId,
            cancellationToken);

        // If already cancelled, return early
        if (order.Status == OrderStatus.Cancelled)
        {
            _logger.LogInformation(
                "CancelOrder | TenantId: {TenantId} | OrderId: {OrderId} | ALREADY_CANCELLED | CorrelationId: {CorrelationId}",
                tenantId, orderId, correlationId);
            return new AdminOrderCancellationResponse
            {
                OrderId = order.Id,
                OrderNumber = order.OrderNumber,
                NewStatus = OrderStatus.Cancelled,
                InventoryReleased = false,
                ShipmentCancelled = false,
                PaymentCancelled = false,
                PaymentCancellationQueued = false,
                CancelledAtUtc = order.CancelledAt ?? DateTime.UtcNow
            };
        }

        if (IsAdminCancellationPending(order))
        {
            _logger.LogInformation(
                "CancelOrder | TenantId: {TenantId} | OrderId: {OrderId} | CANCELLATION_ALREADY_PENDING | CorrelationId: {CorrelationId}",
                tenantId, orderId, correlationId);
            return new AdminOrderCancellationResponse
            {
                OrderId = order.Id,
                OrderNumber = order.OrderNumber,
                NewStatus = OrderStatus.OnHold,
                InventoryReleased = false,
                ShipmentCancelled = false,
                PaymentCancelled = false,
                PaymentCancellationQueued = true,
                CancelledAtUtc = order.UpdatedAt ?? DateTime.UtcNow
            };
        }

        var inventoryReleased = false;
        var shipmentCancelled = false;
        var cancellablePaymentIntentIds = await _context.Set<PaymentIntent>()
            .Where(pi =>
                pi.OrderId == orderId &&
                pi.TenantId == tenantId &&
                !pi.IsDeleted &&
                (pi.Status == PaymentIntentStatus.Pending ||
                 pi.Status == PaymentIntentStatus.Authorized ||
                 pi.Status == PaymentIntentStatus.AwaitingVerification))
            .Select(pi => pi.Id)
            .ToListAsync(cancellationToken);

        var shouldCancelPaymentViaLifecycle =
            cancellablePaymentIntentIds.Count > 0 ||
            order.PaymentStatus is PaymentStatus.Pending or PaymentStatus.Authorized or PaymentStatus.AwaitingVerification;

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        await _context.Entry(order).ReloadAsync(cancellationToken);

        if (order.Status == OrderStatus.Delivered || order.Status == OrderStatus.Completed)
        {
            _logger.LogWarning(
                "CancelOrder | TenantId: {TenantId} | OrderId: {OrderId} | Status: {Status} | ORDER_IMMUTABLE_AFTER_RELOAD | CorrelationId: {CorrelationId}",
                tenantId, orderId, order.Status, correlationId);
            throw new AdminOrderImmutableException(orderId, order.Status);
        }

        await EnsureCancellationDoesNotBypassRecognizedPaymentAsync(
            tenantId,
            order,
            correlationId,
            cancellationToken);

        if (order.Status == OrderStatus.Cancelled)
        {
            await transaction.CommitAsync(cancellationToken);
            _logger.LogInformation(
                "CancelOrder | TenantId: {TenantId} | OrderId: {OrderId} | ALREADY_CANCELLED_AFTER_RELOAD | CorrelationId: {CorrelationId}",
                tenantId, orderId, correlationId);
            return new AdminOrderCancellationResponse
            {
                OrderId = order.Id,
                OrderNumber = order.OrderNumber,
                NewStatus = OrderStatus.Cancelled,
                InventoryReleased = false,
                ShipmentCancelled = false,
                PaymentCancelled = false,
                PaymentCancellationQueued = false,
                CancelledAtUtc = order.CancelledAt ?? DateTime.UtcNow
            };
        }

        if (IsAdminCancellationPending(order))
        {
            await transaction.CommitAsync(cancellationToken);
            _logger.LogInformation(
                "CancelOrder | TenantId: {TenantId} | OrderId: {OrderId} | CANCELLATION_ALREADY_PENDING_AFTER_RELOAD | CorrelationId: {CorrelationId}",
                tenantId, orderId, correlationId);
            return new AdminOrderCancellationResponse
            {
                OrderId = order.Id,
                OrderNumber = order.OrderNumber,
                NewStatus = OrderStatus.OnHold,
                InventoryReleased = false,
                ShipmentCancelled = false,
                PaymentCancelled = false,
                PaymentCancellationQueued = true,
                CancelledAtUtc = order.UpdatedAt ?? DateTime.UtcNow
            };
        }

        cancellablePaymentIntentIds = await _context.Set<PaymentIntent>()
            .Where(pi =>
                pi.OrderId == orderId &&
                pi.TenantId == tenantId &&
                !pi.IsDeleted &&
                (pi.Status == PaymentIntentStatus.Pending ||
                 pi.Status == PaymentIntentStatus.Authorized ||
                 pi.Status == PaymentIntentStatus.AwaitingVerification))
            .Select(pi => pi.Id)
            .ToListAsync(cancellationToken);

        shouldCancelPaymentViaLifecycle =
            cancellablePaymentIntentIds.Count > 0 ||
            order.PaymentStatus is PaymentStatus.Pending or PaymentStatus.Authorized or PaymentStatus.AwaitingVerification;

        var oldStatus = order.Status;

        // -------------------------
        // 1. Cancel Shipment (if exists, tenant-owned, and not delivered)
        // -------------------------
        var shipment = await _context.Shipments
            .Where(s => s.OrderId == orderId && s.TenantId == tenantId)
            .FirstOrDefaultAsync(cancellationToken);

        if (shipment != null && shipment.Status != ShipmentStatus.Delivered && shipment.Status != ShipmentStatus.Cancelled)
        {
            shipment.CancellationReason = request.Reason;
            shipment.UpdatedAt = DateTime.UtcNow;

            _logger.LogInformation(
                "CancelOrder | TenantId: {TenantId} | OrderId: {OrderId} | ShipmentId: {ShipmentId} | ShipmentCancellationQueuedForCarrierAuthority | CorrelationId: {CorrelationId}",
                tenantId, orderId, shipment.Id, correlationId);
        }

        // -------------------------
        // 2. Payment state is finalized after the order transaction via IPaymentLifecycleService.
        // -------------------------

        // -------------------------
        // 3. Update Order Status
        // -------------------------
        var nowUtc = DateTime.UtcNow;
        var paymentCancellationQueued = shouldCancelPaymentViaLifecycle;
        var requestedStatus = paymentCancellationQueued ? OrderStatus.OnHold : OrderStatus.Cancelled;

        using var orderStatusScope = OrderStatusSaveChangesInterceptor.BeginAuthorizedContext();
        order.Status = requestedStatus;
        order.CancelledAt = paymentCancellationQueued ? null : nowUtc;
        order.CancellationReason = request.Reason;
        order.UpdatedAt = nowUtc;

        if (!paymentCancellationQueued && order.CheckoutState != CheckoutState.Paid)
        {
            order.TransitionCheckoutState(CheckoutState.Cancelled);
        }

        if (!paymentCancellationQueued)
        {
            await _couponValidationService.ReleaseCouponReservationAsync(
                tenantId,
                order.Id,
                correlationId,
                "admin_order_cancelled",
                cancellationToken);
        }

        // -------------------------
        // 5. Persist Payment Cancellation Intent (Durable Outbox)
        // -------------------------
        if (shouldCancelPaymentViaLifecycle)
        {
            var lifecycleTargets = cancellablePaymentIntentIds.Count > 0
                ? cancellablePaymentIntentIds
                : new List<Guid> { Guid.Empty }; // Guid.Empty signals "cancel all intents for order"

            foreach (var paymentIntentId in lifecycleTargets)
            {
                var outboxMessage = new PaymentCancellationOutboxMessage
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    OrderId = order.Id,
                    PaymentIntentId = paymentIntentId == Guid.Empty ? null : paymentIntentId,
                    SkipPaymentIntentResolution = paymentIntentId == Guid.Empty,
                    Reason = $"Order cancelled: {request.Reason}",
                    Actor = "admin",
                    CorrelationId = correlationId,
                    CreatedAt = nowUtc,
                    UpdatedAt = nowUtc
                };

                await _context.PaymentCancellationOutboxMessages.AddAsync(outboxMessage, cancellationToken);
            }

            _logger.LogInformation(
                "CancelOrder | TenantId: {TenantId} | OrderId: {OrderId} | PaymentCancellationIntentPersisted: {Count} | CorrelationId: {CorrelationId}",
                tenantId, orderId, lifecycleTargets.Count, correlationId);
        }

        // PRODUCTION HARDENING: Always attempt to release reservations
        // ReleaseOrderReservationsAsync handles per-reservation checks internally,
        // filtering to only those with RemainingReservedQuantity > 0.
        // Using a net sum (GetOpenOrderReservationQuantityAsync) is incorrect because
        // it can hide positive reservations when offset by negative ones (over-releases).
        if (!paymentCancellationQueued)
        {
            inventoryReleased = await _stockRepository.ReleaseOrderReservationsAsync(
                tenantId,
                order.Id,
                cancellationToken);

            _logger.LogInformation(
                "CancelOrder | TenantId: {TenantId} | OrderId: {OrderId} | InventoryReleaseAttempted | CorrelationId: {CorrelationId}",
                tenantId,
                order.Id,
                correlationId);
        }
        else
        {
            _logger.LogInformation(
                "CancelOrder | TenantId: {TenantId} | OrderId: {OrderId} | InventoryReleaseDeferredUntilPaymentCancellation | CorrelationId: {CorrelationId}",
                tenantId,
                order.Id,
                correlationId);
        }

        await _context.SaveChangesAsync(cancellationToken);

        // Append admin notes
        var noteEntry = paymentCancellationQueued
            ? $"{PendingCancellationAdminNoteMarker} {nowUtc:yyyy-MM-dd HH:mm}: Cancellation requested. Payment cancellation must complete before the order becomes terminally cancelled. Reason: {request.Reason}"
            : $"{nowUtc:yyyy-MM-dd HH:mm}: Order cancelled. Reason: {request.Reason}";
        order.AdminNotes = string.IsNullOrEmpty(order.AdminNotes)
            ? noteEntry
            : $"{order.AdminNotes}\n{noteEntry}";

        // -------------------------
        // 4. Emit Events
        // -------------------------
        
        if (!paymentCancellationQueued)
        {
            await _eventPublisher.PublishAsync(new OrderCancelledEvent
            {
                TenantId = tenantId,
                OrderId = order.Id,
                OrderNumber = order.OrderNumber,
                CancellationReason = request.Reason,
                CancelledAt = order.CancelledAt ?? nowUtc,
                RestoreInventory = false,
                CorrelationId = correlationId
            });
        }

        // Order Status Changed Event
        await _eventPublisher.PublishAsync(new OrderStatusChangedEvent
        {
            TenantId = tenantId,
            OrderId = order.Id,
            OrderNumber = order.OrderNumber,
            OldStatus = oldStatus,
            NewStatus = requestedStatus,
            Reason = request.Reason,
            CorrelationId = correlationId
        });

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "CancelOrder | TenantId: {TenantId} | OrderId: {OrderId} | OldStatus: {OldStatus} | NewStatus: {NewStatus} | InventoryReleased: {InventoryReleased} | ShipmentCancelled: {ShipmentCancelled} | PaymentCancellationQueued: {PaymentCancellationQueued} | SUCCESS | CorrelationId: {CorrelationId}",
            tenantId, orderId, oldStatus, requestedStatus, inventoryReleased, shipmentCancelled, paymentCancellationQueued, correlationId);

        return new AdminOrderCancellationResponse
        {
            OrderId = order.Id,
            OrderNumber = order.OrderNumber,
            NewStatus = requestedStatus,
            InventoryReleased = inventoryReleased,
            ShipmentCancelled = shipmentCancelled,
            PaymentCancelled = false, // Not yet confirmed - background worker will process
            PaymentCancellationQueued = paymentCancellationQueued,
            CancelledAtUtc = paymentCancellationQueued ? nowUtc : order.CancelledAt ?? nowUtc
        };
    }

    #region Private Helpers

    /// <summary>
    /// Verifies canonical Order.TenantId ownership. Related customer, shipment,
    /// and payment records are optional and cannot grant or remove order access.
    /// </summary>
    private async Task<bool> VerifyOrderOwnershipAsync(
        Guid tenantId,
        Guid orderId,
        CancellationToken cancellationToken)
    {
        return await _context.Orders
            .AnyAsync(
                order =>
                    order.Id == orderId &&
                    order.TenantId == tenantId &&
                    order.Source == OrderSource.Ecommerce,
                cancellationToken);
    }

    private async Task EnsureCancellationDoesNotBypassRecognizedPaymentAsync(
        Guid tenantId,
        Order order,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (PaymentStatusRecognizesMoney(order.PaymentStatus))
        {
            _logger.LogWarning(
                "CancelOrder | TenantId: {TenantId} | OrderId: {OrderId} | PaymentStatus: {PaymentStatus} | PAYMENT_REQUIRES_REFUND_LIFECYCLE | CorrelationId: {CorrelationId}",
                tenantId,
                order.Id,
                order.PaymentStatus,
                correlationId);

            throw new AdminOrderPaymentRequiresRefundException(order.Id, order.PaymentStatus);
        }

        var hasCapturedPaymentIntent = await _context.Set<PaymentIntent>()
            .AnyAsync(intent =>
                intent.TenantId == tenantId &&
                intent.OrderId == order.Id &&
                !intent.IsDeleted &&
                intent.Status == PaymentIntentStatus.Paid,
                cancellationToken);

        if (!hasCapturedPaymentIntent)
        {
            return;
        }

        _logger.LogWarning(
            "CancelOrder | TenantId: {TenantId} | OrderId: {OrderId} | PaymentIntentStatus: Paid | PAYMENT_INTENT_REQUIRES_REFUND_LIFECYCLE | CorrelationId: {CorrelationId}",
            tenantId,
            order.Id,
            correlationId);

        throw new AdminOrderPaymentRequiresRefundException(order.Id, PaymentStatus.Collected);
    }

    /// <summary>
    /// Legacy ordinal value for PartiallyPaid status that was removed from the enum.
    /// Money was recognized outside the canonical lifecycle.
    /// </summary>
    private const int LegacyPartiallyPaidValue = 3;

    private static bool PaymentStatusRecognizesMoney(PaymentStatus status) =>
        status is PaymentStatus.Collected
            or PaymentStatus.Settled
            or PaymentStatus.PartiallyRefunded
            or PaymentStatus.Refunded ||
        (int)status == LegacyPartiallyPaidValue;

    private static List<AdminOrderActionCapabilityDto> BuildAvailableOrderActions(
        Order order,
        IReadOnlyCollection<PaymentIntent> paymentIntents)
    {
        var actions = new List<AdminOrderActionCapabilityDto>();

        if (order.Status == OrderStatus.OnHold)
        {
            actions.Add(new AdminOrderActionCapabilityDto
            {
                Code = "resume-pending",
                Label = "Resume as Pending",
                TargetStatus = OrderStatus.Pending.ToString(),
                RequiresConfirmation = false
            });
        }
        else if (order.Status is not (
                     OrderStatus.Delivered or
                     OrderStatus.Completed or
                     OrderStatus.Cancelled or
                     OrderStatus.Refunded))
        {
            actions.Add(new AdminOrderActionCapabilityDto
            {
                Code = "place-on-hold",
                Label = "Place On Hold",
                TargetStatus = OrderStatus.OnHold.ToString(),
                RequiresConfirmation = false
            });
        }

        var hasCapturedPayment = paymentIntents.Any(intent =>
            !intent.IsDeleted && intent.Status == PaymentIntentStatus.Paid);
        var canCancel = order.Status is not (
                OrderStatus.Delivered or
                OrderStatus.Completed or
                OrderStatus.Cancelled) &&
            !IsAdminCancellationPending(order) &&
            !PaymentStatusRecognizesMoney(order.PaymentStatus) &&
            !hasCapturedPayment;

        if (canCancel)
        {
            actions.Add(new AdminOrderActionCapabilityDto
            {
                Code = "cancel-order",
                Label = "Cancel Order",
                RequiresConfirmation = true,
                RequiredFields = new List<string> { "reason" }
            });
        }

        return actions;
    }

    private List<AdminOrderActionCapabilityDto> BuildAvailableShipmentActions(Shipment? shipment)
    {
        if (shipment == null ||
            shipment.Status == ShipmentStatus.Cancelled ||
            _context.ShipmentCancellationOperations.Any(x => x.TenantId == shipment.TenantId && x.ShipmentId == shipment.Id) ||
            !ShipmentLifecycleTransitionPolicy.CanTransition(shipment.Status, ShipmentStatus.Cancelled))
        {
            return new List<AdminOrderActionCapabilityDto>();
        }

        return new List<AdminOrderActionCapabilityDto>
        {
            new()
            {
                Code = "cancel-shipment",
                Label = "Cancel Shipment",
                RequiresConfirmation = true,
                RequiredFields = new List<string> { "reason" }
            }
        };
    }

    private void EnsureAdminStatusOverrideDoesNotAdvanceOperationalLifecycle(
        Order order,
        OrderStatus requestedStatus,
        string correlationId)
    {
        if (requestedStatus == order.Status || requestedStatus == OrderStatus.OnHold)
        {
            return;
        }

        if (requestedStatus is OrderStatus.Confirmed or OrderStatus.Processing)
        {
            _logger.LogWarning(
                "UpdateOrderStatus | TenantId: {TenantId} | OrderId: {OrderId} | OldStatus: {OldStatus} | NewStatus: {NewStatus} | OPERATIONAL_LIFECYCLE_BYPASS_ATTEMPT | PaymentStatus: {PaymentStatus} | FinancialPostingStatus: {FinancialPostingStatus} | CorrelationId: {CorrelationId}",
                order.TenantId,
                order.Id,
                order.Status,
                requestedStatus,
                order.PaymentStatus,
                order.FinancialPostingStatus,
                correlationId);

            throw new InvalidAdminOrderStateException(
                order.Id,
                order.Status,
                requestedStatus,
                $"Direct status update to {requestedStatus} is prohibited. Use payment and fulfillment lifecycle endpoints so Storefront, Marketplace, Affiliate, and Accounting projections cannot advance ahead of financial truth.");
        }
    }

    /// <summary>
    /// Validates if a status transition is allowed.
    /// Admin can move forward in workflow or to Cancelled/OnHold.
    /// Cannot move backward arbitrarily.
    /// </summary>
    private static bool IsValidStatusTransition(OrderStatus current, OrderStatus requested)
    {
        // Same status is a no-op but allowed
        if (current == requested)
            return true;

        // Delivered and Completed orders are immutable (handled before this call)
        if (current == OrderStatus.Delivered || current == OrderStatus.Completed)
            return false;

        // Cancelled and Refunded orders cannot transition to anything else
        if (current == OrderStatus.Cancelled || current == OrderStatus.Refunded)
            return false;

        // Allow transitions to OnHold or Cancelled from any active state
        if (requested == OrderStatus.Cancelled || requested == OrderStatus.OnHold)
            return true;

        // Allow forward progression in the workflow
        // Pending → Confirmed → Processing → Shipped → Delivered → Completed
        var workflowOrder = new Dictionary<OrderStatus, int>
        {
            { OrderStatus.Pending, 0 },
            { OrderStatus.Confirmed, 1 },
            { OrderStatus.Processing, 2 },
            { OrderStatus.Shipped, 3 },
            { OrderStatus.Delivered, 4 },
            { OrderStatus.Completed, 5 }
        };

        // OnHold can move to any forward state or back to where it was
        if (current == OrderStatus.OnHold)
            return true;

        if (workflowOrder.TryGetValue(current, out var currentOrder) &&
            workflowOrder.TryGetValue(requested, out var requestedOrder))
        {
            // Allow forward movement only
            return requestedOrder > currentOrder;
        }

        return false;
    }

    private async Task<Order?> LoadOrderForAdminStatusMutationAsync(
        Guid tenantId,
        Guid orderId,
        CancellationToken cancellationToken)
    {
        var providerName = _context.Database.ProviderName ?? string.Empty;

        if (providerName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) ||
            providerName.Contains("PostgreSQL", StringComparison.OrdinalIgnoreCase))
        {
            return await _context.Orders
                .FromSqlInterpolated($@"
                    SELECT xmin, *
                    FROM ""Orders""
                    WHERE ""Id"" = {orderId}
                      AND ""TenantId"" = {tenantId}
                      AND ""Source"" = {(int)OrderSource.Ecommerce}
                      AND ""IsDeleted"" = FALSE
                    FOR UPDATE")
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (providerName.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) ||
            providerName.Contains("MicrosoftSqlServer", StringComparison.OrdinalIgnoreCase))
        {
            return await _context.Orders
                .FromSqlInterpolated($@"
                    SELECT *
                    FROM [Orders] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                    WHERE [Id] = {orderId}
                      AND [TenantId] = {tenantId}
                      AND [Source] = {(int)OrderSource.Ecommerce}
                      AND [IsDeleted] = 0")
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(cancellationToken);
        }

        return await _context.Orders
            .FirstOrDefaultAsync(
                order => order.Id == orderId &&
                         order.TenantId == tenantId &&
                         order.Source == OrderSource.Ecommerce &&
                         !order.IsDeleted,
                cancellationToken);
    }

    private uint GetOrderRowVersion(Order order)
    {
        return _context.Entry(order).Property<uint>("xmin").CurrentValue;
    }

    private static string ToApiValue(CheckoutFieldKind kind) => kind switch
    {
        CheckoutFieldKind.System => "SYSTEM",
        CheckoutFieldKind.Custom => "CUSTOM",
        _ => kind.ToString().ToUpperInvariant()
    };

    private static string ToApiValue(CheckoutFieldType type) => type switch
    {
        CheckoutFieldType.Text => "TEXT",
        CheckoutFieldType.Textarea => "TEXTAREA",
        CheckoutFieldType.Number => "NUMBER",
        CheckoutFieldType.Email => "EMAIL",
        CheckoutFieldType.Phone => "PHONE",
        CheckoutFieldType.Select => "SELECT",
        CheckoutFieldType.Radio => "RADIO",
        CheckoutFieldType.Checkbox => "CHECKBOX",
        CheckoutFieldType.Date => "DATE",
        CheckoutFieldType.Image => "IMAGE",
        CheckoutFieldType.File => "FILE",
        CheckoutFieldType.MultiImage => "MULTI_IMAGE",
        _ => type.ToString().ToUpperInvariant()
    };

    /// <summary>
    /// Maps order entity to admin details DTO.
    /// </summary>
    private AdminOrderDetailsDto MapToDetailsDto(
        Order order,
        List<PaymentIntent> paymentIntents,
        Shipment? shipment,
        AdminAffiliateAttributionDto? affiliateAttribution)
    {
        return new AdminOrderDetailsDto
        {
            TenantId = order.TenantId,
            OrderId = order.Id,
            RowVersion = GetOrderRowVersion(order).ToString(System.Globalization.CultureInfo.InvariantCulture),
            OrderNumber = order.OrderNumber,
            Status = order.Status,
            PaymentStatus = order.PaymentStatus,
            FulfillmentStatus = order.FulfillmentStatus,
            ShipmentStatus = shipment?.Status,
            Items = order.Items.OrderBy(i => i.Id).Select(i => new AdminOrderItemDto
            {
                ItemId = i.Id,
                ProductId = i.ProductId,
                SKU = i.SKU,
                ProductName = i.ProductName,
                VariantName = i.VariantName,
                ImageUrl = i.ImageUrl,
                Quantity = i.Quantity,
                UnitPrice = i.UnitPrice,
                DiscountAmount = i.DiscountAmount,
                TaxAmount = i.TaxAmount,
                Total = i.Total
            }).ToList(),
            CheckoutFieldSnapshots = order.CheckoutFieldSnapshots
                .OrderBy(snapshot => snapshot.DisplayOrder)
                .ThenBy(snapshot => snapshot.CreatedAt)
                .ThenBy(snapshot => snapshot.Id)
                .Select(snapshot => new AdminOrderCheckoutFieldSnapshotDto
                {
                    SnapshotId = snapshot.Id,
                    FieldId = snapshot.CheckoutFieldDefinitionId,
                    FieldKind = ToApiValue(snapshot.FieldKind),
                    FieldType = ToApiValue(snapshot.FieldType),
                    SystemFieldKey = snapshot.SystemFieldKey,
                    Label = snapshot.Label,
                    HelpText = snapshot.HelpText,
                    IsRequired = snapshot.IsRequired,
                    IsVisible = snapshot.IsEnabled,
                    DisplayOrder = snapshot.DisplayOrder,
                    ConfigurationSchemaVersion = snapshot.ConfigurationSchemaVersion,
                    Configuration = BuildCheckoutFieldConfigurationPresentation(snapshot),
                    SubmittedValue = BuildCheckoutFieldSubmittedValuePresentation(snapshot),
                    ConfigurationJson = snapshot.ConfigurationJson,
                    ValueJson = BuildSafeCheckoutFieldValueJson(snapshot)
                })
                .ToList(),
            Payments = paymentIntents.Select(pi => new AdminPaymentIntentDto
            {
                PaymentIntentId = pi.Id,
                PaymentReference = pi.PaymentReference,
                Provider = pi.Provider.ToString(),
                PaymentMethod = pi.PaymentMethod,
                Status = pi.Status,
                Amount = pi.Amount,
                Currency = pi.Currency,
                CreatedAt = pi.CreatedAt,
                CapturedAt = pi.CapturedAt,
                FailedAt = pi.FailedAt,
                FailureReason = pi.FailureReason
            }).ToList(),
            Shipment = shipment == null ? null : new AdminShipmentDto
            {
                ShipmentId = shipment.Id,
                ShipmentNumber = shipment.ShipmentNumber,
                Status = shipment.Status,
                ShippingMethodName = shipment.ShippingMethodName,
                CarrierName = shipment.CarrierCode,
                TrackingNumber = shipment.TrackingNumber,
                TrackingUrl = shipment.TrackingUrl,
                EstimatedDeliveryDate = shipment.EstimatedDeliveryDate,
                ActualDeliveryDate = shipment.ActualDeliveryDate,
                CreatedAt = shipment.CreatedAt,
                ShippedAt = shipment.ShippedAtUtc,
                DeliveredAt = shipment.DeliveredAtUtc,
                CancelledAt = shipment.CancelledAtUtc,
                CancellationReason = shipment.CancellationReason,
                Cancellation = PointOfSale.Infrastructure.Shipping.ShipmentCancellationService.ReadResult(shipment,
                    _context.ShipmentCancellationOperations.AsNoTracking().SingleOrDefault(x => x.TenantId == shipment.TenantId && x.ShipmentId == shipment.Id))
            },
            AvailableOrderActions = BuildAvailableOrderActions(order, paymentIntents),
            AvailableShipmentActions = BuildAvailableShipmentActions(shipment),
            Totals = new AdminOrderTotalsDto
            {
                Subtotal = order.Subtotal,
                DiscountAmount = order.DiscountAmount,
                TaxAmount = order.TaxAmount,
                ShippingCost = order.ShippingCost,
                Total = order.Total,
                Currency = order.Currency,
                CouponCode = order.CouponCode
            },
            Customer = new AdminOrderCustomerDto
            {
                CustomerId = order.CustomerId,
                OnlineCustomerId = order.OnlineCustomerId,
                IdentityType = order.CustomerId.HasValue
                    ? "POS_CUSTOMER"
                    : order.OnlineCustomerId.HasValue
                        ? "STOREFRONT_ACCOUNT"
                        : "GUEST",
                Name = order.Customer?.Name ?? order.OnlineCustomer?.DisplayName ?? order.GuestName,
                Email = order.Customer?.Email ?? order.OnlineCustomer?.Email ?? order.GuestEmail,
                Phone = PointOfSale.API.Services.PhoneRisk.OrderPhoneIdentity.Resolve(order.GuestPhone, order.Customer?.Phone, order.OnlineCustomer?.Phone),
                IsGuest = !order.CustomerId.HasValue && !order.OnlineCustomerId.HasValue
            },
            CreatedAtUtc = order.CreatedAt,
            Source = order.Source,
            SourceOrderId = order.SourceOrderId,
            SourceLabel = order.Source == OrderSource.Marketplace ? "Marketplace" : null,
            IsMarketplaceOrder = false,
            AffiliateAttribution = affiliateAttribution,
            MarketplaceLineage = null,
            CustomerNotes = order.CustomerNotes,
            AdminNotes = order.AdminNotes,
            CancellationReason = order.CancellationReason,
            ShippingAddress = new AdminAddressDto
            {
                Address = order.ShippingAddress,
                City = order.ShippingCity,
                State = order.ShippingState,
                ZipCode = order.ShippingZipCode,
                Country = order.ShippingCountry
            },
            BillingAddress = string.IsNullOrEmpty(order.BillingAddress) ? null : new AdminAddressDto
            {
                Address = order.BillingAddress,
                City = order.BillingCity,
                State = order.BillingState,
                ZipCode = order.BillingZipCode,
                Country = order.BillingCountry
            }
        };
    }

    private async Task AttachAffiliateAttributionEvidenceAsync(
        List<AdminOrderListItemDto> orders,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var storefrontOrderIds = orders
            .Where(order => !order.IsMarketplaceOrder && order.TenantId == tenantId)
            .Select(order => order.OrderId)
            .Distinct()
            .ToArray();

        if (storefrontOrderIds.Length == 0)
        {
            return;
        }

        var evidence = await (
            from attribution in _context.AffiliateAttributions.AsNoTracking()
            join order in _context.Orders.AsNoTracking() on attribution.OrderId equals order.Id
            where attribution.TenantId == tenantId
                  && storefrontOrderIds.Contains(attribution.OrderId)
                  && !attribution.IsDeleted
                  && !order.IsDeleted
                  && order.TenantId == tenantId
                  && order.AffiliateAccountId.HasValue
                  && order.AffiliateAccountId.Value == attribution.AffiliateAccountId
                  && order.AffiliateReferralCode != null
                  && order.AffiliateReferralCode == attribution.ReferralCode
                  && order.PromotionLinkId == attribution.PromotionLinkId
            select new AdminAffiliateAttributionDto
            {
                TenantId = attribution.TenantId,
                OrderId = attribution.OrderId,
                AttributionId = attribution.Id,
                AffiliateAccountId = attribution.AffiliateAccountId,
                ReferralCode = attribution.ReferralCode,
                PromotionLinkId = attribution.PromotionLinkId,
                Source = attribution.Source.ToString(),
                AttributedAtUtc = attribution.AttributedAtUtc
            })
            .ToDictionaryAsync(attribution => attribution.OrderId, cancellationToken);

        foreach (var order in orders)
        {
            if (evidence.TryGetValue(order.OrderId, out var attribution))
            {
                order.AffiliateAttribution = attribution;
            }
        }
    }

    private async Task<AdminAffiliateAttributionDto?> LoadAffiliateAttributionEvidenceAsync(
        Guid tenantId,
        Order order,
        CancellationToken cancellationToken)
    {
        if (order.TenantId != tenantId ||
            !order.AffiliateAccountId.HasValue ||
            string.IsNullOrWhiteSpace(order.AffiliateReferralCode))
        {
            return null;
        }

        return await (
            from attribution in _context.AffiliateAttributions.AsNoTracking()
            where attribution.TenantId == tenantId
                  && attribution.OrderId == order.Id
                  && !attribution.IsDeleted
                  && attribution.AffiliateAccountId == order.AffiliateAccountId.Value
                  && attribution.ReferralCode == order.AffiliateReferralCode
                  && attribution.PromotionLinkId == order.PromotionLinkId
            select new AdminAffiliateAttributionDto
            {
                TenantId = attribution.TenantId,
                OrderId = attribution.OrderId,
                AttributionId = attribution.Id,
                AffiliateAccountId = attribution.AffiliateAccountId,
                ReferralCode = attribution.ReferralCode,
                PromotionLinkId = attribution.PromotionLinkId,
                Source = attribution.Source.ToString(),
                AttributedAtUtc = attribution.AttributedAtUtc
            })
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task AttachMarketplaceLineageEvidenceAsync(
        List<AdminOrderListItemDto> orders,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var marketplaceOrderIds = orders
            .Where(order => order.IsMarketplaceOrder && order.TenantId == tenantId)
            .Select(order => order.OrderId)
            .Distinct()
            .ToArray();

        if (marketplaceOrderIds.Length == 0)
        {
            return;
        }

        var evidence = await _context.CommerceHubSubOrders
            .AsNoTracking()
            .Where(subOrder =>
                marketplaceOrderIds.Contains(subOrder.Id) &&
                subOrder.StoreId == tenantId &&
                !subOrder.IsDeleted &&
                !subOrder.MarketplaceOrder.IsDeleted)
            .Select(subOrder => new AdminMarketplaceLineageDto
            {
                TenantId = subOrder.StoreId,
                OrderId = subOrder.Id,
                MarketplaceOrderId = subOrder.MarketplaceOrderId,
                MarketplaceName = "CommerceHub",
                MarketplaceOrderNumber = subOrder.MarketplaceOrder.OrderNumber,
                MarketplaceSubOrderNumber = subOrder.OrderNumber,
                ExternalCustomerId = subOrder.MarketplaceOrder.OnlineCustomerId ?? subOrder.MarketplaceOrder.CustomerId,
                VendorId = subOrder.StoreId,
                VendorName = subOrder.StoreName,
                MarketplaceSourceType = "Marketplace",
                MarketplaceStatus = subOrder.Status,
                MarketplacePaymentStatus = subOrder.PaymentStatus,
                Currency = subOrder.Currency,
                Subtotal = subOrder.Subtotal,
                Shipping = subOrder.Shipping,
                Total = subOrder.Total,
                PaymentReferenceId = subOrder.PaymentReferenceId,
                PaymentSnapshotJson = subOrder.PaymentSnapshotJson,
                ShippingMethodId = subOrder.ShippingMethodId,
                ShippingMethodName = subOrder.ShippingMethodName,
                ShippingSnapshotJson = subOrder.ShippingSnapshotJson,
                CommissionSnapshotJson = subOrder.CommissionSnapshotJson
            })
            .ToDictionaryAsync(lineage => lineage.OrderId, cancellationToken);

        var refundRows = await (
                from refund in _context.CommerceHubRefundRequests.AsNoTracking()
                join item in _context.CommerceHubSubOrderItems.AsNoTracking() on refund.OrderItemId equals item.Id
                where marketplaceOrderIds.Contains(item.SubOrderId) &&
                      refund.VendorId == tenantId &&
                      !refund.IsDeleted &&
                      !item.IsDeleted
                select new { item.SubOrderId, Refund = refund })
            .ToListAsync(cancellationToken);
        var latestRefundBySubOrder = refundRows
            .GroupBy(row => row.SubOrderId)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(row => row.Refund.CreatedAt).ThenByDescending(row => row.Refund.Id).First().Refund);
        var latestRefundIds = latestRefundBySubOrder.Values.Select(refund => refund.Id).ToArray();
        var returnStatuses = await _context.CommerceHubReturnRequests
            .AsNoTracking()
            .Where(returnRequest => latestRefundIds.Contains(returnRequest.RefundId) && !returnRequest.IsDeleted)
            .Select(returnRequest => new { returnRequest.RefundId, returnRequest.Status })
            .ToDictionaryAsync(row => row.RefundId, row => row.Status.ToString(), cancellationToken);

        foreach (var order in orders.Where(order => order.IsMarketplaceOrder))
        {
            if (evidence.TryGetValue(order.OrderId, out var lineage))
            {
                order.MarketplaceLineage = lineage;
            }

            if (latestRefundBySubOrder.TryGetValue(order.OrderId, out var refund))
            {
                order.RefundStatus = refund.Status.ToString();
                order.ReturnStatus = returnStatuses.GetValueOrDefault(refund.Id);
            }
        }
    }

    private async Task<AdminOrderDetailsDto> MapMarketplaceSubOrderToDetailsDtoAsync(
        CommerceHubSubOrder subOrder,
        CancellationToken cancellationToken)
    {
        var marketplaceOrder = subOrder.MarketplaceOrder;
        var refundRequests = await _context.CommerceHubRefundRequests
            .AsNoTracking()
            .Where(refundRequest =>
                refundRequest.OrderItem.SubOrderId == subOrder.Id &&
                !refundRequest.IsDeleted)
            .OrderByDescending(refundRequest => refundRequest.CreatedAt)
            .ThenByDescending(refundRequest => refundRequest.Id)
            .ToListAsync(cancellationToken);
        var evidenceLookup = await _refundEvidenceService.GetEvidenceLookupAsync(
            refundRequests.Select(refundRequest => refundRequest.Id),
            cancellationToken);
        var refundIds = refundRequests.Select(refundRequest => refundRequest.Id).ToArray();
        var returnLookup = await _context.CommerceHubReturnRequests
            .AsNoTracking()
            .Where(returnRequest => refundIds.Contains(returnRequest.RefundId) && !returnRequest.IsDeleted)
            .ToDictionaryAsync(returnRequest => returnRequest.RefundId, cancellationToken);
        var trackedRefundLookup = await _context.CommerceHubRefunds
            .AsNoTracking()
            .Where(refund =>
                refund.TenantId == subOrder.StoreId &&
                refund.SubOrderId == subOrder.Id &&
                refund.RefundRequestId.HasValue &&
                refundIds.Contains(refund.RefundRequestId.Value) &&
                !refund.IsDeleted)
            .GroupBy(refund => refund.RefundRequestId!.Value)
            .Select(group => group.OrderByDescending(refund => refund.CreatedAt).First())
            .ToDictionaryAsync(refund => refund.RefundRequestId!.Value, cancellationToken);
        var executionLookup = await _context.CommerceHubRefundExecutions
            .AsNoTracking()
            .Where(execution => refundIds.Contains(execution.RefundId) && !execution.IsDeleted)
            .GroupBy(execution => execution.RefundId)
            .Select(group => group.OrderByDescending(execution => execution.ExecutedAt).First())
            .ToDictionaryAsync(execution => execution.RefundId, cancellationToken);
        var lifecycleHistory = await _context.CommerceHubSubOrderStatusHistory
            .AsNoTracking()
            .Where(history => history.SubOrderId == subOrder.Id && !history.IsDeleted)
            .OrderByDescending(history => history.ChangedAtUtc)
            .ThenByDescending(history => history.Id)
            .ToListAsync(cancellationToken);
        var refundLookup = refundRequests
            .GroupBy(refundRequest => refundRequest.OrderItemId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<CommerceHubRefundRequest>)group.ToList());

        return new AdminOrderDetailsDto
        {
            TenantId = subOrder.StoreId,
            OrderId = subOrder.Id,
            RowVersion = string.Empty,
            CommerceHubLifecycleVersion = subOrder.ProjectionMutationVersion,
            OrderNumber = subOrder.OrderNumber,
            Status = subOrder.Status,
            PaymentStatus = subOrder.PaymentStatus,
            FulfillmentStatus = null,
            ShipmentStatus = null,
            Items = subOrder.Items.OrderBy(item => item.Id).Select(item => new AdminOrderItemDto
            {
                ItemId = item.Id,
                ProductId = item.ProductId,
                SKU = string.Empty,
                ProductName = item.ProductName,
                VariantName = item.VariantName,
                ImageUrl = item.ImageUrl,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                DiscountAmount = 0m,
                TaxAmount = 0m,
                Total = item.Total,
                Refunds = refundLookup.TryGetValue(item.Id, out var matchedRefunds)
                    ? matchedRefunds.Select(refundRequest => new AdminOrderRefundEvidenceDto
                    {
                        RefundId = refundRequest.Id,
                        Quantity = refundRequest.Quantity,
                        Amount = refundRequest.TotalAmount,
                        Status = refundRequest.Status,
                        Reason = CustomerRefundService.BuildReasonSummary(refundRequest.ReasonCode, refundRequest.ReasonText),
                        ReasonCode = refundRequest.ReasonCode,
                        ReasonText = refundRequest.ReasonText,
                        ReturnRequest = returnLookup.TryGetValue(refundRequest.Id, out var returnRequest)
                            ? CommerceHubReturnMapper.MapDetails(returnRequest, refundRequest)
                            : null,
                        ExecutionState = ResolveRefundExecutionState(
                            refundRequest,
                            trackedRefundLookup.GetValueOrDefault(refundRequest.Id),
                            executionLookup.GetValueOrDefault(refundRequest.Id)),
                        NextAction = ResolveVendorRefundNextAction(
                            refundRequest,
                            returnLookup.GetValueOrDefault(refundRequest.Id)),
                        MediaUrls = evidenceLookup.TryGetValue(refundRequest.Id, out var media)
                            ? media.Select(entry => entry.Url).ToList()
                            : new List<string>(),
                        Media = evidenceLookup.TryGetValue(refundRequest.Id, out var refundMedia)
                            ? refundMedia.ToList()
                            : new List<RefundEvidenceMediaDto>(),
                        CreatedAt = refundRequest.CreatedAt,
                        UpdatedAt = refundRequest.UpdatedAt
                    }).ToList()
                    : new List<AdminOrderRefundEvidenceDto>()
            }).ToList(),
            Payments = new List<AdminPaymentIntentDto>(),
            Shipment = null,
            CommerceHubLifecycleHistory = lifecycleHistory.Select(history => new AdminCommerceHubOrderHistoryDto
            {
                HistoryId = history.Id,
                PreviousStatus = history.PreviousStatus,
                NewStatus = history.NewStatus,
                Reason = history.Reason,
                ChangedAtUtc = history.ChangedAtUtc
            }).ToList(),
            Totals = new AdminOrderTotalsDto
            {
                Subtotal = subOrder.Subtotal,
                DiscountAmount = 0m,
                TaxAmount = 0m,
                ShippingCost = subOrder.Shipping,
                Total = subOrder.Total,
                Currency = subOrder.Currency,
                CouponCode = null
            },
            Customer = new AdminOrderCustomerDto
            {
                CustomerId = null,
                Name = marketplaceOrder.FullName,
                Email = null,
                Phone = marketplaceOrder.PhoneNumber,
                IsGuest = true
            },
            CreatedAtUtc = subOrder.CreatedAt,
            Source = OrderSource.Marketplace,
            SourceOrderId = marketplaceOrder.Id.ToString(),
            SourceLabel = "Marketplace",
            IsMarketplaceOrder = true,
            AffiliateAttribution = null,
            MarketplaceLineage = new AdminMarketplaceLineageDto
            {
                TenantId = subOrder.StoreId,
                OrderId = subOrder.Id,
                MarketplaceOrderId = marketplaceOrder.Id,
                MarketplaceName = "CommerceHub",
                MarketplaceOrderNumber = marketplaceOrder.OrderNumber,
                MarketplaceSubOrderNumber = subOrder.OrderNumber,
                ExternalCustomerId = marketplaceOrder.OnlineCustomerId ?? marketplaceOrder.CustomerId,
                VendorId = subOrder.StoreId,
                VendorName = subOrder.StoreName,
                MarketplaceSourceType = "Marketplace",
                MarketplaceStatus = subOrder.Status,
                MarketplacePaymentStatus = subOrder.PaymentStatus,
                Currency = subOrder.Currency,
                Subtotal = subOrder.Subtotal,
                Shipping = subOrder.Shipping,
                Total = subOrder.Total,
                PaymentReferenceId = subOrder.PaymentReferenceId,
                PaymentSnapshotJson = subOrder.PaymentSnapshotJson,
                ShippingMethodId = subOrder.ShippingMethodId,
                ShippingMethodName = subOrder.ShippingMethodName,
                ShippingSnapshotJson = subOrder.ShippingSnapshotJson,
                CommissionSnapshotJson = subOrder.CommissionSnapshotJson
            },
            CustomerNotes = marketplaceOrder.Notes,
            AdminNotes = null,
            CancellationReason = null,
            ShippingAddress = new AdminAddressDto
            {
                Address = marketplaceOrder.AddressLine,
                City = marketplaceOrder.City,
                State = marketplaceOrder.Area,
                ZipCode = null,
                Country = null
            },
            BillingAddress = null
        };
    }

    private static string ResolveRefundExecutionState(
        CommerceHubRefundRequest refundRequest,
        CommerceHubRefund? trackedRefund,
        CommerceHubRefundExecution? execution)
    {
        if (execution != null)
        {
            return execution.IsSuccessful ? "Completed" : "Failed";
        }

        if (trackedRefund != null)
        {
            return trackedRefund.Status.ToString();
        }

        return refundRequest.Status is CommerceHubRefundRequestStatus.Paid or CommerceHubRefundRequestStatus.Completed
            ? "Completed"
            : "NotStarted";
    }

    private static string ResolveVendorRefundNextAction(
        CommerceHubRefundRequest refundRequest,
        CommerceHubReturnRequest? returnRequest)
    {
        if (returnRequest != null && CommerceHubReturnPolicy.CanConfirmReceipt(returnRequest.Status))
        {
            return "ConfirmReceipt";
        }

        return refundRequest.Status switch
        {
            CommerceHubRefundRequestStatus.Requested or CommerceHubRefundRequestStatus.UnderReview => "PlatformReview",
            CommerceHubRefundRequestStatus.Approved or CommerceHubRefundRequestStatus.WaitingForReturn => "AwaitReturnDelivery",
            CommerceHubRefundRequestStatus.AwaitingPayout or CommerceHubRefundRequestStatus.Processing => "PlatformRefundExecution",
            CommerceHubRefundRequestStatus.Completed or CommerceHubRefundRequestStatus.Paid => "Complete",
            CommerceHubRefundRequestStatus.Rejected or CommerceHubRefundRequestStatus.Cancelled or CommerceHubRefundRequestStatus.Failed => "None",
            _ => "PlatformReview"
        };
    }

    private static AdminOrderCheckoutFieldConfigurationPresentationDto BuildCheckoutFieldConfigurationPresentation(
        OrderCheckoutFieldSnapshot snapshot)
    {
        var result = new AdminOrderCheckoutFieldConfigurationPresentationDto
        {
            SchemaVersion = snapshot.ConfigurationSchemaVersion
        };

        if (string.IsNullOrWhiteSpace(snapshot.ConfigurationJson))
        {
            return result;
        }

        try
        {
            using var document = JsonDocument.Parse(snapshot.ConfigurationJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                result.HasAdditionalConfiguration = true;
                return result;
            }

            var root = document.RootElement;
            if (root.TryGetProperty("schemaVersion", out var schemaVersion) &&
                schemaVersion.TryGetInt32(out var parsedSchemaVersion))
            {
                result.SchemaVersion = parsedSchemaVersion;
            }

            if (root.TryGetProperty("options", out var options) &&
                options.ValueKind == JsonValueKind.Array)
            {
                result.Options = options.EnumerateArray()
                    .Where(option => option.ValueKind == JsonValueKind.Object)
                    .Select(option => new AdminOrderCheckoutFieldOptionDto
                    {
                        Value = ReadString(option, "value") ?? string.Empty,
                        Label = ReadString(option, "label") ?? ReadString(option, "value") ?? string.Empty
                    })
                    .Where(option => !string.IsNullOrWhiteSpace(option.Value))
                    .ToList();
            }

            var allowedMimeTypes = ReadStringArray(root, "allowedMimeTypes");
            var maxFileSize = ReadNullableInt64(root, "maxFileSize");
            var maxFiles = ReadNullableInt32(root, "maxFiles");
            if (allowedMimeTypes.Count > 0 || maxFileSize.HasValue || maxFiles.HasValue)
            {
                result.UploadPolicy = new AdminOrderCheckoutFieldUploadPolicyDto
                {
                    AllowedMimeTypes = allowedMimeTypes,
                    MaxFileSize = maxFileSize,
                    MaxFiles = maxFiles
                };
            }

            if (root.TryGetProperty("conditionalRules", out var rules) &&
                rules.ValueKind == JsonValueKind.Array)
            {
                result.ConditionalRules = rules.EnumerateArray()
                    .Where(rule => rule.ValueKind == JsonValueKind.Object)
                    .Select(MapConditionalRule)
                    .ToList();
            }

            var knownProperties = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "schemaVersion",
                "options",
                "allowedMimeTypes",
                "maxFileSize",
                "maxFiles",
                "conditionalRules"
            };
            result.HasAdditionalConfiguration = root.EnumerateObject()
                .Any(property => !knownProperties.Contains(property.Name));
        }
        catch (JsonException)
        {
            result.HasAdditionalConfiguration = true;
        }

        return result;
    }

    private static AdminOrderCheckoutFieldConditionalRuleDto MapConditionalRule(JsonElement rule) => new()
    {
        RuleId = ReadString(rule, "ruleId") ?? string.Empty,
        ConditionMode = ReadString(rule, "conditionMode") ?? string.Empty,
        Actions = ReadStringArray(rule, "actions"),
        Conditions = rule.TryGetProperty("conditions", out var conditions) &&
                     conditions.ValueKind == JsonValueKind.Array
            ? conditions.EnumerateArray()
                .Where(condition => condition.ValueKind == JsonValueKind.Object)
                .Select(condition => new AdminOrderCheckoutFieldRuleConditionDto
                {
                    SourceFieldId = ReadGuid(condition, "sourceFieldId") ?? Guid.Empty,
                    Operator = ReadString(condition, "operator") ?? string.Empty,
                    ComparisonValue = ReadString(condition, "comparisonValue")
                })
                .Where(condition => condition.SourceFieldId != Guid.Empty)
                .ToList()
            : new List<AdminOrderCheckoutFieldRuleConditionDto>()
    };

    private static AdminOrderCheckoutFieldSubmittedValueDto BuildCheckoutFieldSubmittedValuePresentation(
        OrderCheckoutFieldSnapshot snapshot)
    {
        AdminOrderCheckoutFieldConfigurationPresentationDto? configuration = null;
        JsonElement value;
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(snapshot.ValueJson) ? "null" : snapshot.ValueJson);
            value = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return new AdminOrderCheckoutFieldSubmittedValueDto
            {
                Kind = "INVALID",
                DisplayValue = "Invalid stored value"
            };
        }

        if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return new AdminOrderCheckoutFieldSubmittedValueDto();
        }

        if (IsUploadField(snapshot.FieldType))
        {
            var attachments = value.ValueKind == JsonValueKind.Array
                ? value.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.Object && ReadGuid(item, "attachmentId").HasValue)
                    .Select(item => new AdminOrderCheckoutAttachmentPresentationDto
                    {
                        AttachmentId = ReadGuid(item, "attachmentId")!.Value,
                        FileName = ReadString(item, "fileName") ?? string.Empty,
                        ContentType = ReadString(item, "contentType") ?? string.Empty,
                        SizeBytes = ReadNullableInt64(item, "sizeBytes") ?? 0,
                        MediaType = ReadString(item, "mediaType"),
                        UploadedAtUtc = ReadNullableDateTime(item, "uploadedAtUtc"),
                        FinalizedAtUtc = ReadNullableDateTime(item, "finalizedAtUtc")
                    })
                    .ToList()
                : new List<AdminOrderCheckoutAttachmentPresentationDto>();

            return new AdminOrderCheckoutFieldSubmittedValueDto
            {
                Kind = "ATTACHMENTS",
                DisplayValue = attachments.Count == 0
                    ? "Not provided"
                    : string.Join(", ", attachments.Select(attachment => attachment.FileName).Where(name => !string.IsNullOrWhiteSpace(name))),
                Attachments = attachments
            };
        }

        if (snapshot.FieldType is CheckoutFieldType.Select or CheckoutFieldType.Radio)
        {
            var optionValue = ValueAsString(value);
            configuration ??= BuildCheckoutFieldConfigurationPresentation(snapshot);
            var optionLabel = configuration.Options
                .FirstOrDefault(option => string.Equals(option.Value, optionValue, StringComparison.Ordinal))
                ?.Label;

            return new AdminOrderCheckoutFieldSubmittedValueDto
            {
                Kind = "OPTION",
                DisplayValue = string.IsNullOrWhiteSpace(optionLabel)
                    ? optionValue ?? "Not provided"
                    : optionLabel,
                OptionValue = optionValue,
                OptionLabel = optionLabel
            };
        }

        if (snapshot.FieldType == CheckoutFieldType.Checkbox)
        {
            bool? booleanValue = value.ValueKind == JsonValueKind.True
                ? true
                : value.ValueKind == JsonValueKind.False
                    ? false
                    : bool.TryParse(ValueAsString(value), out var parsed) ? parsed : null;

            return new AdminOrderCheckoutFieldSubmittedValueDto
            {
                Kind = "BOOLEAN",
                DisplayValue = booleanValue == true ? "Yes" : "No",
                BooleanValue = booleanValue
            };
        }

        if (snapshot.FieldType == CheckoutFieldType.Number)
        {
            decimal? numberValue = value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var parsedNumber)
                ? parsedNumber
                : decimal.TryParse(ValueAsString(value), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsedTextNumber)
                    ? parsedTextNumber
                    : null;

            return new AdminOrderCheckoutFieldSubmittedValueDto
            {
                Kind = "NUMBER",
                DisplayValue = numberValue?.ToString(CultureInfo.InvariantCulture) ?? "Not provided",
                NumberValue = numberValue
            };
        }

        if (snapshot.FieldType == CheckoutFieldType.Date)
        {
            var dateValue = ValueAsString(value);
            return new AdminOrderCheckoutFieldSubmittedValueDto
            {
                Kind = "DATE",
                DisplayValue = string.IsNullOrWhiteSpace(dateValue) ? "Not provided" : dateValue,
                DateValue = dateValue
            };
        }

        var textValue = ValueAsString(value);
        return new AdminOrderCheckoutFieldSubmittedValueDto
        {
            Kind = "TEXT",
            DisplayValue = string.IsNullOrWhiteSpace(textValue) ? "Not provided" : textValue,
            TextValue = textValue
        };
    }

    private static string BuildSafeCheckoutFieldValueJson(OrderCheckoutFieldSnapshot snapshot)
    {
        if (!IsUploadField(snapshot.FieldType))
        {
            return snapshot.ValueJson;
        }

        var submittedValue = BuildCheckoutFieldSubmittedValuePresentation(snapshot);
        if (submittedValue.Kind == "INVALID")
        {
            return "null";
        }

        return JsonSerializer.Serialize(submittedValue.Attachments.Select(attachment => new
        {
            attachmentId = attachment.AttachmentId,
            fileName = attachment.FileName,
            contentType = attachment.ContentType,
            sizeBytes = attachment.SizeBytes,
            mediaType = attachment.MediaType,
            uploadedAtUtc = attachment.UploadedAtUtc,
            finalizedAtUtc = attachment.FinalizedAtUtc
        }));
    }

    private static string NormalizeAttachmentContentType(string? contentType) =>
        (contentType ?? string.Empty).Split(';', 2)[0].Trim().ToLowerInvariant();

    private static bool IsUploadField(CheckoutFieldType fieldType) =>
        fieldType is CheckoutFieldType.Image or CheckoutFieldType.File or CheckoutFieldType.MultiImage;

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : value.ToString();
    }

    private static List<string> ReadStringArray(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return new List<string>();
        }

        return value.EnumerateArray()
            .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item!)
            .ToList();
    }

    private static Guid? ReadGuid(JsonElement element, string propertyName)
    {
        var value = ReadString(element, propertyName);
        return Guid.TryParse(value, out var parsed) ? parsed : null;
    }

    private static long? ReadNullableInt64(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
        {
            return number;
        }

        return long.TryParse(ReadString(element, propertyName), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static int? ReadNullableInt32(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number;
        }

        return int.TryParse(ReadString(element, propertyName), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static DateTime? ReadNullableDateTime(JsonElement element, string propertyName)
    {
        var value = ReadString(element, propertyName);
        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : null;
    }

    private static string? ValueAsString(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => value.ToString(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => null
    };

    private const string PendingCancellationAdminNoteMarker = "[admin-cancellation-pending]";

    private static bool IsAdminCancellationPending(Order order) =>
        order.Status == OrderStatus.OnHold &&
        !string.IsNullOrWhiteSpace(order.AdminNotes) &&
        order.AdminNotes.Contains(PendingCancellationAdminNoteMarker, StringComparison.Ordinal);

    #endregion
}
