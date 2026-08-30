using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Fulfillment;
using PointOfSale.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;

namespace PointOfSale.API.Controllers;

/// <summary>
/// PHASE OFL: Order Fulfillment Lifecycle Controller
/// 
/// This controller is the ONLY way to change order fulfillment status.
/// All endpoints delegate to IOrderFulfillmentService.
/// 
/// ABSOLUTE RULES:
/// - NO direct FulfillmentStatus writes in this controller
/// - NO direct DbContext access
/// - All transitions go through IOrderFulfillmentService
/// - All transitions emit domain events
/// </summary>
[ApiController]
[Route("api/orders/{orderId}/fulfillment")]
[Authorize(Roles = "Owner,Admin,Manager")]
public class OrderFulfillmentController : ControllerBase
{
    private readonly IOrderFulfillmentService _fulfillmentService;
    private readonly ITenantContext _tenantContext;
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<OrderFulfillmentController> _logger;

    public OrderFulfillmentController(
        IOrderFulfillmentService fulfillmentService,
        ITenantContext tenantContext,
        ApplicationDbContext dbContext,
        ILogger<OrderFulfillmentController> logger)
    {
        _fulfillmentService = fulfillmentService;
        _tenantContext = tenantContext;
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// Gets available fulfillment actions for an order.
    /// Used by UI to render dynamic action buttons.
    /// </summary>
    [HttpGet("actions")]
    public async Task<IActionResult> GetAvailableActions(
        Guid orderId,
        CancellationToken ct)
    {
        var traceId = Activity.Current?.TraceId.ToString() ?? HttpContext.TraceIdentifier;
        if (!await IsCurrentTenantOrderAsync(orderId, ct))
        {
            _logger.LogWarning(
                "[Fulfillment] GetActions rejected for non-owned order | TenantId: {TenantId} | OrderId: {OrderId} | TraceId: {TraceId}",
                _tenantContext.RequiredTenantId,
                orderId,
                traceId);
            return NotFound(new { error = "ORDER_NOT_FOUND", message = "Order not found", traceId });
        }
        
        _logger.LogInformation(
            "[Fulfillment] GetActions | OrderId: {OrderId} | TraceId: {TraceId}",
            orderId, traceId);

        var result = await _fulfillmentService.GetAvailableActionsAsync(orderId, ct);

        // Build boolean flags for UI convenience
        var actionCodes = result.AvailableActions.Select(a => a.Code).ToHashSet();

        return Ok(new
        {
            orderId = result.OrderId,
            orderNumber = result.OrderNumber,
            currentStatus = result.CurrentStatus.ToString(),
            currentStatusValue = (int)result.CurrentStatus,
            
            // Boolean flags for UI buttons
            canConfirm = actionCodes.Contains("confirm"),
            canPack = actionCodes.Contains("pack"),
            canOutForDelivery = actionCodes.Contains("out-for-delivery"),
            canDeliver = actionCodes.Contains("delivered"),
            canComplete = actionCodes.Contains("complete"),
            canMarkFailed = actionCodes.Contains("failed-delivery"),
            canRetryDelivery = actionCodes.Contains("retry-delivery"),
            canReturn = actionCodes.Contains("returned"),
            canCancel = actionCodes.Contains("cancel"),
            
            // Full action details for advanced UI
            availableActions = result.AvailableActions.Select(a => new
            {
                code = a.Code,
                label = a.Label,
                targetStatus = a.TargetStatus.ToString(),
                requiresConfirmation = a.RequiresConfirmation,
                requiredFields = a.RequiredFields,
                iconHint = a.IconHint
            }).ToList()
        });
    }

    /// <summary>
    /// Confirms an order for fulfillment.
    /// Transition: Created → Confirmed
    /// </summary>
    [HttpPost("confirm")]
    public async Task<IActionResult> Confirm(
        Guid orderId,
        [FromBody] FulfillmentActionRequest? request,
        CancellationToken ct)
    {
        var traceId = Activity.Current?.TraceId.ToString() ?? HttpContext.TraceIdentifier;
        if (!await IsCurrentTenantOrderAsync(orderId, ct))
        {
            return NotFound(new { error = "ORDER_NOT_FOUND", message = "Order not found", traceId });
        }

        var userName = User.Identity?.Name ?? "system";

        _logger.LogInformation(
            "[Fulfillment] CONFIRM requested | OrderId: {OrderId} | User: {User} | TraceId: {TraceId}",
            orderId, userName, traceId);

        var result = await _fulfillmentService.ConfirmAsync(
            orderId,
            FulfillmentRequestSource.Manual,
            userName,
            ct: ct);
        return HandleResult(result);
    }

    /// <summary>
    /// Marks an order as packed.
    /// Transition: Confirmed → Packed
    /// </summary>
    [HttpPost("pack")]
    public async Task<IActionResult> Pack(
        Guid orderId,
        [FromBody] PackActionRequest? request,
        CancellationToken ct)
    {
        var traceId = Activity.Current?.TraceId.ToString() ?? HttpContext.TraceIdentifier;
        if (!await IsCurrentTenantOrderAsync(orderId, ct))
        {
            return NotFound(new { error = "ORDER_NOT_FOUND", message = "Order not found", traceId });
        }

        var userName = User.Identity?.Name ?? "system";

        _logger.LogInformation(
            "[Fulfillment] PACK requested | OrderId: {OrderId} | User: {User} | TraceId: {TraceId}",
            orderId, userName, traceId);

        var result = await _fulfillmentService.PackAsync(
            orderId,
            FulfillmentRequestSource.Manual,
            request?.PackingSlipNumber,
            userName,
            ct: ct);
        return HandleResult(result);
    }

    /// <summary>
    /// Marks an order as out for delivery.
    /// Transition: Packed → OutForDelivery
    /// </summary>
    [HttpPost("out-for-delivery")]
    public async Task<IActionResult> OutForDelivery(
        Guid orderId,
        [FromBody] OutForDeliveryActionRequest? request,
        CancellationToken ct)
    {
        var traceId = Activity.Current?.TraceId.ToString() ?? HttpContext.TraceIdentifier;
        if (!await IsCurrentTenantOrderAsync(orderId, ct))
        {
            return NotFound(new { error = "ORDER_NOT_FOUND", message = "Order not found", traceId });
        }

        var userName = User.Identity?.Name ?? "system";

        _logger.LogInformation(
            "[Fulfillment] OUT_FOR_DELIVERY requested | OrderId: {OrderId} | User: {User} | TraceId: {TraceId}",
            orderId, userName, traceId);

        var result = await _fulfillmentService.MarkOutForDeliveryAsync(
            orderId,
            FulfillmentRequestSource.Manual,
            request?.ShipmentId,
            request?.TrackingNumber,
            userName,
            ct: ct);
        return HandleResult(result);
    }

    /// <summary>
    /// Marks an order as delivered.
    /// Transition: OutForDelivery → Delivered
    /// 
    /// PAYMENT BOUNDARY:
    /// - Delivery records fulfillment evidence only.
    /// - COD collection requires the payment lifecycle command or carrier collection evidence.
    /// - Online payment authority remains unchanged.
    /// </summary>
    [HttpPost("delivered")]
    public async Task<IActionResult> Delivered(
        Guid orderId,
        [FromBody] DeliveredActionRequest? request,
        CancellationToken ct)
    {
        var traceId = Activity.Current?.TraceId.ToString() ?? HttpContext.TraceIdentifier;
        if (!await IsCurrentTenantOrderAsync(orderId, ct))
        {
            return NotFound(new { error = "ORDER_NOT_FOUND", message = "Order not found", traceId });
        }

        var userName = User.Identity?.Name ?? "system";

        _logger.LogInformation(
            "[Fulfillment] DELIVERED requested | OrderId: {OrderId} | User: {User} | TraceId: {TraceId}",
            orderId, userName, traceId);

        var result = await _fulfillmentService.MarkDeliveredAsync(
            orderId,
            FulfillmentRequestSource.Manual,
            request?.DeliveryReference,
            request?.ProofOfDeliveryUrl,
            request?.RecipientName,
            userName,
            ct: ct);
        return HandleResult(result);
    }

    /// <summary>
    /// Marks an order as completed.
    /// Transition: Delivered → Completed
    /// </summary>
    [HttpPost("complete")]
    public async Task<IActionResult> Complete(
        Guid orderId,
        [FromBody] FulfillmentActionRequest? request,
        CancellationToken ct)
    {
        var traceId = Activity.Current?.TraceId.ToString() ?? HttpContext.TraceIdentifier;
        if (!await IsCurrentTenantOrderAsync(orderId, ct))
        {
            return NotFound(new { error = "ORDER_NOT_FOUND", message = "Order not found", traceId });
        }

        var userName = User.Identity?.Name ?? "system";

        _logger.LogInformation(
            "[Fulfillment] COMPLETE requested | OrderId: {OrderId} | User: {User} | TraceId: {TraceId}",
            orderId, userName, traceId);

        var result = await _fulfillmentService.MarkCompletedAsync(
            orderId,
            FulfillmentRequestSource.Manual,
            userName,
            ct: ct);
        return HandleResult(result);
    }

    /// <summary>
    /// Marks an order as returned.
    /// Transition: Delivered → Returned
    /// 
    /// PAYMENT INTEGRATION:
    /// - If shouldRefund is true, may trigger refund via PaymentLifecycle
    /// </summary>
    [HttpPost("returned")]
    public async Task<IActionResult> Returned(
        Guid orderId,
        [FromBody] ReturnedActionRequest request,
        CancellationToken ct)
    {
        var traceId = Activity.Current?.TraceId.ToString() ?? HttpContext.TraceIdentifier;
        if (!await IsCurrentTenantOrderAsync(orderId, ct))
        {
            return NotFound(new { error = "ORDER_NOT_FOUND", message = "Order not found", traceId });
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return BadRequest(new { error = "Reason is required for returns" });
        }

        if (request.ShouldRefund || request.RefundAmount.HasValue)
        {
            return Conflict(new
            {
                error = "REFUND_LIFECYCLE_REQUIRED",
                message = "Fulfillment return cannot create refund authority. Record the physical return first, then use the payment lifecycle refund endpoint."
            });
        }

        var userName = User.Identity?.Name ?? "system";

        _logger.LogInformation(
            "[Fulfillment] RETURNED requested | OrderId: {OrderId} | Reason: {Reason} | " +
            "ShouldRefund: {ShouldRefund} | User: {User} | TraceId: {TraceId}",
            orderId, request.Reason, request.ShouldRefund, userName, traceId);

        var result = await _fulfillmentService.MarkReturnedAsync(
            orderId,
            FulfillmentRequestSource.Manual,
            request.Reason,
            request.ShouldRefund,
            request.RefundAmount,
            request.ReturnAuthorizationNumber,
            userName,
            ct: ct);
        return HandleResult(result);
    }

    /// <summary>
    /// Marks a delivery as failed.
    /// Transition: OutForDelivery → FailedDelivery
    /// </summary>
    [HttpPost("failed-delivery")]
    public async Task<IActionResult> FailedDelivery(
        Guid orderId,
        [FromBody] FailedDeliveryActionRequest request,
        CancellationToken ct)
    {
        var traceId = Activity.Current?.TraceId.ToString() ?? HttpContext.TraceIdentifier;
        if (!await IsCurrentTenantOrderAsync(orderId, ct))
        {
            return NotFound(new { error = "ORDER_NOT_FOUND", message = "Order not found", traceId });
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return BadRequest(new { error = "Reason is required for failed delivery" });
        }

        var userName = User.Identity?.Name ?? "system";

        _logger.LogWarning(
            "[Fulfillment] FAILED_DELIVERY requested | OrderId: {OrderId} | Reason: {Reason} | " +
            "Attempt: {Attempt} | User: {User} | TraceId: {TraceId}",
            orderId, request.Reason, request.AttemptNumber, userName, traceId);

        var result = await _fulfillmentService.MarkDeliveryFailedAsync(
            orderId,
            FulfillmentRequestSource.Manual,
            request.Reason,
            request.AttemptNumber,
            request.CanRetry,
            userName,
            ct: ct);
        return HandleResult(result);
    }

    /// <summary>
    /// Retries delivery after a failed attempt.
    /// Transition: FailedDelivery → OutForDelivery
    /// </summary>
    [HttpPost("retry-delivery")]
    public async Task<IActionResult> RetryDelivery(
        Guid orderId,
        [FromBody] FulfillmentActionRequest? request,
        CancellationToken ct)
    {
        var traceId = Activity.Current?.TraceId.ToString() ?? HttpContext.TraceIdentifier;
        if (!await IsCurrentTenantOrderAsync(orderId, ct))
        {
            return NotFound(new { error = "ORDER_NOT_FOUND", message = "Order not found", traceId });
        }

        var userName = User.Identity?.Name ?? "system";

        _logger.LogInformation(
            "[Fulfillment] RETRY_DELIVERY requested | OrderId: {OrderId} | User: {User} | TraceId: {TraceId}",
            orderId, userName, traceId);

        var result = await _fulfillmentService.RetryDeliveryAsync(
            orderId,
            FulfillmentRequestSource.Manual,
            userName,
            ct: ct);
        return HandleResult(result);
    }

    /// <summary>
    /// Cancels an order.
    /// Transition: Created/Confirmed/Packed/FailedDelivery → Cancelled
    /// </summary>
    [HttpPost("cancel")]
    public async Task<IActionResult> Cancel(
        Guid orderId,
        [FromBody] CancelActionRequest request,
        CancellationToken ct)
    {
        var traceId = Activity.Current?.TraceId.ToString() ?? HttpContext.TraceIdentifier;
        if (!await IsCurrentTenantOrderAsync(orderId, ct))
        {
            return NotFound(new { error = "ORDER_NOT_FOUND", message = "Order not found", traceId });
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return BadRequest(new { error = "Reason is required for cancellation" });
        }

        return StatusCode(StatusCodes.Status410Gone, new
        {
            error = "ORDER_CANCELLATION_LIFECYCLE_REQUIRED",
            message = "Fulfillment cancellation cannot coordinate payment and inventory authority. Use POST /api/admin/orders/{orderId}/cancel.",
            orderId
        });
    }

    private IActionResult HandleResult(OrderFulfillmentResult result)
    {
        if (result.Success)
        {
            return Ok(new
            {
                success = true,
                orderId = result.OrderId,
                orderNumber = result.OrderNumber,
                oldStatus = result.OldStatus.ToString(),
                newStatus = result.NewStatus.ToString(),
                transitionedAt = result.TransitionedAt
            });
        }

        var (statusCode, code) = MapFulfillmentFailure(result.ErrorMessage);
        var traceId = Activity.Current?.TraceId.ToString() ?? HttpContext.TraceIdentifier;

        return StatusCode(statusCode, new
        {
            success = false,
            orderId = result.OrderId,
            orderNumber = result.OrderNumber,
            currentStatus = result.OldStatus.ToString(),
            code,
            message = result.ErrorMessage ?? "The fulfillment transition was rejected.",
            traceId
        });
    }

    private static (int StatusCode, string Code) MapFulfillmentFailure(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return (StatusCodes.Status422UnprocessableEntity, "FULFILLMENT_TRANSITION_REJECTED");
        }

        if (message.Contains("Order not found", StringComparison.OrdinalIgnoreCase))
        {
            return (StatusCodes.Status404NotFound, "ORDER_NOT_FOUND");
        }

        if (message.StartsWith("MANUAL_DELIVERY_EVIDENCE_REQUIRED", StringComparison.Ordinal))
        {
            return (StatusCodes.Status422UnprocessableEntity, "MANUAL_DELIVERY_EVIDENCE_REQUIRED");
        }

        if (message.StartsWith("MANUAL_DELIVERY_PROOF_URL_INVALID", StringComparison.Ordinal))
        {
            return (StatusCodes.Status422UnprocessableEntity, "MANUAL_DELIVERY_PROOF_URL_INVALID");
        }

        if (message.StartsWith("MANUAL_DELIVERY_EVIDENCE_INVALID", StringComparison.Ordinal))
        {
            return (StatusCodes.Status422UnprocessableEntity, "MANUAL_DELIVERY_EVIDENCE_INVALID");
        }

        if (message.StartsWith("FAILED_DELIVERY_EVIDENCE_INVALID", StringComparison.Ordinal))
        {
            return (StatusCodes.Status422UnprocessableEntity, "FAILED_DELIVERY_EVIDENCE_INVALID");
        }

        if (message.StartsWith("RETURN_EVIDENCE_INVALID", StringComparison.Ordinal))
        {
            return (StatusCodes.Status422UnprocessableEntity, "RETURN_EVIDENCE_INVALID");
        }

        if (message.StartsWith("REFUND_LIFECYCLE_REQUIRED", StringComparison.Ordinal))
        {
            return (StatusCodes.Status409Conflict, "REFUND_LIFECYCLE_REQUIRED");
        }

        if (message.StartsWith("ORDER_CANCELLATION_LIFECYCLE_REQUIRED", StringComparison.Ordinal))
        {
            return (StatusCodes.Status409Conflict, "ORDER_CANCELLATION_LIFECYCLE_REQUIRED");
        }

        if (message.Contains("accounting", StringComparison.OrdinalIgnoreCase))
        {
            return (StatusCodes.Status409Conflict, "ACCOUNTING_STATE_CONFLICT");
        }

        if (message.Contains("payment", StringComparison.OrdinalIgnoreCase))
        {
            return (StatusCodes.Status409Conflict, "PAYMENT_STATE_CONFLICT");
        }

        if (message.Contains("Cannot manually update", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("source", StringComparison.OrdinalIgnoreCase) && message.Contains("not allowed", StringComparison.OrdinalIgnoreCase))
        {
            return (StatusCodes.Status409Conflict, "FULFILLMENT_AUTHORITY_MISMATCH");
        }

        if (message.Contains("Cannot transition", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("terminal order", StringComparison.OrdinalIgnoreCase))
        {
            return (StatusCodes.Status409Conflict, "FULFILLMENT_TRANSITION_INVALID");
        }

        return (StatusCodes.Status422UnprocessableEntity, "FULFILLMENT_TRANSITION_REJECTED");
    }

    private async Task<bool> IsCurrentTenantOrderAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.RequiredTenantId;
        return await _dbContext.Orders
            .AsNoTracking()
            .IgnoreQueryFilters()
            .AnyAsync(
                order => order.Id == orderId &&
                         order.TenantId == tenantId &&
                         !order.IsDeleted,
                cancellationToken);
    }
}

#region Request DTOs

public record FulfillmentActionRequest
{
    public string? Notes { get; init; }
}

public record PackActionRequest
{
    public string? PackingSlipNumber { get; init; }
    public string? Notes { get; init; }
}

public record OutForDeliveryActionRequest
{
    public Guid? ShipmentId { get; init; }
    public string? TrackingNumber { get; init; }
    public string? Notes { get; init; }
}

public record DeliveredActionRequest
{
    public string? DeliveryReference { get; init; }
    public string? ProofOfDeliveryUrl { get; init; }
    public string? RecipientName { get; init; }
    public string? Notes { get; init; }
}

public record ReturnedActionRequest
{
    public string Reason { get; init; } = string.Empty;
    public bool ShouldRefund { get; init; }
    public decimal? RefundAmount { get; init; }
    public string? ReturnAuthorizationNumber { get; init; }
    public string? Notes { get; init; }
}

public record FailedDeliveryActionRequest
{
    public string Reason { get; init; } = string.Empty;
    public int AttemptNumber { get; init; } = 1;
    public bool CanRetry { get; init; } = true;
    public string? Notes { get; init; }
}

public record CancelActionRequest
{
    public string Reason { get; init; } = string.Empty;
    public bool ReleaseInventory { get; init; } = true;
    public string? Notes { get; init; }
}

#endregion
