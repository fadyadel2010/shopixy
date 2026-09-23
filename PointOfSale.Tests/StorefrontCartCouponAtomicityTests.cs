using System.Security.Claims;
using System.Data.Common;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Controllers;
using PointOfSale.API.DTOs.Cart;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Security;
using PointOfSale.API.Services.Cart;
using PointOfSale.API.Services.Storefront;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Discounts;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.MultiTenancy;
using StorefrontCouponValidationResult = PointOfSale.API.DTOs.Storefront.CouponValidationResult;

namespace PointOfSale.Tests;

public sealed class StorefrontCartCouponAtomicityTests
{
    [Theory]
    [InlineData("active")]
    [InlineData("expired")]
    [InlineData("inactive")]
    [InlineData("missing")]
    public async Task GetCart_RealCouponAuthority_SeparatesPersistedStateFromCurrentCouponReads(string state)
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var cart = CreateCart(tenantId, customerId);
        var couponId = Guid.NewGuid();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        RegisterSqliteFunctions(connection);
        await using (var bootstrap = CreateDbContext(connection, tenantId))
        {
            await bootstrap.Database.EnsureCreatedAsync();
            SeedCartCustomerAuthority(bootstrap, tenantId, customerId);
            var couponState = StorefrontCartCouponState.Create(
                tenantId, cart.CartId, cart.OwnerType, cart.OwnerId);
            couponState.ApplyCoupon(couponId, "SAVE10", "fixed", 10m, 10m, cart.Subtotal, customerId);
            bootstrap.StorefrontCartCouponStates.Add(couponState);
            if (state != "missing")
            {
                var coupon = new Coupon
                {
                    Id = couponId,
                    TenantId = tenantId,
                    DiscountType = DiscountType.FixedAmount,
                    DiscountValue = 10m,
                    IsActive = state != "inactive",
                    EndDate = state == "expired" ? DateTime.UtcNow.AddDays(-1) : DateTime.UtcNow.AddDays(1)
                };
                coupon.SetCode("SAVE10");
                bootstrap.Coupons.Add(coupon);
            }
            await bootstrap.SaveChangesAsync();
        }

        var counter = new CouponReadCounter();
        await using var db = CreateDbContext(connection, tenantId, counter);
        var couponService = new StorefrontCouponService(
            new CouponValidationService(db, new OutboxBackedTestPublisher(db),
                NullLogger<CouponValidationService>.Instance), db);
        var controller = CreateController(db, CreateCartServiceMock(cart).Object,
            couponService, new OutboxBackedTestPublisher(db), tenantId, customerId);

