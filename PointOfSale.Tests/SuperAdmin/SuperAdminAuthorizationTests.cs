using System.Reflection;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.API.SuperAdmin.Controllers;
using PointOfSale.API.SuperAdmin.Infrastructure;
using PointOfSale.API.SuperAdmin.Models;

namespace PointOfSale.Tests.SuperAdmin;

public class SuperAdminAuthorizationTests
{
    [Fact]
    public void SuperAdminBaseController_Requires_SuperAdminPolicy()
    {
        var authorizeAttribute = typeof(SuperAdminBaseController)
            .GetCustomAttribute<AuthorizeAttribute>(inherit: true);

        Assert.NotNull(authorizeAttribute);
        Assert.Equal(SuperAdminSecurityConstants.PolicyName, authorizeAttribute!.Policy);
        Assert.True(typeof(SuperAdminBaseController).IsAssignableFrom(typeof(SuperAdminFinanceController)));
    }

    [Fact]
    public async Task AuthorizationResultHandler_Returns_Standardized_Unauthorized_Response()
    {
        var handler = new SuperAdminAuthorizationMiddlewareResultHandler(
            NullLogger<SuperAdminAuthorizationMiddlewareResultHandler>.Instance);
        var httpContext = new DefaultHttpContext
        {
            TraceIdentifier = "req-auth-401"
        };
        httpContext.Request.Path = "/api/super-admin/finance/overview";
        httpContext.Response.Body = new MemoryStream();

        var policy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .Build();

        await handler.HandleAsync(
            _ => Task.CompletedTask,
            httpContext,
            policy,
            PolicyAuthorizationResult.Challenge());

        Assert.Equal(StatusCodes.Status401Unauthorized, httpContext.Response.StatusCode);

        httpContext.Response.Body.Position = 0;
        using var reader = new StreamReader(httpContext.Response.Body, Encoding.UTF8, leaveOpen: true);
        var body = await reader.ReadToEndAsync();
        Assert.Contains("AUTH_UNAUTHORIZED", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AuthorizationResultHandler_Returns_Standardized_Forbidden_Response()
    {
        var handler = new SuperAdminAuthorizationMiddlewareResultHandler(
            NullLogger<SuperAdminAuthorizationMiddlewareResultHandler>.Instance);
        var httpContext = new DefaultHttpContext
        {
            TraceIdentifier = "req-auth-403"
        };
        httpContext.Request.Path = "/api/super-admin/finance/overview";
        httpContext.Response.Body = new MemoryStream();

        var policy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .Build();

        await handler.HandleAsync(
            _ => Task.CompletedTask,
            httpContext,
            policy,
            PolicyAuthorizationResult.Forbid());

        Assert.Equal(StatusCodes.Status403Forbidden, httpContext.Response.StatusCode);

        httpContext.Response.Body.Position = 0;
        using var reader = new StreamReader(httpContext.Response.Body, Encoding.UTF8, leaveOpen: true);
        var body = await reader.ReadToEndAsync();
        Assert.Contains("AUTH_FORBIDDEN", body, StringComparison.Ordinal);
    }

    [Fact]
    public void TokenClaimsValidator_Accepts_Required_SuperAdmin_Claims()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                new Claim(ClaimTypes.Role, SuperAdminSecurityConstants.Role),
                new Claim(SuperAdminSecurityConstants.AuthTypeClaim, SuperAdminSecurityConstants.AuthTypeValue),
                new Claim(SuperAdminSecurityConstants.TokenVersionClaim, "7")
            },
            authenticationType: SuperAdminSecurityConstants.AuthenticationScheme));

        Assert.True(SuperAdminTokenClaimsValidator.HasRequiredClaims(principal));
    }

    [Fact]
    public void TokenClaimsValidator_Rejects_Missing_AuthType()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                new Claim(ClaimTypes.Role, SuperAdminSecurityConstants.Role),
                new Claim(SuperAdminSecurityConstants.TokenVersionClaim, "7")
            },
            authenticationType: SuperAdminSecurityConstants.AuthenticationScheme));

        Assert.False(SuperAdminTokenClaimsValidator.HasRequiredClaims(principal));
    }

    [Fact]
    public void TokenClaimsValidator_Rejects_Invalid_AuthType()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                new Claim(ClaimTypes.Role, SuperAdminSecurityConstants.Role),
                new Claim(SuperAdminSecurityConstants.AuthTypeClaim, "pos_admin"),
                new Claim(SuperAdminSecurityConstants.TokenVersionClaim, "7")
            },
            authenticationType: SuperAdminSecurityConstants.AuthenticationScheme));

        Assert.False(SuperAdminTokenClaimsValidator.HasRequiredClaims(principal));
    }
}
