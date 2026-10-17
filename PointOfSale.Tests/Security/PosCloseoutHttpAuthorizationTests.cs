using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PointOfSale.API.Controllers;
using PointOfSale.API.Security;
using PointOfSale.Core.Security;

namespace PointOfSale.Tests.Security;

// Real MVC routing, endpoint attributes, permission convention, policies and handler.
// Only upstream authentication is controlled; a resource filter stops every admitted
// request before model binding/controller activation. No database or external operation.
public sealed class PosCloseoutHttpAuthorizationTests : IClassFixture<PosCloseoutAuthorizationHost>
{
    private readonly PosCloseoutAuthorizationHost _host;
    public PosCloseoutHttpAuthorizationTests(PosCloseoutAuthorizationHost host) => _host = host;

    public static IEnumerable<object[]> Requests()
    {
        foreach (var (method, path, restricted) in new[]
        {
            ("GET", "/api/admin/homepage-media/{0}/preview", true),
            ("POST", "/api/admin/homepage-media", true),
            ("GET", "/api/admin/orders/{0}/checkout-attachments/{0}/download", true),
            ("GET", "/api/admin/shipments/{0}/parcel-evidence", false),
            ("PUT", "/api/admin/shipments/{0}/parcel-evidence", false),
            ("POST", "/api/admin/shipments/{0}/parcel-quote", false),
        })
        foreach (var role in new[] { "Owner", "Admin", "Manager", "Cashier", "Warehouse", "anonymous", "untrusted-owner" })
        {
            var admitted = role is "Owner" or "Admin" || (!restricted && role is "Manager" or "Warehouse");
            yield return [method, string.Format(path, "11111111-1111-4111-8111-111111111111"), role,
                role == "anonymous" ? HttpStatusCode.Unauthorized : admitted ? HttpStatusCode.NoContent : HttpStatusCode.Forbidden];
        }
    }

    [Theory]
    [MemberData(nameof(Requests))]
    public async Task CurrentMvcEndpoints_EnforceCanonicalPermissionsOverHttp(string method, string path, string role, HttpStatusCode expected)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        request.Headers.Add("X-Test-Role", role);
        using var response = await _host.Client.SendAsync(request);
        Assert.Equal(expected, response.StatusCode);
    }
}

public sealed class PosCloseoutAuthorizationHost : IAsyncLifetime
{
    private WebApplication _app = null!;
    public HttpClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddScheme<AuthenticationSchemeOptions, ControlledPosAuthentication>(JwtBearerDefaults.AuthenticationScheme, _ => { });
        builder.Services.AddAuthorization(options =>
        {
            foreach (var permission in PosPermissions.All)
                options.AddPolicy(permission, policy =>
                {
                    policy.AuthenticationSchemes.Add(JwtBearerDefaults.AuthenticationScheme);
                    policy.RequireAuthenticatedUser().AddRequirements(new PosPermissionRequirement(permission));
                });
        });
        builder.Services.AddSingleton<IAuthorizationHandler, PosPermissionAuthorizationHandler>();
        builder.Services.AddControllers(options =>
        {
            options.Conventions.Add(new PosPermissionApplicationModelConvention());
            options.Filters.Add(new NoBusinessOperations());
        }).AddApplicationPart(typeof(AdminOrdersController).Assembly)
            .ConfigureApplicationPartManager(manager => manager.FeatureProviders.Add(new SelectedControllers()));
        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapControllers();
        await _app.StartAsync();
        var address = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        Client = new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(20) };
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        await _app.DisposeAsync();
    }

    private sealed class NoBusinessOperations : IResourceFilter
    {
        public void OnResourceExecuting(ResourceExecutingContext context) => context.Result = new NoContentResult();
        public void OnResourceExecuted(ResourceExecutedContext context) { }
    }

    private sealed class SelectedControllers : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            foreach (var controller in feature.Controllers.ToArray())
                if (controller.AsType() != typeof(AdminOrdersController) && controller.AsType() != typeof(AdminShipmentsController) && controller.AsType() != typeof(HomepageMediaController)
                    && controller.AsType() != typeof(AdminStoreConfigController) && controller.AsType() != typeof(AdminCheckoutFieldDefinitionsController)
                    && controller.AsType() != typeof(AdminDomainsController) && controller.AsType() != typeof(WishlistAnalyticsAdminController)
                    && controller.AsType() != typeof(AdminCmsPagesController) && controller.AsType() != typeof(AdminStorefrontFooterController)
                    && controller.AsType() != typeof(CouponsController) && controller.AsType() != typeof(PromotionsController) && controller.AsType() != typeof(AdminSmartOffersController)
                    && controller.AsType() != typeof(AdminAbandonedCheckoutsController) && controller.AsType() != typeof(AdminPhonesController)
                    && controller.AsType() != typeof(OnlineCustomersAdminController) && controller.AsType() != typeof(PointOfSale.API.Controllers.Admin.AdminAbandonedCartRecoveryController)
                    && controller.AsType() != typeof(PointOfSale.API.Controllers.Admin.WhatsAppAdminController)
                    && controller.AsType() != typeof(PaymentMethodsAdminController) && controller.AsType() != typeof(AdminManualPaymentController)
                    && controller.AsType() != typeof(ShippingMethodsController) && controller.AsType() != typeof(ShipmentController)
                    && controller.AsType() != typeof(OrderFulfillmentController) && controller.AsType() != typeof(PaymentLifecycleController)
                    && controller.AsType() != typeof(AdminAnalyticsAttributionController) && controller.AsType() != typeof(AdminCampaignCostController)
                    && controller.AsType() != typeof(AdminAnalyticsFunnelsController) && controller.AsType() != typeof(PointOfSale.API.Controllers.Admin.AdminPixelTrackingController)
                    && controller.AsType() != typeof(AdminCommerceAiController) && controller.AsType() != typeof(StorefrontAuthProvidersAdminController))
                    feature.Controllers.Remove(controller);
        }
    }
}

public sealed class ControlledPosAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var role = Request.Headers["X-Test-Role"].ToString();
        if (role == "anonymous") return Task.FromResult(AuthenticateResult.NoResult());
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "22222222-2222-4222-8222-222222222222"),
            new(ClaimTypes.Role, role == "untrusted-owner" ? "Owner" : role),
            new("tenant_id", "33333333-3333-4333-8333-333333333333"),
            new(PosSecurityConstants.SessionBindingClaim, "controlled-test-binding"),
        };
        if (role != "untrusted-owner") claims.Add(new(PosSecurityConstants.SqlAuthorityClaim, PosSecurityConstants.SqlAuthorityValue));
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }
}