        var response = await controller.GetCart(CancellationToken.None);
        var result = Assert.IsType<CartDto>(Assert.IsType<OkObjectResult>(response).Value);
        Assert.Equal(state == "active" ? "SAVE10" : null, result.AppliedCouponCode);
        Assert.Equal(state == "active" ? 10m : 0m, result.DiscountTotal);
        Assert.Equal(1, counter.CountFrom("OnlineCustomers"));
        Assert.Equal(1, counter.CountFrom("Products"));
        Assert.Equal(1, counter.CountFrom("Coupons"));
        Assert.Equal(state == "active" ? 1 : 2, counter.CountFrom("StorefrontCartCouponStates"));
        Assert.Equal(state == "active" ? 1 : 0, counter.CountFrom("CouponUsages"));
        Assert.Equal(state == "active" ? 1 : 0, counter.CountFrom("CouponReservations"));
        if (state == "active")
        {
            var repeated = await controller.GetCart(CancellationToken.None);
            Assert.Equal("SAVE10", Assert.IsType<CartDto>(Assert.IsType<OkObjectResult>(repeated).Value).AppliedCouponCode);
            Assert.Equal(2, counter.CountFrom("OnlineCustomers"));
            Assert.Equal(2, counter.CountFrom("StorefrontCartCouponStates"));
            Assert.Equal(2, counter.CountFrom("Coupons"));
            Assert.Equal(2, counter.CountFrom("CouponUsages"));
            Assert.Equal(2, counter.CountFrom("CouponReservations"));
            await using var verification = CreateDbContext(connection, tenantId);
            Assert.Empty(await verification.OutboxMessages.ToListAsync());
        }
        else
        {
            await using var verification = CreateDbContext(connection, tenantId);
            var persisted = await verification.StorefrontCartCouponStates.IgnoreQueryFilters()
                .SingleAsync(item => item.TenantId == tenantId && item.CartId == cart.CartId);
            Assert.False(persisted.HasAppliedCoupon);
            Assert.Single(await verification.OutboxMessages.ToListAsync());
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task GetCart_ReadsPersistedCouponAuthorityExactlyOnce(
        bool hasPersistedCoupon, bool foreignTenantState)
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var cart = CreateCart(tenantId, customerId);
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        RegisterSqliteFunctions(connection);
        await using (var bootstrap = CreateDbContext(connection, tenantId))
        {
            await bootstrap.Database.EnsureCreatedAsync();
            SeedCartCustomerAuthority(bootstrap, tenantId, customerId);
            if (hasPersistedCoupon)
            {
                var stateTenantId = foreignTenantState ? Guid.NewGuid() : tenantId;
                if (foreignTenantState)
                {
                    bootstrap.Tenants.Add(new Tenant
                    {
                        Id = stateTenantId, Name = "Other coupon tenant", Currency = "USD",
                        Status = PointOfSale.Core.Enums.TenantStatus.Active, IsActive = true
                    });
                }

                var state = StorefrontCartCouponState.Create(
                    stateTenantId, cart.CartId, cart.OwnerType, cart.OwnerId);
                state.ApplyCoupon(Guid.NewGuid(), "SAVE10", "fixed", 10m, 10m, cart.Subtotal, customerId);
                bootstrap.StorefrontCartCouponStates.Add(state);
                await bootstrap.SaveChangesAsync();
            }
        }

        var counter = new CouponReadCounter();
        await using var db = CreateDbContext(connection, tenantId, counter);
        var controller = CreateController(db, CreateCartServiceMock(cart).Object,
            CreateCouponServiceMock().Object, new OutboxBackedTestPublisher(db), tenantId, customerId);

        var response = await controller.GetCart(CancellationToken.None);

        var result = Assert.IsType<CartDto>(Assert.IsType<OkObjectResult>(response).Value);
        Assert.Equal(hasPersistedCoupon && !foreignTenantState ? "SAVE10" : null, result.AppliedCouponCode);
        Assert.Equal(hasPersistedCoupon && !foreignTenantState ? 10m : 0m, result.DiscountTotal);
        // Customer/tenant authentication authority + persisted cart coupon
        // state: one SQL command each, even when Redis has no coupon.
        Assert.Equal(2, counter.CommandCount);
    }

    private sealed class CouponReadCounter : DbCommandInterceptor
    {
        public int CommandCount { get; private set; }
        private readonly List<string> _commands = new();

