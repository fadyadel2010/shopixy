using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using PointOfSale.API.Attributes;
using PointOfSale.API.Development.CommerceHub;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.API.SuperAdmin.Models;
using PointOfSale.API.SuperAdmin.Services;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MongoDB.Services;
using PointOfSale.Infrastructure.Reliability;

namespace PointOfSale.API.Controllers;

[DevelopmentOnlyController]
[AllowAnonymousTenant]
[ApiController]
[Route("api/dev/commercehub")]
[ApiExplorerSettings(IgnoreApi = true)]
[Authorize(Policy = SuperAdminSecurityConstants.PolicyName)]
public class DevelopmentCommerceHubController : ControllerBase
{
    private readonly IWebHostEnvironment _environment;
    private readonly IDevelopmentCommerceHubSeedService _seedService;
    private readonly ICommerceHubPaymentService _paymentService;
    private readonly IRefundExecutionService _refundExecutionService;
    private readonly ApplicationDbContext _dbContext;
    private readonly IInventoryConsistencyService _inventoryConsistencyService;
    private readonly ISuperAdminAuditService _auditService;
    private readonly ICommerceHubOrderReadRepository _orderReadRepository;

    public DevelopmentCommerceHubController(
        IWebHostEnvironment environment,
        IDevelopmentCommerceHubSeedService seedService,
        ICommerceHubPaymentService paymentService,
        IRefundExecutionService refundExecutionService,
        ApplicationDbContext dbContext,
        IInventoryConsistencyService inventoryConsistencyService,
        ISuperAdminAuditService auditService,
        ICommerceHubOrderReadRepository orderReadRepository)
    {
        _environment = environment;
        _seedService = seedService;
        _paymentService = paymentService;
        _refundExecutionService = refundExecutionService;
        _dbContext = dbContext;
        _inventoryConsistencyService = inventoryConsistencyService;
        _auditService = auditService;
        _orderReadRepository = orderReadRepository;
    }

    [HttpPost("seed")]
    public async Task<ActionResult<CommerceHubSeedResult>> Seed(CancellationToken cancellationToken)
    {
        if (!_environment.IsDevelopment())
        {
            return NotFound();
        }

        var result = await _seedService.SeedAsync(cancellationToken);
        return Ok(result);
    }

    [HttpDelete("seed")]
    public async Task<ActionResult<CommerceHubSeedResetResult>> Reset(CancellationToken cancellationToken)
    {
        if (!_environment.IsDevelopment())
        {
            return NotFound();
        }

        var result = await _seedService.ResetAsync(cancellationToken);
        return Ok(result);
    }

    [HttpPost("m1d/refunds/{refundId:guid}/execute-cod-debt-reduction")]
    public async Task<IActionResult> ExecuteM1dCodDebtReduction(
        Guid refundId,
        [FromBody] DevelopmentM1dCodRefundExecutionRequest? request,
        CancellationToken cancellationToken)
    {
        if (!_environment.IsDevelopment())
        {
            return NotFound();
        }

        var actorIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var actorId = Guid.TryParse(actorIdValue, out var parsedActorId) ? parsedActorId : (Guid?)null;
        var actorEmail = User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name;

        var result = await _refundExecutionService.ExecuteDevelopmentCodDebtReductionAsync(
            refundId,
            request?.IdempotencyKey ?? string.Empty,
            actorId,
            actorEmail,
            HttpContext.TraceIdentifier,
            cancellationToken);

        return Ok(result);
    }

    [HttpPost("m1d/refunds/{refundId:guid}/execute-online-before-payout")]
    public async Task<IActionResult> ExecuteM1dOnlineBeforePayout(
        Guid refundId,
        [FromBody] DevelopmentOnlineRefundExecutionRequest request,
        CancellationToken cancellationToken)
    {
        if (!_environment.IsDevelopment())
        {
            return NotFound();
        }

        if (!request.AdministratorConfirmation)
        {
            return BadRequest(new
            {
                code = "DEVELOPMENT_REFUND_SIMULATION_CONFIRMATION_REQUIRED",
                message = "Explicit administrator confirmation is required."
            });
        }

        var actorIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var actorId = Guid.TryParse(actorIdValue, out var parsedActorId) ? parsedActorId : (Guid?)null;
        var actorEmail = User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name;
        var result = await _refundExecutionService.ExecuteDevelopmentOnlineBeforePayoutAsync(
            refundId,
            request.IdempotencyKey,
            request.Reason,
            actorId,
            actorEmail,
            HttpContext.TraceIdentifier,
            cancellationToken);

        return Ok(result);
    }

