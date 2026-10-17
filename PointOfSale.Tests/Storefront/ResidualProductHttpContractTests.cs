using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using PointOfSale.API.Controllers;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Middleware;
using PointOfSale.API.Services.Storefront;
using PointOfSale.Infrastructure.Elasticsearch.Services;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Tests.Storefront;

// Real MVC serialization/model binding and the canonical middleware over Kestrel.
// Tenant middleware and database reads are separately covered: controlled tenant
// headers here permit rejected/error responses without any shared data access.
public sealed class ResidualProductHttpContractTests
{
    [Fact]
    public async Task DetailAndVariantResponses_UseCanonicalNoStoreForSuccessAndEveryFailure() =>
        await VerifyHttpContractAsync();

    internal static async Task VerifyHttpContractAsync()
    {
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var variantId = Guid.NewGuid();
        var product = new ProductReadDto { Id = productId, Name = "Backend name", Price = 137m,
            Pricing = new() { CurrentPrice = 129m }, Availability = new() { CanPurchase = false, StockLabel = "Unavailable" } };
        var backend = new Mock<IStorefrontProductService>(MockBehavior.Strict);
        backend.Setup(s => s.GetProductByIdAsync(tenantId, productId, false, It.IsAny<CancellationToken>())).ReturnsAsync(product);
        backend.Setup(s => s.GetProductByIdAsync(tenantId, It.Is<Guid>(id => id != productId), false, It.IsAny<CancellationToken>())).ReturnsAsync((ProductReadDto?)null);
        backend.Setup(s => s.GetProductBySlugAsync(tenantId, "backend-product", false, It.IsAny<CancellationToken>())).ReturnsAsync(product);
        backend.Setup(s => s.GetProductBySlugAsync(tenantId, "outage", false, It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("controlled outage"));
        StorefrontVariantResolutionRequest? captured = null;
        backend.Setup(s => s.ResolveProductVariantAsync(tenantId, productId, It.IsAny<StorefrontVariantResolutionRequest>(), false, It.IsAny<CancellationToken>()))
            .Returns<Guid, Guid, StorefrontVariantResolutionRequest, bool, CancellationToken>((_, _, request, _, _) =>
            {
                captured = request;
                if (request.SelectedOptions.ContainsKey("outage")) throw new InvalidOperationException("controlled outage");
                return Task.FromResult<StorefrontVariantResolutionResponse?>(request.SelectedOptions.Any(option => string.Equals(option.Key, "Color", StringComparison.OrdinalIgnoreCase) && option.Value == "Red")
                    ? new() { ProductId = productId, ResolvedVariantId = variantId, Variant = new() { Id = variantId, Price = 137m,
                        Pricing = new() { CurrentPrice = 129m }, Availability = new() { CanPurchase = false }, IsActive = true } }
                    : null);
            });

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        builder.Services.AddSingleton(backend.Object);
        builder.Services.AddSingleton(Mock.Of<IProductSearchService>());
        builder.Services.AddSingleton(Mock.Of<IProductDiscoveryService>());
        builder.Services.AddSingleton(Mock.Of<IStorefrontCategoryService>());
        builder.Services.AddSingleton<CommerceReliabilityTelemetry>();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ITenantContext>(provider =>
        {
            var header = provider.GetRequiredService<IHttpContextAccessor>().HttpContext!.Request.Headers["X-Tenant-Id"].ToString();
            Guid? value = Guid.TryParse(header, out var parsed) ? parsed : null;
            return Mock.Of<ITenantContext>(context => context.TenantId == value);
        });
        builder.Services.AddControllers().AddApplicationPart(typeof(StorefrontProductsController).Assembly)
            .ConfigureApplicationPartManager(manager => manager.FeatureProviders.Add(new OnlyProductController())).AddControllersAsServices();
        builder.Services.AddTransient(provider => new StorefrontProductsController(backend.Object, provider.GetRequiredService<IProductSearchService>(),
            provider.GetRequiredService<IProductDiscoveryService>(), provider.GetRequiredService<IStorefrontCategoryService>(), null!,
            provider.GetRequiredService<CommerceReliabilityTelemetry>(), provider.GetRequiredService<ILogger<StorefrontProductsController>>()));
        await using var app = builder.Build();
        app.UseMiddleware<StorefrontHttpCachePolicyMiddleware>();
        app.MapControllers();
        app.MapGet("/unrelated", context => { context.Response.Headers.CacheControl = "public, max-age=60"; return Task.CompletedTask; });
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var client = new HttpClient { BaseAddress = new Uri(address) };
        client.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId.ToString());
        client.DefaultRequestHeaders.Add("If-None-Match", "\"stale\"");

        async Task<HttpResponseMessage> Check(string path, HttpStatusCode status, object? body = null)
        {
            var response = body == null ? await client.GetAsync(path) : await client.PostAsJsonAsync(path, body);
            Assert.Equal(status, response.StatusCode);
            // HttpClient parses/reorders directives; assert HTTP semantics, not
            // serialization order. The hosted raw headers are retained separately.
            var cache = response.Headers.CacheControl!;
            Assert.True(cache.Private);
            Assert.True(cache.NoStore);
            Assert.True(cache.NoCache);
            Assert.True(cache.MustRevalidate);
            Assert.Equal(TimeSpan.Zero, cache.MaxAge);
            Assert.Equal("no-cache", response.Headers.GetValues("Pragma").Single());
            Assert.Equal("0", response.Content.Headers.GetValues("Expires").Single());
            Assert.Equal("no-store", response.Headers.GetValues("Surrogate-Control").Single());
            Assert.Null(response.Headers.ETag);
            Assert.Null(response.Content.Headers.LastModified);
            return response;
        }
        using var detail = await Check($"/api/storefront/products/by-id/{productId}", HttpStatusCode.OK);
        var accepted = await detail.Content.ReadFromJsonAsync<ProductReadDto>();
        Assert.Equal(product.Pricing.CurrentPrice, accepted!.Pricing.CurrentPrice);
        Assert.False(accepted.Availability.CanPurchase);
        using var slug = await Check("/api/storefront/products/backend-product", HttpStatusCode.OK);
        using var missing = await Check($"/api/storefront/products/by-id/{Guid.NewGuid()}", HttpStatusCode.NotFound);
        using var unavailable = await Check("/api/storefront/products/outage", HttpStatusCode.ServiceUnavailable);
        var path = $"/api/storefront/products/by-id/{productId}/variant-resolution";
        using var resolved = await Check(path, HttpStatusCode.OK, new { selectedOptions = new { Color = "Red" }, variantId = "forged", price = 1, canPurchase = true });
        Assert.Equal("Red", Assert.Single(captured!.SelectedOptions).Value);
        var result = await resolved.Content.ReadFromJsonAsync<StorefrontVariantResolutionResponse>();
        Assert.Equal(variantId, result!.Variant.Id);
        Assert.Equal(129m, result.Variant.Pricing.CurrentPrice);
        Assert.False(result.Variant.Availability.CanPurchase);
        using var conflict = await Check(path, HttpStatusCode.Conflict, new { selectedOptions = new { Color = "Invalid" } });
        using var invalid = await Check(path, HttpStatusCode.BadRequest, new { selectedOptions = "invalid-shape" });
        using var outage = await Check(path, HttpStatusCode.ServiceUnavailable, new { selectedOptions = new { outage = "yes" } });
        client.DefaultRequestHeaders.Remove("X-Tenant-Id");
        using var rejectedTenant = await Check(path, HttpStatusCode.BadRequest, new { selectedOptions = new { Color = "Red" } });
        using var unrelated = await client.GetAsync("/unrelated");
        Assert.True(unrelated.Headers.CacheControl!.Public);
        Assert.False(unrelated.Headers.CacheControl.NoStore);
    }

    private sealed class OnlyProductController : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            foreach (var controller in feature.Controllers.ToArray())
                if (controller.AsType() != typeof(StorefrontProductsController)) feature.Controllers.Remove(controller);
        }
    }
}
