namespace PointOfSale.Infrastructure.Shipping.Adapters;

internal static class CarrierHttpSafety
{
    // Includes label-bearing JSON. The deadline covers headers AND bounded content;
    // HttpClient.Timeout alone does not cover a ResponseHeadersRead body stream.
    internal static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpRequestMessage request, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        try
        {
            await response.Content.LoadIntoBufferAsync(4 * 1024 * 1024, deadline.Token);
            return response;
        }
        catch { response.Dispose(); throw; }
    }
}
