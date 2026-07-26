using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.Security;
using PointOfSale.API.SuperAdmin.Models;
using PointOfSale.API.SuperAdmin.Services;
using PointOfSale.API.SuperAdmin.DTOs;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.SuperAdmin.Controllers;

[Route("api/super-admin/auth")]
public sealed class SuperAdminAuthController : SuperAdminBaseController
{
    private static readonly string[] NavigationCapabilities =
    [
        "super-admin.finance.view",
        "super-admin.payments.view",
        "super-admin.payment-providers.view",
        "super-admin.orders.view",
        "super-admin.reviews.view",
        "super-admin.users.govern",
        "super-admin.marketplace.govern",
        "super-admin.marketplace.operate",
        "super-admin.refunds.manage",
        "super-admin.payouts.manage",
        "super-admin.commission.manage",
        "super-admin.promotions.manage",
        "super-admin.homepage.manage",
        "super-admin.storefront-defaults.manage",
        "super-admin.marketplace-cms.manage",
        "super-admin.marketplace-footer.manage"
    ];

    private readonly ISuperAdminAuthService _authService;
    private readonly ApplicationDbContext _dbContext;

    public SuperAdminAuthController(ISuperAdminAuthService authService, ApplicationDbContext dbContext)
    {
        _authService = authService;
        _dbContext = dbContext;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(SuperAdminSecurityConstants.RateLimitPolicyName)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Login(
        [FromBody] SuperAdminLoginRequest? request,
        CancellationToken cancellationToken)
    {
        if (request == null)
        {
            return ErrorResponse(
                StatusCodes.Status400BadRequest,
                "VALIDATION_ERROR",
                "Request body is required.");
        }

        var result = await _authService.LoginAsync(request, cancellationToken);
        if (!result.Success || result.Response == null)
        {
            if (string.Equals(result.Error, "RATE_LIMITED", StringComparison.Ordinal))
            {
                return ErrorResponse(
                    StatusCodes.Status429TooManyRequests,
                    "RATE_LIMITED",
                    result.Message ?? "Too many login attempts. Please try again later.");
            }

            if (string.Equals(result.Error, "INVALID_REQUEST", StringComparison.Ordinal))
            {
                return ErrorResponse(
                    StatusCodes.Status400BadRequest,
                    "VALIDATION_ERROR",
                    result.Message ?? "Email and password are required.");
            }

            if (string.Equals(result.Error, "SUPER_ADMIN_BANNED", StringComparison.Ordinal))
            {
                return ErrorResponse(
                    StatusCodes.Status403Forbidden,
                    "SUPER_ADMIN_BANNED",
                    result.Message ?? "This Super Admin account is banned.");
            }

            if (string.Equals(result.Error, "SUPER_ADMIN_SUSPENDED", StringComparison.Ordinal))
            {
                return ErrorResponse(
                    StatusCodes.Status403Forbidden,
                    "SUPER_ADMIN_SUSPENDED",
                    result.Message ?? "This Super Admin account is suspended.");
            }

            if (string.Equals(result.Error, "SUPER_ADMIN_FINANCIAL_LOCKED", StringComparison.Ordinal))
            {
                return ErrorResponse(
                    StatusCodes.Status403Forbidden,
                    "SUPER_ADMIN_FINANCIAL_LOCKED",
                    result.Message ?? "This Super Admin account is financially locked.");
            }

            return ErrorResponse(
                StatusCodes.Status401Unauthorized,
                "AUTH_UNAUTHORIZED",
                result.Message ?? "Invalid email or password.");
        }

        AuthCookieConfiguration.SetSessionCookies(
            HttpContext,
            AuthCookieConfiguration.SuperAdmin,
            result.Response.AccessToken,
            ResolveLifetime(result.Response.ExpiresAtUtc),
            result.Response.RefreshToken,
            ResolveLifetime(result.Response.RefreshTokenExpiresAtUtc));

        return OkResponse(ToClientSession(result.Response));
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting(SuperAdminSecurityConstants.RateLimitPolicyName)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh(
        [FromBody] SuperAdminRefreshRequest? request,
        CancellationToken cancellationToken)
    {
        if (request == null)
        {
            return ErrorResponse(
                StatusCodes.Status400BadRequest,
                "VALIDATION_ERROR",
                "Request body is required.");
        }

        var refreshRequest = new SuperAdminRefreshRequest
        {
            RefreshToken = ResolveRefreshToken(request),
            TokenVersion = request.TokenVersion
        };

        var result = await _authService.RefreshAsync(refreshRequest, cancellationToken);
        if (!result.Success || result.Response == null)
        {
            if (string.Equals(result.Error, "INVALID_REQUEST", StringComparison.Ordinal))
            {
                return ErrorResponse(
                    StatusCodes.Status400BadRequest,
                    "VALIDATION_ERROR",
                    result.Message ?? "Refresh token is required.");
            }

            if (string.Equals(result.Error, "SESSION_REVOKED", StringComparison.Ordinal))
            {
                return ErrorResponse(
                    StatusCodes.Status401Unauthorized,
                    "SESSION_REVOKED",
                    result.Message ?? "Session has been revoked. Please log in again.");
            }

            if (string.Equals(result.Error, "SUPER_ADMIN_BANNED", StringComparison.Ordinal) ||
                string.Equals(result.Error, "SUPER_ADMIN_SUSPENDED", StringComparison.Ordinal) ||
                string.Equals(result.Error, "SUPER_ADMIN_FINANCIAL_LOCKED", StringComparison.Ordinal))
            {
                return ErrorResponse(
                    StatusCodes.Status403Forbidden,
                    result.Error,
                    result.Message ?? "Current Super Admin governance state does not allow session refresh.");
            }

            return ErrorResponse(
                StatusCodes.Status401Unauthorized,
                "AUTH_UNAUTHORIZED",
                result.Message ?? "Invalid or expired refresh token.");
        }

        AuthCookieConfiguration.SetSessionCookies(
            HttpContext,
            AuthCookieConfiguration.SuperAdmin,
            result.Response.AccessToken,
            ResolveLifetime(result.Response.ExpiresAtUtc),
            result.Response.RefreshToken,
            ResolveLifetime(result.Response.RefreshTokenExpiresAtUtc));

        return OkResponse(ToClientSession(result.Response));
    }

    [HttpPost("logout")]
    [Authorize(Policy = SuperAdminSecurityConstants.PolicyName)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        var superAdminIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(superAdminIdClaim) || !Guid.TryParse(superAdminIdClaim, out var superAdminId))
        {
            return ErrorResponse(
                StatusCodes.Status401Unauthorized,
                "AUTH_UNAUTHORIZED",
                "Unable to identify the Super Admin from the access token.");
        }

        var result = await _authService.LogoutAsync(superAdminId, cancellationToken);
        if (!result.Success)
        {
            return ErrorResponse(
                StatusCodes.Status401Unauthorized,
                "AUTH_UNAUTHORIZED",
                result.Message ?? "Logout failed.");
        }

        AuthCookieConfiguration.ClearSessionCookies(HttpContext, AuthCookieConfiguration.SuperAdmin);

        return OkResponse(new { message = "Logged out successfully." });
    }

    [HttpGet("session")]
    [Authorize(Policy = SuperAdminSecurityConstants.PolicyName)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Session(CancellationToken cancellationToken)
    {
        var superAdminIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(superAdminIdClaim) || !Guid.TryParse(superAdminIdClaim, out var superAdminId))
        {
            return ErrorResponse(
                StatusCodes.Status401Unauthorized,
                "AUTH_UNAUTHORIZED",
                "Unable to identify the Super Admin from the session cookie.");
        }

        var superAdmin = await _dbContext.SuperAdmins
            .AsNoTracking()
            .FirstOrDefaultAsync(admin => admin.Id == superAdminId, cancellationToken);

        if (superAdmin == null)
        {
            return ErrorResponse(
                StatusCodes.Status401Unauthorized,
                "AUTH_UNAUTHORIZED",
                "Unable to find the current Super Admin session.");
        }

        AuthCookieConfiguration.EnsureCsrfCookie(HttpContext);

        var response = new SuperAdminLoginResponse
        {
            SuperAdminId = superAdmin.Id,
            Email = superAdmin.Email,
            Role = SuperAdminSecurityConstants.Role,
            ExpiresAtUtc = ResolveAccessTokenExpiry(),
            RefreshTokenExpiresAtUtc = superAdmin.RefreshTokenExpiresAtUtc ?? DateTime.UtcNow,
            TokenVersion = superAdmin.TokenVersion,
            AccessToken = string.Empty,
            RefreshToken = string.Empty,
            NavigationCapabilities = NavigationCapabilities
        };

        return OkResponse(response);
    }

    private string ResolveRefreshToken(SuperAdminRefreshRequest? request)
    {
        if (Request.Cookies.TryGetValue(AuthCookieConfiguration.SuperAdminRefreshTokenCookieName, out var refreshToken) &&
            !string.IsNullOrWhiteSpace(refreshToken))
        {
            return refreshToken;
        }

        return request?.RefreshToken ?? string.Empty;
    }

    private DateTime ResolveAccessTokenExpiry()
    {
        return AuthCookieConfiguration.TryReadJwtExpiryUtc(
            Request.Cookies[AuthCookieConfiguration.SuperAdminAccessTokenCookieName],
            out var expiresAtUtc)
            ? expiresAtUtc
            : DateTime.UtcNow;
    }

    private static TimeSpan ResolveLifetime(DateTime expiresAtUtc)
    {
        var lifetime = expiresAtUtc - DateTime.UtcNow;
        return lifetime > TimeSpan.Zero ? lifetime : TimeSpan.FromMinutes(1);
    }

    private static SuperAdminLoginResponse ToClientSession(SuperAdminLoginResponse response) => new()
    {
        SuperAdminId = response.SuperAdminId,
        Email = response.Email,
        Role = response.Role,
        TokenType = response.TokenType,
        ExpiresAtUtc = response.ExpiresAtUtc,
        RefreshTokenExpiresAtUtc = response.RefreshTokenExpiresAtUtc,
        TokenVersion = response.TokenVersion,
        NavigationCapabilities = NavigationCapabilities,
        AccessToken = string.Empty,
        RefreshToken = string.Empty
    };
}