        public int CountFrom(string table) => _commands.Count(command =>
            command.Contains($"FROM \"{table}\"", StringComparison.OrdinalIgnoreCase));

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            CommandCount++;
            _commands.Add(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    [Fact]
    public async Task ApplyCoupon_WhenOutboxPersistenceSucceeds_CommitsCouponStateAndOutboxMessage()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var logger = new TestLogger<StorefrontCartController>();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        RegisterSqliteFunctions(connection);

        await using (var bootstrapContext = CreateDbContext(connection, tenantId))
        {
            await bootstrapContext.Database.EnsureCreatedAsync();
            SeedCartCustomerAuthority(bootstrapContext, tenantId, customerId);
        }

        await using var controllerContext = CreateDbContext(connection, tenantId);
        var cartService = CreateCartServiceMock(CreateCart(tenantId, customerId));
        var couponService = CreateCouponServiceMock();
        var publisher = new OutboxBackedTestPublisher(controllerContext);
        var controller = CreateController(
            controllerContext,
            cartService.Object,
            couponService.Object,
            publisher,
            tenantId,
            customerId,
            logger);

        var result = await controller.ApplyCoupon(
            new ApplyCouponRequest { Code = "save10" },
            CancellationToken.None);

        if (result is not OkObjectResult ok)
        {
            var error = Assert.IsType<ObjectResult>(result);
            throw new Xunit.Sdk.XunitException(
                $"Unexpected result. Status={error.StatusCode}; Value={JsonSerializer.Serialize(error.Value)}; Exception={logger.LastException}");
        }
        var response = Assert.IsType<CartDto>(ok.Value);
        Assert.Equal("SAVE10", response.AppliedCouponCode);
        Assert.Equal(10m, response.DiscountTotal);

        await using var verificationContext = CreateDbContext(connection, tenantId);
        var state = await verificationContext.StorefrontCartCouponStates
            .IgnoreQueryFilters()
            .SingleAsync(item => item.TenantId == tenantId && item.CartId == response.CartId);
        var outbox = await verificationContext.OutboxMessages.SingleAsync();

        Assert.True(state.HasAppliedCoupon);
        Assert.Equal("SAVE10", state.CouponCode);
        Assert.Equal(10m, state.DiscountAmount);
        Assert.Equal(typeof(PointOfSale.API.Events.CartCouponAppliedEvent).FullName, outbox.EventType);
        Assert.Contains("\"CouponCode\":\"SAVE10\"", outbox.EventPayload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyCoupon_WhenOutboxPersistenceFails_ReturnsFailureAndRollsBackState()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        RegisterSqliteFunctions(connection);

        await using (var bootstrapContext = CreateDbContext(connection, tenantId))
        {
            await bootstrapContext.Database.EnsureCreatedAsync();
            SeedCartCustomerAuthority(bootstrapContext, tenantId, customerId);
        }

        await using var controllerContext = CreateDbContext(connection, tenantId);
        var cartService = CreateCartServiceMock(CreateCart(tenantId, customerId));
        var couponService = CreateCouponServiceMock();
        var publisher = new OutboxBackedTestPublisher(controllerContext, throwAfterWrite: true);
        var controller = CreateController(
            controllerContext,
            cartService.Object,
            couponService.Object,
            publisher,
            tenantId,
            customerId);

        var result = await controller.ApplyCoupon(
            new ApplyCouponRequest { Code = "save10" },
            CancellationToken.None);

        var error = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, error.StatusCode);

        await using var verificationContext = CreateDbContext(connection, tenantId);
        Assert.Empty(await verificationContext.StorefrontCartCouponStates.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await verificationContext.OutboxMessages.ToListAsync());
    }

    [Fact]
    public async Task ApplyCoupon_WhenCacheRefreshFails_ReturnsSuccessAfterDurableCommit()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var logger = new TestLogger<StorefrontCartController>();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        RegisterSqliteFunctions(connection);

        await using (var bootstrapContext = CreateDbContext(connection, tenantId))
        {
            await bootstrapContext.Database.EnsureCreatedAsync();
            SeedCartCustomerAuthority(bootstrapContext, tenantId, customerId);
        }

        await using var controllerContext = CreateDbContext(connection, tenantId);
        var cartService = CreateCartServiceMock(
            CreateCart(tenantId, customerId),
            saveCartException: new InvalidOperationException("redis unavailable"));
        var couponService = CreateCouponServiceMock();
        var publisher = new OutboxBackedTestPublisher(controllerContext);
        var controller = CreateController(
            controllerContext,
            cartService.Object,
            couponService.Object,
            publisher,
            tenantId,
            customerId,
            logger);

        var result = await controller.ApplyCoupon(
            new ApplyCouponRequest { Code = "save10" },
            CancellationToken.None);

        if (result is not OkObjectResult ok)
        {
            var error = Assert.IsType<ObjectResult>(result);
            throw new Xunit.Sdk.XunitException(
                $"Unexpected result. Status={error.StatusCode}; Value={JsonSerializer.Serialize(error.Value)}; Exception={logger.LastException}");
        }
        var response = Assert.IsType<CartDto>(ok.Value);
        Assert.Equal("SAVE10", response.AppliedCouponCode);

        await using var verificationContext = CreateDbContext(connection, tenantId);
        Assert.Single(await verificationContext.StorefrontCartCouponStates.IgnoreQueryFilters().ToListAsync());
        Assert.Single(await verificationContext.OutboxMessages.ToListAsync());
    }

