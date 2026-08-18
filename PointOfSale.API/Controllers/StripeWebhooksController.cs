using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PointOfSale.API.Attributes;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Infrastructure.RateLimiting;
using PointOfSale.API.Security;
using PointOfSale.API.Services.Payments;
using PointOfSale.API.Services.Payments.Stripe;

namespace PointOfSale.API.Controllers;

/// <summary>
/// Stripe Webhook Controller.
/// 
/// Route: POST /api/webhooks/stripe
/// 
/// PHASE 10.4.D4: Stripe Integration (Global, Intent-Based, Production-Grade)
/// 
/// This endpoint receives webhook events directly from Stripe.
/// 
/// CRITICAL RULES:
/// - Signature MUST be verified using webhook secret
/// - All events are idempotent (duplicates are ignored)
/// - NEVER acknowledge unresolved failures with 200
/// - Webhook is the ONLY source of truth for payment status
/// 
/// Events handled:
/// - payment_intent.succeeded → Mark order as Paid
/// - payment_intent.payment_failed → Mark payment failed
/// - payment_intent.canceled → Mark order canceled
/// - charge.refunded → Mark order refunded (future)
/// </summary>
[ApiController]
[Route("api/webhooks/stripe")]
[AllowAnonymous]
[AllowAnonymousTenant]
public class StripeWebhooksController : ControllerBase
{
    private readonly IStripePaymentService _stripePaymentService;
    private readonly IPaymentWebhookRequestPipeline _webhookRequestPipeline;
    private readonly ILogger<StripeWebhooksController> _logger;

    private const string TenantIdHeader = "X-Tenant-Id";

    public StripeWebhooksController(
        IStripePaymentService stripePaymentService,
        IPaymentWebhookRequestPipeline webhookRequestPipeline,
        ILogger<StripeWebhooksController> logger)
    {
        _stripePaymentService = stripePaymentService;
        _webhookRequestPipeline = webhookRequestPipeline;
        _logger = logger;
    }

    /// <summary>
    /// Handle Stripe webhook events.
    /// 
    /// Required header:
    /// - Stripe-Signature: Stripe's signature for verification
    /// 
    /// Tenant resolution is server-authoritative:
    /// - Extract Stripe payment intent reference from the payload
    /// - Resolve the matching payment intent from the database
    /// - Derive tenant from the stored payment record
    /// - Any tenant query/header value is treated as advisory only and rejected if it mismatches
    /// </summary>
    [HttpPost]
    [Consumes("application/json")]
    [RequestSizeLimit(WebhookRequestLimits.PaymentProviderMaxBytes)]
    [EnableRateLimiting(WebhookRateLimiterPolicy.PolicyName)]
    [ProducesResponseType(typeof(StripeWebhookResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(StripeWebhookResultDto), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> HandleWebhook(CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();

        _logger.LogInformation(
            "Stripe webhook received | TraceId: {TraceId}",
            traceId);

        PaymentWebhookRequestContext webhookContext;
        try
        {
            webhookContext = await _webhookRequestPipeline.PrepareAsync(
                HttpContext,
                "stripe",
                traceId,
                request => request.Headers["Stripe-Signature"].FirstOrDefault(),
                request =>
                {
                    if (request.Query.TryGetValue("tenant", out var tenantQuery) &&
                        Guid.TryParse(tenantQuery, out var tenantFromQuery))
                    {
                        return tenantFromQuery;
                    }

                    if (request.Headers.TryGetValue(TenantIdHeader, out var tenantHeader) &&
                        Guid.TryParse(tenantHeader, out var tenantFromHeader))
                    {
                        return tenantFromHeader;
                    }

                    return null;
                },
                cancellationToken);
        }
        catch (PaymentWebhookRequestPreparationException ex) when (ex.UseApiErrorResponse)
        {
            return StatusCode(ex.StatusCode, ex.ToApiError(traceId));
        }
        catch (PaymentWebhookRequestPreparationException ex)
        {
            return StatusCode(ex.StatusCode, new StripeWebhookResultDto
            {
                Processed = false,
                Message = ex.Message
            });
        }

        try
        {
            var result = await _stripePaymentService.ProcessWebhookAsync(
                webhookContext.TenantId,
                webhookContext.Signature,
                webhookContext.RawPayload,
                traceId,
                cancellationToken);

            _logger.LogInformation(
                "Stripe webhook processed | EventType: {EventType} | Processed: {Processed} | TraceId: {TraceId}",
                result.EventType, result.Processed, traceId);

            if (!result.Processed)
            {
                _logger.LogError(
                    "Stripe webhook processing did not complete successfully | TenantId: {TenantId} | EventType: {EventType} | TraceId: {TraceId}",
                    webhookContext.TenantId,
                    result.EventType,
                    traceId);

                return StatusCode(StatusCodes.Status500InternalServerError, result);
            }

            return Ok(result);
        }
        catch (StripeWebhookSignatureException)
        {
            WebhookRequestBodyReader.RecordInvalidSignature(HttpContext, "stripe", "payment");
            _logger.LogWarning(
                "Invalid Stripe webhook signature | TenantId: {TenantId} | TraceId: {TraceId}",
                webhookContext.TenantId, traceId);

            // Return 401 for invalid signature - Stripe should investigate
            return Unauthorized(new ApiErrorResponse(
                "INVALID_SIGNATURE",
                "Webhook signature is invalid.",
                traceId));
        }
        catch (PaymentException ex)
        {
            _logger.LogError(
                ex,
                "Stripe webhook validation failed | TenantId: {TenantId} | Code: {Code} | TraceId: {TraceId}",
                webhookContext.TenantId,
                ex.ErrorCode,
                traceId);

            return StatusCode(StatusCodes.Status500InternalServerError, new StripeWebhookResultDto
            {
                Processed = false,
                Message = "Webhook processing failed. Provider should retry."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Stripe webhook processing error | TenantId: {TenantId} | TraceId: {TraceId}",
                webhookContext.TenantId, traceId);

            return StatusCode(StatusCodes.Status500InternalServerError, new StripeWebhookResultDto
            {
                Processed = false,
                Message = "Webhook processing failed. Provider should retry."
            });
        }
    }

    /// <summary>
    /// Health check endpoint for Stripe webhook URL verification.
    /// </summary>
    [HttpGet]
    [HttpHead]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult HealthCheck()
    {
        return Ok(new
        {
            status = "healthy",
            provider = "stripe",
            timestamp = DateTime.UtcNow
        });
    }

    #region Helper Methods

    private string GetTraceId()
    {
        return Activity.Current?.Id ?? HttpContext.TraceIdentifier;
    }

    #endregion
}
