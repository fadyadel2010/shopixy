using System.Diagnostics;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.API.Attributes;
using PointOfSale.API.DTOs.CommerceHub;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Security;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.API.Services.Platform;
using StatusCodes = Microsoft.AspNetCore.Http.StatusCodes;

namespace PointOfSale.API.Controllers;

[AllowAnonymous]
[AllowAnonymousTenant]
[ApiController]
[Route("api/commercehub/cart")]
public class CommerceHubCartController : ControllerBase
{
    private readonly ICommerceHubCartService _cartService;
    private readonly IRateLimitService _rateLimitService;
    private readonly ILogger<CommerceHubCartController> _logger;

    public CommerceHubCartController(
        ICommerceHubCartService cartService,
        IRateLimitService rateLimitService,
        ILogger<CommerceHubCartController> logger)
    {
        _cartService = cartService;
        _rateLimitService = rateLimitService;
        _logger = logger;
    }

    [HttpGet]
    [ProducesResponseType(typeof(CommerceHubCartStateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetCart(CancellationToken cancellationToken)
    {
        var traceId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;

        try
        {
            var owner = await ResolveOwnerContextAsync(cancellationToken);
            var response = await _cartService.GetCartAsync(owner, traceId, cancellationToken);
            return Ok(response);
        }
        catch (CommerceHubCartException ex)
        {
            _logger.LogWarning(
                "CommerceHub cart load blocked | Code: {Code} | TraceId: {TraceId}",
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
            _logger.LogError(ex, "CommerceHub cart load failed unexpectedly | TraceId: {TraceId}", traceId);
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new ApiErrorResponse("CART_LOAD_FAILED", "Unable to load the cart right now. Please try again.", traceId));
        }
    }

    [HttpPost]
    [ProducesResponseType(typeof(CommerceHubCartStateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> SaveCart(
        [FromBody] CommerceHubCartUpsertRequestDto request,
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

        try
        {
            var owner = await ResolveOwnerContextAsync(cancellationToken);
            var response = await _cartService.SaveCartAsync(request, owner, traceId, cancellationToken);
            return Ok(response);
        }
        catch (CommerceHubCartException ex)
        {
            _logger.LogWarning(
                "CommerceHub cart save blocked | Code: {Code} | TraceId: {TraceId}",
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
            _logger.LogError(ex, "CommerceHub cart save failed unexpectedly | TraceId: {TraceId}", traceId);
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new ApiErrorResponse("CART_SAVE_FAILED", "Unable to save the cart right now. Please try again.", traceId));
        }
    }

    [HttpDelete]
    [ProducesResponseType(typeof(CommerceHubCartStateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ClearCart(CancellationToken cancellationToken)
    {
        var traceId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;

        try
        {
            var owner = await ResolveOwnerContextAsync(cancellationToken);
            var response = await _cartService.ClearCartAsync(owner, traceId, cancellationToken);
            return Ok(response);
        }
        catch (CommerceHubCartException ex)
        {
            _logger.LogWarning(
                "CommerceHub cart clear blocked | Code: {Code} | TraceId: {TraceId}",
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
            _logger.LogError(ex, "CommerceHub cart clear failed unexpectedly | TraceId: {TraceId}", traceId);
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new ApiErrorResponse("CART_CLEAR_FAILED", "Unable to clear the cart right now. Please try again.", traceId));
        }
    }

    [HttpPost("price")]
    [ProducesResponseType(typeof(CommerceHubCartPricingResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Calculate(
        [FromBody] CommerceHubCartPricingRequestDto request,
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

        try
        {
            var owner = await ResolveOwnerContextAsync(cancellationToken);
            var promoRateLimitError = await EnforcePromoRateLimitAsync(request.PromoCode, owner, traceId);
            if (promoRateLimitError != null)
            {
                return promoRateLimitError;
            }

            var response = await _cartService.CalculateAsync(request, owner, traceId, cancellationToken);
            return Ok(response);
        }
        catch (CommerceHubCheckoutException ex)
        {
            _logger.LogWarning(
                "CommerceHub cart pricing blocked | Code: {Code} | TraceId: {TraceId}",
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
            _logger.LogError(ex, "CommerceHub cart pricing failed unexpectedly | TraceId: {TraceId}", traceId);
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new ApiErrorResponse("CART_PRICING_FAILED", "Unable to calculate cart totals right now. Please try again.", traceId));
        }
    }

    private async Task<CommerceHubCartOwnerContext> ResolveOwnerContextAsync(CancellationToken cancellationToken)
    {
        var authenticationResult = await HttpContext.AuthenticateAsync(CustomerSecurityConstants.AuthenticationScheme);
        if (!authenticationResult.Succeeded || authenticationResult.Principal == null)
        {
            return CommerceHubCartOwnerContext.Guest;
        }

        var customerIdClaim = authenticationResult.Principal.FindFirst("CustomerId")?.Value;

        if (!Guid.TryParse(customerIdClaim, out var customerId) || customerId == Guid.Empty)
        {
            return CommerceHubCartOwnerContext.Guest;
        }

        cancellationToken.ThrowIfCancellationRequested();

        return new CommerceHubCartOwnerContext
        {
            IsAuthenticated = true,
            TenantId = CommerceHubCartOwnerContext.CommerceHubCartTenantScopeId,
            CommerceHubCustomerId = customerId,
            OnlineCustomerId = customerId
        };
    }

    private async Task<IActionResult?> EnforcePromoRateLimitAsync(
        string? promoCode,
        CommerceHubCartOwnerContext owner,
        string traceId)
    {
        if (string.IsNullOrWhiteSpace(promoCode))
        {
            return null;
        }

        var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var ownerScope = owner.IsAuthenticated && owner.CommerceHubCustomerId.HasValue
            ? $"customer:{owner.CommerceHubCustomerId.Value:N}"
            : $"ip:{clientIp}";
        var endpoint = $"commercehub-promo-preview:{ownerScope}";

        var result = await _rateLimitService.CheckRateLimitAsync(clientIp, endpoint, limit: 20, windowSeconds: 300);
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
}
