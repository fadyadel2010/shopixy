using System.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PointOfSale.API.Attributes;
using PointOfSale.API.DTOs.Customers;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Security;
using PointOfSale.API.Services.CommerceHub;

namespace PointOfSale.API.Controllers;

[AllowAnonymousTenant]
[ApiController]
[Route("api/customers/auth")]
public class CustomersAuthController : ControllerBase
{
    private const string CustomerApiCookiePath = "/api/customers";
    private const string CommerceHubApiCookiePath = "/api/commercehub";
    private readonly ICommerceHubCustomerAuthService _authService;
    private readonly ICustomerTokenRevocationService _tokenRevocationService;
    private readonly IWebHostEnvironment _env;

    public CustomersAuthController(
        ICommerceHubCustomerAuthService authService,
        ICustomerTokenRevocationService tokenRevocationService,
        IWebHostEnvironment env)
    {
        _authService = authService;
        _tokenRevocationService = tokenRevocationService;
        _env = env;
    }

    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting("PublicRegisterRateLimit")]
    [ProducesResponseType(typeof(CustomerAuthResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register(
        [FromBody] CustomerRegisterRequestDto request,
        CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        var result = await _authService.RegisterAsync(request, cancellationToken);

        if (result.Success)
        {
            ClearCommerceHubRecoveryCookie();
            return Ok(new { message = result.Message });
        }

        var statusCode = result.Error switch
        {
            "EMAIL_EXISTS" => StatusCodes.Status409Conflict,
            "BLACKLISTED_REGISTRATION" => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest
        };

        return StatusCode(
            statusCode,
            new ApiErrorResponse(result.Error ?? "REGISTER_FAILED", result.Message ?? "Unable to register customer.", traceId));
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("PublicLoginRateLimit")]
    [ProducesResponseType(typeof(CustomerAuthResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Login(
        [FromBody] CustomerLoginRequestDto request,
        CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        var result = await _authService.LoginAsync(request, cancellationToken);

        if (result.Success)
        {
            SetCustomerAccessCookie(result.Response!);
            ClearCommerceHubRecoveryCookie();
            return Ok(CreateBrowserAuthResponse(result.Response!));
        }

        var statusCode = result.Error switch
        {
            "INVALID_REQUEST" => StatusCodes.Status400BadRequest,
            "CUSTOMER_BANNED" or "CUSTOMER_SUSPENDED" or "EMAIL_NOT_VERIFIED" => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status401Unauthorized
        };

        return StatusCode(
            statusCode,
            new ApiErrorResponse(result.Error ?? "LOGIN_FAILED", result.Message ?? "Unable to sign in.", traceId));
    }

    [HttpGet("me")]
    [Authorize(AuthenticationSchemes = CustomerSecurityConstants.AuthenticationScheme, Roles = CustomerSecurityConstants.Role)]
    [EnableRateLimiting("PublicReadRateLimit")]
    [ProducesResponseType(typeof(CustomerProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Me(CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        var customerId = ResolveCustomerId();
        if (!customerId.HasValue)
        {
            return Unauthorized(ApiErrorResponse.InvalidToken(traceId));
        }

        var customer = await _authService.GetProfileAsync(customerId.Value, cancellationToken);
        if (customer == null)
        {
            return NotFound(new ApiErrorResponse("CUSTOMER_NOT_FOUND", "Customer not found.", traceId));
        }

        AuthCookieConfiguration.EnsureCsrfCookie(HttpContext);

        return Ok(customer);
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        bool revocationFailed = false;

        // SECURITY: Revoke the access token server-side to prevent replay attacks
        if (Request.Cookies.TryGetValue(
            AuthCookieConfiguration.CommerceHubCustomerAccessTokenCookieName,
            out var accessToken) &&
            !string.IsNullOrWhiteSpace(accessToken))
        {
            try
            {
                await _tokenRevocationService.RevokeTokenAsync(accessToken, "logout", cancellationToken);
            }
            catch (TokenRevocationPersistenceException ex)
            {
                if (ex.SubjectId.HasValue)
                {
                    try
                    {
                        await _tokenRevocationService.RevokeAllTokensAsync(
                            ex.SubjectId.Value,
                            "logout_blacklist_persistence_fallback",
                            cancellationToken);
                    }
                    catch (TokenRevocationPersistenceException)
                    {
                        revocationFailed = true;
                    }
                }
                else
                {
                    revocationFailed = true;
                }
            }
        }

        // SECURITY: Always clear browser cookie authority, even if the access-token cookie
        // is already missing or revocation fails. The HttpOnly recovery cookie is server-
        // controlled and must not survive an auth-boundary logout attempt.
        ClearCustomerAccessCookies();
        ClearCommerceHubRecoveryCookie();

        // Return 503 if revocation failed, but cookie is already cleared
        if (revocationFailed)
        {
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                ApiErrorResponse.ServiceUnavailable(traceId));
        }

        var customerId = ResolveCustomerId();
        if (customerId.HasValue)
        {
            await _authService.RecordIdentityEventAsync(
                customerId.Value,
                PointOfSale.Core.Entities.CommerceHubCustomerIdentityEvent.EventTypes.LoggedOut,
                "Customer logged out",
                cancellationToken: cancellationToken);
        }

        return Ok(new { message = "Logged out successfully." });
    }

    [HttpPost("revoke-all-sessions")]
    [Authorize(AuthenticationSchemes = CustomerSecurityConstants.AuthenticationScheme, Roles = CustomerSecurityConstants.Role)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> RevokeAllSessions(CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        var customerId = ResolveCustomerId();
        if (!customerId.HasValue)
        {
            return Unauthorized(ApiErrorResponse.InvalidToken(traceId));
        }

        bool revocationFailed = false;
        try
        {
            await _tokenRevocationService.RevokeAllTokensAsync(
                customerId.Value,
                "customer_requested_revoke_all_sessions",
                cancellationToken);
        }
        catch (TokenRevocationPersistenceException)
        {
            revocationFailed = true;
        }
        finally
        {
            // SECURITY: Always clear the cookie, even if revocation fails
            // This ensures fail-closed behavior - the browser won't keep sending the token
            ClearCustomerAccessCookies();
            ClearCommerceHubRecoveryCookie();
        }

        if (revocationFailed)
        {
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                ApiErrorResponse.ServiceUnavailable(traceId));
        }

        await _authService.RecordIdentityEventAsync(
            customerId.Value,
            PointOfSale.Core.Entities.CommerceHubCustomerIdentityEvent.EventTypes.AllSessionsRevoked,
            "Customer revoked all sessions",
            cancellationToken: cancellationToken);

        return Ok(new { message = "All sessions revoked successfully." });
    }

    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [EnableRateLimiting("PublicRegisterRateLimit")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ForgotPassword(
        [FromBody] CustomerForgotPasswordRequestDto request,
        CancellationToken cancellationToken)
    {
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var result = await _authService.ForgotPasswordAsync(request.Email, ipAddress, cancellationToken);

        // PRIVACY: Always return 200 OK to prevent user enumeration
        return Ok(new { message = result.Message });
    }

    [HttpPost("reset-password")]
    [AllowAnonymous]
    [EnableRateLimiting("PublicRegisterRateLimit")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResetPassword(
        [FromBody] CustomerResetPasswordRequestDto request,
        CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var result = await _authService.ResetPasswordAsync(request.Token, request.NewPassword, ipAddress, cancellationToken);

        if (result.Success)
        {
            // SECURITY: Clear any existing session cookie after password reset
            ClearCustomerAccessCookies();
            ClearCommerceHubRecoveryCookie();
            return Ok(new { message = result.Message });
        }

        return BadRequest(new ApiErrorResponse(result.Error ?? "RESET_FAILED", result.Message ?? "Unable to reset password.", traceId));
    }

    [HttpPost("verify-email")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> VerifyEmail(
        [FromBody] CustomerVerifyEmailRequestDto request,
        CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        var result = await _authService.VerifyEmailAsync(request.Token, cancellationToken);

        if (result.Success)
        {
            return Ok(new { message = result.Message });
        }

        return BadRequest(new ApiErrorResponse(result.Error ?? "VERIFY_FAILED", result.Message ?? "Unable to verify email.", traceId));
    }

    [HttpPost("resend-verification")]
    [Authorize(AuthenticationSchemes = CustomerSecurityConstants.AuthenticationScheme, Roles = CustomerSecurityConstants.Role)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResendVerification(
        [FromBody] CustomerResendVerificationRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await _authService.ResendVerificationEmailAsync(request.Email, cancellationToken);
        return Ok(new { message = result.Message });
    }

    [HttpPut("profile")]
    [Authorize(AuthenticationSchemes = CustomerSecurityConstants.AuthenticationScheme, Roles = CustomerSecurityConstants.Role)]
    [ProducesResponseType(typeof(CustomerProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateProfile(
        [FromBody] CustomerUpdateProfileRequestDto request,
        CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        var customerId = ResolveCustomerId();
        if (!customerId.HasValue)
        {
            return Unauthorized(ApiErrorResponse.InvalidToken(traceId));
        }

        var result = await _authService.UpdateProfileAsync(customerId.Value, request, cancellationToken);

        if (result.Success)
        {
            return Ok(result.Response?.Customer);
        }

        return BadRequest(new ApiErrorResponse(result.Error ?? "UPDATE_FAILED", result.Message ?? "Unable to update profile.", traceId));
    }

    [HttpPost("change-password")]
    [Authorize(AuthenticationSchemes = CustomerSecurityConstants.AuthenticationScheme, Roles = CustomerSecurityConstants.Role)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ChangePassword(
        [FromBody] CustomerChangePasswordRequestDto request,
        CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        var customerId = ResolveCustomerId();
        if (!customerId.HasValue)
        {
            return Unauthorized(ApiErrorResponse.InvalidToken(traceId));
        }

        var result = await _authService.ChangePasswordAsync(customerId.Value, request, cancellationToken);

        if (result.Success)
        {
            // SECURITY: Clear the cookie after password change so user must re-authenticate
            ClearCustomerAccessCookies();
            ClearCommerceHubRecoveryCookie();
            return Ok(new { message = result.Message });
        }

        return BadRequest(new ApiErrorResponse(result.Error ?? "CHANGE_FAILED", result.Message ?? "Unable to change password.", traceId));
    }

    [HttpPost("change-email")]
    [Authorize(AuthenticationSchemes = CustomerSecurityConstants.AuthenticationScheme, Roles = CustomerSecurityConstants.Role)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ChangeEmail(
        [FromBody] CustomerChangeEmailRequestDto request,
        CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        var customerId = ResolveCustomerId();
        if (!customerId.HasValue)
        {
            return Unauthorized(ApiErrorResponse.InvalidToken(traceId));
        }

        var result = await _authService.ChangeEmailAsync(customerId.Value, request, cancellationToken);
        if (result.Success)
        {
            ClearCustomerAccessCookies();
            ClearCommerceHubRecoveryCookie();
            return Ok(new { message = result.Message });
        }

        var statusCode = result.Error == "EMAIL_EXISTS"
            ? StatusCodes.Status409Conflict
            : StatusCodes.Status400BadRequest;
        return StatusCode(
            statusCode,
            new ApiErrorResponse(result.Error ?? "EMAIL_CHANGE_FAILED", result.Message ?? "Unable to change email.", traceId));
    }

    private Guid? ResolveCustomerId()
    {
        var customerIdClaim = User.FindFirst("CustomerId")?.Value;
        return Guid.TryParse(customerIdClaim, out var customerId) ? customerId : null;
    }

    private void SetCustomerAccessCookie(CustomerAuthResponseDto response)
    {
        var lifetime = response.ExpiresAt - DateTime.UtcNow > TimeSpan.Zero
            ? response.ExpiresAt - DateTime.UtcNow
            : TimeSpan.FromMinutes(1);

        foreach (var path in new[] { CustomerApiCookiePath, CommerceHubApiCookiePath })
        {
            Response.Cookies.Append(
                AuthCookieConfiguration.CommerceHubCustomerAccessTokenCookieName,
                response.AccessToken,
                AuthCookieConfiguration.CreateHttpOnlyOptions(lifetime, path));
        }

        AuthCookieConfiguration.EnsureCsrfCookie(HttpContext);
    }

    private void ClearCustomerAccessCookies()
    {
        foreach (var path in new[] { CustomerApiCookiePath, CommerceHubApiCookiePath })
        {
            Response.Cookies.Delete(
                AuthCookieConfiguration.CommerceHubCustomerAccessTokenCookieName,
                AuthCookieConfiguration.CreateExpiredOptions(path));
        }
    }

    private void ClearCommerceHubRecoveryCookie()
    {
        var allowInsecureDevelopmentCookie = _env.IsDevelopment();

        CommerceHubOrderAccessCookie.ClearOrderAccessToken(HttpContext, allowInsecureDevelopmentCookie);
    }

    private static CustomerAuthResponseDto CreateBrowserAuthResponse(CustomerAuthResponseDto response) => new()
    {
        Customer = response.Customer,
        ExpiresAt = response.ExpiresAt,
        AccessToken = string.Empty
    };

    private string GetTraceId() => Activity.Current?.Id ?? HttpContext.TraceIdentifier;
}
