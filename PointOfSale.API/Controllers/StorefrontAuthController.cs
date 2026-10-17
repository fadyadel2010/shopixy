using System.Diagnostics;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Infrastructure.Http;
using PointOfSale.API.Models;
using PointOfSale.API.Security;
using PointOfSale.API.Services.Storefront;

namespace PointOfSale.API.Controllers;

/// <summary>
/// Storefront Authentication Controller - Customer registration and login.
///
/// ARCHITECTURAL RULES:
/// - COMPLETELY SEPARATE from POS AuthController
/// - Uses OnlineCustomer entity (NOT User entity)
/// - Separate JWT secret/issuer/audience
/// - Storefront JWTs CANNOT be used for POS APIs
/// - Tenant isolation via X-Tenant-Id header
///
/// SECURITY (Phase 10.3.x Hardening):
/// - Rate limiting on login attempts
/// - HttpOnly cookies for refresh tokens (XSS protection)
/// - JWT blacklist for revoked tokens
///
/// Route: /api/storefront/auth
///
/// Phase: 10.3.2 - Storefront Login / Register
/// Phase: 10.3.3 - Social Login (Google / Facebook)
/// Phase: 10.3.4 - Refresh Token Rotation
/// Phase: 10.3.x - Authentication Hardening
/// </summary>
[ApiController]
[Route("api/storefront/auth")]
public class StorefrontAuthController : ControllerBase
{
    private readonly IStorefrontAuthService _authService;
    private readonly IStorefrontRefreshTokenService _refreshTokenService;
    private readonly IStorefrontSocialLoginService _socialLoginService;
    private readonly IStorefrontPasswordResetService _passwordResetService;
    private readonly IStorefrontAuthRateLimiter _rateLimiter;
    private readonly IStorefrontCookieHelper _cookieHelper;
    private readonly IStorefrontJwtBlacklist _jwtBlacklist;
    private readonly StorefrontJwtSettings _jwtSettings;
    private readonly ILogger<StorefrontAuthController> _logger;
    private const string TenantIdHeader = "X-Tenant-Id";

    public StorefrontAuthController(
        IStorefrontAuthService authService,
        IStorefrontRefreshTokenService refreshTokenService,
        IStorefrontSocialLoginService socialLoginService,
        IStorefrontPasswordResetService passwordResetService,
        IStorefrontAuthRateLimiter rateLimiter,
        IStorefrontCookieHelper cookieHelper,
        IStorefrontJwtBlacklist jwtBlacklist,
        IOptions<StorefrontJwtSettings> jwtSettings,
        ILogger<StorefrontAuthController> logger)
    {
        _authService = authService;
        _refreshTokenService = refreshTokenService;
        _socialLoginService = socialLoginService;
        _passwordResetService = passwordResetService;
        _rateLimiter = rateLimiter;
        _cookieHelper = cookieHelper;
        _jwtBlacklist = jwtBlacklist;
        _jwtSettings = jwtSettings.Value;
        _logger = logger;
    }

