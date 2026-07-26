using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Http;
using PointOfSale.API.Security;

namespace PointOfSale.Tests.Security;

public sealed class JwtBearerRoutingHelperTests
{
    [Fact]
    public void TryGetBearerTokenForIssuer_WhenIssuerMatches_ReturnsToken()
    {
        var request = CreateRequestWithBearerToken("StorefrontAPI");

        var matched = JwtBearerRoutingHelper.TryGetBearerTokenForIssuer(
            request,
            "StorefrontAPI",
            out var token);

        Assert.True(matched);
        Assert.False(string.IsNullOrWhiteSpace(token));
    }

    [Fact]
    public void TryGetBearerTokenForIssuer_WhenIssuerDoesNotMatch_ReturnsFalse()
    {
        var request = CreateRequestWithBearerToken("CommerceHubCustomerAPI");

        var matched = JwtBearerRoutingHelper.TryGetBearerTokenForIssuer(
            request,
            "StorefrontAPI",
            out var token);

        Assert.False(matched);
        Assert.Equal(string.Empty, token);
    }

    [Fact]
    public void TryGetBearerTokenForIssuer_WhenTokenIsMalformed_ReturnsFalse()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Bearer definitely-not-a-jwt";

        var matched = JwtBearerRoutingHelper.TryGetBearerTokenForIssuer(
            context.Request,
            "StorefrontAPI",
            out var token);

        Assert.False(matched);
        Assert.Equal(string.Empty, token);
    }

    private static HttpRequest CreateRequestWithBearerToken(string issuer)
    {
        var token = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(issuer: issuer));
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = $"Bearer {token}";
        return context.Request;
    }
}
