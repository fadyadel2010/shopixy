using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.API.DTOs.Cart;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Security;
using PointOfSale.API.Services.Cart;
using PointOfSale.API.Services.Storefront;

namespace PointOfSale.API.Controllers;

/// <summary>
/// Storefront Shipping Methods Controller.
/// 
/// Route: GET /api/storefront/shipping-methods
/// 
/// PHASE 5.1: SHIPPING METHODS READ API
/// 
/// ARCHITECTURAL BOUNDARY (STRICT):
/// - Shipping Methods are managed ONLY in POS
/// - This API is READ-ONLY
/// - Only Active shipping methods are exposed
/// - Tenant isolation is mandatory
/// 
/// Required Headers:
/// - X-Tenant-Id: GUID (required)
/// </summary>
[ApiController]
[Route("api/storefront/shipping-methods")]
[AllowAnonymous]
public class StorefrontShippingMethodsController : ControllerBase
{
    private readonly ICartService _cartService;
    private readonly IShippingMethodService _shippingMethodService;
    private readonly IWarehouseResolutionService _warehouseResolutionService;
    private readonly ILogger<StorefrontShippingMethodsController> _logger;

    private const string TenantIdHeader = "X-Tenant-Id";
    private const string SessionIdHeader = "X-Session-Id";
    private const string StorefrontAuthenticationScheme = "StorefrontJwt";
    private const string CustomerAuthenticationScheme = CustomerSecurityConstants.AuthenticationScheme;

    public StorefrontShippingMethodsController(
        ICartService cartService,
        IShippingMethodService shippingMethodService,
        IWarehouseResolutionService warehouseResolutionService,
        ILogger<StorefrontShippingMethodsController> logger)
    {
        _cartService = cartService;
        _shippingMethodService = shippingMethodService;
        _warehouseResolutionService = warehouseResolutionService;
        _logger = logger;
    }