    /// <summary>
    /// Register a new customer account.
    /// </summary>
    /// <param name="request">Registration details</param>
    /// <returns>Registration confirmation and customer info</returns>
    /// <response code="200">Registration successful</response>
    /// <response code="400">Invalid request or email already exists</response>
    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting("PublicRegisterRateLimit")]
    [ProducesResponseType(typeof(StorefrontAuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Register(
        [FromBody] StorefrontRegisterRequest request,
        CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();

        // Validate tenant header
        var tenantValidation = ValidateTenantHeader(traceId);
        if (tenantValidation.Error != null)
            return tenantValidation.Error;

        var tenantId = tenantValidation.TenantId!.Value;

        _logger.LogInformation(
            "[StorefrontAuth] {Action} endpoint called | TenantId: {TenantId} | TraceId: {TraceId}",
            "REGISTER",
            tenantId,
            traceId);

        var result = await _authService.RegisterAsync(tenantId, request, cancellationToken);

        if (!result.Success)
        {
            if (result.Error == "REGISTRATION_FAILED")
            {
                return StatusCode(
                    StatusCodes.Status503ServiceUnavailable,
                    ApiErrorResponse.ServiceUnavailable(traceId));
            }

            return BadRequest(new ApiErrorResponse(
                result.Error ?? "REGISTRATION_FAILED",
                result.Message ?? "Registration failed",
                traceId));
        }

        return Ok(CreateBrowserAuthResponse(result.Response));
    }

    /// <summary>
    /// Authenticate a customer and get tokens.
    /// Rate limited to prevent brute force attacks.
    /// </summary>
    /// <param name="request">Login credentials</param>
    /// <returns>Authentication tokens and customer info</returns>
    /// <response code="200">Login successful</response>
    /// <response code="401">Invalid credentials or account deactivated</response>
    /// <response code="429">Too many login attempts</response>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("PublicLoginRateLimit")]
    [ProducesResponseType(typeof(StorefrontAuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Login(
        [FromBody] StorefrontLoginRequest request,
        CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        var ipAddress = GetClientIpAddress();

        // Validate tenant header
        var tenantValidation = ValidateTenantHeader(traceId);
        if (tenantValidation.Error != null)
            return tenantValidation.Error;

        var tenantId = tenantValidation.TenantId!.Value;

        // SECURITY: Check rate limit before processing login
        var rateLimitResult = await _rateLimiter.CheckLoginAsync(tenantId, request.Email, ipAddress);
        if (!rateLimitResult.Allowed)
        {
            _logger.LogWarning(
                "[StorefrontAuth] {Action} | TenantId: {TenantId} | Email: {Email} | IP: {IP} | TraceId: {TraceId}",
                "RATE_LIMIT_HIT",
                tenantId,
                MaskEmail(request.Email),
                MaskIp(ipAddress),
                traceId);

            Response.Headers["Retry-After"] = rateLimitResult.RetryAfterSeconds.ToString();
            return StatusCode(StatusCodes.Status429TooManyRequests, new ApiErrorResponse(
                "TOO_MANY_ATTEMPTS",
                $"Too many login attempts. Please try again in {rateLimitResult.RetryAfterSeconds} seconds.",
                traceId));
        }

        _logger.LogInformation(
            "[StorefrontAuth] {Action} endpoint called | TenantId: {TenantId} | TraceId: {TraceId}",
            "LOGIN",
            tenantId,
            traceId);

        var result = await _authService.LoginAsync(tenantId, request, cancellationToken);

        if (!result.Success)
        {
            // A rejected credential submission must not leave an older browser identity active.
            _cookieHelper.ClearAccessTokenCookie(Response);
            _cookieHelper.ClearRefreshTokenCookie(Response);

            if (result.Error == "LOGIN_FAILED")
            {
                return StatusCode(
                    StatusCodes.Status503ServiceUnavailable,
                    ApiErrorResponse.ServiceUnavailable(traceId));
            }

            if (result.Error == "PHONE_BLOCKED")
            {
                return StatusCode(StatusCodes.Status403Forbidden, new ApiErrorResponse(
                    result.Error,
                    result.Message ?? "This account is not allowed to sign in.",
                    traceId));
            }

            // Special handling for deactivated accounts
            if (result.Error == "ACCOUNT_DEACTIVATED")
            {
                return Unauthorized(new ApiErrorResponse(
                    result.Error,
                    result.Message ?? "Account deactivated",
                    traceId));
            }

            _logger.LogWarning(
                "[StorefrontAuth] {Action} | TenantId: {TenantId} | Email: {Email} | TraceId: {TraceId}",
                "LOGIN_FAILED",
                tenantId,
                MaskEmail(request.Email),
                traceId);

            return Unauthorized(new ApiErrorResponse(
                result.Error ?? "LOGIN_FAILED",
                result.Message ?? "Login failed",
                traceId));
        }

        if (!JwtTokenMetadataReader.TryReadJti(result.Response!.AccessToken, out var loginAccessTokenJti))
        {
            _logger.LogError(
                "[StorefrontAuth] LOGIN failed - Access token missing JTI | TenantId: {TenantId} | TraceId: {TraceId}",
                tenantId,
                traceId);
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                ApiErrorResponse.ServiceUnavailable(traceId));
        }

        // Issue proper refresh token for the customer
        var userAgent = GetUserAgent();
        var refreshResult = await _refreshTokenService.IssueTokenAsync(
            tenantId,
            result.Response!.Customer.Id,
            ipAddress,
            userAgent,
            accessTokenJti: loginAccessTokenJti,
            accessTokenExpiresAtUtc: result.Response.ExpiresAt,
            cancellationToken: cancellationToken);

        if (refreshResult.Success && refreshResult.ExpiresAtUtc.HasValue)
        {
            _cookieHelper.SetRefreshTokenCookie(
                Response,
                refreshResult.RawRefreshToken!,
                tenantId,
                refreshResult.ExpiresAtUtc.Value);
        }
        else
        {
            _logger.LogError(
                "[StorefrontAuth] LOGIN failed - refresh token authority was not persisted | TenantId: {TenantId} | CustomerId: {CustomerId} | TraceId: {TraceId}",
                tenantId,
                result.Response.Customer.Id,
                traceId);

            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                ApiErrorResponse.ServiceUnavailable(traceId));
        }

        _cookieHelper.SetAccessTokenCookie(Response, result.Response.AccessToken, result.Response.ExpiresAt);
        AuthCookieConfiguration.EnsureCsrfCookie(HttpContext);

        // Reset rate limit counter on successful login
        await _rateLimiter.ResetLoginCounterAsync(tenantId, request.Email, ipAddress);

        _logger.LogInformation(
            "[StorefrontAuth] {Action} | TenantId: {TenantId} | CustomerId: {CustomerId} | TraceId: {TraceId}",
            "LOGIN_SUCCESS",
            tenantId,
            result.Response.Customer.Id,
            traceId);

        return Ok(CreateBrowserAuthResponse(result.Response));
    }

    /// <summary>
    /// Refresh access token using refresh token.
    /// Implements token rotation with reuse detection.
    /// 
    /// SECURITY: Supports both cookie-based and body-based refresh tokens.
    /// Cookie-based is preferred (HttpOnly = XSS protection).
    /// Body-based is for backward compatibility.
    /// </summary>
    /// <param name="request">Optional refresh token (uses cookie if not provided)</param>
    /// <returns>New authentication tokens</returns>
    /// <response code="200">Token refreshed successfully</response>
    /// <response code="401">Invalid, expired, or reused refresh token</response>
    [HttpPost("refresh")]
    [HttpPost("refresh-token")]
    [AllowAnonymous]
    [EnableRateLimiting("PublicRefreshRateLimit")]
    [ProducesResponseType(typeof(StorefrontAuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RefreshToken(
        [FromBody] RefreshTokenRequestDto? request,
        CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();

        // Get refresh token - prefer cookie, fallback to body
        var refreshToken = _cookieHelper.GetRefreshTokenFromCookie(Request);
        var tokenFromCookie = refreshToken != null;

        if (string.IsNullOrEmpty(refreshToken) && !string.IsNullOrEmpty(request?.RefreshToken))
        {
            // SECURITY: Log warning when body is used (should migrate to cookies)
            _logger.LogWarning(
                "[StorefrontAuth] {Action} using body instead of cookie | TraceId: {TraceId}",
                "REFRESH_BODY_USED",
                traceId);
            refreshToken = request.RefreshToken;
        }

        if (string.IsNullOrEmpty(refreshToken))
        {
            return Unauthorized(new ApiErrorResponse(
                "MISSING_TOKEN",
                "Refresh token is required",
                traceId));
        }

        // Get tenant - prefer cookie, fallback to header
        Guid tenantId;
        var cookieTenantId = _cookieHelper.GetTenantIdFromCookie(Request);
        if (cookieTenantId.HasValue)
        {
            tenantId = cookieTenantId.Value;
        }
        else
        {
            var tenantValidation = ValidateTenantHeader(traceId);
            if (tenantValidation.Error != null)
                return tenantValidation.Error;
            tenantId = tenantValidation.TenantId!.Value;
        }

        var ipAddress = GetClientIpAddress();
        var userAgent = GetUserAgent();

        _logger.LogInformation(
            "[StorefrontAuth] {Action} endpoint called | TenantId: {TenantId} | FromCookie: {FromCookie} | TraceId: {TraceId}",
            "REFRESH",
            tenantId,
            tokenFromCookie,
            traceId);

        var result = await _refreshTokenService.RotateTokenAsync(
            tenantId,
            refreshToken,
            ipAddress,
            userAgent,
            cancellationToken);

        if (!result.Success)
        {
            // Clear cookie on failure
            _cookieHelper.ClearAccessTokenCookie(Response);
            _cookieHelper.ClearRefreshTokenCookie(Response);

            // Special handling for token reuse detection (security event)
            if (result.Error == "TOKEN_REUSE_DETECTED")
            {
                return Unauthorized(new ApiErrorResponse(
                    result.Error,
                    result.Message ?? "Security alert: Please log in again",
                    traceId));
            }

            return Unauthorized(new ApiErrorResponse(
                result.Error ?? "REFRESH_FAILED",
                result.Message ?? "Token refresh failed",
                traceId));
        }

        // Set new refresh token as cookie
        if (result.RefreshTokenExpiresAt.HasValue)
        {
            _cookieHelper.SetRefreshTokenCookie(
                Response,
                result.NewRefreshToken!,
                tenantId,
                result.RefreshTokenExpiresAt.Value);
        }
        else
        {
            _logger.LogWarning(
                "[StorefrontAuth] Refresh token issued without expiry time | TenantId: {TenantId} | Action: {Action}",
                tenantId,
                "TOKEN_REFRESH");
        }
        _cookieHelper.SetAccessTokenCookie(Response, result.NewAccessToken!, result.AccessTokenExpiresAt!.Value);
        AuthCookieConfiguration.EnsureCsrfCookie(HttpContext);

        _logger.LogInformation(
            "[StorefrontAuth] {Action} | TenantId: {TenantId} | CustomerId: {CustomerId} | TraceId: {TraceId}",
            "TOKEN_REFRESH",
            tenantId,
            result.Customer?.Id,
            traceId);

        return Ok(CreateBrowserAuthResponse(new StorefrontAuthResponse
        {
            Customer = result.Customer!,
            AccessToken = result.NewAccessToken!,
            RefreshToken = result.NewRefreshToken!,
            ExpiresAt = result.AccessTokenExpiresAt!.Value
        }));
    }

    /// <summary>
    /// Get current authenticated customer's info.
    /// Requires valid Storefront JWT.
    /// </summary>
    /// <returns>Current customer info</returns>
    [HttpGet("me")]
    [Authorize(AuthenticationSchemes = "StorefrontJwt")]
    [ProducesResponseType(typeof(StorefrontCustomerDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetCurrentCustomer(CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();

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

        // Get customer profile from service
        var profileService = HttpContext.RequestServices.GetRequiredService<IStorefrontOnlineCustomerService>();
        var profile = await profileService.GetProfileAsync(tenantId, customerId, cancellationToken);

        if (profile == null)
        {
            return NotFound(new ApiErrorResponse(
                "CUSTOMER_NOT_FOUND",
                "Customer not found",
                traceId));
        }

        // CRITICAL: Check if customer is still active
        if (!profile.IsActive)
        {
            _logger.LogWarning(
                "[StorefrontAuth] {Action} BLOCKED - Account deactivated | TenantId: {TenantId} | CustomerId: {CustomerId} | TraceId: {TraceId}",
                "GET_ME",
                tenantId,
                customerId,
                traceId);
            return Unauthorized(new ApiErrorResponse(
                "ACCOUNT_DEACTIVATED",
                "Your account has been deactivated",
                traceId));
        }

        AuthCookieConfiguration.EnsureCsrfCookie(HttpContext);

        return Ok(new StorefrontCustomerDto
        {
            Id = profile.Id,
            Email = profile.Email,
            DisplayName = profile.DisplayName,
            Phone = profile.Phone,
            Provider = profile.Provider,
            EmailVerified = profile.EmailVerified,
            CreatedAtUtc = profile.CreatedAtUtc
        });
    }

    #region Social Login Endpoints (Phase 10.3.3)

    [HttpPost("social/challenge/{provider}")]
    [AllowAnonymous]
    [EnableRateLimiting("PublicLoginRateLimit")]
    public async Task<IActionResult> SocialChallenge(string provider, CancellationToken cancellationToken)
    {
        provider = provider.ToLowerInvariant();
        var tenant = ValidateTenantHeader(GetTraceId());
        if (tenant.Error != null) return tenant.Error;
        var available = await _socialLoginService.GetAvailableProvidersAsync(tenant.TenantId!.Value, cancellationToken);
        if (!(provider == "google" && available.Google || provider == "facebook" && available.Facebook))
            return StatusCode(403, new ApiErrorResponse("PROVIDER_DISABLED", "Provider is unavailable", GetTraceId()));
        return Ok(HttpContext.RequestServices.GetRequiredService<SocialLoginChallengeService>().Issue(HttpContext, tenant.TenantId.Value, provider));
    }

    private async Task<string?> ConsumeSocialChallenge(Guid tenant, string provider, string? state) =>
        await HttpContext.RequestServices.GetRequiredService<SocialLoginChallengeService>().ConsumeAsync(HttpContext, tenant, provider, state);

    private IActionResult RejectSocialState()
    {
        _cookieHelper.ClearAccessTokenCookie(Response);
        _cookieHelper.ClearRefreshTokenCookie(Response);
        return Unauthorized(new ApiErrorResponse("INVALID_LOGIN_STATE", "Login attempt expired or did not match this browser. Start again.", GetTraceId()));
    }

    /// <summary>
    /// Get available social login providers for this tenant.
    /// Used by frontend to show/hide social login buttons.
    /// </summary>
    /// <returns>Available providers</returns>
    [HttpGet("providers")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AvailableProvidersResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAvailableProviders(CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();

        var tenantValidation = ValidateTenantHeader(traceId);
        if (tenantValidation.Error != null)
            return tenantValidation.Error;

        var tenantId = tenantValidation.TenantId!.Value;
        var result = await _socialLoginService.GetAvailableProvidersAsync(tenantId, cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// Authenticate with Google ID token.
    /// Creates new account or links to existing email.
    /// </summary>
    /// <param name="request">Google ID token</param>
    /// <returns>Authentication tokens and customer info</returns>
    /// <response code="200">Login/registration successful</response>
    /// <response code="401">Invalid token or account deactivated</response>
    /// <response code="403">Google login is disabled for this store</response>
    [HttpPost("google")]
    [AllowAnonymous]
    [EnableRateLimiting("PublicLoginRateLimit")]
    [ProducesResponseType(typeof(StorefrontAuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GoogleLogin(
        [FromBody] GoogleLoginRequest request,
        CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        var ipAddress = GetClientIpAddress();
        var userAgent = GetUserAgent();

        var tenantValidation = ValidateTenantHeader(traceId);
        if (tenantValidation.Error != null)
            return tenantValidation.Error;

        var tenantId = tenantValidation.TenantId!.Value;

        _logger.LogInformation(
            "[StorefrontAuth] {Action} endpoint called | TenantId: {TenantId} | TraceId: {TraceId}",
            "GOOGLE_LOGIN",
            tenantId,
            traceId);

        var nonce = await ConsumeSocialChallenge(tenantId, "google", request.State);
        if (nonce == null) return RejectSocialState();
        var result = await _socialLoginService.GoogleLoginAsync(
            tenantId,
            request.IdToken,
            ipAddress,
            userAgent,
            cancellationToken, nonce);

        if (!result.Success)
        {
            _cookieHelper.ClearAccessTokenCookie(Response);
            _cookieHelper.ClearRefreshTokenCookie(Response);
            var statusCode = result.Error switch
            {
                "PROVIDER_DISABLED" => StatusCodes.Status403Forbidden,
                "PROVIDER_UNAVAILABLE" => StatusCodes.Status503ServiceUnavailable,
                "ACCOUNT_DEACTIVATED" => StatusCodes.Status401Unauthorized,
                _ => StatusCodes.Status401Unauthorized
            };

            return StatusCode(statusCode, new ApiErrorResponse(
                result.Error ?? "GOOGLE_LOGIN_FAILED",
                result.Message ?? "Google login failed",
                traceId));
        }

        // Set refresh token as HttpOnly cookie
        if (!string.IsNullOrEmpty(result.RefreshToken) && result.RefreshTokenExpiresAt.HasValue)
        {
            _cookieHelper.SetRefreshTokenCookie(
                Response,
                result.RefreshToken,
                tenantId,
                result.RefreshTokenExpiresAt.Value);
        }
        else
        {
            _logger.LogError(
                "[StorefrontAuth] GOOGLE_LOGIN failed - refresh token authority was not persisted | TenantId: {TenantId} | CustomerId: {CustomerId} | TraceId: {TraceId}",
                tenantId,
                result.Customer?.Id,
                traceId);

            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                ApiErrorResponse.ServiceUnavailable(traceId));
        }
        _cookieHelper.SetAccessTokenCookie(Response, result.AccessToken!, result.ExpiresAt!.Value);
        AuthCookieConfiguration.EnsureCsrfCookie(HttpContext);

        _logger.LogInformation(
            "[StorefrontAuth] {Action} | TenantId: {TenantId} | CustomerId: {CustomerId} | IsNewUser: {IsNewUser} | TraceId: {TraceId}",
            "GOOGLE_LOGIN_SUCCESS",
            tenantId,
            result.Customer?.Id,
            result.IsNewUser,
            traceId);

        return Ok(CreateBrowserAuthResponse(new StorefrontAuthResponse
        {
            Customer = result.Customer!,
            AccessToken = result.AccessToken!,
            RefreshToken = result.RefreshToken!,
            ExpiresAt = result.ExpiresAt!.Value
        }));
    }

    /// <summary>
    /// Authenticate with Facebook access token.
    /// Creates new account or links to existing email.
    /// </summary>
    /// <param name="request">Facebook access token</param>
    /// <returns>Authentication tokens and customer info</returns>
    /// <response code="200">Login/registration successful</response>
    /// <response code="401">Invalid token or account deactivated</response>
    /// <response code="403">Facebook login is disabled for this store</response>
    [HttpPost("facebook")]
    [AllowAnonymous]
    [EnableRateLimiting("PublicLoginRateLimit")]
    [ProducesResponseType(typeof(StorefrontAuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> FacebookLogin(
        [FromBody] FacebookLoginRequest request,
        CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        var ipAddress = GetClientIpAddress();
        var userAgent = GetUserAgent();

        var tenantValidation = ValidateTenantHeader(traceId);
        if (tenantValidation.Error != null)
            return tenantValidation.Error;

        var tenantId = tenantValidation.TenantId!.Value;

        _logger.LogInformation(
            "[StorefrontAuth] {Action} endpoint called | TenantId: {TenantId} | TraceId: {TraceId}",
            "FACEBOOK_LOGIN",
            tenantId,
            traceId);

        if (await ConsumeSocialChallenge(tenantId, "facebook", request.State) == null) return RejectSocialState();
        var result = await _socialLoginService.FacebookLoginAsync(
            tenantId,
            request.AccessToken,
            ipAddress,
            userAgent,
            cancellationToken);

        if (!result.Success)
        {
            _cookieHelper.ClearAccessTokenCookie(Response);
            _cookieHelper.ClearRefreshTokenCookie(Response);
            var statusCode = result.Error switch
            {
                "PROVIDER_DISABLED" => StatusCodes.Status403Forbidden,
                "PROVIDER_UNAVAILABLE" => StatusCodes.Status503ServiceUnavailable,
                "ACCOUNT_DEACTIVATED" => StatusCodes.Status401Unauthorized,
                _ => StatusCodes.Status401Unauthorized
            };

            return StatusCode(statusCode, new ApiErrorResponse(
                result.Error ?? "FACEBOOK_LOGIN_FAILED",
                result.Message ?? "Facebook login failed",
                traceId));
        }

        // Set refresh token as HttpOnly cookie
        if (!string.IsNullOrEmpty(result.RefreshToken) && result.RefreshTokenExpiresAt.HasValue)
        {
            _cookieHelper.SetRefreshTokenCookie(
                Response,
                result.RefreshToken,
                tenantId,
                result.RefreshTokenExpiresAt.Value);
        }
        else
        {
            _logger.LogError(
                "[StorefrontAuth] FACEBOOK_LOGIN failed - refresh token authority was not persisted | TenantId: {TenantId} | CustomerId: {CustomerId} | TraceId: {TraceId}",
                tenantId,
                result.Customer?.Id,
                traceId);

            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                ApiErrorResponse.ServiceUnavailable(traceId));
        }
        _cookieHelper.SetAccessTokenCookie(Response, result.AccessToken!, result.ExpiresAt!.Value);
        AuthCookieConfiguration.EnsureCsrfCookie(HttpContext);

        _logger.LogInformation(
            "[StorefrontAuth] {Action} | TenantId: {TenantId} | CustomerId: {CustomerId} | IsNewUser: {IsNewUser} | TraceId: {TraceId}",
            "FACEBOOK_LOGIN_SUCCESS",
            tenantId,
            result.Customer?.Id,
            result.IsNewUser,
            traceId);

        return Ok(CreateBrowserAuthResponse(new StorefrontAuthResponse
        {
            Customer = result.Customer!,
            AccessToken = result.AccessToken!,
            RefreshToken = result.RefreshToken!,
            ExpiresAt = result.ExpiresAt!.Value
        }));
    }

    #endregion

    /// <summary>
    /// Logout - revokes the refresh token and blacklists the access token.
    /// 
    /// SECURITY:
    /// - Revokes refresh token (PostgreSQL)
    /// - Blacklists access token (Redis) for remaining lifetime
    /// - Clears HttpOnly cookies
    /// - Idempotent (multiple calls OK)
    /// </summary>
    /// <param name="request">Optional refresh token (uses cookie if not provided)</param>
    /// <returns>Logout confirmation</returns>
    [HttpPost("logout")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Logout(
        [FromBody] LogoutRequestDto? request,
        CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();

        // Get tenant - prefer cookie, fallback to header
        Guid? tenantId = _cookieHelper.GetTenantIdFromCookie(Request);
        if (!tenantId.HasValue)
        {
            var tenantValidation = ValidateTenantHeader(traceId);
            if (tenantValidation.TenantId.HasValue)
            {
                tenantId = tenantValidation.TenantId.Value;
            }
        }

        // Get refresh token - prefer cookie, fallback to body
        var refreshToken = _cookieHelper.GetRefreshTokenFromCookie(Request);
        if (string.IsNullOrEmpty(refreshToken) && !string.IsNullOrEmpty(request?.RefreshToken))
        {
            refreshToken = request.RefreshToken;
        }

        // Revoke refresh token if we have one
        if (tenantId.HasValue && !string.IsNullOrWhiteSpace(refreshToken))
        {
            await _refreshTokenService.RevokeTokenAsync(
                tenantId.Value,
                refreshToken,
                "User logout",
                cancellationToken);

            _logger.LogInformation(
                "[StorefrontAuth] {Action} - Refresh token revoked | TenantId: {TenantId} | TraceId: {TraceId}",
                "LOGOUT",
                tenantId,
                traceId);
        }

        // Blacklist access token if we have one (from JWT claims)
        // Explicitly authenticate StorefrontJwt scheme since this is [AllowAnonymous]
        var authResult = await HttpContext.AuthenticateAsync("StorefrontJwt");
        if (authResult.Succeeded && authResult.Principal != null)
        {
            var jti = authResult.Principal.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Jti)?.Value;
            var expClaim = authResult.Principal.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Exp)?.Value;

            if (!string.IsNullOrEmpty(jti) && tenantId.HasValue)
            {
                // Derive expiry from the token's exp claim; fall back to the configured
                // access-token lifetime if the claim is absent or unparseable.
                var expiresAt = DateTime.UtcNow.AddMinutes(_jwtSettings.AccessTokenExpirationMinutes);
                if (!string.IsNullOrEmpty(expClaim) && long.TryParse(expClaim, out var expUnix))
                {
                    expiresAt = DateTimeOffset.FromUnixTimeSeconds(expUnix).UtcDateTime;
                }

                try
                {
                    await _jwtBlacklist.BlacklistTokenAsync(
                        tenantId.Value,
                        jti,
                        expiresAt,
                        cancellationToken);
                }
                catch (TokenRevocationPersistenceException)
                {
                    // CRITICAL: Clear cookies even if server-side revocation fails,
                    // forcing the client into a logged-out state locally.
                    _cookieHelper.ClearAccessTokenCookie(Response);
                    _cookieHelper.ClearRefreshTokenCookie(Response);
                    
                    return StatusCode(
                        StatusCodes.Status503ServiceUnavailable,
                        ApiErrorResponse.ServiceUnavailable(traceId));
                }

                _logger.LogInformation(
                    "[StorefrontAuth] {Action} - Access token blacklisted | TenantId: {TenantId} | Jti: {Jti} | TraceId: {TraceId}",
                    "LOGOUT",
                    tenantId,
                    jti,
                    traceId);
            }
        }

        // Always clear cookies (idempotent)
        _cookieHelper.ClearAccessTokenCookie(Response);
        _cookieHelper.ClearRefreshTokenCookie(Response);

        return Ok(new { message = "Logged out successfully" });
    }

    #region Password Reset Endpoints (Phase 10.3.5)

    /// <summary>
    /// Request password reset email.
    /// SECURITY: Returns same response regardless of email existence (no user enumeration).
    /// </summary>
    /// <param name="request">Email address</param>
    /// <returns>Generic success message</returns>
    /// <response code="200">Always returns success to prevent user enumeration</response>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ForgotPasswordResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> ForgotPassword(
        [FromBody] ForgotPasswordRequest request,
        CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        var ipAddress = GetClientIpAddress();

        var tenantValidation = ValidateTenantHeader(traceId);
        if (tenantValidation.Error != null)
        {
            // SECURITY: Even on tenant error, return generic response
            return Ok(new ForgotPasswordResult
            {
                Success = true,
                Message = "If the email exists, we sent instructions."
            });
        }

        var tenantId = tenantValidation.TenantId!.Value;

        _logger.LogInformation(
            "[StorefrontAuth] {Action} endpoint called | TenantId: {TenantId} | Email: {Email} | TraceId: {TraceId}",
            "FORGOT_PASSWORD",
            tenantId,
            MaskEmail(request.Email),
            traceId);

        var result = await _passwordResetService.ForgotPasswordAsync(
            tenantId,
            request.Email,
            ipAddress,
            cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// Reset password with token.
    /// On success, revokes all sessions and forces re-login.
    /// </summary>
    /// <param name="request">Reset token and new password</param>
    /// <returns>Success or error</returns>
    /// <response code="200">Password reset successful</response>
    /// <response code="400">Invalid token or weak password</response>
    [HttpPost("reset-password")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ResetPasswordResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResetPassword(
        [FromBody] ResetPasswordRequest request,
        CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        var ipAddress = GetClientIpAddress();

        var tenantValidation = ValidateTenantHeader(traceId);
        if (tenantValidation.Error != null)
            return tenantValidation.Error;

        var tenantId = tenantValidation.TenantId!.Value;

        _logger.LogInformation(
            "[StorefrontAuth] {Action} endpoint called | TenantId: {TenantId} | TraceId: {TraceId}",
            "RESET_PASSWORD",
            tenantId,
            traceId);

        var result = await _passwordResetService.ResetPasswordAsync(
            tenantId,
            request.Token,
            request.NewPassword,
            ipAddress,
            cancellationToken);

        if (!result.Success)
        {
            return BadRequest(new ApiErrorResponse(
                result.Error ?? "RESET_FAILED",
                result.Message ?? "Password reset failed",
                traceId));
        }

        // Clear any cookies for this client
        _cookieHelper.ClearAccessTokenCookie(Response);
        _cookieHelper.ClearRefreshTokenCookie(Response);

        _logger.LogInformation(
            "[StorefrontAuth] {Action} | TenantId: {TenantId} | Status: SUCCESS | TraceId: {TraceId}",
            "RESET_PASSWORD",
            tenantId,
            traceId);

        return Ok(result);
    }

    #endregion

    #region Helper Methods

    private (Guid? TenantId, IActionResult? Error) ValidateTenantHeader(string traceId)
    {
        return PointOfSale.API.Helpers.StorefrontTenantContextResolver.ValidateTenant(this, traceId);
    }

    private string GetTraceId() => Activity.Current?.Id ?? HttpContext.TraceIdentifier;

    private string GetClientIpAddress()
    {
        return ClientIpAddressResolver.GetTrustedClientIp(HttpContext);
    }

    private string? GetUserAgent()
    {
        return HttpContext.Request.Headers["User-Agent"].FirstOrDefault();
    }

    private static string MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return "[empty]";

        var atIndex = email.IndexOf('@');
        if (atIndex <= 1)
            return "***@" + (atIndex >= 0 ? email[(atIndex + 1)..] : "***");

        return email[0] + "***" + email[(atIndex - 1)..];
    }

    private static string MaskIp(string? ip)
    {
        if (string.IsNullOrWhiteSpace(ip))
            return "[unknown]";

        var lastDot = ip.LastIndexOf('.');
        if (lastDot > 0)
            return ip[..(lastDot + 1)] + "***";

        return ip[..Math.Min(ip.Length, 3)] + "***";
    }

    private static StorefrontAuthResponse CreateBrowserAuthResponse(StorefrontAuthResponse response) => new()
    {
        Customer = response.Customer,
        ExpiresAt = response.ExpiresAt,
        VerificationEmailSent = response.VerificationEmailSent,
        AccessToken = string.Empty,
        RefreshToken = string.Empty
    };

    #endregion
}

#region DTOs

public class RefreshTokenRequestDto
{
    public string? RefreshToken { get; set; }
}

public class LogoutRequestDto
{
    public string? RefreshToken { get; set; }
}


#endregion