    [HttpPost("payments/{paymentId:guid}/simulate-success")]
    public async Task<IActionResult> SimulateOnlinePaymentSuccess(
        Guid paymentId,
        [FromBody] DevelopmentOnlinePaymentSuccessRequest request,
        CancellationToken cancellationToken)
    {
        if (!_environment.IsDevelopment())
        {
            return NotFound();
        }

        if (!request.AdministratorConfirmation)
        {
            return BadRequest(new
            {
                code = "DEVELOPMENT_PAYMENT_SIMULATION_CONFIRMATION_REQUIRED",
                message = "Explicit administrator confirmation is required."
            });
        }

        var actorIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var actorId = Guid.TryParse(actorIdValue, out var parsedActorId) ? parsedActorId : (Guid?)null;
        var actorEmail = User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name;
        var result = await _paymentService.ExecuteDevelopmentSuccessAsync(
            paymentId,
            request.IdempotencyKey,
            actorId,
            actorEmail,
            HttpContext.TraceIdentifier,
            cancellationToken);

        await _auditService.LogActionAsync(
            action: "DEVELOPMENT_ONLINE_PAYMENT_SUCCESS_SIMULATED",
            entityType: "CommerceHubPayment",
            entityId: paymentId.ToString(),
            description: "Development operator entered the canonical online payment-success lifecycle without external money movement.",
            success: result.Processed,
            details: new
            {
                paymentId,
                request.IdempotencyKey,
                request.Reason,
                IsDevelopmentSimulation = true,
                ExternalMoneyMovementConfirmed = false,
                result.Provider,
                result.ProviderReference,
                result.TransactionId,
                result.Message
            },
            reference: result.TransactionId,
            actorId: actorId,
            actorEmail: actorEmail,
            cancellationToken: cancellationToken);

        return Ok(new
        {
            result.Processed,
            result.Provider,
            result.ProviderReference,
            result.TransactionId,
            result.Message,
            EvidenceType = "DevelopmentOnlinePaymentSuccessSimulation",
            IsDevelopmentSimulation = true,
            ExternalMoneyMovementConfirmed = false
        });
    }

    [HttpGet("operations/orders/{marketplaceOrderId:guid}/suborders/{subOrderId:guid}/cod-reservation")]
    public async Task<IActionResult> InspectCodReservation(
        Guid marketplaceOrderId,
        Guid subOrderId,
        CancellationToken cancellationToken)
    {
        if (!_environment.IsDevelopment())
        {
            return NotFound();
        }

        var snapshot = await LoadCodReservationSnapshotAsync(marketplaceOrderId, subOrderId, cancellationToken);
        return snapshot == null ? NotFound() : Ok(snapshot);
    }

    [HttpPost("operations/orders/{marketplaceOrderId:guid}/suborders/{subOrderId:guid}/repair-cod-reservation")]
    public async Task<IActionResult> RepairCodReservation(
        Guid marketplaceOrderId,
        Guid subOrderId,
        [FromBody] DevelopmentCodReservationRepairRequest request,
        CancellationToken cancellationToken)
    {
        if (!_environment.IsDevelopment())
        {
            return NotFound();
        }

        var actorIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var actorId = Guid.TryParse(actorIdValue, out var parsedActorId) ? parsedActorId : (Guid?)null;
        var actorEmail = User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name;
        var correlationId = HttpContext.TraceIdentifier;

        try
        {
            var subOrder = await _dbContext.CommerceHubSubOrders
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Include(current => current.Items.Where(item => !item.IsDeleted))
                .FirstOrDefaultAsync(current =>
                    current.Id == subOrderId &&
                    current.MarketplaceOrderId == marketplaceOrderId &&
                    !current.IsDeleted,
                    cancellationToken);

            if (subOrder == null)
            {
                return NotFound();
            }

            if (request.TenantId == Guid.Empty || subOrder.StoreId != request.TenantId)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { code = "SUBORDER_FORBIDDEN", message = "The requested tenant does not own this sub-order." });
            }