    /// <summary>
    /// Compatibility GET for server-admitted method presentation. The optional
    /// warehouse filter affects display only; selection and checkout always create
    /// and validate an opaque WarehouseResolution.
    /// 
    /// Response includes:
    /// - Shipping method details
    /// - Base rates and per-kg rates
    /// - Free shipping thresholds
    /// - Estimated delivery days
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of active shipping methods</returns>
    [HttpGet]
    [ProducesResponseType(typeof(ShippingMethodsListResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetShippingMethods([FromQuery] Guid? warehouseId, CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        SetNoStoreHeaders();

        // Validate tenant identity securely
        var tenantValidation = ValidateTenantHeader(traceId);
        if (tenantValidation.Error != null)
        {
            return tenantValidation.Error;
        }
        var tenantId = tenantValidation.TenantId!.Value;

        var ownerValidation = await ValidateHeadersAsync(tenantId, traceId);
        if (ownerValidation.Error != null)
        {
            return ownerValidation.Error;
        }

        try
        {
            var cart = await _cartService.GetCartAsync(
                tenantId,
                ownerValidation.Owner!,
                traceId,
                cancellationToken);

            var response = await _shippingMethodService.GetShippingMethodsAvailabilityAsync(
                tenantId,
                cart.Subtotal,
                cart.TotalWeight,
                traceId,
                warehouseId,
                cancellationToken);

            _logger.LogDebug(
                "GetShippingMethods | TenantId: {TenantId} | Status: {Status} | Count: {Count} | TraceId: {TraceId}",
                tenantId, response.Status, response.TotalCount, traceId);

            return Ok(response);
        }
        catch (UnauthorizedAccessException ex) when (string.Equals(ex.Message, TenantErrorCodes.TenantMismatch, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "GetShippingMethods rejected due to tenant mismatch | TenantId: {TenantId} | TraceId: {TraceId}",
                tenantId,
                traceId);

            return Unauthorized(new ApiErrorResponse(
                TenantErrorCodes.TenantMismatch,
                "Authenticated customer does not belong to the resolved tenant.",
                traceId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error getting shipping methods | TenantId: {TenantId} | TraceId: {TraceId}",
                tenantId, traceId);

            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                ApiErrorResponse.ServiceUnavailable(traceId));
        }
    }

    [HttpPost("preview")]
    [ProducesResponseType(typeof(ShippingMethodsPreviewResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> PreviewShippingMethods(
        [FromBody] ShippingMethodsPreviewRequestDto request,
        CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        SetNoStoreHeaders();

        var tenantValidation = ValidateTenantHeader(traceId);
        if (tenantValidation.Error != null)
        {
            return tenantValidation.Error;
        }
        var tenantId = tenantValidation.TenantId!.Value;

        var ownerValidation = await ValidateHeadersAsync(tenantId, traceId);
        if (ownerValidation.Error != null)
        {
            return ownerValidation.Error;
        }

        if (request == null)
        {
            return BadRequest(new ApiErrorResponse("INVALID_REQUEST", "Request body is required", traceId));
        }

        var (errorCode, errorMessage) = request.Validate();
        if (errorCode != null)
        {
            return BadRequest(new ApiErrorResponse(errorCode, errorMessage!, traceId));
        }

        try
        {
            var cart = await _warehouseResolutionService.LoadPreviewCartAsync(
                tenantId,
                ownerValidation.Owner!,
                request.CheckoutSessionId,
                traceId,
                cancellationToken);

            var availability = await _shippingMethodService.GetShippingMethodsAvailabilityForConfiguredWarehousesAsync(
                tenantId,
                cart.Subtotal,
                cart.TotalWeight,
                cart.Items,
                traceId,
                cancellationToken);

            return Ok(new ShippingMethodsPreviewResponseDto
            {
                TenantId = tenantId,
                Status = "PREVIEW",
                ShippingMethodsStatus = availability.Status,
                ShippingMethods = availability.ShippingMethods
            });
        }
        catch (WarehouseResolutionException ex)
        {
            _logger.LogWarning(
                "Shipping method preview warehouse resolution failed | TenantId: {TenantId} | ErrorCode: {ErrorCode} | TraceId: {TraceId}",
                tenantId,
                ex.ErrorCode,
                traceId);
            return StatusCode(ex.StatusCode, new ApiErrorResponse(ex.ErrorCode, ex.Message, traceId));
        }
        catch (UnauthorizedAccessException ex) when (string.Equals(ex.Message, TenantErrorCodes.TenantMismatch, StringComparison.Ordinal))
        {
            return Unauthorized(new ApiErrorResponse(
                TenantErrorCodes.TenantMismatch,
                "Authenticated customer does not belong to the resolved tenant.",
                traceId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error previewing shipping methods | TenantId: {TenantId} | TraceId: {TraceId}",
                tenantId, traceId);

            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new ApiErrorResponse(
                    WarehouseResolutionErrorCodes.WarehouseResolutionServiceUnavailable,
                    "Shipping methods are temporarily unavailable.",
                    traceId));
        }
    }

    [HttpPost("select")]
    [ProducesResponseType(typeof(ShippingMethodSelectionResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> SelectShippingMethod(
        [FromBody] ShippingMethodSelectionRequestDto request,
        CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        SetNoStoreHeaders();

        var tenantValidation = ValidateTenantHeader(traceId);
        if (tenantValidation.Error != null)
        {
            return tenantValidation.Error;
        }
        var tenantId = tenantValidation.TenantId!.Value;

        var ownerValidation = await ValidateHeadersAsync(tenantId, traceId);
        if (ownerValidation.Error != null)
        {
            return ownerValidation.Error;
        }

        if (request == null)
        {
            return BadRequest(new ApiErrorResponse("INVALID_REQUEST", "Request body is required", traceId));
        }

        var (errorCode, errorMessage) = request.Validate();
        if (errorCode != null)
        {
            return BadRequest(new ApiErrorResponse(errorCode, errorMessage!, traceId));
        }

        try
        {
            var resolution = await _warehouseResolutionService.ResolveForSelectedShippingMethodAsync(
                tenantId,
                ownerValidation.Owner!,
                request.CheckoutSessionId,
                request.ShippingMethodId,
                traceId,
                cancellationToken);

            return Ok(new ShippingMethodSelectionResponseDto
            {
                TenantId = tenantId,
                ShippingMethodId = request.ShippingMethodId,
                WarehouseResolutionId = resolution.Resolution.Id,
                ResolutionVersion = resolution.Resolution.ResolutionVersion,
                Status = resolution.Resolution.Status.ToString().ToUpperInvariant(),
                ExpiresAtUtc = resolution.Resolution.ExpiresAtUtc
            });
        }
        catch (WarehouseResolutionException ex)
        {
            _logger.LogWarning(
                "Shipping method selection warehouse resolution failed | TenantId: {TenantId} | ShippingMethodId: {ShippingMethodId} | ErrorCode: {ErrorCode} | TraceId: {TraceId}",
                tenantId,
                request.ShippingMethodId,
                ex.ErrorCode,
                traceId);
            return StatusCode(ex.StatusCode, new ApiErrorResponse(ex.ErrorCode, ex.Message, traceId));
        }
        catch (UnauthorizedAccessException ex) when (string.Equals(ex.Message, TenantErrorCodes.TenantMismatch, StringComparison.Ordinal))
        {
            _logger.LogWarning(ex,
                "SelectShippingMethod rejected due to tenant mismatch | TenantId: {TenantId} | TraceId: {TraceId}",
                tenantId,
                traceId);
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "SelectShippingMethod failed | TenantId: {TenantId} | TraceId: {TraceId}",
                tenantId,
                traceId);
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                ApiErrorResponse.ServiceUnavailable(traceId));
        }
    }

    #region Helper Methods

    /// <summary>
    /// Validates tenant identity by prioritizing TenantContext (trusted source) over X-Tenant-Id header.
    /// SECURITY: Prevents tenant spoofing by validating header against middleware-resolved context.
    /// </summary>
    private (Guid? TenantId, IActionResult? Error) ValidateTenantHeader(string traceId)
    {
        return PointOfSale.API.Helpers.StorefrontTenantContextResolver.ValidateTenant(this, traceId);
    }

    private async Task<(CartOwner? Owner, IActionResult? Error)> ValidateHeadersAsync(Guid tenantId, string traceId)
    {
        var sessionId = Request.Headers.TryGetValue(SessionIdHeader, out var sessionHeaderValue) &&
                        !string.IsNullOrWhiteSpace(sessionHeaderValue)
            ? sessionHeaderValue.ToString()!.Trim()
            : null;

        var authenticatedCustomer = await ResolveAuthenticatedCustomerAsync();
        if (authenticatedCustomer.CustomerId.HasValue)
        {
            var tenantClaimError = ValidateAuthenticatedTenantClaim(
                authenticatedCustomer.Principal,
                tenantId,
                traceId);
            if (tenantClaimError != null)
            {
                return (null, tenantClaimError);
            }

            return (new CartOwner
            {
                OwnerType = "customer",
                OwnerId = authenticatedCustomer.CustomerId.Value.ToString("N")
            }, null);
        }

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return (null, BadRequest(new ApiErrorResponse(
                "SESSION_HEADER_MISSING",
                "X-Session-Id header is required for guest requests",
                traceId)));
        }

        return (new CartOwner
        {
            OwnerType = "session",
            OwnerId = sessionId
        }, null);
    }

    private async Task<ShippingMethodCallerIdentity> ResolveAuthenticatedCustomerAsync()
    {
        var customerAuthenticationResult = await HttpContext.AuthenticateAsync(CustomerAuthenticationScheme);
        if (TryGetAuthenticatedCustomerId(customerAuthenticationResult.Principal, out var customerId))
        {
            return new ShippingMethodCallerIdentity(customerAuthenticationResult.Principal, customerId);
        }

        var storefrontAuthenticationResult = await HttpContext.AuthenticateAsync(StorefrontAuthenticationScheme);
        if (TryGetAuthenticatedCustomerId(storefrontAuthenticationResult.Principal, out customerId))
        {
            return new ShippingMethodCallerIdentity(storefrontAuthenticationResult.Principal, customerId);
        }

        return new ShippingMethodCallerIdentity(null, null);
    }

    private IActionResult? ValidateAuthenticatedTenantClaim(
        ClaimsPrincipal? principal,
        Guid tenantId,
        string traceId)
    {
        var tenantIdClaim = principal?.FindFirst("TenantId")?.Value;
        if (string.IsNullOrWhiteSpace(tenantIdClaim) ||
            !Guid.TryParse(tenantIdClaim, out var claimTenantId))
        {
            return null;
        }

        if (claimTenantId == tenantId)
        {
            return null;
        }

        _logger.LogWarning(
            "X-Tenant-Id header mismatch with authenticated claim | Header: {Header} | Claim: {Claim} | TraceId: {TraceId}",
            tenantId,
            claimTenantId,
            traceId);
        return Unauthorized(new ApiErrorResponse(
            TenantErrorCodes.TenantMismatch,
            "X-Tenant-Id header does not match authenticated tenant",
            traceId));
    }

    private static bool TryGetAuthenticatedCustomerId(ClaimsPrincipal? principal, out Guid customerId)
    {
        customerId = Guid.Empty;

        if (principal?.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        return Guid.TryParse(
            principal.FindFirst("CustomerId")?.Value
                ?? principal.FindFirst("customer_id")?.Value
                ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value,
            out customerId) &&
            customerId != Guid.Empty;
    }

    private string GetTraceId()
    {
        return Activity.Current?.Id ?? HttpContext.TraceIdentifier;
    }

    private void SetNoStoreHeaders()
    {
        Response.Headers["Cache-Control"] = "no-store, no-cache, max-age=0";
        Response.Headers["Pragma"] = "no-cache";
        Response.Headers["Expires"] = "0";
    }

    private sealed record ShippingMethodCallerIdentity(ClaimsPrincipal? Principal, Guid? CustomerId);

    #endregion
}
