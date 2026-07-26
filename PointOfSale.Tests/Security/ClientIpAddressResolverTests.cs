using System.Net;
using Microsoft.AspNetCore.Http;
using PointOfSale.API.Infrastructure.Http;

namespace PointOfSale.Tests.Security;

public sealed class ClientIpAddressResolverTests
{
    [Fact]
    public void GetTrustedClientIp_IgnoresSpoofedForwardedHeaders()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Forwarded-For"] = "203.0.113.10";
        httpContext.Request.Headers["X-Real-IP"] = "203.0.113.11";
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.25");

        var clientIp = ClientIpAddressResolver.GetTrustedClientIp(httpContext);

        Assert.Equal("198.51.100.25", clientIp);
    }

    [Fact]
    public void GetTrustedClientIp_ReturnsFallback_WhenRemoteIpAddressIsNull()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Forwarded-For"] = "203.0.113.10";
        httpContext.Request.Headers["X-Real-IP"] = "203.0.113.11";
        httpContext.Connection.RemoteIpAddress = null;

        var clientIp = ClientIpAddressResolver.GetTrustedClientIp(httpContext);

        Assert.Equal("unknown", clientIp);
    }

    [Fact]
    public void GetTrustedClientIp_NormalizesIPv4MappedIPv6Address()
    {
        var httpContext = new DefaultHttpContext();
        // IPv4-mapped IPv6 address: ::ffff:198.51.100.25
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse("::ffff:198.51.100.25");

        var clientIp = ClientIpAddressResolver.GetTrustedClientIp(httpContext);

        Assert.Equal("198.51.100.25", clientIp);
    }
}
