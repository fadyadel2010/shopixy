using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Services.Storefront;
using PointOfSale.Core.Helpers;

namespace PointOfSale.API.Controllers;

/// <summary>
/// Storefront Online Customer Controller - Read-only customer profile endpoints.
/// 
/// ARCHITECTURAL RULES:
/// - READ-ONLY operations only
/// - No writing to PostgreSQL from Storefront
/// - No access to POS customers table
/// - Tenant isolation via X-Tenant-Id header
/// 
/// SECURITY NOTES:
/// - IsActive check: Deactivated customers are rejected
/// - TODO (Phase 10.3.2): JWT middleware will handle authentication
/// - TODO (Phase 10.3.2): JWT must be rejected if IsActive = false
/// 
/// Route: /api/storefront/customer
/// 
/// Phase: 10.3.1 - Online Customer Identity (Core)
/// Phase: 10.3.1 Hardening - Security & Observability
/// </summary>
[ApiController]
[Route("api/storefront/customer")]
public class StorefrontOnlineCustomerController : ControllerBase
{
    private const string GenericRecoveryMessage = "If the email exists, we sent instructions.";

    private readonly IStorefrontOnlineCustomerService _customerService;
    private readonly IStorefrontCaptchaGuard _captchaGuard;
    private readonly ILogger<StorefrontOnlineCustomerController> _logger;
    private const string TenantIdHeader = "X-Tenant-Id";

    public StorefrontOnlineCustomerController(
        IStorefrontOnlineCustomerService customerService,
        IStorefrontCaptchaGuard captchaGuard,
        ILogger<StorefrontOnlineCustomerController> logger)
    {
        _customerService = customerService;
        _captchaGuard = captchaGuard;
        _logger = logger;
    }

