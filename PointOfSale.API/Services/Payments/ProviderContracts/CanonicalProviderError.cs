namespace PointOfSale.API.Services.Payments.ProviderContracts;

public sealed record CanonicalProviderError(
    CanonicalProviderErrorType Type,
    string Code,
    string Message,
    string? ProviderRequestId,
    IReadOnlyDictionary<string, string> Metadata)
{
    public static CanonicalProviderError Validation(string provider, string code, string message)
    {
        return Create(provider, CanonicalProviderErrorType.Validation, code, message);
    }

    public static CanonicalProviderError Configuration(string provider, string code, string message)
    {
        return Create(provider, CanonicalProviderErrorType.Configuration, code, message);
    }

    public static CanonicalProviderError NotSupported(string provider, string code, string message)
    {
        return Create(provider, CanonicalProviderErrorType.NotSupported, code, message);
    }

    public static CanonicalProviderError Create(
        string provider,
        CanonicalProviderErrorType type,
        string code,
        string message,
        string? providerRequestId = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["provider"] = provider
        };

        if (metadata is not null)
        {
            foreach (var (key, value) in metadata.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                if (!string.IsNullOrWhiteSpace(key) && value is not null)
                {
                    values[key.Trim()] = value;
                }
            }
        }

        return new CanonicalProviderError(
            type,
            string.IsNullOrWhiteSpace(code) ? "PROVIDER_ERROR" : code.Trim(),
            string.IsNullOrWhiteSpace(message) ? "Provider error." : message.Trim(),
            providerRequestId,
            values);
    }
}

public enum CanonicalProviderErrorType
{
    Unknown = 0,
    Configuration = 1,
    Validation = 2,
    Authentication = 3,
    Authorization = 4,
    RateLimited = 5,
    Network = 6,
    ProviderUnavailable = 7,
    ProviderRejected = 8,
    AlreadyCaptured = 9,
    AlreadyRefunded = 10,
    InvalidState = 11,
    NotSupported = 12
}

public enum PaymentFailureReason
{
    Unknown = 0,
    ProviderConfiguration = 1,
    InvalidRequest = 2,
    AuthenticationFailed = 3,
    AuthorizationFailed = 4,
    RateLimited = 5,
    NetworkFailure = 6,
    ProviderUnavailable = 7,
    ProviderDeclined = 8,
    AlreadyCaptured = 9,
    AlreadyRefunded = 10,
    InvalidState = 11
}
