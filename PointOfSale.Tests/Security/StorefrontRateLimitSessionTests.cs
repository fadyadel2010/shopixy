using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PointOfSale.API.Security;

namespace PointOfSale.Tests.Security;

public sealed class StorefrontRateLimitSessionTests
{
    [Fact]
    public async Task ResolveTrustedSubjectAsync_WhenAuthenticatedCustomerExists_IgnoresSpoofedHeadersAndCookie()
    {
        var customerId = Guid.NewGuid();
        var httpContext = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection()
                .AddSingleton(CreateAuthenticationService(storefrontCustomerId: customerId))
                .BuildServiceProvider()
        };

        httpContext.Request.Headers["X-Customer-Id"] = Guid.NewGuid().ToString("D");
        httpContext.Request.Headers["X-Session-Id"] = "attacker-session";
        httpContext.Request.Headers["Cookie"] = $"{StorefrontRateLimitSession.CookieName}=abcdef0123456789abcdef0123456789";

        var subject = await StorefrontRateLimitSession.ResolveTrustedSubjectAsync(httpContext, "203.0.113.10");

        Assert.Equal($"customer:{customerId:N}", subject);
    }

    [Fact]
    public async Task ResolveTrustedSubjectAsync_WhenServerCookieExists_IgnoresSpoofedHeaders()
    {
        const string trustedSessionId = "abcdef0123456789abcdef0123456789";
        var httpContext = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection()
                .AddSingleton(CreateAuthenticationService())
                .BuildServiceProvider()
        };

        httpContext.Request.Headers["X-Customer-Id"] = Guid.NewGuid().ToString("D");
        httpContext.Request.Headers["X-Session-Id"] = "attacker-session";
        httpContext.Request.Headers["Cookie"] = $"{StorefrontRateLimitSession.CookieName}={trustedSessionId}";

        var subject = await StorefrontRateLimitSession.ResolveTrustedSubjectAsync(httpContext, "203.0.113.10");

        Assert.Equal($"session:{trustedSessionId}", subject);
    }

    [Fact]
    public async Task ResolveTrustedSubjectAsync_WhenOnlySpoofedHeadersExist_FallsBackToAnonymousPartition()
    {
        var httpContext = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection()
                .AddSingleton(CreateAuthenticationService())
                .BuildServiceProvider()
        };

        httpContext.Request.Headers["X-Customer-Id"] = Guid.NewGuid().ToString("D");
        httpContext.Request.Headers["X-Session-Id"] = "attacker-session";

        var subject = await StorefrontRateLimitSession.ResolveTrustedSubjectAsync(httpContext, "203.0.113.10");

        Assert.StartsWith("anonymous:", subject, StringComparison.Ordinal);
        Assert.DoesNotContain("attacker-session", subject, StringComparison.Ordinal);
    }

    private static IAuthenticationService CreateAuthenticationService(
        Guid? storefrontCustomerId = null,
        Guid? customerJwtId = null)
    {
        var authenticationService = new Mock<IAuthenticationService>();

        authenticationService
            .Setup(service => service.AuthenticateAsync(
                It.IsAny<HttpContext>(),
                "StorefrontJwt"))
            .ReturnsAsync(storefrontCustomerId.HasValue
                ? AuthenticateResult.Success(new AuthenticationTicket(
                    new ClaimsPrincipal(new ClaimsIdentity(
                        new[]
                        {
                            new Claim("CustomerId", storefrontCustomerId.Value.ToString("D")),
                            new Claim(ClaimTypes.NameIdentifier, storefrontCustomerId.Value.ToString("D"))
                        },
                        "StorefrontJwt")),
                    "StorefrontJwt"))
                : AuthenticateResult.NoResult());

        authenticationService
            .Setup(service => service.AuthenticateAsync(
                It.IsAny<HttpContext>(),
                "CustomerJwt"))
            .ReturnsAsync(customerJwtId.HasValue
                ? AuthenticateResult.Success(new AuthenticationTicket(
                    new ClaimsPrincipal(new ClaimsIdentity(
                        new[]
                        {
                            new Claim("CustomerId", customerJwtId.Value.ToString("D")),
                            new Claim(ClaimTypes.NameIdentifier, customerJwtId.Value.ToString("D"))
                        },
                        "CustomerJwt")),
                    "CustomerJwt"))
                : AuthenticateResult.NoResult());

        return authenticationService.Object;
    }
}