            if (subOrder.ProjectionMutationVersion != request.ExpectedVersion)
            {
                return Conflict(new { code = "SUBORDER_VERSION_CONFLICT", message = "The sub-order changed before reservation repair." });
            }

            if (subOrder.PaymentMethod != CommerceHubPaymentMethod.CashOnDelivery ||
                subOrder.PaymentStatus != PaymentStatus.CashOnDelivery)
            {
                return Conflict(new { code = "COD_RESERVATION_REPAIR_NOT_APPLICABLE", message = "Only authoritative cash-on-delivery sub-orders can be repaired." });
            }

            if (subOrder.Status is OrderStatus.Delivered or OrderStatus.Completed or OrderStatus.Cancelled or OrderStatus.Refunded ||
                subOrder.Items.Count == 0 ||
                subOrder.Items.Any(item => item.Status is CommerceHubOrderItemStatus.Delivered or CommerceHubOrderItemStatus.Cancelled || item.Quantity <= 0))
            {
                return Conflict(new { code = "COD_RESERVATION_REPAIR_STATE_INVALID", message = "The sub-order is not in a reservable pre-delivery state." });
            }

            foreach (var item in subOrder.Items.OrderBy(item => item.Id))
            {
                var reservations = await _dbContext.InventoryReservations
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(reservation => reservation.OrderItemId == item.Id && !reservation.IsDeleted)
                    .ToListAsync(cancellationToken);
                var nowUtc = DateTime.UtcNow;
                var rawOpen = reservations.Where(reservation =>
                    reservation.Status == InventoryReservationStatus.Active &&
                    reservation.RemainingReservedQuantity > 0m &&
                    (reservation.ExpiresAtUtc == null || reservation.ExpiresAtUtc > nowUtc)).ToList();

                if (rawOpen.Any(reservation =>
                        reservation.TenantId != subOrder.StoreId ||
                        reservation.OrderId != marketplaceOrderId ||
                        reservation.SubOrderId != subOrderId ||
                        reservation.ProductId != item.ProductId ||
                        reservation.VariantId != item.VariantId))
                {
                    return Conflict(new { code = "COD_RESERVATION_REPAIR_LINEAGE_CONFLICT", message = "An open reservation has foreign or stale order-item lineage." });
                }

                var current = await ProductVariantInventoryLineageAuthority.FilterCurrentConsumableReservationsAsync(
                    _dbContext,
                    subOrder.StoreId,
                    rawOpen,
                    nowUtc,
                    cancellationToken);
                var currentQuantity = current.Sum(reservation => reservation.RemainingReservedQuantity);

                if (currentQuantity > item.Quantity)
                {
                    return Conflict(new { code = "COD_RESERVATION_REPAIR_OVER_RESERVED", message = "Current reservations exceed exact SQL item authority." });
                }

                var missingQuantity = item.Quantity - currentQuantity;
                if (missingQuantity <= 0m)
                {
                    continue;
                }

                var repaired = await _inventoryConsistencyService.ReserveOrderItemAsync(
                    new InventoryReservationRequest(
                        TenantId: subOrder.StoreId,
                        OrderId: marketplaceOrderId,
                        SubOrderId: subOrderId,
                        OrderItemId: item.Id,
                        ProductId: item.ProductId,
                        VariantId: item.VariantId,
                        Quantity: missingQuantity,
                        WarehouseId: null,
                        ExpiresAtUtc: null,
                        EventId: InventoryEventIdFactory.Create(
                            "development-cod-reservation-repair-v1",
                            marketplaceOrderId,
                            subOrderId,
                            item.Id,
                            item.Quantity),
                        CorrelationId: correlationId,
                        Source: "DevelopmentCommerceHubCodReservationRepair"),
                    cancellationToken);

                if (!repaired)
                {
                    return Conflict(new { code = "COD_RESERVATION_REPAIR_STOCK_UNAVAILABLE", message = "Canonical inventory authority cannot reserve the exact missing quantity." });
                }
            }

            var after = await LoadCodReservationSnapshotAsync(marketplaceOrderId, subOrderId, cancellationToken);
            await _auditService.LogActionAsync(
                "DEVELOPMENT_COD_RESERVATION_REPAIRED",
                "CommerceHubSubOrder",
                subOrderId.ToString(),
                "Development operator reconciled COD reservation through canonical inventory authority.",
                success: true,
                details: new { marketplaceOrderId, subOrderId, request.TenantId, request.ExpectedVersion, request.Reason },
                reference: correlationId,
                actorId: actorId,
                actorEmail: actorEmail,
                cancellationToken: cancellationToken);

