using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Security;

namespace PointOfSale.API.Services.Payments;

public interface IPaymentRequestAuthorizationService
{
    Task<PaymentRequestAuthorizationOutcome> AuthorizeOrderAccessAsync(
        HttpContext httpContext,
        Guid tenantId,
        Guid orderId,
        string traceId,
        string? checkoutToken = null,
        CancellationToken cancellationToken = default);
}

public sealed record PaymentRequestAuthorizationOutcome(
    bool Authorized,
    Guid? AuthenticatedCustomerId,
    PaymentOrderAccessInfo? OrderAccessInfo,
    int? FailureStatusCode,
    ApiErrorResponse? Error)
{
    public static PaymentRequestAuthorizationOutcome Success(
        Guid? authenticatedCustomerId,
        PaymentOrderAccessInfo orderAccessInfo) =>
        new(
            true,
            authenticatedCustomerId,
            orderAccessInfo,
            null,
            null);

    public static PaymentRequestAuthorizationOutcome Failure(
        int statusCode,
        string code,
        string message,
        string traceId) =>
        new(
            false,
            null,
            null,
            statusCode,
            new ApiErrorResponse(code, message, traceId));

    public IActionResult ToActionResult()
    {
        if (Authorized || FailureStatusCode == null || Error == null)
        {
            throw new InvalidOperationException("Only authorization failures can be converted to action results.");
        }

        return FailureStatusCode.Value switch
        {
            StatusCodes.Status400BadRequest => new BadRequestObjectResult(Error),
            StatusCodes.Status404NotFound => new NotFoundObjectResult(Error),
            StatusCodes.Status409Conflict => new ConflictObjectResult(Error),
            _ => new ObjectResult(Error)
            {
                StatusCode = FailureStatusCode
            }
        };
    }
}

public sealed class PaymentRequestAuthorizationService : IPaymentRequestAuthorizationService
{
    private const string SessionIdHeader = "X-Session-Id";
    private const string CheckoutTokenHeader = "X-Checkout-Token";
    private const string StorefrontAuthenticationScheme = "StorefrontJwt";
    private const string CustomerAuthenticationScheme = CustomerSecurityConstants.AuthenticationScheme;

    private readonly IPaymentService _paymentService;
    private readonly ICheckoutSessionTokenService _checkoutSessionTokenService;
    private readonly IAuthenticationService _authenticationService;
    private readonly ILogger<PaymentRequestAuthorizationService> _logger;

    public PaymentRequestAuthorizationService(
        IPaymentService paymentService,
        ICheckoutSessionTokenService checkoutSessionTokenService,
        IAuthenticationService authenticationService,
        ILogger<PaymentRequestAuthorizationService> logger)
    {
        _paymentService = paymentService;
        _checkoutSessionTokenService = checkoutSessionTokenService;
        _authenticationService = authenticationService;
        _logger = logger;
    }

