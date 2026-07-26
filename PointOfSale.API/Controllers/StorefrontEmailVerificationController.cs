using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Services.Storefront;

namespace PointOfSale.API.Controllers;

/// <summary>
/// Storefront Email Verification Controller.
/// 
/// ARCHITECTURAL RULES:
/// - Storefront ONLY - does NOT affect POS users
/// - PostgreSQL is the source of truth
/// - Rate-limited resend functionality
/// - Tenant isolation enforced
/// 
/// Route: /api/storefront/auth
/// 
/// Phase: 10.3.2.5 - Storefront Email Verification Flow
/// </summary>
[ApiController]
[Route("api/storefront/auth")]
public class StorefrontEmailVerificationController : ControllerBase
{
    private readonly IStorefrontEmailVerificationService _verificationService;
    private readonly ILogger<StorefrontEmailVerificationController> _logger;
    private const string TenantIdHeader = "X-Tenant-Id";

    public StorefrontEmailVerificationController(
        IStorefrontEmailVerificationService verificationService,
        ILogger<StorefrontEmailVerificationController> logger)
    {
        _verificationService = verificationService;
        _logger = logger;
    }

    /// <summary>
    /// Verify email using the token from the verification link.
    /// Public endpoint - no authentication required.
    /// </summary>
    /// <param name="request">Verification token</param>
    /// <returns>Verification result</returns>
    /// <response code="200">Email verified successfully</response>
    /// <response code="400">Invalid or expired token</response>
    [HttpPost("verify-email")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(VerifyEmailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> VerifyEmail(
        [FromBody] VerifyEmailRequest request,
        CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();

        // Validate tenant header
        var tenantValidation = ValidateTenantHeader(traceId);
        if (tenantValidation.Error != null)
            return tenantValidation.Error;

        var tenantId = tenantValidation.TenantId!.Value;

        if (string.IsNullOrWhiteSpace(request.Token))
        {
            return BadRequest(new ApiErrorResponse(
                "INVALID_TOKEN",
                "Verification token is required",
                traceId));
        }

        _logger.LogInformation(
            "[EmailVerification] {Action} endpoint called | TenantId: {TenantId} | TraceId: {TraceId}",
            "VERIFY_EMAIL",
            tenantId,
            traceId);

        var result = await _verificationService.VerifyEmailAsync(tenantId, request.Token, cancellationToken);

        if (!result.Success)
        {
            return BadRequest(new ApiErrorResponse(
                result.Error ?? "VERIFICATION_FAILED",
                result.Message ?? "Verification failed",
                traceId));
        }

        return Ok(new VerifyEmailResponse
        {
            Success = true,
            Message = result.Message ?? "Email verified successfully"
        });
    }

    /// <summary>
    /// Resend verification email.
    /// Requires authenticated Storefront customer JWT.
    /// Rate-limited: max 3 attempts per hour.
    /// </summary>
    /// <returns>Resend result</returns>
    /// <response code="200">Verification email resent</response>
    /// <response code="400">Already verified or error</response>
    /// <response code="401">Not authenticated</response>
    /// <response code="429">Rate limited</response>
    [HttpPost("resend-verification")]
    [Authorize(AuthenticationSchemes = "StorefrontJwt")]
    [ProducesResponseType(typeof(VerifyEmailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ResendVerification(CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();

        // Extract CustomerId and TenantId from JWT claims
        var customerIdClaim = User.FindFirst("CustomerId")?.Value;
        var tenantIdClaim = User.FindFirst("TenantId")?.Value;

        if (!Guid.TryParse(customerIdClaim, out var customerId) ||
            !Guid.TryParse(tenantIdClaim, out var tenantId))
        {
            return Unauthorized(new ApiErrorResponse(
                "INVALID_TOKEN",
                "Invalid authentication token",
                traceId));
        }

        _logger.LogInformation(
            "[EmailVerification] {Action} endpoint called | TenantId: {TenantId} | CustomerId: {CustomerId} | TraceId: {TraceId}",
            "RESEND_VERIFICATION",
            tenantId,
            customerId,
            traceId);

        var result = await _verificationService.RequestVerificationAsync(
            tenantId,
            customerId,
            isResend: true,
            cancellationToken);

        if (!result.Success)
        {
            if (result.Error == "RATE_LIMITED")
            {
                Response.Headers["Retry-After"] = result.RetryAfterSeconds?.ToString() ?? "3600";
                return StatusCode(StatusCodes.Status429TooManyRequests, new ApiErrorResponse(
                    result.Error,
                    result.Message ?? "Too many requests",
                    traceId));
            }

            return BadRequest(new ApiErrorResponse(
                result.Error ?? "RESEND_FAILED",
                result.Message ?? "Failed to resend verification",
                traceId));
        }

        return Ok(new VerifyEmailResponse
        {
            Success = true,
            Message = result.Message ?? "Verification email sent"
        });
    }

    /// <summary>
    /// Get email verification status.
    /// Requires authenticated Storefront customer JWT.
    /// </summary>
    /// <returns>Verification status</returns>
    [HttpGet("verification-status")]
    [Authorize(AuthenticationSchemes = "StorefrontJwt")]
    [ProducesResponseType(typeof(EmailVerificationStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetVerificationStatus(CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();

        // Extract CustomerId and TenantId from JWT claims
        var customerIdClaim = User.FindFirst("CustomerId")?.Value;
        var tenantIdClaim = User.FindFirst("TenantId")?.Value;

        if (!Guid.TryParse(customerIdClaim, out var customerId) ||
            !Guid.TryParse(tenantIdClaim, out var tenantId))
        {
            return Unauthorized(new ApiErrorResponse(
                "INVALID_TOKEN",
                "Invalid authentication token",
                traceId));
        }

        var status = await _verificationService.GetVerificationStatusAsync(tenantId, customerId, cancellationToken);
        return Ok(status);
    }

    #region Helper Methods

    private (Guid? TenantId, IActionResult? Error) ValidateTenantHeader(string traceId)
    {
        return PointOfSale.API.Helpers.StorefrontTenantContextResolver.ValidateTenant(this, traceId);
    }

    private string GetTraceId() => Activity.Current?.Id ?? HttpContext.TraceIdentifier;

    #endregion
}

#region DTOs

public class VerifyEmailRequest
{
    public string Token { get; set; } = string.Empty;
}

public class VerifyEmailResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}

#endregion
