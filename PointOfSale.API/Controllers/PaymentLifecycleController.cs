using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Payments;

namespace PointOfSale.API.Controllers;

/// <summary>
/// PHASE: COD Payment Lifecycle Architecture
/// 
/// Event-driven payment lifecycle management.
/// 
/// REPLACES the legacy UpdatePaymentStatus endpoint with proper event-driven actions.
/// 
/// Base Path: /api/payments/lifecycle
/// 
/// CRITICAL RULES:
/// 1. NO direct PaymentStatus changes
/// 2. Every money-recognizing action creates a journal entry
/// 3. Actions are contextual based on payment type
/// 
/// COD Orders:
///   POST /api/payments/lifecycle/{orderId}/cod-delivered
///   
/// Manual wallet review is owned by the Admin Manual Payment workflow.
///   
/// Refunds:
///   POST /api/payments/lifecycle/{orderId}/refund
///   
/// Settlements (admin only):
///   POST /api/payments/lifecycle/settlements/cod
///   POST /api/payments/lifecycle/settlements/gateway
/// </summary>
[ApiController]
[Route("api/payments/lifecycle")]
[Authorize(Roles = "Owner,Admin,Manager")]
public class PaymentLifecycleController : ControllerBase
{
    private readonly IPaymentLifecycleService _lifecycleService;
    private readonly ITenantContext _tenantContext;
    
    public PaymentLifecycleController(
        IPaymentLifecycleService lifecycleService,
        ITenantContext tenantContext)
    {
        _lifecycleService = lifecycleService;
        _tenantContext = tenantContext;
    }
    
    // ═══════════════════════════════════════════════════════════════════════════
    // UI ACTIONS ROUTE
    // ═══════════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Get allowed payment actions for an order.
    /// UI uses this to determine which buttons to show.
    /// 
    /// GET /api/payments/lifecycle/{orderId}/actions
    /// </summary>
    [HttpGet("{orderId:guid}/actions")]
    [ProducesResponseType(typeof(PaymentActionsResponse), 200)]
    public async Task<IActionResult> GetAllowedActions(Guid orderId)
    {
        var actions = await _lifecycleService.GetAllowedActionsAsync(orderId);
        
        return Ok(new PaymentActionsResponse
        {
            OrderId = actions.OrderId,
            CurrentStatus = actions.CurrentStatus.ToString(),
            PaymentMethodCode = actions.PaymentMethodCode,
            StatusMessage = actions.StatusMessage,
            AvailableActions = BuildActionList(actions, User)
        });
    }
    
