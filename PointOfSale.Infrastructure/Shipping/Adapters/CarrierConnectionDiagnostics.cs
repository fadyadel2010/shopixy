using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PointOfSale.Infrastructure.Shipping.Adapters;

internal static class CarrierConnectionDiagnostics
{
    internal static CarrierValidationResult Failure(string code) => new()
    {
        Success = false, ErrorCode = code,
        ErrorMessage = "The provider connection test failed. Check the diagnostic code and selected environment."
    };

    internal static CarrierValidationResult? HttpFailure(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized => Failure("INVALID_CREDENTIALS"),
        HttpStatusCode.Forbidden => Failure("PROVIDER_ACCESS_DENIED"),
        HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed => Failure("WRONG_ENVIRONMENT_OR_ENDPOINT"),
        HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout => Failure("CONNECTION_TIMEOUT"),
        _ when (int)status >= 400 => Failure($"PROVIDER_HTTP_{(int)status}"),
        _ => null
    };

    // Never return provider descriptions, tokens, account data or raw bodies.
    internal static string? Code(JsonElement root, string property, CarrierCredentials credentials)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(property, out var value)) return null;
        var code = value.ValueKind == JsonValueKind.String ? value.GetString() : value.ValueKind == JsonValueKind.Number ? value.GetRawText() : null;
        if (code == null || !Regex.IsMatch(code, "^[A-Za-z0-9_-]{1,40}$", RegexOptions.CultureInvariant)) return null;
        var secrets = new[] { credentials.ApiKey, credentials.ApiSecret }.Concat(credentials.ExtraConfig.Values);
        return secrets.Any(secret => !string.IsNullOrEmpty(secret) && code.Contains(secret, StringComparison.Ordinal)) ? null : code;
    }
}
