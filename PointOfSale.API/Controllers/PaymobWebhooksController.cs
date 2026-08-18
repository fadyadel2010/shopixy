using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PointOfSale.API.Attributes;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Infrastructure.RateLimiting;
using PointOfSale.API.Security;
using PointOfSale.API.Services.Payments;
using PointOfSale.API.Services.Payments.Paymob;

namespace PointOfSale.API.Controllers;

/// <summary>
/// Paymob Webhook Controller.
/// 
/// Route: POST /api/webhooks/paymob
/// 
/// PHASE 10.4.D5: Paymob Integration (Production-Grade, Stripe-Level)
/// 
/// This endpoint receives webhook events directly from Paymob.
/// 
/// CRITICAL RULES:
/// - HMAC signature MUST be verified using webhook secret
/// - All events are idempotent (duplicates are ignored)
/// - NEVER acknowledge unresolved failures with 200
/// - Webhook is the ONLY source of truth for payment status
/// 
/// Events handled:
/// - Transaction processed (success = true) → Mark order as Paid
/// - Transaction processed (success = false) → Mark payment failed
/// </summary>
[ApiController]
[Route("api/webhooks/paymob")]
[AllowAnonymous]
[AllowAnonymousTenant]
public class PaymobWebhooksController : ControllerBase
{
    private readonly IPaymobPaymentService _paymobPaymentService;
    private readonly IPaymentWebhookRequestPipeline _webhookRequestPipeline;
    private readonly ILogger<PaymobWebhooksController> _logger;

    private const string HmacHeader = "Hmac";
    private const string TenantIdHeader = "X-Tenant-Id";

    public PaymobWebhooksController(
        IPaymobPaymentService paymobPaymentService,
        IPaymentWebhookRequestPipeline webhookRequestPipeline,
        ILogger<PaymobWebhooksController> logger)
    {
        _paymobPaymentService = paymobPaymentService;
        _webhookRequestPipeline = webhookRequestPipeline;
        _logger = logger;
    }

