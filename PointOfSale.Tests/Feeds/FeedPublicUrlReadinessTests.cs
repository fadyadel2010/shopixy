using PointOfSale.API.Services.Feeds;
using Xunit;

namespace PointOfSale.Tests.Feeds;

public sealed class FeedPublicUrlReadinessTests
{
    [Theory]
    [InlineData("https://shop.example.com", true)]
    [InlineData("https://store.brand-example.com", true)]
    [InlineData("", false)]
    [InlineData("store.example.com", false)]
    [InlineData("/products/slug", false)]
    [InlineData("http://shop.example.com", false)]
    [InlineData("http://shopixy.localhost:5174", false)]
    [InlineData("https://localhost:5174", false)]
    [InlineData("https://127.0.0.1", false)]
    [InlineData("https://[::1]", false)]
    [InlineData("https://10.1.2.3", false)]
    [InlineData("https://172.16.2.3", false)]
    [InlineData("https://192.168.1.5", false)]
    [InlineData("file:///tmp/product.html", false)]
    [InlineData("data:text/html,hello", false)]
    public void StorefrontBaseUrlReadiness_IsStructuralAndPublicHttpsOnly(string url, bool expected)
    {
        var ready = FeedPublicUrlReadiness.IsPublicStorefrontBaseUrl(url, out _);

        Assert.Equal(expected, ready);
    }

    [Fact]
    public void ProductUrlReadiness_RequiresTenantOriginAndCanonicalProductRoute()
    {
        const string tenantBaseUrl = "https://shop.example.com";

        Assert.True(FeedPublicUrlReadiness.IsPublicProductUrl(
            "https://shop.example.com/products/blue-sneakers",
            tenantBaseUrl,
            "blue-sneakers",
            out _));

        Assert.False(FeedPublicUrlReadiness.IsPublicProductUrl(
            "https://other.example.com/products/blue-sneakers",
            tenantBaseUrl,
            "blue-sneakers",
            out _));

        Assert.False(FeedPublicUrlReadiness.IsPublicProductUrl(
            "https://shop.example.com/p/blue-sneakers",
            tenantBaseUrl,
            "blue-sneakers",
            out _));

        Assert.False(FeedPublicUrlReadiness.IsPublicProductUrl(
            "/products/blue-sneakers",
            tenantBaseUrl,
            "blue-sneakers",
            out _));
    }

    [Theory]
    [InlineData("https://cdn.example.com/products/1.jpg", true)]
    [InlineData("https://pub-123.r2.dev/products/1.png", true)]
    [InlineData("https://cdn.example.com/products/1.webp", true)]
    [InlineData("https://cdn.example.com/products/1.jpg?version=2", true)]
    [InlineData("https://cdn.example.com/products/1.jpg?X-Amz-Expires=300&X-Amz-Signature=abc", false)]
    [InlineData("https://cdn.example.com/products/1.jpg?se=2026-08-20&sig=abc", false)]
    [InlineData("http://cdn.example.com/products/1.jpg", false)]
    [InlineData("https://localhost/products/1.jpg", false)]
    [InlineData("https://127.0.0.1/products/1.jpg", false)]
    [InlineData("/products/1.jpg", false)]
    [InlineData("https://cdn.example.com/products/1.pdf", false)]
    [InlineData("file:///tmp/1.jpg", false)]
    [InlineData("data:image/png;base64,AAAA", false)]
    public void ImageUrlReadiness_RejectsNonPublicSignedRelativeAndNonImageUrls(string url, bool expected)
    {
        var ready = FeedPublicUrlReadiness.IsPublicImageUrl(url, out _);

        Assert.Equal(expected, ready);
    }
}
