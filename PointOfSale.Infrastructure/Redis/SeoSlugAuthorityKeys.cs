using System.Security.Cryptography;
using System.Text;

namespace PointOfSale.Infrastructure.Redis;

public static class SeoSlugAuthorityKeys
{
    // A global generation also fences tenant lifecycle and cross-type slug claims.
    public static string Generation(string instanceName) =>
        $"{instanceName.Trim().TrimEnd(':')}:authority:seo-slug:generation:v1";

    public static string Negative(string instanceName, Guid tenantId, string slug) =>
        $"{instanceName.Trim().TrimEnd(':')}:tenant:{tenantId:N}:seo:negative:v1:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(slug)))}";
}
