using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PointOfSale.API.Attributes;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Filters;
using PointOfSale.API.Infrastructure.RateLimiting;
using PointOfSale.API.Security;
using PointOfSale.API.Services.Shipping;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.Shipping;

namespace PointOfSale.API.Controllers;

/// <summary>
/// Shipping carrier webhook receiver.
/// 
/// CARRIER-AGNOSTIC SHIPPING MODULE
/// 
/// This controller receives webhooks from shipping carriers:
/// - Status updates (picked up, in transit, delivered)
/// - COD collection notifications
/// - Delivery failure notifications
/// 
/// ARCHITECTURAL RULES:
/// - No authentication (carriers can't authenticate)
/// - Signature validation per carrier
/// - Carrier-specific parsing in adapter
/// - Only updates Shipment, NEVER Order
/// </summary>
[ApiController]
[Route("api/webhooks/shipping")]
[AllowAnonymous] // Webhooks are unauthenticated
[AllowAnonymousTenant]
public class ShippingWebhooksController : ControllerBase
{
    private readonly ICarrierShipmentService _carrierService;
    private readonly IShippingCarrierRegistry _carrierRegistry;
    private readonly IShippingWebhookRequestPipeline _requestPipeline;
    private readonly CommerceReliabilityTelemetry? _reliabilityTelemetry;
    private readonly ILogger<ShippingWebhooksController> _logger;
    
    public ShippingWebhooksController(
        ICarrierShipmentService carrierService,
        IShippingCarrierRegistry carrierRegistry,
        IShippingWebhookRequestPipeline requestPipeline,
        ILogger<ShippingWebhooksController> logger,
        CommerceReliabilityTelemetry? reliabilityTelemetry = null)
    {
        _carrierService = carrierService;
        _carrierRegistry = carrierRegistry;
        _requestPipeline = requestPipeline;
        _reliabilityTelemetry = reliabilityTelemetry;
        _logger = logger;
    }
    
    /// <summary>
    /// Generic webhook endpoint for any carrier.
    /// </summary>
    /// <remarks>
    /// Carriers should POST to: /api/webhooks/shipping/{carrierCode}
    /// 
    /// Examples:
    /// - Bosta: /api/webhooks/shipping/bosta
    /// - Aramex: /api/webhooks/shipping/aramex
    /// 
    /// The carrier adapter is responsible for parsing the payload.
    /// </remarks>
    [HttpPost("{carrierCode}")]
    [ServiceFilter(typeof(WebhookContentTypeValidationFilter))]
    [RequestSizeLimit(WebhookRequestLimits.ShippingProviderMaxBytes)]
    [EnableRateLimiting(WebhookRateLimiterPolicy.PolicyName)]
    public async Task<IActionResult> ReceiveWebhook(
        string carrierCode,
        [FromHeader(Name = "X-Webhook-Signature")] string? signature,
        CancellationToken cancellationToken)
    {
        return await HandleWebhookAsync(carrierCode, signature, cancellationToken);
    }
    
    /// <summary>
    /// Bosta-specific webhook endpoint.
    /// </summary>
    /// <remarks>
    /// Bosta sends webhooks with specific format.
    /// This endpoint handles Bosta signature validation.
    /// </remarks>
    [HttpPost("bosta")]
    [ServiceFilter(typeof(WebhookContentTypeValidationFilter))]
    [RequestSizeLimit(WebhookRequestLimits.ShippingProviderMaxBytes)]
    [EnableRateLimiting(WebhookRateLimiterPolicy.PolicyName)]
    public async Task<IActionResult> ReceiveBostaWebhook(
        [FromHeader(Name = "Authorization")] string? authorization,
        CancellationToken cancellationToken)
    {
        return await HandleWebhookAsync("bosta", authorization, cancellationToken);
    }
    
    /// <summary>
    /// Aramex-specific webhook endpoint.
    /// </summary>
    [HttpPost("aramex")]
    [ServiceFilter(typeof(WebhookContentTypeValidationFilter))]
    [RequestSizeLimit(WebhookRequestLimits.ShippingProviderMaxBytes)]
    [EnableRateLimiting(WebhookRateLimiterPolicy.PolicyName)]
    public async Task<IActionResult> ReceiveAramexWebhook(
        [FromHeader(Name = "X-Aramex-Signature")] string? signature,
        CancellationToken cancellationToken)
    {
        return await HandleWebhookAsync("aramex", signature, cancellationToken);
    }
    
    /// <summary>
    /// Mylerz-specific webhook endpoint.
    /// </summary>
    /// <remarks>
    /// Mylerz sends webhooks with HMAC-SHA256 signature.
    /// </remarks>
    [HttpPost("mylerz")]
    [ServiceFilter(typeof(WebhookContentTypeValidationFilter))]
    [RequestSizeLimit(WebhookRequestLimits.ShippingProviderMaxBytes)]
    [EnableRateLimiting(WebhookRateLimiterPolicy.PolicyName)]
    public async Task<IActionResult> ReceiveMylerzWebhook(
        [FromHeader(Name = "X-Mylerz-Signature")] string? signature,
        CancellationToken cancellationToken)
    {
        return await HandleWebhookAsync("mylerz", signature, cancellationToken);
    }
    
