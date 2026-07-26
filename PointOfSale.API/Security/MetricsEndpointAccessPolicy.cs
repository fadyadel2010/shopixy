using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace PointOfSale.API.Security;

public static class MetricsEndpointAccessPolicy
{
    public const string ApiKeyHeaderName = "X-Prometheus-Api-Key";

    public static bool IsAuthorized(HttpContext httpContext, string? configuredApiKey)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        // SECURITY: Only allow loopback addresses to bypass API key requirement.
        // Private network addresses (10.x, 172.16-31.x, 192.168.x) are NOT trusted
        // because behind proxies/load balancers, RemoteIpAddress often shows the proxy's
        // private IP, making all external requests appear "internal" unless forwarded
        // headers are properly locked down with KnownProxies/KnownNetworks.
        return HasValidApiKey(httpContext, configuredApiKey) ||
               IsLoopbackAddress(httpContext.Connection.RemoteIpAddress);
    }

    private static bool IsLoopbackAddress(IPAddress? address)
    {
        if (address == null)
        {
            return false;
        }

        return IPAddress.IsLoopback(address);
    }

    private static bool HasValidApiKey(HttpContext httpContext, string? configuredApiKey)
    {
        if (string.IsNullOrWhiteSpace(configuredApiKey))
        {
            return false;
        }

        var providedKey = httpContext.Request.Headers[ApiKeyHeaderName].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(providedKey))
        {
            return false;
        }

        var expectedBytes = Encoding.UTF8.GetBytes(configuredApiKey.Trim());
        var providedBytes = Encoding.UTF8.GetBytes(providedKey.Trim());

        return expectedBytes.Length == providedBytes.Length &&
               CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes);
    }
}