    [Fact]
    public async Task RemoveCoupon_WhenOutboxPersistenceFails_ReturnsFailureAndRollsBackState()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        RegisterSqliteFunctions(connection);

        await using (var bootstrapContext = CreateDbContext(connection, tenantId))
        {
            await bootstrapContext.Database.EnsureCreatedAsync();
            SeedCartCustomerAuthority(bootstrapContext, tenantId, customerId);
        }

        // Setup: Apply a coupon first
        await using (var setupContext = CreateDbContext(connection, tenantId))
        {
            var cart = CreateCart(tenantId, customerId);
            var cartService = CreateCartServiceMock(cart);
            var couponService = CreateCouponServiceMock();
            var publisher = new OutboxBackedTestPublisher(setupContext);
            var controller = CreateController(
                setupContext,
                cartService.Object,
                couponService.Object,
                publisher,
                tenantId,
                customerId);

            await controller.ApplyCoupon(
                new ApplyCouponRequest { Code = "save10" },
                CancellationToken.None);
            await setupContext.SaveChangesAsync();
        }

        // Test: Remove coupon with outbox failure
        await using var controllerContext = CreateDbContext(connection, tenantId);
        var cartWithCoupon = CreateCart(tenantId, customerId);
        cartWithCoupon.AppliedCouponCode = "SAVE10";
        cartWithCoupon.AppliedCoupon = new AppliedCouponInfo
        {
            Code = "SAVE10",
            Type = "fixed",
            Value = 10m,
            Amount = 10m
        };
        cartWithCoupon.DiscountTotal = 10m;
        cartWithCoupon.GrandTotal = 90m;

        var cartService2 = CreateCartServiceMock(cartWithCoupon);
        var couponService2 = CreateCouponServiceMock();
        var publisher2 = new OutboxBackedTestPublisher(controllerContext, throwAfterWrite: true);
        var controller2 = CreateController(
            controllerContext,
            cartService2.Object,
            couponService2.Object,
            publisher2,
            tenantId,
            customerId);

        var result = await controller2.RemoveCoupon(CancellationToken.None);