    /// <summary>
    /// Handle Paymob webhook events.
    /// 
    /// Paymob sends transaction callbacks to this endpoint.
    /// 
    /// Required Headers:
    /// - Hmac: Paymob's HMAC signature for verification
    /// 
    /// Tenant resolution is server-authoritative:
    /// - Extract Paymob order reference from the webhook payload
    /// - Resolve the matching payment intent from the database
    /// - Derive tenant from the stored payment record
    /// - Any tenant query/header value is treated as advisory only and rejected if it mismatches
    /// </summary>
    [HttpPost]
    [Consumes("application/json")]
    [RequestSizeLimit(WebhookRequestLimits.PaymentProviderMaxBytes)]
    [EnableRateLimiting(WebhookRateLimiterPolicy.PolicyName)]
    [ProducesResponseType(typeof(PaymobWebhookResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(PaymobWebhookResultDto), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> HandleWebhook(CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();

        _logger.LogInformation(
            "Paymob webhook received | TraceId: {TraceId}",
            traceId);

        PaymentWebhookRequestContext webhookContext;
        try
        {
            webhookContext = await _webhookRequestPipeline.PrepareAsync(
                HttpContext,
                "paymob",
                traceId,
                request =>
                {
                    if (request.Headers.TryGetValue(HmacHeader, out var hmacHeader))
                    {
                        var headerSignature = hmacHeader.FirstOrDefault();
                        if (!string.IsNullOrWhiteSpace(headerSignature))
                        {
                            return headerSignature;
                        }
                    }

                    return request.Query.TryGetValue("hmac", out var hmacQuery)
                        ? hmacQuery.FirstOrDefault()
                        : null;
                },
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
            return StatusCode(ex.StatusCode, new PaymobWebhookResultDto
            {
                Processed = false,
                Message = ex.Message
            });
        }

        try
        {
            var result = await _paymobPaymentService.ProcessWebhookAsync(
                webhookContext.TenantId,
                webhookContext.Signature,
                webhookContext.RawPayload,
                traceId,
                cancellationToken);

            _logger.LogInformation(
                "Paymob webhook processed | TransactionId: {TransactionId} | Processed: {Processed} | TraceId: {TraceId}",
                result.TransactionId, result.Processed, traceId);

            if (!result.Processed)
            {
                _logger.LogError(
                    "Paymob webhook processing did not complete successfully | TenantId: {TenantId} | TransactionId: {TransactionId} | TraceId: {TraceId}",
                    webhookContext.TenantId,
                    result.TransactionId,
                    traceId);

                return StatusCode(StatusCodes.Status500InternalServerError, result);
            }

            return Ok(result);
        }
        catch (PaymobWebhookSignatureException)
        {
            WebhookRequestBodyReader.RecordInvalidSignature(HttpContext, "paymob", "payment");
            _logger.LogWarning(
                "Invalid Paymob webhook HMAC | TenantId: {TenantId} | TraceId: {TraceId}",
                webhookContext.TenantId, traceId);

            // Return 401 for invalid signature
            return Unauthorized(new ApiErrorResponse("INVALID_SIGNATURE", "Invalid HMAC signature.", traceId));
        }
        catch (PaymentException ex)
        {
            _logger.LogError(
                ex,
                "Paymob webhook validation failed | TenantId: {TenantId} | Code: {Code} | TraceId: {TraceId}",
                webhookContext.TenantId,
                ex.ErrorCode,
                traceId);

            return StatusCode(StatusCodes.Status500InternalServerError, new PaymobWebhookResultDto
            {
                Processed = false,
                Message = "Paymob not configured for this tenant"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Paymob webhook processing error | TenantId: {TenantId} | TraceId: {TraceId}",
                webhookContext.TenantId, traceId);

            return StatusCode(StatusCodes.Status500InternalServerError, new PaymobWebhookResultDto
            {
                Processed = false,
                Message = "Webhook processing failed. Provider should retry."
            });
        }
    }

    /// <summary>
    /// Handle Paymob callback redirect.
    /// 
    /// When customer completes payment on Paymob page, they are redirected here.
    /// This is NOT for payment confirmation - just for UI flow.
    /// Actual payment status comes from webhook.
    /// </summary>
    [HttpGet("callback")]
    [ProducesResponseType(StatusCodes.Status302Found)]
    public async Task<IActionResult> HandleCallback(CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();

        _logger.LogInformation(
            "Paymob callback received | TraceId: {TraceId}",
            traceId);

        // Extract callback parameters
        var callbackParams = new Dictionary<string, string>();
        foreach (var param in Request.Query)
        {
            callbackParams[param.Key] = param.Value.FirstOrDefault() ?? string.Empty;
        }

        try
        {
            var result = await _paymobPaymentService.ValidateCallbackAsync(
                callbackParams,
                traceId,
                cancellationToken);

            if (!result.IsValid ||
                !result.TenantId.HasValue ||
                string.IsNullOrWhiteSpace(result.StorefrontBaseUrl))
            {
                _logger.LogWarning(
                    "Paymob callback rejected because stored routing context is unavailable | TenantId: {TenantId} | OrderId: {OrderId} | PaymentIntentId: {PaymentIntentId} | TraceId: {TraceId}",
                    result.TenantId,
                    result.OrderId,
                    result.PaymentIntentId,
                    traceId);

                return BuildSafeErrorPage(
                    StatusCodes.Status400BadRequest,
                    "Unable to verify this payment return",
                    "The payment callback did not contain a valid tenant-specific routing context.",
                    traceId);
            }

            var claimedTenantId = TryGetClaimedTenantId();
            if (claimedTenantId.HasValue && claimedTenantId.Value != result.TenantId.Value)
            {
                _logger.LogWarning(
                    "Paymob callback tenant mismatch | ClaimedTenantId: {ClaimedTenantId} | ResolvedTenantId: {ResolvedTenantId} | TraceId: {TraceId}",
                    claimedTenantId,
                    result.TenantId,
                    traceId);

                return BuildSafeErrorPage(
                    StatusCodes.Status400BadRequest,
                    "Unable to verify this payment return",
                    "The payment callback tenant did not match the stored payment record.",
                    traceId);
            }

            ApplyResolvedTenantContext(result.TenantId.Value);

            // Redirect to the pending confirmation page.
            // Final success/failure is still decided by backend payment state polling.
            var storefrontUrl = PaymentIntentRoutingMetadataSerializer.NormalizeBaseUrl(result.StorefrontBaseUrl);
            var orderQuery = result.OrderId.HasValue
                ? $"?orderId={result.OrderId}"
                : string.Empty;
            var redirectUrl = $"{storefrontUrl}/checkout/pending{orderQuery}";

            _logger.LogInformation(
                "Paymob callback processed | OrderId: {OrderId} | Success: {Success} | RedirectTo: {RedirectUrl} | TraceId: {TraceId}",
                result.OrderId, result.IsSuccess, redirectUrl, traceId);

            return Redirect(redirectUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Paymob callback processing error | TraceId: {TraceId}",
                traceId);

            return BuildSafeErrorPage(
                StatusCodes.Status500InternalServerError,
                "Payment verification is still pending",
                "We could not safely route this payment callback. Please return to the store and check your order status.",
                traceId);
        }
    }

    /// <summary>
    /// Health check endpoint for Paymob webhook URL verification.
    /// </summary>
    [HttpGet]
    [HttpHead]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult HealthCheck()
    {
        return Ok(new
        {
            status = "healthy",
            provider = "paymob",
            timestamp = DateTime.UtcNow
        });
    }

    #region Helper Methods

    private Guid? TryGetClaimedTenantId()
    {
        if (Request.Query.TryGetValue("tenant", out var tenantQuery) &&
            Guid.TryParse(tenantQuery, out var tenantFromQuery))
        {
            return tenantFromQuery;
        }

        if (Request.Headers.TryGetValue(TenantIdHeader, out var tenantHeader) &&
            Guid.TryParse(tenantHeader, out var tenantFromHeader))
        {
            return tenantFromHeader;
        }

        return null;
    }

    private void ApplyResolvedTenantContext(Guid tenantId)
    {
        var tenantContext = HttpContext.RequestServices.GetService<PointOfSale.Infrastructure.MultiTenancy.TenantContext>();
        tenantContext?.SetTenantId(tenantId);
    }

    private ContentResult BuildSafeErrorPage(
        int statusCode,
        string title,
        string message,
        string traceId)
    {
        Response.StatusCode = statusCode;

        var html = $@"
<!doctype html>
<html lang=""en"">
<head>
  <meta charset=""utf-8"">
  <meta name=""viewport"" content=""width=device-width, initial-scale=1"">
  <title>{System.Net.WebUtility.HtmlEncode(title)}</title>
  <style>
    body {{ font-family: Arial, sans-serif; background: #f8fafc; color: #0f172a; margin: 0; }}
    main {{ max-width: 40rem; margin: 8vh auto; padding: 2rem; background: #ffffff; border: 1px solid #e2e8f0; border-radius: 16px; box-shadow: 0 12px 40px rgba(15, 23, 42, 0.08); }}
    h1 {{ margin-top: 0; font-size: 1.75rem; }}
    p {{ line-height: 1.6; }}
    code {{ background: #e2e8f0; padding: 0.125rem 0.375rem; border-radius: 6px; }}
  </style>
</head>
<body>
  <main>
    <h1>{System.Net.WebUtility.HtmlEncode(title)}</h1>
    <p>{System.Net.WebUtility.HtmlEncode(message)}</p>
    <p>Reference: <code>{System.Net.WebUtility.HtmlEncode(traceId)}</code></p>
  </main>
</body>
</html>";

        return new ContentResult
        {
            Content = html,
            ContentType = "text/html; charset=utf-8",
            StatusCode = statusCode
        };
    }

    private string GetTraceId()
    {
        return Activity.Current?.Id ?? HttpContext.TraceIdentifier;
    }

    #endregion
}
