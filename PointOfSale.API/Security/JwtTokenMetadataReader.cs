using System.IdentityModel.Tokens.Jwt;

namespace PointOfSale.API.Security;

public static class JwtTokenMetadataReader
{
    public static bool TryReadJti(string token, out string? jti)
    {
        jti = null;

        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var handler = new JwtSecurityTokenHandler();
        if (!handler.CanReadToken(token))
        {
            return false;
        }

        var jwt = handler.ReadJwtToken(token);
        jti = jwt.Claims.FirstOrDefault(claim => claim.Type == JwtRegisteredClaimNames.Jti)?.Value;
        return !string.IsNullOrWhiteSpace(jti);
    }
}
