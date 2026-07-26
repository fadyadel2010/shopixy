using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Security;
using PointOfSale.Infrastructure.Instrumentation;

namespace PointOfSale.API.Filters;

/// <summary>
/// Validates webhook content-type and records telemetry before [Consumes] attribute takes effect.
/// This ensures invalid_content_type telemetry is captured even when ASP.NET rejects the request early.
/// </summary>
public sealed class WebhookContentTypeValidationFilter : IAsyncActionFilter
{
    private readonly CommerceReliabilityTelemetry _telemetry;

    public WebhookContentTypeValidationFilter(CommerceReliabilityTelemetry telemetry)
    {
        _telemetry = telemetry;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var request = context.HttpContext.Request;
        var contentType = request.ContentType;

        // Check if content-type is application/json
        if (!IsApplicationJson(contentType))
        {
            // Determine provider and webhook kind from route/controller
            var provider = ResolveProvider(context);
            var webhookKind = ResolveWebhookKind(context);

            // Record telemetry for invalid content type
            _telemetry.RecordWebhookPayloadRejected(provider, "invalid_content_type", webhookKind);

            // Return 415 Unsupported Media Type
            // Use correlation ID from header if present, otherwise fall back to TraceIdentifier
            var traceId = context.HttpContext.Request.Headers.TryGetValue("X-Correlation-ID", out var correlationId) && !string.IsNullOrWhiteSpace(correlationId.ToString())
                ? correlationId.ToString()
                : context.HttpContext.TraceIdentifier;
            
            context.Result = new ObjectResult(new ApiErrorResponse(
                "WEBHOOK_UNSUPPORTED_CONTENT_TYPE",
                "Webhook content type must be application/json.",
                traceId))
            {
                StatusCode = StatusCodes.Status415UnsupportedMediaType
            };
            return;
        }

        // Content-type is valid, proceed with action execution
        await next();
    }

    private static bool IsApplicationJson(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return false;
        }

        var mediaType = contentType.Split(';', 2)[0].Trim();
        return string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveProvider(ActionExecutingContext context)
    {
        // Try to get provider from route parameters
        if (context.RouteData.Values.TryGetValue("carrierCode", out var carrierCode) && carrierCode is string carrier)
        {
            return carrier;
        }

        // Check controller name to determine provider type
        var controllerName = context.Controller.GetType().Name;
        if (controllerName.Contains("Shipping", StringComparison.OrdinalIgnoreCase))
        {
            // Try to extract from action name or route
            var actionName = context.ActionDescriptor.DisplayName ?? string.Empty;
            if (actionName.Contains("bosta", StringComparison.OrdinalIgnoreCase))
                return "bosta";
            if (actionName.Contains("aramex", StringComparison.OrdinalIgnoreCase))
                return "aramex";
            if (actionName.Contains("mylerz", StringComparison.OrdinalIgnoreCase))
                return "mylerz";
            if (actionName.Contains("dhl", StringComparison.OrdinalIgnoreCase))
                return "dhl";

            return "unknown_carrier";
        }

        if (controllerName.Contains("Payment", StringComparison.OrdinalIgnoreCase))
        {
            // Try to get provider from route parameters
            if (context.RouteData.Values.TryGetValue("provider", out var provider) && provider is string providerStr)
            {
                return providerStr;
            }
            if (context.RouteData.Values.TryGetValue("paymentProvider", out var paymentProvider) && paymentProvider is string paymentProviderStr)
            {
                return paymentProviderStr;
            }

            // Try to extract from action name or controller name
            var actionName = context.ActionDescriptor.DisplayName ?? string.Empty;
            if (actionName.Contains("paymob", StringComparison.OrdinalIgnoreCase) || controllerName.Contains("paymob", StringComparison.OrdinalIgnoreCase))
                return "paymob";
            if (actionName.Contains("commercehub", StringComparison.OrdinalIgnoreCase) || controllerName.Contains("commercehub", StringComparison.OrdinalIgnoreCase))
                return "commercehub";

            return "unknown_payment_provider";
        }

        return "unknown";
    }

    private static string ResolveWebhookKind(ActionExecutingContext context)
    {
        var controllerName = context.Controller.GetType().Name;
        if (controllerName.Contains("Shipping", StringComparison.OrdinalIgnoreCase))
        {
            return "shipping";
        }

        if (controllerName.Contains("Payment", StringComparison.OrdinalIgnoreCase))
        {
            return "payment";
        }

        return "unknown";
    }
}
