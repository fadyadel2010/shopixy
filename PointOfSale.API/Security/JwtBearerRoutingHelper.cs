using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Http;

namespace PointOfSale.API.Security;

public static class JwtBearerRoutingHelper
{
    private const string BearerPrefix = "Bearer ";

    public static bool TryGetBearerToken(HttpRequest request, out string token)
    {
        token = string.Empty;

        if (!request.Headers.TryGetValue("Authorization", out var authorizationHeader))
        {
            return false;
        }

        var authorizationValue = authorizationHeader.ToString();
        if (!authorizationValue.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        token = authorizationValue[BearerPrefix.Length..].Trim();
        return token.Length > 0;
    }

    public static bool TryReadIssuer(string token, out string issuer)
    {
        issuer = string.Empty;

        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var tokenHandler = new JwtSecurityTokenHandler();
        if (!tokenHandler.CanReadToken(token))
        {
            return false;
        }

        try
        {
            var jwt = tokenHandler.ReadJwtToken(token);
            if (string.IsNullOrWhiteSpace(jwt.Issuer))
            {
                return false;
            }

            issuer = jwt.Issuer;
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public static bool TryGetBearerTokenForIssuer(
        HttpRequest request,
        string expectedIssuer,
        out string token)
    {
        token = string.Empty;

        if (!TryGetBearerToken(request, out var candidateToken))
        {
            return false;
        }

        if (!TryReadIssuer(candidateToken, out var issuer))
        {
            return false;
        }

        if (!string.Equals(issuer, expectedIssuer, StringComparison.Ordinal))
        {
            return false;
        }

        token = candidateToken;
        return true;
    }
}
