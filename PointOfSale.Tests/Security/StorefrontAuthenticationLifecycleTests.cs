using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PointOfSale.API.Controllers;
using PointOfSale.API.Models;
using PointOfSale.API.Services.PhoneRisk;
using PointOfSale.API.Services.Storefront;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MultiTenancy;

namespace PointOfSale.Tests.Security;

public sealed class StorefrontAuthenticationLifecycleTests
{
    [Theory]
    [InlineData("alllowercase1")]
    [InlineData("ALLUPPERCASE1")]
    [InlineData("NoDigitsHere")]
    [InlineData("Short1A")]
    public async Task Register_RejectsPasswordsOutsidePublishedPolicy(string password)
    {
        var tenantId = Guid.NewGuid();
        await using var context = Context(tenantId);
        var service = Service(context);

        var result = await service.RegisterAsync(tenantId, new StorefrontRegisterRequest
        {
            Email = "policy@example.test",
            Password = password,
            DisplayName = "Policy Test"
        });

        Assert.False(result.Success);
        Assert.Equal("WEAK_PASSWORD", result.Error);
        Assert.Empty(await context.OnlineCustomers.ToListAsync());
    }

    [Fact]
    public async Task Register_NormalizesEmailHashesPasswordAndKeepsRegistrationUnauthenticated()
    {
        var tenantId = Guid.NewGuid();
        await using var context = Context(tenantId);
        var publisher = Publisher();
        var service = Service(context, publisher.Object);

        var result = await service.RegisterAsync(tenantId, new StorefrontRegisterRequest
        {
            Email = "  Customer@Example.Test  ",
            Password = "StrongPassword1",
            DisplayName = "Customer"
        });

        Assert.True(result.Success);
        Assert.NotNull(result.Response);
        Assert.Empty(result.Response.AccessToken);
        Assert.Empty(result.Response.RefreshToken);

        var customer = await context.OnlineCustomers.SingleAsync();
        Assert.Equal("customer@example.test", customer.Email);
        Assert.NotEqual("StrongPassword1", customer.PasswordHash);
        Assert.NotEqual(
            PasswordVerificationResult.Failed,
            new PasswordHasher<OnlineCustomer>().VerifyHashedPassword(
                customer,
                customer.PasswordHash!,
                "StrongPassword1"));
        publisher.Verify(
            value => value.PublishAsync(
                It.Is<OnlineCustomerRegisteredEvent>(@event =>
                    @event.TenantId == tenantId && @event.CustomerId == customer.Id),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Register_InvalidPhone_ReturnsFieldSpecific400WithoutCookies()
    {
        var tenantId = Guid.NewGuid();
        var auth = new Mock<IStorefrontAuthService>();
        auth.Setup(s => s.RegisterAsync(tenantId, It.IsAny<StorefrontRegisterRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StorefrontAuthResult { Error = "INVALID_PHONE", Message = "Enter a valid phone number." });
        var cookies = new Mock<IStorefrontCookieHelper>(MockBehavior.Strict);
        var controller = Controller(tenantId, auth.Object, Mock.Of<IStorefrontAuthRateLimiter>(), cookies.Object);
        var result = Assert.IsType<BadRequestObjectResult>(await controller.Register(new StorefrontRegisterRequest(), CancellationToken.None));
        var error = Assert.IsType<PointOfSale.API.DTOs.Storefront.ApiErrorResponse>(result.Value);
        Assert.Equal("INVALID_PHONE", error.Code);
        Assert.Contains("phone", System.Text.Json.JsonSerializer.Serialize(error.Details));
        cookies.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task LoginFailure_ClearsOlderBrowserCookiesAndMapsInfrastructureFailureTo503()
    {
        var tenantId = Guid.NewGuid();
        var authService = new Mock<IStorefrontAuthService>();
        authService
            .Setup(value => value.LoginAsync(
                tenantId,
                It.IsAny<StorefrontLoginRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StorefrontAuthResult
            {
                Success = false,
                Error = "LOGIN_FAILED",
                Message = "An error occurred during login"
            });
        var cookies = new Mock<IStorefrontCookieHelper>();
        var rateLimiter = new Mock<IStorefrontAuthRateLimiter>();
        rateLimiter
            .Setup(value => value.CheckLoginAsync(tenantId, It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(RateLimitResult.Allow(1, 5));
        var controller = Controller(tenantId, authService.Object, rateLimiter.Object, cookies.Object);

        var result = await controller.Login(
            new StorefrontLoginRequest { Email = "customer@example.test", Password = "not-recorded" },
            CancellationToken.None);

        var unavailable = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, unavailable.StatusCode);
        cookies.Verify(value => value.ClearAccessTokenCookie(controller.Response), Times.Once);
        cookies.Verify(value => value.ClearRefreshTokenCookie(controller.Response), Times.Once);
    }

    private static StorefrontAuthService Service(
        ApplicationDbContext context,
        IEventPublisher? publisher = null) =>
        new(
            context,
            Options.Create(new StorefrontJwtSettings
            {
                Secret = "storefront-auth-lifecycle-test-secret-123456789",
                Issuer = "PointOfSale.Tests.Storefront",
                Audience = "PointOfSale.Tests.Storefront",
                AccessTokenExpirationMinutes = 15,
                RefreshTokenExpirationDays = 7
            }),
            new PasswordHasher<OnlineCustomer>(),
            publisher ?? Publisher().Object,
            Mock.Of<IPhoneRiskProfileService>(),
            NullLogger<StorefrontAuthService>.Instance,
            Mock.Of<IStorefrontEmailVerificationService>());

    private static Mock<IEventPublisher> Publisher()
    {
        var publisher = new Mock<IEventPublisher>();
        publisher
            .Setup(value => value.PublishAsync(
                It.IsAny<OnlineCustomerRegisteredEvent>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        publisher
            .Setup(value => value.PublishAsync(
                It.IsAny<OnlineCustomerLoggedInEvent>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return publisher;
    }

    private static StorefrontAuthController Controller(
        Guid tenantId,
        IStorefrontAuthService authService,
        IStorefrontAuthRateLimiter rateLimiter,
        IStorefrontCookieHelper cookies)
    {
        var tenantContext = new TenantContext();
        tenantContext.SetTenantId(tenantId);
        var services = new ServiceCollection()
            .AddSingleton<ITenantContext>(tenantContext)
            .BuildServiceProvider();
        var httpContext = new DefaultHttpContext { RequestServices = services };
        httpContext.Response.Body = new MemoryStream();

        return new StorefrontAuthController(
            authService,
            Mock.Of<IStorefrontRefreshTokenService>(),
            Mock.Of<IStorefrontSocialLoginService>(),
            Mock.Of<IStorefrontPasswordResetService>(),
            rateLimiter,
            cookies,
            Mock.Of<IStorefrontJwtBlacklist>(),
            Options.Create(new StorefrontJwtSettings()),
            NullLogger<StorefrontAuthController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
    }

    private static ApplicationDbContext Context(Guid tenantId)
    {
        var tenantContext = new TenantContext();
        tenantContext.SetTenantId(tenantId);
        return new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options,
            tenantContext);
    }
}
