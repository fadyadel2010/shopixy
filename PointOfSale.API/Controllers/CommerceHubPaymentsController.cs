using System.Diagnostics;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using PointOfSale.API.Attributes;
using PointOfSale.API.DTOs.CommerceHub;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Filters;
using PointOfSale.API.Infrastructure.RateLimiting;
using PointOfSale.API.Security;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.API.Services.Platform;

namespace PointOfSale.API.Controllers;

[AllowAnonymous]
[AllowAnonymousTenant]
[ApiController]
[Route("api/commercehub/payments")]
public class CommerceHubPaymentsController : ControllerBase
{
    private const string CustomerAuthenticationScheme = CustomerSecurityConstants.AuthenticationScheme;
    private readonly ICommerceHubPaymentOrchestrationService _paymentOrchestrationService;
    private readonly IRateLimitService _rateLimitService;
    private readonly CommerceHubCustomerIdentityOptions _customerIdentityOptions;
    private readonly ILogger<CommerceHubPaymentsController> _logger;

    public CommerceHubPaymentsController(
        ICommerceHubPaymentOrchestrationService paymentOrchestrationService,
        IRateLimitService rateLimitService,
        IOptions<CommerceHubCustomerIdentityOptions> customerIdentityOptions,
        ILogger<CommerceHubPaymentsController> logger)
    {
        _paymentOrchestrationService = paymentOrchestrationService;
        _rateLimitService = rateLimitService;
        _customerIdentityOptions = customerIdentityOptions.Value;
        _logger = logger;
    }