    /// <summary>
    /// DHL-specific webhook endpoint.
    /// </summary>
    /// <remarks>
    /// DHL sends webhooks with signature in header.
    /// </remarks>
    [HttpPost("dhl")]
    [ServiceFilter(typeof(WebhookContentTypeValidationFilter))]
    [RequestSizeLimit(WebhookRequestLimits.ShippingProviderMaxBytes)]
    [EnableRateLimiting(WebhookRateLimiterPolicy.PolicyName)]
    public async Task<IActionResult> ReceiveDhlWebhook(
        [FromHeader(Name = "X-DHL-Signature")] string? signature,
        CancellationToken cancellationToken)
    {
        return await HandleWebhookAsync("dhl", signature, cancellationToken);
    }

    private async Task<IActionResult> HandleWebhookAsync(
        string carrierCode,
        string? signature,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Received shipping webhook from carrier {CarrierCode}", carrierCode);

        if (!_carrierRegistry.IsCarrierSupported(carrierCode))
        {
            _logger.LogWarning("Webhook received for unsupported carrier: {CarrierCode}", carrierCode);
            _reliabilityTelemetry?.RecordWebhookProcessing(carrierCode, "shipping", "rejected");
            return NotFound(new ApiErrorResponse(
                "WEBHOOK_CARRIER_NOT_SUPPORTED",
                "Carrier webhook endpoint not found.",
                HttpContext.TraceIdentifier));
        }

        string payload;
        try
        {
            payload = await WebhookRequestBodyReader.ReadJsonBodyAsync(
                HttpContext,
                carrierCode,
                WebhookRequestLimits.ShippingProviderMaxBytes,
                "shipping",
                cancellationToken);
        }
        catch (WebhookRequestRejectedException ex)
        {
            _reliabilityTelemetry?.RecordWebhookProcessing(carrierCode, "shipping", "rejected");
            return StatusCode(
                ex.StatusCode,
                ex.ToApiError(HttpContext.TraceIdentifier));
        }

        if (string.IsNullOrWhiteSpace(payload))
        {
            WebhookRequestBodyReader.RecordPayloadRejected(HttpContext, carrierCode, "empty_payload", "shipping");
            _logger.LogWarning("Empty shipping webhook payload from {CarrierCode}", carrierCode);
            _reliabilityTelemetry?.RecordWebhookProcessing(carrierCode, "shipping", "rejected");
            return BadRequest(new ApiErrorResponse(
                "WEBHOOK_EMPTY_PAYLOAD",
                "Webhook payload is required.",
                HttpContext.TraceIdentifier));
        }

        var sourceIp = HttpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = Request.Headers.UserAgent.ToString();
        ShippingWebhookRequestContext webhookContext;

        try
        {
            webhookContext = await _requestPipeline.PrepareAsync(
                HttpContext,
                carrierCode,
                payload,
                signature,
                cancellationToken);
        }
        catch (ShippingWebhookRequestPreparationException ex)
        {
            _logger.LogWarning(
                "Rejected shipping webhook before processing | CarrierCode: {CarrierCode} | ErrorCode: {ErrorCode} | TraceId: {TraceId}",
                ex.CarrierCode,
                ex.ErrorCode,
                HttpContext.TraceIdentifier);

            _reliabilityTelemetry?.RecordWebhookProcessing(carrierCode, "shipping", "rejected");
            return StatusCode(
                ex.StatusCode,
                ex.ToApiError(HttpContext.TraceIdentifier));
        }

        var result = default(WebhookProcessResult);
        try
        {
            result = await _carrierService.ProcessWebhookAsync(
                webhookContext.CarrierCode,
                webhookContext.RawPayload,
                webhookContext.Signature,
                sourceIp ?? webhookContext.SourceIp,
                string.IsNullOrWhiteSpace(userAgent) ? webhookContext.UserAgent : userAgent,
                cancellationToken);
        }
        catch
        {
            _reliabilityTelemetry?.RecordWebhookProcessing(carrierCode, "shipping", "failed");
            throw;
        }

        if (!result.Success)
        {
            _logger.LogWarning(
                "Rejected shipping webhook from {CarrierCode}: {ErrorCode} - {ErrorMessage}",
                carrierCode,
                result.ErrorCode,
                result.ErrorMessage);

            _reliabilityTelemetry?.RecordWebhookProcessing(carrierCode, "shipping", "rejected");
            return StatusCode(
                result.StatusCode,
                new ApiErrorResponse(
                    result.ErrorCode ?? "WEBHOOK_REJECTED",
                    result.ErrorMessage ?? "Webhook rejected.",
                    HttpContext.TraceIdentifier));
        }

        _logger.LogInformation(
            "Processed shipping webhook from {CarrierCode}: ShipmentFound={Found}, StatusChanged={Changed}, Duplicate={Duplicate}",
            carrierCode,
            result.ShipmentFound,
            result.StatusChanged,
            result.IsDuplicate);

        if (result.IsDuplicate)
        {
            _reliabilityTelemetry?.RecordWebhookProcessing(carrierCode, "shipping", "duplicate");
            return Ok(new
            {
                processed = false,
                duplicate = true,
                shipmentFound = result.ShipmentFound,
                shipmentId = result.ShipmentId
            });
        }

        _reliabilityTelemetry?.RecordWebhookProcessing(carrierCode, "shipping", "processed");
        return Ok(new
        {
            processed = true,
            shipmentFound = result.ShipmentFound,
            shipmentId = result.ShipmentId,
            statusChanged = result.StatusChanged
        });
    }
    
    /// <summary>
    /// Health check endpoint for carriers to verify connectivity.
    /// </summary>
    [HttpGet("health")]
    public IActionResult HealthCheck()
    {
        return Ok(new
        {
            status = "healthy",
            timestamp = DateTime.UtcNow,
            supportedCarriers = _carrierRegistry.GetSupportedCarrierCodes()
        });
    }
}
