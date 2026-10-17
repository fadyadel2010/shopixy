using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PointOfSale.API.Middleware;

namespace PointOfSale.Tests.Infrastructure;

public sealed class Phase5HttpCacheTests
{
    [Theory]
    [InlineData("products/item", 200)]
    [InlineData("shipping-methods", 400)]
    [InlineData("payment-methods", 503)]
    [InlineData("pages/preview", 200)]
    [InlineData("wishlist", 401)]
    [InlineData("auth/me", 403)]
    [InlineData("orders/missing", 404)]
    public async Task CentralPolicyWinsOverLateHandlerHeaders_OverRealHttp(string route, int status)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions {EnvironmentName = "Testing"});
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(o => o.Listen(IPAddress.Loopback, 0));
        await using var app = builder.Build();
        app.UseMiddleware<StorefrontHttpCachePolicyMiddleware>();
        app.Run(async context =>
        {
            context.Response.OnStarting(() =>
            {
                context.Response.Headers.CacheControl = "public, max-age=3600";
                context.Response.Headers.ETag = "\"old\"";
                context.Response.Headers.LastModified = DateTimeOffset.UtcNow.ToString("R");
                return Task.CompletedTask;
            });
            context.Response.StatusCode = status;
            await context.Response.WriteAsync("controlled handler response");
        });
        await app.StartAsync();
        using var client = new HttpClient {BaseAddress = new Uri(app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.Single())};
        using var response = await client.GetAsync("/api/storefront/" + route);
        Assert.Equal(status, (int)response.StatusCode);
        var cache = response.Headers.CacheControl!;
        Assert.True(cache.Private && cache.NoStore && cache.NoCache && cache.MustRevalidate);
        Assert.False(cache.Public);
        Assert.Equal(TimeSpan.Zero, cache.MaxAge);
        Assert.Null(response.Headers.ETag);
        Assert.Null(response.Content.Headers.LastModified);
        Assert.Equal("no-store", response.Headers.GetValues("Surrogate-Control").Single());
    }
}
