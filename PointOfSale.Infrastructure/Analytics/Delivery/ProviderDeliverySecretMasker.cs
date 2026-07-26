using System.Text.RegularExpressions;

namespace PointOfSale.Infrastructure.Analytics.Delivery;

public static partial class ProviderDeliverySecretMasker
{
    public static string? Mask(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var masked = AccessTokenRegex().Replace(value, "$1***");
        masked = ApiSecretRegex().Replace(masked, "$1***");
        masked = BearerRegex().Replace(masked, "$1***");
        return masked;
    }

    [GeneratedRegex("(access_token=)[^&\\s]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AccessTokenRegex();

    [GeneratedRegex("(api_secret=)[^&\\s]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ApiSecretRegex();

    [GeneratedRegex("(bearer\\s+)[a-z0-9._\\-]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BearerRegex();
}
