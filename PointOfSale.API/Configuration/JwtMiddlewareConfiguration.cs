using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using PointOfSale.API.Middleware;
using PointOfSale.API.Models;
using PointOfSale.API.Security;
using System.Text;

namespace PointOfSale.API.Configuration;

/// <summary>
/// PHASE CR-1: JWT Middleware Configuration with Cookie Support
/// 
/// SECURITY HARDENING:
/// - Reads JWT from the HttpOnly POS session cookie
/// - Falls back to Authorization header (REST APIs)
/// - Validates tenant_id claim exists
/// - No token logging
/// 
/// ZERO-TRUST PRINCIPLES:
/// - Cookie-first authentication
/// - Server-authoritative tenant
/// - Claim validation on every request
/// </summary>
public static class JwtMiddlewareConfiguration
{
    public static IServiceCollection AddHardenedJwtAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var jwtSettings = configuration.GetSection("Jwt").Get<JwtSettings>()
            ?? throw new InvalidOperationException("JWT settings not configured");

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = jwtSettings.Issuer,
                ValidAudience = jwtSettings.Audience,
                IssuerSigningKey = new SymmetricSecurityKey(
                    Encoding.UTF8.GetBytes(jwtSettings.Secret)),
                ClockSkew = TimeSpan.Zero // No tolerance for expired tokens
            };

            options.Events = new JwtBearerEvents
            {
                // PHASE CR-1: Cookie-first token extraction
                OnMessageReceived = context =>
                {
                    // Priority 1: HttpOnly POS session cookie
                    if (context.Request.Cookies.TryGetValue(AuthCookieConfiguration.PosAccessTokenCookieName, out var cookieToken))
                    {
                        context.Token = cookieToken;
                        return Task.CompletedTask;
                    }

                    // Priority 2: Authorization header (for REST APIs)
                    if (context.Request.Headers.ContainsKey("Authorization"))
                    {
                        var authHeader = context.Request.Headers["Authorization"].ToString();
                        if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                        {
                            context.Token = authHeader.Substring("Bearer ".Length).Trim();
                        }
                    }

                    return Task.CompletedTask;
                },

                // PHASE CR-1: Validate tenant claim exists
                OnTokenValidated = context =>
                {
                    var tenantClaim = context.Principal?.FindFirst("tenant_id")?.Value;
                    if (string.IsNullOrEmpty(tenantClaim))
                    {
                        context.Fail("Missing tenant_id claim in JWT");
                        return Task.CompletedTask;
                    }

                    // Validate tenant claim is a valid GUID
                    if (!Guid.TryParse(tenantClaim, out _))
                    {
                        context.Fail("Invalid tenant_id claim format");
                        return Task.CompletedTask;
                    }

                    return Task.CompletedTask;
                },

                // PHASE CR-1: Enhanced error logging (no token exposure)
                OnAuthenticationFailed = context =>
                {
                    var logger = context.HttpContext.RequestServices
                        .GetRequiredService<ILogger<Program>>();

                    logger.LogWarning(
                        "JWT authentication failed: {Error} | Path: {Path} | IP: {IP}",
                        context.Exception.Message,
                        PublicFeedPathRedactor.Redact(context.Request.Path.Value ?? "unknown"),
                        context.HttpContext.Connection.RemoteIpAddress
                    );

                    return Task.CompletedTask;
                },

                // PHASE CR-1: Challenge response (no token hints)
                OnChallenge = context =>
                {
                    // Suppress default response to avoid leaking auth details
                    context.HandleResponse();

                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    context.Response.ContentType = "application/json";

                    var response = new
                    {
                        error = "unauthorized",
                        message = "Authentication required"
                    };

                    return context.Response.WriteAsJsonAsync(response);
                }
            };

            // PHASE CR-1: SignalR-specific configuration
            options.Events.OnMessageReceived = async context =>
            {
                // Check if this is a SignalR request
                var path = context.HttpContext.Request.Path;
                if (path.StartsWithSegments("/hubs"))
                {
                    // SignalR MUST use cookie authentication
                    if (context.Request.Cookies.TryGetValue(AuthCookieConfiguration.PosAccessTokenCookieName, out var token))
                    {
                        context.Token = token;
                    }
                    else
                    {
                        // Log unauthorized SignalR attempt
                        var logger = context.HttpContext.RequestServices
                            .GetRequiredService<ILogger<Program>>();

                        logger.LogWarning(
                            "SignalR connection attempt without auth cookie | Path: {Path} | IP: {IP}",
                            PublicFeedPathRedactor.Redact(path.Value ?? "unknown"),
                            context.HttpContext.Connection.RemoteIpAddress
                        );
                    }
                }
                else
                {
                    // REST API: Try cookie first, then header
                    if (context.Request.Cookies.TryGetValue(AuthCookieConfiguration.PosAccessTokenCookieName, out var cookieToken))
                    {
                        context.Token = cookieToken;
                    }
                    else if (context.Request.Headers.ContainsKey("Authorization"))
                    {
                        var authHeader = context.Request.Headers["Authorization"].ToString();
                        if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                        {
                            context.Token = authHeader.Substring("Bearer ".Length).Trim();
                        }
                    }
                }

                await Task.CompletedTask;
            };
        });

        return services;
    }
}
