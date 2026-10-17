using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using System.Security.Claims;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.RateLimiting;
using PointOfSale.Infrastructure.Accounting;
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
        
        var result = await _lifecycleService.MarkOfflineCollectionDeliveredAsync(
            _tenantContext.RequiredTenantId,
            orderId,
            request?.DeliveryReference,
            userName);
        
        if (!result.Success)
            return SettlementFailure(result);
        
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
            return BadRequest(new { error = "Refund amount must be positive", code = "REFUND_AMOUNT_INVALID" });
        
        if (string.IsNullOrWhiteSpace(request.RefundId))
            return BadRequest(new { error = "Refund ID is required", code = "REFUND_ID_REQUIRED" });
        
        var result = await _lifecycleService.RecordRefundAsync(
            orderId,
            request.Amount,
            request.RefundId,
            request.Reason ?? "Customer refund");
        
        if (!result.Success)
            return SettlementFailure(result);
        
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
    
    [HttpGet("~/api/payments/cod/manual-settlements/eligible-orders")]
    [Authorize(Roles = "Owner,Admin")]
    public async Task<IActionResult> GetManualSettlementEligibility([FromQuery] int page = 1, CancellationToken ct = default)
    {
        try { return Ok(await _lifecycleService.GetManualCodSettlementSelectionAsync(_tenantContext.RequiredTenantId, page, ct)); }
        catch (ManualCodSettlementException ex) { return Conflict(new { error = ex.Code, code = ex.Code }); }
    }

    [HttpPost("~/api/payments/cod/manual-settlements")]
    [Authorize(Roles = "Owner,Admin")]
    [EnableRateLimiting("FinancialWrite")]
    public async Task<IActionResult> RecordManualCodSettlement([FromBody] ManualCodSettlementApiRequest request, CancellationToken ct)
    {
        var rawActor = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!Guid.TryParse(rawActor, out var actor) || actor == Guid.Empty) return Forbid();
        try
        {
            return Ok(await _lifecycleService.RecordManualCodSettlementAsync(_tenantContext.RequiredTenantId, actor,
                new ManualCodSettlementRequest(request.SettlementId, request.OrderIds), ct));
        }
        catch (ManualCodSettlementException ex) { return Conflict(new { error = ex.Code, code = ex.Code }); }
    }

    [HttpGet("~/api/payments/cod/manual-settlements/{settlementId:guid}")]
    [Authorize(Roles = "Owner,Admin")]
    public async Task<IActionResult> GetManualCodSettlement(Guid settlementId, CancellationToken ct)
    {
        var result = await _lifecycleService.GetManualCodSettlementAsync(_tenantContext.RequiredTenantId, settlementId, ct);
        return result == null ? NotFound() : Ok(result);
    }

    /// <summary>
    /// COD settlement ingress is contained (T-10).
    ///
    /// POST /api/payments/lifecycle/settlements/cod
    ///
    /// No authenticated carrier remittance evidence source exists, so this route
    /// always returns HTTP 409 SETTLEMENT_EVIDENCE_REQUIRED before trusting or
    /// validating any caller-authored settlement facts and without calling
    /// <c>RecordOfflineSettlementAsync</c>. Existing settlement entities, SQL
    /// replay readers, outbox handlers, journal shapes and refund lineage remain
    /// intact as dormant/recovery code for historical rows.
    /// </summary>
    [HttpPost("settlements/cod")]
    [Authorize(Roles = "Owner,Admin")]
    [ProducesResponseType(409)]
    public Task<IActionResult> RecordCodSettlement(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CodSettlementApiRequest? _)
    {
        // T-10 containment: reject before trusting/validating caller-authored
        // carrier, settlement ID, amount, fees, selector, bank reference or
        // destination facts. Never call RecordOfflineSettlementAsync.
        return Task.FromResult<IActionResult>(Conflict(new
        {
            error = "COD settlement is unavailable: authenticated carrier remittance evidence is unavailable.",
            code = "SETTLEMENT_EVIDENCE_REQUIRED"
        }));
    }

    /// <summary>
    /// Gateway settlement ingress is contained (T-10).
    ///
    /// POST /api/payments/lifecycle/settlements/gateway
    ///
    /// No authenticated provider payout evidence source exists, so this route
    /// always returns HTTP 409 SETTLEMENT_EVIDENCE_REQUIRED before trusting or
    /// validating any caller-authored settlement facts and without calling
    /// <c>RecordGatewaySettlementAsync</c>. Existing settlement entities, SQL
    /// replay readers, outbox handlers, journal shapes and refund lineage remain
    /// intact as dormant/recovery code for historical rows.
    /// </summary>
    [HttpPost("settlements/gateway")]
    [Authorize(Roles = "Owner,Admin")]
    [ProducesResponseType(409)]
    public Task<IActionResult> RecordGatewaySettlement(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] GatewaySettlementApiRequest? _)
    {
        // T-10 containment: reject before trusting/validating caller-authored
        // provider, settlement ID, amount, fees, selector, bank reference or
        // destination facts. Never call RecordGatewaySettlementAsync.
        return Task.FromResult<IActionResult>(Conflict(new
        {
            error = "Gateway settlement is unavailable: authenticated provider payout evidence is unavailable.",
            code = "SETTLEMENT_EVIDENCE_REQUIRED"
        }));
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
                Label = "Record COD Collection",
                Description = "Confirm cash collection after physical delivery",
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

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ManualCodSettlementApiRequest(Guid SettlementId, List<Guid> OrderIds);

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