    // ═══════════════════════════════════════════════════════════════════════════
    // COD OPERATIONS
    // ═══════════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Mark a COD order as delivered (cash collected from customer).
    /// 
    /// POST /api/payments/lifecycle/{orderId}/cod-delivered
    /// 
    /// TRIGGERS:
    /// - PaymentStatus: Pending → Collected
    /// - Journal: DEBIT COD Clearing, CREDIT E-commerce Sales
    /// </summary>
    [HttpPost("{orderId:guid}/cod-delivered")]
    [ProducesResponseType(typeof(PaymentActionResponse), 200)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> MarkCodDelivered(Guid orderId, [FromBody] CodDeliveredRequest? request)
    {
        var userName = User.Identity?.Name ?? "System";
        
        var result = await _lifecycleService.HandlePaymentResultAsync(new HandlePaymentResultRequest
        {
            OrderId = orderId,
            Outcome = PaymentLifecycleOutcome.Succeeded,
            Source = PaymentLifecycleSource.OfflineCollection,
            PaymentReference = request?.DeliveryReference,
            Actor = userName
        });
        
        if (!result.Success)
            return BadRequest(new { error = result.ErrorMessage });
        
        return Ok(new PaymentActionResponse
        {
            Success = true,
            NewStatus = result.NewStatus?.ToString() ?? "Collected",
            Message = "COD order marked as delivered successfully",
            JournalNumber = result.JournalNumber
        });
    }
    
    // ═══════════════════════════════════════════════════════════════════════════
    // WALLET OPERATIONS
    // ═══════════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Legacy wallet approval route removed. Manual wallet review is owned by
    /// the Admin Manual Payment workflow.
    /// 
    /// No active route is exposed here.
    /// 
    /// TRIGGERS:
    /// - PaymentStatus: AwaitingVerification → Collected
    /// - Journal: DEBIT Wallet Clearing, CREDIT E-commerce Sales
    /// </summary>
    [NonAction]
    [Authorize(Roles = "Owner,Admin")]
    [ProducesResponseType(typeof(PaymentActionResponse), 200)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> ApproveWalletPayment(Guid orderId, [FromBody] WalletApproveRequest? request)
    {
        await Task.CompletedTask;
        return NotFound();
    }
    
    /// <summary>
    /// Legacy wallet rejection route removed. Manual wallet review is owned by
    /// the Admin Manual Payment workflow.
    /// 
    /// No active route is exposed here.
    /// 
    /// TRIGGERS:
    /// - PaymentStatus: AwaitingVerification → Rejected
    /// - NO journal entry (money never recognized)
    /// </summary>
    [NonAction]
    [Authorize(Roles = "Owner,Admin")]
    [ProducesResponseType(typeof(PaymentActionResponse), 200)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> RejectWalletPayment(Guid orderId, [FromBody] WalletRejectRequest request)
    {
        await Task.CompletedTask;
        return NotFound();
    }
    
    // ═══════════════════════════════════════════════════════════════════════════
    // REFUND OPERATIONS
    // ═══════════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Record a refund for an order.
    /// 
    /// POST /api/payments/lifecycle/{orderId}/refund
    /// 
    /// TRIGGERS:
    /// - PaymentStatus → Refunded (full) or PartiallyRefunded (partial)
    /// - Journal: DEBIT E-commerce Sales, CREDIT Clearing
    /// </summary>
    [HttpPost("{orderId:guid}/refund")]
    [Authorize(Roles = "Owner,Admin")]
    [ProducesResponseType(typeof(PaymentActionResponse), 200)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> RecordRefund(Guid orderId, [FromBody] RefundRequest request)
    {
        if (request.Amount <= 0)
            return BadRequest(new { error = "Refund amount must be positive" });
        
        if (string.IsNullOrWhiteSpace(request.RefundId))
            return BadRequest(new { error = "Refund ID is required" });
        
        var result = await _lifecycleService.RecordRefundAsync(
            orderId,
            request.Amount,
            request.RefundId,
            request.Reason ?? "Customer refund");
        
        if (!result.Success)
            return BadRequest(new { error = result.ErrorMessage });
        
        return Ok(new PaymentActionResponse
        {
            Success = true,
            NewStatus = result.NewStatus?.ToString(),
            Message = "Refund recorded successfully",
            JournalNumber = result.JournalNumber
        });
    }
    
    // ═══════════════════════════════════════════════════════════════════════════
    // SETTLEMENT OPERATIONS (Admin Only)
    // ═══════════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Record a COD settlement from shipping company.
    /// 
    /// POST /api/payments/lifecycle/settlements/cod
    /// 
    /// TRIGGERS:
    /// - PaymentStatus: Collected → Settled (for included orders)
    /// - Journal: DEBIT Bank (net), DEBIT Shipping Fees, CREDIT COD Clearing (gross)
    /// </summary>
    [HttpPost("settlements/cod")]
    [Authorize(Roles = "Owner,Admin")]
    [ProducesResponseType(typeof(PaymentActionResponse), 200)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> RecordCodSettlement([FromBody] CodSettlementApiRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SettlementId))
            return BadRequest(new { error = "Settlement ID is required" });

        if (request.Fees < 0)
            return BadRequest(new { error = "Fees cannot be negative" });

        if (string.IsNullOrWhiteSpace(request.Currency))
            return BadRequest(new { error = "Currency is required" });
        
        var result = await _lifecycleService.RecordOfflineSettlementAsync(new OfflineSettlementRequest
        {
            TenantId = _tenantContext.RequiredTenantId,
            SettlementId = request.SettlementId,
            GrossAmount = request.GrossAmount,
            Fees = request.Fees,
            Currency = request.Currency,
            ShippingCompany = request.ShippingCompany,
            OrderIds = request.OrderIds,
            BankReference = request.BankReference
        });
        
        if (!result.Success)
            return SettlementFailure(result);
        
        return Ok(new PaymentActionResponse
        {
            Success = true,
            NewStatus = "Settled",
            Message = $"COD settlement recorded. {request.OrderIds.Count} orders settled.",
            JournalNumber = result.JournalNumber
        });
    }
    
    /// <summary>
    /// Record a gateway settlement.
    /// 
    /// POST /api/payments/lifecycle/settlements/gateway
    /// 
    /// TRIGGERS:
    /// - Journal: DEBIT Bank (net), DEBIT Gateway Fees, CREDIT Gateway Clearing (gross)
    /// </summary>
    [HttpPost("settlements/gateway")]
    [Authorize(Roles = "Owner,Admin")]
    [ProducesResponseType(typeof(PaymentActionResponse), 200)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> RecordGatewaySettlement([FromBody] GatewaySettlementApiRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SettlementId))
            return BadRequest(new { error = "Settlement ID is required" });
        
        if (string.IsNullOrWhiteSpace(request.Gateway))
            return BadRequest(new { error = "Gateway is required" });
        
        if (request.GrossAmount <= 0)
            return BadRequest(new { error = "Gross amount must be positive" });

        if (request.Fees < 0)
            return BadRequest(new { error = "Fees cannot be negative" });

        if (string.IsNullOrWhiteSpace(request.Currency))
            return BadRequest(new { error = "Currency is required" });

        // Validate that exactly one selector is provided (null-safe)
        var hasPayoutBatchId = !string.IsNullOrWhiteSpace(request.PayoutBatchId) ? 1 : 0;
        var hasOrderIds = (request.OrderIds?.Count ?? 0) > 0 ? 1 : 0;
        var hasPaymentIntentIds = (request.PaymentIntentIds?.Count ?? 0) > 0 ? 1 : 0;
        var selectorCount = hasPayoutBatchId + hasOrderIds + hasPaymentIntentIds;

        if (selectorCount == 0)
        {
            return BadRequest(new
            {
                error = "Gateway settlement requires exactly one of: payoutBatchId, orderIds, or paymentIntentIds"
            });
        }

        if (selectorCount > 1)
        {
            return BadRequest(new
            {
                error = "Gateway settlement requires exactly one selector - cannot mix payoutBatchId, orderIds, and paymentIntentIds"
            });
        }
        
        var result = await _lifecycleService.RecordGatewaySettlementAsync(new GatewaySettlementRequest
        {
            TenantId = _tenantContext.RequiredTenantId,
            SettlementId = request.SettlementId,
            Gateway = request.Gateway,
            PayoutBatchId = request.PayoutBatchId,
            OrderIds = request.OrderIds,
            PaymentIntentIds = request.PaymentIntentIds,
            GrossAmount = request.GrossAmount,
            Fees = request.Fees,
            Currency = request.Currency,
            BankReference = request.BankReference
        });
        
        if (!result.Success)
            return SettlementFailure(result);
        
        return Ok(new PaymentActionResponse
        {
            Success = true,
            NewStatus = "Settled",
            Message = "Gateway settlement recorded",
            JournalNumber = result.JournalNumber
        });
    }
    
    // ═══════════════════════════════════════════════════════════════════════════
    // HELPERS
    // ═══════════════════════════════════════════════════════════════════════════
    
    private static List<AvailableAction> BuildActionList(PaymentAllowedActions actions, System.Security.Claims.ClaimsPrincipal user)
    {
        var list = new List<AvailableAction>();
        
        // Check if user has Owner or Admin role for restricted actions
        var isOwnerOrAdmin = user.IsInRole("Owner") || user.IsInRole("Admin");
        
        if (actions.CanMarkOfflineCollectionDelivered)
        {
            list.Add(new AvailableAction
            {
                ActionCode = "cod-delivered",
                Label = "Mark as Delivered",
                Description = "Confirm cash collected from customer",
                Method = "POST",
                Endpoint = $"/api/payments/lifecycle/{actions.OrderId}/cod-delivered"
            });
        }
        
        // Refund action is restricted to Owner/Admin
        if (actions.CanRefund && isOwnerOrAdmin)
        {
            list.Add(new AvailableAction
            {
                ActionCode = "refund",
                Label = "Issue Refund",
                Description = "Refund full or partial amount",
                Method = "POST",
                Endpoint = $"/api/payments/lifecycle/{actions.OrderId}/refund",
                RequiresAmount = true
            });
        }
        
        return list;
    }

    private IActionResult SettlementFailure(PaymentTransitionResult result)
    {
        var payload = new { error = result.ErrorMessage, code = result.FailureCode };
        return result.FailureCode is "SETTLEMENT_MISMATCH" or "OFFLINE_SETTLEMENT_REJECTED"
            ? Conflict(payload)
            : BadRequest(payload);
    }
}

#region Request/Response DTOs

public class CodDeliveredRequest
{
    public string? DeliveryReference { get; set; }
}

public class WalletApproveRequest
{
    public string? TransactionReference { get; set; }
}

public class WalletRejectRequest
{
    public string Reason { get; set; } = string.Empty;
}

public class RefundRequest
{
    public decimal Amount { get; set; }
    public string RefundId { get; set; } = string.Empty;
    public string? Reason { get; set; }
}

public class CodSettlementApiRequest
{
    public string SettlementId { get; set; } = string.Empty;
    public decimal GrossAmount { get; set; }
    public decimal Fees { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string ShippingCompany { get; set; } = string.Empty;
    public List<Guid> OrderIds { get; set; } = new();
    public string? BankReference { get; set; }
}

public class GatewaySettlementApiRequest
{
    public string SettlementId { get; set; } = string.Empty;
    public string Gateway { get; set; } = string.Empty;
    public string? PayoutBatchId { get; set; }
    public List<Guid> OrderIds { get; set; } = new();
    public List<string> PaymentIntentIds { get; set; } = new();
    public decimal GrossAmount { get; set; }
    public decimal Fees { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string? BankReference { get; set; }
}

public class PaymentActionsResponse
{
    public Guid OrderId { get; set; }
    public string CurrentStatus { get; set; } = string.Empty;
    public string PaymentMethodCode { get; set; } = string.Empty;
    public string StatusMessage { get; set; } = string.Empty;
    public List<AvailableAction> AvailableActions { get; set; } = new();
}

public class AvailableAction
{
    public string ActionCode { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Method { get; set; } = "POST";
    public string Endpoint { get; set; } = string.Empty;
    public bool RequiresReason { get; set; }
    public bool RequiresAmount { get; set; }
}

public class PaymentActionResponse
{
    public bool Success { get; set; }
    public string? NewStatus { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? JournalNumber { get; set; }
}

#endregion
