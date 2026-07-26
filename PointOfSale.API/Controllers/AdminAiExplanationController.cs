using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using PointOfSale.API.Services.AI.Admin;
using PointOfSale.API.Services.AI.Models;
using PointOfSale.Core.Interfaces;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace PointOfSale.API.Controllers;

/// <summary>
/// Admin AI Explanation Controller - Event explanation intelligence.
/// 
/// PHASE 6.5: CommerceAI Closure & Hardening
/// 
/// CRITICAL RULES:
/// - Admin-only access
/// - AI explains WHY, never suggests WHAT
/// - No system internals exposed
/// - No action recommendations
/// </summary>
[ApiController]
[Route("api/admin/ai")]
[Authorize(Roles = "Owner,Admin,Manager")]
public class AdminAiExplanationController : ControllerBase
{
    private readonly IAdminAuditService _auditService;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<AdminAiExplanationController> _logger;

    public AdminAiExplanationController(
        IAdminAuditService auditService,
        ITenantContext tenantContext,
        ILogger<AdminAiExplanationController> logger)
    {
        _auditService = auditService;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    /// <summary>
    /// Explain an event or status change using AI.
    /// 
    /// POST /api/admin/ai/explain-event
    /// Tenant scope is obtained from trusted request context.
    /// 
    /// SAFETY GUARANTEES:
    /// - Returns explanation ONLY (no suggestions)
    /// - Never exposes system internals
    /// - Never recommends actions
    /// - Admin-only access
    /// </summary>
    [HttpPost("explain-event")]
    [ProducesResponseType(typeof(AdminExplanationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ExplainEvent(
        [FromBody] AdminExplanationRequest request,
        CancellationToken cancellationToken = default)
    {
        var correlationId = Guid.NewGuid().ToString();

        var tenantId = _tenantContext.RequiredTenantId;

        // Validate request
        if (string.IsNullOrWhiteSpace(request.Question))
        {
            return BadRequest(new { error = "Question cannot be empty" });
        }

        try
        {
            _logger.LogInformation(
                "Admin AI explanation requested | TenantId: {TenantId} | Question: {Question} | CorrelationId: {CorrelationId}",
                tenantId, request.Question, correlationId);

            // Build audit context from request
            var auditContext = new AdminAuditContext
            {
                Question = request.Question,
                Actor = request.Actor ?? "system",
                Event = request.Event ?? new AuditEventContext(),
                Order = request.Order,
                Shipment = request.Shipment,
                Source = request.Source ?? "AdminAction"
            };

            // Get explanation from AI
            var response = await _auditService.ExplainEventAsync(auditContext, cancellationToken);

            _logger.LogInformation(
                "Admin AI explanation generated | TenantId: {TenantId} | Success: {Success} | CorrelationId: {CorrelationId}",
                tenantId, response.Success, correlationId);

            return Ok(response);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Admin AI request cancelled | TenantId: {TenantId} | CorrelationId: {CorrelationId}", tenantId, correlationId);
            return StatusCode(500, new
            {
                error = "Request timeout",
                message = "The explanation request took too long. Please try again."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Admin AI request failed | TenantId: {TenantId} | CorrelationId: {CorrelationId}", tenantId, correlationId);
            return StatusCode(500, new
            {
                error = "Explanation failed",
                message = "Unable to generate explanation. Please try again."
            });
        }
    }

}

/// <summary>
/// Request for admin AI explanation.
/// PHASE 6.5: Admin Mode endpoint.
/// </summary>
public class AdminExplanationRequest
{
    /// <summary>
    /// The admin's question about the event.
    /// </summary>
    public string Question { get; set; } = string.Empty;

    /// <summary>
    /// Who/what caused the event (optional).
    /// Values: "system", "carrier", "admin", "payment", "customer"
    /// </summary>
    public string? Actor { get; set; }

    /// <summary>
    /// Event details (optional).
    /// </summary>
    public AuditEventContext? Event { get; set; }

    /// <summary>
    /// Order details (optional).
    /// </summary>
    public OrderTrackingContext? Order { get; set; }

    /// <summary>
    /// Shipment details (optional).
    /// </summary>
    public ShipmentTrackingContext? Shipment { get; set; }

    /// <summary>
    /// Event source (optional).
    /// Values: "Webhook", "AdminAction", "BackgroundJob", "Integration", "API"
    /// </summary>
    public string? Source { get; set; }
}
