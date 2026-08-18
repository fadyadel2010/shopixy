using System.IO;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Moq;
using MongoDB.Driver;
using PointOfSale.API.Configuration;
using PointOfSale.API.Controllers;
using PointOfSale.API.DTOs.Cart;
using PointOfSale.API.DTOs.Checkout;
using PointOfSale.API.DTOs.CommerceHub;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Middleware;
using PointOfSale.API.Security;
using PointOfSale.API.Services.Cart;
using PointOfSale.API.Services.Checkout;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.API.Services.Payments;
using PointOfSale.API.Services.Payments.Stripe;
using PointOfSale.API.Services;
using PointOfSale.API.Services.Promotions;
using PointOfSale.API.Services.Shipping;
using PointOfSale.API.Services.Storefront;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Marketplace;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Data.Repositories;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.MongoDB.Services;
using PointOfSale.Infrastructure.MultiTenancy;
using PointOfSale.Infrastructure.Payments;
using PointOfSale.Infrastructure.Redis.Configuration;
using PointOfSale.Infrastructure.Redis.Services;
using PointOfSale.Infrastructure.Shipping;
using PointOfSale.Tests.TestDoubles;

namespace PointOfSale.Tests.Security;

public sealed class StorefrontTenantIsolationTests
{
    [Theory]
    [InlineData("/api/storefront/checkout")]
    [InlineData("/api/storefront/checkout-session")]
    [InlineData("/api/storefront/payments/stripe/create-intent")]
    [InlineData("/api/storefront/payments/paymob/intent/11111111-1111-1111-1111-111111111111")]
    [InlineData("/api/storefront/orders")]
    public async Task TenantResolutionMiddleware_MustReject_CrossTenantMismatch_OnSensitiveAnonymousStorefrontRoutes(string path)
    {
        var tenantA = Tenant(Guid.NewGuid(), "tenant-a");
        var tenantB = Tenant(Guid.NewGuid(), "tenant-b");

        await using var context = CreateDbContext();
        context.Tenants.AddRange(tenantA, tenantB);
        await context.SaveChangesAsync();

        var tenantContext = new TenantContext();
        var invoked = false;
        var middleware = Middleware(_ =>
        {
            invoked = true;
            return Task.CompletedTask;
        });

        var http = Http(path, "tenant-b.example.com");
        http.User = new ClaimsPrincipal(new ClaimsIdentity(
            new[]
            {
                new Claim("tenant_id", tenantA.Id.ToString("D")),
                new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString("D"))
            },
            "StorefrontJwt"));
        http.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(new AllowAnonymousAttribute()),
            "storefront-sensitive"));

        await middleware.InvokeAsync(http, tenantContext, context);

        Assert.Equal(StatusCodes.Status403Forbidden, http.Response.StatusCode);
        Assert.False(invoked);
        Assert.Null(tenantContext.TenantId);
        Assert.Contains("TENANT_FORBIDDEN", await Body(http), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckoutService_ProcessCheckoutAsync_MustReject_CustomerFromDifferentTenant_BeforeOrderCreation()
    {
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        await using var context = CreateDbContext(tenantId);
        context.OnlineCustomers.Add(new OnlineCustomer
        {
            Id = customerId,
            TenantId = otherTenantId,
            Email = "cross-tenant@example.com",
            NormalizedEmail = "cross-tenant@example.com",
            DisplayName = "Cross Tenant"
        });
        await context.SaveChangesAsync();

        var service = CreateCheckoutService(context);

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.ProcessCheckoutAsync(
                tenantId,
                customerId.ToString("N"),
                "customer",
                sessionId: null,
                customerId,
                "req-tenant-checkout",
                ValidCheckoutRequest(),
                "trace-checkout",
                CancellationToken.None));

        Assert.Equal("TENANT_MISMATCH", ex.Message);
        Assert.Empty(await context.Orders.ToListAsync());
    }

    [Fact]
    public async Task CartService_MergeCartAsync_MustReject_CustomerOwnerFromDifferentTenant()
    {
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        await using var context = CreateDbContext(tenantId);
        context.OnlineCustomers.Add(new OnlineCustomer
        {
            Id = customerId,
            TenantId = otherTenantId,
            Email = "cart-cross-tenant@example.com",
            NormalizedEmail = "cart-cross-tenant@example.com",
            DisplayName = "Cart Cross Tenant"
        });
        await context.SaveChangesAsync();

        var service = CreateCartService(context);

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.MergeCartAsync(
                tenantId,
                new CartOwner { OwnerType = "session", OwnerId = "guest-session-1" },
                new CartOwner { OwnerType = "customer", OwnerId = customerId.ToString("N") },
                "trace-cart",
                CancellationToken.None));

        Assert.Equal("TENANT_MISMATCH", ex.Message);
    }

    [Fact]
    public async Task StripePaymentService_CreatePaymentIntentAsync_MustReject_RequestingCustomerFromDifferentTenant()
    {
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var orderCustomerId = Guid.NewGuid();
        var requestingCustomerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        await using var context = CreateDbContext(tenantId);
        context.OnlineCustomers.AddRange(
            new OnlineCustomer
            {
                Id = orderCustomerId,
                TenantId = tenantId,
                Email = "owner@example.com",
                NormalizedEmail = "owner@example.com",
                DisplayName = "Owner"
            },
            new OnlineCustomer
            {
                Id = requestingCustomerId,
                TenantId = otherTenantId,
                Email = "attacker@example.com",
                NormalizedEmail = "attacker@example.com",
                DisplayName = "Attacker"
            });
        context.Orders.Add(new Order
        {
            Id = orderId,
            TenantId = tenantId,
            OrderNumber = "ORD-TENANT-1",
            OnlineCustomerId = orderCustomerId,
            PaymentMethod = PaymentMethod.Card,
            PaymentMethodCode = "STRIPE",
            Status = OrderStatus.Pending,
            PaymentStatus = PointOfSale.Core.Enums.PaymentStatus.Pending,
            Currency = "USD",
            Total = 10m,
            SessionId = "session-1"
        });
        await context.SaveChangesAsync();

        var service = CreateStripePaymentService(context);

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.CreatePaymentIntentAsync(
                tenantId,
                orderId,
                "checkout-token",
                requestingCustomerId,
                promotionCalculationFailed: false,
                "trace-stripe",
                CancellationToken.None));

        Assert.Equal("TENANT_MISMATCH", ex.Message);
        Assert.Empty(await context.PaymentIntents.ToListAsync());
    }

    [Fact]
    public async Task StripePaymentService_CreatePaymentIntentAsync_WhenClaimedIntentIsIncomplete_MustFailClosed_WithoutCreatingDuplicate()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var dataProtectionProvider = new EphemeralDataProtectionProvider();

        await using var context = CreateDbContext(tenantId);
        context.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "tenant-stripe",
            Slug = "tenant-stripe",
            Currency = "USD",
            IsActive = true,
            StorefrontBaseUrl = "https://tenant-stripe.example.com"
        });
        context.OnlineCustomers.Add(new OnlineCustomer
        {
            Id = customerId,
            TenantId = tenantId,
            Email = "owner@example.com",
            NormalizedEmail = "owner@example.com",
            DisplayName = "Owner"
        });
        context.Orders.Add(new Order
        {
            Id = orderId,
            TenantId = tenantId,
            OrderNumber = "ORD-CLAIM-1",
            OnlineCustomerId = customerId,
            PaymentMethod = PaymentMethod.Card,
            PaymentMethodCode = "STRIPE",
            Status = OrderStatus.Pending,
            PaymentStatus = PointOfSale.Core.Enums.PaymentStatus.Pending,
            Currency = "USD",
            Total = 25m,
            SessionId = "session-claimed"
        });
        context.TenantPaymentMethods.Add(new TenantPaymentMethod
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Code = "STRIPE",
            DisplayName = "Stripe",
            IsEnabled = true,
            SupportsOnline = true,
            SupportsOffline = false,
            ConfigEncrypted = ProtectStripeConfig(dataProtectionProvider)
        });
        context.PaymentIntents.Add(new PaymentIntent
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            TenantId = tenantId,
            Amount = 25m,
            Currency = "USD",
            PaymentMethod = PaymentMethod.Card,
            Provider = PaymentProvider.Stripe,
            PaymentReference = "PAY-CLAIMED-1",
            IdempotencyKey = $"stripe:intent:{tenantId:N}:{orderId:N}",
            Status = PaymentIntentStatus.Pending,
            ProviderMetadata = PaymentIntentRoutingMetadataSerializer.Serialize(
                tenantId,
                orderId,
                "https://tenant-stripe.example.com"),
            CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var checkoutSessionTokenService = new Mock<ICheckoutSessionTokenService>();
        checkoutSessionTokenService
            .Setup(service => service.ValidatePaymentToken("checkout-token", tenantId))
            .Returns(new CheckoutSessionTokenValidationResult(
                true,
                false,
                orderId.ToString("N"),
                null,
                DateTimeOffset.UtcNow.AddMinutes(5),
                "checkout-payment"));

        var runtimeApplicationService = new Mock<IStripeRuntimeApplicationService>();
        runtimeApplicationService
            .Setup(service => service.CreatePaymentIntentAsync(
                It.IsAny<StripeRuntimePaymentIntentApplicationRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new StripeApiException(
                "Stripe recovery is unavailable in this fail-closed scenario.",
                "STRIPE_RECOVERY_UNAVAILABLE"));
        var tenantConfigurationResolver = new Mock<IStripeTenantConfigurationResolver>();
        tenantConfigurationResolver
            .Setup(resolver => resolver.ResolveCredentialsAsync(
                tenantId,
                StripeOperationalCapability.PaymentIntent,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StripeTenantRuntimeCredentials(
                tenantId,
                Guid.NewGuid(),
                "pk_test_isolation",
                "sk_test_isolation",
                "whsec_isolation",
                true));

        var service = CreateStripePaymentService(
            context,
            checkoutSessionTokenService.Object,
            dataProtectionProvider,
            runtimeApplicationService.Object,
            tenantConfigurationResolver.Object);

        var ex = await Assert.ThrowsAsync<StripeApiException>(() =>
            service.CreatePaymentIntentAsync(
                tenantId,
                orderId,
                "checkout-token",
                customerId,
                promotionCalculationFailed: false,
                "trace-stripe-claim",
                CancellationToken.None));

        Assert.Equal("STRIPE_API_ERROR", ex.ErrorCode);
        Assert.Equal("STRIPE_RECOVERY_UNAVAILABLE", ex.StripeErrorCode);
        runtimeApplicationService.Verify(service => service.CreatePaymentIntentAsync(
            It.IsAny<StripeRuntimePaymentIntentApplicationRequest>(),
            It.IsAny<CancellationToken>()), Times.Once);

        var intents = await context.PaymentIntents
            .Where(intent => intent.TenantId == tenantId && intent.OrderId == orderId)
            .ToListAsync();

        var claimedIntent = Assert.Single(intents);
        Assert.Equal(PaymentIntentStatus.Pending, claimedIntent.Status);
        Assert.Null(claimedIntent.ProviderIntentId);
        Assert.Null(claimedIntent.RedirectUrl);
    }

    [Fact]
    public async Task CheckoutController_WhenCheckoutServiceRejectsTenantMismatch_ReturnsUnauthorized()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        const string requestId = "11111111-1111-1111-1111-111111111111";
        var checkoutService = new Mock<ICheckoutService>();

        checkoutService
            .Setup(service => service.ProcessCheckoutAsync(
                tenantId,
                customerId.ToString("N"),
                "customer",
                null,
                customerId,
                requestId,
                It.IsAny<CheckoutRequestDto>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnauthorizedAccessException("TENANT_MISMATCH"));

        var controller = CreateCheckoutController(
            checkoutService.Object,
            CreateAuthenticationService(storefrontCustomerId: customerId));
        controller.Request.Headers["X-Tenant-Id"] = tenantId.ToString("D");
        controller.Request.Headers["Idempotency-Key"] = requestId;
        controller.Request.Headers["X-Request-Id"] = requestId;

        var result = await controller.Checkout(ValidCheckoutRequest(), CancellationToken.None);

        var unauthorized = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, unauthorized.StatusCode);
        var error = Assert.IsType<ApiErrorResponse>(unauthorized.Value);
        Assert.Equal("TENANT_MISMATCH", error.Code);
    }

    [Fact]
    public async Task StorefrontCartController_WhenCartServiceRejectsTenantMismatch_ReturnsUnauthorized()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var cartService = new Mock<ICartService>();
        await using var dbContext = CreateDbContext(tenantId);
        dbContext.OnlineCustomers.Add(new OnlineCustomer
        {
            Id = customerId,
            TenantId = tenantId,
            Email = "customer@example.com",
            NormalizedEmail = "customer@example.com",
            DisplayName = "Customer",
            IsActive = true
        });
        await dbContext.SaveChangesAsync();

        cartService
            .Setup(service => service.AddItemAsync(
                tenantId,
                It.Is<CartOwner>(owner =>
                    owner.OwnerType == "customer" &&
                    owner.OwnerId == customerId.ToString("N")),
                It.IsAny<AddCartItemRequest>(),
                It.IsAny<Guid?>(),
                It.IsAny<string?>(),
                It.IsAny<Guid?>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnauthorizedAccessException("TENANT_MISMATCH"));

        var controller = CreateStorefrontCartController(
            cartService.Object,
            Mock.Of<IStorefrontCouponService>(),
            tenantId,
            customerId,
            dbContext);

        var result = await controller.AddItem(
            new AddCartItemRequest
            {
                ProductId = Guid.NewGuid(),
                Quantity = 1
            },
            CancellationToken.None);

        var unauthorized = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, unauthorized.StatusCode);
        var error = Assert.IsType<ApiErrorResponse>(unauthorized.Value);
        Assert.Equal("TENANT_MISMATCH", error.Code);
    }

    [Fact]
    public async Task StorefrontStripePaymentsController_MustIgnoreBodyCustomerId_AndReturnTenantMismatch_WhenAuthenticatedCustomerBelongsToOtherTenant()
    {
        var tenantId = Guid.NewGuid();
        var authenticatedCustomerId = Guid.NewGuid();
        var bodyCustomerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var authorizationService = new Mock<IPaymentRequestAuthorizationService>();
        authorizationService
            .Setup(service => service.AuthorizeOrderAccessAsync(
                It.IsAny<HttpContext>(),
                tenantId,
                orderId,
                It.IsAny<string>(),
                "checkout-token",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentRequestAuthorizationOutcome.Success(
                authenticatedCustomerId,
                new PaymentOrderAccessInfo(null, authenticatedCustomerId, null, false)));

        var stripePaymentService = new Mock<IStripePaymentService>();
        stripePaymentService
            .Setup(service => service.CreatePaymentIntentAsync(
                tenantId,
                orderId,
                "checkout-token",
                authenticatedCustomerId,
                false,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string?>()))
            .ThrowsAsync(new UnauthorizedAccessException("TENANT_MISMATCH"));

        var controller = CreateStorefrontStripePaymentsController(
            tenantId,
            authenticatedCustomerId,
            authorizationService.Object,
            stripePaymentService.Object);

        var result = await controller.CreatePaymentIntent(
            new CreateStripePaymentIntentRequest
            {
                OrderId = orderId,
                CheckoutToken = "checkout-token",
                CustomerId = bodyCustomerId
            },
            CancellationToken.None);

        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(result);
        var error = Assert.IsType<ApiErrorResponse>(unauthorized.Value);
        Assert.Equal("TENANT_MISMATCH", error.Code);

        stripePaymentService.Verify(service => service.CreatePaymentIntentAsync(
            tenantId,
            orderId,
            "checkout-token",
            authenticatedCustomerId,
            false,
            It.IsAny<string>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<string?>()), Times.Once);
        stripePaymentService.Verify(service => service.CreatePaymentIntentAsync(
            tenantId,
            orderId,
            "checkout-token",
            bodyCustomerId,
            false,
            It.IsAny<string>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<string?>()), Times.Never);
    }

    private static CheckoutService CreateCheckoutService(ApplicationDbContext dbContext)
    {
        var dataProtector = new Mock<Microsoft.AspNetCore.DataProtection.IDataProtector>();
        var dataProtectionProvider = new Mock<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>();
        dataProtectionProvider
            .Setup(provider => provider.CreateProtector(It.IsAny<string>()))
            .Returns(dataProtector.Object);

        var mongoDatabase = new Mock<IMongoDatabase>();
        mongoDatabase
            .Setup(database => database.GetCollection<StorefrontConfigDocument>(
                "storefront_configs",
                It.IsAny<MongoCollectionSettings>()))
            .Returns(Mock.Of<IMongoCollection<StorefrontConfigDocument>>());

        var runtimeReadiness = new Mock<IStorefrontPaymentRuntimeReadinessService>();
        runtimeReadiness
            .Setup(service => service.EvaluateAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(StorefrontPaymentRuntimeReadiness.Ready);

        return new CheckoutService(
            dbContext,
            Mock.Of<IProductVariantReadRepository>(),
            Mock.Of<IProductReadRepository>(),
            Mock.Of<IShippingMethodReadRepository>(),
            Mock.Of<IPaymentMethodReadRepository>(),
            Mock.Of<IProductVariantStockRepository>(),
            mongoDatabase.Object,
            Mock.Of<IEventPublisher>(),
            Mock.Of<ICouponValidationService>(),
            Mock.Of<IPromotionEngine>(),
            Mock.Of<PointOfSale.API.Services.Payments.ProviderContracts.IPaymentGateway>(),
            runtimeReadiness.Object,
            Mock.Of<PointOfSale.API.Services.Pricing.IStorefrontPricingService>(),
            Mock.Of<ICheckoutExecutionLockManager>(),
            Mock.Of<ICheckoutRequestIdempotencyCoordinator>(),
            Mock.Of<IMarketplaceEventOutboxService>(),
            Mock.Of<PointOfSale.API.Services.PhoneRisk.IPhoneRiskProfileService>(),
            Mock.Of<PointOfSale.API.Services.PhoneRisk.IOrderVelocityService>(),
            dataProtectionProvider.Object,
            Mock.Of<IPaymentIntentClientSecretProtector>(),
            Mock.Of<PointOfSale.API.Services.Payments.Paymob.IPaymobSensitiveArtifactProtector>(),
            Mock.Of<ICheckoutSessionTokenService>(),
            Mock.Of<ICheckoutAttachmentService>(),
            Mock.Of<IWarehouseResolutionService>(),
            Options.Create(new R2Options()),
            Options.Create(new PaymentLifecycleSettings { RequireRowLevelLocking = false }),
            NullLogger<CheckoutService>.Instance,
            new HttpContextAccessor(),
            Mock.Of<ICartService>());
    }

    private static CartService CreateCartService(ApplicationDbContext dbContext)
    {
        var mongoDatabase = new Mock<IMongoDatabase>();
        mongoDatabase
            .Setup(database => database.GetCollection<StorefrontConfigDocument>(
                "storefront_configs",
                It.IsAny<MongoCollectionSettings>()))
            .Returns(Mock.Of<IMongoCollection<StorefrontConfigDocument>>());

        return new CartService(
            dbContext,
            CreateDisabledRedisFactory(),
            Options.Create(new RedisSettings { Enabled = false, InstanceName = "test" }),
            Mock.Of<IProductReadRepository>(),
            Mock.Of<IEventPublisher>(),
            mongoDatabase.Object,
            Mock.Of<IPromotionEngine>(),
            Mock.Of<PointOfSale.API.Services.Pricing.ITaxCalculationService>(),
            new CommerceReliabilityTelemetry(),
            Mock.Of<IDistributedLockService>(),
            NullLogger<CartService>.Instance);
    }

    private static StripePaymentService CreateStripePaymentService(ApplicationDbContext dbContext)
    {
        return CreateStripePaymentService(dbContext, Mock.Of<ICheckoutSessionTokenService>());
    }

    private static StripePaymentService CreateStripePaymentService(
        ApplicationDbContext dbContext,
        ICheckoutSessionTokenService checkoutSessionTokenService,
        IDataProtectionProvider? dataProtectionProvider = null,
        IStripeRuntimeApplicationService? runtimeApplicationService = null,
        IStripeTenantConfigurationResolver? tenantConfigurationResolver = null)
    {
        return new StripePaymentService(
            dbContext,
            dataProtectionProvider ?? new EphemeralDataProtectionProvider(),
            Mock.Of<IPaymentIntentClientSecretProtector>(),
            CreateDisabledRedisFactory(),
            checkoutSessionTokenService,
            Mock.Of<IEventPublisher>(),
            Mock.Of<PointOfSale.Core.Interfaces.Payments.IPaymentLifecycleService>(),
            tenantConfigurationResolver ?? Mock.Of<IStripeTenantConfigurationResolver>(),
            runtimeApplicationService ?? Mock.Of<IStripeRuntimeApplicationService>(),
            Mock.Of<IStripeWebhookRuntimeIntegrationService>(),
            CreateStorefrontUrlResolver(),
            NullLogger<StripePaymentService>.Instance,
            CreateServiceScopeFactory(),
            CreateRedirectUrlRecoveryQueue(),
            new CommerceReliabilityTelemetry());
    }

    private static PointOfSale.API.Services.BackgroundTasks.IBackgroundTaskQueue<PointOfSale.API.Services.BackgroundTasks.RedirectUrlRecoveryWorkItem> CreateRedirectUrlRecoveryQueue()
    {
        var mockQueue = new Mock<PointOfSale.API.Services.BackgroundTasks.IBackgroundTaskQueue<PointOfSale.API.Services.BackgroundTasks.RedirectUrlRecoveryWorkItem>>();
        mockQueue
            .Setup(q => q.QueueAsync(It.IsAny<PointOfSale.API.Services.BackgroundTasks.RedirectUrlRecoveryWorkItem>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        return mockQueue.Object;
    }

    private static StorefrontUrlResolver CreateStorefrontUrlResolver()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storefront:UrlTemplate"] = "https://{slug}.example.com"
            })
            .Build();

        return new StorefrontUrlResolver(
            configuration,
            NullLogger<StorefrontUrlResolver>.Instance,
            services.GetRequiredService<IServiceScopeFactory>());
    }

    private static RedisConnectionFactory CreateDisabledRedisFactory()
    {
        return new RedisConnectionFactory(
            Options.Create(new RedisSettings { Enabled = false, InstanceName = "test" }),
            NullLogger<RedisConnectionFactory>.Instance);
    }

    private static IServiceScopeFactory CreateServiceScopeFactory() =>
        new ServiceCollection()
            .BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();

    private static CheckoutController CreateCheckoutController(
        ICheckoutService checkoutService,
        IAuthenticationService authenticationService)
    {
        var services = new ServiceCollection()
            .AddSingleton(authenticationService)
            .BuildServiceProvider();

        var httpContext = new DefaultHttpContext
        {
            RequestServices = services
        };

        return new CheckoutController(
            checkoutService,
            Mock.Of<ICheckoutAttachmentService>(),
            NullLogger<CheckoutController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            }
        };
    }

    private static StorefrontCartController CreateStorefrontCartController(
        ICartService cartService,
        IStorefrontCouponService couponService,
        Guid tenantId,
        Guid customerId,
        ApplicationDbContext? dbContext = null)
    {
        var tenantContext = new TenantContext();
        tenantContext.SetTenantId(tenantId);

        var services = new ServiceCollection()
            .AddSingleton<ITenantContext>(tenantContext)
            .AddSingleton(CreateAuthenticationService(storefrontCustomerId: customerId))
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
            Mock.Of<IEventPublisher>(),
            Mock.Of<ISmartOfferRedeemTokenService>(),
            dbContext ?? CreateDbContext(),
            new CommerceReliabilityTelemetry(),
            NullLogger<StorefrontCartController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            }
        };
    }

    private static StorefrontStripePaymentsController CreateStorefrontStripePaymentsController(
        Guid tenantId,
        Guid customerId,
        IPaymentRequestAuthorizationService? paymentAuthorizationServiceOverride = null,
        IStripePaymentService? stripePaymentServiceOverride = null)
    {
        var tenantContext = new TenantContext();
        tenantContext.SetTenantId(tenantId);

        var services = new ServiceCollection()
            .AddSingleton<ITenantContext>(tenantContext)
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

        var paymentAuthorizationService = paymentAuthorizationServiceOverride;
        if (paymentAuthorizationService == null)
        {
            var paymentAuthorizationMock = new Mock<IPaymentRequestAuthorizationService>();
            paymentAuthorizationMock
                .Setup(service => service.AuthorizeOrderAccessAsync(
                    It.IsAny<HttpContext>(),
                    tenantId,
                    It.IsAny<Guid>(),
                    It.IsAny<string>(),
                    It.IsAny<string?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(PaymentRequestAuthorizationOutcome.Success(
                    customerId,
                    new PaymentOrderAccessInfo(null, customerId, null, false)));
            paymentAuthorizationService = paymentAuthorizationMock.Object;
        }

        var stripePaymentService = stripePaymentServiceOverride;
        if (stripePaymentService == null)
        {
            var stripePaymentServiceMock = new Mock<IStripePaymentService>();
            stripePaymentServiceMock
                .Setup(service => service.CreatePaymentIntentAsync(
                    tenantId,
                    It.IsAny<Guid>(),
                    It.IsAny<string>(),
                    customerId,
                    It.IsAny<bool>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<string?>()))
                .ReturnsAsync(new StripePaymentIntentResponseDto
                {
                    PaymentIntentId = Guid.NewGuid(),
                    OrderId = Guid.NewGuid()
                });
            stripePaymentService = stripePaymentServiceMock.Object;
        }

        return new StorefrontStripePaymentsController(
            stripePaymentService,
            paymentAuthorizationService,
            NullLogger<StorefrontStripePaymentsController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            }
        };
    }

    private static IAuthenticationService CreateAuthenticationService(
        Guid? storefrontCustomerId = null,
        Guid? customerJwtCustomerId = null)
    {
        var authenticationService = new Mock<IAuthenticationService>();

        authenticationService
            .Setup(service => service.AuthenticateAsync(
                It.IsAny<HttpContext>(),
                "StorefrontJwt"))
            .ReturnsAsync(CreateAuthenticateResult(storefrontCustomerId, "StorefrontJwt"));

        authenticationService
            .Setup(service => service.AuthenticateAsync(
                It.IsAny<HttpContext>(),
                CustomerSecurityConstants.AuthenticationScheme))
            .ReturnsAsync(CreateAuthenticateResult(
                customerJwtCustomerId,
                CustomerSecurityConstants.AuthenticationScheme));

        authenticationService
            .Setup(service => service.AuthenticateAsync(
                It.IsAny<HttpContext>(),
                JwtBearerDefaults.AuthenticationScheme))
            .ReturnsAsync(AuthenticateResult.NoResult());

        return authenticationService.Object;
    }

    private static AuthenticateResult CreateAuthenticateResult(Guid? customerId, string scheme)
    {
        if (!customerId.HasValue)
        {
            return AuthenticateResult.NoResult();
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            new[]
            {
                new Claim("CustomerId", customerId.Value.ToString("D")),
                new Claim(ClaimTypes.NameIdentifier, customerId.Value.ToString("D"))
            },
            scheme));

        return AuthenticateResult.Success(new AuthenticationTicket(principal, scheme));
    }

    private static CheckoutRequestDto ValidCheckoutRequest()
    {
        return new CheckoutRequestDto
        {
            CartId = Guid.NewGuid(),
            CheckoutSessionId = Guid.NewGuid(),
            WarehouseResolutionId = Guid.NewGuid(),
            WarehouseResolutionVersion = 1,
            ShippingMethodId = Guid.NewGuid(),
            PaymentMethod = "STRIPE",
            ShippingAddress = new AddressDto
            {
                Name = "Test User",
                Phone = "01000000000",
                Street = "123 Test St",
                City = "Cairo",
                Country = "EG"
            },
            ExpectedPricing = CheckoutTestFixtures.CreateDefaultExpectedPricing()
        };
    }

    private static TenantResolutionMiddleware Middleware(RequestDelegate next)
    {
        return new TenantResolutionMiddleware(
            next,
            NullLogger<TenantResolutionMiddleware>.Instance,
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Storefront:UrlTemplate"] = "https://{slug}.example.com"
                })
                .Build());
    }

    private static DefaultHttpContext Http(string path, string host)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection()
                .AddSingleton(new CommerceReliabilityTelemetry())
                .BuildServiceProvider()
        };
        context.Request.Scheme = "https";
        context.Request.Host = new HostString(host);
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static async Task<string> Body(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, Encoding.UTF8, false, leaveOpen: true);
        return await reader.ReadToEndAsync();
    }

    private static Tenant Tenant(Guid id, string slug)
    {
        return new Tenant
        {
            Id = id,
            Name = slug,
            Slug = slug,
            IsActive = true,
            Status = TenantStatus.Active,
            StorefrontBaseUrl = $"https://{slug}.example.com"
        };
    }

    private static ApplicationDbContext CreateDbContext(Guid? tenantId = null)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        if (!tenantId.HasValue)
        {
            return new ApplicationDbContext(options);
        }

        var tenantContext = new TenantContext();
        tenantContext.SetTenantId(tenantId.Value);
        return new ApplicationDbContext(options, tenantContext);
    }

    private static string ProtectStripeConfig(IDataProtectionProvider dataProtectionProvider)
    {
        var protector = dataProtectionProvider.CreateProtector("PaymentMethodSecrets");
        var json = System.Text.Json.JsonSerializer.Serialize(
            new TenantStripeConfiguration
            {
                PublishableKey = "pk_test_123",
                SecretKey = "sk_test_123",
                WebhookSecret = "whsec_123"
            },
            new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
            });

        return protector.Protect(json);
    }
}
