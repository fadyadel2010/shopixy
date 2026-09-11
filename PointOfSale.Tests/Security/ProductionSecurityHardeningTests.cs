using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PointOfSale.API.Attributes;
using PointOfSale.API.Controllers;
using PointOfSale.API.Infrastructure.Mvc;
using PointOfSale.API.Middleware;
using PointOfSale.API.Models;
using PointOfSale.API.Services.Audit;
using PointOfSale.API.Services.Auth;
using PointOfSale.API.Security;
using PointOfSale.API.SuperAdmin.Controllers;
using PointOfSale.API.SuperAdmin.Models;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Security;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MultiTenancy;
using PointOfSale.Tests.TestDoubles;

namespace PointOfSale.Tests.Security;

public sealed class ProductionSecurityHardeningTests
{
    private static readonly HashSet<string> ApprovedAnonymousControllers = new(StringComparer.Ordinal)
    {
        "AffiliateAuthController", "BreadcrumbsController", "CheckoutController", "CheckoutSessionController",
        "CommerceHubCartController", "CommerceHubCategoriesController", "CommerceHubController", "CommerceHubPaymentsController",
        "FawryWebhookController", "MarketplaceCmsPagesController", "MarketplaceFooterController",
        "PaymentWebhooksController", "PaymobWebhooksController", "PlatformController",
        "PublicAffiliateController", "PublicAffiliateProductsController", "PublicGoogleProductFeedController",
        "PublicMetaProductFeedController", "PublicSnapchatProductFeedController", "PublicTikTokProductFeedController",
        "RobotsController", "SeoController",
        "ShippingWebhooksController", "SitemapController", "SlugResolverController",
        "StorefrontCartRecoveryController", "StorefrontCategoriesController", "StorefrontChatController",
        "StorefrontCommerceAiController", "StorefrontFooterController", "StorefrontFunnelController",
        "StorefrontCheckoutFieldsController", "StorefrontHomeController", "StorefrontLandingPageController",
        "StorefrontNavigationController", "StorefrontPagesController", "StorefrontPaymentMethodsController",
        "StorefrontPaymobPaymentsController", "StorefrontProductsController", "StorefrontSearchController",
        "StorefrontShippingMethodsController", "StorefrontSmartOffersController", "StorefrontThemePreviewController",
        "StorefrontStripePaymentsController", "StripeWebhooksController"
    };

    private static readonly HashSet<string> ApprovedAnonymousActions = new(StringComparer.Ordinal)
    {
        "SuperAdminAuthController.Login", "SuperAdminAuthController.Refresh", "AdminAbandonedCartRecoveryController.LogRecoveryResult",
        "AffiliatePortalController.DiscoverCategories", "AffiliatePortalController.DiscoverPublicProductDetail",
        "AffiliatePortalController.DiscoverPublicProducts",
        "AuthController.Register", "AuthController.Login", "AuthController.RefreshToken", "AuthController.VerifyEmail",
        "CommerceHubOrdersController.GetOrder", "CustomersAuthController.Register", "CustomersAuthController.Login",
        "CustomersAuthController.Logout", "CustomersAuthController.ForgotPassword", "CustomersAuthController.ResetPassword",
        "CustomersAuthController.VerifyEmail", "MarketplaceController.GetAttachment", "MarketplaceController.GetServiceReviews",
        "MarketplaceProductReviewsController.GetProductReviews",
        "MarketplaceReadController.GetHomepage", "MarketplaceReadController.GetProduct", "MarketplaceReadController.GetProducts",
        "MarketplaceProductReviewsController.GetProductRatingSummary", "MarketplaceProductReviewsController.GetReviewContext",
        "PaymentsController.CreatePaymentIntent", "PaymentsController.ConfirmCodOrder",
        "PaymentsController.GetCanonicalPaymentStatus", "PaymentsController.GetOrderPaymentStatus",
        "ShippingMethodsController.CalculateShippingCost", "StorefrontAnalyticsController.SubmitEvent",
        "StorefrontAnalyticsController.SubmitEventBatch", "StorefrontAnalyticsController.Health",
        "StorefrontAuthController.Register", "StorefrontAuthController.Login", "StorefrontAuthController.RefreshToken",
        "StorefrontAuthController.GetAvailableProviders", "StorefrontAuthController.GoogleLogin", "StorefrontAuthController.FacebookLogin",
        "StorefrontAuthController.Logout", "StorefrontAuthController.ForgotPassword", "StorefrontAuthController.ResetPassword",
        "StorefrontCartController.AddItem", "StorefrontCartController.ApplyCoupon", "StorefrontCartController.ApplyDraft",
        "StorefrontCartController.ClearCart", "StorefrontCartController.GetCart", "StorefrontCartController.RemoveCoupon",
        "StorefrontCartController.RemoveItem", "StorefrontCartController.RemoveItemByVariant",
        "StorefrontCartController.UpdateItem", "StorefrontCartController.UpdateItemByVariant",
        "StorefrontStoreConfigController.GetStoreConfig",
        "StorefrontEmailVerificationController.VerifyEmail", "StorefrontOrdersReadController.GetGuestOrderTracking",
        "StorefrontOrdersReadController.TrackGuestOrder",
        "StorefrontShippingQuotesController.GetQuotes", "StorefrontManualWalletController.GetConfig"
    };

