using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MongoDB.Driver;
using PointOfSale.API.Services.AI;
using PointOfSale.API.Services.AI.Models;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.MultiTenancy;
using PointOfSale.Infrastructure.Data;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace PointOfSale.API.Controllers;

/// <summary>
/// Storefront Chat Controller - AI Commerce Assistant endpoint.
/// 
/// PHASE 6.5: CommerceAI Closure & Hardening
/// 
/// CRITICAL RULES:
/// - AI is READ-ONLY (no order creation, no mutations)
/// - Feature must be enabled via Admin control page
/// - All responses are validated before returning
/// - Tenant isolation is mandatory
/// </summary>
[ApiController]
[Route("api/storefront/chat")]
[AllowAnonymous]
public class StorefrontChatController : ControllerBase
{
    private readonly ICommerceContextBuilder _contextBuilder;
    private readonly IAIRouterService _aiRouter;
    private readonly ApplicationDbContext _dbContext;
    private readonly TenantContext _tenantContext;
    private readonly ILogger<StorefrontChatController> _logger;

    public StorefrontChatController(
        ICommerceContextBuilder contextBuilder,
        IAIRouterService aiRouter,
        ApplicationDbContext dbContext,
        TenantContext tenantContext,
        ILogger<StorefrontChatController> logger)
    {
        _contextBuilder = contextBuilder;
        _aiRouter = aiRouter;
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    /// <summary>
    /// Process a customer message through the AI assistant.
    /// 
    /// POST /api/storefront/chat
    /// Tenant is resolved by middleware into TenantContext.
    /// 
    /// SAFETY GUARANTEES:
    /// - Returns 403 if CommerceAI is disabled
    /// - Returns 400 if TenantContext is missing or invalid
    /// - Returns 500 with safe message on AI failure
    /// - NEVER exposes system internals
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ChatResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ProcessMessage(
        [FromBody] ChatRequest request,
        CancellationToken cancellationToken = default)
    {
        var correlationId = Guid.NewGuid().ToString();

        // Get tenant ID from TenantContext (already resolved by middleware)
        var tenantId = _tenantContext.TenantId;
        if (!tenantId.HasValue || tenantId.Value == Guid.Empty)
        {
            _logger.LogWarning("Chat request without tenant context | CorrelationId: {CorrelationId}", correlationId);
            return BadRequest(new { error = "Tenant context not available" });
        }

        var resolvedTenantId = tenantId.Value;

        // Validate request
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return BadRequest(new { error = "Message cannot be empty" });
        }

        try
        {
            var commerceAiEnabled = await _dbContext.StoreConfigs
                .AsNoTracking()
                .Where(config => config.TenantId == resolvedTenantId)
                .Select(config => config.CommerceAiEnabled)
                .SingleOrDefaultAsync(cancellationToken);

            if (!commerceAiEnabled)
            {
                _logger.LogWarning(
                    "Chat request for tenant with AI disabled | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
                    resolvedTenantId, correlationId);

                return StatusCode(403, new
                {
                    error = "AI assistant is not available",
                    message = "The AI assistant is currently disabled. Please contact support if you need assistance."
                });
            }

            // Extract customer ID from claims (if authenticated)
            Guid? customerId = null;
            var authentication = await HttpContext.AuthenticateAsync("StorefrontJwt");
            var principal = authentication.Succeeded ? authentication.Principal : null;
            var customerIdClaim = principal?.FindFirst("CustomerId")?.Value;
            if (!string.IsNullOrEmpty(customerIdClaim) && Guid.TryParse(customerIdClaim, out var parsedCustomerId))
            {
                if (principal?.FindFirst("TenantId")?.Value != resolvedTenantId.ToString() ||
                    !await _dbContext.OnlineCustomers.AnyAsync(c => c.TenantId == resolvedTenantId && c.Id == parsedCustomerId && c.IsActive, cancellationToken))
                    return StatusCode(403, new { error = "Invalid customer authority" });
                customerId = parsedCustomerId;
            }

            // Build commerce context
            var context = await _contextBuilder.BuildAsync(
                tenantId: resolvedTenantId,
                message: request.Message,
                customerId: customerId,
                productIdsInView: request.ProductIdsInView,
                cartSessionId: Request.Headers["X-Session-Id"].FirstOrDefault());
            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogInformation(
                "Processing chat message | TenantId: {TenantId} | CustomerId: {CustomerId} | MessageLength: {Length} | CorrelationId: {CorrelationId}",
                resolvedTenantId, customerId, request.Message.Length, correlationId);

            // Route through AI
            var response = await _aiRouter.ProcessAsync(context, cancellationToken);

            _logger.LogInformation(
                "Chat response generated | TenantId: {TenantId} | Type: {Type} | IsFallback: {IsFallback} | CorrelationId: {CorrelationId}",
                resolvedTenantId, response.Type, response.IsFallback, correlationId);

            // StorefrontHttpCachePolicyMiddleware owns and reasserts the full
            // private/no-store contract for success and every failure response.
            return response.IsFallback ? StatusCode(503, response) : Ok(response);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Chat request cancelled | TenantId: {TenantId} | CorrelationId: {CorrelationId}", resolvedTenantId, correlationId);
            return StatusCode(cancellationToken.IsCancellationRequested ? 499 : 504, new ChatResponse
            {
                Type = ChatResponseTypes.Message,
                Message = "The request took too long. Please try again.",
                IsFallback = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Chat request failed | TenantId: {TenantId} | CorrelationId: {CorrelationId}", resolvedTenantId, correlationId);
            return StatusCode(500, new ChatResponse
            {
                Type = ChatResponseTypes.Message,
                Message = "I'm having trouble right now. Please try again in a moment.",
                IsFallback = true
            });
        }
    }
}
