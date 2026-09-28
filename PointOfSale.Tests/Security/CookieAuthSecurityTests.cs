using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;
using PointOfSale.API.Controllers;
using PointOfSale.API.DTOs.Customers;
using PointOfSale.API.Middleware;
using PointOfSale.API.Security;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.API.Services.Storefront;

namespace PointOfSale.Tests.Security;

public sealed class CookieAuthSecurityTests
{
    [Fact]
    public void CreateHttpOnlyOptions_MarksTokenCookies_AsHttpOnlySecureAndStrict()
    {
        var options = AuthCookieConfiguration.CreateHttpOnlyOptions(TimeSpan.FromMinutes(15), "/api/customers");

        Assert.True(options.HttpOnly);
        Assert.True(options.Secure);
        Assert.Equal(SameSiteMode.Strict, options.SameSite);
        Assert.Equal("/api/customers", options.Path);
    }

    [Theory]
    [InlineData("Development", false, false)]
    [InlineData("Development", true, true)]
    [InlineData("Production", false, true)]
    public void SetSessionCookies_UsesInsecureCookiesOnlyForDevelopmentHttp(
        string environmentName,
        bool isHttps,
        bool expectsSecure)
    {
        var environment = new Mock<IHostEnvironment>();
        environment.SetupGet(value => value.EnvironmentName).Returns(environmentName);
        var services = new ServiceCollection()
            .AddSingleton(environment.Object)
            .BuildServiceProvider();
        var context = new DefaultHttpContext
        {
            RequestServices = services
        };
        context.Request.Scheme = isHttps ? "https" : "http";

        AuthCookieConfiguration.SetSessionCookies(
            context,
            AuthCookieConfiguration.Pos,
            "access-token",
            TimeSpan.FromMinutes(15),
            "refresh-token",
            TimeSpan.FromDays(7));

        var issuedCookies = context.Response.Headers.SetCookie
            .Where(value => value is not null &&
                (value.StartsWith($"{AuthCookieConfiguration.PosAccessTokenCookieName}=", StringComparison.Ordinal) ||
                 value.StartsWith($"{AuthCookieConfiguration.PosRefreshTokenCookieName}=", StringComparison.Ordinal) ||
                 value.StartsWith($"{AuthCookieConfiguration.CsrfCookieName}=", StringComparison.Ordinal)))
            .ToArray();

        Assert.Equal(3, issuedCookies.Length);
        Assert.All(issuedCookies, value =>
            Assert.Equal(expectsSecure, value!.Contains("secure", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void StorefrontCookieHelper_SetAccessTokenCookie_ScopesAccessCookieToStorefrontAndPayments()
    {
        var helper = CreateStorefrontCookieHelper();
        var httpContext = new DefaultHttpContext();

        helper.SetAccessTokenCookie(
            httpContext.Response,
            "access-token",
            DateTime.UtcNow.AddMinutes(15));

        var setCookieHeaders = httpContext.Response.Headers["Set-Cookie"].ToArray();

        Assert.Contains(setCookieHeaders, value =>
            value.StartsWith($"{AuthCookieConfiguration.StorefrontAccessTokenCookieName}=access-token", StringComparison.Ordinal) &&
            value.Contains("path=/api/storefront", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(setCookieHeaders, value =>
            value.StartsWith($"{AuthCookieConfiguration.StorefrontAccessTokenCookieName}=access-token", StringComparison.Ordinal) &&
            value.Contains("path=/api/payments", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(setCookieHeaders, value =>
            value.StartsWith($"{AuthCookieConfiguration.StorefrontAccessTokenCookieName}=", StringComparison.Ordinal) &&
            value.Contains("path=/api;", StringComparison.OrdinalIgnoreCase) &&
            value.Contains("expires=", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void StorefrontCookieHelper_ClearAccessTokenCookie_ExpiresStorefrontPaymentsAndBroadApiCookiePaths()
    {
        var helper = CreateStorefrontCookieHelper();
        var httpContext = new DefaultHttpContext();

        helper.ClearAccessTokenCookie(httpContext.Response);

        var setCookieHeaders = httpContext.Response.Headers["Set-Cookie"].ToArray();
        var headerDump = string.Join(Environment.NewLine, setCookieHeaders);

        var clearsStorefrontPath = setCookieHeaders.Any(value =>
            value.StartsWith($"{AuthCookieConfiguration.StorefrontAccessTokenCookieName}=", StringComparison.Ordinal) &&
            value.Contains("path=/api/storefront", StringComparison.OrdinalIgnoreCase) &&
            value.Contains("expires=", StringComparison.OrdinalIgnoreCase));
        var clearsPaymentsPath = setCookieHeaders.Any(value =>
            value.StartsWith($"{AuthCookieConfiguration.StorefrontAccessTokenCookieName}=", StringComparison.Ordinal) &&
            value.Contains("path=/api/payments", StringComparison.OrdinalIgnoreCase) &&
            value.Contains("expires=", StringComparison.OrdinalIgnoreCase));
        var clearsBroadApiPath = setCookieHeaders.Any(value =>
            value.StartsWith($"{AuthCookieConfiguration.StorefrontAccessTokenCookieName}=", StringComparison.Ordinal) &&
            value.Contains("path=/api;", StringComparison.OrdinalIgnoreCase) &&
            value.Contains("expires=", StringComparison.OrdinalIgnoreCase));

        Assert.True(clearsStorefrontPath, headerDump);
        Assert.True(clearsPaymentsPath, headerDump);
        Assert.True(clearsBroadApiPath, headerDump);
    }

    [Fact]
    public async Task CookieCsrfProtectionMiddleware_BlocksUnsafeCookieAuthenticatedRequest_WithoutMatchingHeader()
    {
        var nextCalled = false;
        var middleware = new CookieCsrfProtectionMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Headers["Cookie"] =
            $"{AuthCookieConfiguration.PosAccessTokenCookieName}=token; {AuthCookieConfiguration.CsrfCookieName}=cookie-value";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);

        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, Encoding.UTF8, leaveOpen: true);
        var body = await reader.ReadToEndAsync();
        Assert.Contains("CSRF_VALIDATION_FAILED", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CookieCsrfProtectionMiddleware_AllowsUnsafeCookieAuthenticatedRequest_WithMatchingHeader()
    {
        var nextCalled = false;
        var middleware = new CookieCsrfProtectionMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Headers["Cookie"] =
            $"{AuthCookieConfiguration.PosAccessTokenCookieName}=token; {AuthCookieConfiguration.CsrfCookieName}=cookie-value";
        context.Request.Headers[AuthCookieConfiguration.CsrfHeaderName] = "cookie-value";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
        Assert.NotEqual(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [Fact]
    public async Task CookieCsrfProtectionMiddleware_AllowsLeaseTokenAuthorizedUploadEndpoint_WithoutCsrfHeader()
    {
        var nextCalled = false;
        var middleware = new CookieCsrfProtectionMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var context = new DefaultHttpContext();
        context.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(new LeaseTokenAuthorizedUploadAttribute()),
            "development checkout attachment upload"));
        context.Request.Method = HttpMethods.Put;
        context.Request.Path = "/api/storefront/checkout/attachments/development-upload";
        context.Request.Headers["Cookie"] =
            $"{AuthCookieConfiguration.StorefrontAccessTokenCookieName}=token; {AuthCookieConfiguration.CsrfCookieName}=cookie-value";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
        Assert.NotEqual(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [Fact]
    public async Task CookieCsrfProtectionMiddleware_BlocksUnmarkedDevelopmentUploadPath_WithoutCsrfHeader()
    {
        var nextCalled = false;
        var middleware = new CookieCsrfProtectionMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Put;
        context.Request.Path = "/api/storefront/checkout/attachments/development-upload";
        context.Request.Headers["Cookie"] =
            $"{AuthCookieConfiguration.StorefrontAccessTokenCookieName}=token; {AuthCookieConfiguration.CsrfCookieName}=cookie-value";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [Fact]
    public async Task CustomersAuthController_Login_SetsHttpOnlyCookie_And_SanitizesResponseBody()
    {
        var authService = new Mock<ICommerceHubCustomerAuthService>();
        authService
            .Setup(service => service.LoginAsync(It.IsAny<CustomerLoginRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommerceHubCustomerAuthResult
            {
                Success = true,
                Response = new CustomerAuthResponseDto
                {
                    AccessToken = "server-only-token",
                    ExpiresAt = DateTime.UtcNow.AddMinutes(15),
                    Customer = new CustomerProfileDto
                    {
                        Id = Guid.NewGuid(),
                        Email = "customer@example.com",
                        Name = "Secure Customer",
                        CreatedAt = DateTime.UtcNow
                    }
                }
            });

        var controller = CreateCustomersAuthController(authService.Object);

        var result = await controller.Login(
            new CustomerLoginRequestDto
            {
                Email = "customer@example.com",
                Password = "P@ssw0rd!"
            },
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<CustomerAuthResponseDto>(ok.Value);
        Assert.Equal(string.Empty, response.AccessToken);

        var setCookieHeaders = controller.HttpContext.Response.Headers["Set-Cookie"];
        Assert.Contains(setCookieHeaders, value =>
            value.StartsWith("commercehub_customer_access_token=", StringComparison.Ordinal) &&
            value.Contains("path=/api/customers", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(setCookieHeaders, value =>
            value.StartsWith("commercehub_customer_access_token=", StringComparison.Ordinal) &&
            value.Contains("path=/api/commercehub", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(setCookieHeaders, value => value.Contains("commercehub_order_access=", StringComparison.Ordinal));
        Assert.Contains(setCookieHeaders, value => value.Contains("httponly", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(setCookieHeaders, value => value.Contains("secure", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(setCookieHeaders, value => value.Contains("samesite=strict", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CustomersAuthController_Logout_RevokesToken_And_ExpiresAccessCookie()
    {
        const string SeedToken = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.seed";

        var revocationMock = new Mock<ICustomerTokenRevocationService>(MockBehavior.Strict);
        revocationMock
            .Setup(svc => svc.RevokeTokenAsync(
                SeedToken,
                "logout",
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var controller = CreateCustomersAuthController(
            Mock.Of<ICommerceHubCustomerAuthService>(),
            revocationMock.Object,
            cookies: new Dictionary<string, string>
            {
                [AuthCookieConfiguration.CommerceHubCustomerAccessTokenCookieName] = SeedToken
            });

        var result = await controller.Logout(CancellationToken.None);

        // Assert: RevokeTokenAsync must have been called exactly once before the cookie was expired
        revocationMock.Verify(
            svc => svc.RevokeTokenAsync(SeedToken, "logout", It.IsAny<CancellationToken>()),
            Times.Once);

        Assert.IsType<OkObjectResult>(result);

        var setCookieHeaders = controller.HttpContext.Response.Headers["Set-Cookie"];
        Assert.Contains(setCookieHeaders, value =>
            value.StartsWith("commercehub_customer_access_token=", StringComparison.Ordinal) &&
            value.Contains("path=/api/customers", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(setCookieHeaders, value =>
            value.StartsWith("commercehub_customer_access_token=", StringComparison.Ordinal) &&
            value.Contains("path=/api/commercehub", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(setCookieHeaders, value => value.Contains("commercehub_order_access=", StringComparison.Ordinal));
        Assert.Contains(setCookieHeaders, value => value.Contains("expires=", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(setCookieHeaders, value => value.Contains("path=/api/customers", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(setCookieHeaders, value => value.Contains("path=/api/commercehub", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CustomersAuthController_Logout_WhenRevocationFails_ReturnsServiceUnavailable()
    {
        const string SeedToken = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.seed";

        var revocationMock = new Mock<ICustomerTokenRevocationService>(MockBehavior.Strict);
        revocationMock
            .Setup(svc => svc.RevokeTokenAsync(
                SeedToken,
                "logout",
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TokenRevocationPersistenceException("Database unavailable"));

        var controller = CreateCustomersAuthController(
            Mock.Of<ICommerceHubCustomerAuthService>(),
            revocationMock.Object,
            cookies: new Dictionary<string, string>
            {
                [AuthCookieConfiguration.CommerceHubCustomerAccessTokenCookieName] = SeedToken
            });

        var result = await controller.Logout(CancellationToken.None);

        // Assert: RevokeTokenAsync must have been called exactly once
        revocationMock.Verify(
            svc => svc.RevokeTokenAsync(SeedToken, "logout", It.IsAny<CancellationToken>()),
            Times.Once);

        // Assert: Fail-closed behavior - return 503 but STILL clear the cookie
        var statusCodeResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, statusCodeResult.StatusCode);

        // Assert: Cookie WAS expired (Set-Cookie header present with expiry directive)
        var setCookieHeaders = controller.HttpContext.Response.Headers["Set-Cookie"];
        Assert.NotEqual(0, setCookieHeaders.Count);
        
        // Verify the cookie is being cleared (contains the cookie name and an expiry directive)
        var cookieHeader = setCookieHeaders.ToString();
        Assert.Contains(AuthCookieConfiguration.CommerceHubCustomerAccessTokenCookieName, cookieHeader, StringComparison.Ordinal);
        Assert.Contains(CommerceHubOrderAccessCookie.CookieName, cookieHeader, StringComparison.Ordinal);
        
        // Verify expiry directive is present (either "expires=" with past date or "max-age=0")
        Assert.True(
            cookieHeader.Contains("expires=", StringComparison.OrdinalIgnoreCase) ||
            cookieHeader.Contains("max-age=0", StringComparison.OrdinalIgnoreCase),
            "Cookie header should contain an expiry directive (expires= or max-age=0)");
    }

    [Fact]
    public async Task CustomersAuthController_Logout_WhenSingleTokenRevocationFailsButCutoffFallbackSucceeds_ReturnsOk()
    {
        const string SeedToken = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.seed";
        var customerId = Guid.NewGuid();

        var revocationMock = new Mock<ICustomerTokenRevocationService>(MockBehavior.Strict);
        revocationMock
            .Setup(svc => svc.RevokeTokenAsync(
                SeedToken,
                "logout",
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TokenRevocationPersistenceException("Database unavailable", subjectId: customerId));
        revocationMock
            .Setup(svc => svc.RevokeAllTokensAsync(
                customerId,
                "logout_blacklist_persistence_fallback",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(DateTime.UtcNow);

        var controller = CreateCustomersAuthController(
            Mock.Of<ICommerceHubCustomerAuthService>(),
            revocationMock.Object,
            cookies: new Dictionary<string, string>
            {
                [AuthCookieConfiguration.CommerceHubCustomerAccessTokenCookieName] = SeedToken
            });

        var result = await controller.Logout(CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        revocationMock.Verify(
            svc => svc.RevokeAllTokensAsync(
                customerId,
                "logout_blacklist_persistence_fallback",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CustomersAuthController_Logout_WithoutAccessTokenCookie_StillExpiresRecoveryCookie()
    {
        var revocationMock = new Mock<ICustomerTokenRevocationService>(MockBehavior.Strict);
        var controller = CreateCustomersAuthController(
            Mock.Of<ICommerceHubCustomerAuthService>(),
            revocationMock.Object);

        var result = await controller.Logout(CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        revocationMock.VerifyNoOtherCalls();

        var setCookieHeaders = controller.HttpContext.Response.Headers["Set-Cookie"];
        Assert.Contains(setCookieHeaders, value => value.Contains("commercehub_customer_access_token=", StringComparison.Ordinal));
        Assert.Contains(setCookieHeaders, value => value.Contains("commercehub_order_access=", StringComparison.Ordinal));
        Assert.Contains(setCookieHeaders, value => value.Contains("expires=", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(setCookieHeaders, value => value.Contains("path=/api/customers", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(setCookieHeaders, value => value.Contains("path=/api/commercehub", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CustomersAuthController_RevokeAllSessions_RevokesDurableCutoff_And_ExpiresCookie()
    {
        var customerId = Guid.NewGuid();
        var revocationMock = new Mock<ICustomerTokenRevocationService>(MockBehavior.Strict);
        revocationMock
            .Setup(svc => svc.RevokeAllTokensAsync(
                customerId,
                "customer_requested_revoke_all_sessions",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(DateTime.UtcNow);

        var principal = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(
            [
                new System.Security.Claims.Claim("CustomerId", customerId.ToString()),
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, CustomerSecurityConstants.Role)
            ],
            CustomerSecurityConstants.AuthenticationScheme));

        var controller = CreateCustomersAuthController(
            Mock.Of<ICommerceHubCustomerAuthService>(),
            revocationMock.Object,
            user: principal);

        var result = await controller.RevokeAllSessions(CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        var setCookieHeaders = controller.HttpContext.Response.Headers["Set-Cookie"];
        Assert.Contains(setCookieHeaders, value => value.Contains("commercehub_customer_access_token=", StringComparison.Ordinal));
        Assert.Contains(setCookieHeaders, value => value.Contains("commercehub_order_access=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CustomersAuthController_RevokeAllSessions_OnPersistenceFailure_Returns503_And_ExpiresCookie()
    {
        var customerId = Guid.NewGuid();
        var revocationMock = new Mock<ICustomerTokenRevocationService>(MockBehavior.Strict);
        revocationMock
            .Setup(svc => svc.RevokeAllTokensAsync(
                customerId,
                "customer_requested_revoke_all_sessions",
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TokenRevocationPersistenceException("Database unavailable", subjectId: customerId));

        var principal = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(
            [
                new System.Security.Claims.Claim("CustomerId", customerId.ToString()),
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, CustomerSecurityConstants.Role)
            ],
            CustomerSecurityConstants.AuthenticationScheme));

        var controller = CreateCustomersAuthController(
            Mock.Of<ICommerceHubCustomerAuthService>(),
            revocationMock.Object,
            user: principal);

        var result = await controller.RevokeAllSessions(CancellationToken.None);

        var statusCodeResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, statusCodeResult.StatusCode);
        
        var setCookieHeaders = controller.HttpContext.Response.Headers["Set-Cookie"];
        Assert.Contains(setCookieHeaders, value => value.Contains("commercehub_customer_access_token=", StringComparison.Ordinal));
        Assert.Contains(setCookieHeaders, value => value.Contains("commercehub_order_access=", StringComparison.Ordinal));
    }

    private static CustomersAuthController CreateCustomersAuthController(
        ICommerceHubCustomerAuthService authService,
        ICustomerTokenRevocationService? tokenRevocationService = null,
        Dictionary<string, string>? cookies = null,
        System.Security.Claims.ClaimsPrincipal? user = null)
    {
        var httpContext = new DefaultHttpContext();

        if (cookies != null)
        {
            // Seed request cookies so the controller's cookie-read path is exercised.
            httpContext.Request.Headers["Cookie"] = string.Join("; ",
                cookies.Select(kvp => $"{kvp.Key}={kvp.Value}"));
        }

        if (user != null)
        {
            httpContext.User = user;
        }

        var mockEnv = new Mock<IWebHostEnvironment>();
        mockEnv.SetupGet(e => e.EnvironmentName).Returns("Development");

        var controller = new CustomersAuthController(
            authService,
            tokenRevocationService ?? Mock.Of<ICustomerTokenRevocationService>(),
            mockEnv.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            }
        };

        controller.HttpContext.Response.Body = new MemoryStream();
        return controller;
    }

    private static StorefrontCookieHelper CreateStorefrontCookieHelper()
    {
        var settings = Options.Create(new StorefrontCookieSettings
        {
            AccessTokenCookiePath = StorefrontCookieSettings.DefaultAccessTokenCookiePath,
            PaymentAccessTokenCookiePath = StorefrontCookieSettings.DefaultPaymentAccessTokenCookiePath,
            CookiePath = "/api/storefront/auth",
            SameSiteMode = "Lax",
            AllowNonSecureInDevelopment = true
        });

        var mockEnv = new Mock<IWebHostEnvironment>();
        mockEnv.SetupGet(e => e.EnvironmentName).Returns("Development");

        return new StorefrontCookieHelper(settings, mockEnv.Object);
    }
}
