using System.Collections.Generic;
using System.Security.Claims;
using System.Text.RegularExpressions;
using MassTransit;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Events;
using PointOfSale.API.Controllers;
using PointOfSale.API.DTOs.Cart;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Security;
using PointOfSale.Core.Interfaces;
using PointOfSale.API.Services.Cart;
using PointOfSale.API.Services.Storefront;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.MultiTenancy;
using StorefrontCouponValidationResult = PointOfSale.API.DTOs.Storefront.CouponValidationResult;

namespace PointOfSale.Tests;

public sealed class StorefrontCartCouponRevalidationTests
{
    [Fact]
    public async Task GetCart_WhenCouponIsNoLongerValid_ClearsCouponUsingAuthenticatedCustomerId()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var cart = new CartDto
        {
            CartId = Guid.NewGuid(),
            TenantId = tenantId,
            OwnerType = "customer",
            OwnerId = customerId.ToString("N"),
            Currency = "USD",
            Subtotal = 100m,
            DiscountTotal = 10m,
            GrandTotal = 90m,
            AppliedCouponCode = "USED10",
            AppliedCoupon = new AppliedCouponInfo
            {
                CouponId = Guid.NewGuid(),
                Code = "USED10",
                Type = "percentage",
                Value = 10m,
                Amount = 10m
            },
            LastUpdatedUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddHours(1)
        };

