using System.Text;

namespace PointOfSale.API.Services.AI;

internal static class AIProviderTransport
{
    internal static bool ValidConfiguration(string endpoint, string model, string key, int timeoutSeconds, int maxTokens, double temperature) =>
        !string.IsNullOrWhiteSpace(model) && !string.IsNullOrWhiteSpace(key) &&
        Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) && (uri.Scheme is "http" or "https") && string.IsNullOrEmpty(uri.UserInfo) &&
        timeoutSeconds is >= 1 and <= 120 && maxTokens is >= 1 and <= 32768 && double.IsFinite(temperature) && temperature is >= 0 and <= 1;

    internal static bool WithinPromptBudget(string system, string user) => system.Length + user.Length <= 65536;

    internal static async Task<string> ReadBoundedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        const int limit = 131072;
        if (content.Headers.ContentLength > limit) throw new InvalidDataException("Provider response exceeds limit");
        using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(buffer, cancellationToken)) != 0)
        {
            if (output.Length + count > limit) throw new InvalidDataException("Provider response exceeds limit");
            output.Write(buffer, 0, count);
        }
        return Encoding.UTF8.GetString(output.ToArray());
    }
}
