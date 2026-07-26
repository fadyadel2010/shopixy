using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.API.Attributes;
using PointOfSale.API.Configuration;
using PointOfSale.API.DTOs.CommerceHub;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Security;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.API.Services.Platform;

namespace PointOfSale.API.Controllers;

[AllowAnonymousTenant]
[ApiController]
[Route("api/commercehub/checkout")]
public class CommerceHubCheckoutController : ControllerBase
{
    private const string IdempotencyKeyHeader = "Idempotency-Key";
    private readonly ICommerceHubCheckoutService _checkoutService;
    private readonly IRateLimitService _rateLimitService;
    private readonly ILogger<CommerceHubCheckoutController> _logger;

    public CommerceHubCheckoutController(
        ICommerceHubCheckoutService checkoutService,
        IRateLimitService rateLimitService,
        ILogger<CommerceHubCheckoutController> logger)
    {
        _checkoutService = checkoutService;
        _rateLimitService = rateLimitService;
        _logger = logger;
    }

    [Authorize(AuthenticationSchemes = CustomerSecurityConstants.AuthenticationScheme, Roles = CustomerSecurityConstants.Role)]
    [HttpPost]
    [ProducesResponseType(typeof(CommerceHubCheckoutResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Checkout(
        [FromBody] CommerceHubCheckoutRequestDto request,
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

        var idempotencyKey = Request.Headers.TryGetValue(IdempotencyKeyHeader, out var idempotencyHeader)
            ? idempotencyHeader.FirstOrDefault()
            : request.IdempotencyKey;

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return BadRequest(new ApiErrorResponse("IDEMPOTENCY_KEY_REQUIRED", "Idempotency-Key header is required.", traceId));
        }

        try
        {
            var customerId = ResolveCustomerId();
            if (!customerId.HasValue)
            {
                return Unauthorized(new ApiErrorResponse("INVALID_TOKEN", "Invalid customer authentication token.", traceId));
            }

            var checkoutRateLimitError = await EnforceCheckoutRateLimitAsync(customerId.Value, traceId);
            if (checkoutRateLimitError != null)
            {
                return checkoutRateLimitError;
            }

            var promoRateLimitError = await EnforcePromoRateLimitAsync(
                request.PromoCode,
                $"commercehub-promo-checkout:{customerId.Value:N}",
                traceId);
            if (promoRateLimitError != null)
            {
                return promoRateLimitError;
            }

            var response = await _checkoutService.CheckoutAsync(request, customerId.Value, traceId, idempotencyKey, cancellationToken);
            ApplyOrderAccessCookie(response);
            return Ok(response);
        }
        catch (CommerceHubCheckoutException ex)
        {
            _logger.LogWarning(
                "CommerceHub checkout blocked | Code: {Code} | TraceId: {TraceId}",
                ex.Code,
                traceId);

            return StatusCode(
                ex.StatusCode,
                new ApiErrorResponse(ex.Code, ex.Message, traceId)
                {
                    Data = ex.DataPayload
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CommerceHub checkout failed unexpectedly | TraceId: {TraceId}", traceId);
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new ApiErrorResponse("CHECKOUT_FAILED", "Unable to place the order right now. Please try again.", traceId));
        }
    }

    private Guid? ResolveCustomerId()
    {
        var customerIdClaim = User.FindFirst("CustomerId")?.Value;
        return Guid.TryParse(customerIdClaim, out var customerId) ? customerId : null;
    }

    private async Task<IActionResult?> EnforcePromoRateLimitAsync(
        string? promoCode,
        string endpoint,
        string traceId)
    {
        if (string.IsNullOrWhiteSpace(promoCode))
        {
            return null;
        }

        var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var result = await _rateLimitService.CheckRateLimitAsync(clientIp, endpoint, limit: 12, windowSeconds: 300);
        if (result.Allowed)
        {
            return null;
        }

        await _rateLimitService.RecordRateLimitHitAsync(clientIp, endpoint);

        return StatusCode(
            StatusCodes.Status429TooManyRequests,
            new ApiErrorResponse(
                "PROMO_RATE_LIMITED",
                "Too many promo attempts were made. Please wait a few minutes before trying again.",
                traceId));
    }

    private async Task<IActionResult?> EnforceCheckoutRateLimitAsync(Guid customerId, string traceId)
    {
        var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var result = await _rateLimitService.CheckRateLimitAsync(
            clientIp,
            $"commercehub-checkout:{customerId:N}",
            limit: 6,
            windowSeconds: 60);

        if (result.Allowed)
        {
            return null;
        }

        await _rateLimitService.RecordRateLimitHitAsync(clientIp, $"commercehub-checkout:{customerId:N}");

        return StatusCode(
            StatusCodes.Status429TooManyRequests,
            new ApiErrorResponse(
                "CHECKOUT_RATE_LIMITED",
                "Too many checkout attempts were made. Please wait a moment before trying again.",
                traceId));
    }

    private void ApplyOrderAccessCookie(CommerceHubCheckoutResponseDto response)
    {
        if (response.MarketplaceOrderId == Guid.Empty || string.IsNullOrWhiteSpace(response.OrderAccessToken))
        {
            return;
        }

        var options = HttpContext.RequestServices
            .GetService<Microsoft.Extensions.Options.IOptions<CheckoutSessionSecurityOptions>>()
            ?.Value;
        var environment = HttpContext.RequestServices.GetService<IWebHostEnvironment>();
        var allowInsecureDevelopmentCookie = environment?.IsDevelopment() == true;

        CommerceHubOrderAccessCookie.StoreOrderAccessToken(
            HttpContext,
            response.MarketplaceOrderId,
            response.OrderNumber,
            response.OrderAccessToken,
            TimeSpan.FromMinutes(options?.RecoveryTokenTtlMinutes ?? 20),
            allowInsecureDevelopmentCookie);

        response.OrderAccessToken = null;
    }
}