    /// <summary>
    /// Gets the current customer's profile.
    /// Requires authenticated Storefront customer JWT.
    /// </summary>
    /// <returns>Customer profile if found</returns>
    /// <response code="200">Profile returned successfully</response>
    /// <response code="400">Missing or invalid tenant/customer headers</response>
    /// <response code="401">Customer not authenticated or deactivated</response>
    /// <response code="404">Customer not found</response>
    /// <response code="503">Service temporarily unavailable</response>
    [HttpGet("profile")]
    [Authorize(AuthenticationSchemes = "StorefrontJwt")]
    [ProducesResponseType(typeof(OnlineCustomerProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetProfile(CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();

        // Extract CustomerId and TenantId from JWT claims (populated by StorefrontJwt authentication)
        var customerIdClaim = User.FindFirst("CustomerId")?.Value;
        var tenantIdClaim = User.FindFirst("TenantId")?.Value;

        if (!Guid.TryParse(customerIdClaim, out var customerId) ||
            !Guid.TryParse(tenantIdClaim, out var tenantId))
        {
            _logger.LogWarning(
                "[StorefrontCustomer] {Action} - Invalid JWT claims | TraceId: {TraceId}",
                "GET_PROFILE",
                traceId);
            return Unauthorized(new ApiErrorResponse(
                "INVALID_TOKEN",
                "Invalid authentication token",
                traceId));
        }

        _logger.LogInformation(
            "[StorefrontCustomer] {Action} request | TenantId: {TenantId} | CustomerId: {CustomerId} | TraceId: {TraceId}",
            "GET_PROFILE",
            tenantId,
            customerId,
            traceId);

        try
        {
            var profile = await _customerService.GetProfileAsync(tenantId, customerId, cancellationToken);

            if (profile == null)
            {
                _logger.LogWarning(
                    "[StorefrontCustomer] {Action} - Customer not found | TenantId: {TenantId} | CustomerId: {CustomerId} | TraceId: {TraceId}",
                    "GET_PROFILE",
                    tenantId,
                    customerId,
                    traceId);
                return NotFound(new ApiErrorResponse(
                    "CUSTOMER_NOT_FOUND",
                    "Customer profile not found",
                    traceId));
            }

            // CRITICAL: Deactivated customers must be blocked
            // This is a defense-in-depth check - JWT middleware should also check this
            // TODO (Phase 10.3.2): JWT middleware must check IsActive and reject tokens for deactivated customers
            if (!profile.IsActive)
            {
                _logger.LogWarning(
                    "[StorefrontCustomer] {Action} - BLOCKED: Customer account deactivated | TenantId: {TenantId} | CustomerId: {CustomerId} | TraceId: {TraceId}",
                    "GET_PROFILE",
                    tenantId,
                    customerId,
                    traceId);
                return Unauthorized(new ApiErrorResponse(
                    "CUSTOMER_DEACTIVATED",
                    "Customer account has been deactivated",
                    traceId));
            }

            _logger.LogInformation(
                "[StorefrontCustomer] {Action} success | TenantId: {TenantId} | CustomerId: {CustomerId} | TraceId: {TraceId}",
                "GET_PROFILE",
                tenantId,
                customerId,
                traceId);

            return Ok(profile);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[StorefrontCustomer] {Action} failed | TenantId: {TenantId} | CustomerId: {CustomerId} | TraceId: {TraceId}",
                "GET_PROFILE",
                tenantId,
                customerId,
                traceId);

            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                ApiErrorResponse.ServiceUnavailable(traceId));
        }
    }

    /// <summary>
    /// Returns a generic recovery message without revealing whether the email exists.
    /// Public endpoint hardened against email enumeration.
    /// </summary>
    /// <param name="request">Account recovery request containing email</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Generic recovery message</returns>
    [HttpPost("email-exists")]
    [EnableRateLimiting("PublicReadRateLimit")]
    [ProducesResponseType(typeof(AccountRecoveryMessageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> CheckEmailExists(
        [FromBody] AccountRecoveryRequest request,
        CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        var genericResponse = new AccountRecoveryMessageResponse
        {
            Message = GenericRecoveryMessage
        };

        // Validate tenant header
        var tenantValidation = ValidateTenantHeader(traceId);
        if (tenantValidation.Error != null)
            return tenantValidation.Error;

        var tenantId = tenantValidation.TenantId!.Value;

        if (request == null || string.IsNullOrWhiteSpace(request.Email))
        {
            _logger.LogDebug(
                "[StorefrontCustomer] {Action} - Empty email | TenantId: {TenantId} | TraceId: {TraceId}",
                "EMAIL_EXISTS",
                tenantId,
                traceId);
            return BadRequest(new ApiErrorResponse(
                "INVALID_EMAIL",
                "Email is required",
                traceId));
        }

        var email = request.Email;

        _logger.LogDebug(
            "[StorefrontCustomer] {Action} request | TenantId: {TenantId} | Email: {Email} | TraceId: {TraceId}",
            "EMAIL_EXISTS",
            tenantId,
            MaskEmail(email),
            traceId);

        var captchaResult = await _captchaGuard.ValidateAsync(HttpContext, cancellationToken);
        if (!captchaResult.IsValid)
        {
            _logger.LogWarning(
                "[StorefrontCustomer] {Action} blocked by CAPTCHA guard | TenantId: {TenantId} | TraceId: {TraceId} | ErrorCode: {ErrorCode}",
                "EMAIL_EXISTS",
                tenantId,
                traceId,
                captchaResult.ErrorCode);

            if (string.Equals(captchaResult.ErrorCode, "CAPTCHA_PROVIDER_NOT_CONFIGURED", StringComparison.Ordinal))
            {
                return StatusCode(
                    StatusCodes.Status503ServiceUnavailable,
                    new ApiErrorResponse(
                        captchaResult.ErrorCode!,
                        captchaResult.ErrorMessage ?? "CAPTCHA validation is unavailable.",
                        traceId));
            }

            return BadRequest(new ApiErrorResponse(
                captchaResult.ErrorCode ?? "CAPTCHA_REQUIRED",
                captchaResult.ErrorMessage ?? "CAPTCHA validation is required.",
                traceId));
        }

        return Ok(genericResponse);
    }

    #region Helper Methods

    private (Guid? TenantId, IActionResult? Error) ValidateTenantHeader(string traceId)
    {
        return PointOfSale.API.Helpers.StorefrontTenantContextResolver.ValidateTenant(this, traceId);
    }

    private string GetTraceId() => Activity.Current?.Id ?? HttpContext.TraceIdentifier;

    /// <summary>
    /// Masks email for logging to prevent PII leakage.
    /// Example: "user@example.com" -> "u***@example.com"
    /// </summary>
    private static string MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return "[empty]";

        var atIndex = email.IndexOf('@');
        if (atIndex <= 1)
            return "***@" + (atIndex >= 0 ? email[(atIndex + 1)..] : "***");

        return email[0] + "***" + email[(atIndex - 1)..];
    }

    #endregion
}

public class AccountRecoveryRequest
{
    public string Email { get; set; } = string.Empty;
}

public class AccountRecoveryMessageResponse
{
    public string Message { get; set; } = string.Empty;
}