        var error = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, error.StatusCode);

        // Verify rollback: coupon state should still exist from setup
        await using var verificationContext = CreateDbContext(connection, tenantId);
        var states = await verificationContext.StorefrontCartCouponStates.IgnoreQueryFilters().ToListAsync();
        Assert.Single(states);
        Assert.True(states[0].HasAppliedCoupon);
        Assert.Equal("SAVE10", states[0].CouponCode);

        // Only the setup outbox message should exist, not the removal one
        var outboxMessages = await verificationContext.OutboxMessages.ToListAsync();
        Assert.Single(outboxMessages);
        Assert.Contains("CartCouponAppliedEvent", outboxMessages[0].EventType, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RemoveCoupon_WhenCacheRefreshFails_ReturnsSuccessAfterDurableCommit()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var logger = new TestLogger<StorefrontCartController>();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        RegisterSqliteFunctions(connection);

        await using (var bootstrapContext = CreateDbContext(connection, tenantId))
        {
            await bootstrapContext.Database.EnsureCreatedAsync();
            SeedCartCustomerAuthority(bootstrapContext, tenantId, customerId);
        }

        // Setup: Apply a coupon first
        Guid cartId;
        await using (var setupContext = CreateDbContext(connection, tenantId))
        {
            var cart = CreateCart(tenantId, customerId);
            cartId = cart.CartId;
            var cartService = CreateCartServiceMock(cart);
            var couponService = CreateCouponServiceMock();
            var publisher = new OutboxBackedTestPublisher(setupContext);
            var controller = CreateController(
                setupContext,
                cartService.Object,
                couponService.Object,
                publisher,
                tenantId,
                customerId);

            await controller.ApplyCoupon(
                new ApplyCouponRequest { Code = "save10" },
                CancellationToken.None);
            await setupContext.SaveChangesAsync();
        }

        // Test: Remove coupon with cache refresh failure
        await using var controllerContext = CreateDbContext(connection, tenantId);
        var cartWithCoupon = CreateCart(tenantId, customerId);
        cartWithCoupon.CartId = cartId;
        cartWithCoupon.AppliedCouponCode = "SAVE10";
        cartWithCoupon.AppliedCoupon = new AppliedCouponInfo
        {
            Code = "SAVE10",
            Type = "fixed",
            Value = 10m,
            Amount = 10m
        };
        cartWithCoupon.DiscountTotal = 10m;
        cartWithCoupon.GrandTotal = 90m;

        var cartService2 = CreateCartServiceMock(
            cartWithCoupon,
            saveCartException: new InvalidOperationException("redis unavailable"));
        var couponService2 = CreateCouponServiceMock();
        var publisher2 = new OutboxBackedTestPublisher(controllerContext);
        var controller2 = CreateController(
            controllerContext,
            cartService2.Object,
            couponService2.Object,
            publisher2,
            tenantId,
            customerId,
            logger);

        var result = await controller2.RemoveCoupon(CancellationToken.None);

        if (result is not OkObjectResult ok)
        {
            var error = Assert.IsType<ObjectResult>(result);
            throw new Xunit.Sdk.XunitException(
                $"Unexpected result. Status={error.StatusCode}; Value={JsonSerializer.Serialize(error.Value)}; Exception={logger.LastException}");
        }
        var response = Assert.IsType<CartDto>(ok.Value);
        Assert.Null(response.AppliedCouponCode);
        Assert.Equal(0m, response.DiscountTotal);

        // Verify durable commit: coupon state should reflect removal
        await using var verificationContext = CreateDbContext(connection, tenantId);
        var state = await verificationContext.StorefrontCartCouponStates
            .IgnoreQueryFilters()
            .SingleAsync(item => item.TenantId == tenantId && item.CartId == cartId);
        Assert.False(state.HasAppliedCoupon);
        Assert.Null(state.CouponCode);
        Assert.Null(state.DiscountAmount);

        // Should have both apply and remove outbox messages
        var outboxMessages = await verificationContext.OutboxMessages.ToListAsync();
        Assert.Equal(2, outboxMessages.Count);
        Assert.Contains(outboxMessages, msg => msg.EventType.Contains("CartCouponAppliedEvent", StringComparison.Ordinal));
        Assert.Contains(outboxMessages, msg => msg.EventType.Contains("CartCouponRemovedEvent", StringComparison.Ordinal));
    }

    private static Mock<ICartService> CreateCartServiceMock(CartDto cart, Exception? saveCartException = null)
    {
        var cartService = new Mock<ICartService>();
        cartService
            .Setup(service => service.GetCartAsync(
                cart.TenantId,
                It.IsAny<CartOwner>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(cart);

        if (saveCartException == null)
        {
            cartService
                .Setup(service => service.SaveCartAsync(
                    It.IsAny<CartDto>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
        }
        else
        {
            cartService
                .Setup(service => service.SaveCartAsync(
                    It.IsAny<CartDto>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(saveCartException);
        }

        return cartService;
    }

    private static Mock<IStorefrontCouponService> CreateCouponServiceMock()
    {
        var couponId = Guid.NewGuid();
        var couponService = new Mock<IStorefrontCouponService>();
        couponService
            .Setup(service => service.ValidateCouponAsync(
                It.IsAny<Guid>(),
                "SAVE10",
                It.IsAny<CartDto>(),
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StorefrontCouponValidationResult
            {
                IsValid = true,
                CouponId = couponId,
                Code = "SAVE10",
                DiscountType = "fixed",
                DiscountValue = 10m,
                DiscountAmount = 10m
            });

        couponService
            .Setup(service => service.RevalidateCouponAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<CartDto>(),
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, string _, CartDto cart, Guid _, string _, CancellationToken _) =>
                new StorefrontCouponValidationResult
                {
                    IsValid = true,
                    CouponId = cart.AppliedCoupon?.CouponId,
                    Code = cart.AppliedCouponCode,
                    DiscountType = cart.AppliedCoupon?.Type,
                    DiscountValue = cart.AppliedCoupon?.Value,
                    DiscountAmount = cart.AppliedCoupon?.Amount
                });

        return couponService;
    }

    private static CartDto CreateCart(Guid tenantId, Guid customerId)
    {
        return new CartDto
        {
            CartId = Guid.NewGuid(),
            TenantId = tenantId,
            OwnerType = "customer",
            OwnerId = customerId.ToString("N"),
            Currency = "USD",
            Items = new List<CartItemDto>
            {
                new()
                {
                    ProductId = Guid.NewGuid(),
                    Quantity = 1,
                    UnitPrice = 100m,
                    Name = "Test product",
                    SKU = "SKU-1",
                    AddedAtUtc = DateTime.UtcNow
                }
            },
            Subtotal = 100m,
            GrandTotal = 100m,
            LastUpdatedUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddHours(1)
        };
    }

    private static StorefrontCartController CreateController(
        ApplicationDbContext dbContext,
        ICartService cartService,
        IStorefrontCouponService couponService,
        IEventPublisher eventPublisher,
        Guid tenantId,
        Guid customerId,
        ILogger<StorefrontCartController>? logger = null)
    {
        var services = new ServiceCollection()
            .AddSingleton<ITenantContext>(new TenantContextForTest(tenantId))
            .AddSingleton(CreateAuthenticationService(customerId, tenantId))
            .BuildServiceProvider();

        var httpContext = new DefaultHttpContext
        {
            RequestServices = services,
            User = new ClaimsPrincipal(new ClaimsIdentity(
                new[]
                {
                    new Claim("TenantId", tenantId.ToString("D")),
                    new Claim("CustomerId", customerId.ToString("D")),
                    new Claim(ClaimTypes.NameIdentifier, customerId.ToString("D"))
                },
                "StorefrontJwt"))
        };
        httpContext.Request.Headers["X-Tenant-Id"] = tenantId.ToString("D");

        return new StorefrontCartController(
            cartService,
            couponService,
            eventPublisher,
            Mock.Of<ISmartOfferRedeemTokenService>(),
            dbContext,
            new CommerceReliabilityTelemetry(),
            logger ?? NullLogger<StorefrontCartController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            }
        };
    }

    private sealed class TenantContextForTest : ITenantContext
    {
        public TenantContextForTest(Guid tenantId) => TenantId = tenantId;
        public Guid? TenantId { get; }
        public bool HasTenant => true;
    }

    private static ApplicationDbContext CreateDbContext(
        SqliteConnection connection, Guid tenantId, DbCommandInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection);
        if (interceptor != null)
        {
            builder.AddInterceptors(interceptor);
        }

        var options = builder.Options;

        var tenantContext = new TenantContext();
        tenantContext.SetTenantId(tenantId);
        return new CouponAtomicityApplicationDbContext(options, tenantContext);
    }

    private static void SeedCartCustomerAuthority(ApplicationDbContext db, Guid tenantId, Guid customerId)
    {
        db.Database.ExecuteSqlInterpolated(
            $"INSERT INTO \"Currencies\" (\"Code\", \"Name\", \"Symbol\", \"DecimalPlaces\") VALUES ({"USD"}, {"US Dollar"}, {"$"}, {2})");
        db.Database.ExecuteSqlInterpolated(
            $"INSERT INTO \"Tenants\" (\"Id\", \"Name\", \"Currency\", \"IsActive\", \"Status\", \"CreatedAtUtc\", \"BootstrapVersion\", \"UseBaileys\") VALUES ({tenantId}, {"Coupon atomicity tenant"}, {"USD"}, {true}, {1}, {DateTime.UtcNow}, {1}, {false})");
        var email = $"atomicity-{customerId:N}@example.com";
        db.Database.ExecuteSqlInterpolated(
            $"INSERT INTO \"OnlineCustomers\" (\"Id\", \"TenantId\", \"Email\", \"NormalizedEmail\", \"Provider\", \"IsActive\", \"EmailVerified\", \"CreatedAtUtc\", \"PhoneVerified\") VALUES ({customerId}, {tenantId}, {email}, {email}, {"Local"}, {true}, {true}, {DateTime.UtcNow}, {false})");
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

    private static IAuthenticationService CreateAuthenticationService(Guid customerId, Guid tenantId)
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

    private static AuthenticateResult CreateAuthenticateResult(Guid customerId, Guid tenantId, string scheme)
    {
        var identity = new ClaimsIdentity(
            new[]
            {
                new Claim("TenantId", tenantId.ToString("D")),
                new Claim("CustomerId", customerId.ToString("D")),
                new Claim(ClaimTypes.NameIdentifier, customerId.ToString("D"))
            },
            scheme);

        return AuthenticateResult.Success(new AuthenticationTicket(
            new ClaimsPrincipal(identity),
            scheme));
    }

    private sealed class OutboxBackedTestPublisher : IEventPublisher
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly bool _throwAfterWrite;

        public OutboxBackedTestPublisher(ApplicationDbContext dbContext, bool throwAfterWrite = false)
        {
            _dbContext = dbContext;
            _throwAfterWrite = throwAfterWrite;
        }

        public Task PublishAsync<T>(T @event, CancellationToken cancellationToken = default)
            where T : IntegrationEvent
        {
            _dbContext.OutboxMessages.Add(new OutboxMessage
            {
                Id = Guid.NewGuid(),
                EventType = @event.GetType().FullName ?? typeof(T).FullName ?? nameof(IntegrationEvent),
                EventPayload = JsonSerializer.Serialize(@event, @event.GetType()),
                TenantId = @event.TenantId,
                AggregateId = @event is PointOfSale.API.Events.CartEventBase cartEvent ? cartEvent.CartId : null,
                CreatedAtUtc = DateTime.UtcNow
            });

            if (_throwAfterWrite)
            {
                throw new InvalidOperationException("Simulated outbox write failure.");
            }

            return Task.CompletedTask;
        }

        public Task PublishAsync(object @event, CancellationToken cancellationToken = default)
        {
            switch (@event)
            {
                case IntegrationEvent integrationEvent:
                    return PublishAsync(integrationEvent, cancellationToken);
                case PointOfSale.API.Events.CartEventBase cartEvent:
                    var outboxMessage = new OutboxMessage
                    {
                        Id = Guid.NewGuid(),
                        EventType = @event.GetType().FullName ?? @event.GetType().Name,
                        EventPayload = JsonSerializer.Serialize(@event, @event.GetType()),
                        TenantId = cartEvent.TenantId,
                        AggregateId = cartEvent.CartId,
                        CreatedAtUtc = cartEvent.OccurredAtUtc
                    };
                    _dbContext.OutboxMessages.Add(outboxMessage);
                    _dbContext.Entry(outboxMessage).Property("xmin").CurrentValue = 1u;

                    if (_throwAfterWrite)
                    {
                        throw new InvalidOperationException("Simulated outbox write failure.");
                    }

                    return Task.CompletedTask;
                default:
                    throw new NotSupportedException(
                        $"Publisher {nameof(OutboxBackedTestPublisher)} does not support event type '{@event.GetType().Name}'.");
            }
        }

        public Task PublishBatchAsync<T>(IEnumerable<T> events, CancellationToken cancellationToken = default)
            where T : IntegrationEvent
        {
            foreach (var @event in events)
            {
                PublishAsync(@event, cancellationToken);
            }

            return Task.CompletedTask;
        }

        public Task PublishSensitiveAsync<T>(T @event, CancellationToken cancellationToken = default)
            where T : IntegrationEvent, ISensitiveIntegrationEvent
        {
            return PublishAsync(@event, cancellationToken);
        }
    }

    private sealed class CouponAtomicityApplicationDbContext : ApplicationDbContext
    {
        public CouponAtomicityApplicationDbContext(
            DbContextOptions<ApplicationDbContext> options,
            ITenantContext tenantContext)
            : base(options, tenantContext)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // The production model has PostgreSQL-only CHECK expressions
            // (btrim/regex); this fixture tests relational transactions and
            // outbox rollback, not PostgreSQL expression translation.
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

            modelBuilder.Entity<OutboxMessage>()
                .Property<uint>("xmin")
                .HasDefaultValue(1u)
                .ValueGeneratedOnAdd();
        }
    }

    private sealed class TestLogger<T> : ILogger<T>
    {
        public Exception? LastException { get; private set; }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (exception != null)
            {
                LastException = exception;
            }
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }
}
