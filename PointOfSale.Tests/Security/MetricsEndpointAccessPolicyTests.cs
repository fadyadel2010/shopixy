using System.Net;
using Microsoft.AspNetCore.Http;
using PointOfSale.API.Security;

namespace PointOfSale.Tests.Security;

public sealed class MetricsEndpointAccessPolicyTests
{
    [Fact]
    public void IsAuthorized_DeniesPublicAddressWithoutApiKey()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse("8.8.8.8");

        var authorized = MetricsEndpointAccessPolicy.IsAuthorized(httpContext, configuredApiKey: "expected-key");

        Assert.False(authorized);
    }

    [Fact]
    public void IsAuthorized_DeniesPrivateAddressWithoutApiKey()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.42");

        var authorized = MetricsEndpointAccessPolicy.IsAuthorized(httpContext, configuredApiKey: null);

        Assert.False(authorized);
    }

    [Fact]
    public void IsAuthorized_AllowsPrivateAddressWithValidApiKey()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.42");
        httpContext.Request.Headers[MetricsEndpointAccessPolicy.ApiKeyHeaderName] = "expected-key";

        var authorized = MetricsEndpointAccessPolicy.IsAuthorized(httpContext, configuredApiKey: "expected-key");

        Assert.True(authorized);
    }

    [Fact]
    public void IsAuthorized_AllowsLoopbackIPv4WithoutApiKey()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = IPAddress.Loopback; // 127.0.0.1

        var authorized = MetricsEndpointAccessPolicy.IsAuthorized(httpContext, configuredApiKey: null);

        Assert.True(authorized);
    }

    [Fact]
    public void IsAuthorized_AllowsLoopbackIPv6WithoutApiKey()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = IPAddress.IPv6Loopback; // ::1

        var authorized = MetricsEndpointAccessPolicy.IsAuthorized(httpContext, configuredApiKey: null);

        Assert.True(authorized);
    }

    [Fact]
    public void IsAuthorized_AllowsMatchingApiKey()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse("8.8.4.4");
        httpContext.Request.Headers[MetricsEndpointAccessPolicy.ApiKeyHeaderName] = "expected-key";

        var authorized = MetricsEndpointAccessPolicy.IsAuthorized(httpContext, configuredApiKey: "expected-key");

        Assert.True(authorized);
    }

    [Fact]
    public void IsAuthorized_PublicIp_WrongApiKey_ReturnsFalse()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse("8.8.4.4");
        httpContext.Request.Headers[MetricsEndpointAccessPolicy.ApiKeyHeaderName] = "wrong-key";

        var authorized = MetricsEndpointAccessPolicy.IsAuthorized(httpContext, configuredApiKey: "expected-key");

        Assert.False(authorized);
    }
}
