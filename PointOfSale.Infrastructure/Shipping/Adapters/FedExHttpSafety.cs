using System.Net;
using System.Text;

namespace PointOfSale.Infrastructure.Shipping.Adapters;

// FedEx-only limits. The linked request timeout also covers streamed response bodies.
internal static class FedExHttpSafety
{
    internal const int ResponseLimit = 8 * 1024 * 1024;
    internal const int OAuthResponseLimit = 64 * 1024;

    internal static async Task<string> ReadBodyAsync(HttpContent content, int limit, CancellationToken ct)
    {
        if (content.Headers.ContentLength > limit) throw new FedExResponseLimitException();
        await using var stream = await content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var bytes = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(bytes, ct)) != 0)
        {
            if (buffer.Length + count > limit) throw new FedExResponseLimitException();
            buffer.Write(bytes, 0, count);
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    internal static TimeSpan? RetryDelay(HttpResponseMessage response, int attempt)
    {
        // 429 can mean exhausted daily quota, not just the ten-second rate window.
        // Without an explicit Retry-After, return the failure rather than hammering FedEx.
        var retryAfter = response.Headers.RetryAfter;
        var delay = retryAfter?.Delta ?? (retryAfter?.Date - DateTimeOffset.UtcNow);
        if (delay.HasValue)
        {
            if (delay > TimeSpan.FromSeconds(30)) return null;
            return delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
        }
        return response.StatusCode == HttpStatusCode.TooManyRequests
            ? null : TimeSpan.FromMilliseconds(250 * attempt);
    }
}

internal sealed class FedExResponseLimitException : Exception;
