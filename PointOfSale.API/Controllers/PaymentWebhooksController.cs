using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PointOfSale.API.DTOs.Payments;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Attributes;
using PointOfSale.API.Infrastructure.RateLimiting;
using PointOfSale.API.Security;
using PointOfSale.API.Services.Payments;

namespace PointOfSale.API.Controllers;

/// <summary>
/// Payment Webhooks Controller - Handles webhook callbacks from payment providers.
/// 
/// Route: POST /api/payments/webhooks/{provider}
/// 
/// PHASE 4.1: PAYMENTS API
/// 
/// Required Headers:
/// - X-Signature or provider-specific signature header (for verification)
///
/// Tenant Resolution:
/// - Tenant is resolved from the webhook payload by looking up the stored payment record
/// - X-Tenant-Id, if supplied, is treated only as a mismatch check and is never trusted
/// 
/// Webhook Flow:
/// 1. Receive raw payload
/// 2. Verify signature
/// 3. Check idempotency (duplicate = 200 OK no-op)
/// 4. Process payment event
/// 5. Update order/payment status
/// 6. Emit domain events
/// 
/// IMPORTANT: Only return 200 OK for successful processing or verified duplicates.
/// Any unresolved failure must return 500 so the provider retries.
/// </summary>
[ApiController]
[Route("api/payments/webhooks")]
[AllowAnonymous]
[AllowAnonymousTenant]
public class PaymentWebhooksController : ControllerBase
{
    private readonly IPaymentService _paymentService;
    private readonly IPaymentWebhookRequestPipeline _webhookRequestPipeline;
    private readonly ILogger<PaymentWebhooksController> _logger;

    private const string TenantIdHeader = "X-Tenant-Id";
    private const string SignatureHeader = "X-Signature";

    // Provider-specific signature headers
    private static readonly Dictionary<string, string> ProviderSignatureHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        { "stripe", "Stripe-Signature" },
        { "paypal", "Paypal-Transmission-Sig" },
        { "tap", "Tap-Signature" }
    };

    public PaymentWebhooksController(
        IPaymentService paymentService,
        IPaymentWebhookRequestPipeline webhookRequestPipeline,
        ILogger<PaymentWebhooksController> logger)
    {
        _paymentService = paymentService;
        _webhookRequestPipeline = webhookRequestPipeline;
        _logger = logger;
    }

    /// <summary>
    /// Handles payment webhook from a provider.
    /// 
    /// The raw body is read and passed to the payment service for processing.
    /// Signature verification is performed by the payment service.
    /// 
    /// </summary>
    /// <param name="provider">Payment provider name (e.g., stripe, paypal, tap)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Webhook processing result</returns>
    [HttpPost("{provider}")]
    [Consumes("application/json")]
    [RequestSizeLimit(WebhookRequestLimits.PaymentProviderMaxBytes)]
    [EnableRateLimiting(WebhookRateLimiterPolicy.PolicyName)]
    [ProducesResponseType(typeof(WebhookResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(WebhookResponseDto), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> HandleWebhook(
        [FromRoute] string provider,
        CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();

        _logger.LogInformation(
            "Webhook received | Provider: {Provider} | TraceId: {TraceId}",
            provider, traceId);

        PaymentWebhookRequestContext webhookContext;
        try
        {
            webhookContext = await _webhookRequestPipeline.PrepareAsync(
                HttpContext,
                provider,
                traceId,
                request =>
                {
                    if (ProviderSignatureHeaders.TryGetValue(provider, out var providerSignatureHeader))
                    {
                        var providerSignature = request.Headers[providerSignatureHeader].FirstOrDefault();
                        if (!string.IsNullOrWhiteSpace(providerSignature))
                        {
                            return providerSignature;
                        }
                    }

                    return request.Headers[SignatureHeader].FirstOrDefault();
                },
                request =>
                {
                    if (request.Headers.TryGetValue(TenantIdHeader, out var tenantHeaderValue) &&
                        Guid.TryParse(tenantHeaderValue, out var tenantId))
                    {
                        return tenantId;
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
            return StatusCode(ex.StatusCode, new WebhookResponseDto
            {
                Processed = false,
                Message = ex.Message
            });
        }

        try
        {
            var response = await _paymentService.ProcessWebhookAsync(
                webhookContext.TenantId,
                provider.ToLowerInvariant(),
                webhookContext.Signature,
                webhookContext.RawPayload,
                traceId,
                webhookContext.SourceIp,
                webhookContext.UserAgent,
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "Webhook processed | Provider: {Provider} | Processed: {Processed} | LogId: {LogId} | TraceId: {TraceId}",
                provider, response.Processed, response.EventLogId, traceId);

            if (!response.Processed)
            {
                _logger.LogError(
                    "Webhook processing did not complete successfully | Provider: {Provider} | TenantId: {TenantId} | LogId: {LogId} | TraceId: {TraceId}",
                    provider,
                    webhookContext.TenantId,
                    response.EventLogId,
                    traceId);

                return StatusCode(StatusCodes.Status500InternalServerError, response);
            }

            return Ok(response);
        }
        catch (PaymentInvalidSignatureException ex)
        {
            WebhookRequestBodyReader.RecordInvalidSignature(HttpContext, provider, "payment");
            _logger.LogWarning(
                "Invalid webhook signature | Provider: {Provider} | TraceId: {TraceId}",
                provider, traceId);
            
            // Return 401 for invalid signature - provider should investigate
            return Unauthorized(new ApiErrorResponse(ex.ErrorCode, ex.Message, traceId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Webhook processing error | Provider: {Provider} | TraceId: {TraceId}",
                provider, traceId);

            return StatusCode(StatusCodes.Status500InternalServerError, new WebhookResponseDto
            {
                Processed = false,
                Message = "Webhook processing failed. Provider should retry."
            });
        }
    }

    /// <summary>
    /// Health check endpoint for webhook providers.
    /// Some providers ping this endpoint to verify connectivity.
    /// </summary>
    /// <param name="provider">Payment provider name</param>
    /// <returns>Health check response</returns>
    [HttpGet("{provider}")]
    [HttpHead("{provider}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult WebhookHealthCheck([FromRoute] string provider)
    {
        _logger.LogDebug("Webhook health check | Provider: {Provider}", provider);
        return Ok(new { status = "healthy", provider, timestamp = DateTime.UtcNow });
    }

    private string GetTraceId()
    {
        return Activity.Current?.Id ?? HttpContext.TraceIdentifier;
    }

}