    public async Task<PaymentRequestAuthorizationOutcome> AuthorizeOrderAccessAsync(
        HttpContext httpContext,
        Guid tenantId,
        Guid orderId,
        string traceId,
        string? checkoutToken = null,
        CancellationToken cancellationToken = default)
    {
        PaymentOrderAccessInfo orderAccess;

        try
        {
            orderAccess = await _paymentService.GetOrderAccessInfoAsync(
                tenantId,
                orderId,
                cancellationToken);
        }
        catch (PaymentOrderNotFoundException ex)
        {
            return PaymentRequestAuthorizationOutcome.Failure(
                StatusCodes.Status404NotFound,
                ex.ErrorCode,
                ex.Message,
                traceId);
        }

        if (orderAccess.RequiresGuestSessionValidation)
        {
            var sessionId = GetHeaderValue(httpContext, SessionIdHeader);
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                _logger.LogWarning(
                    "Guest payment access blocked: missing {Header} | TenantId: {TenantId} | OrderId: {OrderId} | TraceId: {TraceId}",
                    SessionIdHeader,
                    tenantId,
                    orderId,
                    traceId);

                return PaymentRequestAuthorizationOutcome.Failure(
                    StatusCodes.Status400BadRequest,
                    "SESSION_HEADER_MISSING",
                    "X-Session-Id header is required for guest order access.",
                    traceId);
            }

            if (string.IsNullOrWhiteSpace(orderAccess.SessionId) ||
                !string.Equals(sessionId, orderAccess.SessionId, StringComparison.Ordinal))
            {
                _logger.LogWarning(
                    "[SECURITY] Guest payment access denied due to session mismatch | TenantId: {TenantId} | OrderId: {OrderId} | TraceId: {TraceId}",
                    tenantId,
                    orderId,
                    traceId);

                return PaymentRequestAuthorizationOutcome.Failure(
                    StatusCodes.Status403Forbidden,
                    "ORDER_ACCESS_DENIED",
                    "You do not have permission to access this order.",
                    traceId);
            }

            if (string.IsNullOrWhiteSpace(checkoutToken))
            {
                checkoutToken = GetHeaderValue(httpContext, CheckoutTokenHeader)
                    ?? GuestOrderAccessCookie.ReadCheckoutToken(httpContext.Request);
            }
            
            if (string.IsNullOrWhiteSpace(checkoutToken))
            {
                _logger.LogWarning(
                    "Guest payment access blocked: missing guest checkout token | TenantId: {TenantId} | OrderId: {OrderId} | TraceId: {TraceId}",
                    tenantId,
                    orderId,
                    traceId);

                return PaymentRequestAuthorizationOutcome.Failure(
                    StatusCodes.Status400BadRequest,
                    "CHECKOUT_TOKEN_MISSING",
                    "A valid guest checkout token is required for guest order access.",
                    traceId);
            }

            var tokenValidation = _checkoutSessionTokenService.ValidatePaymentToken(checkoutToken, tenantId);
            if (!tokenValidation.IsValid ||
                tokenValidation.IsExpired ||
                !Guid.TryParse(tokenValidation.Subject, out var tokenOrderId) ||
                tokenOrderId != orderId)
            {
                _logger.LogWarning(
                    "[SECURITY] Guest payment access denied due to checkout token validation failure | TenantId: {TenantId} | OrderId: {OrderId} | TokenExpired: {TokenExpired} | TraceId: {TraceId}",
                    tenantId,
                    orderId,
                    tokenValidation.IsExpired,
                    traceId);

                return PaymentRequestAuthorizationOutcome.Failure(
                    StatusCodes.Status403Forbidden,
                    "ORDER_ACCESS_DENIED",
                    "You do not have permission to access this order.",
                    traceId);
            }

            return PaymentRequestAuthorizationOutcome.Success(null, orderAccess);
        }

        var authenticatedCustomerId = await ResolveAuthenticatedCustomerIdAsync(httpContext);
        if (authenticatedCustomerId.HasValue && orderAccess.MatchesCustomer(authenticatedCustomerId.Value))
        {
            return PaymentRequestAuthorizationOutcome.Success(authenticatedCustomerId, orderAccess);
        }

        _logger.LogWarning(
            "[SECURITY] Payment order access denied | TenantId: {TenantId} | OrderId: {OrderId} | AuthenticatedCustomerId: {AuthenticatedCustomerId} | TraceId: {TraceId}",
            tenantId,
            orderId,
            authenticatedCustomerId,
            traceId);

        return PaymentRequestAuthorizationOutcome.Failure(
            StatusCodes.Status403Forbidden,
            "ORDER_ACCESS_DENIED",
            "You do not have permission to access this order.",
            traceId);
    }

    private async Task<Guid?> ResolveAuthenticatedCustomerIdAsync(HttpContext httpContext)
    {
        var customerAuthenticationResult = await _authenticationService.AuthenticateAsync(
            httpContext,
            CustomerAuthenticationScheme);
        if (TryGetAuthenticatedCustomerId(customerAuthenticationResult.Principal, out var customerId))
        {
            return customerId;
        }

        var storefrontAuthenticationResult = await _authenticationService.AuthenticateAsync(
            httpContext,
            StorefrontAuthenticationScheme);

        return TryGetAuthenticatedCustomerId(storefrontAuthenticationResult.Principal, out customerId)
            ? customerId
            : null;
    }

    private static string? GetHeaderValue(HttpContext httpContext, string headerName)
    {
        if (!httpContext.Request.Headers.TryGetValue(headerName, out var headerValue) ||
            string.IsNullOrWhiteSpace(headerValue))
        {
            return null;
        }

        return headerValue.ToString().Trim();
    }

    private static bool TryGetAuthenticatedCustomerId(ClaimsPrincipal? principal, out Guid customerId)
    {
        customerId = Guid.Empty;

        if (principal?.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        var customerIdClaim = principal.FindFirst("CustomerId")?.Value
            ?? principal.FindFirst("customer_id")?.Value
            ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return Guid.TryParse(customerIdClaim, out customerId);
    }
}