            return Ok(after);
        }
        catch
        {
            await _auditService.LogActionAsync(
                "DEVELOPMENT_COD_RESERVATION_REPAIR_FAILED",
                "CommerceHubSubOrder",
                subOrderId.ToString(),
                "Development COD reservation reconciliation failed.",
                success: false,
                details: new { marketplaceOrderId, subOrderId, request.TenantId, request.ExpectedVersion, request.Reason },
                reference: correlationId,
                actorId: actorId,
                actorEmail: actorEmail,
                cancellationToken: CancellationToken.None);
            throw;
        }
    }

    private async Task<object?> LoadCodReservationSnapshotAsync(
        Guid marketplaceOrderId,
        Guid subOrderId,
        CancellationToken cancellationToken)
    {
        var order = await _dbContext.CommerceHubMarketplaceOrders
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(current => current.Id == marketplaceOrderId && !current.IsDeleted, cancellationToken);
        var subOrder = await _dbContext.CommerceHubSubOrders
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(current => current.Items.Where(item => !item.IsDeleted))
            .FirstOrDefaultAsync(current =>
                current.Id == subOrderId &&
                current.MarketplaceOrderId == marketplaceOrderId &&
                !current.IsDeleted,
                cancellationToken);

        if (order == null || subOrder == null)
        {
            return null;
        }

        var itemIds = subOrder.Items.Select(item => item.Id).ToList();
        var variantIds = subOrder.Items.Select(item => item.VariantId).Distinct().ToList();
        var reservations = await _dbContext.InventoryReservations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(reservation => itemIds.Contains(reservation.OrderItemId))
            .OrderBy(reservation => reservation.CreatedAt)
            .ToListAsync(cancellationToken);
        var stocks = await _dbContext.ProductVariantStocks
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(stock => stock.TenantId == subOrder.StoreId && variantIds.Contains(stock.VariantId))
            .OrderBy(stock => stock.WarehouseId)
            .ToListAsync(cancellationToken);
        var currentStocks = await ProductVariantInventoryLineageAuthority.FilterCurrentInventoryStockOwnersAsync(
            _dbContext,
            subOrder.StoreId,
            stocks,
            cancellationToken);
        var currentStockIds = currentStocks.Select(stock => stock.Id).ToHashSet();
        var nowUtc = DateTime.UtcNow;
        var rawActiveReservations = reservations.Where(reservation =>
            !reservation.IsDeleted &&
            reservation.Status == InventoryReservationStatus.Active &&
            reservation.RemainingReservedQuantity > 0m &&
            (reservation.ExpiresAtUtc == null || reservation.ExpiresAtUtc > nowUtc)).ToList();
        var currentReservations = await ProductVariantInventoryLineageAuthority.FilterCurrentConsumableReservationsAsync(
            _dbContext,
            subOrder.StoreId,
            rawActiveReservations,
            nowUtc,
            cancellationToken);
        var currentReservationIds = currentReservations.Select(reservation => reservation.Id).ToHashSet();
        var warehouses = await _dbContext.Warehouses
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(warehouse => stocks.Select(stock => stock.WarehouseId).Contains(warehouse.Id))
            .ToDictionaryAsync(warehouse => warehouse.Id, cancellationToken);
        var variants = await _dbContext.ProductVariants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(variant => variantIds.Contains(variant.Id))
            .ToListAsync(cancellationToken);
        var store = await _dbContext.Tenants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(tenant => tenant.Id == subOrder.StoreId)
            .Select(tenant => new { tenant.Id, tenant.Name, tenant.IsActive })
            .FirstOrDefaultAsync(cancellationToken);
        var history = await _dbContext.CommerceHubSubOrderStatusHistory
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(entry => entry.SubOrderId == subOrderId && !entry.IsDeleted)
            .OrderBy(entry => entry.ChangedAtUtc)
            .Select(entry => new { entry.Id, entry.PreviousStatus, entry.NewStatus, entry.Reason, entry.CorrelationId, entry.ChangedAtUtc })
            .ToListAsync(cancellationToken);
        var movements = await _dbContext.StockMovements
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(movement => movement.ReferenceLineId.HasValue && itemIds.Contains(movement.ReferenceLineId.Value))
            .OrderBy(movement => movement.CreatedAt)
            .Select(movement => new
            {
                movement.Id,
                movement.TenantId,
                movement.WarehouseId,
                movement.ProductId,
                movement.ProductVariantId,
                movement.MovementType,
                movement.QuantityChange,
                movement.ReservedQuantityChange,
                movement.QuantityAfter,
                movement.ReservedAfter,
                movement.AvailableAfter,
                movement.ReferenceId,
                movement.ReferenceLineId,
                movement.ReservationId,
                movement.InventoryEventId,
                movement.InventoryEventType,
                movement.CorrelationId,
                movement.Source,
                movement.CreatedAt
            })
            .ToListAsync(cancellationToken);
        var inventoryInbox = await _dbContext.InventoryEventInbox
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(entry => entry.ReferenceLineId.HasValue && itemIds.Contains(entry.ReferenceLineId.Value))
            .OrderBy(entry => entry.FirstReceivedAtUtc)
            .Select(entry => new
            {
                entry.Id,
                entry.TenantId,
                entry.EventId,
                entry.EventType,
                entry.ReferenceId,
                entry.ReferenceLineId,
                entry.FirstReceivedAtUtc,
                entry.LastReceivedAtUtc,
                entry.AppliedAtUtc,
                entry.Status,
                entry.ReplayCount,
                entry.StockMovementId,
                entry.CorrelationId,
                entry.Source
            })
            .ToListAsync(cancellationToken);
        var earnings = await _dbContext.CommerceHubVendorEarnings
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(earning => earning.SubOrderId == subOrderId && !earning.IsDeleted)
            .OrderBy(earning => earning.CreatedAt)
            .Select(earning => new
            {
                earning.Id,
                earning.TenantId,
                earning.SubOrderId,
                earning.PaymentId,
                earning.RefundId,
                earning.OriginalEarningId,
                earning.Amount,
                earning.PaidOutAmount,
                earning.PayoutReservedAmount,
                earning.Status,
                earning.AvailableAtUtc,
                earning.CreatedAt
            })
            .ToListAsync(cancellationToken);
        var financialTransactions = await _dbContext.CommerceHubFinancialTransactions
            .AsNoTracking()
            .Where(transaction => transaction.ReferenceId == subOrderId || transaction.ReferenceId == marketplaceOrderId)
            .OrderBy(transaction => transaction.CreatedAtUtc)
            .Select(transaction => new
            {
                transaction.Id,
                transaction.EntityType,
                transaction.EntityId,
                transaction.TransactionType,
                transaction.Direction,
                transaction.FinancialBucket,
                transaction.Amount,
                transaction.Currency,
                transaction.ReferenceType,
                transaction.ReferenceId,
                transaction.IdempotencyKey,
                transaction.CorrelationId,
                transaction.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);
        var financialTransactionIds = financialTransactions.Select(transaction => transaction.Id).ToList();
        var accountingOutbox = await _dbContext.AccountingOutboxMessages
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(message =>
                financialTransactionIds.Contains(message.BusinessEntityId) ||
                message.BusinessEntityId == subOrderId ||
                message.BusinessEntityId == marketplaceOrderId ||
                message.PayloadJson.Contains(subOrderId.ToString()) ||
                message.IdempotencyKey.Contains(subOrderId.ToString("N")))
            .OrderBy(message => message.OccurredAtUtc)
            .Select(message => new
            {
                message.Id,
                message.TenantId,
                message.EventType,
                message.SourceType,
                message.BusinessEntityId,
                message.IdempotencyKey,
                message.AggregateVersion,
                message.OccurredAtUtc,
                message.ProcessedAtUtc,
                message.JournalId,
                message.AttemptCount,
                message.IsFailed,
                message.CorrelationId
            })
            .ToListAsync(cancellationToken);
        var projection = await _orderReadRepository.GetByMarketplaceOrderIdAsync(marketplaceOrderId, cancellationToken);

        return new
        {
            inspectedAtUtc = nowUtc,
            parent = new { order.Id, order.OrderNumber, order.CustomerId, order.Status, order.PaymentStatus, order.PaymentMethodCode, order.Currency, order.TotalAmount, order.ProjectionMutationVersion },
            subOrder = new { subOrder.Id, subOrder.OrderNumber, subOrder.MarketplaceOrderId, subOrder.StoreId, subOrder.StoreName, subOrder.Status, subOrder.PaymentStatus, subOrder.PaymentMethod, subOrder.PaymentMethodCode, subOrder.ShippingMethodId, subOrder.ShippingMethodName, subOrder.ShippingSnapshotJson, subOrder.Total, subOrder.ProjectionMutationVersion },
            store,
            items = subOrder.Items.OrderBy(item => item.Id).Select(item => new
            {
                item.Id,
                item.SubOrderId,
                item.ListingId,
                item.ProductId,
                item.VariantId,
                item.ProductName,
                item.VariantName,
                item.Quantity,
                item.Status,
                item.DeliveredAt,
                currentActiveReservedQuantity = currentReservations.Where(reservation => reservation.OrderItemId == item.Id).Sum(reservation => reservation.RemainingReservedQuantity),
                exactReservationReady = currentReservations.Where(reservation => reservation.OrderItemId == item.Id).Sum(reservation => reservation.RemainingReservedQuantity) == item.Quantity
            }),
            variants = variants.Select(variant => new { variant.Id, variant.TenantId, variant.ProductId, variant.SKU, variant.IsActive, variant.SemanticLifecycleState, variant.SemanticReplacementVariantId, variant.SemanticSupersededAtUtc }),
            reservations = reservations.Select(reservation => new
            {
                reservation.Id,
                reservation.TenantId,
                reservation.OrderId,
                reservation.SubOrderId,
                reservation.OrderItemId,
                reservation.ProductId,
                reservation.VariantId,
                reservation.WarehouseId,
                reservation.ReservedQuantity,
                reservation.CommittedQuantity,
                reservation.ReleasedQuantity,
                reservation.Status,
                reservation.ExpiresAtUtc,
                reservation.ReservationEventId,
                reservation.LastMutationEventId,
                reservation.CreatedAt,
                reservation.UpdatedAt,
                isCurrentConsumable = currentReservationIds.Contains(reservation.Id)
            }),
            inventory = stocks.Select(stock => new
            {
                stock.Id,
                stock.TenantId,
                stock.VariantId,
                stock.WarehouseId,
                warehouseName = warehouses.GetValueOrDefault(stock.WarehouseId)?.Name,
                warehouseTenantId = warehouses.GetValueOrDefault(stock.WarehouseId)?.TenantId,
                warehouseActive = warehouses.GetValueOrDefault(stock.WarehouseId)?.IsActive,
                stock.Quantity,
                stock.ReservedQuantity,
                availableQuantity = stock.AvailableQuantity,
                stock.DamagedQuantity,
                stock.ProjectionMutationVersion,
                isCurrentOwner = currentStockIds.Contains(stock.Id)
            }),
            shipment = new { kind = "CommerceHubSubOrderShippingSnapshot", subOrder.ShippingMethodId, subOrder.ShippingMethodName, subOrder.ShippingSnapshotJson },
            history,
            movements,
            inventoryInbox,
            earnings,
            financialTransactions,
            accountingOutbox,
            projection = projection == null
                ? null
                : new
                {
                    projection.MarketplaceOrderId,
                    projection.OrderNumber,
                    projection.Status,
                    projection.PaymentStatus,
                    projection.LastEventId,
                    projection.LastEventOccurredAtUtc,
                    projection.LastEventVersion,
                    projection.LastSourceMutationTicks,
                    projection.ProjectionChecksum,
                    subOrders = projection.SubOrders.Select(current => new
                    {
                        current.SubOrderId,
                        current.OrderNumber,
                        current.StoreId,
                        current.Status,
                        current.PaymentStatus
                    })
                }
        };
    }
}

public sealed class DevelopmentM1dCodRefundExecutionRequest
{
    public string IdempotencyKey { get; init; } = string.Empty;
}

public sealed class DevelopmentOnlineRefundExecutionRequest
{
    public string IdempotencyKey { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public bool AdministratorConfirmation { get; init; }
}

public sealed class DevelopmentOnlinePaymentSuccessRequest
{
    public string IdempotencyKey { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public bool AdministratorConfirmation { get; init; }
}

public sealed class DevelopmentCodReservationRepairRequest
{
    public Guid TenantId { get; init; }
    public long ExpectedVersion { get; init; }
    public string Reason { get; init; } = string.Empty;
}