    [HttpPost("initiate")]
    [ProducesResponseType(typeof(CommerceHubPaymentInitiateResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Initiate(
        [FromBody] CommerceHubPaymentInitiateRequestDto request,
        CancellationToken cancellationToken)
    {
        var traceId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;

        if (request == null)
        {
            return BadRequest(new ApiErrorResponse("INVALID_REQUEST", "Request body is required.", traceId));
        }

        var validation = request.Validate();
        if (validation.Code != null)
        {
            return BadRequest(new ApiErrorResponse(validation.Code, validation.Message!, traceId));
        }

        var orderAccessToken = request.OrderAccessToken?.Trim();
        if (string.IsNullOrWhiteSpace(orderAccessToken))
        {
            orderAccessToken = CommerceHubOrderAccessCookie.ReadOrderAccessToken(Request, request.OrderId);
        }

        var authenticatedCustomerId = await ResolveAuthenticatedCustomerIdAsync();
        if (!authenticatedCustomerId.HasValue && string.IsNullOrWhiteSpace(orderAccessToken))
        {
            return Unauthorized(new ApiErrorResponse(
                "ORDER_ACCESS_DENIED",
                "You do not have permission to initiate payment for this order. Please sign in or use a valid order access token.",
                traceId));
        }

        var rateLimitError = await EnforceInitiateRateLimitAsync(request.OrderId, traceId);
        if (rateLimitError != null)
        {
            return rateLimitError;
        }

        try
        {
            var response = await _paymentOrchestrationService.ExecuteAsync(
                new CommerceHubPaymentInitiateRequestDto
                {
                    OrderId = request.OrderId,
                    Provider = request.Provider,
                    OrderAccessToken = orderAccessToken
                },
                authenticatedCustomerId,
                traceId,
                cancellationToken);

            // Only refresh cookie for guest users (not authenticated customers)
            if (!authenticatedCustomerId.HasValue)
            {
                RefreshOrderAccessCookie(response, orderAccessToken);
            }

            return Ok(response);
        }
        catch (CommerceHubCheckoutException ex)
        {
            _logger.LogWarning(
                "CommerceHub payment initiation blocked | Code: {Code} | TraceId: {TraceId}",
                ex.Code,
                traceId);

            return StatusCode(ex.StatusCode, new ApiErrorResponse(ex.Code, ex.Message, traceId)
            {
                Data = ex.DataPayload
            });
        }
    }

    [HttpPost("webhook")]
    [ServiceFilter(typeof(WebhookContentTypeValidationFilter))]
    [RequestSizeLimit(WebhookRequestLimits.PaymentProviderMaxBytes)]
    [EnableRateLimiting(WebhookRateLimiterPolicy.PolicyName)]
    [ProducesResponseType(typeof(CommerceHubPaymentWebhookResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(CommerceHubPaymentWebhookResultDto), StatusCodes.Status500InternalServerError)]
    public Task<IActionResult> HandleWebhook(CancellationToken cancellationToken) =>
        HandleWebhookCoreAsync(providerOverride: null, cancellationToken);

    [HttpPost("~/api/payments/paymob/webhook")]
    [ServiceFilter(typeof(WebhookContentTypeValidationFilter))]
    [RequestSizeLimit(WebhookRequestLimits.PaymentProviderMaxBytes)]
    [EnableRateLimiting(WebhookRateLimiterPolicy.PolicyName)]
    [ProducesResponseType(typeof(CommerceHubPaymentWebhookResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(CommerceHubPaymentWebhookResultDto), StatusCodes.Status500InternalServerError)]
    public Task<IActionResult> HandleLegacyPaymobWebhook(CancellationToken cancellationToken) =>
        HandleWebhookCoreAsync(CommerceHubPaymentMethods.Paymob, cancellationToken);

    [HttpGet("paymob/callback")]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> HandlePaymobCallback(CancellationToken cancellationToken)
    {
        var traceId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
        var parameters = Request.Query.ToDictionary(
            item => item.Key,
            item => item.Value.FirstOrDefault() ?? string.Empty,
            StringComparer.OrdinalIgnoreCase);
        var result = await _paymentOrchestrationService.ValidateCallbackAsync(
            CommerceHubPaymentMethods.Paymob,
            parameters,
            traceId,
            cancellationToken);

        if (!result.IsValid || !result.OrderId.HasValue)
        {
            return BadRequest(new ApiErrorResponse(
                "PAYMOB_CALLBACK_INVALID",
                "The Paymob browser return could not be verified.",
                traceId));
        }

        // The callback is only a browser-routing signal. CommerceHub reads the
        // canonical backend order/payment state after navigation.
        if (!Uri.TryCreate(_customerIdentityOptions.PublicBaseUrl, UriKind.Absolute, out var publicBaseUrl) ||
            (!string.Equals(publicBaseUrl.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(publicBaseUrl.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)))
        {
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new ApiErrorResponse(
                    "COMMERCEHUB_PUBLIC_URL_INVALID",
                    "CommerceHub browser return routing is not configured.",
                    traceId));
        }

        var redirectBase = publicBaseUrl.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? publicBaseUrl
            : new Uri(publicBaseUrl.AbsoluteUri + "/");
        var redirectUrl = new Uri(
            redirectBase,
            $"orders/{result.OrderId.Value:D}?payment=pending");
        return Redirect(redirectUrl.ToString());
    }

    private async Task<IActionResult> HandleWebhookCoreAsync(
        string? providerOverride,
        CancellationToken cancellationToken)
    {
        var traceId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
        var provider = providerOverride ?? ResolveProvider();
        var signature = ResolveSignature(provider);

        if (!IsSupportedWebhookProvider(provider))
        {
            WebhookRequestBodyReader.RecordPayloadRejected(HttpContext, provider, "unsupported_provider", "payment");
            return BadRequest(new ApiErrorResponse(
                "PAYMENT_PROVIDER_INVALID",
                $"Unsupported payment provider '{provider}'.",
                traceId));
        }

        if (string.IsNullOrWhiteSpace(signature))
        {
            WebhookRequestBodyReader.RecordInvalidSignature(HttpContext, provider, "payment", "missing_signature");
            WebhookRequestBodyReader.RecordPayloadRejected(HttpContext, provider, "missing_signature", "payment");
            return Unauthorized(new ApiErrorResponse(
                "INVALID_SIGNATURE",
                "Webhook signature is required.",
                traceId));
        }

        string rawPayload;
        try
        {
            rawPayload = await WebhookRequestBodyReader.ReadJsonBodyAsync(
                HttpContext,
                provider,
                WebhookRequestLimits.PaymentProviderMaxBytes,
                "payment",
                cancellationToken);
        }
        catch (WebhookRequestRejectedException ex)
        {
            return StatusCode(ex.StatusCode, ex.ToApiError(traceId));
        }

        try
        {
            var result = await _paymentOrchestrationService.ProcessWebhookAsync(
                provider,
                signature,
                rawPayload,
                traceId,
                cancellationToken);

            if (result.ShouldRetry)
            {
                _logger.LogError(
                    "CommerceHub payment webhook requires retry | Provider: {Provider} | PaymentId: {PaymentId} | MarketplaceOrderId: {MarketplaceOrderId} | TraceId: {TraceId}",
                    provider,
                    result.PaymentId,
                    result.MarketplaceOrderId,
                    traceId);

                return StatusCode(StatusCodes.Status500InternalServerError, result);
            }

            return Ok(result);
        }
        catch (CommerceHubCheckoutException ex) when (ex.StatusCode == StatusCodes.Status401Unauthorized)
        {
            WebhookRequestBodyReader.RecordInvalidSignature(HttpContext, provider, "payment");
            _logger.LogWarning(
                "CommerceHub payment webhook rejected | Provider: {Provider} | Code: {Code} | TraceId: {TraceId}",
                provider,
                ex.Code,
                traceId);

            return Unauthorized(new ApiErrorResponse(ex.Code, ex.Message, traceId));
        }
        catch (CommerceHubCheckoutException ex)
        {
            _logger.LogError(
                ex,
                "CommerceHub payment webhook validation failed and will be retried | Provider: {Provider} | Code: {Code} | TraceId: {TraceId}",
                provider,
                ex.Code,
                traceId);

            return StatusCode(StatusCodes.Status500InternalServerError, new CommerceHubPaymentWebhookResultDto
            {
                Processed = false,
                Provider = provider,
                Message = "An error occurred processing the webhook; please retry",
                ShouldRetry = true // Retry validation failures
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "CommerceHub payment webhook failed unexpectedly | Provider: {Provider} | TraceId: {TraceId}",
                provider,
                traceId);

            return StatusCode(StatusCodes.Status500InternalServerError, new CommerceHubPaymentWebhookResultDto
            {
                Processed = false,
                Provider = provider,
                Message = "Processing error.",
                ShouldRetry = true // Retry unexpected errors
            });
        }
    }

    private string ResolveProvider()
    {
        var provider = Request.Query["provider"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(provider))
        {
            return provider.Trim();
        }

        provider = Request.Headers["X-Payment-Provider"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(provider))
        {
            return provider.Trim();
        }

        if (Request.Headers.ContainsKey("Stripe-Signature"))
        {
            return CommerceHubPaymentMethods.Stripe;
        }

        return CommerceHubPaymentMethods.Paymob;
    }

    private static bool IsSupportedWebhookProvider(string provider) =>
        string.Equals(provider, CommerceHubPaymentMethods.Stripe, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(provider, CommerceHubPaymentMethods.Paymob, StringComparison.OrdinalIgnoreCase);

    private string ResolveSignature(string provider)
    {
        if (string.Equals(provider, CommerceHubPaymentMethods.Stripe, StringComparison.OrdinalIgnoreCase))
        {
            return Request.Headers["Stripe-Signature"].FirstOrDefault() ?? string.Empty;
        }

        return Request.Headers["Hmac"].FirstOrDefault() ??
               Request.Headers["hmac"].FirstOrDefault() ??
               Request.Query["hmac"].FirstOrDefault() ??
               string.Empty;
    }

    private async Task<IActionResult?> EnforceInitiateRateLimitAsync(string orderId, string traceId)
    {
        var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var endpoint = $"commercehub-payment-initiate:{orderId.Trim()}";
        var result = await _rateLimitService.CheckRateLimitAsync(clientIp, endpoint, limit: 8, windowSeconds: 60);

        if (result.Allowed)
        {
            return null;
        }

        await _rateLimitService.RecordRateLimitHitAsync(clientIp, endpoint);

        return StatusCode(
            StatusCodes.Status429TooManyRequests,
            new ApiErrorResponse(
                "PAYMENT_RATE_LIMITED",
                "Too many payment attempts were made. Please wait a moment before trying again.",
                traceId));
    }

    private async Task<Guid?> ResolveAuthenticatedCustomerIdAsync()
    {
        var customerAuthenticationResult = await HttpContext.AuthenticateAsync(CustomerAuthenticationScheme);
        if (!customerAuthenticationResult.Succeeded || customerAuthenticationResult.Principal == null)
        {
            return null;
        }

        var customerIdClaim = customerAuthenticationResult.Principal.FindFirst("CustomerId")?.Value;
        return Guid.TryParse(customerIdClaim, out var customerId) ? customerId : null;
    }

    private void RefreshOrderAccessCookie(
        CommerceHubPaymentInitiateResponseDto response,
        string? orderAccessToken)
    {
        if (response.MarketplaceOrderId == Guid.Empty || string.IsNullOrWhiteSpace(orderAccessToken))
        {
            return;
        }

        var options = HttpContext.RequestServices
            .GetService<Microsoft.Extensions.Options.IOptions<PointOfSale.API.Configuration.CheckoutSessionSecurityOptions>>()
            ?.Value;
        var environment = HttpContext.RequestServices.GetService<IWebHostEnvironment>();
        var allowInsecureDevelopmentCookie = environment?.IsDevelopment() == true;

        CommerceHubOrderAccessCookie.StoreOrderAccessToken(
            HttpContext,
            response.MarketplaceOrderId,
            response.OrderNumber,
            orderAccessToken,
            TimeSpan.FromMinutes(options?.RecoveryTokenTtlMinutes ?? 20),
            allowInsecureDevelopmentCookie);
    }
}