    [Fact]
    public void DebugController_MustBeDevelopmentOnly_AndHidden()
    {
        var attributes = typeof(DebugController).GetCustomAttributes(true);
        Assert.Contains(attributes, a => a is DevelopmentOnlyControllerAttribute);
        Assert.Contains(attributes, a => a is ApiExplorerSettingsAttribute api && api.IgnoreApi);
        Assert.Contains(attributes, a => a is AuthorizeAttribute auth && auth.Policy == SuperAdminSecurityConstants.PolicyName);
        Assert.DoesNotContain(attributes, a => a is AllowAnonymousAttribute);

        var source = File.ReadAllText(ApiPath("Controllers", "DebugController.cs"));
        Assert.Contains("_environment.IsDevelopment()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IgnoreQueryFilters()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SecretFirst3", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ValidateToken", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetConfig", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DevelopmentOnlyControllerFeatureProvider_MustStripDevControllers()
    {
        var feature = new ControllerFeature();
        feature.Controllers.Add(typeof(DebugController).GetTypeInfo());
        feature.Controllers.Add(typeof(DevelopmentCommerceHubController).GetTypeInfo());
        feature.Controllers.Add(typeof(WarehousesController).GetTypeInfo());
        new DevelopmentOnlyControllerFeatureProvider().PopulateFeature(Array.Empty<ApplicationPart>(), feature);
        Assert.DoesNotContain(feature.Controllers, c => c.AsType() == typeof(DebugController));
        Assert.DoesNotContain(feature.Controllers, c => c.AsType() == typeof(DevelopmentCommerceHubController));
        Assert.Contains(feature.Controllers, c => c.AsType() == typeof(WarehousesController));
    }

    [Fact]
    public void Program_MustConfigure_ControllerFiltering_FallbackAuth_AndCaseInsensitiveJson()
    {
        var source = File.ReadAllText(ApiPath("Program.cs"));
        Assert.Contains("DevelopmentOnlyControllerFeatureProvider", source, StringComparison.Ordinal);
        Assert.Contains("if (!builder.Environment.IsDevelopment())", source, StringComparison.Ordinal);
        Assert.Contains("options.FallbackPolicy", source, StringComparison.Ordinal);
        Assert.Contains("RequireAuthenticatedUser()", source, StringComparison.Ordinal);
        Assert.Contains("PropertyNameCaseInsensitive = true", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SensitiveEndpoints_MustNotAllowAnonymous()
    {
        AssertNoAllowAnonymous(typeof(TenantsController).GetMethod(nameof(TenantsController.EnableBaileysForAllTenants)));
        AssertNoAllowAnonymous(typeof(WarehousesController).GetMethod(nameof(WarehousesController.GetWarehouses)));
        AssertNoAllowAnonymous(typeof(CustomersAuthController).GetMethod(nameof(CustomersAuthController.Me)));
        AssertNoAllowAnonymous(typeof(SuperAdminAuthController).GetMethod(nameof(SuperAdminAuthController.Logout)));
    }

    [Theory]
    [InlineData(nameof(AffiliatePortalController.DiscoverProducts))]
    [InlineData(nameof(AffiliatePortalController.GetProductDetail))]
    public void AffiliateCommissionActions_MustInheritAffiliateOnly_WithoutAnonymousOverride(string actionName)
    {
        var action = typeof(AffiliatePortalController).GetMethod(actionName);

        AssertNoAllowAnonymous(action);
        Assert.Contains(
            typeof(AffiliatePortalController).GetCustomAttributes<AuthorizeAttribute>(inherit: true),
            attribute => attribute.Policy == "AffiliateOnly");
    }

    [Fact]
    public void DevelopmentCommerceHubController_MustStayDevOnly_AndSuperAdminProtected()
    {
        var attributes = typeof(DevelopmentCommerceHubController).GetCustomAttributes(true);
        Assert.Contains(attributes, a => a is DevelopmentOnlyControllerAttribute);
        Assert.Contains(attributes, a => a is ApiExplorerSettingsAttribute api && api.IgnoreApi);
        Assert.DoesNotContain(attributes, a => a is AllowAnonymousAttribute);
        Assert.Contains(attributes, a => a is AuthorizeAttribute auth && auth.Policy == SuperAdminSecurityConstants.PolicyName);
    }

    [Fact]
    public void RegisterRequest_MustNotExposeRoleProperty() => Assert.Null(typeof(RegisterRequest).GetProperty("Role"));

    [Fact]
    public void RegisterRequest_MustReject_Unknown_Forbidden_And_Duplicate_Fields()
    {
        foreach (var payload in new[]
                 {
                     RegJson(",\"role\":\"Owner\""), RegJson(",\"ROLE\":\"Owner\""),
                     RegJson(",\"tenantId\":\"11111111-1111-1111-1111-111111111111\""),
                     RegJson(",\"TenantId\":\"11111111-1111-1111-1111-111111111111\""),
                     RegJson(",\"unexpected\":true"),
                     "{\"username\":\"u\",\"email\":\"a@b.com\",\"EMAIL\":\"x@y.com\",\"password\":\"P@ssw0rd!\",\"firstName\":\"A\",\"lastName\":\"B\"}"
                 })
        {
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<RegisterRequest>(payload));
        }
    }

    [Fact]
    public void RegisterRequest_MustAccept_Whitelisted_Payload()
    {
        var request = JsonSerializer.Deserialize<RegisterRequest>(RegJson(",\"phone\":\"01000000000\""));
        Assert.NotNull(request);
        Assert.Equal("cashier.user", request!.Username);
        Assert.Equal("01000000000", request.Phone);
    }

    [Fact]
    public async Task Register_AssignsCashierRole_AndServerTenant()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateDbContext(tenantId);
        var controller = CreateAuthController(context, tenantId);
        var result = await controller.Register(
            new RegisterRequest("cashier.user", "cashier@example.com", "P@ssw0rd!", "Cash", "User", "01000000000"),
            CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<AuthResponse>(ok.Value);
        var created = await context.Users.SingleAsync();
        Assert.Equal(UserRole.Cashier.ToString(), response.User.Role);
        Assert.Equal(tenantId, response.User.TenantId);
        Assert.Equal(string.Empty, response.AccessToken);
        Assert.Equal(string.Empty, response.RefreshToken);
        Assert.Equal(UserRole.Cashier, created.Role);
        Assert.Equal(tenantId, created.TenantId);
        Assert.Null(created.RefreshToken);
        Assert.Null(created.RefreshTokenExpiryTime);

        var setCookieHeaders = controller.HttpContext.Response.Headers["Set-Cookie"];
        Assert.DoesNotContain(
            setCookieHeaders,
            value => value?.StartsWith($"{AuthCookieConfiguration.PosAccessTokenCookieName}=", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(
            setCookieHeaders,
            value => value?.StartsWith($"{AuthCookieConfiguration.PosRefreshTokenCookieName}=", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task Register_RejectsMissingTenantContext()
    {
        await using var context = CreateDbContext();
        var controller = CreateAuthController(context, null);
        var result = await controller.Register(
            new RegisterRequest("tenantless.user", "tenantless@example.com", "P@ssw0rd!", "Tenantless", "User", null),
            CancellationToken.None);
        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("TENANT_REQUIRED", badRequest.Value?.ToString(), StringComparison.Ordinal);
        Assert.Empty(context.Users);
    }

    [Fact]
    public async Task TenantResolutionMiddleware_MustReject_RawHeaderSpoofing()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateDbContext();
        context.Tenants.Add(Tenant(tenantId, "tenant-a"));
        await context.SaveChangesAsync();

        var tenantContext = new TenantContext();
        var called = false;
        var middleware = Middleware(_ => { called = true; return Task.CompletedTask; });
        var http = Http("/api/auth/register", "api.example.com");
        http.Request.Headers[TenantResolutionMiddleware.TenantIdHeaderName] = tenantId.ToString();
        http.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(), "secured"));

        await middleware.InvokeAsync(http, tenantContext, context);

        Assert.Equal(StatusCodes.Status400BadRequest, http.Response.StatusCode);
        Assert.False(called);
        Assert.Null(tenantContext.TenantId);
        Assert.Contains("TENANT_REQUIRED", await Body(http), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TenantResolutionMiddleware_MustResolve_FromVerifiedHost()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateDbContext();
        context.Tenants.Add(Tenant(tenantId, "tenant-a"));
        await context.SaveChangesAsync();

        var tenantContext = new TenantContext();
        var called = false;
        var middleware = Middleware(_ => { called = true; return Task.CompletedTask; });
        var http = Http("/api/storefront/products", "tenant-a.example.com");
        http.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(), "storefront"));

        await middleware.InvokeAsync(http, tenantContext, context);

        Assert.True(called);
        Assert.Equal(tenantId, tenantContext.TenantId);
        Assert.Equal(tenantId.ToString(), http.Request.Headers[TenantResolutionMiddleware.TenantIdHeaderName].ToString());
    }

    [Theory]
    [InlineData(TenantStatus.Suspended, true, false)]
    [InlineData(TenantStatus.Active, false, false)]
    [InlineData(TenantStatus.Active, true, true)]
    public async Task TenantResolutionMiddleware_MustReject_NonCurrentTenantAuthority(
        TenantStatus status,
        bool isActive,
        bool isDeleted)
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateDbContext();
        var tenant = Tenant(tenantId, "tenant-a");
        tenant.Status = status;
        tenant.IsActive = isActive;
        tenant.DeletedAtUtc = isDeleted ? DateTime.UtcNow : null;
        context.Tenants.Add(tenant);
        await context.SaveChangesAsync();

        var tenantContext = new TenantContext();
        var called = false;
        var middleware = Middleware(_ => { called = true; return Task.CompletedTask; });
        var http = Http("/api/storefront/products", "tenant-a.example.com");
        http.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(), "storefront"));

        await middleware.InvokeAsync(http, tenantContext, context);

        Assert.Equal(StatusCodes.Status400BadRequest, http.Response.StatusCode);
        Assert.False(called);
        Assert.Null(tenantContext.TenantId);
        Assert.Contains("TENANT_REQUIRED", await Body(http), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TenantResolutionMiddleware_MustReject_AuthenticatedHeaderSpoofing()
    {
        var tenantA = Tenant(Guid.NewGuid(), "tenant-a");
        var tenantB = Tenant(Guid.NewGuid(), "tenant-b");
        await using var context = CreateDbContext();
        context.Tenants.AddRange(tenantA, tenantB);
        await context.SaveChangesAsync();

        var tenantContext = new TenantContext();
        var called = false;
        var middleware = Middleware(_ => { called = true; return Task.CompletedTask; });
        var http = Http("/api/warehouses", "tenant-a.example.com");
        http.User = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim("tenant_id", tenantA.Id.ToString()), new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()) }, "TestAuth"));
        http.Request.Headers[TenantResolutionMiddleware.TenantIdHeaderName] = tenantB.Id.ToString();
        http.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(), "secured"));

        await middleware.InvokeAsync(http, tenantContext, context);

        Assert.Equal(StatusCodes.Status403Forbidden, http.Response.StatusCode);
        Assert.False(called);
        Assert.Null(tenantContext.TenantId);
        Assert.Contains("TENANT_FORBIDDEN", await Body(http), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TenantResolutionMiddleware_MustAllow_MatchingAuthenticatedHeader()
    {
        var tenantA = Tenant(Guid.NewGuid(), "tenant-a");
        await using var context = CreateDbContext();
        context.Tenants.Add(tenantA);
        await context.SaveChangesAsync();

        var tenantContext = new TenantContext();
        var called = false;
        var middleware = Middleware(_ => { called = true; return Task.CompletedTask; });
        var http = Http("/api/warehouses", "tenant-a.example.com");
        http.User = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim("tenant_id", tenantA.Id.ToString()), new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()) }, "TestAuth"));
        http.Request.Headers[TenantResolutionMiddleware.TenantIdHeaderName] = tenantA.Id.ToString();
        http.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(), "secured"));

        await middleware.InvokeAsync(http, tenantContext, context);

        Assert.True(called);
        Assert.Equal(tenantA.Id, tenantContext.TenantId);
        Assert.Equal(tenantA.Id.ToString(), http.Request.Headers[TenantResolutionMiddleware.TenantIdHeaderName].ToString());
    }

    [Fact]
    public async Task TenantResolutionMiddleware_MustBlock_ClaimHostCrossTenantMismatch()
    {
        var tenantA = Tenant(Guid.NewGuid(), "tenant-a");
        var tenantB = Tenant(Guid.NewGuid(), "tenant-b");
        await using var context = CreateDbContext();
        context.Tenants.AddRange(tenantA, tenantB);
        await context.SaveChangesAsync();

        var tenantContext = new TenantContext();
        var called = false;
        var middleware = Middleware(_ => { called = true; return Task.CompletedTask; });
        var http = Http("/api/warehouses", "tenant-b.example.com");
        http.User = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim("tenant_id", tenantA.Id.ToString()), new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()) }, "TestAuth"));
        http.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(), "secured"));

        await middleware.InvokeAsync(http, tenantContext, context);

        Assert.Equal(StatusCodes.Status403Forbidden, http.Response.StatusCode);
        Assert.False(called);
        Assert.Null(tenantContext.TenantId);
        Assert.Contains("TENANT_FORBIDDEN", await Body(http), StringComparison.Ordinal);
    }

    [Fact]
    public void ProductsController_MigrateToMultiUnit_MustUseTenantScopedInterpolatedSql_WithPrechecks()
    {
        var source = File.ReadAllText(ApiPath("Controllers", "ProductsController.cs"));
        Assert.Contains("ExecuteSqlInterpolatedAsync", source, StringComparison.Ordinal);
        Assert.Contains("pi.\"\"TenantId\"\" = {tenantId}", source, StringComparison.Ordinal);
        Assert.Contains("si.\"\"TenantId\"\" = {tenantId}", source, StringComparison.Ordinal);
        Assert.Contains("r.\"\"TenantId\"\" = {tenantId}", source, StringComparison.Ordinal);
        Assert.Contains("purchaseItemsToUpdate", source, StringComparison.Ordinal);
        Assert.Contains("updatedPurchaseItems != purchaseItemsToUpdate", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ExecuteSqlRawAsync(@\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void WarehousesController_WriteEndpoints_MustUseDtoModels()
    {
        Assert.Equal(typeof(CreateWarehouseRequest), typeof(WarehousesController).GetMethod(nameof(WarehousesController.CreateWarehouse))!.GetParameters().Single().ParameterType);
        Assert.Equal(typeof(UpdateWarehouseRequest), typeof(WarehousesController).GetMethod(nameof(WarehousesController.UpdateWarehouse))!.GetParameters().Single(p => p.Name == "request").ParameterType);
    }

    [Fact]
    public void Controllers_MustLimitAllowAnonymous_ToApprovedSurface()
    {
        var types = ControllerTypes().ToArray();
        var controllers = types.Where(t => t.GetCustomAttributes<AllowAnonymousAttribute>(true).Any()).Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
        var actions = types.Where(t => !t.GetCustomAttributes<AllowAnonymousAttribute>(true).Any())
            .SelectMany(t => HttpActions(t).Where(m => m.GetCustomAttributes<AllowAnonymousAttribute>(true).Any()).Select(m => $"{t.Name}.{m.Name}"))
            .ToHashSet(StringComparer.Ordinal);
        Assert.True(controllers.SetEquals(ApprovedAnonymousControllers), Diff("controllers", ApprovedAnonymousControllers, controllers));
        Assert.True(actions.SetEquals(ApprovedAnonymousActions), Diff("actions", ApprovedAnonymousActions, actions));
    }

    [Fact]
    public void WriteEndpoints_MustNotBindDomainEntitiesDirectly()
    {
        var offenders = ControllerTypes()
            .SelectMany(t => HttpActions(t).Where(IsWriteAction).SelectMany(m => m.GetParameters()
                .Where(p => HasEntityType(p.ParameterType)).Select(p => $"{t.Name}.{m.Name}({p.ParameterType.Name})")))
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
        Assert.True(offenders.Length == 0, "Write endpoints must use DTO/request models only. Offenders: " + string.Join(", ", offenders));
    }

    private static IEnumerable<Type> ControllerTypes() =>
        typeof(AuthController).Assembly.GetTypes().Where(t =>
            t.IsClass &&
            !t.IsAbstract &&
            typeof(ControllerBase).IsAssignableFrom(t) &&
            !t.GetCustomAttributes<TestingOnlyControllerAttribute>(inherit: true).Any());
    private static IEnumerable<MethodInfo> HttpActions(Type type) => type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly).Where(m => m.GetCustomAttributes(true).OfType<HttpMethodAttribute>().Any());
    private static bool IsWriteAction(MethodInfo method) => method.GetCustomAttributes(true).OfType<HttpMethodAttribute>().SelectMany(a => a.HttpMethods).Any(m => m is "POST" or "PUT" or "PATCH");
    private static bool HasEntityType(Type type) => type.Namespace?.StartsWith("PointOfSale.Core.Entities", StringComparison.Ordinal) == true || (type.IsArray && HasEntityType(type.GetElementType()!)) || (type.IsGenericType && type.GetGenericArguments().Any(HasEntityType));
    private static void AssertNoAllowAnonymous(MethodInfo? method) { Assert.NotNull(method); Assert.DoesNotContain(method!.GetCustomAttributes(true), a => a is AllowAnonymousAttribute); }
    private static string Diff(string label, IReadOnlySet<string> expected, IReadOnlySet<string> actual) => $"{label} mismatch. Missing: [{string.Join(", ", expected.Except(actual, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal))}] Unexpected: [{string.Join(", ", actual.Except(expected, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal))}]";
    private static string RegJson(string extra = "") => $$"""{"username":"cashier.user","email":"cashier@example.com","password":"P@ssw0rd!","firstName":"Cash","lastName":"User"{{extra}}}""";
    private static string ApiPath(params string[] parts) => Path.GetFullPath(Path.Combine(new[] { AppContext.BaseDirectory, "..", "..", "..", "..", "PointOfSale.API" }.Concat(parts).ToArray()));
    private static DefaultHttpContext Http(string path, string host)
    {
        var c = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().BuildServiceProvider()
        };
        c.Request.Scheme = "https";
        c.Request.Host = new HostString(host);
        c.Request.Path = path;
        c.Response.Body = new MemoryStream();
        return c;
    }
    private static async Task<string> Body(HttpContext context) { context.Response.Body.Position = 0; using var reader = new StreamReader(context.Response.Body, Encoding.UTF8, false, leaveOpen: true); return await reader.ReadToEndAsync(); }
    private static TenantResolutionMiddleware Middleware(RequestDelegate next) => new(next, NullLogger<TenantResolutionMiddleware>.Instance, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Storefront:UrlTemplate"] = "https://{slug}.example.com" }).Build());
    private static Tenant Tenant(Guid id, string slug) => new()
    {
        Id = id,
        Name = slug,
        Slug = slug,
        IsActive = true,
        Status = TenantStatus.Active,
        StorefrontBaseUrl = $"https://{slug}.example.com"
    };

    private static ApplicationDbContext CreateDbContext(Guid? tenantId = null)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options;
        if (!tenantId.HasValue) return new ApplicationDbContext(options);
        var tenantContext = new TenantContext();
        tenantContext.SetTenantId(tenantId.Value);
        return new ApplicationDbContext(options, tenantContext);
    }

    private static AuthController CreateAuthController(ApplicationDbContext context, Guid? tenantId)
    {
        var tenantContext = new TenantContext();
        if (tenantId.HasValue) tenantContext.SetTenantId(tenantId.Value);
        var controller = new AuthController(
            context,
            Options.Create(new JwtSettings { Secret = "super-secret-key-super-secret-key-12345", Issuer = "PointOfSale.Tests", Audience = "PointOfSale.Tests", AccessTokenExpirationMinutes = 15, RefreshTokenExpirationDays = 7 }),
            tenantContext,
            new PasswordHasher<User>(),
            Mock.Of<IEmailVerificationService>(),
            Mock.Of<IAuditService>(),
            new NoOpUserGovernanceService(),
            new NoOpCommerceHubVendorDebtService(),
            NullLogger<AuthController>.Instance);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
        controller.HttpContext.Response.Body = new MemoryStream();
        return controller;
    }
}