        var cartService = new Mock<ICartService>();
        cartService
            .Setup(service => service.GetCartAsync(
                tenantId,
                It.IsAny<CartOwner>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(cart);
        cartService
            .Setup(service => service.SaveCartAsync(
                It.IsAny<CartDto>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var couponService = new Mock<IStorefrontCouponService>();
        Guid capturedCustomerId = Guid.Empty;
        couponService
            .Setup(service => service.RevalidateCouponAsync(
                tenantId,
                "USED10",
                It.IsAny<CartDto>(),
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<Guid, string, CartDto, Guid, string, CancellationToken>((_, _, _, customer, _, _) =>
                capturedCustomerId = customer)
            .ReturnsAsync(StorefrontCouponValidationResult.Failure(
                CouponErrorCodes.CustomerMaxExceeded,
                "Coupon already used by this customer."));

        var controller = CreateController(cartService.Object, couponService.Object, tenantId, customerId);

        var result = await controller.GetCart(CancellationToken.None);

        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.True(
            objectResult.StatusCode is null or StatusCodes.Status200OK,
            $"Expected successful cart response but received status {objectResult.StatusCode}.");
        var response = Assert.IsType<CartDto>(objectResult.Value);
        Assert.Equal(customerId, capturedCustomerId);
        Assert.Null(response.AppliedCouponCode);
        Assert.Null(response.AppliedCoupon);
        Assert.Equal(0m, response.DiscountTotal);
        Assert.Equal(100m, response.GrandTotal);

        cartService.Verify(service => service.SaveCartAsync(
            It.Is<CartDto>(saved =>
                saved.AppliedCouponCode == null &&
                saved.AppliedCoupon == null &&
                saved.DiscountTotal == 0m &&
                saved.GrandTotal == 100m),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetCart_WhenCustomerClaimMissingAndSessionHeaderMissing_ReturnsBadRequestWithoutLoadingCart()
    {
        var tenantId = Guid.NewGuid();

        var cartService = new Mock<ICartService>(MockBehavior.Strict);
        var couponService = new Mock<IStorefrontCouponService>(MockBehavior.Strict);
        var controller = CreateController(cartService.Object, couponService.Object, tenantId, customerId: null);

        var result = await controller.GetCart(CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        cartService.Verify(service => service.GetCartAsync(
            It.IsAny<Guid>(),
            It.IsAny<CartOwner>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()),
            Times.Never);
        couponService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetCart_WhenGuestSessionHeaderPresent_UsesSessionOwnershipAndSkipsCouponRevalidation()
    {
        var tenantId = Guid.NewGuid();
        const string sessionId = "guest-session-123";
        var cart = new CartDto
        {
            CartId = Guid.NewGuid(),
            TenantId = tenantId,
            OwnerType = "session",
            OwnerId = sessionId,
            Currency = "USD",
            Subtotal = 25m,
            GrandTotal = 25m,
            LastUpdatedUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddHours(1)
        };

        var cartService = new Mock<ICartService>();
        cartService
            .Setup(service => service.GetCartAsync(
                tenantId,
                It.Is<CartOwner>(owner =>
                    owner.OwnerType == "session" &&
                    owner.OwnerId == sessionId),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(cart);

        var couponService = new Mock<IStorefrontCouponService>(MockBehavior.Strict);
        var controller = CreateController(cartService.Object, couponService.Object, tenantId, customerId: null, sessionId);

        var result = await controller.GetCart(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<CartDto>(ok.Value);
        Assert.Equal("session", response.OwnerType);
        Assert.Equal(sessionId, response.OwnerId);

        cartService.VerifyAll();
        couponService.VerifyNoOtherCalls();
    }

    private static StorefrontCartController CreateController(
        ICartService cartService,
        IStorefrontCouponService couponService,
        Guid tenantId,
        Guid? customerId,
        string? sessionId = null)
    {
        var tenantContext = new TenantContext();
        tenantContext.SetTenantId(tenantId);

        var services = new ServiceCollection()
            .AddSingleton<ITenantContext>(tenantContext)
            .AddSingleton(CreateAuthenticationService(customerId, tenantId))
            .BuildServiceProvider();

        var claims = new List<Claim>
        {
            new("TenantId", tenantId.ToString("D"))
        };

        if (customerId.HasValue)
        {
            claims.Add(new Claim("CustomerId", customerId.Value.ToString("D")));
            claims.Add(new Claim(ClaimTypes.NameIdentifier, customerId.Value.ToString("D")));
        }

        var identity = new ClaimsIdentity(claims, "StorefrontJwt");

        var httpContext = new DefaultHttpContext
        {
            RequestServices = services,
            User = new ClaimsPrincipal(identity)
        };
        httpContext.Request.Headers["X-Tenant-Id"] = tenantId.ToString("D");
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            httpContext.Request.Headers["X-Session-Id"] = sessionId;
        }

        var dbContext = CreateDbContext(tenantContext);
        if (customerId.HasValue)
        {
            // Seed only the authority read needs; the full production
            // SaveChanges pipeline also provisions unrelated canonical links.
            dbContext.Database.ExecuteSqlInterpolated(
                $"INSERT INTO \"Currencies\" (\"Code\", \"Name\", \"Symbol\", \"DecimalPlaces\") VALUES ({"USD"}, {"US Dollar"}, {"$"}, {2})");
            dbContext.Database.ExecuteSqlInterpolated(
                $"INSERT INTO \"Tenants\" (\"Id\", \"Name\", \"Currency\", \"IsActive\", \"Status\", \"CreatedAtUtc\", \"BootstrapVersion\", \"UseBaileys\") VALUES ({tenantId}, {"Coupon revalidation tenant"}, {"USD"}, {true}, {1}, {DateTime.UtcNow}, {1}, {false})");
            dbContext.Database.ExecuteSqlInterpolated(
                $"INSERT INTO \"OnlineCustomers\" (\"Id\", \"TenantId\", \"Email\", \"NormalizedEmail\", \"Provider\", \"IsActive\", \"EmailVerified\", \"CreatedAtUtc\", \"PhoneVerified\") VALUES ({customerId.Value}, {tenantId}, {$"customer-{customerId:N}@example.com"}, {$"customer-{customerId:N}@example.com"}, {"Local"}, {true}, {true}, {DateTime.UtcNow}, {false})");
        }

        return new StorefrontCartController(
            cartService,
            couponService,
            CreateEventPublisher(),
            Mock.Of<ISmartOfferRedeemTokenService>(),
            dbContext,
            new CommerceReliabilityTelemetry(),
            NullLogger<StorefrontCartController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            }
        };
    }

    private static IAuthenticationService CreateAuthenticationService(Guid? customerId, Guid tenantId)
    {
        var authenticationService = new Mock<IAuthenticationService>();

        authenticationService
            .Setup(service => service.AuthenticateAsync(
                It.IsAny<HttpContext>(),
                CustomerSecurityConstants.AuthenticationScheme))
            .ReturnsAsync(AuthenticateResult.NoResult());

        authenticationService
            .Setup(service => service.AuthenticateAsync(
                It.IsAny<HttpContext>(),
                "StorefrontJwt"))
            .ReturnsAsync(CreateAuthenticateResult(customerId, tenantId, "StorefrontJwt"));

        return authenticationService.Object;
    }

    private static AuthenticateResult CreateAuthenticateResult(Guid? customerId, Guid tenantId, string scheme)
    {
        if (!customerId.HasValue)
        {
            return AuthenticateResult.NoResult();
        }

        var identity = new ClaimsIdentity(
            new[]
            {
                new Claim("TenantId", tenantId.ToString("D")),
                new Claim("CustomerId", customerId.Value.ToString("D")),
                new Claim(ClaimTypes.NameIdentifier, customerId.Value.ToString("D"))
            },
            scheme);

        return AuthenticateResult.Success(new AuthenticationTicket(
            new ClaimsPrincipal(identity),
            scheme));
    }

    private static ApplicationDbContext CreateDbContext(TenantContext tenantContext)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        RegisterSqliteFunctions(connection);

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        var dbContext = new SqliteCouponTestDbContext(options, tenantContext);
        dbContext.Database.EnsureCreated();
        return dbContext;
    }

    // The production model has PostgreSQL-only CHECK expressions (including
    // btrim and regex operators). Keep relational transactions and queries in
    // these controller tests while excluding constraints SQLite cannot parse.
    private sealed class SqliteCouponTestDbContext(
        DbContextOptions<ApplicationDbContext> options,
        ITenantContext tenantContext)
        : ApplicationDbContext(options, tenantContext)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                foreach (var constraint in entityType.GetCheckConstraints().ToArray())
                {
                    entityType.RemoveCheckConstraint(constraint.Name);
                }

                foreach (var property in entityType.GetProperties().Where(property =>
                             string.Equals(property.GetColumnName(), "xmin", StringComparison.OrdinalIgnoreCase)))
                {
                    property.SetColumnType("INTEGER");
                    property.ValueGenerated = ValueGenerated.Never;
                    property.IsConcurrencyToken = false;
                    property.SetDefaultValue(0u);
                }
            }
        }
    }

    private static void RegisterSqliteFunctions(SqliteConnection connection)
    {
        connection.CreateFunction<string?, string?, string?, string?, string?>(
            "REGEXP_REPLACE",
            (input, pattern, replacement, options) =>
            {
                if (input == null)
                {
                    return null;
                }

                var regexOptions = RegexOptions.None;
                if (!string.IsNullOrWhiteSpace(options) &&
                    options.Contains('i', StringComparison.OrdinalIgnoreCase))
                {
                    regexOptions |= RegexOptions.IgnoreCase;
                }

                return Regex.Replace(
                    input,
                    pattern ?? string.Empty,
                    replacement ?? string.Empty,
                    regexOptions);
            });
    }

    private static IEventPublisher CreateEventPublisher()
    {
        var publisher = new Mock<IEventPublisher>();
        publisher
            .Setup(service => service.PublishAsync(It.IsAny<CartCouponAppliedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        publisher
            .Setup(service => service.PublishAsync(It.IsAny<CartCouponRemovedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        publisher
            .Setup(service => service.PublishAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return publisher.Object;
    }
}
