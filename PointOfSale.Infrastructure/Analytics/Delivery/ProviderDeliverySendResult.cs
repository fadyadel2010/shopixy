namespace PointOfSale.Infrastructure.Analytics.Delivery;

public sealed record ProviderDeliverySendResult(
    bool Success,
    int? StatusCode,
    string? ResponseBody,
    string? ErrorMessage,
    TimeSpan Latency,
    TimeSpan? RetryAfter)
{
    public static ProviderDeliverySendResult Ok(int? statusCode, string? responseBody, TimeSpan latency) =>
        new(true, statusCode, responseBody, null, latency, null);

    public static ProviderDeliverySendResult Fail(
        int? statusCode,
        string? responseBody,
        string errorMessage,
        TimeSpan latency,
        TimeSpan? retryAfter = null) =>
        new(false, statusCode, responseBody, errorMessage, latency, retryAfter);
}
