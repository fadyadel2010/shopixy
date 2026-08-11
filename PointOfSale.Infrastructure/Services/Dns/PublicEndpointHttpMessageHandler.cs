using System.Net;
using System.Net.Sockets;

namespace PointOfSale.Infrastructure.Services.Dns;

/// <summary>
/// Creates outbound HTTP connections only to publicly routable addresses.
/// DNS is resolved immediately before the socket is connected and the socket
/// is pinned to the validated address, preventing DNS-rebinding between an
/// application-layer preflight check and the actual connection.
/// </summary>
public static class PublicEndpointHttpMessageHandler
{
    public static SocketsHttpHandler Create()
    {
        return new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            ConnectCallback = ConnectPublicEndpointAsync,
        };
    }

    public static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address) ||
            address.Equals(IPAddress.Any) ||
            address.Equals(IPAddress.IPv6Any) ||
            address.Equals(IPAddress.None) ||
            address.Equals(IPAddress.IPv6None) ||
            address.IsIPv6LinkLocal ||
            address.IsIPv6SiteLocal ||
            address.IsIPv6Multicast)
        {
            return false;
        }

        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return bytes[0] switch
            {
                0 or 10 or 127 => false,
                100 when bytes[1] is >= 64 and <= 127 => false,
                169 when bytes[1] == 254 => false,
                172 when bytes[1] is >= 16 and <= 31 => false,
                192 when bytes[1] == 0 => false,
                192 when bytes[1] == 2 => false,
                192 when bytes[1] == 88 && bytes[2] == 99 => false,
                192 when bytes[1] == 168 => false,
                198 when bytes[1] is 18 or 19 => false,
                198 when bytes[1] == 51 && bytes[2] == 100 => false,
                203 when bytes[1] == 0 && bytes[2] == 113 => false,
                >= 224 => false,
                _ => true,
            };
        }

        // fc00::/7 (unique local), ::/128, and documentation/special-purpose
        // ranges must never be verification targets. Global unicast starts at
        // 2000::/3; restricting to it fails closed for non-public IPv6 space.
        var isDocumentationRange = bytes[0] == 0x20 &&
            bytes[1] == 0x01 &&
            bytes[2] == 0x0D &&
            bytes[3] == 0xB8;

        return address.AddressFamily == AddressFamily.InterNetworkV6 &&
               (bytes[0] & 0xE0) == 0x20 &&
               !isDocumentationRange;
    }

    private static async ValueTask<Stream> ConnectPublicEndpointAsync(
        SocketsHttpConnectionContext context,
        CancellationToken cancellationToken)
    {
        var addresses = await System.Net.Dns.GetHostAddressesAsync(
            context.DnsEndPoint.Host,
            cancellationToken);

        if (addresses.Length == 0 || addresses.Any(address => !IsPublicAddress(address)))
        {
            throw new HttpRequestException(
                "HTTP verification target resolved to a non-public network address.");
        }

        Exception? lastError = null;
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
            {
                NoDelay = true,
            };

            try
            {
                await socket.ConnectAsync(
                    new IPEndPoint(address, context.DnsEndPoint.Port),
                    cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (OperationCanceledException)
            {
                socket.Dispose();
                throw;
            }
            catch (Exception ex) when (ex is SocketException or IOException)
            {
                lastError = ex;
                socket.Dispose();
            }
        }

        throw new HttpRequestException(
            "HTTP verification target could not be reached through a public endpoint.",
            lastError);
    }
}
