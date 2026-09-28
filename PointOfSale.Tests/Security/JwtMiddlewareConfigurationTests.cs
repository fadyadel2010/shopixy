using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PointOfSale.API.Configuration;
using PointOfSale.API.Security;

namespace PointOfSale.Tests.Security;

public sealed class JwtMiddlewareConfigurationTests
{
    [Theory]
    [InlineData("/api/auth/session")]
    [InlineData("/hubs/notifications")]
    public async Task PosAuthentication_ReadsCanonicalAccessTokenCookie(string requestPath)
    {
        const string token = "canonical-access-token";
        using var provider = BuildServices();
        var options = provider
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
        var httpContext = new DefaultHttpContext
        {
            RequestServices = provider
        };
        httpContext.Request.Path = requestPath;
        httpContext.Request.Headers.Cookie = $"{AuthCookieConfiguration.PosAccessTokenCookieName}={token}";
        var scheme = new AuthenticationScheme(
            JwtBearerDefaults.AuthenticationScheme,
            JwtBearerDefaults.AuthenticationScheme,
            typeof(JwtBearerHandler));
        var context = new MessageReceivedContext(httpContext, scheme, options);

        await options.Events.OnMessageReceived(context);

        Assert.Equal(token, context.Token);
    }

    private static ServiceProvider BuildServices()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"] = "test-secret-that-is-long-enough-for-hmac-sha256",
                ["Jwt:Issuer"] = "test-issuer",
                ["Jwt:Audience"] = "test-audience"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHardenedJwtAuthentication(configuration);
        return services.BuildServiceProvider();
    }
}
