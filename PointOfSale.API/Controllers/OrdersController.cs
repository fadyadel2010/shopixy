using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PointOfSale.API.Infrastructure.Idempotency;
using PointOfSale.Core.Analytics.Attribution;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Marketplace;
using PointOfSale.Core.Events;
using PointOfSale.Core.Pagination;
using PointOfSale.Core.Pricing;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.API.DTOs.CommerceHub;
using PointOfSale.API.Infrastructure.DocumentNumbers;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.API.Services.Coupons;
using PointOfSale.API.Services.Pricing;
using PointOfSale.API.Services.PhoneRisk;
using PointOfSale.Core.Discounts;
using PointOfSale.Core.Financial;
using PointOfSale.Infrastructure.Attribution;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.Data.Repositories;

namespace PointOfSale.API.Controllers;

/// <summary>
/// API controller for managing e-commerce orders
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class OrdersController : ControllerBase
{
    private const string IdempotencyKeyHeader = "Idempotency-Key";
    private const int StaleOrderIdempotencyClaimMinutes = 5;
    private const int AdminOrderInventoryReservationTtlMinutes = 10;
    private static readonly JsonSerializerOptions OrderIdempotencyJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ApplicationDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly IEventPublisher _eventPublisher;
    private readonly IMarketplaceEventOutboxService _marketplaceEventOutbox;
    private readonly IOrderAttributionStore _orderAttributionStore;
    private readonly IPhoneRiskProfileService _phoneRiskProfileService;
    private readonly IOrderVelocityService _orderVelocityService;
    private readonly IRefundEvidenceService _refundEvidenceService;
    private readonly ICouponValidationService _couponValidationService;
    private readonly IShippingCalculationService _shippingCalculationService;
    private readonly IProductVariantStockRepository _stockRepository;
    private readonly CommerceReliabilityTelemetry? _reliabilityTelemetry;
    private readonly ILogger<OrdersController> _logger;

    public OrdersController(
        ApplicationDbContext context,
        ITenantContext tenantContext,
        IEventPublisher eventPublisher,
        IMarketplaceEventOutboxService marketplaceEventOutbox,
        IOrderAttributionStore orderAttributionStore,
        IPhoneRiskProfileService phoneRiskProfileService,
        IOrderVelocityService orderVelocityService,
        IRefundEvidenceService refundEvidenceService,
        ICouponValidationService couponValidationService,
        IShippingCalculationService shippingCalculationService,
        IProductVariantStockRepository stockRepository,
        ILogger<OrdersController> logger,
        CommerceReliabilityTelemetry? reliabilityTelemetry = null)
    {
        _context = context;
        _tenantContext = tenantContext;
        _eventPublisher = eventPublisher;
        _marketplaceEventOutbox = marketplaceEventOutbox;
        _orderAttributionStore = orderAttributionStore;
        _phoneRiskProfileService = phoneRiskProfileService;
        _orderVelocityService = orderVelocityService;
        _refundEvidenceService = refundEvidenceService;
        _couponValidationService = couponValidationService;
        _shippingCalculationService = shippingCalculationService;
        _stockRepository = stockRepository;
        _reliabilityTelemetry = reliabilityTelemetry;
        _logger = logger;
    }

    private bool IsPostgresProvider() => _context.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;
    private bool IsSqlServerProvider() => _context.Database.ProviderName?.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>
    /// Get all orders with filtering
    /// </summary>
    [HttpGet]
    [Authorize(Roles = "Owner,Admin,Manager")]
    public async Task<ActionResult<PagedResult<OrderListDto>>> GetOrders(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] OrderStatus? status = null,
        [FromQuery] PaymentStatus? paymentStatus = null,
        [FromQuery] OrderSource? source = null,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null,
        [FromQuery] string? search = null,
        [FromQuery] bool? affiliateOnly = null) // AF-35: Filter affiliate orders
    {
        page = PaginationGuard.NormalizePage(page);
        pageSize = PaginationGuard.NormalizePageSize(pageSize);
        var skip = PaginationGuard.GetSkip(page, pageSize);
        var tenantId = _tenantContext.RequiredTenantId;

        var standardOrdersQuery = BuildStandardOrdersQuery(
                status,
                paymentStatus,
                source,
                startDate,
                endDate,
                search,
                affiliateOnly)
            .Select(o => new OrderListDto
            {
                Id = o.Id,
                OrderNumber = o.OrderNumber,
                CustomerId = o.CustomerId,
                OnlineCustomerId = o.OnlineCustomerId,
                CustomerName = o.Customer != null
                    ? o.Customer.Name
                    : o.OnlineCustomer != null
                        ? o.OnlineCustomer.DisplayName
                        : o.GuestName,
                CustomerEmail = o.Customer != null
                    ? o.Customer.Email
                    : o.OnlineCustomer != null
                        ? o.OnlineCustomer.Email
                        : o.GuestEmail,
                Status = o.Status,
                PaymentStatus = o.PaymentStatus,
                Source = o.Source,
                SourceOrderId = o.SourceOrderId,
                Total = o.Total,
                Currency = o.Currency,
                ItemCount = o.Items.Count,
                CreatedAt = o.CreatedAt,
                ShippedAt = o.ShippedAt,
                DeliveredAt = o.DeliveredAt,
                IsAffiliateOrder = o.AffiliateAccountId != null,
                AffiliateId = o.AffiliateAccountId,
                IsMarketplaceSubOrder = false
            });

        var marketplaceOrdersQuery = BuildMarketplaceSubOrdersQuery(
                tenantId,
                status,
                paymentStatus,
                source,
                startDate,
                endDate,
                search,
                affiliateOnly)
            .Select(subOrder => new OrderListDto
            {
                Id = subOrder.Id,
                OrderNumber = subOrder.OrderNumber,
                CustomerId = null,
                OnlineCustomerId = null,
                CustomerName = subOrder.MarketplaceOrder.FullName,
                CustomerEmail = null,
                Status = subOrder.Status,
                PaymentStatus = subOrder.PaymentStatus,
                Source = OrderSource.Marketplace,
                SourceOrderId = null,
                Total = subOrder.Total,
                Currency = subOrder.Currency,
                ItemCount = subOrder.Items.Count,
                CreatedAt = subOrder.CreatedAt,
                ShippedAt = null,
                DeliveredAt = null,
                IsAffiliateOrder = false,
                AffiliateId = null,
                IsMarketplaceSubOrder = true
            });

        var combinedQuery = standardOrdersQuery.Concat(marketplaceOrdersQuery);
        var totalCount = await combinedQuery.CountAsync();
        var orders = await combinedQuery
            .OrderByDescending(order => order.CreatedAt)
            .Skip(skip)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new PagedResult<OrderListDto>
        {
            Items = orders,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        });
    }

    /// <summary>
    /// Get order by ID with full details
    /// </summary>
    [HttpGet("{id}")]
    [Authorize(Roles = "Owner,Admin,Manager")]
    public async Task<ActionResult<OrderDetailDto>> GetOrder(Guid id, CancellationToken cancellationToken)
    {
        var matchedStandardOrder = await _context.Orders
            .AsNoTracking()
            .Include(o => o.Customer)
            .Include(o => o.OnlineCustomer)
            .Include(o => o.ShippingMethod)
            .Include(o => o.Coupon)
            .Include(o => o.Items)
                .ThenInclude(i => i.Product)
            .Include(o => o.CheckoutFieldSnapshots)
            .Include(o => o.Warehouse)
            .FirstOrDefaultAsync(
                o => o.Id == id && o.TenantId == _tenantContext.RequiredTenantId,
                cancellationToken);

        if (matchedStandardOrder != null)
        {
            return Ok(await MapToOrderDetailDtoAsync(matchedStandardOrder, cancellationToken));
        }

        var marketplaceSubOrder = await LoadMarketplaceSubOrderByIdAsync(id, cancellationToken);
        if (marketplaceSubOrder == null)
        {
            return NotFound(new { message = "Order not found" });
        }

        return Ok(await MapToMarketplaceOrderDetailDtoAsync(marketplaceSubOrder, cancellationToken));
    }

    /// <summary>
    /// Get order by order number
    /// </summary>
    [HttpGet("by-number/{orderNumber}")]
    [Authorize(Roles = "Owner,Admin,Manager")]
    public async Task<ActionResult<OrderDetailDto>> GetOrderByNumber(string orderNumber, CancellationToken cancellationToken)
    {
        var matchedStandardOrder = await _context.Orders
            .AsNoTracking()
            .Include(o => o.Customer)
            .Include(o => o.OnlineCustomer)
            .Include(o => o.ShippingMethod)
            .Include(o => o.Coupon)
            .Include(o => o.Items)
                .ThenInclude(i => i.Product)
            .Include(o => o.CheckoutFieldSnapshots)
            .Include(o => o.Warehouse)
            .FirstOrDefaultAsync(
                o => o.OrderNumber == orderNumber && o.TenantId == _tenantContext.RequiredTenantId,
                cancellationToken);

        if (matchedStandardOrder != null)
        {
            return Ok(await MapToOrderDetailDtoAsync(matchedStandardOrder, cancellationToken));
        }

        var marketplaceSubOrder = await LoadMarketplaceSubOrderByOrderNumberAsync(orderNumber, cancellationToken);
        if (marketplaceSubOrder == null)
        {
            return NotFound(new { message = "Order not found" });
        }

        return Ok(await MapToMarketplaceOrderDetailDtoAsync(marketplaceSubOrder, cancellationToken));
    }

    /// <summary>
    /// Create a new order
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Owner,Admin,Manager")]
    public async Task<ActionResult<OrderDetailDto>> CreateOrder(CreateOrderDto dto, CancellationToken cancellationToken)
    {
        if (dto.Source is OrderSource.Ecommerce or OrderSource.Marketplace)
        {
            return BadRequest(new
            {
                message = "Commerce orders must be created through the storefront or CommerceHub checkout pipeline so cart, payment intent recovery, inventory reservation, projections, settlement, and accounting stay synchronized."
            });
        }

        var idempotencyResolution = ResolveCreateOrderIdempotencyKey();
        if (idempotencyResolution.ErrorResult != null)
        {
            return idempotencyResolution.ErrorResult;
        }

        var requestHash = BuildCreateOrderRequestHash(dto);
        // snapshotHash is intentionally null here: CreateOrder has no server-authoritative
        // order snapshot at claim time, so there is nothing distinct to compare against.
        // The infrastructure column exists for the Checkout flow, which does derive a
        // real snapshot hash via CheckoutRequestIdempotencyCoordinator.
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        OrderRequestIdempotency? idempotencyRecord = null;
        OrderDetailDto? response = null;
        var transactionCommitted = false;

        try
        {
            var claim = await TryClaimOrderIdempotencyAsync(
                idempotencyResolution.IdempotencyKey!,
                requestHash,
                snapshotHash: null,
                cancellationToken);

            if (claim.ShortCircuitResult != null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return claim.ShortCircuitResult;
            }

            idempotencyRecord = claim.Record!;
            var (financials, errorResult) = await CalculateAuthoritativeCreateOrderFinancialsAsync(dto, cancellationToken);
            if (errorResult != null)
            {
                await transaction.RollbackAsync(cancellationToken);
                await CleanupOrderIdempotencyAsync(idempotencyRecord, cancellationToken);
                return errorResult;
            }

            var validatedCurrency = await ValidateCreateOrderCurrencyAsync(dto, cancellationToken);

            var customerValidationError = await ValidateCreateOrderCustomerAsync(dto, cancellationToken);
            if (customerValidationError != null)
            {
                await transaction.RollbackAsync(cancellationToken);
                await CleanupOrderIdempotencyAsync(idempotencyRecord, cancellationToken);
                return customerValidationError;
            }

            var onlineCustomerValidationError = await ValidateCreateOrderOnlineCustomerAsync(dto, cancellationToken);
            if (onlineCustomerValidationError != null)
            {
                await transaction.RollbackAsync(cancellationToken);
                await CleanupOrderIdempotencyAsync(idempotencyRecord, cancellationToken);
                return onlineCustomerValidationError;
            }

            // Generate order number
            var orderNumber = await GenerateOrderNumber(cancellationToken);

            // Create order
            var order = new Order
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantContext.RequiredTenantId,  // Multi-tenant
                OrderNumber = orderNumber,
                CustomerId = dto.CustomerId,
                OnlineCustomerId = dto.Source == OrderSource.Ecommerce ? dto.OnlineCustomerId : null,
                GuestEmail = dto.GuestEmail,
                GuestPhone = dto.GuestPhone,
                GuestName = dto.GuestName,
                Status = OrderStatus.Pending,
                PaymentStatus = PaymentStatus.Pending,
                ShippingAddress = dto.ShippingAddress,
                ShippingCity = dto.ShippingCity,
                ShippingState = dto.ShippingState,
                ShippingZipCode = dto.ShippingZipCode,
                ShippingCountry = dto.ShippingCountry,
                BillingAddress = dto.BillingAddress,
                BillingCity = dto.BillingCity,
                BillingState = dto.BillingState,
                BillingZipCode = dto.BillingZipCode,
                BillingCountry = dto.BillingCountry,
                ShippingMethodId = dto.ShippingMethodId,
                ShippingCost = financials!.ShippingCost,
                Subtotal = financials.Subtotal,
                DiscountAmount = financials.DiscountAmount,
                TaxAmount = financials.TaxAmount,
                Total = financials.Total,
                Currency = validatedCurrency,
                CouponCode = financials.Coupon?.Code,
                CouponId = financials.Coupon?.Id,
                PaymentMethod = dto.PaymentMethod,
                CustomerNotes = dto.CustomerNotes,
                Source = dto.Source,
                SourceOrderId = dto.SourceOrderId,
                WarehouseId = dto.WarehouseId
            };

            _context.Orders.Add(order);

            // Add order items
            foreach (var item in financials.Items)
            {
                var orderItem = new OrderItem
                {
                    OrderId = order.Id,
                    ProductId = item.Request.ProductId,
                    VariantId = item.Variant?.Id,
                    UnitId = item.UnitId,
                    WarehouseId = item.Request.WarehouseId ?? dto.WarehouseId,
                    SKU = item.Variant?.SKU ?? item.Product.SKU ?? item.Product.Barcode ?? item.Product.Id.ToString(),
                    ProductName = item.Product.Name,
                    VariantName = item.Request.VariantName,
                    ImageUrl = item.Variant?.ImageUrl ?? item.Product.Images?.Split(',').FirstOrDefault(),
                    Quantity = item.Request.Quantity,
                    UnitPrice = item.UnitPrice,
                    DiscountAmount = item.DiscountAmount,
                    TaxAmount = item.TaxAmount,
                    Total = item.Total,
                    CostPrice = item.Variant?.CostPrice ?? 0m,
                    Notes = item.Request.Notes
                };

                order.Items.Add(orderItem);
            }

            var inventoryReservationError = await ReserveInventoryForCreatedOrderAsync(
                order.TenantId,
                order,
                financials,
                cancellationToken);
            if (inventoryReservationError != null)
            {
                await transaction.RollbackAsync(cancellationToken);
                await CleanupOrderIdempotencyAsync(idempotencyRecord, cancellationToken);
                return inventoryReservationError;
            }

            if (financials.Coupon != null)
            {
                await _couponValidationService.ReserveCouponAsync(
                    _tenantContext.RequiredTenantId,
                    financials.Coupon.Id,
                    financials.Coupon.Code,
                    order.Id,
                    ResolveCreateOrderCouponCustomerId(dto),
                    new CouponValidationSnapshot
                    {
                        OrderSubtotal = order.Subtotal,
                        AppliedDiscountAmount = order.DiscountAmount,
                        CartItems = financials.Items
                            .Select(item => new CouponScopeValidationItem(
                                item.Product.Id,
                                item.Product.CategoryId))
                            .ToList()
                    },
                    HttpContext.TraceIdentifier,
                    cancellationToken);
            }

            await _marketplaceEventOutbox.EnqueueAsync(new PointOfSale.Core.Events.Marketplace.OrderCreatedEvent
            {
                CorrelationId = HttpContext.TraceIdentifier,
                EntityId = order.Id,
                AggregateRootId = order.Id,
                TenantId = order.TenantId,
                Version = ResolveCreatedOrderEventVersion(order),
                OrderNumber = order.OrderNumber,
                CustomerId = order.CustomerId,
                OnlineCustomerId = order.OnlineCustomerId,
                TotalAmount = order.Total,
                SubtotalAmount = order.Subtotal,
                Currency = order.Currency,
                PaymentMethodCode = string.IsNullOrWhiteSpace(order.PaymentMethodCode)
                    ? order.PaymentMethod.ToString()
                    : order.PaymentMethodCode,
                PaymentMethodName = string.IsNullOrWhiteSpace(order.PaymentMethodName)
                    ? order.PaymentMethod.ToString()
                    : order.PaymentMethodName,
                IsPaid = order.IsPaid,
                Items = order.Items.Select(i => new PointOfSale.Core.Events.Marketplace.MarketplaceOrderItem
                {
                    ProductId = i.ProductId,
                    Sku = i.SKU,
                    Quantity = i.Quantity,
                    UnitPrice = i.UnitPrice
                }).ToList()
            }, cancellationToken);

            if (order.CustomerId.HasValue)
            {
                order.Customer = await _context.Customers
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        customer => customer.Id == order.CustomerId.Value,
                        cancellationToken);
            }

            order.ShippingMethod = financials.ShippingMethod;

            if (order.WarehouseId.HasValue)
            {
                order.Warehouse = await _context.Warehouses
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        warehouse => warehouse.Id == order.WarehouseId.Value,
                        cancellationToken);
            }

            idempotencyRecord.OrderId = order.Id;
            idempotencyRecord.CompletedAtUtc ??= DateTime.UtcNow;
            idempotencyRecord.UpdatedAt = DateTime.UtcNow;

            // Publish through the MassTransit EF outbox before SaveChanges so the
            // event is committed atomically with the order and idempotency record.
            await PublishOrderCreatedEventAsync(order, idempotencyResolution.IdempotencyKey!, cancellationToken);

            await _context.SaveChangesAsync(cancellationToken);
            
            // Commit the transaction immediately to release locks before expensive enrichment
            await transaction.CommitAsync(cancellationToken);
            transactionCommitted = true;

            // Record coupon telemetry after durable commit
            if (order.CouponId.HasValue)
            {
                _reliabilityTelemetry?.RecordCouponReservationCreated(_tenantContext.RequiredTenantId, "pos");
            }

            // Perform expensive enrichment operations outside the transaction
            response = await MapToOrderDetailDtoAsync(order, cancellationToken);

            // Update idempotency record with minimal replay payload (outside transaction)
            // Store only non-sensitive fields to avoid persisting PII like email, phone, addresses
            var minimalReplay = OrderIdempotencyReplayDto.FromOrderDetail(response);
            idempotencyRecord.ResponseStatusCode = StatusCodes.Status201Created;
            idempotencyRecord.ResponsePayload = SerializeOrderPayload(minimalReplay);
            idempotencyRecord.CompletedAtUtc = DateTime.UtcNow;
            idempotencyRecord.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync(cancellationToken);

            return CreatedAtAction(nameof(GetOrder), new { id = order.Id }, response);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (!transactionCommitted)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                await CleanupOrderIdempotencyAsync(idempotencyRecord, CancellationToken.None);
            }

            throw;
        }
        catch (InvalidOperationException ex) when (IsCurrencyValidationFailure(ex))
        {
            if (!transactionCommitted)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                await CleanupOrderIdempotencyAsync(idempotencyRecord, CancellationToken.None);
            }

            return BadRequest(new
            {
                message = ex.Message
            });
        }
        catch (InvalidOperationException ex) when (IsCouponConflict(ex))
        {
            if (!transactionCommitted)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                await CleanupOrderIdempotencyAsync(idempotencyRecord, CancellationToken.None);
            }

            _logger.LogWarning(
                ex,
                "Coupon consumption rejected order creation for tenant {TenantId}",
                _tenantContext.RequiredTenantId);

            return Conflict(new
            {
                message = ex.Message
            });
        }
        catch (DbUpdateException ex) when (IsSourceOrderIdentityConflict(ex))
        {
            if (!transactionCommitted)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                await CleanupOrderIdempotencyAsync(idempotencyRecord, CancellationToken.None);
            }

            _logger.LogWarning(
                ex,
                "Duplicate source order identity rejected for tenant {TenantId}, source {Source}, source order {SourceOrderId}",
                _tenantContext.RequiredTenantId,
                dto.Source,
                dto.SourceOrderId);

            return Conflict(new
            {
                message = "An active order already exists for this source order identity."
            });
        }
        catch (Exception ex)
        {
            if (!transactionCommitted)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                await CleanupOrderIdempotencyAsync(idempotencyRecord, CancellationToken.None);
            }

            _logger.LogError(ex, "Failed to create order for tenant {TenantId}", _tenantContext.RequiredTenantId);
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                message = "Failed to create order."
            });
        }
    }

    private Task PublishOrderCreatedEventAsync(
        Order order,
        string requestIdempotencyKey,
        CancellationToken cancellationToken)
    {
        var evt = new OrderCreatedEvent
        {
            TenantId = order.TenantId,
            EventVersion = ResolveCreatedOrderEventVersion(order),
            CorrelationId = string.IsNullOrWhiteSpace(HttpContext.TraceIdentifier)
                ? order.Id.ToString("N")
                : HttpContext.TraceIdentifier,
            IdempotencyKey = $"orders:create:{order.TenantId}:{requestIdempotencyKey}",
            OrderId = order.Id,
            OrderNumber = order.OrderNumber,
            CustomerId = order.CustomerId,
            OnlineCustomerId = order.OnlineCustomerId,
            SessionId = order.SessionId,
            CustomerEmail = order.CustomerId.HasValue ? order.Customer?.Email : order.GuestEmail,
            CustomerName = order.CustomerId.HasValue ? order.Customer?.Name : order.GuestName,
            Status = order.Status,
            PaymentStatus = order.PaymentStatus,
            Source = order.Source,
            SourceOrderId = order.SourceOrderId,
            Subtotal = order.Subtotal,
            DiscountAmount = order.DiscountAmount,
            TaxAmount = order.TaxAmount,
            ShippingCost = order.ShippingCost,
            Total = order.Total,
            Currency = order.Currency,
            Items = order.Items.Select(i => new OrderItemInfo
            {
                ProductId = i.ProductId,
                SKU = i.SKU,
                ProductName = i.ProductName,
                Quantity = i.Quantity,
                UnitPrice = i.UnitPrice,
                Total = i.Total
            }).ToList()
        };

        return _eventPublisher.PublishAsync(evt, cancellationToken);
    }

    private static bool IsSourceOrderIdentityConflict(DbUpdateException exception)
    {
        const string indexName = "IX_Orders_TenantId_Source_SourceOrderId";

        if (exception.InnerException is PostgresException postgresException)
        {
            return postgresException.SqlState == PostgresErrorCodes.UniqueViolation &&
                   string.Equals(postgresException.ConstraintName, indexName, StringComparison.Ordinal);
        }

        return exception.InnerException?.Message.Contains(indexName, StringComparison.OrdinalIgnoreCase) == true;
    }

    private static int ResolveCreatedOrderEventVersion(Order order) =>
        PointOfSale.Core.Helpers.ProductEventVersionHelper.ToProductEventVersion(
            order.ProjectionMutationVersion > 0
                ? order.ProjectionMutationVersion
                : 1);

    private async Task<ActionResult<OrderDetailDto>?> ReserveInventoryForCreatedOrderAsync(
        Guid tenantId,
        Order order,
        AuthoritativeCreateOrderFinancials financials,
        CancellationToken cancellationToken)
    {
        var expiresAtUtc = DateTime.UtcNow.AddMinutes(AdminOrderInventoryReservationTtlMinutes);

        foreach (var item in financials.Items)
        {
            if (item.Variant == null)
            {
                _logger.LogError(
                    "Admin order creation blocked because product has no active default variant for stock reservation | TenantId: {TenantId} | ProductId: {ProductId} | OrderId: {OrderId}",
                    tenantId,
                    item.Product.Id,
                    order.Id);

                return BadRequest(new
                {
                    message = $"Product {item.Product.Id} cannot be ordered because it has no active default sellable variant."
                });
            }

            var warehouseId = item.Request.WarehouseId ?? order.WarehouseId;
            var stockQuantity = item.Request.Quantity * item.ConversionFactor;
            var reserved = await _stockRepository.ReserveAsync(
                tenantId,
                item.Variant.Id,
                warehouseId,
                stockQuantity,
                order.Id,
                expiresAtUtc,
                cancellationToken);

            if (!reserved)
            {
                _logger.LogWarning(
                    "Admin order creation blocked by insufficient stock | TenantId: {TenantId} | ProductId: {ProductId} | VariantId: {VariantId} | WarehouseId: {WarehouseId} | Quantity: {Quantity} | OrderId: {OrderId}",
                    tenantId,
                    item.Product.Id,
                    item.Variant.Id,
                    warehouseId,
                    stockQuantity,
                    order.Id);

                return Conflict(new
                {
                    message = "Insufficient stock to reserve all order items.",
                    productId = item.Product.Id,
                    variantId = item.Variant.Id,
                    warehouseId,
                    requestedQuantity = stockQuantity
                });
            }
        }

        await _stockRepository.BindOrderReservationsAsync(
            tenantId,
            order.Id,
            order.Items.ToList(),
            cancellationToken);

        return null;
    }

    private async Task<string> ValidateCreateOrderCurrencyAsync(
        CreateOrderDto dto,
        CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.RequiredTenantId;
        var context = $"tenantId:{tenantId:D};source:orders_controller";

        try
        {
            var tenantCurrency = await _context.Tenants
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(tenant => tenant.Id == tenantId)
                .Select(tenant => tenant.Currency)
                .FirstOrDefaultAsync(cancellationToken);

            tenantCurrency = FinancialCurrencyGuard.RequireCurrency(
                tenantCurrency,
                "tenant.currency",
                context);

            return FinancialCurrencyGuard.RequireMatch(
                dto.Currency ?? string.Empty,
                "order.request.currency",
                tenantCurrency,
                "tenant.currency",
                context);
        }
        catch (FinancialCurrencyMismatchException ex)
        {
            _reliabilityTelemetry?.RecordCurrencyMismatch(tenantId, null, "order_creation");
            _logger.LogError(ex, "Order creation rejected due to currency mismatch | TenantId: {TenantId}", tenantId);
            throw new InvalidOperationException("Order currency must match the tenant currency configuration.", ex);
        }
        catch (FinancialCurrencyMissingException ex)
        {
            _reliabilityTelemetry?.RecordCurrencyMissing(tenantId, null, "order_creation");
            _logger.LogError(ex, "Order creation rejected due to missing currency | TenantId: {TenantId}", tenantId);
            throw new InvalidOperationException("Order currency is required and tenant currency must be configured.", ex);
        }
        catch (FinancialCurrencyInvalidException ex)
        {
            _reliabilityTelemetry?.RecordCurrencyInvalid(tenantId, null, "order_creation");
            _logger.LogError(ex, "Order creation rejected due to invalid currency | TenantId: {TenantId}", tenantId);
            throw new InvalidOperationException("Order currency is invalid.", ex);
        }
    }

    /// <summary>
    /// [DEPRECATED] Update order status.
    /// 
    /// This endpoint is BLOCKED to enforce tenant isolation and lifecycle side effects.
    /// Direct status mutations without ownership verification or lifecycle event emissions
    /// corrupt cross-tenant data and financial integrity.
    /// 
    /// Use the Admin Orders API instead:
    /// - PUT /api/admin/orders/{orderId}/status
    /// - POST /api/admin/orders/{orderId}/cancel
    /// </summary>
    [HttpPatch("{id}/status")]
    [Authorize(Roles = "Owner,Admin,Manager")]
    [Obsolete("Use Admin Orders API: /api/admin/orders/{orderId}/...")]
    public async Task<IActionResult> UpdateOrderStatus(Guid id, UpdateOrderStatusDto dto)
    {
        // ═══════════════════════════════════════════════════════════════════════
        // PRODUCTION HARDENING: GUARD BLOCK
        // ═══════════════════════════════════════════════════════════════════════
        // Direct status changes here are PROHIBITED because:
        // 1. It bypasses TenantId ownership checks, creating cross-tenant mutation risks.
        // 2. It bypasses critical lifecycle side effects (e.g. inventory release, accounting).
        // 
        // This endpoint returns 410 Gone.
        // ═══════════════════════════════════════════════════════════════════════

        _logger.LogWarning(
            "DEPRECATED_ENDPOINT_BLOCKED | Endpoint: PATCH /api/orders/{OrderId}/status | OrderId: {OrderId} | User: {User} | TraceId: {TraceId}",
            id, id, User.Identity?.Name ?? "Unknown", HttpContext.TraceIdentifier);

        return StatusCode(410, new
        {
            error = "DEPRECATED_ENDPOINT",
            message = "Direct order status changes via this legacy endpoint are prohibited. Use the Admin Orders API.",
            reason = "Cross-tenant security and financial integrity require tenant ownership verification and proper lifecycle event emission.",
            migration = new
            {
                updateStatus = $"PUT /api/admin/orders/{id}/status",
                cancelOrder = $"POST /api/admin/orders/{id}/cancel"
            }
        });
    }

    /// <summary>
    /// [DEPRECATED] Update payment status.
    /// 
    /// PHASE: COD Payment Lifecycle Architecture
    /// 
    /// This endpoint is BLOCKED to enforce ledger-first principles.
    /// Direct PaymentStatus changes are prohibited because they bypass journal creation.
    /// 
    /// Use the certified payment workflow instead:
    /// - COD orders: POST /api/payments/lifecycle/{orderId}/cod-delivered
    /// - Manual wallet orders: use the Admin Manual Payment review endpoints
    /// - Refunds: POST /api/payments/lifecycle/{orderId}/refund
    /// 
    /// GET /api/payments/lifecycle/{orderId}/actions to see available actions.
    /// </summary>
    [HttpPatch("{id}/payment-status")]
    [Authorize(Roles = "Owner,Admin,Manager")]
    [Obsolete("Use Payment Lifecycle API: /api/payments/lifecycle/{orderId}/...")]
    public async Task<IActionResult> UpdatePaymentStatus(Guid id, UpdatePaymentStatusDto dto)
    {
        // ═══════════════════════════════════════════════════════════════════════
        // PHASE: COD Payment Lifecycle Architecture - GUARD BLOCK
        // ═══════════════════════════════════════════════════════════════════════
        // 
        // Direct PaymentStatus changes are PROHIBITED because:
        // 1. They bypass journal entry creation
        // 2. Money recognition without ledger posting violates financial integrity
        // 3. The ledger becomes inconsistent with order state
        //
        // This endpoint returns 410 Gone with migration instructions.
        // ═══════════════════════════════════════════════════════════════════════
        
        var order = await _context.Orders.FindAsync(id);
        
        if (order == null)
        {
            return NotFound(new { message = "Order not found" });
        }
        
        // Determine appropriate new endpoint based on payment method
        var paymentMethodCode = order.PaymentMethodCode?.ToUpperInvariant() ?? "";
        var isCod = paymentMethodCode == "COD";
        var isWallet = paymentMethodCode.Contains("WALLET") || 
                       paymentMethodCode.Contains("VODAFONE") || 
                       paymentMethodCode.Contains("INSTAPAY");
        
        string suggestedEndpoint;
        string actionDescription;
        
        if (isCod)
        {
            suggestedEndpoint = $"/api/payments/lifecycle/{id}/cod-delivered";
            actionDescription = "Mark as Delivered (COD)";
        }
        else if (isWallet)
        {
            suggestedEndpoint = $"/api/admin/manual-payments/{id}/confirm";
            actionDescription = "Review Manual Wallet Payment";
        }
        else
        {
            suggestedEndpoint = $"/api/payments/lifecycle/{id}/refund";
            actionDescription = "Online payments are captured via gateway webhooks. Use refund endpoint if needed.";
        }
        
        return StatusCode(410, new
        {
            error = "DEPRECATED_ENDPOINT",
            message = "Direct PaymentStatus changes are no longer allowed. Use the Payment Lifecycle API.",
            reason = "Financial integrity requires all payment status changes to create journal entries.",
            migration = new
            {
                checkAvailableActions = $"GET /api/payments/lifecycle/{id}/actions",
                suggestedEndpoint = suggestedEndpoint,
                actionDescription = actionDescription
            },
            documentation = "See PAYMENT_LIFECYCLE_ARCHITECTURE.md for details."
        });
    }

    /// <summary>
    /// Get order statistics
    /// </summary>
    [HttpGet("statistics")]
    [Authorize(Roles = "Owner,Admin,Manager")]
    public async Task<ActionResult<OrderStatisticsDto>> GetOrderStatistics(
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null)
    {
        var tenantId = _tenantContext.RequiredTenantId;
        var revenueStatuses = new[]
        {
            PaymentStatus.Collected,
            PaymentStatus.Settled
        };

        var standardQuery = BuildStandardOrdersQuery(
            status: null,
            paymentStatus: null,
            source: null,
            startDate,
            endDate,
            search: null,
            affiliateOnly: null);

        var marketplaceQuery = BuildMarketplaceSubOrdersQuery(
            tenantId,
            status: null,
            paymentStatus: null,
            source: null,
            startDate,
            endDate,
            search: null,
            affiliateOnly: null);

        // Aggregate all standard order statistics in a single query
        var standardStats = await standardQuery
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Count = g.Count(),
                Gross = g.Sum(o => (decimal?)o.Total) ?? 0m,
                Revenue = g.Where(o => revenueStatuses.Contains(o.PaymentStatus)).Sum(o => (decimal?)o.Total) ?? 0m,
                StatusCounts = g.GroupBy(o => o.Status)
                    .Select(sg => new StatusCountRow { Status = sg.Key, Count = sg.Count() })
                    .ToList()
            })
            .FirstOrDefaultAsync();

        // Aggregate all marketplace order statistics in a single query
        var marketplaceStats = await marketplaceQuery
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Count = g.Count(),
                Gross = g.Sum(o => (decimal?)o.Total) ?? 0m,
                Revenue = g.Where(o => revenueStatuses.Contains(o.PaymentStatus)).Sum(o => (decimal?)o.Total) ?? 0m,
                StatusCounts = g.GroupBy(o => o.Status)
                    .Select(sg => new StatusCountRow { Status = sg.Key, Count = sg.Count() })
                    .ToList()
            })
            .FirstOrDefaultAsync();

        var standardOrderCount = standardStats?.Count ?? 0;
        var marketplaceOrderCount = marketplaceStats?.Count ?? 0;
        var standardGross = standardStats?.Gross ?? 0m;
        var marketplaceGross = marketplaceStats?.Gross ?? 0m;
        var standardRevenue = standardStats?.Revenue ?? 0m;
        var marketplaceRevenue = marketplaceStats?.Revenue ?? 0m;
        var standardStatusCounts = standardStats?.StatusCounts ?? new List<StatusCountRow>();
        var marketplaceStatusCounts = marketplaceStats?.StatusCounts ?? new List<StatusCountRow>();

        var combinedTotalOrders = standardOrderCount + marketplaceOrderCount;
        var combinedTotalRevenue = standardRevenue + marketplaceRevenue;
        var combinedAverageOrderValue = combinedTotalOrders > 0
            ? (standardGross + marketplaceGross) / combinedTotalOrders
            : 0m;

        return Ok(new OrderStatisticsDto
        {
            TotalOrders = combinedTotalOrders,
            TotalRevenue = combinedTotalRevenue,
            AverageOrderValue = combinedAverageOrderValue,
            PendingOrders = GetStatusCount(OrderStatus.Pending, standardStatusCounts, marketplaceStatusCounts),
            ProcessingOrders = GetStatusCount(OrderStatus.Processing, standardStatusCounts, marketplaceStatusCounts),
            ShippedOrders = GetStatusCount(OrderStatus.Shipped, standardStatusCounts, marketplaceStatusCounts),
            DeliveredOrders = GetStatusCount(OrderStatus.Delivered, standardStatusCounts, marketplaceStatusCounts),
            CancelledOrders = GetStatusCount(OrderStatus.Cancelled, standardStatusCounts, marketplaceStatusCounts)
        });
    }

    private IQueryable<Order> BuildStandardOrdersQuery(
        OrderStatus? status,
        PaymentStatus? paymentStatus,
        OrderSource? source,
        DateTime? startDate,
        DateTime? endDate,
        string? search,
        bool? affiliateOnly)
    {
        var query = _context.Orders
            .AsNoTracking()
            .Include(o => o.Customer)
            .Include(o => o.OnlineCustomer)
            .AsQueryable();

        query = query.Where(o =>
            (!o.PaymentMethodCode.Contains("MANUALWALLET") &&
             !o.PaymentMethodCode.Contains("WALLET") &&
             !o.PaymentMethodCode.Contains("VODAFONE") &&
             !o.PaymentMethodCode.Contains("INSTAPAY"))
            ||
            (o.PaymentStatus == PaymentStatus.Collected ||
             o.PaymentStatus == PaymentStatus.Settled ||
             o.PaymentStatus == PaymentStatus.Refunded ||
             o.PaymentStatus == PaymentStatus.PartiallyRefunded));

        if (status.HasValue)
        {
            query = query.Where(o => o.Status == status.Value);
        }

        if (paymentStatus.HasValue)
        {
            query = query.Where(o => o.PaymentStatus == paymentStatus.Value);
        }

        if (source.HasValue)
        {
            query = query.Where(o => o.Source == source.Value);
        }

        if (startDate.HasValue)
        {
            query = query.Where(o => o.CreatedAt >= startDate.Value);
        }

        if (endDate.HasValue)
        {
            query = query.Where(o => o.CreatedAt <= endDate.Value.AddDays(1));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(o =>
                o.OrderNumber.Contains(search) ||
                (o.Customer != null && o.Customer.Name.Contains(search)) ||
                (o.GuestEmail != null && o.GuestEmail.Contains(search)) ||
                (o.SourceOrderId != null && o.SourceOrderId.Contains(search)));
        }

        if (affiliateOnly == true)
        {
            query = query.Where(o => o.AffiliateAccountId != null);
        }

        return query;
    }

    private IQueryable<CommerceHubSubOrder> BuildMarketplaceSubOrdersQuery(
        Guid tenantId,
        OrderStatus? status,
        PaymentStatus? paymentStatus,
        OrderSource? source,
        DateTime? startDate,
        DateTime? endDate,
        string? search,
        bool? affiliateOnly)
    {
        var query = _context.CommerceHubSubOrders
            .AsNoTracking()
            .Where(subOrder =>
                !subOrder.IsDeleted &&
                subOrder.StoreId == tenantId &&
                !subOrder.MarketplaceOrder.IsDeleted);

        if (affiliateOnly == true ||
            (source.HasValue && source.Value != OrderSource.Marketplace))
        {
            return query.Where(_ => false);
        }

        if (status.HasValue)
        {
            query = query.Where(subOrder => subOrder.Status == status.Value);
        }

        if (paymentStatus.HasValue)
        {
            query = query.Where(subOrder => subOrder.PaymentStatus == paymentStatus.Value);
        }

        if (startDate.HasValue)
        {
            query = query.Where(subOrder => subOrder.CreatedAt >= startDate.Value);
        }

        if (endDate.HasValue)
        {
            query = query.Where(subOrder => subOrder.CreatedAt <= endDate.Value.AddDays(1));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(subOrder =>
                subOrder.OrderNumber.Contains(search) ||
                subOrder.MarketplaceOrder.FullName.Contains(search) ||
                subOrder.MarketplaceOrder.PhoneNumber.Contains(search));
        }

        return query;
    }

    private async Task<CommerceHubSubOrder?> LoadMarketplaceSubOrderByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.RequiredTenantId;

        return await _context.CommerceHubSubOrders
            .AsNoTracking()
            .Include(subOrder => subOrder.MarketplaceOrder)
            .Include(subOrder => subOrder.Items)
            .FirstOrDefaultAsync(
                subOrder =>
                    subOrder.Id == id &&
                    !subOrder.IsDeleted &&
                    subOrder.StoreId == tenantId &&
                    !subOrder.MarketplaceOrder.IsDeleted,
                cancellationToken);
    }

    private async Task<CommerceHubSubOrder?> LoadMarketplaceSubOrderByOrderNumberAsync(
        string orderNumber,
        CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.RequiredTenantId;

        return await _context.CommerceHubSubOrders
            .AsNoTracking()
            .Include(subOrder => subOrder.MarketplaceOrder)
            .Include(subOrder => subOrder.Items)
            .FirstOrDefaultAsync(
                subOrder =>
                    subOrder.OrderNumber == orderNumber &&
                    !subOrder.IsDeleted &&
                    subOrder.StoreId == tenantId &&
                    !subOrder.MarketplaceOrder.IsDeleted,
                cancellationToken);
    }

    private static int GetStatusCount(
        OrderStatus status,
        IEnumerable<StatusCountRow> standardStatusCounts,
        IEnumerable<StatusCountRow> marketplaceStatusCounts)
    {
        var standardCount = standardStatusCounts.FirstOrDefault(item => item.Status == status)?.Count ?? 0;
        var marketplaceCount = marketplaceStatusCounts.FirstOrDefault(item => item.Status == status)?.Count ?? 0;
        return standardCount + marketplaceCount;
    }

    private (string? IdempotencyKey, ActionResult<OrderDetailDto>? ErrorResult) ResolveCreateOrderIdempotencyKey()
    {
        var rawIdempotencyKey = Request.Headers[IdempotencyKeyHeader].FirstOrDefault()?.Trim();

        if (string.IsNullOrWhiteSpace(rawIdempotencyKey))
        {
            return (null, BadRequest(new
            {
                message = $"{IdempotencyKeyHeader} header is required for order creation."
            }));
        }

        if (!Guid.TryParse(rawIdempotencyKey, out var parsedIdempotencyKey))
        {
            return (null, BadRequest(new
            {
                message = $"{IdempotencyKeyHeader} must be a valid GUID."
            }));
        }

        return (parsedIdempotencyKey.ToString("D"), null);
    }

    private static string BuildCreateOrderRequestHash(CreateOrderDto dto)
    {
        return DeterministicRequestHasher.ComputeHash(new
        {
            Operation = "CREATE_ORDER",
            dto.CustomerId,
            dto.OnlineCustomerId,
            GuestEmail = dto.GuestEmail?.Trim().ToLowerInvariant(),
            GuestPhone = dto.GuestPhone?.Trim(),
            GuestName = dto.GuestName?.Trim(),
            ShippingAddress = dto.ShippingAddress?.Trim(),
            ShippingCity = dto.ShippingCity?.Trim(),
            ShippingState = dto.ShippingState?.Trim(),
            ShippingZipCode = dto.ShippingZipCode?.Trim(),
            ShippingCountry = dto.ShippingCountry?.Trim(),
            BillingAddress = dto.BillingAddress?.Trim(),
            BillingCity = dto.BillingCity?.Trim(),
            BillingState = dto.BillingState?.Trim(),
            BillingZipCode = dto.BillingZipCode?.Trim(),
            BillingCountry = dto.BillingCountry?.Trim(),
            dto.ShippingMethodId,
            dto.ShippingCost,
            dto.Subtotal,
            dto.DiscountAmount,
            dto.TaxAmount,
            Currency = dto.Currency?.Trim().ToUpperInvariant(),
            CouponCode = dto.CouponCode?.Trim().ToUpperInvariant(),
            dto.PaymentMethod,
            CustomerNotes = dto.CustomerNotes?.Trim(),
            dto.Source,
            SourceOrderId = dto.SourceOrderId?.Trim(),
            dto.WarehouseId,
            Items = (dto.Items ?? [])
                .OrderBy(item => item.ProductId)
                .ThenBy(item => item.UnitId)
                .ThenBy(item => item.WarehouseId)
                .ThenBy(item => item.VariantName, StringComparer.Ordinal)
                .ThenBy(item => item.Notes, StringComparer.Ordinal)
                .Select(item => new
                {
                    item.ProductId,
                    item.UnitId,
                    item.WarehouseId,
                    VariantName = item.VariantName?.Trim(),
                    item.Quantity,
                    item.UnitPrice,
                    item.DiscountAmount,
                    item.TaxAmount,
                    Notes = item.Notes?.Trim()
                })
                .ToList()
        });
    }

    private async Task<BadRequestObjectResult?> ValidateCreateOrderOnlineCustomerAsync(
        CreateOrderDto dto,
        CancellationToken cancellationToken)
    {
        if (dto.Source == OrderSource.Ecommerce)
        {
            if (!dto.OnlineCustomerId.HasValue)
            {
                return null;
            }

            var onlineCustomerExists = await _context.OnlineCustomers
                .AsNoTracking()
                .AnyAsync(customer =>
                    customer.Id == dto.OnlineCustomerId.Value &&
                    customer.TenantId == _tenantContext.RequiredTenantId &&
                    customer.IsActive,
                    cancellationToken);

            return onlineCustomerExists
                ? null
                : BadRequest(new
                {
                    message = "OnlineCustomerId does not reference an active storefront customer for this tenant."
                });
        }

        if (dto.OnlineCustomerId.HasValue)
        {
            return BadRequest(new
            {
                message = "OnlineCustomerId is only valid for ecommerce storefront orders."
            });
        }

        return null;
    }

    private async Task<BadRequestObjectResult?> ValidateCreateOrderCustomerAsync(
        CreateOrderDto dto,
        CancellationToken cancellationToken)
    {
        if (!dto.CustomerId.HasValue)
        {
            return null;
        }

        var tenantId = _tenantContext.RequiredTenantId;
        var customerExists = await _context.Customers
            .AsNoTracking()
            .AnyAsync(
                customer =>
                    customer.Id == dto.CustomerId.Value &&
                    customer.TenantId == tenantId &&
                    !customer.IsDeleted,
                cancellationToken);

        return customerExists
            ? null
            : BadRequest(new
            {
                message = "CustomerId does not reference a current customer for this tenant."
            });
    }

    private static Guid? ResolveCreateOrderCouponCustomerId(CreateOrderDto dto) =>
        dto.Source == OrderSource.Ecommerce
            ? dto.OnlineCustomerId
            : dto.CustomerId;

    private async Task<(OrderRequestIdempotency? Record, ActionResult<OrderDetailDto>? ShortCircuitResult)> TryClaimOrderIdempotencyAsync(
        string idempotencyKey,
        string requestHash,
        string? snapshotHash,
        CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.RequiredTenantId;

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var claimId = Guid.NewGuid();
            var claimToken = Guid.NewGuid().ToString("N");
            var nowUtc = DateTime.UtcNow;
            var rowsInserted = await TryInsertOrderIdempotencyClaimAsync(
                claimId,
                tenantId,
                idempotencyKey,
                requestHash,
                snapshotHash,
                claimToken,
                nowUtc,
                cancellationToken);

            if (rowsInserted > 0)
            {
                var claimedRecord = await _context.OrderRequestIdempotencyRecords
                    .FirstAsync(
                        record =>
                            record.TenantId == tenantId &&
                            record.Key == idempotencyKey,
                        cancellationToken);

                return (claimedRecord, null);
            }

            var existingRecord = await _context.OrderRequestIdempotencyRecords
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    record =>
                        record.TenantId == tenantId &&
                        record.Key == idempotencyKey,
                    cancellationToken);

            if (existingRecord?.IsDeleted == true)
            {
                await DeleteOrderIdempotencyRecordByIdAsync(existingRecord.Id, cancellationToken);
                continue;
            }

            if (existingRecord == null)
            {
                continue;
            }

            if (!string.Equals(existingRecord.RequestHash, requestHash, StringComparison.Ordinal))
            {
                return (null, Conflict(new
                {
                    message = "Idempotency-Key was already used for a different order payload."
                }));
            }

            // Snapshot hash comparison enforces strict matching:
            // - If one side has a snapshot and the other doesn't, treat as mismatch (security guard)
            // - If both sides have snapshots, they must match exactly
            // - Only skip check if both sides are null/empty (CreateOrder flow or legacy records)
            var existingHasSnapshot = !string.IsNullOrWhiteSpace(existingRecord.SnapshotHash);
            var incomingHasSnapshot = !string.IsNullOrWhiteSpace(snapshotHash);
            
            // Reject if only one side has a snapshot (prevents bypassing snapshot-based idempotency)
            if (existingHasSnapshot != incomingHasSnapshot)
            {
                return (null, Conflict(new
                {
                    message = "Idempotency-Key was already used for a different order snapshot."
                }));
            }
            
            // Reject if both have snapshots but they don't match
            if (existingHasSnapshot && incomingHasSnapshot &&
                !string.Equals(existingRecord.SnapshotHash, snapshotHash, StringComparison.Ordinal))
            {
                return (null, Conflict(new
                {
                    message = "Idempotency-Key was already used for a different order snapshot."
                }));
            }

            if (existingRecord.ResponseStatusCode.HasValue ||
                existingRecord.OrderId.HasValue ||
                existingRecord.CompletedAtUtc.HasValue)
            {
                return (null, await BuildStoredOrderResponseAsync(existingRecord, cancellationToken));
            }

            var claimAge = nowUtc - (existingRecord.ClaimTimestampUtc ?? existingRecord.CreatedAt);
            if (claimAge.TotalMinutes < StaleOrderIdempotencyClaimMinutes)
            {
                return (null, Conflict(new
                {
                    message = "An order request with the same idempotency key is already being processed."
                }));
            }

            var rowsDeleted = await DeleteStaleOrderIdempotencyClaimAsync(
                tenantId,
                idempotencyKey,
                nowUtc.AddMinutes(-StaleOrderIdempotencyClaimMinutes),
                cancellationToken);

            if (rowsDeleted > 0)
            {
                continue;
            }

            return (null, Conflict(new
            {
                message = "An order request with the same idempotency key is already being processed."
            }));
        }

        return (null, Conflict(new
        {
            message = "An order request with the same idempotency key is already being processed."
        }));
    }

    private async Task<int> TryInsertOrderIdempotencyClaimAsync(
        Guid claimId,
        Guid tenantId,
        string idempotencyKey,
        string requestHash,
        string? snapshotHash,
        string claimToken,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        if (IsPostgresProvider())
        {
            return await _context.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO ""OrderRequestIdempotency""
                (""Id"", ""TenantId"", ""Key"", ""RequestHash"", ""SnapshotHash"", ""ClaimToken"", ""ClaimTimestampUtc"", ""CreatedAt"", ""UpdatedAt"", ""IsDeleted"")
                VALUES
                ({claimId}, {tenantId}, {idempotencyKey}, {requestHash}, {snapshotHash}, {claimToken}, {nowUtc}, {nowUtc}, {nowUtc}, {false})
                ON CONFLICT (""TenantId"", ""Key"") DO NOTHING", cancellationToken);
        }

        if (IsSqlServerProvider())
        {
            return await _context.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO [OrderRequestIdempotency]
                    ([Id], [TenantId], [Key], [RequestHash], [SnapshotHash], [ClaimToken], [ClaimTimestampUtc], [CreatedAt], [UpdatedAt], [IsDeleted])
                SELECT
                    {claimId}, {tenantId}, {idempotencyKey}, {requestHash}, {snapshotHash}, {claimToken}, {nowUtc}, {nowUtc}, {nowUtc}, {false}
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM [OrderRequestIdempotency] WITH (UPDLOCK, HOLDLOCK)
                    WHERE [TenantId] = {tenantId}
                      AND [Key] = {idempotencyKey})", cancellationToken);
        }

        var existingTrackedClaim = _context.ChangeTracker
            .Entries<OrderRequestIdempotency>()
            .Any(entry =>
                entry.State != EntityState.Detached &&
                entry.State != EntityState.Deleted &&
                entry.Entity.TenantId == tenantId &&
                entry.Entity.Key == idempotencyKey);

        if (existingTrackedClaim)
        {
            return 0;
        }

        var existingPersistedClaim = await _context.OrderRequestIdempotencyRecords
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(
                record =>
                    record.TenantId == tenantId &&
                    record.Key == idempotencyKey,
                cancellationToken);

        if (existingPersistedClaim)
        {
            return 0;
        }

        var claim = new OrderRequestIdempotency
        {
            Id = claimId,
            TenantId = tenantId,
            Key = idempotencyKey,
            RequestHash = requestHash,
            SnapshotHash = snapshotHash,
            ClaimToken = claimToken,
            ClaimTimestampUtc = nowUtc,
            CreatedAt = nowUtc,
            UpdatedAt = nowUtc,
            IsDeleted = false
        };

        _context.OrderRequestIdempotencyRecords.Add(claim);

        try
        {
            return await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _context.Entry(claim).State = EntityState.Detached;
            return 0;
        }
    }

    private async Task<int> DeleteStaleOrderIdempotencyClaimAsync(
        Guid tenantId,
        string idempotencyKey,
        DateTime cutoffUtc,
        CancellationToken cancellationToken)
    {
        if (IsPostgresProvider())
        {
            return await _context.Database.ExecuteSqlInterpolatedAsync($@"
                DELETE FROM ""OrderRequestIdempotency""
                WHERE ""TenantId"" = {tenantId}
                  AND ""Key"" = {idempotencyKey}
                  AND ""OrderId"" IS NULL
                  AND ""ResponseStatusCode"" IS NULL
                  AND ""CompletedAtUtc"" IS NULL
                  AND COALESCE(""ClaimTimestampUtc"", ""CreatedAt"") < {cutoffUtc}", cancellationToken);
        }

        if (IsSqlServerProvider())
        {
            return await _context.Database.ExecuteSqlInterpolatedAsync($@"
                DELETE FROM [OrderRequestIdempotency]
                WHERE [TenantId] = {tenantId}
                  AND [Key] = {idempotencyKey}
                  AND [OrderId] IS NULL
                  AND [ResponseStatusCode] IS NULL
                  AND [CompletedAtUtc] IS NULL
                  AND COALESCE([ClaimTimestampUtc], [CreatedAt]) < {cutoffUtc}", cancellationToken);
        }

        var staleRecord = await _context.OrderRequestIdempotencyRecords
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                record =>
                    record.TenantId == tenantId &&
                    record.Key == idempotencyKey &&
                    record.OrderId == null &&
                    record.ResponseStatusCode == null &&
                    record.CompletedAtUtc == null &&
                    (record.ClaimTimestampUtc ?? record.CreatedAt) < cutoffUtc,
                cancellationToken);

        if (staleRecord == null)
        {
            return 0;
        }

        _context.OrderRequestIdempotencyRecords.Remove(staleRecord);
        return await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task CleanupOrderIdempotencyAsync(
        OrderRequestIdempotency? record,
        CancellationToken cancellationToken)
    {
        _context.ChangeTracker.Clear();

        if (record == null)
        {
            return;
        }

        var currentRecord = await _context.OrderRequestIdempotencyRecords
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(item => item.Id == record.Id, cancellationToken);

        if (currentRecord == null)
        {
            return;
        }

        if (!string.Equals(currentRecord.ClaimToken, record.ClaimToken, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "Skipping admin order idempotency cleanup for stolen claim | RecordId: {RecordId}",
                record.Id);
            return;
        }

        if (currentRecord.OrderId.HasValue ||
            currentRecord.ResponseStatusCode.HasValue ||
            currentRecord.CompletedAtUtc.HasValue)
        {
            _logger.LogWarning(
                "Skipping admin order idempotency cleanup for durable or ambiguous record | RecordId: {RecordId} | OrderId: {OrderId} | ResponseStatusCode: {ResponseStatusCode}",
                currentRecord.Id,
                currentRecord.OrderId,
                currentRecord.ResponseStatusCode);
            return;
        }

        _context.OrderRequestIdempotencyRecords.Remove(currentRecord);
        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task<int> DeleteOrderIdempotencyRecordByIdAsync(Guid recordId, CancellationToken cancellationToken)
    {
        if (IsPostgresProvider())
        {
            return await _context.Database.ExecuteSqlInterpolatedAsync($@"
                DELETE FROM ""OrderRequestIdempotency""
                WHERE ""Id"" = {recordId}", cancellationToken);
        }

        if (IsSqlServerProvider())
        {
            return await _context.Database.ExecuteSqlInterpolatedAsync($@"
                DELETE FROM [OrderRequestIdempotency]
                WHERE [Id] = {recordId}", cancellationToken);
        }

        var record = await _context.OrderRequestIdempotencyRecords
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(item => item.Id == recordId, cancellationToken);

        if (record == null)
        {
            return 0;
        }

        _context.OrderRequestIdempotencyRecords.Remove(record);
        return await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task<ActionResult<OrderDetailDto>> BuildStoredOrderResponseAsync(
        OrderRequestIdempotency record,
        CancellationToken cancellationToken)
    {
        // Defensive check: If OrderId is set but ResponseStatusCode is null,
        // treat as a completed/created response to prevent duplicate order creation
        if (record.OrderId.HasValue && !record.ResponseStatusCode.HasValue)
        {
            _logger.LogWarning(
                "Idempotency record has OrderId but no ResponseStatusCode - treating as 201 Created | Key: {Key} | OrderId: {OrderId}",
                record.Key, record.OrderId.Value);
            
            var order = await _context.Orders
                .AsNoTracking()
                .Include(o => o.Customer)
                .Include(o => o.OnlineCustomer)
                .Include(o => o.ShippingMethod)
                .Include(o => o.Coupon)
                .Include(o => o.Items)
                    .ThenInclude(i => i.Product)
                .Include(o => o.Warehouse)
                .FirstOrDefaultAsync(
                    o => o.Id == record.OrderId.Value && o.TenantId == record.TenantId,
                    cancellationToken);

            if (order != null)
            {
                var fullResponse = await MapToOrderDetailDtoAsync(order, cancellationToken);
                return CreatedAtAction(nameof(GetOrder), new { id = order.Id }, fullResponse);
            }
            
            // Order not found but OrderId was set - return minimal 201 response
            return CreatedAtAction(nameof(GetOrder), new { id = record.OrderId.Value }, new { id = record.OrderId.Value });
        }

        var statusCode = record.ResponseStatusCode ?? StatusCodes.Status200OK;

        // If we have an OrderId, fetch the full order details from the database
        // This avoids storing sensitive PII in the idempotency record
        if (statusCode == StatusCodes.Status201Created && record.OrderId.HasValue)
        {
            var order = await _context.Orders
                .AsNoTracking()
                .Include(o => o.Customer)
                .Include(o => o.OnlineCustomer)
                .Include(o => o.ShippingMethod)
                .Include(o => o.Coupon)
                .Include(o => o.Items)
                    .ThenInclude(i => i.Product)
                .Include(o => o.Warehouse)
                .FirstOrDefaultAsync(
                    o => o.Id == record.OrderId.Value && o.TenantId == record.TenantId,
                    cancellationToken);

            if (order != null)
            {
                var fullResponse = await MapToOrderDetailDtoAsync(order, cancellationToken);
                return CreatedAtAction(nameof(GetOrder), new { id = order.Id }, fullResponse);
            }
        }

        // Fallback: use stored minimal payload if order not found or for non-201 responses
        if (string.IsNullOrWhiteSpace(record.ResponsePayload))
        {
            return new StatusCodeResult(statusCode);
        }

        using var document = JsonDocument.Parse(record.ResponsePayload);
        var payload = document.RootElement.Clone();

        if (statusCode == StatusCodes.Status201Created && record.OrderId.HasValue)
        {
            return CreatedAtAction(nameof(GetOrder), new { id = record.OrderId.Value }, payload);
        }

        return new ObjectResult(payload)
        {
            StatusCode = statusCode
        };
    }

    private static string SerializeOrderPayload(object payload) =>
        JsonSerializer.Serialize(payload, OrderIdempotencyJsonOptions);

    private async Task<(AuthoritativeCreateOrderFinancials? Financials, BadRequestObjectResult? ErrorResult)>
        CalculateAuthoritativeCreateOrderFinancialsAsync(
            CreateOrderDto dto,
            CancellationToken cancellationToken)
    {
        if (dto.Items == null || dto.Items.Count == 0)
        {
            return (null, BadRequest(new { message = "Order must contain at least one item" }));
        }

        var productIds = dto.Items
            .Select(item => item.ProductId)
            .Distinct()
            .ToList();

        var products = await _context.Products
            .Where(product => productIds.Contains(product.Id))
            .ToDictionaryAsync(product => product.Id, cancellationToken);

        var productUnits = await _context.ProductUnits
            .Where(productUnit => productIds.Contains(productUnit.ProductId))
            .ToListAsync(cancellationToken);

        var defaultVariants = await _context.ProductVariants
            .Where(variant =>
                productIds.Contains(variant.ProductId) &&
                variant.IsDefault &&
                variant.IsActive &&
                !variant.IsDeleted)
            .ToDictionaryAsync(variant => variant.ProductId, cancellationToken);

        var authoritativeItems = new List<AuthoritativeCreateOrderItem>(dto.Items.Count);

        foreach (var item in dto.Items)
        {
            if (item.Quantity <= 0)
            {
                return (null, BadRequest(new
                {
                    message = $"Quantity must be greater than zero for product {item.ProductId}"
                }));
            }

            if (!products.TryGetValue(item.ProductId, out var product))
            {
                return (null, BadRequest(new
                {
                    message = $"Product {item.ProductId} not found"
                }));
            }

            ProductUnit? configuredUnit = null;
            Guid? resolvedUnitId = item.UnitId;
            var conversionFactor = 1m;

            if (item.UnitId.HasValue)
            {
                configuredUnit = productUnits.FirstOrDefault(
                    unit => unit.ProductId == item.ProductId && unit.UnitId == item.UnitId.Value);

                if (configuredUnit == null || configuredUnit.ConversionFactor <= 0m)
                {
                    return (null, BadRequest(new
                    {
                        message = $"Unit {item.UnitId} is not a valid current unit for product {item.ProductId}"
                    }));
                }

                conversionFactor = configuredUnit.ConversionFactor;
            }
            else
            {
                configuredUnit = productUnits.FirstOrDefault(
                    unit => unit.ProductId == item.ProductId && unit.IsDefault);

                if (configuredUnit == null || configuredUnit.ConversionFactor <= 0m)
                {
                    return (null, BadRequest(new
                    {
                        message = $"Product {item.ProductId} has no valid current default unit"
                    }));
                }

                resolvedUnitId = configuredUnit.UnitId;
                conversionFactor = configuredUnit.ConversionFactor;
            }

            defaultVariants.TryGetValue(item.ProductId, out var variant);

            var unitPrice = ResolveAuthoritativeUnitPrice(product, configuredUnit, variant);
            var lineSubtotal = RoundCurrency(unitPrice * item.Quantity);
            var lineDiscountAmount = 0m;
            var taxableAmount = RoundCurrency(lineSubtotal - lineDiscountAmount);
            var lineTaxAmount = CalculateAuthoritativeItemTax(product, taxableAmount);
            var lineTotal = RoundCurrency(taxableAmount + lineTaxAmount);

            authoritativeItems.Add(new AuthoritativeCreateOrderItem(
                item,
                product,
                variant,
                resolvedUnitId,
                conversionFactor,
                unitPrice,
                lineSubtotal,
                lineDiscountAmount,
                lineTaxAmount,
                lineTotal));
        }

        var subtotal = RoundCurrency(authoritativeItems.Sum(item => item.Subtotal));
        var validatedDiscountAmount = 0m;
        var taxAmount = RoundCurrency(authoritativeItems.Sum(item => item.TaxAmount));
        var correlationId = string.IsNullOrWhiteSpace(HttpContext.TraceIdentifier)
            ? Guid.NewGuid().ToString("N")
            : HttpContext.TraceIdentifier;

        ShippingMethod? shippingMethod = null;
        var shippingCost = 0m;
        if (dto.ShippingMethodId.HasValue)
        {
            shippingMethod = await _context.ShippingMethods
                .FirstOrDefaultAsync(
                    method => method.Id == dto.ShippingMethodId.Value && method.IsActive,
                    cancellationToken);

            if (shippingMethod == null)
            {
                return (null, BadRequest(new
                {
                    message = $"Shipping method {dto.ShippingMethodId} not found or inactive"
                }));
            }

            shippingCost = CalculateAuthoritativeShippingCost(shippingMethod, subtotal, authoritativeItems);
        }

        Coupon? coupon = null;
        var couponDiscount = 0m;
        if (!string.IsNullOrWhiteSpace(dto.CouponCode))
        {
            var normalizedCode = dto.CouponCode.Trim().ToUpperInvariant();
            coupon = await _context.Coupons
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    entity => entity.TenantId == _tenantContext.RequiredTenantId &&
                              entity.NormalizedCode == normalizedCode &&
                              !entity.IsDeleted,
                    cancellationToken);

            if (coupon == null)
            {
                return (null, BadRequest(new
                {
                    message = "Coupon is invalid or expired"
                }));
            }

            var couponValidation = await _couponValidationService.ValidateCouponByIdAsync(
                _tenantContext.RequiredTenantId,
                coupon.Id,
                coupon.Code,
                ResolveCreateOrderCouponCustomerId(dto),
                new CouponValidationSnapshot
                {
                    OrderSubtotal = subtotal,
                    CartItems = authoritativeItems
                        .Select(item =>
                        {
                            // SECURITY: Validate CategoryId to prevent category-scoped coupon bypass
                            if (item.Product.CategoryId == Guid.Empty)
                            {
                                _logger.LogError(
                                    "Product {ProductId} has Guid.Empty CategoryId during coupon validation. " +
                                    "This could allow bypassing category-scoped coupons. Order creation blocked.",
                                    item.Product.Id);
                                throw new InvalidOperationException(
                                    $"Product {item.Product.Id} has an invalid category assignment. " +
                                    "Please contact support.");
                            }
                            return new CouponScopeValidationItem(item.Product.Id, item.Product.CategoryId);
                        })
                        .ToList()
                },
                correlationId,
                cancellationToken: cancellationToken);

            if (!couponValidation.IsValid)
            {
                return (null, BadRequest(new
                {
                    errorCode = couponValidation.ErrorCode,
                    message = couponValidation.ErrorMessage ?? "Coupon is invalid or expired"
                }));
            }

            couponDiscount = RoundCurrency(couponValidation.CalculatedDiscountAmount ?? 0m);
        }

        var total = RoundCurrency(subtotal - couponDiscount - validatedDiscountAmount + taxAmount + shippingCost);
        var orderFinancialMismatches = new List<object>();

        if (RoundCurrency(dto.Subtotal) != subtotal)
        {
            orderFinancialMismatches.Add(new
            {
                field = nameof(dto.Subtotal),
                client = RoundCurrency(dto.Subtotal),
                server = subtotal
            });
        }

        var authoritativeDiscountAmount = validatedDiscountAmount + couponDiscount;
        if (RoundCurrency(dto.DiscountAmount) != authoritativeDiscountAmount)
        {
            orderFinancialMismatches.Add(new
            {
                field = nameof(dto.DiscountAmount),
                client = RoundCurrency(dto.DiscountAmount),
                server = authoritativeDiscountAmount
            });
        }

        if (RoundCurrency(dto.TaxAmount) != taxAmount)
        {
            orderFinancialMismatches.Add(new
            {
                field = nameof(dto.TaxAmount),
                client = RoundCurrency(dto.TaxAmount),
                server = taxAmount
            });
        }

        if (RoundCurrency(dto.ShippingCost) != shippingCost)
        {
            orderFinancialMismatches.Add(new
            {
                field = nameof(dto.ShippingCost),
                client = RoundCurrency(dto.ShippingCost),
                server = shippingCost
            });
        }

        var itemFinancialMismatches = authoritativeItems
            .Select((item, index) => new
            {
                Item = item,
                Index = index
            })
            .Where(entry =>
                RoundCurrency(entry.Item.Request.UnitPrice) != entry.Item.UnitPrice ||
                RoundCurrency(entry.Item.Request.DiscountAmount) != entry.Item.DiscountAmount ||
                RoundCurrency(entry.Item.Request.TaxAmount) != entry.Item.TaxAmount)
            .Select(entry => new
            {
                itemIndex = entry.Index,
                productId = entry.Item.Request.ProductId,
                client = new
                {
                    unitPrice = RoundCurrency(entry.Item.Request.UnitPrice),
                    discountAmount = RoundCurrency(entry.Item.Request.DiscountAmount),
                    taxAmount = RoundCurrency(entry.Item.Request.TaxAmount)
                },
                server = new
                {
                    unitPrice = entry.Item.UnitPrice,
                    discountAmount = entry.Item.DiscountAmount,
                    taxAmount = entry.Item.TaxAmount
                }
            })
            .ToList();

        if (orderFinancialMismatches.Count > 0 || itemFinancialMismatches.Count > 0)
        {
            return (null, BadRequest(new
            {
                message = "Client-supplied financials do not match server-calculated values.",
                orderFinancialMismatches,
                itemFinancialMismatches,
                serverTotals = new
                {
                    subtotal,
                    discountAmount = authoritativeDiscountAmount,
                    couponDiscount,
                    taxAmount,
                    shippingCost,
                    total
                }
            }));
        }

        return (new AuthoritativeCreateOrderFinancials(
            authoritativeItems,
            coupon,
            shippingMethod,
            subtotal,
            couponDiscount + validatedDiscountAmount,
            taxAmount,
            shippingCost,
            total), null);
    }

    private static decimal ResolveAuthoritativeUnitPrice(
        Product product,
        ProductUnit? configuredUnit,
        ProductVariant? defaultVariant)
    {
        if (product.HasVariants)
        {
            return defaultVariant is null
                ? 0m
                : RoundCurrency(CatalogPricingAuthority.ResolveVariantOnlinePrice(defaultVariant));
        }

        return RoundCurrency(CatalogPricingAuthority.ResolveSimpleSellingPrice(configuredUnit));
    }

    private static decimal CalculateAuthoritativeItemTax(Product product, decimal taxableAmount)
    {
        if (product.PriceIncludesTax || taxableAmount <= 0m)
        {
            return 0m;
        }

        return RoundCurrency(taxableAmount * product.TaxRate / 100m);
    }

    private decimal CalculateAuthoritativeShippingCost(
        ShippingMethod shippingMethod,
        decimal subtotal,
        IEnumerable<AuthoritativeCreateOrderItem> items)
    {
        var totalWeight = items.Sum(item =>
            item.Product.Weight *
            item.Request.Quantity *
            item.ConversionFactor);

        return _shippingCalculationService.Calculate(shippingMethod, subtotal, totalWeight);
    }

    private static decimal RoundCurrency(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static bool IsCouponConflict(InvalidOperationException exception)
    {
        return exception.Message.StartsWith("This coupon", StringComparison.OrdinalIgnoreCase) ||
               exception.Message.Contains("coupon", StringComparison.OrdinalIgnoreCase) ||
               exception.Message.Contains("Authenticated customer is required", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCurrencyValidationFailure(InvalidOperationException exception)
    {
        return exception.InnerException is FinancialCurrencyMissingException or FinancialCurrencyInvalidException or FinancialCurrencyMismatchException;
    }

    private sealed record AuthoritativeCreateOrderItem(
        CreateOrderItemDto Request,
        Product Product,
        ProductVariant? Variant,
        Guid? UnitId,
        decimal ConversionFactor,
        decimal UnitPrice,
        decimal Subtotal,
        decimal DiscountAmount,
        decimal TaxAmount,
        decimal Total);

    private sealed record AuthoritativeCreateOrderFinancials(
        IReadOnlyList<AuthoritativeCreateOrderItem> Items,
        Coupon? Coupon,
        ShippingMethod? ShippingMethod,
        decimal Subtotal,
        decimal DiscountAmount,
        decimal TaxAmount,
        decimal ShippingCost,
        decimal Total);

    private async Task<string> GenerateOrderNumber(CancellationToken cancellationToken)
    {
        var timestampUtc = DateTime.UtcNow;

        if (IsPostgresProvider())
        {
            var sequenceValue = await DocumentNumberingSupport.GetNextSequenceValueAsync(
                _context,
                DocumentNumberingSupport.OrderNumberSequenceName,
                cancellationToken);

            return DocumentNumberingSupport.FormatOperationsOrderNumber(timestampUtc, sequenceValue);
        }

        // Fallback for SQL Server and other providers: query-based numbering with tenant locking
        await LockTenantForOrderNumberAsync(_tenantContext.RequiredTenantId, cancellationToken);

        var prefix = $"ORD-{timestampUtc:yyyyMMdd}";
        
        // Query all orders with the prefix and extract the maximum valid suffix
        var orderNumbers = await _context.Orders
            .Where(order =>
                order.TenantId == _tenantContext.RequiredTenantId &&
                order.OrderNumber.StartsWith(prefix))
            .Select(order => order.OrderNumber)
            .ToListAsync(cancellationToken);

        // Find the maximum valid suffix from all matching order numbers
        var maxSuffix = 0;
        foreach (var orderNumber in orderNumbers)
        {
            var parts = orderNumber.Split('-');
            
            // Validate exact format: ORD-yyyyMMdd-####
            if (parts.Length != 3)
                continue;
            
            if (parts[0] != "ORD")
                continue;
            
            // Validate 8-digit date format
            if (!DateTime.TryParseExact(parts[1], "yyyyMMdd", null, 
                System.Globalization.DateTimeStyles.None, out _))
                continue;
            
            // Validate numeric suffix with minimum 4 digits
            if (!int.TryParse(parts[2], out var suffix) || parts[2].Length < 4)
                continue;
            
            maxSuffix = Math.Max(maxSuffix, suffix);
        }

        return GenerateOrderNumberFromMaxSuffix(prefix, maxSuffix);
    }

    private async Task LockTenantForOrderNumberAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        Tenant? tenant;

        if (IsPostgresProvider())
        {
            tenant = await _context.Tenants
                .FromSqlInterpolated($@"SELECT * FROM ""Tenants"" WHERE ""Id"" = {tenantId} FOR UPDATE")
                .FirstOrDefaultAsync(cancellationToken);
        }
        else if (IsSqlServerProvider())
        {
            tenant = await _context.Tenants
                .FromSqlInterpolated($@"SELECT * FROM [Tenants] WITH (UPDLOCK, ROWLOCK, HOLDLOCK) WHERE [Id] = {tenantId}")
                .FirstOrDefaultAsync(cancellationToken);
        }
        else
        {
            tenant = await _context.Tenants.FirstOrDefaultAsync(entity => entity.Id == tenantId, cancellationToken);
        }

        if (tenant == null)
        {
            throw new InvalidOperationException("Tenant not found");
        }
    }

    private static string GenerateOrderNumberFromLast(string? lastOrderNumber, DateTime timestampUtc)
    {
        var prefix = $"ORD-{timestampUtc:yyyyMMdd}";

        if (string.IsNullOrEmpty(lastOrderNumber) || !lastOrderNumber.StartsWith(prefix))
        {
            return $"{prefix}-0001";
        }

        var lastNumber = int.Parse(lastOrderNumber.Split('-').Last());
        var nextNumber = DocumentNumberingSupport.IncrementLegacyNumber(lastNumber);
        return $"{prefix}-{nextNumber:D4}";
    }

    private static string GenerateOrderNumberFromMaxSuffix(string prefix, int maxSuffix)
    {
        var nextNumber = DocumentNumberingSupport.IncrementLegacyNumber(maxSuffix);
        return $"{prefix}-{nextNumber:D4}";
    }

    private async Task<OrderDetailDto> MapToOrderDetailDtoAsync(Order order, CancellationToken cancellationToken)
    {
        var sessionId = order.SessionId;
        var customerPhone = order.Customer?.Phone ?? order.OnlineCustomer?.Phone ?? order.GuestPhone;
        var normalizedCustomerPhone = !string.IsNullOrWhiteSpace(customerPhone)
            ? await _phoneRiskProfileService.NormalizePhoneAsync(
                _tenantContext.RequiredTenantId,
                customerPhone,
                cancellationToken)
            : null;

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            var attribution = await _orderAttributionStore.GetAttributionAsync(
                _tenantContext.RequiredTenantId,
                order.Id,
                AttributionModel.LastTouch,
                cancellationToken);
            sessionId = attribution?.SessionId;
        }

        var phoneRiskProfile = await _phoneRiskProfileService.GetStatusAsync(
            _tenantContext.RequiredTenantId,
            customerPhone,
            cancellationToken);

        var globalSpamReports = await _phoneRiskProfileService.GetGlobalSpamReportsAsync(
            _tenantContext.RequiredTenantId,
            customerPhone,
            cancellationToken);

        var previousOrderStats = await GetPreviousOrderStatsByPhoneAsync(
            _tenantContext.RequiredTenantId,
            order.Id,
            customerPhone,
            cancellationToken);

        var hasReportedSpam = await _phoneRiskProfileService.HasReportedSpamAsync(
            _tenantContext.RequiredTenantId,
            order.Id,
            cancellationToken);

        var velocityAssessment = await _orderVelocityService.GetCurrentAssessmentAsync(
            order.IpAddress,
            normalizedCustomerPhone,
            cancellationToken);

        var velocityFlags = new CustomerVelocityFlagsDto
        {
            IpVelocityTriggered = velocityAssessment.IpVelocityTriggered,
            PhoneVelocityTriggered = velocityAssessment.PhoneVelocityTriggered
        };

        var customerRisk = CalculateCustomerRisk(
            phoneRiskProfile,
            previousOrderStats,
            globalSpamReports,
            velocityFlags);

        var shipment = await _context.Shipments
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.OrderId == order.Id, cancellationToken);

        return new OrderDetailDto
        {
            Id = order.Id,
            OrderNumber = order.OrderNumber,
            CustomerId = order.CustomerId,
            OnlineCustomerId = order.OnlineCustomerId,
            CustomerName = order.Customer?.Name ?? order.OnlineCustomer?.DisplayName ?? order.GuestName,
            CustomerEmail = order.Customer?.Email ?? order.OnlineCustomer?.Email ?? order.GuestEmail,
            CustomerPhone = customerPhone,
            SessionId = sessionId,
            Status = order.Status,
            PaymentStatus = order.PaymentStatus,
            ShippingAddress = order.ShippingAddress,
            ShippingCity = order.ShippingCity,
            ShippingState = order.ShippingState,
            ShippingZipCode = order.ShippingZipCode,
            ShippingCountry = order.ShippingCountry,
            BillingAddress = order.BillingAddress,
            ShippingMethodName = order.ShippingMethod?.Name,
            ShippingCost = order.ShippingCost,
            TrackingNumber = order.TrackingNumber,
            Carrier = order.Carrier,
            Subtotal = order.Subtotal,
            DiscountAmount = order.DiscountAmount,
            TaxAmount = order.TaxAmount,
            Total = order.Total,
            Currency = order.Currency,
            CouponCode = order.CouponCode,
            PaymentMethod = order.PaymentMethod,
            PaymentMethodCode = order.PaymentMethodCode,
            PaymentMethodName = order.PaymentMethodName,
            PaymentFee = order.PaymentFee,
            IsPaid = order.IsPaid,
            PaymentReference = order.PaymentReference,
            PaymentGateway = order.PaymentGateway,
            PaidAt = order.PaidAt,
            CustomerNotes = order.CustomerNotes,
            AdminNotes = order.AdminNotes,
            Source = order.Source,
            SourceOrderId = order.SourceOrderId,
            WarehouseName = order.Warehouse?.Name,
            FulfillmentStatus = (int)order.FulfillmentStatus,
            Shipment = shipment == null
                ? null
                : new OrderShipmentDto
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
                    CancellationReason = shipment.CancellationReason
                },
            CreatedAt = order.CreatedAt,
            ShippedAt = order.ShippedAt,
            DeliveredAt = order.DeliveredAt,
            CancelledAt = order.CancelledAt,
            CancellationReason = order.CancellationReason,
            Items = order.Items.Select(i => new OrderItemDto
            {
                Id = i.Id,
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
                .Select(snapshot => new OrderCheckoutFieldSnapshotDto
                {
                    FieldId = snapshot.CheckoutFieldDefinitionId,
                    FieldKind = ToCheckoutFieldApiValue(snapshot.FieldKind),
                    FieldType = ToCheckoutFieldApiValue(snapshot.FieldType),
                    SystemFieldKey = snapshot.SystemFieldKey,
                    Label = snapshot.Label,
                    HelpText = snapshot.HelpText,
                    IsRequired = snapshot.IsRequired,
                    DisplayOrder = snapshot.DisplayOrder,
                    ConfigurationSchemaVersion = snapshot.ConfigurationSchemaVersion,
                    ConfigurationJson = snapshot.ConfigurationJson,
                    ValueJson = snapshot.ValueJson
                })
                .ToList(),
            // AF-35: Affiliate visibility
            IsAffiliateOrder = order.AffiliateAccountId.HasValue,
            AffiliateId = order.AffiliateAccountId,
            PreviousOrdersCount = previousOrderStats.PreviousOrdersCount,
            CanceledOrdersCount = previousOrderStats.CanceledOrdersCount,
            GlobalSpamReports = globalSpamReports,
            HasReportedSpam = hasReportedSpam,
            CustomerVelocityFlags = velocityFlags,
            CustomerRiskLevel = customerRisk.Level,
            CustomerRiskReason = customerRisk.Reason,
            PhoneRiskProfile = phoneRiskProfile,
            IsMarketplaceSubOrder = false,
            SupportsCustomerRiskActions = true,
            SupportsAdminOverride = true,
            SupportsPaymentActions = true,
            SupportsFulfillmentActions = true
        };
    }

    private async Task<OrderDetailDto> MapToMarketplaceOrderDetailDtoAsync(
        CommerceHubSubOrder subOrder,
        CancellationToken cancellationToken)
    {
        var marketplaceOrder = subOrder.MarketplaceOrder;
        var paymentMethodCode = string.IsNullOrWhiteSpace(subOrder.PaymentMethodCode)
            ? marketplaceOrder.PaymentMethodCode
            : subOrder.PaymentMethodCode;
        var paymentMethodName = string.IsNullOrWhiteSpace(subOrder.PaymentMethodName)
            ? marketplaceOrder.PaymentMethodName
            : subOrder.PaymentMethodName;
        var refundRequests = await _context.CommerceHubRefundRequests
            .AsNoTracking()
            .Where(refundRequest =>
                refundRequest.OrderId == marketplaceOrder.Id &&
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
        var refundLookup = refundRequests
            .GroupBy(refundRequest => refundRequest.OrderItemId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<CommerceHubRefundRequest>)group.ToList());

        return new OrderDetailDto
        {
            Id = subOrder.Id,
            OrderNumber = subOrder.OrderNumber,
            CustomerId = null,
            OnlineCustomerId = null,
            CustomerName = marketplaceOrder.FullName,
            CustomerEmail = null,
            CustomerPhone = marketplaceOrder.PhoneNumber,
            SessionId = null,
            Status = subOrder.Status,
            PaymentStatus = subOrder.PaymentStatus,
            ShippingAddress = marketplaceOrder.AddressLine,
            ShippingCity = marketplaceOrder.City,
            ShippingState = marketplaceOrder.Area,
            ShippingZipCode = null,
            ShippingCountry = null,
            BillingAddress = null,
            ShippingMethodName = subOrder.ShippingMethodName,
            ShippingCost = subOrder.Shipping,
            TrackingNumber = null,
            Carrier = null,
            Subtotal = subOrder.Subtotal,
            DiscountAmount = 0m,
            TaxAmount = 0m,
            Total = subOrder.Total,
            Currency = subOrder.Currency,
            CouponCode = null,
            PaymentMethod = MapMarketplacePaymentMethod(paymentMethodCode),
            PaymentMethodCode = paymentMethodCode,
            PaymentMethodName = paymentMethodName,
            PaymentFee = 0m,
            IsPaid = subOrder.PaymentStatus is PaymentStatus.Collected or PaymentStatus.Settled or PaymentStatus.Refunded or PaymentStatus.PartiallyRefunded,
            PaymentReference = subOrder.PaymentReferenceId ?? marketplaceOrder.PaymentReference,
            PaymentGateway = marketplaceOrder.PaymentGateway,
            PaidAt = subOrder.PaidAt,
            CustomerNotes = marketplaceOrder.Notes,
            AdminNotes = null,
            Source = OrderSource.Marketplace,
            SourceOrderId = null,
            WarehouseName = null,
            FulfillmentStatus = null,
            Shipment = null,
            CreatedAt = subOrder.CreatedAt,
            ShippedAt = null,
            DeliveredAt = null,
            CancelledAt = subOrder.Status == OrderStatus.Cancelled ? subOrder.UpdatedAt : null,
            CancellationReason = null,
            ShippingSnapshotJson = subOrder.ShippingSnapshotJson,
            PaymentSnapshotJson = subOrder.PaymentSnapshotJson,
            Items = subOrder.Items.Select(item => new OrderItemDto
            {
                Id = item.Id,
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
                    ? matchedRefunds.Select(refundRequest => new OrderRefundEvidenceDto
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
                        MediaUrls = evidenceLookup.TryGetValue(refundRequest.Id, out var media)
                            ? media.Select(item => item.Url).ToList()
                            : new List<string>(),
                        Media = evidenceLookup.TryGetValue(refundRequest.Id, out var refundMedia)
                            ? refundMedia.ToList()
                            : new List<RefundEvidenceMediaDto>(),
                        CreatedAt = refundRequest.CreatedAt,
                        UpdatedAt = refundRequest.UpdatedAt
                    }).ToList()
                    : new List<OrderRefundEvidenceDto>()
            }).ToList(),
            IsAffiliateOrder = false,
            AffiliateId = null,
            PreviousOrdersCount = 0,
            CanceledOrdersCount = 0,
            GlobalSpamReports = 0,
            HasReportedSpam = false,
            CustomerVelocityFlags = new CustomerVelocityFlagsDto(),
            CustomerRiskLevel = "SAFE",
            CustomerRiskReason = null,
            PhoneRiskProfile = null,
            IsMarketplaceSubOrder = true,
            SupportsCustomerRiskActions = false,
            SupportsAdminOverride = false,
            SupportsPaymentActions = false,
            SupportsFulfillmentActions = false
        };
    }

    private static PaymentMethod MapMarketplacePaymentMethod(string paymentMethodCode)
    {
        return string.Equals(paymentMethodCode, "COD", StringComparison.OrdinalIgnoreCase)
            ? PaymentMethod.Cash
            : PaymentMethod.Card;
    }

    private static string ToCheckoutFieldApiValue(CheckoutFieldKind kind) => kind switch
    {
        CheckoutFieldKind.System => "SYSTEM",
        CheckoutFieldKind.Custom => "CUSTOM",
        _ => kind.ToString().ToUpperInvariant()
    };

    private static string ToCheckoutFieldApiValue(CheckoutFieldType type) => type switch
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

    private async Task<PreviousOrderStatsDto> GetPreviousOrderStatsByPhoneAsync(
        Guid tenantId,
        Guid currentOrderId,
        string? phoneNumber,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            return new PreviousOrderStatsDto();
        }

        return await _context.Orders
            .AsNoTracking()
            .Where(o => o.TenantId == tenantId && o.Id != currentOrderId)
            .Where(o => o.GuestPhone == phoneNumber 
                || (o.Customer != null && o.Customer.Phone == phoneNumber)
                || (o.OnlineCustomer != null && o.OnlineCustomer.Phone == phoneNumber))
            .GroupBy(_ => 1)
            .Select(g => new PreviousOrderStatsDto
            {
                PreviousOrdersCount = g.Count(),
                CanceledOrdersCount = g.Count(o => o.Status == OrderStatus.Cancelled)
            })
            .FirstOrDefaultAsync(cancellationToken)
            ?? new PreviousOrderStatsDto();
    }

    private static CustomerRiskAssessmentDto CalculateCustomerRisk(
        AdminPhoneRiskStatusDto? phoneRiskProfile,
        PreviousOrderStatsDto previousOrderStats,
        int globalSpamReports,
        CustomerVelocityFlagsDto velocityFlags)
    {
        var score = 0;
        var reasons = new List<string>();

        if (globalSpamReports >= 5)
        {
            reasons.Add("Severe spam reputation across stores");

            return new CustomerRiskAssessmentDto
            {
                Level = "CRITICAL_RISK",
                Reason = string.Join("; ", reasons)
            };
        }

        if (globalSpamReports >= 3)
        {
            score += 10;
            reasons.Add("Reported as spam by other stores");
        }

        if ((phoneRiskProfile?.SpamReports ?? 0) >= 1)
        {
            score += 5;
            reasons.Add("Reported as spam in this store");
        }

        var cancellationRate = previousOrderStats.PreviousOrdersCount > 0
            ? previousOrderStats.CanceledOrdersCount / (double)previousOrderStats.PreviousOrdersCount
            : 0;

        if (previousOrderStats.PreviousOrdersCount >= 3 && cancellationRate > 0.6)
        {
            score += 7;
            reasons.Add("High cancellation rate");
        }

        if (previousOrderStats.PreviousOrdersCount == 0)
        {
            score += 2;
            reasons.Add("First time customer");
        }

        if (velocityFlags.IpVelocityTriggered)
        {
            score += 5;
            reasons.Add("Multiple orders from the same IP in the last 5 minutes");
        }

        if (velocityFlags.PhoneVelocityTriggered)
        {
            score += 5;
            reasons.Add("Multiple orders from the same phone in the last 5 minutes");
        }

        var level = score switch
        {
            < 5 => "SAFE",
            <= 12 => "SUSPICIOUS",
            _ => "HIGH_RISK"
        };

        return new CustomerRiskAssessmentDto
        {
            Level = level,
            Reason = reasons.Count > 0 ? string.Join("; ", reasons) : "No elevated risk signals"
        };
    }
}

#region DTOs

public class PagedResult<T>
{
    public List<T> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
}

public class OrderListDto
{
    public Guid Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public Guid? CustomerId { get; set; }
    public Guid? OnlineCustomerId { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerEmail { get; set; }
    public OrderStatus Status { get; set; }
    public PaymentStatus PaymentStatus { get; set; }
    public OrderSource Source { get; set; }
    public string? SourceOrderId { get; set; }
    public decimal Total { get; set; }
    public string Currency { get; set; } = "USD";
    public int ItemCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ShippedAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    // AF-35: Affiliate order visibility
    public bool IsAffiliateOrder { get; set; }
    public Guid? AffiliateId { get; set; }
    public bool IsMarketplaceSubOrder { get; set; }
}

public class OrderDetailDto
{
    public Guid Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public Guid? CustomerId { get; set; }
    public Guid? OnlineCustomerId { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerEmail { get; set; }
    public string? CustomerPhone { get; set; }
    public string? SessionId { get; set; }
    public OrderStatus Status { get; set; }
    public PaymentStatus PaymentStatus { get; set; }
    public string ShippingAddress { get; set; } = string.Empty;
    public string? ShippingCity { get; set; }
    public string? ShippingState { get; set; }
    public string? ShippingZipCode { get; set; }
    public string? ShippingCountry { get; set; }
    public string? BillingAddress { get; set; }
    public string? ShippingMethodName { get; set; }
    public decimal ShippingCost { get; set; }
    public string? TrackingNumber { get; set; }
    public string? Carrier { get; set; }
    public decimal Subtotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal Total { get; set; }
    public string Currency { get; set; } = "USD";
    public string? CouponCode { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public string? PaymentMethodCode { get; set; }
    public string? PaymentMethodName { get; set; }
    public decimal PaymentFee { get; set; }
    public bool IsPaid { get; set; }
    public string? PaymentReference { get; set; }
    public string? PaymentGateway { get; set; }
    public DateTime? PaidAt { get; set; }
    public string? CustomerNotes { get; set; }
    public string? AdminNotes { get; set; }
    public OrderSource Source { get; set; }
    public string? SourceOrderId { get; set; }
    public string? WarehouseName { get; set; }
    public int? FulfillmentStatus { get; set; }
    public OrderShipmentDto? Shipment { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ShippedAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancellationReason { get; set; }
    public string? ShippingSnapshotJson { get; set; }
    public string? PaymentSnapshotJson { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
    public List<OrderCheckoutFieldSnapshotDto> CheckoutFieldSnapshots { get; set; } = new();
    // AF-35: Affiliate order details
    public bool IsAffiliateOrder { get; set; }
    public Guid? AffiliateId { get; set; }
    public int PreviousOrdersCount { get; set; }
    public int CanceledOrdersCount { get; set; }
    public int GlobalSpamReports { get; set; }
    public bool HasReportedSpam { get; set; }
    public CustomerVelocityFlagsDto CustomerVelocityFlags { get; set; } = new();
    public string CustomerRiskLevel { get; set; } = "SAFE";
    public string? CustomerRiskReason { get; set; }
    public AdminPhoneRiskStatusDto? PhoneRiskProfile { get; set; }
    public bool IsMarketplaceSubOrder { get; set; }
    public bool SupportsCustomerRiskActions { get; set; } = true;
    public bool SupportsAdminOverride { get; set; } = true;
    public bool SupportsPaymentActions { get; set; } = true;
    public bool SupportsFulfillmentActions { get; set; } = true;
}

public class OrderCheckoutFieldSnapshotDto
{
    public Guid FieldId { get; set; }
    public string FieldKind { get; set; } = string.Empty;
    public string FieldType { get; set; } = string.Empty;
    public string? SystemFieldKey { get; set; }
    public string Label { get; set; } = string.Empty;
    public string? HelpText { get; set; }
    public bool IsRequired { get; set; }
    public int DisplayOrder { get; set; }
    public int ConfigurationSchemaVersion { get; set; }
    public string ConfigurationJson { get; set; } = "{}";
    public string ValueJson { get; set; } = "null";
}

/// <summary>
/// Minimal replay envelope for idempotency - excludes sensitive PII.
/// Used to replay order creation responses without storing customer email, phone, addresses, or risk data.
/// </summary>
public class OrderIdempotencyReplayDto
{
    public Guid Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public Guid? CustomerId { get; set; }
    public Guid? OnlineCustomerId { get; set; }
    public OrderStatus Status { get; set; }
    public PaymentStatus PaymentStatus { get; set; }
    public decimal Subtotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal Total { get; set; }
    public string Currency { get; set; } = "USD";
    public string? CouponCode { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public string? PaymentMethodCode { get; set; }
    public string? PaymentMethodName { get; set; }
    public decimal PaymentFee { get; set; }
    public bool IsPaid { get; set; }
    public OrderSource Source { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsAffiliateOrder { get; set; }
    public Guid? AffiliateId { get; set; }
    public bool IsMarketplaceSubOrder { get; set; }
    
    /// <summary>
    /// Creates a minimal replay envelope from a full OrderDetailDto, excluding sensitive PII.
    /// </summary>
    public static OrderIdempotencyReplayDto FromOrderDetail(OrderDetailDto order)
    {
        return new OrderIdempotencyReplayDto
        {
            Id = order.Id,
            OrderNumber = order.OrderNumber,
            CustomerId = order.CustomerId,
            OnlineCustomerId = order.OnlineCustomerId,
            Status = order.Status,
            PaymentStatus = order.PaymentStatus,
            Subtotal = order.Subtotal,
            DiscountAmount = order.DiscountAmount,
            TaxAmount = order.TaxAmount,
            Total = order.Total,
            Currency = order.Currency,
            CouponCode = order.CouponCode,
            PaymentMethod = order.PaymentMethod,
            PaymentMethodCode = order.PaymentMethodCode,
            PaymentMethodName = order.PaymentMethodName,
            PaymentFee = order.PaymentFee,
            IsPaid = order.IsPaid,
            Source = order.Source,
            CreatedAt = order.CreatedAt,
            IsAffiliateOrder = order.IsAffiliateOrder,
            AffiliateId = order.AffiliateId,
            IsMarketplaceSubOrder = order.IsMarketplaceSubOrder
        };
    }
}

public class CustomerVelocityFlagsDto
{
    public bool IpVelocityTriggered { get; set; }
    public bool PhoneVelocityTriggered { get; set; }
}

public class PreviousOrderStatsDto
{
    public int PreviousOrdersCount { get; set; }
    public int CanceledOrdersCount { get; set; }
}

public class CustomerRiskAssessmentDto
{
    public string Level { get; set; } = "SAFE";
    public string? Reason { get; set; }
}

public class StatusCountRow
{
    public OrderStatus Status { get; set; }
    public int Count { get; set; }
}

public class OrderItemDto
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public string SKU { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string? VariantName { get; set; }
    public string? ImageUrl { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal Total { get; set; }
    public List<OrderRefundEvidenceDto> Refunds { get; set; } = new();
}

public class OrderRefundEvidenceDto
{
    public Guid RefundId { get; set; }
    public int Quantity { get; set; }
    public decimal Amount { get; set; }
    public CommerceHubRefundRequestStatus Status { get; set; }
    public string Reason { get; set; } = string.Empty;
    public RefundReasonCode ReasonCode { get; set; }
    public string? ReasonText { get; set; }
    public CommerceHubReturnDetailsDto? ReturnRequest { get; set; }
    public List<string> MediaUrls { get; set; } = new();
    public List<RefundEvidenceMediaDto> Media { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class OrderShipmentDto
{
    public Guid ShipmentId { get; set; }
    public string ShipmentNumber { get; set; } = string.Empty;
    public ShipmentStatus Status { get; set; }
    public string ShippingMethodName { get; set; } = string.Empty;
    public string? CarrierName { get; set; }
    public string? TrackingNumber { get; set; }
    public string? TrackingUrl { get; set; }
    public DateTime? EstimatedDeliveryDate { get; set; }
    public DateTime? ActualDeliveryDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ShippedAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancellationReason { get; set; }
}

public class CreateOrderDto
{
    public Guid? CustomerId { get; set; }
    public Guid? OnlineCustomerId { get; set; }
    public string? GuestEmail { get; set; }
    public string? GuestPhone { get; set; }
    public string? GuestName { get; set; }
    public string ShippingAddress { get; set; } = string.Empty;
    public string? ShippingCity { get; set; }
    public string? ShippingState { get; set; }
    public string? ShippingZipCode { get; set; }
    public string? ShippingCountry { get; set; }
    public string? BillingAddress { get; set; }
    public string? BillingCity { get; set; }
    public string? BillingState { get; set; }
    public string? BillingZipCode { get; set; }
    public string? BillingCountry { get; set; }
    public Guid? ShippingMethodId { get; set; }
    public decimal ShippingCost { get; set; }
    public decimal Subtotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public string? Currency { get; set; }
    public string? CouponCode { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public string? CustomerNotes { get; set; }
    public OrderSource Source { get; set; } = OrderSource.Manual;
    public string? SourceOrderId { get; set; }
    public Guid? WarehouseId { get; set; }
    public List<CreateOrderItemDto> Items { get; set; } = new();
}

public class CreateOrderItemDto
{
    public Guid ProductId { get; set; }
    public Guid? UnitId { get; set; }
    public Guid? WarehouseId { get; set; }
    public string? VariantName { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public string? Notes { get; set; }
}

public class UpdateOrderStatusDto
{
    public OrderStatus Status { get; set; }
    public string? TrackingNumber { get; set; }
    public string? Carrier { get; set; }
    public string? Reason { get; set; }
    public string? AdminNotes { get; set; }
}

public class UpdatePaymentStatusDto
{
    public PaymentStatus PaymentStatus { get; set; }
    public string? PaymentReference { get; set; }
    public string? PaymentGateway { get; set; }
}

public class OrderStatisticsDto
{
    public int TotalOrders { get; set; }
    public decimal TotalRevenue { get; set; }
    public decimal AverageOrderValue { get; set; }
    public int PendingOrders { get; set; }
    public int ProcessingOrders { get; set; }
    public int ShippedOrders { get; set; }
    public int DeliveredOrders { get; set; }
    public int CancelledOrders { get; set; }
}

#endregion
