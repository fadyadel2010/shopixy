using System.Collections.Generic;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Moq;
using PointOfSale.API.Services.Feeds;
using Xunit;

namespace PointOfSale.Tests.Feeds;

/// <summary>
/// FEED-5: Development-only exception for the configured local storefront
/// template. Production stays strict; the exception only fires under the
/// Development environment AND only for URLs matching <c>Storefront:UrlTemplate</c>.
/// </summary>
public sealed class FeedPublicUrlReadinessDevTests
{
    private const string DevTemplate = "http://{slug}.localhost:5174";

    private static FeedStorefrontReadinessService CreateService(string environmentName)
    {
        var env = new Mock<IWebHostEnvironment>();
        env.SetupGet(e => e.EnvironmentName).Returns(environmentName);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Storefront:UrlTemplate"] = DevTemplate,
                })
            .Build();
        return new FeedStorefrontReadinessService(env.Object, config);
    }

    private static FeedStorefrontReadinessService CreateDevService() =>
        CreateService(Environments.Development);

    private static FeedStorefrontReadinessService CreateProdService() =>
        CreateService(Environments.Production);

    // =========================================================================
    // Base URL readiness
    // =========================================================================

    [Theory]
    [InlineData("http://shopixy.localhost:5174", true)]
    [InlineData("http://acme.localhost:5174", true)]
    [InlineData("https://shop.example.com", true)]
    [InlineData("http://127.0.0.1:5174", false)]
    [InlineData("http://[::1]:5174", false)]
    [InlineData("http://localhost.evil.example", false)]
    [InlineData("http://localhost:5174", false)]
    [InlineData("http://shopixy.localhost:5173", false)]
    [InlineData("http://shopixy.localhost", false)]
    [InlineData("https://localhost:5174", false)]
    [InlineData("file:///tmp/product.html", false)]
    [InlineData("data:text/html,hello", false)]
    [InlineData("not-a-url", false)]
    [InlineData("", false)]
    [InlineData("http://shopixy.localhost:5174/products/example", false)]
    public void DevStorefrontBaseUrl_AllowsOnlyConfiguredLocalTemplate(string baseUrl, bool expected)
    {
        var service = CreateDevService();

        var ready = service.IsPublicStorefrontBaseUrl(baseUrl, out _);

        Assert.Equal(expected, ready);
    }

    [Theory]
    [InlineData("http://shopixy.localhost:5174", false)]
    [InlineData("http://localhost:5174", false)]
    [InlineData("http://127.0.0.1:5174", false)]
    [InlineData("http://shopixy.localhost:5173", false)]
    [InlineData("https://shop.example.com", true)]
    [InlineData("http://shop.example.com", false)]
    public void ProdStorefrontBaseUrl_RejectsLocalhostAndHttp(string baseUrl, bool expected)
    {
        var service = CreateProdService();

        var ready = service.IsPublicStorefrontBaseUrl(baseUrl, out _);

        Assert.Equal(expected, ready);
    }

    // =========================================================================
    // Product URL readiness
    // =========================================================================

    [Theory]
    [InlineData("http://shopixy.localhost:5174/products/example", true)]
    [InlineData("http://acme.localhost:5174/products/blue-sneakers", true)]
    [InlineData("http://shopixy.localhost:5174/products/example?ref=x", false)]
    [InlineData("http://shopixy.localhost:5174/p/example", false)]
    [InlineData("http://shopixy.localhost:5173/products/example", false)]
    [InlineData("http://127.0.0.1:5174/products/example", false)]
    [InlineData("file:///tmp/products/example", false)]
    [InlineData("not-a-url", false)]
    public void DevProductUrl_OnlyAllowsConfiguredLocalRoute(string productUrl, bool expected)
    {
        var service = CreateDevService();
        var slug = expected && productUrl.Contains("/blue-sneakers", StringComparison.Ordinal)
            ? "blue-sneakers"
            : "example";
        var expectedBaseUrl = productUrl.Contains("acme.localhost", StringComparison.Ordinal)
            ? "http://acme.localhost:5174"
            : "http://shopixy.localhost:5174";

        var ready = service.IsPublicProductUrl(
            productUrl,
            expectedBaseUrl,
            slug,
            out _);

        Assert.Equal(expected, ready);
    }

    [Fact]
    public void DevProductUrl_BriefExample_PassesForProductRoute()
    {
        var service = CreateDevService();

        var ready = service.IsPublicProductUrl(
            "http://shopixy.localhost:5174/products/example",
            "http://shopixy.localhost:5174",
            "example",
            out _);

        Assert.True(ready);
    }

    [Theory]
    [InlineData("http://shopixy.localhost:5174/products/example", false)]
    [InlineData("http://localhost:5174/products/example", false)]
    [InlineData("http://127.0.0.1:5174/products/example", false)]
    public void ProdAndLocalStorefront_ProductUrl_RejectedOutsideDevelopment(
        string productUrl,
        bool expected)
    {
        foreach (var env in new[] { Environments.Production, Environments.Staging })
        {
            var service = CreateService(env);

            var ready = service.IsPublicProductUrl(productUrl, "http://shopixy.localhost:5174", "example", out _);

            Assert.Equal(expected, ready);
        }
    }

    [Fact]
    public void ProdProductUrl_AllowsHttpsPublicRoute()
    {
        var service = CreateProdService();

        var ready = service.IsPublicProductUrl(
            "https://shop.example.com/products/example",
            "https://shop.example.com",
            "example",
            out _);

        Assert.True(ready);
    }

    [Fact]
    public void DevProductUrl_RejectsMismatchedBaseOrigin()
    {
        var service = CreateDevService();

        var ready = service.IsPublicProductUrl(
            "http://shopixy.localhost:5174/products/example",
            "https://shop.example.com",
            "example",
            out _);

        Assert.False(ready);
    }

    [Fact]
    public void DevProductUrl_RejectsMismatchedPort()
    {
        var service = CreateDevService();

        var ready = service.IsPublicProductUrl(
            "http://shopixy.localhost:5173/products/example",
            "http://shopixy.localhost:5174",
            "example",
            out _);

        Assert.False(ready);
    }

    [Fact]
    public void ImageUrlReadiness_StaysStrictInDevelopment()
    {
        // Image URLs are never relaxed: localhost/HTTP images are rejected
        // even in Development, exactly like Production.
        Assert.False(FeedPublicUrlReadiness.IsPublicImageUrl("http://shopixy.localhost:5174/img/1.jpg", out _));
        Assert.False(FeedPublicUrlReadiness.IsPublicImageUrl("https://localhost/img/1.jpg", out _));
        Assert.True(FeedPublicUrlReadiness.IsPublicImageUrl("https://cdn.example.com/img/1.jpg", out _));
    }

    [Fact]
    public void StaticStrictMethods_AreProductionSafeDefaults()
    {
        // The static methods never depend on environment and always reject localhost/HTTP.
        Assert.False(FeedPublicUrlReadiness.IsPublicStorefrontBaseUrl("http://shopixy.localhost:5174", out _));
        Assert.False(FeedPublicUrlReadiness.IsPublicStorefrontBaseUrl("http://shop.example.com", out _));
        Assert.True(FeedPublicUrlReadiness.IsPublicStorefrontBaseUrl("https://shop.example.com", out _));
    }
}
