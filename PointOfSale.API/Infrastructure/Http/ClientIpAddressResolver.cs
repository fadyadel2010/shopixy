namespace PointOfSale.API.Infrastructure.Http;

internal static class ClientIpAddressResolver
{
    public static string GetTrustedClientIp(HttpContext? httpContext, string fallback = "unknown")
    {
        var remoteIp = httpContext?.Connection.RemoteIpAddress;
        if (remoteIp == null)
        {
            return fallback;
        }

        if (remoteIp.IsIPv4MappedToIPv6)
        {
            remoteIp = remoteIp.MapToIPv4();
        }

        return remoteIp.ToString();
    }
}
