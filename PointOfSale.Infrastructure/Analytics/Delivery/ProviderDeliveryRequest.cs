using PointOfSale.Core.Analytics;

namespace PointOfSale.Infrastructure.Analytics.Delivery;

public sealed record ProviderDeliveryRequest(
    Guid TenantId,
    AnalyticsProvider Provider,
    string RequestType,
    string DeliveryKey,
    string PayloadJson,
    string? CorrelationId,
    DateTime OccurredAtUtc,
    string? EndpointUrl = null,
    string HttpMethod = "POST",
    IReadOnlyDictionary<string, string>? Headers = null,
    string ContentType = "application/json",
    int? TimeoutSeconds = null);
